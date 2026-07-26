using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Localization;
using DV.ThingTypes;
using UnityEngine;
using UnityModManagerNet;
using CustomDemonstrators.Slots;
using CustomDemonstrators.Saves;
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
    private static bool _openAdditionalPicker;

    private static string ReplacementKey(string slotId) => $"replacement:{slotId}";
    private static string CargoKey(string slotId) => $"cargo:{slotId}";
    private static string TenderKey(string slotId) => $"tender:{slotId}";
    private static string ExtraKey(string garageId) => $"extra:{garageId}";
    private const string AdditionalKey = "additional";

    // Edit buffers for the price text fields, keyed by "<slotId>:order" / "<slotId>:install"
    private static readonly Dictionary<string, string> _priceText = [];

    private const string IntroText =
        """
        Choose a replacement for each Demonstrator and Garage spawn or add additional Demonstrators. The chosen stock spawns in place of the default when a new save is created.

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
        _candidateCargos ??= [.. Globals.G.Types.cargos
            .Where(c => c != null && !DemonstratorSlotFactory.IsSlotCargo(c)).OrderBy(c => c.id)];
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
        DrawAdditionalSlots();
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
                {
                    DrawDemonstratorExtras(livery.id, Main.Settings.GetReplacement(livery) ?? livery,
                        GarageVehicles.OriginalTender(garage));
                }
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

    // Quest tuning shown beneath a demonstrator row. Shared by the game's slots and the ones this mod
    // adds: both key their settings off a slot id, which for an added slot is simply its loco.
    private static void DrawDemonstratorExtras(string slotId, TrainCarLivery effectiveLoco, TrainCarLivery? originalTender)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Tender:", GUILayout.Width(80));
        bool tenderOpen = _openTenderPickerFor == slotId;
        if (GUILayout.Button($"{TenderLabel(slotId, originalTender)} ▼", GUILayout.Width(300)))
        {
            _openTenderPickerFor = tenderOpen ? null : slotId;
            SearchPicker.Reset(TenderKey(slotId));
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (tenderOpen)
            SearchPicker.Draw(TenderKey(slotId), TenderOptions(slotId, originalTender));

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Parts cargo:", GUILayout.Width(80));
        bool open = _openCargoPickerFor == slotId;
        if (GUILayout.Button($"{CargoChoiceLabel(slotId, effectiveLoco)} ▼", GUILayout.Width(300)))
        {
            _openCargoPickerFor = open ? null : slotId;
            SearchPicker.Reset(CargoKey(slotId));
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (open)
            SearchPicker.Draw(CargoKey(slotId), CargoOptions(slotId));

        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Order price:", GUILayout.Width(80));
        DrawPriceField($"{slotId}:order", Main.Settings.GetOrderPrice(slotId),
            v => Main.Settings.SetOrderPrice(slotId, v));
        GUILayout.Space(12);
        GUILayout.Label("Install price:", GUILayout.Width(80));
        DrawPriceField($"{slotId}:install", Main.Settings.GetInstallPrice(slotId),
            v => Main.Settings.SetInstallPrice(slotId, v));
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

    private const string AdditionalSlotsText =
        "Extra museum demonstrators, each restored and unlocked on its own. Changes apply when the save is "
        + "next loaded, or straight away with the force respawn button above.\n\n"
        + "Each slot is given one of the museum's empty roundhouse stalls, which caps how many you can add.";

    private static void DrawAdditionalSlots()
    {
        GUILayout.Label("Additional demonstrators", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label(AdditionalSlotsText);
        GUILayout.Space(4);

        foreach (var slot in Main.Settings.AdditionalSlots.ToList())
        {
            var loco = GetLiveryById(slot.LocoId);
            string name = loco != null ? Loc(loco.localizationKey, loco.id) : $"? {slot.LocoId}";

            GUILayout.BeginHorizontal();
            GUILayout.Label(name, GUILayout.Width(220));
            GUILayout.Label($"[{slot.LocoId}]", GUILayout.Width(220));
            if (GUILayout.Button("Remove", GUILayout.Width(70)))
            {
                Main.Settings.RemoveAdditionalSlot(slot.LocoId);
                continue;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawHomeRow(slot);
            if (loco != null)
                DrawDemonstratorExtras(slot.LocoId, loco, null);
        }

        DrawAddSlotRow();

        GUILayout.EndVertical();
    }

    private static void DrawAddSlotRow()
    {
        int free = MuseumStalls.FreeCount();
        bool unlocked = free > 0 || Main.Settings.OverrideSlotLimit;

        GUILayout.BeginHorizontal();
        bool open = _openAdditionalPicker;
        GUI.enabled = unlocked;
        if (GUILayout.Button(open ? "Add demonstrator ▲" : "Add demonstrator ▼", GUILayout.Width(180)))
        {
            _openAdditionalPicker = !open;
            SearchPicker.Reset(AdditionalKey);
        }
        GUI.enabled = true;
        GUILayout.Label($"{free} of {MuseumStalls.All.Count} museum stall(s) free", GUILayout.Width(220));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Only worth showing once it's the thing standing in the way, or while it's still switched on.
        if (free == 0 || Main.Settings.OverrideSlotLimit)
        {
            GUILayout.BeginHorizontal();
            bool overridden = GUILayout.Toggle(Main.Settings.OverrideSlotLimit,
                " Override the stall limit", GUILayout.Width(200));
            if (overridden != Main.Settings.OverrideSlotLimit)
            {
                Main.Settings.OverrideSlotLimit = overridden;
                if (!overridden) _openAdditionalPicker = false;
            }
            GUILayout.Label("Slots added past the museum's free stalls have to be given a track by hand, "
                + "standing on it in game. Be aware that this can cause all manner of bugs if the track you "
                + "assign the demonstrator to has other spawns assigned to it.");
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        if (_openAdditionalPicker && unlocked)
            SearchPicker.Draw(AdditionalKey, AdditionalSlotOptions());
    }

    // Which roundhouse stall the slot got. One out of the mod's own list is read-only — there is nothing
    // to decide. A slot added past that list has no stall to show, so it gets the placement controls.
    private static void DrawHomeRow(Settings.AdditionalSlot slot)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Space(20);
        GUILayout.Label("Stall:", GUILayout.Width(80));
        GUILayout.Label(StallLabel(slot), GUILayout.Width(300));

        if (string.IsNullOrEmpty(slot.Stall))
            DrawPlacementControls(slot);

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private static void DrawPlacementControls(Settings.AdditionalSlot slot)
    {
        // Placement is relative to a museum marker that only exists in a loaded world, and the player has
        // to be standing on the track in any case.
        var anchor = DemonstratorSlotFactory.Template()?.garageSpawner?.locoSpawnPoint?.transform;
        var player = PlayerManager.PlayerTransform;
        if (anchor == null || player == null)
        {
            GUILayout.Label("(load a save and stand on a track to place)", GUILayout.Width(250));
            return;
        }

        if (GUILayout.Button("Set to where I'm standing", GUILayout.Width(190)))
        {
            Main.Settings.SetAdditionalSlotHome(slot.LocoId,
                anchor.InverseTransformPoint(player.position),
                Quaternion.Inverse(anchor.rotation).eulerAngles.y + player.eulerAngles.y);
        }

        if (slot.Home.HasValue && GUILayout.Button("Clear", GUILayout.Width(50)))
            Main.Settings.SetAdditionalSlotHome(slot.LocoId, null, 0f);
    }

    private static string StallLabel(Settings.AdditionalSlot slot)
    {
        if (!string.IsNullOrEmpty(slot.Stall)) return slot.Stall!;
        return slot.Home.HasValue
            ? "Placed by hand"
            : "Not placed — shares another demonstrator's stall";
    }

    private static IEnumerable<SearchPicker.Option> AdditionalSlotOptions()
    {
        foreach (var candidate in _candidateLiveries!)
        {
            if (!GarageReplacements.CanBeAdditionalSlot(candidate)) continue;

            var chosen = candidate;
            yield return new(Loc(chosen.localizationKey, chosen.id), chosen.id, () =>
            {
                Main.Settings.AddAdditionalSlot(chosen.id);
                _openAdditionalPicker = false;
            });
        }
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

    private static IEnumerable<SearchPicker.Option> TenderOptions(string slotId, TrainCarLivery? originalTender)
    {
        yield return new(DefaultTenderLabel, null, () =>
        {
            Main.Settings.SetTenderId(slotId, null);
            _openTenderPickerFor = null;
        });

        foreach (var candidate in _candidateTenders!)
        {
            if (!GarageReplacements.CanSelectTender(slotId, originalTender, candidate)) continue;

            var chosen = candidate;
            yield return new(Loc(chosen.localizationKey, chosen.id), chosen.id, () =>
            {
                Main.Settings.SetTenderId(slotId, chosen.id);
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
