using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;
using UnityEngine;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

// The render of a train car that sits inside a locked garage
internal static class GaragePreviews
{
    private static readonly Dictionary<GarageType_v2, Preview> _previews = [];

    private sealed class Preview(GameObject cover, GameObject original)
    {
        internal readonly GameObject Cover = cover;

        internal readonly GameObject Original = original;

        internal GameObject? Standin;
    }

    internal static void Reset() => _previews.Clear();

    internal static void Replace(GaragePadlockUnlocker unlocker)
    {
        try
        {
            var cover = unlocker.lootCover;
            var spawner = unlocker.GetComponent<GarageCarSpawner>();
            var garage = spawner?.garageType;
            if (cover == null || spawner == null || garage == null) return;

            if (GarageUnlocks.Manager()?.IsGarageUnlocked(garage) == true) return;

            var newCover = new GameObject($"CustomDemonstrators_{garage.id}_Preview");
            newCover.transform.SetParent(cover.transform.parent, worldPositionStays: false);
            cover.transform.SetParent(newCover.transform, worldPositionStays: true);
            unlocker.lootCover = newCover;

            var preview = new Preview(newCover, cover);
            _previews[garage] = preview;
            unlocker.StartCoroutine(ApplyWhenLoaded(preview, spawner, unlocker));
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to rebuild a locked garage's preview model:", ex);
        }
    }

    private static IEnumerator ApplyWhenLoaded(
        Preview preview, GarageCarSpawner spawner, GaragePadlockUnlocker unlocker)
    {
        while (!AStartGameData.carsAndJobsLoadingFinished) yield return null;

        // Make sure colliders are in place after a tick
        yield return WaitFor.FixedUpdate;

        if (preview.Cover == null || spawner == null || unlocker == null) yield break;

        try
        {
            Apply(preview, spawner, unlocker);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to build a locked garage's preview model:", ex);
        }
    }

    internal static void Refresh(GarageCarSpawner? spawner)
    {
        try
        {
            var garage = spawner?.garageType;
            if (garage == null || !_previews.TryGetValue(garage, out var preview)) return;

            if (preview.Cover == null)
            {
                _previews.Remove(garage);
                return;
            }

            var unlocker = spawner!.GetComponent<GaragePadlockUnlocker>();
            if (unlocker == null) return;

            Apply(preview, spawner, unlocker);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException("Failed to refresh a locked garage's preview model:", ex);
        }
    }

    private static void Apply(Preview preview, GarageCarSpawner spawner, GaragePadlockUnlocker unlocker)
    {
        var garage = spawner.garageType;

        if (preview.Standin != null) UnityEngine.Object.DestroyImmediate(preview.Standin);
        preview.Standin = null;

        var liveries = garage.garageCarLiveries;
        bool stock = liveries == null || liveries.SequenceEqual(VanillaGarages.OriginalLiveries(garage));

        // Bob's garage has a vanilla bug that shows a handcar prefab before it's opened. Since we're already
        // here we can fix that and build a BE2 prefab instead.
        if (stock && garage.v1 != Garage.Bob)
        {
            preview.Original?.SetActive(true);
            return;
        }

        var standin = liveries != null ? GarageStandIn.Build(liveries, spawner, unlocker, garage) : null;

        if (standin == null)
        {
            preview.Original?.SetActive(stock);
            Main.Logger.Warning($"Couldn't build a preview model for {garage.id}; "
                + (stock ? "leaving the game's own in place." : "leaving the garage empty instead."));
            return;
        }

        preview.Original?.SetActive(false);

        standin.transform.SetParent(preview.Cover.transform, worldPositionStays: true);
        preview.Standin = standin;
        Main.Logger.Log($"Rebuilt the locked preview in {garage.id} as "
            + string.Join(" + ", liveries!.Where(l => l != null).Select(l => l.id)));
    }
}
