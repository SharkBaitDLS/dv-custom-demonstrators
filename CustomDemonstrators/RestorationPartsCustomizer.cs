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

namespace CustomDemonstrators;

internal static class RestorationPartsCustomizer
{
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

    // Settings sentinel meaning "force the generic crate", i.e. skip auto-detect and use the DM3 reskin
    // even if a name-matching cargo exists. Distinct from null (auto-detect) and a real cargo id.
    internal const string GenericCrateSentinel = "__cd_generic_crate__";

    internal static void ApplyCargo(LocoRestorationController controller, string slotId, TrainCarLivery? replacementLoco)
    {
        var choice = Main.Settings.GetPartsCargoId(slotId);

        if (!string.IsNullOrEmpty(choice) && choice != GenericCrateSentinel)
        {
            var picked = FindCargo(choice!);
            if (picked != null)
            {
                controller.locoPartCargo = picked; // use the chosen cargo (and its model) as-is
                return;
            }
        }

        if (replacementLoco == null || controller.locoPartCargo == null) return;

        if (choice != GenericCrateSentinel)
        {
            var matched = FuzzyMatchPartsCargo(replacementLoco);
            if (matched != null)
            {
                controller.locoPartCargo = matched;
                return;
            }
        }

        Customize(controller.locoPartCargo, replacementLoco);
    }

    internal static CargoType_v2? FindCargo(string id) =>
        Globals.G?.Types?.cargos?.FirstOrDefault(c => c != null && c.id == id);

    // Best-effort guess at the cargo a CCL modder set up as `loco`'s repair parts, matching by name
    // in both the localized string and the parts id. Only used to pre-fill the settings GUI, we don't
    // make any guesses at actual runtime and fully respect the user's choice in the actual ApplyCargo
    // method.
    internal static CargoType_v2? FuzzyMatchPartsCargo(TrainCarLivery loco)
    {
        var cargos = Globals.G?.Types?.cargos;
        if (cargos == null) return null;

        string locoId = Normalize(loco.id);
        string locoName = Normalize(LocalizationAPI.L(loco.localizationKey));

        CargoType_v2? best = null;
        int bestScore = 0;
        foreach (var cargo in cargos)
        {
            if (cargo == null) continue;
            string cargoId = Normalize(cargo.id);
            string cargoName = Normalize(LocalizationAPI.L(cargo.localizationKeyFull));

            int score = ContainScore(cargoId, locoId) + ContainScore(cargoId, locoName)
                + ContainScore(cargoName, locoId) + ContainScore(cargoName, locoName);
            if (score == 0) continue;
            if (cargoId.Contains("part") || cargoName.Contains("part")) score += 3;

            if (score > bestScore)
            {
                bestScore = score;
                best = cargo;
            }
        }
        return best;
    }

    private static int ContainScore(string haystack, string needle) =>
        needle.Length >= 2 && haystack.Contains(needle) ? needle.Length : 0;

    private static string Normalize(string? s) =>
        string.IsNullOrEmpty(s) ? "" : new string(s!.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static void Customize(CargoType_v2 partsCargo, TrainCarLivery loco)
    {
        var template = IsSteam(loco) ? SteamTemplate : DieselTemplate;
        RenameToMatch(partsCargo, loco, template);
        UseDm3Model(partsCargo);
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
        string fullKey = $"customdemonstrators/parts/{loco.id}";
        string shortKey = $"customdemonstrators/parts/{loco.id}_short";

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

    // Swap the parts crate model for the crate one, keeping the cargo's own loadable car types
    private static void UseDm3Model(CargoType_v2 partsCargo)
    {
        var dm3 = CargoType.TrainPartsDM3.ToV2();
        if (dm3 == null || dm3 == partsCargo || dm3.loadableCarTypes == null || dm3.loadableCarTypes.Length == 0)
            return;
        if (partsCargo.loadableCarTypes == null) return;

        foreach (var li in partsCargo.loadableCarTypes)
        {
            var dm3Li = dm3.loadableCarTypes.FirstOrDefault(d => d.carType == li.carType) ?? dm3.loadableCarTypes[0];
            li.cargoPrefabVariants = dm3Li.cargoPrefabVariants;
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
