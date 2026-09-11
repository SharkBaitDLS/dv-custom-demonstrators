using System;
using System.Collections.Generic;
using System.Linq;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;
using CustomDemonstrators.World;
using DV.LocoRestoration;
using Newtonsoft.Json.Linq;

namespace CustomDemonstrators.Api;

public static class SaveRecord
{
    public const string KeyPrefix = "CustomDemonstrators_";

    /// <summary>
    /// Brings some of another save's demonstrators into the loaded save's record,
    /// and leaves every other slot untouched.
    /// </summary>
    /// <param name="theirs">the entire data object from the other save</param>
    /// <param name="saveIds">the demonstrators to pull from that save</param>
    /// <returns>true if a merge was completed</returns>
    public static bool MergeDemonstratorsFrom(JObject theirs, IEnumerable<string> saveIds) =>
        MergeDemonstratorsFrom(theirs, saveIds, []);

    /// <summary>
    /// Brings some of another save's demonstrators into the loaded save's record,
    /// and leaves every other slot untouched.
    /// </summary>
    /// <param name="theirs">the entire data object from the other save</param>
    /// <param name="saveIds">
    /// the demonstrators to put back into the slot they came from, displacing whatever stands there now
    /// </param>
    /// <param name="asNewSlots">
    /// the demonstrators to give a slot of their own instead, leaving the slot they used to stand in as it
    /// is. Only for a locomotive <see cref="NewSlotEligibility"/> answers null for.
    /// </param>
    /// <returns>true if a merge was completed</returns>
    public static bool MergeDemonstratorsFrom(
        JObject theirs, IEnumerable<string> saveIds, IEnumerable<string> asNewSlots)
    {
        try
        {
            return RecordMerge.Demonstrators(theirs, saveIds, asNewSlots);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException(
                $"{nameof(SaveRecord)}.{nameof(MergeDemonstratorsFrom)} failed:", ex);
            return false;
        }
    }

    /// <summary>
    /// What each of these demonstrators would displace by going back into the slot it came from, if any.
    /// </summary>
    /// <param name="theirs">the entire data object from the other save</param>
    /// <param name="saveIds">the demonstrators being weighed up</param>
    /// <returns>save id to the save id it would displace</returns>
    public static IDictionary<string, string> SlotOccupants(JObject theirs, IEnumerable<string> saveIds)
    {
        try
        {
            return RecordMerge.SlotOccupants(theirs, saveIds);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"{nameof(SaveRecord)}.{nameof(SlotOccupants)} failed:", ex);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Why each of these demonstrators can't be given a demonstrator slot of its own, or null if they can.
    /// </summary>
    /// <param name="theirs">the entire data object from the other save</param>
    /// <param name="saveIds">the demonstrators being weighed up</param>
    /// <returns>save id to the reason it is unavailable, or null where it is available</returns>
    public static IDictionary<string, string?> NewSlotEligibility(JObject theirs, IEnumerable<string> saveIds)
    {
        try
        {
            return RecordMerge.NewSlotEligibility(theirs, saveIds);
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"{nameof(SaveRecord)}.{nameof(NewSlotEligibility)} failed:", ex);
            return new Dictionary<string, string?>();
        }
    }

    // Re-reads the fingerprint of the loaded save and brings the world into line with it. Returns
    // false if there is no save to read or the reload failed.
    //
    // The parts cargo table (CargoIds/CargoValues) is reconciled rather than re-read, since the cargo types
    // registered with the game this session are already holding the numbers it handed out.
    public static bool Reload()
    {
        try
        {
            if (SaveState.Data() == null)
            {
                Main.Logger.Warning($"{nameof(SaveRecord)}.{nameof(Reload)} was called with no save loaded.");
                return false;
            }

            SaveConfig.Reset();
            MuseumStalls.Reset();
            GarageHomes.Reset();
            RestorationPartsCustomizer.Reset();
            SaveGuard.FingerprintReplaced();

            DemonstratorSetup.ReturnLentLicenses();

            SlotTypes.ReconcileCargoNumbers();

            GarageLiveries.RepointSpawners(GarageLiveries.Apply());
            DemonstratorSlots.Reconcile();
            RespawnChangedSlots();
            AddedGarages.Reconcile();
            CommsRadioRefresher.Refresh();

            Main.Logger.Log("Re-read this mod's record from the loaded save at another mod's request. The "
                + $"settings were left alone; demonstrator changes are {State(SaveGuard.IsDemonstratorOutOfSync)} "
                + $"and garage changes are {State(SaveGuard.IsGarageOutOfSync)} for this save.");
            return true;
        }
        catch (Exception ex)
        {
            Main.Logger.LogException($"{nameof(SaveRecord)}.{nameof(Reload)} failed:", ex);
            return false;
        }
    }

    private static void RespawnChangedSlots()
    {
        var pending = RecordMerge.TakePendingSlots();

        foreach (var controller in LocoRestorationController.allLocoRestorationControllers.ToList())
        {
            if (controller == null) continue;

            if (pending.Contains(DemonstratorSetup.OriginalLoco(controller)?.id ?? ""))
                DemonstratorSetup.ApplyTo(controller);
            else
                DemonstratorRespawner.ReinitializeDemonstrator(controller);
        }
    }

    private static string State(bool outOfSync) => outOfSync ? "held back" : "in effect";
}
