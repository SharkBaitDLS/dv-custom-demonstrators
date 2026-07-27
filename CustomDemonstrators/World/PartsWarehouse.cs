using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DV;
using DV.Localization;
using DV.LocoRestoration;
using DV.ThingTypes;
using HarmonyLib;

namespace CustomDemonstrators.World;

internal static class PartsWarehouse
{
    private static readonly List<(WarehouseMachineController Machine, CargoType Cargo)> _added = [];

    internal static void Reset() => _added.Clear();

    internal static void EnsureSupported(LocoRestorationController controller)
    {
        var machine = controller.warehouseMachineForPartPickup;
        var cargo = controller.locoPartCargo;
        if (machine == null || machine.supportedCargoTypes == null || cargo == null) return;

        Prune();
        if (machine.supportedCargoTypes.Contains(cargo.v1)) return;

        machine.supportedCargoTypes.Add(cargo.v1);
        _added.Add((machine, cargo.v1));
        RefreshDisplay(machine);
        Main.Logger.Log($"Registered parts cargo '{cargo.id}' with warehouse machine {machine.name}.");
    }

    // Drop entries no live restoration asks for any more, so reconfiguring a demonstrator doesn't leave the
    // warehouse advertising cargo nothing will ever order.
    private static void Prune()
    {
        var touched = new HashSet<WarehouseMachineController>();

        for (int i = _added.Count - 1; i >= 0; i--)
        {
            var (machine, cargo) = _added[i];
            if (machine == null)
            {
                _added.RemoveAt(i);
                continue;
            }

            bool stillUsed = LocoRestorationController.allLocoRestorationControllers
                .Any(c => c != null
                    && c.warehouseMachineForPartPickup == machine
                    && c.locoPartCargo != null
                    && c.locoPartCargo.v1 == cargo);
            if (stillUsed) continue;

            machine.supportedCargoTypes?.Remove(cargo);
            _added.RemoveAt(i);
            touched.Add(machine);
        }

        foreach (var machine in touched) RefreshDisplay(machine);
    }

    private static readonly FieldInfo? SupportedText =
        AccessTools.Field(typeof(WarehouseMachineController), "supportedCargoTypesText");

    private static void RefreshDisplay(WarehouseMachineController machine)
    {
        if (SupportedText == null || machine.supportedCargoTypes == null) return;

        var cargos = Globals.G?.Types?.cargos;
        if (cargos == null) return;

        var text = new StringBuilder();
        foreach (var cargo in machine.supportedCargoTypes)
        {
            // Don't use the game's V2 method since we hit cargoes that are now removed and it
            // throws an error if it no longer exists.
            var v2 = cargos.FirstOrDefault(c => c != null && c.v1 == cargo);
            if (v2 != null) text.AppendLine(LocalizationAPI.L(v2.localizationKeyFull));
        }
        SupportedText.SetValue(machine, text.ToString());
    }
}
