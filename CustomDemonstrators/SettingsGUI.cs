using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Localization;
using DV.ThingTypes;
using UnityEngine;
using UnityModManagerNet;

namespace CustomDemonstrators;

internal static class SettingsGUI
{
    // TODO: localize? We do have Localization Helper in scope but I'm wary of machine translations.
    private const string NoReplacementLabel = "(default — no replacement)";
    private const string AutoCargoLabel = "Auto-detect";
    private const string GenericCrateLabel = "Generic parts crate";
    private const string DefaultTenderLabel = "Default";

    private static List<TrainCarLivery>? _candidateLiveries;
    private static string? _openPickerFor;
    private static Vector2 _pickerScroll;
    private static string _pickerSearch = "";

    private static List<CargoType_v2>? _candidateCargos;
    private static string? _openCargoPickerFor;
    private static Vector2 _cargoScroll;
    private static string _cargoSearch = "";

    private static List<TrainCarLivery>? _candidateTenders;
    private static string? _openTenderPickerFor;
    private static Vector2 _tenderScroll;
    private static string _tenderSearch = "";

    private static string? _openExtraPickerFor;
    private static Vector2 _extraScroll;
    private static string _extraSearch = "";

    // Edit buffers for the price text fields, keyed by "<slotId>:order" / "<slotId>:install"
    private static readonly Dictionary<string, string> _priceText = [];

    private const string IntroText =
        """
        Choose a replacement for each Demonstrator and Garage spawn. The chosen stock spawns in place of the default when a new save is created.

        Demonstrator changes have no effect on existing save games, but any unopened Garages will reflect your choices.
        """;

    internal static void OnGUI(UnityModManager.ModEntry entry)
    {
        if (Globals.G?.Types == null)
        {
            GUILayout.Label("Waiting for game data to load…");
            return;
        }

        _candidateLiveries ??= [.. Globals.G.Types.Liveries.OrderBy(l => l.id)];
        _candidateCargos ??= [.. Globals.G.Types.cargos.Where(c => c != null).OrderBy(c => c.id)];
        _candidateTenders ??= [.. Globals.G.Types.Liveries.Where(GarageReplacements.IsValidTender).OrderBy(l => l.id)];

        GUILayout.Label(IntroText, GUILayout.ExpandWidth(true));
        GUILayout.Space(6);

        DrawSaveGuardNotice();

#if DEBUG
        DebugCheats.Draw();
#endif

        var groups = GarageVehicles.Groups;

        DrawSection(Loc("license/museum_cs", "Museum"), groups.Where(g => g.isDemonstrator));
        GUILayout.Space(6);
        DrawSection(Loc("comms/mode_work_train", "Work Trains"), groups.Where(g => !g.isDemonstrator));
    }

    private static void DrawSaveGuardNotice()
    {
        if (SaveGuard.IsDemonstratorOutOfSync)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Demonstrator changes are not in effect for this save because its demonstrator "
                + "settings differ from the ones it was created with.");
            if (GUILayout.Button("Force respawn demonstrators", GUILayout.Width(360)))
                SaveGuard.ForceApplyDemonstrators();
            GUILayout.Label("Each demonstrator respawns as a fresh wreck of your chosen replacement. Demonstrators "
                + "you've already finished restoring are kept as owned by your player but will no longer be "
                + "associated with a demonstrator slot in the museum or summonable by the comms radio.");
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }

        if (SaveGuard.IsGarageOutOfSync)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Garage changes are not in effect for this save because its garage settings differ "
                + "from the ones it was created with.");
            if (GUILayout.Button("Force respawn garages", GUILayout.Width(360)))
                SaveGuard.ForceApplyGarages();
            GUILayout.Label("Each opened garage respawns your chosen replacement. Cars you've already taken "
                + "ownership of are kept as owned by your player but will no longer be summonable by the comms radio.");
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }
    }

    private static string Loc(string? key, string fallback) =>
        string.IsNullOrEmpty(key) ? fallback : LocalizationAPI.L(key);

    private static TrainCarLivery? GetLiveryById(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    private static void DrawSection(
        string heading,
        IEnumerable<(GarageType_v2 garage, bool isDemonstrator, List<TrainCarLivery> liveries)> groups)
    {
        GUILayout.Label(heading, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Space(2);
        foreach (var (garage, isDemonstrator, liveries) in groups)
        {
            var kind = GarageReplacements.KindFor(garage, isDemonstrator);
            foreach (var livery in liveries)
            {
                DrawReplacementRow(livery, kind);
                if (kind == SlotKind.Demonstrator)
                    DrawDemonstratorExtras(garage, livery);
            }
            if (!isDemonstrator)
                DrawGarageExtras(garage);
        }
        GUILayout.Space(2);
        GUILayout.EndVertical();
        GUILayout.Space(2);
    }

    private static void DrawReplacementRow(TrainCarLivery livery, SlotKind kind)
    {
        bool pickerOpen = _openPickerFor == livery.id;
        string displayName = Loc(livery.localizationKey, livery.id);

        Main.Settings.LiveryReplacements.TryGetValue(livery.id, out var replacementId);
        string replacementLabel = string.IsNullOrEmpty(replacementId)
            ? NoReplacementLabel
            : GetLiveryById(replacementId) is TrainCarLivery rep
                ? Loc(rep.localizationKey, rep.id)
                : $"? {replacementId}";

        GUILayout.BeginHorizontal();
        GUILayout.Label(displayName, GUILayout.Width(200));
        GUILayout.Label("→", GUILayout.Width(20));
        if (GUILayout.Button($"{replacementLabel} ▼", GUILayout.Width(240)))
        {
            _openPickerFor = pickerOpen ? null : livery.id;
            _pickerScroll = Vector2.zero;
            _pickerSearch = "";
        }
        if (!string.IsNullOrEmpty(replacementId) && GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            GarageReplacements.Select(livery, null);
            if (_openPickerFor == livery.id) _openPickerFor = null;
        }
        GUILayout.EndHorizontal();

        if (pickerOpen)
            DrawReplacementPicker(livery, kind);
    }

    // Demonstrator-only quest tuning shown beneath each demonstrator row
    private static void DrawDemonstratorExtras(GarageType_v2 garage, TrainCarLivery slot)
    {
        var effectiveLoco = Main.Settings.GetReplacement(slot) ?? slot;
        var originalTender = GarageVehicles.OriginalTender(garage);

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Tender:", GUILayout.Width(80));
        bool tenderOpen = _openTenderPickerFor == slot.id;
        if (GUILayout.Button($"{TenderLabel(slot.id, originalTender)} ▼", GUILayout.Width(300)))
        {
            _openTenderPickerFor = tenderOpen ? null : slot.id;
            _tenderScroll = Vector2.zero;
            _tenderSearch = "";
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (tenderOpen)
            DrawTenderPicker(slot, originalTender);

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Parts cargo:", GUILayout.Width(80));
        bool open = _openCargoPickerFor == slot.id;
        if (GUILayout.Button($"{CargoChoiceLabel(slot.id, effectiveLoco)} ▼", GUILayout.Width(300)))
        {
            _openCargoPickerFor = open ? null : slot.id;
            _cargoScroll = Vector2.zero;
            _cargoSearch = "";
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (open)
            DrawCargoPicker(slot.id);

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Order price:", GUILayout.Width(80));
        DrawPriceField($"{slot.id}:order", Main.Settings.GetOrderPrice(slot.id),
            v => Main.Settings.SetOrderPrice(slot.id, v));
        GUILayout.Space(12);
        GUILayout.Label("Install price:", GUILayout.Width(80));
        DrawPriceField($"{slot.id}:install", Main.Settings.GetInstallPrice(slot.id),
            v => Main.Settings.SetInstallPrice(slot.id, v));
        GUILayout.Label("(blank = game default)");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    // Extra cars appended to the spawned consist beyond the default car.
    private static void DrawGarageExtras(GarageType_v2 garage)
    {
        foreach (var id in Main.Settings.GetExtraCars(garage.id).ToList())
        {
            var lv = GetLiveryById(id);
            string name = lv != null ? Loc(lv.localizationKey, lv.id) : $"? {id}";
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            GUILayout.Label($"+ {name}  [{id}]", GUILayout.Width(300));
            if (GUILayout.Button("Remove", GUILayout.Width(70)))
                Main.Settings.RemoveExtraCar(garage.id, id);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        bool open = _openExtraPickerFor == garage.id;
        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        if (GUILayout.Button(open ? "Add car ▲" : "Add car ▼", GUILayout.Width(140)))
        {
            _openExtraPickerFor = open ? null : garage.id;
            _extraScroll = Vector2.zero;
            _extraSearch = "";
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (open)
            DrawExtraPicker(garage);
        GUILayout.Space(4);
    }

    private static void DrawExtraPicker(GarageType_v2 garage)
    {
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        var newSearch = GUILayout.TextField(_extraSearch, GUILayout.ExpandWidth(true));
        if (newSearch != _extraSearch)
        {
            _extraSearch = newSearch;
            _extraScroll = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        _extraScroll = GUILayout.BeginScrollView(_extraScroll, GUILayout.Height(160));

        foreach (var candidate in _candidateLiveries!)
        {
            if (!GarageReplacements.CanAddExtraCar(candidate)) continue;

            string displayName = Loc(candidate.localizationKey, candidate.id);
            if (_extraSearch.Length > 0
                && !displayName.ToLower().Contains(_extraSearch.ToLower())
                && !candidate.id.ToLower().Contains(_extraSearch.ToLower()))
                continue;

            if (GUILayout.Button($"{displayName}  [{candidate.id}]", GUILayout.ExpandWidth(true)))
            {
                Main.Settings.AddExtraCar(garage.id, candidate.id);
                _openExtraPickerFor = null;
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static string TenderLabel(string slotId, TrainCarLivery? originalTender)
    {
        var resolved = GarageReplacements.ResolveTender(slotId, originalTender);
        bool isDefault = string.IsNullOrEmpty(Main.Settings.GetTenderId(slotId));
        if (resolved == null)
            return $"{DefaultTenderLabel} → (none)";
        string name = Loc(resolved.localizationKey, resolved.id);
        return isDefault ? $"{DefaultTenderLabel} → {name}" : name;
    }

    private static void DrawTenderPicker(TrainCarLivery slot, TrainCarLivery? originalTender)
    {
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        var newSearch = GUILayout.TextField(_tenderSearch, GUILayout.ExpandWidth(true));
        if (newSearch != _tenderSearch)
        {
            _tenderSearch = newSearch;
            _tenderScroll = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        _tenderScroll = GUILayout.BeginScrollView(_tenderScroll, GUILayout.Height(160));

        if (GUILayout.Button(DefaultTenderLabel, GUILayout.ExpandWidth(true)))
        {
            Main.Settings.SetTenderId(slot.id, null);
            _openTenderPickerFor = null;
        }

        foreach (var candidate in _candidateTenders!)
        {
            if (!GarageReplacements.CanSelectTender(slot, originalTender, candidate)) continue;

            string displayName = Loc(candidate.localizationKey, candidate.id);
            if (_tenderSearch.Length > 0
                && !displayName.ToLower().Contains(_tenderSearch.ToLower())
                && !candidate.id.ToLower().Contains(_tenderSearch.ToLower()))
                continue;

            if (GUILayout.Button($"{displayName}  [{candidate.id}]", GUILayout.ExpandWidth(true)))
            {
                Main.Settings.SetTenderId(slot.id, candidate.id);
                _openTenderPickerFor = null;
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static string CargoChoiceLabel(string slotId, TrainCarLivery effectiveLoco)
    {
        var choice = Main.Settings.GetPartsCargoId(slotId);
        if (string.IsNullOrEmpty(choice))
        {
            var suggestion = RestorationPartsCustomizer.FuzzyMatchPartsCargo(effectiveLoco);
            return suggestion != null
                ? $"{AutoCargoLabel} → {Loc(suggestion.localizationKeyFull, suggestion.id)}"
                : $"{AutoCargoLabel} → generic crate";
        }
        if (choice == RestorationPartsCustomizer.GenericCrateSentinel)
            return GenericCrateLabel;
        var cargo = RestorationPartsCustomizer.FindCargo(choice!);
        return cargo != null ? Loc(cargo.localizationKeyFull, cargo.id) : $"? {choice}";
    }

    private static void DrawCargoPicker(string slotId)
    {
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        var newSearch = GUILayout.TextField(_cargoSearch, GUILayout.ExpandWidth(true));
        if (newSearch != _cargoSearch)
        {
            _cargoSearch = newSearch;
            _cargoScroll = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        _cargoScroll = GUILayout.BeginScrollView(_cargoScroll, GUILayout.Height(160));

        if (GUILayout.Button(AutoCargoLabel, GUILayout.ExpandWidth(true)))
        {
            Main.Settings.SetPartsCargoId(slotId, null);
            _openCargoPickerFor = null;
        }
        if (GUILayout.Button(GenericCrateLabel, GUILayout.ExpandWidth(true)))
        {
            Main.Settings.SetPartsCargoId(slotId, RestorationPartsCustomizer.GenericCrateSentinel);
            _openCargoPickerFor = null;
        }

        var currentChoice = Main.Settings.GetPartsCargoId(slotId);
        foreach (var cargo in _candidateCargos!)
        {
            // Only offer cargos the parts flatcar can actually load, but never hide an existing choice.
            if (cargo.id != currentChoice && !GarageReplacements.CanBeRestorationParts(cargo)) continue;

            string displayName = Loc(cargo.localizationKeyFull, cargo.id);
            if (_cargoSearch.Length > 0
                && !displayName.ToLower().Contains(_cargoSearch.ToLower())
                && !cargo.id.ToLower().Contains(_cargoSearch.ToLower()))
                continue;

            if (GUILayout.Button($"{displayName}  [{cargo.id}]", GUILayout.ExpandWidth(true)))
            {
                Main.Settings.SetPartsCargoId(slotId, cargo.id);
                _openCargoPickerFor = null;
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static void DrawPriceField(string fieldKey, float? current, System.Action<float?> set)
    {
        if (!_priceText.TryGetValue(fieldKey, out var text))
            text = current.HasValue ? current.Value.ToString("0") : "";

        var newText = GUILayout.TextField(text, GUILayout.Width(90));
        _priceText[fieldKey] = newText;
        if (newText != text)
        {
            if (string.IsNullOrWhiteSpace(newText))
                set(null);
            else if (float.TryParse(newText, out var v) && v >= 0f)
                set(v);
        }
    }

    private static void DrawReplacementPicker(TrainCarLivery slot, SlotKind kind)
    {
        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        var newSearch = GUILayout.TextField(_pickerSearch, GUILayout.ExpandWidth(true));
        if (newSearch != _pickerSearch)
        {
            _pickerSearch = newSearch;
            _pickerScroll = Vector2.zero;
        }
        GUILayout.EndHorizontal();

        _pickerScroll = GUILayout.BeginScrollView(_pickerScroll, GUILayout.Height(160));

        if (GUILayout.Button(NoReplacementLabel, GUILayout.ExpandWidth(true)))
        {
            GarageReplacements.Select(slot, null);
            _openPickerFor = null;
        }

        foreach (var candidate in _candidateLiveries!)
        {
            if (candidate.id == slot.id) continue;

            if (!GarageReplacements.CanSelect(slot, kind, candidate)) continue;

            string displayName = Loc(candidate.localizationKey, candidate.id);
            if (_pickerSearch.Length > 0
                && !displayName.ToLower().Contains(_pickerSearch.ToLower())
                && !candidate.id.ToLower().Contains(_pickerSearch.ToLower()))
                continue;

            // Flag candidates another garage already spawns so the player knows clicking swaps them.
            string suffix = GarageReplacements.IsClaimedByOther(slot, candidate.id) ? "  ↔ swaps" : "";
            if (GUILayout.Button($"{displayName}  [{candidate.id}]{suffix}", GUILayout.ExpandWidth(true)))
            {
                GarageReplacements.Select(slot, candidate.id);
                _openPickerFor = null;
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }
}
