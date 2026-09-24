using System;
using System.Collections.Generic;
using CustomDemonstrators.Saves;
using Newtonsoft.Json.Linq;

namespace CustomDemonstrators.Api;

public static class SaveRecord
{
    public const string KeyPrefix = "CustomDemonstrators_";

    /// <summary>
    /// Puts a selection of another save's demonstrators back into the loaded world. Each one's slot is pointed at
    /// the locomotive it held in that save, ready for the caller to put its cars and restoration state back,
    /// and every other slot is left as it is.
    /// </summary>
    /// <remarks>
    /// Write the other save's <c>CustomDemonstrators_CargoIds</c> / <c>CargoValues</c> into the loaded save
    /// first if the parts cargo numbers are to come along, since they are reconciled here.
    /// </remarks>
    /// <param name="theirs">the entire data object from the other save</param>
    /// <param name="saveIds">
    /// the demonstrators to put back into the slot they came from, displacing whatever stands there now
    /// </param>
    /// <param name="asNewSlots">
    /// the demonstrators to give a slot of their own instead, leaving the slot they used to stand in as it
    /// is. Only for a locomotive <see cref="NewSlotEligibility"/> answers null for.
    /// </param>
    /// <returns>true once every slot is pointed at its demonstrator</returns>
    public static bool RestoreDemonstratorsFrom(
        JObject theirs, IEnumerable<string> saveIds, IEnumerable<string> asNewSlots)
    {
        try
        {
            if (SaveState.Data() == null)
            {
                Main.Logger.Warning($"{nameof(SaveRecord)}.{nameof(RestoreDemonstratorsFrom)} was called with no "
                    + "save loaded.");
                return false;
            }

            if (RecordMerge.Demonstrators(theirs, saveIds, asNewSlots) is not { } merged) return false;

            RecordReload.Run(merged);
            return true;
        }
        catch (Exception ex)
        {
            Main.Logger.LogException(
                $"{nameof(SaveRecord)}.{nameof(RestoreDemonstratorsFrom)} failed:", ex);
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
}
