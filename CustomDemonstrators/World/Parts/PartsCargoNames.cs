using System;
using System.Collections.Generic;
using System.Linq;
using DV.Localization;
using DV.Simulation.Cars;
using DV.Simulation.Controllers;
using DV.ThingTypes;
using DVLangHelper.Data;
using DVLangHelper.Runtime;
using CustomDemonstrators.Localization;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World.Parts;

internal static class PartsCargoNames
{
    // The parts string template to derive a replaced demonstrator's name from
    private readonly struct Template(string fullKey, string shortKey, string token)
    {
        public readonly string FullKey = fullKey;
        public readonly string ShortKey = shortKey;
        public readonly string Token = token;
    }

    private static readonly Template DieselTemplate = new("cargo/tp_dm3", "cargo/tp_dm3_short", "DM3");
    private static readonly Template SteamTemplate = new("cargo/tp_s060", "cargo/tp_s060_short", "S060");

    private const string RewrittenKeyPrefix = "customdemonstrators/parts/";

    private static TranslationInjector? _injector;
    private static TranslationInjector Injector => _injector ??= new TranslationInjector("CustomDemonstrators");

    // I2 language code (lower-case) -> DVLangHelper enum, for mapping source languages to the injector.
    private static readonly Dictionary<string, DVLanguage> _byCode =
        Enum.GetValues(typeof(DVLanguage)).Cast<DVLanguage>()
            .GroupBy(l => l.Code().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

    internal static bool HasAuthored(TrainCarLivery? loco) =>
        AuthoredKey(CustomCarLoaderHelper.PartsNameKeyFor(loco)) != null
        || AuthoredKey(CustomCarLoaderHelper.PartsShortNameKeyFor(loco)) != null;

    // useAuthored false skips the author's name even where they gave one.
    internal static void Apply(CargoType_v2 partsCargo, TrainCarLivery loco, bool useAuthored)
    {
        if (useAuthored && ApplyAuthored(partsCargo, loco)) return;
        RenameToMatch(partsCargo, loco, IsSteam(loco) ? SteamTemplate : DieselTemplate);
    }

    // CCL registers these strings itself, so a key that resolves to nothing means no value was provided by
    // the mod author and we should build our own string.
    private static string? AuthoredKey(string? key) =>
        key != null && I2Terms.Find(key).Term != null ? key : null;

    // Names the parts exactly as the CCL author did, in every language they supplied.
    private static bool ApplyAuthored(CargoType_v2 partsCargo, TrainCarLivery loco)
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
        var (src, template) = I2Terms.Find(templateKey);
        if (src == null || template == null) return items;

        string fallbackName = I2Terms.ValueFor(loco.localizationKey, "en") ?? LocalizationAPI.L(loco.localizationKey);
        for (int i = 0; i < src.mLanguages.Count && i < template.Languages.Length; i++)
        {
            string code = src.mLanguages[i].Code;
            if (string.IsNullOrEmpty(code) || !_byCode.TryGetValue(code.ToLowerInvariant(), out var lang))
                continue;
            string templateValue = template.Languages[i];
            if (string.IsNullOrEmpty(templateValue)) continue;

            string locoName = I2Terms.ValueFor(loco.localizationKey, code) is { Length: > 0 } n ? n : fallbackName;
            items.Add(new TranslationItem(lang, templateValue.Replace(token, locoName)));
        }
        return items;
    }
}
