using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV;
using DV.Localization;
using DV.LocoRestoration;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using DV.ThingTypes;
using DV.ThingTypes.TransitionHelpers;
using DVLangHelper.Data;
using DVLangHelper.Runtime;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using CCL.Types;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

internal static class RestorationPartsCustomizer
{
    // The pristine parts-cargo state per demonstrator SLOT (keyed by the original demonstrator loco id),
    // captured before we first customize. Keyed by slot (not controller) so the settings layer can revert
    // it the moment an override changes, making the GUI's parts row reflect it immediately.
    private sealed class CargoSnapshot(
        CargoType_v2 cargo, string? fullKey, string? shortKey, float mass, GameObject[][] variants)
    {
        public readonly CargoType_v2 Cargo = cargo;
        public readonly string? FullKey = fullKey;
        public readonly string? ShortKey = shortKey;
        public readonly float Mass = mass;
        public readonly GameObject[][] Variants = variants; // parallel to Cargo.loadableCarTypes
    }

    private static readonly Dictionary<string, CargoSnapshot> _snapshots = [];

    // Slots whose cargo we have rewritten since snapshotting it. A rewrite doesn't always show up in the
    // name (a CCL author can change nothing but the crate model), so "is this dirty?" has to be tracked
    // rather than inferred from any one field. Same lifetime as the snapshots for the same reason.
    private static readonly HashSet<string> _customized = [];

    // The parts string template to derive a replaced demonstrator's name from. The DM3 ("DM3 Drivetrain")
    // is the generic default while steam locos use the S060 ("S060 Boiler"). Token is the
    // loco-code substring we replace with the new loco's name.
    private readonly struct Template(string fullKey, string shortKey, string token)
    {
        public readonly string FullKey = fullKey;
        public readonly string ShortKey = shortKey;
        public readonly string Token = token;
    }

    private static readonly Template DieselTemplate = new("cargo/tp_dm3", "cargo/tp_dm3_short", "DM3");
    private static readonly Template SteamTemplate = new("cargo/tp_s060", "cargo/tp_s060_short", "S060");

    private static TranslationInjector? _injector;
    private static TranslationInjector Injector => _injector ??= new TranslationInjector("CustomDemonstrators");

    // I2 language code (lower-case) -> DVLangHelper enum, for mapping source languages to the injector.
    private static readonly Dictionary<string, DVLanguage> _byCode =
        Enum.GetValues(typeof(DVLanguage)).Cast<DVLanguage>()
            .GroupBy(l => l.Code().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

    private static readonly FieldInfo? _prefabCacheField =
        AccessTools.Field(typeof(CargoType_v2), "_trainCargoToCargoPrefabs");

    // Repair any cargoes that duplicate their loadable car types
    private static void Sanitize(CargoType_v2? cargo)
    {
        var loadables = cargo != null ? cargo.loadableCarTypes : null;
        if (cargo == null || loadables == null || loadables.Length == 0) return;

        var seen = new HashSet<TrainCarType_v2>();
        var kept = new List<CargoType_v2.LoadableInfo>(loadables.Length);
        var duplicates = new List<string>();
        int blanks = 0;

        foreach (var li in loadables)
        {
            if (li == null || li.carType == null) { blanks++; continue; }
            if (!seen.Add(li.carType)) { duplicates.Add(li.carType.id); continue; }
            kept.Add(li);
        }

        if (duplicates.Count == 0 && blanks == 0) return;

        cargo.loadableCarTypes = [.. kept];
        _prefabCacheField?.SetValue(cargo, null); // drop any dictionary built from the old array

        if (duplicates.Count > 0)
            Main.Logger.Warning($"Cargo '{cargo.id}' listed {string.Join(", ", duplicates.Distinct())} more "
                + "than once among its loadable car types; dropped the duplicate(s). This is a data error in "
                + "whichever mod contributed that car type, and would otherwise crash loading that cargo.");
        if (blanks > 0)
            Main.Logger.Warning($"Cargo '{cargo.id}' had {blanks} loadable car type entries with no car type; "
                + "dropped them. Same failure mode as a duplicate entry.");
    }

    // Settings sentinel meaning "force the generic crate" even if the CCL mod provides a different prefab
    internal const string GenericCrateSentinel = "__cd_generic_crate__";

    private const string RewrittenKeyPrefix = "customdemonstrators/parts/";

    internal static void ApplyCargo(LocoRestorationController controller, string slotId, TrainCarLivery? replacementLoco)
    {
        Sanitize(controller.locoPartCargo);

        var source = ChooseCargo(controller, slotId, replacementLoco);

        Sanitize(controller.locoPartCargo);

        Main.Logger.Log($"Restoration '{slotId}' is using parts cargo "
            + $"'{(controller.locoPartCargo != null ? controller.locoPartCargo.id : "<none>")}' ({source}).");

        // Whatever the choice turned out to be, the warehouse has to be willing to load it.
        PartsWarehouse.EnsureSupported(controller);
    }

    // Returns how the cargo was chosen, for the log line in ApplyCargo.
    private static string ChooseCargo(LocoRestorationController controller, string slotId, TrainCarLivery? replacementLoco)
    {
        // Snapshots exist to put a shared game cargo back the way we found it. A slot added by this mod
        // only ever rewrites a copy it owns, and gets a fresh one each load, so it has nothing to restore.
        if (!SlotTypes.IsSlotGarage(GameTypes.GarageOf(controller)))
            EnsureSnapshot(slotId, controller.locoPartCargo);

        var choice = CargoChoice(slotId);

        if (!string.IsNullOrEmpty(choice) && choice != GenericCrateSentinel)
        {
            var picked = FindCargo(choice!);
            if (picked != null && SlotChoices.CanBeRestorationParts(picked))
            {
                controller.locoPartCargo = picked; // use the chosen cargo (and its model) as-is
                SyncRegisterNames(controller);
                return "explicit override";
            }

            // This should generally never happen since the settings GUI enforces correctness of the
            // overrides, but if the settings file is hand-edited or a mod author updates their CCL
            // car in a way that invalidates a previously valid configuration we should not brick the
            // entire quest chain. Log so user bug reports can be pointed to the actual root cause.
            Main.Logger.Warning($"Restoration parts cargo override '{choice}' for {slotId} is invalid "
                + "(cargo missing or not loadable on the parts flatcar); falling back to the car's own "
                + "parts, or the generic crate where its mod doesn't specify any.");
        }

        // Reverting back to vanilla quest state
        if (replacementLoco == null)
        {
            RevertCargo(controller, slotId);
            return "vanilla (no replacement loco)";
        }
        if (controller.locoPartCargo == null) return "none (slot has no parts cargo)";

        // Forcing the generic crate is the player saying they want the plain reskin, so it also opts out
        // of whatever the CCL author specified for the parts.
        bool useAuthored = choice != GenericCrateSentinel;

        bool authored = useAuthored && HasAuthoredParts(replacementLoco);

        // Past this point the cargo gets rewritten in place, so the slot has to be holding a cargo that is
        // ours to rewrite rather than whatever an explicit override last pointed it at.
        if (_snapshots.TryGetValue(slotId, out var snapshot))
            controller.locoPartCargo = snapshot.Cargo;

        // An added slot has no vanilla cargo to put back, and owns a copy made for exactly this.
        if (DemonstratorSlots.OwnCargoFor(controller) is CargoType_v2 own)
            controller.locoPartCargo = own;

        Customize(controller.locoPartCargo, replacementLoco, useAuthored);
        _customized.Add(slotId);
        SyncRegisterNames(controller);
        return authored ? "authored by the car's mod"
            : choice == GenericCrateSentinel ? "generic crate (forced)"
            : "generic crate";
    }

    private static readonly SavedMap _bakedCargo =
        new("CustomDemonstrators_CargoSlots", "CustomDemonstrators_CargoChoices");

    internal static string? BakedCargoChoice(string slotId) => _bakedCargo.Get(slotId);

    internal static void MergeCargoChoicesFrom(SaveGameData other, ICollection<string> slotIds) =>
        _bakedCargo.MergeFrom(other, slotIds);

    private static string? CargoChoice(string slotId)
    {
        if (!SaveGuard.AllowDemonstratorChanges()) return _bakedCargo.Get(slotId);

        var choice = Main.Settings.GetPartsCargoId(slotId);
        _bakedCargo.Set(slotId, string.IsNullOrEmpty(choice) ? null : choice);
        return choice;
    }

    // Point the order/install cash registers at the active cargo's name so the purchase receipt reads
    // the chosen parts, not the original demonstrator's. The modules carry their own localizationKey
    // (baked to the vanilla cargo); InitializeData() copies it into the receipt's resourceName when the
    // controller starts, which is after we run here, so overwriting the field is enough.
    private static void SyncRegisterNames(LocoRestorationController controller)
    {
        var key = controller.locoPartCargo != null ? controller.locoPartCargo.localizationKeyFull : null;
        if (string.IsNullOrEmpty(key)) return;
        if (controller.orderPartsModule != null) controller.orderPartsModule.localizationKey = key!;
        if (controller.installPartsModule != null) controller.installPartsModule.localizationKey = key!;
    }

    private static void EnsureSnapshot(string slotId, CargoType_v2? cargo)
    {
        if (string.IsNullOrEmpty(slotId) || cargo == null || _snapshots.ContainsKey(slotId)) return;
        var variants = cargo.loadableCarTypes?.Select(li => li.cargoPrefabVariants).ToArray() ?? [];
        _snapshots[slotId] = new CargoSnapshot(
            cargo, cargo.localizationKeyFull, cargo.localizationKeyShort, cargo.massPerUnit, variants);
    }

    internal static void RevertSlotCargo(string slotId)
    {
        if (!_snapshots.TryGetValue(slotId, out var snap)) return;
        if (!_customized.Remove(slotId)) return;

        snap.Cargo.localizationKeyFull = snap.FullKey;
        snap.Cargo.localizationKeyShort = snap.ShortKey;
        snap.Cargo.massPerUnit = snap.Mass;

        var loadables = snap.Cargo.loadableCarTypes;
        if (loadables != null)
        {
            for (int i = 0; i < loadables.Length && i < snap.Variants.Length; i++)
            {
                loadables[i].cargoPrefabVariants = snap.Variants[i];
            }
        }
        _prefabCacheField?.SetValue(snap.Cargo, null); // force TrainCargoToCargoPrefabs to rebuild
    }

    private static void RevertCargo(LocoRestorationController controller, string slotId)
    {
        if (!_snapshots.TryGetValue(slotId, out var snap)) return;
        if (controller.locoPartCargo == snap.Cargo && !_customized.Contains(slotId)) return;

        RevertSlotCargo(slotId);
        controller.locoPartCargo = snap.Cargo;
        SyncRegisterNames(controller);
    }

    // Snapshots must *not* be reset between saves or else we mistakenly read our modified state into the snapshot
    internal static void Reset()
    {
        _bakedCargo.Reset();
    }

    internal static CargoType_v2? FindCargo(string id) =>
        GameTypes.Cargos?.FirstOrDefault(c => c != null && c.id == id);

    // Whether the CCL author configured anything about this loco's replacement parts,
    // used purely in the GUI to indicate to the users whether the default setting
    // comes from our mod or theirs.
    internal static bool HasAuthoredParts(TrainCarLivery? loco) =>
        AuthoredKey(CustomCarLoaderHelper.PartsNameKeyFor(loco)) != null
        || AuthoredKey(CustomCarLoaderHelper.PartsShortNameKeyFor(loco)) != null
        || HasAuthoredModel(loco);

    private static bool HasAuthoredModel(TrainCarLivery? loco) => CustomCarLoaderHelper.PartsModelFor(loco) switch
    {
        null or PartsCargoModel.GenericBox => false,
        PartsCargoModel.Custom => CustomCarLoaderHelper.HasPartsPrefab(loco),
        _ => true,
    };

    // CCL registers these strings itself, so a key that resolves to nothing means no value was provided by
    // the mod author and we should build our own string.
    private static string? AuthoredKey(string? key) =>
        key != null && FindTerm(key).td != null ? key : null;

    private static void Customize(CargoType_v2 partsCargo, TrainCarLivery loco, bool useAuthored)
    {
        if (!useAuthored || !ApplyAuthoredName(partsCargo, loco))
        {
            var template = IsSteam(loco) ? SteamTemplate : DieselTemplate;
            RenameToMatch(partsCargo, loco, template);
        }

        ApplyModel(partsCargo, loco, useAuthored);

        if (CustomCarLoaderHelper.PartsMassFor(loco) is float mass && mass > 0f)
            partsCargo.massPerUnit = mass;
    }

    // Names the parts exactly as the CCL author did, in every language they supplied.
    private static bool ApplyAuthoredName(CargoType_v2 partsCargo, TrainCarLivery loco)
    {
        var fullKey = AuthoredKey(CustomCarLoaderHelper.PartsNameKeyFor(loco));
        var shortKey = AuthoredKey(CustomCarLoaderHelper.PartsShortNameKeyFor(loco));
        if (fullKey == null && shortKey == null) return false;

        // An author who filled in only one of the two gets it used for both, the same as the game does
        // for cargoes whose short name would just repeat the long one.
        partsCargo.localizationKeyFull = fullKey ?? shortKey!;
        partsCargo.localizationKeyShort = shortKey ?? fullKey!;
        return true;
    }

    private static bool IsSteam(TrainCarLivery loco)
    {
        if (CarTypes.IsSteamLocomotive(loco)) return true;
        var prefab = loco.prefab;
        return prefab != null
            && (prefab.GetComponentInChildren<BoilerSimController>(includeInactive: true) != null
                || prefab.GetComponentInChildren<FireboxSimController>(includeInactive: true) != null);
    }

    private static void RenameToMatch(CargoType_v2 partsCargo, TrainCarLivery loco, Template template)
    {
        string fullKey = $"{RewrittenKeyPrefix}{loco.id}";
        string shortKey = $"{RewrittenKeyPrefix}{loco.id}_short";

        var fullItems = BuildSubstituted(template.FullKey, template.Token, loco);
        if (fullItems.Count > 0)
        {
            Injector.AddTranslations(fullKey, fullItems);
            partsCargo.localizationKeyFull = fullKey;
        }

        var shortItems = BuildSubstituted(template.ShortKey, template.Token, loco);
        if (shortItems.Count > 0)
        {
            Injector.AddTranslations(shortKey, shortItems);
            partsCargo.localizationKeyShort = shortKey;
        }
    }

    // For each language of the template term, replace the template's loco-code token with the loco's name.
    private static List<TranslationItem> BuildSubstituted(string templateKey, string token, TrainCarLivery loco)
    {
        var items = new List<TranslationItem>();
        var (src, template) = FindTerm(templateKey);
        if (src == null || template == null) return items;

        string fallbackName = GetTermValueByCode(loco.localizationKey, "en") ?? LocalizationAPI.L(loco.localizationKey);
        for (int i = 0; i < src.mLanguages.Count && i < template.Languages.Length; i++)
        {
            string code = src.mLanguages[i].Code;
            if (string.IsNullOrEmpty(code) || !_byCode.TryGetValue(code.ToLowerInvariant(), out var lang))
                continue;
            string templateValue = template.Languages[i];
            if (string.IsNullOrEmpty(templateValue)) continue;

            string locoName = GetTermValueByCode(loco.localizationKey, code) is { Length: > 0 } n ? n : fallbackName;
            items.Add(new TranslationItem(lang, templateValue.Replace(token, locoName)));
        }
        return items;
    }

    private static readonly Dictionary<PartsCargoModel, CargoType> _modelSources = new()
    {
        [PartsCargoModel.GenericBox] = CargoType.TrainPartsDM3,
        [PartsCargoModel.BoilerS060] = CargoType.TrainPartsS060,
        [PartsCargoModel.WheelsS282] = CargoType.TrainPartsS282A,
        [PartsCargoModel.EngineDE6] = CargoType.TrainPartsDE6,
    };

    private static void ApplyModel(CargoType_v2 partsCargo, TrainCarLivery loco, bool useAuthored)
    {
        var model = useAuthored
            ? CustomCarLoaderHelper.PartsModelFor(loco) ?? PartsCargoModel.GenericBox
            : PartsCargoModel.GenericBox;

        if (model == PartsCargoModel.Custom && UseAuthoredModel(partsCargo, loco)) return;

        UseStockModel(partsCargo,
            _modelSources.TryGetValue(model, out var source) ? source : CargoType.TrainPartsDM3);
    }

    // Loads the author's own crate prefabs, or falls back to the default crate if not provided
    private static bool UseAuthoredModel(CargoType_v2 partsCargo, TrainCarLivery loco)
    {
        var (dm1u, flatbed) = CustomCarLoaderHelper.PartsPrefabsFor(loco);
        if (dm1u == null && flatbed == null)
        {
            return false;
        }
        if (partsCargo.loadableCarTypes == null) return false;

        // Default to the stock crate so a carrier the author shipped no prefab for keeps a model that fits it.
        UseStockModel(partsCargo, CargoType.TrainPartsDM3);

        var dm1uType = TrainCarType.LocoDM1U.ToV2();

        foreach (var li in partsCargo.loadableCarTypes)
        {
            bool isDm1u = li.carType != null && li.carType == dm1uType;
            var prefab = isDm1u ? dm1u : flatbed;

            if (prefab != null) li.cargoPrefabVariants = [prefab];
        }
        _prefabCacheField?.SetValue(partsCargo, null); // force TrainCargoToCargoPrefabs to rebuild
        return true;
    }

    // Swap the parts crate model for one of the game's own, keeping the cargo's loadable car types
    private static void UseStockModel(CargoType_v2 partsCargo, CargoType model)
    {
        var stock = model.ToV2();
        if (stock == null || stock == partsCargo || stock.loadableCarTypes == null
            || stock.loadableCarTypes.Length == 0)
            return;
        if (partsCargo.loadableCarTypes == null) return;

        foreach (var li in partsCargo.loadableCarTypes)
        {
            var from = stock.loadableCarTypes.FirstOrDefault(d => d.carType == li.carType)
                ?? stock.loadableCarTypes[0];
            li.cargoPrefabVariants = from.cargoPrefabVariants;
        }
        _prefabCacheField?.SetValue(partsCargo, null); // force TrainCargoToCargoPrefabs to rebuild
    }

    private static (LanguageSourceData? src, TermData? td) FindTerm(string term)
    {
        foreach (var s in LocalizationManager.Sources)
        {
            if (s == null) continue;
            var t = s.GetTermData(term);
            if (t != null) return (s, t);
        }
        return (null, null);
    }

    private static string? GetTermValueByCode(string term, string code)
    {
        foreach (var s in LocalizationManager.Sources)
        {
            if (s == null) continue;
            var t = s.GetTermData(term);
            if (t == null) continue;
            for (int i = 0; i < s.mLanguages.Count && i < t.Languages.Length; i++)
                if (string.Equals(s.mLanguages[i].Code, code, StringComparison.OrdinalIgnoreCase))
                    return t.Languages[i];
        }
        return null;
    }
}
