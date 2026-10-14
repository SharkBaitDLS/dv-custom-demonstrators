using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.LocoRestoration;
using DV.ThingTypes;
using UnityEngine;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

// The original demonstrator controllers
internal static class RestorationTemplates
{
    internal sealed class Snapshot
    {
        internal readonly LocoRestorationController Source;

        private readonly Dictionary<FieldInfo, object?> _fields;

        internal readonly GarageType_v2 Garage;
        internal readonly TrainCarLivery Loco;
        internal readonly TrainCarLivery? SecondCar;
        internal readonly CargoType_v2? PartsCargo;
        internal readonly float OrderPrice;
        internal readonly string OrderKey;
        internal readonly float InstallPrice;
        internal readonly string InstallKey;
        internal readonly float SpawnLocoPlayerSqrDistanceFromTrack;
        internal readonly bool FlipSpawnLoco;

        internal readonly GameObject? Board;

        internal Snapshot(LocoRestorationController source, GarageType_v2 garage, GameObject? board)
        {
            Source = source;
            _fields = Fields.ToDictionary(f => f, f => (object?)f.GetValue(source));
            Garage = garage;
            Loco = source.locoLivery;
            SecondCar = source.secondCarLivery;
            PartsCargo = source.locoPartCargo;
            OrderPrice = source.orderPartsModule.price;
            OrderKey = source.orderPartsModule.localizationKey;
            InstallPrice = source.installPartsModule.price;
            InstallKey = source.installPartsModule.localizationKey;
            SpawnLocoPlayerSqrDistanceFromTrack = source.garageSpawner.spawnLocoPlayerSqrDistanceFromTrack;
            FlipSpawnLoco = source.garageSpawner.flipSpawnLoco;
            Board = board;
        }

        internal void CopyTo(LocoRestorationController controller)
        {
            foreach (var kv in _fields) kv.Key.SetValue(controller, kv.Value);
        }
    }

    private static readonly FieldInfo[] Fields =
        typeof(LocoRestorationController).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static readonly List<Snapshot> _snapshots = [];
    private static GameObject? _holder;

    internal static IReadOnlyList<Snapshot> All => _snapshots;

    internal static void Reset()
    {
        _snapshots.Clear();
        if (_holder != null) Object.Destroy(_holder);
        _holder = null;
    }

    // Called from Awake's prefix, which Harmony runs even while Mapify is suppressing Awake
    internal static void Capture(LocoRestorationController controller)
    {
        if (!IsComplete(controller)) return;

        var garage = controller.garageSpawner.garageType;
        if (!VanillaGarages.IsDemonstrator(garage.v1)) return;
        if (_snapshots.Any(s => s.Garage == garage)) return;

        _snapshots.Add(new Snapshot(controller, garage, CopyBoard(controller)));
    }

    internal static Snapshot? Of(LocoRestorationController controller)
    {
        var snapshot = _snapshots.FirstOrDefault(s => s.Source == controller);
        if (snapshot != null || !IsComplete(controller)) return snapshot;

        Main.Logger.Warning($"No snapshot was taken of restoration {GameTypes.Id(controller.locoLivery)} as it "
            + "woke; reading it as it stands now instead.");
        snapshot = new Snapshot(controller, controller.garageSpawner.garageType, null);
        _snapshots.Add(snapshot);
        return snapshot;
    }

    private static bool IsComplete(LocoRestorationController controller) =>
        controller != null && controller.locoLivery != null
        && controller.garageSpawner != null && controller.garageSpawner.garageType != null
        && controller.orderPartsModule != null && controller.installPartsModule != null;

    private static GameObject? CopyBoard(LocoRestorationController controller)
    {
        if (!controller.TryGetComponent<LocoRestorationView>(out _)) return null;

        if (_holder == null)
        {
            _holder = new GameObject("CustomDemonstrators_RestorationTemplates");
            _holder.SetActive(false);
            Object.DontDestroyOnLoad(_holder);
        }

        var board = Object.Instantiate(controller.gameObject, _holder.transform, worldPositionStays: false);
        board.name = $"CustomDemonstrators_{controller.locoLivery.id}_TemplateBoard";

        // Never awoken, so destroying these can't unregister anything the originals registered
        foreach (var c in board.GetComponentsInChildren<LocoRestorationController>(includeInactive: true))
            Object.DestroyImmediate(c);
        foreach (var s in board.GetComponentsInChildren<GarageCarSpawner>(includeInactive: true))
            Object.DestroyImmediate(s);
        foreach (var m in board.GetComponentsInChildren<CashRegisterModule>(includeInactive: true))
            Object.DestroyImmediate(m);

        return board;
    }
}
