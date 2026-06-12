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
    private const string NoReplacementLabel = "(default — no replacement)";

    private static List<TrainCarLivery>? _candidateLiveries;
    private static string? _openPickerFor;
    private static Vector2 _pickerScroll;
    private static string _pickerSearch = "";

    private const string IntroText =
        """
        Choose a replacement for each Demonstrator and Garage spawn. The chosen stock spawns in place of the default when a new save is created.

        Has no effect on existing save games.
        """;

    internal static void OnGUI(UnityModManager.ModEntry entry)
    {
        if (Globals.G?.Types == null)
        {
            GUILayout.Label("Waiting for game data to load…");
            return;
        }

        _candidateLiveries ??= [.. Globals.G.Types.Liveries.OrderBy(l => l.id)];

        GUILayout.Label(IntroText, GUILayout.ExpandWidth(true));
        GUILayout.Space(6);

        var groups = GarageVehicles.Groups;

        DrawSection(Loc("license/museum_cs", "Museum"), groups.Where(g => g.isDemonstrator), locoOnly: true);
        GUILayout.Space(6);
        DrawSection(Loc("comms/mode_work_train", "Work Trains"), groups.Where(g => !g.isDemonstrator), locoOnly: false);
    }

    private static string Loc(string? key, string fallback) =>
        string.IsNullOrEmpty(key) ? fallback : LocalizationAPI.L(key);

    private static TrainCarLivery? GetLiveryById(string id) =>
        Globals.G?.Types?.Liveries.FirstOrDefault(l => l.id == id);

    private static void DrawSection(
        string heading,
        IEnumerable<(GarageType_v2 garage, bool isDemonstrator, List<TrainCarLivery> liveries)> groups,
        bool locoOnly)
    {
        GUILayout.Label(heading, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Space(2);
        foreach (var livery in groups.SelectMany(g => g.liveries))
            DrawReplacementRow(livery, locoOnly);
        GUILayout.Space(2);
        GUILayout.EndVertical();
        GUILayout.Space(2);
    }

    private static void DrawReplacementRow(TrainCarLivery livery, bool locoOnly)
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
            DrawReplacementPicker(livery, locoOnly);
    }

    private static void DrawReplacementPicker(TrainCarLivery slot, bool isDemonstrator)
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
            // CanSelect enforces both the demonstrator loco-only rule and that the resulting swap
            // wouldn't push a non-loco onto a demonstrator.
            if (!GarageReplacements.CanSelect(slot, isDemonstrator, candidate)) continue;
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
