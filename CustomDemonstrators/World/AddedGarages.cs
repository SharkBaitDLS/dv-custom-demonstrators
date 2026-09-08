using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Garages;
using DV.ThingTypes;
using UnityEngine;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

// Player-added work train garages.
// 
// Each one spawns the consist the player configured for it wherever the player placed it,
// and "opens" when the license its first car needs is bought.
internal static class AddedGarages
{
    // Everything a live garage owns, kept so one removed mid-game can be taken apart again.
    private sealed class Built
    {
        public GarageType_v2 Garage = null!;
        public GarageCarSpawner? Spawner;
        public GameObject? Host;
        public GameObject? Home;
    }

    // Keyed by the livery the garage spawns first, which is what identifies it in the settings.
    private static readonly Dictionary<string, Built> _garages = [];
    private static bool _built;

    // Scene objects die with the world, the ScriptableObjects we registered into Globals do not, so they are
    // torn out here and rebuilt on the next load rather than accumulating across sessions.
    internal static void Reset()
    {
        _built = false;

        var types = Globals.G?.Types;
        if (types != null && _garages.Count > 0)
        {
            foreach (var garage in _garages.Values) types.garages.Remove(garage.Garage);
            types.RecalculateCaches();
        }

        _garages.Clear();
    }

    internal static void BuildAll()
    {
        if (_built) return;
        _built = true;

        var types = Globals.G?.Types;
        if (types == null) return;

        var wanted = Desired().ToList();
        if (wanted.Count == 0) return;

        var template = Template();
        if (template == null)
        {
            Main.Logger.Warning("No garage to use as a template; the additional garages were skipped.");
            return;
        }

        Main.Logger.Log($"Building {wanted.Count} additional garage(s) from template {template.garageType.id}.");

        bool added = false;
        foreach (var (primaryId, extras) in wanted)
        {
            if (Build(primaryId, extras, template)) added = true;
        }

        if (added) types.RecalculateCaches();
    }

    // Brings the live world in line with the current settings for the force respawn button
    internal static void Reconcile()
    {
        var types = Globals.G?.Types;
        if (types == null) return;

        var wanted = Desired().ToDictionary(w => w.PrimaryId, w => w.Extras);
        bool changed = false;

        // Removals first, so a car moving from one garage to another is never claimed twice
        foreach (var primaryId in _garages.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
        {
            if (Remove(primaryId)) changed = true;
        }

        foreach (var kv in wanted.Where(w => _garages.ContainsKey(w.Key)).ToList())
        {
            if (Update(kv.Key, kv.Value)) changed = true;
        }

        var newcomers = wanted.Where(w => !_garages.ContainsKey(w.Key)).ToList();
        if (newcomers.Count > 0)
        {
            var template = Template();
            if (template == null)
            {
                Main.Logger.Warning("No garage to use as a template; the new garages were skipped.");
            }
            else
            {
                foreach (var kv in newcomers)
                {
                    if (Build(kv.Key, kv.Value, template)) changed = true;
                }
            }
        }

        if (changed) types.RecalculateCaches();
    }

    private static bool Build(string primaryId, IReadOnlyList<string> extraIds, GarageCarSpawner template)
    {
        var types = Globals.G?.Types;
        if (types == null) return false;

        var garageId = SlotTypes.WorkGarageId(primaryId);

        var liveries = Consist(primaryId, extraIds, null);
        if (liveries == null) return false;

        var anchor = GarageHomes.Anchor();
        if (anchor == null)
        {
            Main.Logger.Warning($"Additional garage '{primaryId}' was skipped: the world has no origin shift "
                + "parent to hang it off yet.");
            return false;
        }

        if (GarageHomes.Placement(garageId, primaryId) is not (Vector3, float) placement)
        {
            Main.Logger.Warning($"Additional garage '{primaryId}' was skipped: it has nowhere to stand. "
                + "Set its placement in the mod menu, standing where you want it.");
            return false;
        }

        var garage = SlotTypes.GetOrCreateWorkGarage(primaryId, [.. liveries], template.garageType);
        ApplySummonPrice(garage);
        types.garages.Add(garage);
        SlotTypes.AllowSummoning(garage);

        var built = new Built { Garage = garage };
        _garages[primaryId] = built;

        // The spawner and its unlocker share one object, with the spawn point a child of it.
        // Held inactive so everything is configured before the Awakes that register it.
        var host = new GameObject($"CustomDemonstrators_{primaryId}_Garage");
        host.SetActive(false);
        host.transform.SetParent(anchor, worldPositionStays: false);
        built.Host = host;

        var home = new GameObject($"CustomDemonstrators_{primaryId}_Home");
        home.transform.SetParent(host.transform, worldPositionStays: false);
        GarageHomes.PlaceHome(home, placement);
        built.Home = home;

        DropStaleRegistrations(liveries);
        built.Spawner = SlotScene.CreateSpawner(host, garage, home, template);

        var license = liveries[0].requiredLicense;
        if (license != null)
        {
            host.AddComponent<GarageLicenseUnlocker>().license = license;
        }

        host.SetActive(true);

        // A spawner whose liveries were spoken for destroys itself in Awake rather than throwing, so the
        // garage has to be dismantled if that happens.
        if (built.Spawner == null)
        {
            Main.Logger.Warning($"Additional garage '{primaryId}' was skipped: the game rejected its spawner, "
                + "which means something else claimed one of its cars.");
            _garages.Remove(primaryId);
            Detach(built);
            return false;
        }

        WorkTrainGarages.AdoptStrayCars(built.Spawner);

        // Nothing to wait for, so it opens as soon as it exists.
        if (license == null)
        {
            GarageUnlocks.Unlock(garage);
            built.Spawner.AllowSpawning();
        }

        Main.Logger.Log($"Built an additional garage for {string.Join(" + ", liveries.Select(l => l.id))}"
            + (license != null ? $", which opens with the {license.id} license." : ", already open."));
        return true;
    }

    // A garage that is already standing but whose consist or placement has since been changed in the menu.
    private static bool Update(string primaryId, IReadOnlyList<string> extraIds)
    {
        if (!_garages.TryGetValue(primaryId, out var built)) return false;

        var garageId = SlotTypes.WorkGarageId(primaryId);

        // A garage whose placement was cleared has nowhere to be, which is the same as not having one.
        if (GarageHomes.Placement(garageId, primaryId) is not (Vector3 offset, float yaw) placement)
        {
            Main.Logger.Log($"The additional garage for {primaryId} no longer has a placement, "
                + "so it is being taken apart.");
            return Remove(primaryId);
        }

        ApplySummonPrice(built.Garage);

        bool changed = false;

        if (built.Home != null
            && (built.Home.transform.localPosition != offset
                || built.Home.transform.localRotation != Quaternion.Euler(0f, yaw, 0f)))
        {
            GarageHomes.PlaceHome(built.Home, placement);
            Main.Logger.Log($"Moved the additional garage for {primaryId}. Any car it already spawned stays "
                + "where it is until it is summoned or returned home.");
            changed = true;
        }

        var liveries = Consist(primaryId, extraIds, built.Garage);
        if (liveries != null && built.Spawner != null
            && !liveries.SequenceEqual(built.Garage.garageCarLiveries ?? []))
        {
            built.Garage.garageCarLiveries = [.. liveries];
            WorkTrainGarages.Reconcile(built.Spawner);
            changed = true;
        }

        return changed;
    }

    // What the comms radio charges to summon this garage's cars. Like the game's own garages the price is
    // read live rather than baked into the save, so it is simply reapplied whenever the garage is touched.
    private static void ApplySummonPrice(GarageType_v2 garage) =>
        GarageLiveries.ApplySummonPrice(garage, VanillaGarages.DefaultSummonPrice());

    private static bool Remove(string primaryId)
    {
        if (!_garages.TryGetValue(primaryId, out var built)) return false;
        _garages.Remove(primaryId);

        Main.Logger.Log($"Removing the additional garage for {primaryId}.");

        // Its cars stay in the world as the player's, they just have nowhere left to be summoned back to.
        GarageUnlocks.Revoke(built.Garage, "its garage no longer exists.");
        WorkTrainGarages.ReleaseCars(built.Spawner);

        // Unity defers the spawner's OnDestroy to the end of the frame, and that runs a blanket
        // Spawners.Remove over the garage's liveries. If those liveries moved to another garage, they'd
        // get nuked by that pass, so instead we preemptively take them out.
        if (built.Spawner != null)
        {
            foreach (var livery in GarageCarSpawner.Spawners
                .Where(kv => kv.Value == built.Spawner).Select(kv => kv.Key).ToList())
            {
                GarageCarSpawner.Spawners.Remove(livery);
            }
        }
        Detach(built);
        GarageHomes.Release(SlotTypes.WorkGarageId(primaryId));
        return true;
    }

    private static void Detach(Built built)
    {
        built.Garage.garageCarLiveries = [];

        if (built.Host != null) Object.Destroy(built.Host); // takes the spawner and home with it

        Globals.G?.Types?.garages.Remove(built.Garage);
        SlotTypes.RevokeSummoning(built.Garage);
    }

    // A livery whose garage no longer spawns it can still left stale pointed at that garage's spawner,
    // since the game only ever clears the registry wholesale when a spawner is destroyed.
    private static void DropStaleRegistrations(List<TrainCarLivery> liveries)
    {
        foreach (var livery in liveries)
        {
            if (!GarageCarSpawner.Spawners.TryGetValue(livery, out var owner)) continue;
            if (owner != null && owner.garageType?.garageCarLiveries?.Contains(livery) == true) continue;

            GarageCarSpawner.Spawners.Remove(livery);
        }
    }

    // The cars a garage should hold, or null if it can't be built as configured.
    private static List<TrainCarLivery>? Consist(
        string primaryId, IReadOnlyList<string> extraIds, GarageType_v2? own)
    {
        var primary = Livery(primaryId);
        if (primary == null)
        {
            Main.Logger.Warning($"Additional garage '{primaryId}' was skipped: no such livery is loaded.");
            return null;
        }

        var liveries = new List<TrainCarLivery> { primary };
        foreach (var extraId in extraIds)
        {
            var extra = Livery(extraId);
            if (extra == null)
            {
                Main.Logger.Warning($"Additional garage '{primaryId}' left out {extraId}: "
                    + "no such livery is loaded.");
                continue;
            }
            if (!liveries.Contains(extra)) liveries.Add(extra);
        }

        var garages = Globals.G?.Types?.garages;
        if (garages == null) return null;

        foreach (var claimed in liveries)
        {
            var owner = garages.FirstOrDefault(g =>
                g != null && g != own && g.garageCarLiveries?.Contains(claimed) == true);
            if (owner == null) continue;

            Main.Logger.Warning($"Additional garage '{primaryId}' was skipped: "
                + $"{claimed.id} is already spawned by garage {owner.id}.");
            return null;
        }

        return liveries;
    }

    private static IEnumerable<(string PrimaryId, IReadOnlyList<string> Extras)> Desired()
    {
        if (SaveGuard.AllowGarageChanges())
        {
            foreach (var garage in Main.Settings.AdditionalGarages)
            {
                if (string.IsNullOrEmpty(garage.PrimaryId)) continue;
                yield return (garage.PrimaryId,
                    Main.Settings.GetExtraCars(SlotTypes.WorkGarageId(garage.PrimaryId)));
            }
            yield break;
        }

        var baked = SaveConfig.Garages;
        if (baked == null) yield break;

        foreach (var entry in baked)
        {
            if (!SlotTypes.IsWorkGarageId(entry.Key)) continue;
            var primaryId = entry.Value.SpawnIds.FirstOrDefault();
            if (string.IsNullOrEmpty(primaryId)) continue;
            yield return (primaryId, entry.Value.Extras);
        }
    }

    private static GarageCarSpawner? Template() =>
        GarageCarSpawner.Spawners.Values
            .Where(s => s != null && s.garageType != null && s.locoSpawnPoint != null
                && !SlotTypes.IsAdded(s.garageType) && !VanillaGarages.IsDemonstrator(s.garageType.v1))
            .OrderBy(s => (int)s.garageType.v1)
            .FirstOrDefault();

    private static TrainCarLivery? Livery(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);
}
