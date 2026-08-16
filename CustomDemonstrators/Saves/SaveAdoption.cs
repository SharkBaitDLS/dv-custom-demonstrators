using System;
using System.Collections.Generic;
using System.Linq;
using DV.ThingTypes;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.Saves;

// Writes the configuration a save was baked with back into the mod's settings, which is what makes an
// out of sync save playable without respawning anything. Everything here works off the ids parsed out of
// the save's fingerprint rather than off live liveries, so a save baked with stock that is no longer
// installed still round-trips to the same fingerprint instead of being silently reset.
internal static class SaveAdoption
{
    internal static void ApplyDemonstrators()
    {
        var baked = SaveConfig.Demonstrators;
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (garage, isDemonstrator, liveries) in VanillaGarages.Groups)
        {
            if (!isDemonstrator) continue;
            var primary = liveries.FirstOrDefault();
            if (primary == null) continue;

            // A slot the save doesn't mention was never customized in it, so it goes back to its original.
            var (SpawnId, TenderId) = Lookup(baked, primary.id) ?? (primary.id, null);
            SlotChoices.SetSpawn(primary.id, SpawnId);
            AdoptTender(primary.id, VanillaGarages.OriginalTender(garage), TenderId);

            claimed.Add(SpawnId);
            if (TenderId != null) claimed.Add(TenderId);
        }

        AdoptAdditionalSlots(baked, claimed);
        ReleaseGarageClaims(claimed);
    }

    internal static void ApplyGarages()
    {
        var baked = SaveConfig.Garages;
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (garage, isDemonstrator, liveries) in VanillaGarages.Groups)
        {
            if (isDemonstrator) continue;
            var (SpawnIds, Extras) = baked != null && baked.TryGetValue(garage.id, out var e) ? e : default;

            // The fingerprint lists a garage's stock in its own livery order, so position is the only thing
            // tying a baked id back to the slot it replaced.
            for (int i = 0; i < liveries.Count; i++)
            {
                var spawnId = SpawnIds != null && i < SpawnIds.Count
                    ? SpawnIds[i]
                    : liveries[i].id;
                SlotChoices.SetSpawn(liveries[i].id, spawnId);
                claimed.Add(spawnId);
            }

            Main.Settings.ClearExtraCars(garage.id);
            foreach (var extra in Extras ?? [])
            {
                Main.Settings.AddExtraCar(garage.id, extra);
                claimed.Add(extra);
            }
        }

        ReleaseDemonstratorClaims(claimed);

        // The utility flatcar is one of these garages, so the save's may not carry the parts cargos the
        // settings had picked out for the old one.
        SlotChoices.PruneInvalidCargoOverrides();
    }

    private static (string SpawnId, string? TenderId)? Lookup(
        Dictionary<string, (string SpawnId, string? TenderId)>? baked, string slotId) =>
        baked != null && baked.TryGetValue(slotId, out var entry) ? entry : null;

    // ResolveTender falls back to the garage's own tender while the loco is unreplaced, so only write an
    // override where that fallback wouldn't already land on the baked tender. That keeps an adopted save
    // from picking up overrides it never had, which would show up as a fingerprint mismatch.
    private static void AdoptTender(string slotId, TrainCarLivery? originalTender, string? tenderId)
    {
        Main.Settings.SetTenderId(slotId, null);
        if (SlotChoices.ResolveTender(slotId, originalTender)?.id != tenderId)
            Main.Settings.SetTenderId(slotId, tenderId);
    }

    // The slots this mod adds are the baked entries that aren't one of the game's own demonstrators.
    private static void AdoptAdditionalSlots(
        Dictionary<string, (string SpawnId, string? TenderId)>? baked, HashSet<string> claimed)
    {
        var vanilla = VanillaGarages.VanillaDemonstratorIds();
        var wanted = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (baked != null)
        {
            foreach (var kv in baked)
                if (!vanilla.Contains(kv.Key)) wanted[kv.Key] = kv.Value.TenderId;
        }

        foreach (var slot in Main.Settings.AdditionalSlots.ToList())
        {
            if (!wanted.ContainsKey(slot.LocoId)) Main.Settings.RemoveAdditionalSlot(slot.LocoId);
        }

        foreach (var kv in wanted)
        {
            // Adding a slot that's already configured is a no-op, which is what keeps a hand-placed home
            // from being thrown away when its slot is adopted back unchanged.
            Main.Settings.AddAdditionalSlot(kv.Key);
            Main.Settings.SetTenderId(kv.Key, kv.Value);

            claimed.Add(kv.Key);
            if (kv.Value != null) claimed.Add(kv.Value);
        }
    }

    // Adopting only one half of a save can hand the demonstrators a livery the garage settings still spawn,
    // and the game allows a livery in exactly one garage. Whichever side wasn't adopted gives the livery up.
    private static void ReleaseGarageClaims(HashSet<string> claimed)
    {
        foreach (var (garage, isDemonstrator, liveries) in VanillaGarages.Groups)
        {
            if (isDemonstrator) continue;

            foreach (var livery in liveries)
            {
                if (!claimed.Contains(SlotChoices.CurrentSpawnId(livery)) || claimed.Contains(livery.id)) continue;
                Main.Logger.Warning($"Garage {garage.id} gave up {SlotChoices.CurrentSpawnId(livery)}: "
                    + $"this save's demonstrators spawn it. {livery.id} is back to the game's default.");
                SlotChoices.SetSpawn(livery.id, livery.id);
            }

            foreach (var extra in Main.Settings.GetExtraCars(garage.id).ToList())
            {
                if (!claimed.Contains(extra)) continue;
                Main.Logger.Warning($"Garage {garage.id} gave up its extra car {extra}: "
                    + "this save's demonstrators spawn it.");
                Main.Settings.RemoveExtraCar(garage.id, extra);
            }
        }
    }

    private static void ReleaseDemonstratorClaims(HashSet<string> claimed)
    {
        foreach (var (garage, isDemonstrator, liveries) in VanillaGarages.Groups)
        {
            if (!isDemonstrator) continue;
            var primary = liveries.FirstOrDefault();
            if (primary == null) continue;

            if (claimed.Contains(SlotChoices.CurrentSpawnId(primary)) && !claimed.Contains(primary.id))
            {
                Main.Logger.Warning($"Demonstrator {primary.id} gave up {SlotChoices.CurrentSpawnId(primary)}: "
                    + "this save's garages spawn it. It is back to the game's default.");
                SlotChoices.SetSpawn(primary.id, primary.id);
            }

            var tender = SlotChoices.ResolveTender(primary.id, VanillaGarages.OriginalTender(garage));
            if (tender != null && claimed.Contains(tender.id))
                Main.Settings.SetTenderId(primary.id, null);
        }

        foreach (var slot in Main.Settings.AdditionalSlots.ToList())
        {
            var tenderId = Main.Settings.GetTenderId(slot.LocoId);
            if (tenderId != null && claimed.Contains(tenderId))
                Main.Settings.SetTenderId(slot.LocoId, null);

            if (!claimed.Contains(slot.LocoId)) continue;
            Main.Logger.Warning($"Additional demonstrator {slot.LocoId} was dropped: "
                + "this save's garages spawn that locomotive.");
            Main.Settings.RemoveAdditionalSlot(slot.LocoId);
        }
    }
}
