using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DV.LocoRestoration;
using CustomDemonstrators.Slots;
using CustomDemonstrators.World;

namespace CustomDemonstrators.Saves;

// Guards settings from being applied so that the mod is safe to enable on an existing save
internal static class SaveGuard
{
    private const string DemonstratorFingerprintKey = "CustomDemonstrators_DemonstratorFingerprint";
    private const string GarageFingerprintKey = "CustomDemonstrators_GarageFingerprint";

    private static bool? _allowDemo;
    private static bool _forcedDemo;
    private static bool? _allowGarage;
    private static bool _forcedGarage;

    internal static bool AllowDemonstratorChanges()
    {
        if (_allowDemo.HasValue) return _allowDemo.Value;
        bool result = Decide(_forcedDemo, DemonstratorFingerprintKey, DemonstratorFingerprint, out bool undecided);
        if (!undecided) _allowDemo = result; // leave uncached while the save isn't readable yet
        return result;
    }

    // Whether ordinary garages may be (re)baked, same rules as demonstrators but on their own fingerprint.
    internal static bool AllowGarageChanges()
    {
        if (_allowGarage.HasValue) return _allowGarage.Value;
        bool result = Decide(_forcedGarage, GarageFingerprintKey, GarageFingerprint, out bool undecided);
        if (!undecided) _allowGarage = result;
        return result;
    }

    private static bool Decide(bool forced, string key, Func<string> fingerprint, out bool undecided)
    {
        undecided = false;
        if (forced) return WriteFingerprint(key, fingerprint);

        var data = SaveState.Data();
        if (data == null) { undecided = true; return true; }
        if (SaveState.IsNewSession) return WriteFingerprint(key, fingerprint);

        return data.GetString(key) == fingerprint() && WriteFingerprint(key, fingerprint);
    }

    private static bool WriteFingerprint(string key, Func<string> fingerprint)
    {
        SaveState.Data()?.SetString(key, fingerprint());
        return true;
    }

    internal static bool IsGarageBlocking => SaveState.Data() != null && !AllowGarageChanges();

    // The fingerprint the save was last saved with or null if the mod never touched this save
    internal static string? StoredDemonstratorFingerprint => ReadStored(DemonstratorFingerprintKey);

    private static string? ReadStored(string key)
    {
        var s = SaveState.Data()?.GetString(key);
        return string.IsNullOrEmpty(s) ? null : s;
    }

    internal static bool IsDemonstratorOutOfSync => OutOfSync(DemonstratorFingerprintKey, DemonstratorFingerprint);
    internal static bool IsGarageOutOfSync => OutOfSync(GarageFingerprintKey, GarageFingerprint);

    private static bool OutOfSync(string key, Func<string> fingerprint)
    {
        var data = SaveState.Data();
        if (data == null || SaveState.IsNewSession) return false;
        return data.GetString(key) != fingerprint();
    }

    internal static void Invalidate()
    {
        _allowDemo = null;
        _forcedDemo = false;
        _allowGarage = null;
        _forcedGarage = false;
    }

    internal static void ForceApplyDemonstrators()
    {
        _forcedDemo = true;
        _allowDemo = null;
        AllowDemonstratorChanges();
        GarageReplacementApplier.Apply();
        // Add and remove the mod's own slots before respawning, so the loop below sees the final set and a
        // slot that was just taken apart isn't handed a fresh wreck on its way out.
        DemonstratorSlotFactory.Reconcile();
        foreach (var controller in LocoRestorationController.allLocoRestorationControllers.ToList())
            GarageReplacementApplier.ReinitializeDemonstrator(controller);
        CommsRadioRefresher.Refresh();
    }

    internal static void ForceApplyGarages()
    {
        _forcedGarage = true;
        _allowGarage = null;
        AllowGarageChanges();
        GarageReplacementApplier.Apply();
        WorkTrainGarages.ReinitializeAll();
        CommsRadioRefresher.Refresh();
    }

    internal static string DemonstratorFingerprint()
    {
        var entries = new List<(string PrimaryId, string SpawnId, string? TenderId)>();
        foreach (var (garage, isDemonstrator, liveries) in GarageVehicles.Groups)
        {
            if (!isDemonstrator) continue;
            var primary = liveries.FirstOrDefault();
            if (primary == null) continue;
            var tender = GarageReplacements.ResolveTender(primary.id, GarageVehicles.OriginalTender(garage));
            entries.Add((primary.id, GarageReplacements.CurrentSpawnId(primary), tender?.id));
        }

        // Slots this mod adds are part of what a save was baked with, so adding or removing one has to
        // read as a mismatch on an existing save just like changing a vanilla demonstrator does. Sorted so
        // that merely reordering the settings list isn't mistaken for a change.
        foreach (var slot in Main.Settings.AdditionalSlots.OrderBy(s => s.LocoId, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(slot.LocoId)) continue;
            var tenderId = Main.Settings.GetTenderId(slot.LocoId);
            entries.Add((slot.LocoId, slot.LocoId, string.IsNullOrEmpty(tenderId) ? null : tenderId));
        }
        return SaveConfig.SerializeDemonstrators(entries);
    }

    internal static string GarageFingerprint()
    {
        var sb = new StringBuilder();
        foreach (var (garage, isDemonstrator, liveries) in GarageVehicles.Groups)
        {
            if (isDemonstrator) continue;
            sb.Append(garage.id).Append('=');
            foreach (var livery in liveries)
                sb.Append(GarageReplacements.CurrentSpawnId(livery)).Append(',');
            foreach (var extra in Main.Settings.GetExtraCars(garage.id))
                sb.Append('+').Append(extra);
            sb.Append(';');
        }
        return sb.ToString();
    }
}
