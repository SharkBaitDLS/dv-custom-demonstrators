using System.Collections.Generic;
using System.Linq;
using DV.LocoRestoration;
using DV.Localization;
using DV.Optimizers;
using DV.OriginShift;
using DV.ThingTypes;
using UnityEngine;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

// A custom quest board placed next to the home track for an added demonstrator
internal static class SlotBoard
{
    // A cloned board held out of the world, waiting for the controller that will attach to it. Nothing on it
    // has woken yet, so the caller can finish building the slot onto it. Install activates and places it.
    internal readonly struct Pending(
        GameObject board, GameObject holder, Transform source, Vector3 position, Quaternion rotation, bool inStall)
    {
        // The object to build the rest of the slot onto.
        internal readonly GameObject Board = board;

        private readonly GameObject Holder = holder;
        private readonly Transform Source = source;
        private readonly Vector3 Position = position;
        private readonly Quaternion Rotation = rotation;
        private readonly bool InStall = inStall;

        internal GameObject Install(LocoRestorationController controller, string locoId)
        {
            var view = Board.GetComponent<LocoRestorationView>();
            ApplyVisuals(view, controller.locoLivery, locoId);
            view.connectionMode = LocoRestorationView.ConnectionMode.Manual;
            view.controller = controller;

            Board.transform.localPosition = Position;
            Board.transform.localRotation = Rotation;
            Optimize(Board.transform, Source, InStall);

            // Parenting it out of the holder is what wakes the board, and the whole slot riding on it
            Board.transform.SetParent(Source.parent, worldPositionStays: false);

            Object.Destroy(Holder);
            return Board;
        }
    }

    // Clones the museum's own panel for a slot and holds it inactive. The museum keeps a restoration and its
    // panel on one object. The caller is responsible for building the slot onto the board and Installing it.
    internal static Pending? Prepare(string locoId, LocoRestorationController template, SlotScene.SlotHome home)
    {
        var source = Template(template);
        if (source == null)
        {
            Main.Logger.Warning($"Additional demonstrator '{locoId}' found no restoration panel to copy; "
                + "its progress will not be shown in the museum.");
            return null;
        }

        if (Pose(locoId, source.transform, home, template) is not (Vector3 position, Quaternion rotation))
            return null;

        // Build it in a disabled holder so that we have time to strip the quest associations off the clone,
        // hang the slot on it and parent it into the museum's culling logic before any of it activates.
        var holder = new GameObject($"CustomDemonstrators_{locoId}_BoardBuilder");
        holder.SetActive(false);

        var board = Object.Instantiate(source.gameObject, holder.transform, worldPositionStays: false);
        board.name = $"CustomDemonstrators_{locoId}_Board";
        Strip(board, template);

        return new Pending(
            board, holder, source.transform, position, rotation, !string.IsNullOrEmpty(home.Stall));
    }

    internal static void Destroy(GameObject? board)
    {
        if (board == null) return;

        foreach (var optimizer in Optimizers(board.transform))
        {
            optimizer.gameObjectsToDisable.RemoveAll(go => go == null || go.transform.IsChildOf(board.transform));
        }

        SlotPoster.Discard(board);
        Object.Destroy(board);
    }

    internal static void ApplyVisuals(LocoRestorationView? view, TrainCarLivery? livery, string? posterId)
    {
        if (view == null) return;

        FitName(view);
        Rename(view, livery);

        if (string.IsNullOrEmpty(posterId)) SlotPoster.Restore(view.gameObject);
        else SlotPoster.Apply(view.gameObject, posterId!);
    }

    // The fixed font size for vanilla demonstrators is too large for many modded locos' names
    private static void FitName(LocoRestorationView? view)
    {
        var plate = view?.locoNameLabel;
        if (plate == null || plate.enableAutoSizing) return;

        plate.fontSizeMax = plate.fontSize;
        plate.fontSizeMin = 1f;
        plate.enableAutoSizing = true;
        plate.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
    }

#if DEBUG
    internal static string Describe(GameObject? board)
    {
        if (board == null) return "none";

        var optimizer = board.GetComponent<PlayerDistanceMultipleGameObjectsOptimizer>()
            ?? board.transform.parent?.GetComponent<PlayerDistanceMultipleGameObjectsOptimizer>();
        if (optimizer == null) return "lit always, no optimizer";

        var panels = optimizer.gameObjectsToDisable
            .Where(go => go != null && go.transform.IsChildOf(board.transform))
            .ToList();

        var camera = PlayerManager.ActiveCamera;
        var against = optimizer.gameObjectToCheckDistance;
        var away = camera != null && against != null
            ? Vector3.Distance(camera.transform.position, against.transform.position)
            : float.NaN;

        return $"{panels.Count(go => go.activeInHierarchy)}/{panels.Count} lit, "
            + $"{(optimizer.transform.IsChildOf(board.transform) ? "own" : "museum")} optimizer, "
            + $"{away:N0} m out of {Mathf.Sqrt(optimizer.disableSqrDistance):N0} m";
    }
#endif

    private static void Rename(LocoRestorationView? view, TrainCarLivery? livery)
    {
        var plate = view?.locoNameLabel;
        if (plate == null || livery == null) return;

        var key = string.IsNullOrEmpty(livery.localizationKey) ? livery.id : livery.localizationKey;
        if (string.IsNullOrEmpty(key)) return;

        var localize = plate.GetComponent<Localize>();
        if (localize != null)
        {
            localize.key = key;
            localize.UpdateLocalization();
        }
        else
        {
            plate.text = LocalizationAPI.L(key);
        }
    }

    private static LocoRestorationView? Template(LocoRestorationController template) =>
        Resources.FindObjectsOfTypeAll<LocoRestorationView>()
            .FirstOrDefault(v => v != null
                && v.gameObject.scene.IsValid()
                && v.connectionMode == LocoRestorationView.ConnectionMode.Manual
                && v.controller == template
                && v.transform.parent != null);

    private static (Vector3 Position, Quaternion Rotation)? Pose(
        string locoId, Transform source, SlotScene.SlotHome home, LocoRestorationController template)
    {
        var arc = source.parent;

        // It's more exact to place the posters based on the arc of the musem than to compute it
        // based on the stall's bezier curve.
        if (!string.IsNullOrEmpty(home.Stall))
        {
            var from = Bearing(arc, MuseumStalls.Midpoint(template.destinationTrackId));
            var to = Bearing(arc, MuseumStalls.Midpoint(home.Stall));
            if (from == null || to == null)
            {
                Main.Logger.Warning($"Additional demonstrator '{locoId}' has no restoration panel: stall "
                    + $"'{template.destinationTrackId}' or '{home.Stall}' is missing from this world.");
                return null;
            }

            var turn = Quaternion.Euler(0f, to.Value - from.Value, 0f);
            return (turn * source.localPosition, turn * source.localRotation);
        }

        if (!home.Placed || home.Marker == null)
        {
            Main.Logger.Log($"Additional demonstrator '{locoId}' shares the template demonstrator's stall, "
                + "so it shares its restoration panel too.");
            return null;
        }

        // A hand-placed slot has to eat the more fiddly process of computing itself relative to the track
        var anchor = template.garageSpawner.locoSpawnPoint.transform;
        if (Trackside(anchor.position) is not (Vector3 museumRail, Vector3 museumAlong)
            || Trackside(home.Marker.transform.position) is not (Vector3 rail, Vector3 along))
        {
            Main.Logger.Warning($"Additional demonstrator '{locoId}' has no restoration panel: no track near "
                + "its garage position to stand one beside.");
            return null;
        }

        // Make sure it's placed and angled so that the arrow actually points at the assigned track
        var beside = source.position - museumRail;
        var clearance = Mathf.Abs(Vector3.Dot(beside, Vector3.Cross(Vector3.up, museumAlong)));
        var placed = rail + Vector3.Cross(Vector3.up, along) * clearance + Vector3.up * beside.y;

        // Do a best effort to drop the poster onto the ground
        placed.y += GroundDrop(rail, placed);

        return (arc.InverseTransformPoint(placed),
            Quaternion.Inverse(arc.rotation) * Quaternion.LookRotation(-along, Vector3.up));
    }

    // Try to drop to the ground, within reason (so we don't drop off a bridge if someone is lunatic
    // enough to put a spawn on one)
    private static float GroundDrop(Vector3 rail, Vector3 placed)
    {
        if (Ground(placed) is not float atPanel || Ground(rail) is not float atRail) return 0f;

        return Mathf.Clamp(atPanel - atRail, -3f, 3f);
    }

    private static float? Ground(Vector3 position)
    {
        foreach (var terrain in Terrain.activeTerrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;

            var origin = terrain.transform.position;
            var size = terrain.terrainData.size;
            if (position.x < origin.x || position.x > origin.x + size.x) continue;
            if (position.z < origin.z || position.z > origin.z + size.z) continue;

            return origin.y + terrain.SampleHeight(position);
        }
        return null;
    }

    private static (Vector3 Rail, Vector3 Along)? Trackside(Vector3 position)
    {
        var (track, point) = RailTrack.GetClosest(position);
        if (track == null || point == null) return null;

        var along = Vector3.ProjectOnPlane(point.Value.forward, Vector3.up);
        if (along.sqrMagnitude < 1e-6f) return null;

        // Track points are held unshifted, the query position isn't
        return ((Vector3)point.Value.position + OriginShift.currentMove, along.normalized);
    }

    // Where a point sits around the arc the panels are arranged on, in degrees.
    private static float? Bearing(Transform arc, Vector3? world)
    {
        if (world == null) return null;
        var local = arc.InverseTransformPoint(world.Value);
        return Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
    }

    private static void Strip(GameObject board, LocoRestorationController template)
    {
        foreach (var controller in board.GetComponentsInChildren<LocoRestorationController>(includeInactive: true))
            Object.DestroyImmediate(controller);
        foreach (var spawner in board.GetComponentsInChildren<GarageCarSpawner>(includeInactive: true))
            Object.DestroyImmediate(spawner);
        foreach (var module in board.GetComponentsInChildren<CashRegisterModule>(includeInactive: true))
            Object.DestroyImmediate(module);

        // Make sure we don't nuke the vanilla spawner we cloned from
        foreach (var livery in template.garageSpawner.GarageCarLiveries)
            GarageCarSpawner.Spawners[livery] = template.garageSpawner;
    }

    // Make sure our panels are properly culled like the default ones
    private static void Optimize(Transform board, Transform source, bool inStall)
    {
        var museum = Optimizer(source);
        if (museum == null) return;

        var copies = new List<GameObject>();
        foreach (var listed in museum.gameObjectsToDisable.ToList())
        {
            if (listed == null || listed.transform == source || !listed.transform.IsChildOf(source)) continue;

            var copy = board.Find(PathUnder(listed.transform, source));
            if (copy != null) copies.Add(copy.gameObject);
        }
        if (copies.Count == 0) return;

        // A panel in a stall just shares the rest of the museum's logic
        if (inStall)
        {
            museum.gameObjectsToDisable.AddRange(copies);
            return;
        }

        // Activate them since we can't guarantee the museum copies were loaded and if the player
        // *is* nearby we don't want it to be culled. If the player isn't nearby, they'll
        // deactivate within a few seconds either when the optimizer runs or the player teleports.
        foreach (var copy in copies) copy.SetActive(true);

        // Hand-placed panels can't piggyback on the museum culling
        var own = board.gameObject.AddComponent<PlayerDistanceMultipleGameObjectsOptimizer>();
        own.gameObjectToCheckDistance = board.gameObject;
        own.gameObjectsToDisable = copies;
        own.scriptsToDisable = [];
        own.disableSqrDistance = museum.disableSqrDistance;
        own.checkPeriod = museum.checkPeriod;
    }

    private static PlayerDistanceMultipleGameObjectsOptimizer? Optimizer(Transform root) =>
        Optimizers(root).FirstOrDefault();

    private static IEnumerable<PlayerDistanceMultipleGameObjectsOptimizer> Optimizers(Transform root) =>
        Resources.FindObjectsOfTypeAll<PlayerDistanceMultipleGameObjectsOptimizer>()
            .Where(o => o != null
                && o.gameObject.scene.IsValid()
                && o.gameObjectsToDisable != null
                && o.gameObjectsToDisable.Any(go => go != null && go.transform.IsChildOf(root)));

    private static string PathUnder(Transform child, Transform root)
    {
        var path = child.name;
        for (var parent = child.parent; parent != null && parent != root; parent = parent.parent)
        {
            path = parent.name + "/" + path;
        }
        return path;
    }
}
