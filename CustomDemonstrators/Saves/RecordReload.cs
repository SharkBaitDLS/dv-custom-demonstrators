using System.Collections.Generic;
using System.Linq;
using CustomDemonstrators.Slots;
using CustomDemonstrators.World;
using DV.LocoRestoration;

namespace CustomDemonstrators.Saves;

// Brings the loaded world into line with a record that changed underneath it, whether another mod merged
// into it or it was rewritten from what the world was seen to hold. The settings are left alone.
//
// The parts cargo table (CargoIds/CargoValues) is reconciled rather than re-read, since the cargo types
// registered with the game this session are already holding the numbers it handed out.
internal static class RecordReload
{
    // slotsInPlace are the slots whose cars are already, or are about to be, put in place by someone else.
    // They are re-pointed at what the record says but never respawned.
    internal static void Run(ICollection<string> slotsInPlace)
    {
        SaveConfig.Reset();
        MuseumStalls.Reset();
        GarageHomes.Reset();
        RestorationPartsCustomizer.Reset();
        SaveGuard.FingerprintReplaced();

        DemonstratorSetup.ReturnLentLicenses();

        SlotTypes.ReconcileCargoNumbers();

        GarageLiveries.RepointSpawners(GarageLiveries.Apply());
        DemonstratorSlots.Reconcile();

        foreach (var controller in LocoRestorationController.allLocoRestorationControllers.ToList())
        {
            if (controller == null) continue;

            if (slotsInPlace.Contains(DemonstratorSetup.SlotId(controller) ?? ""))
                DemonstratorSetup.ApplyTo(controller);
            else
                DemonstratorRespawner.ReinitializeDemonstrator(controller);
        }

        AddedGarages.Reconcile();
        CommsRadioRefresher.Refresh();

        Main.Logger.Log("Brought the world into line with this save's record. The settings were left alone; "
            + $"demonstrator changes are {State(SaveGuard.IsDemonstratorOutOfSync)} and garage changes are "
            + $"{State(SaveGuard.IsGarageOutOfSync)} for this save.");
    }

    private static string State(bool outOfSync) => outOfSync ? "held back" : "in effect";
}
