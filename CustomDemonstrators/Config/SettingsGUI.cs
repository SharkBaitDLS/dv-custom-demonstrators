using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Localization;
using DV.ThingTypes;
using UnityEngine;
using UnityModManagerNet;
using CustomDemonstrators.Saves;
using CustomDemonstrators.Slots;
using CustomDemonstrators.World;
#if DEBUG
using CustomDemonstrators.Diagnostics;
#endif

namespace CustomDemonstrators.Config;

internal static class SettingsGUI
{
    // TODO: localize? We do have Localization Helper in scope but I'm wary of machine translations.
    private const string NoReplacementLabel = "(default — no replacement)";
    private const string AutoCargoLabel = "Auto-detect";
    private const string GenericCrateLabel = "Generic parts crate";
    private const string DefaultTenderLabel = "Default";

    private static List<TrainCarLivery>? _candidateLiveries;
    private static List<CargoType_v2>? _candidateCargos;
    private static List<TrainCarLivery>? _candidateTenders;

    // Which picker is currently expanded. Search text and scroll position live in SearchPicker, keyed by
    // the same strings used here.
    private static string? _openPickerFor;
    private static string? _openCargoPickerFor;
    private static string? _openTenderPickerFor;
    private static string? _openExtraPickerFor;

    private static string ReplacementKey(string slotId) => $"replacement:{slotId}";
    private static string CargoKey(string slotId) => $"cargo:{slotId}";
    private static string TenderKey(string slotId) => $"tender:{slotId}";
    private static string ExtraKey(string garageId) => $"extra:{garageId}";

    // Edit buffers for the price text fields, keyed by "<slotId>:order" / "<slotId>:install"
    private static readonly Dictionary<string, string> _priceText = [];

    private const string IntroText =
        """
        Choose a replacement for each Demonstrator and Garage spawn. The chosen stock spawns in place of the default when a new save is created.

        If you load an existing save and your settings do not match what already exists in it, this mod will do nothing until you press the force respawn buttons below.
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
            GUILayout.Label("Each demonstrator respawns as a fresh wreck of your chosen replacement at a new random "
                + "location. Any partial progress towards restoration will be lost. Demonstrators "
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
            GUILayout.Label("Each opened garage respawns your chosen replacement. Cars you've already removed from an "
                + "unlocked garage are kept as owned by your player but will no longer be summonable by the comms radio.");
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
            SearchPicker.Reset(ReplacementKey(livery.id));
        }
        if (!string.IsNullOrEmpty(replacementId) && GUILayout.Button("Clear", GUILayout.Width(50)))
        {
            GarageReplacements.Select(livery, null);
            if (_openPickerFor == livery.id) _openPickerFor = null;
        }
        GUILayout.EndHorizontal();

        if (pickerOpen)
            SearchPicker.Draw(ReplacementKey(livery.id), ReplacementOptions(livery, kind));
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
            SearchPicker.Reset(TenderKey(slot.id));
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (tenderOpen)
            SearchPicker.Draw(TenderKey(slot.id), TenderOptions(slot, originalTender));

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Parts cargo:", GUILayout.Width(80));
        bool open = _openCargoPickerFor == slot.id;
        if (GUILayout.Button($"{CargoChoiceLabel(slot.id, effectiveLoco)} ▼", GUILayout.Width(300)))
        {
            _openCargoPickerFor = open ? null : slot.id;
            SearchPicker.Reset(CargoKey(slot.id));
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (open)
            SearchPicker.Draw(CargoKey(slot.id), CargoOptions(slot.id));

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
            SearchPicker.Reset(ExtraKey(garage.id));
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (open)
            SearchPicker.Draw(ExtraKey(garage.id), ExtraCarOptions(garage));
        GUILayout.Space(4);
    }

    private static IEnumerable<SearchPicker.Option> ExtraCarOptions(GarageType_v2 garage)
    {
        foreach (var candidate in _candidateLiveries!)
        {
            if (!GarageReplacements.CanAddExtraCar(candidate)) continue;

            var chosen = candidate;
            yield return new(Loc(chosen.localizationKey, chosen.id), chosen.id, () =>
            {
                Main.Settings.AddExtraCar(garage.id, chosen.id);
                _openExtraPickerFor = null;
            });
        }
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

    private static IEnumerable<SearchPicker.Option> TenderOptions(TrainCarLivery slot, TrainCarLivery? originalTender)
    {
        yield return new(DefaultTenderLabel, null, () =>
        {
            Main.Settings.SetTenderId(slot.id, null);
            _openTenderPickerFor = null;
        });

        foreach (var candidate in _candidateTenders!)
        {
            if (!GarageReplacements.CanSelectTender(slot, originalTender, candidate)) continue;

            var chosen = candidate;
            yield return new(Loc(chosen.localizationKey, chosen.id), chosen.id, () =>
            {
                Main.Settings.SetTenderId(slot.id, chosen.id);
                _openTenderPickerFor = null;
            });
        }
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

    private static IEnumerable<SearchPicker.Option> CargoOptions(string slotId)
    {
        yield return new(AutoCargoLabel, null, () =>
        {
            Main.Settings.SetPartsCargoId(slotId, null);
            _openCargoPickerFor = null;
        });
        yield return new(GenericCrateLabel, null, () =>
        {
            Main.Settings.SetPartsCargoId(slotId, RestorationPartsCustomizer.GenericCrateSentinel);
            _openCargoPickerFor = null;
        });

        var currentChoice = Main.Settings.GetPartsCargoId(slotId);
        foreach (var cargo in _candidateCargos!)
        {
            // Only offer cargos the parts flatcar can actually load, but never hide an existing choice.
            if (cargo.id != currentChoice && !GarageReplacements.CanBeRestorationParts(cargo)) continue;

            var chosen = cargo;
            yield return new(Loc(chosen.localizationKeyFull, chosen.id), chosen.id, () =>
            {
                Main.Settings.SetPartsCargoId(slotId, chosen.id);
                _openCargoPickerFor = null;
            });
        }
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

    private static IEnumerable<SearchPicker.Option> ReplacementOptions(TrainCarLivery slot, SlotKind kind)
    {
        yield return new(NoReplacementLabel, null, () =>
        {
            GarageReplacements.Select(slot, null);
            _openPickerFor = null;
        });

        foreach (var candidate in _candidateLiveries!)
        {
            if (candidate.id == slot.id) continue;
            if (!GarageReplacements.CanSelect(slot, kind, candidate)) continue;

            var chosen = candidate;
            yield return new(
                Loc(chosen.localizationKey, chosen.id),
                chosen.id,
                () =>
                {
                    GarageReplacements.Select(slot, chosen.id);
                    _openPickerFor = null;
                },
                // Flag candidates another garage already spawns so the player knows clicking swaps them.
                GarageReplacements.IsClaimedByOther(slot, chosen.id) ? "  ↔ swaps" : "");
        }
    }
}
