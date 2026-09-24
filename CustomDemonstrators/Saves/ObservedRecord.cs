using System;
using System.Collections.Generic;
using System.Linq;
using CustomDemonstrators.Slots;
using CustomDemonstrators.World;
using DV.LocoRestoration;

namespace CustomDemonstrators.Saves;

internal static class ObservedRecord
{
    internal static void OnSave()
    {
        try
        {
            if (Observe() is { } observed) Write(observed);
        }
        catch (Exception ex)
        {
            // Must never cost the player their save
            Main.Logger.LogException("Couldn't record the demonstrators' state:", ex);
        }
    }

    internal static void OnLoadData(LocoRestorationController controller)
    {
        try
        {
            if (!AStartGameData.carsAndJobsLoadingFinished) return;
            if (controller == null || DemonstratorSetup.SlotId(controller) == null) return;
            if (Observe() is not { } observed) return;

            var moved = Write(observed);
            if (moved.Count == 0) return;

            Main.Logger.Log($"Restoration(s) {string.Join(", ", moved)} were loaded holding stock this save's "
                + "record didn't give them, so the record now follows them.");
            RecordReload.Run(moved);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"Couldn't take on restoration {GameTypes.Id(controller.locoLivery)} as it "
                + "was loaded:", ex);
        }
    }

    private static Dictionary<string, (string SpawnId, string? TenderId)>? Observe()
    {
        var stored = SaveConfig.Demonstrators;
        var live = LiveSlots();
        var entries = new Dictionary<string, (string SpawnId, string? TenderId)>(StringComparer.Ordinal);
        bool untouched = stored == null;

        foreach (var (garage, primary) in VanillaGarages.Demonstrators)
        {
            var original = (primary.id, GameTypes.Id(VanillaGarages.OriginalTender(garage)));
            var entry = Seen(primary.id, live, stored) ?? original;
            entries[primary.id] = entry;
            untouched &= entry == original;
        }

        var added = live.Keys.Where(id => live[id].IsAdded)
            .Concat(stored?.Keys.Where(id => !entries.ContainsKey(id)) ?? [])
            .Distinct();

        foreach (var slotId in added)
        {
            // A slot of its own is named after its loco, so only its tender is free to move. A controller
            // pointed at some other loco can't be written down, and keeps what the record already said.
            var seen = Seen(slotId, live, stored);
            entries[slotId] = seen is { } s && s.SpawnId == slotId ? s
                : stored != null && stored.TryGetValue(slotId, out var kept) ? kept
                : (slotId, seen?.TenderId);
            untouched = false;
        }

        return untouched ? null : entries;
    }

    private static (string SpawnId, string? TenderId)? Seen(string slotId,
        Dictionary<string, (LocoRestorationController Controller, bool IsAdded)> live,
        Dictionary<string, (string SpawnId, string? TenderId)>? stored)
    {
        if (stored != null && stored.TryGetValue(slotId, out var kept) && !Installed(kept)) return kept;
        if (!live.TryGetValue(slotId, out var slot)) return stored?.TryGetValue(slotId, out kept) == true ? kept : null;

        return (slot.Controller.locoLivery.id, GameTypes.Id(slot.Controller.secondCarLivery));
    }

    private static bool Installed((string SpawnId, string? TenderId) entry) =>
        GameTypes.Livery(entry.SpawnId) != null && (entry.TenderId == null || GameTypes.Livery(entry.TenderId) != null);

    private static Dictionary<string, (LocoRestorationController Controller, bool IsAdded)> LiveSlots()
    {
        var live = new Dictionary<string, (LocoRestorationController, bool)>(StringComparer.Ordinal);
        foreach (var controller in LocoRestorationController.allLocoRestorationControllers)
        {
            if (controller == null || controller.locoLivery == null) continue;
            if (DemonstratorSetup.SlotId(controller) is not { } slotId || live.ContainsKey(slotId)) continue;
            live[slotId] = (controller, SlotTypes.IsSlotGarage(GameTypes.GarageOf(controller)));
        }
        return live;
    }

    private static HashSet<string> Write(Dictionary<string, (string SpawnId, string? TenderId)> observed)
    {
        var data = SaveState.Data();
        if (data == null) return [];

        var stored = SaveConfig.Demonstrators ?? [];
        var moved = new HashSet<string>(observed
            .Where(entry => !stored.TryGetValue(entry.Key, out var was) || was != entry.Value)
            .Select(entry => entry.Key), StringComparer.Ordinal);
        moved.UnionWith(stored.Keys.Where(id => !observed.ContainsKey(id)));

        if (!SaveConfig.WriteDemonstrators(data, observed) || moved.Count == 0) return moved;

        SaveGuard.FingerprintReplaced();
        Main.Logger.Log($"Recorded this save's demonstrators as they stand: {SaveGuard.StoredDemonstratorFingerprint}");
        return moved;
    }
}
