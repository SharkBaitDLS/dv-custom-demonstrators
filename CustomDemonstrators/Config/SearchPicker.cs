using System;
using System.Collections.Generic;
using UnityEngine;

namespace CustomDemonstrators.Config;

internal static class SearchPicker
{
    internal readonly struct Option(string label, string? id, Action select, string suffix = "")
    {
        internal readonly string Label = label;
        internal readonly string? Id = id;
        internal readonly string Suffix = suffix;
        internal readonly Action Select = select;
    }

    private sealed class State
    {
        public Vector2 Scroll;
        public string Search = "";
    }

    private const float ListHeight = 160f;

    private static readonly Dictionary<string, State> _states = [];

    internal static void Reset(string key) => _states.Remove(key);

    internal static void Draw(string key, IEnumerable<Option> options)
    {
        if (!_states.TryGetValue(key, out var state))
            _states[key] = state = new State();

        GUILayout.BeginVertical(GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(50));
        var search = GUILayout.TextField(state.Search, GUILayout.ExpandWidth(true));
        if (search != state.Search)
        {
            state.Search = search;
            state.Scroll = Vector2.zero; // a freshly filtered list scrolled halfway down reads as empty
        }
        GUILayout.EndHorizontal();

        state.Scroll = GUILayout.BeginScrollView(state.Scroll, GUILayout.Height(ListHeight));
        foreach (var option in options)
        {
            if (!Matches(option, state.Search)) continue;

            string label = option.Id == null
                ? option.Label
                : $"{option.Label}  [{option.Id}]{option.Suffix}";
            if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
                option.Select();
        }
        GUILayout.EndScrollView();

        GUILayout.EndVertical();
    }

    private static bool Matches(Option option, string search)
    {
        if (search.Length == 0 || option.Id == null) return true;
        return option.Label.ToLower().Contains(search.ToLower())
            || option.Id.ToLower().Contains(search.ToLower());
    }
}
