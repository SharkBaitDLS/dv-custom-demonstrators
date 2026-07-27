using System;
using System.Collections.Generic;

namespace CustomDemonstrators.Saves;

// A string map kept in the save as a pair of parallel string arrays, which is as close to a dictionary as
// SaveGameData gets. Holds the per-slot facts a save has to be able to answer on its own — where a slot ended
// up, which parts cargo it was baked with — for the loads where the settings that made those choices aren't
// the ones this save was baked from, or aren't installed at all.
internal sealed class SavedMap(string keysKey, string valuesKey)
{
    private Dictionary<string, string>? _entries;

    internal void Reset() => _entries = null;

    internal string? Get(string id) => Entries().TryGetValue(id, out var value) ? value : null;

    internal IEnumerable<string> Values => Entries().Values;

    // A null value drops the entry.
    internal void Set(string id, string? value)
    {
        var entries = Entries();
        if (value == null)
        {
            if (entries.Remove(id)) Persist();
            return;
        }

        if (entries.TryGetValue(id, out var current) && current == value) return;
        entries[id] = value;
        Persist();
    }

    private Dictionary<string, string> Entries()
    {
        if (_entries != null) return _entries;

        _entries = [];
        var data = SaveState.Data();
        var keys = data?.GetStringArray(keysKey);
        var values = data?.GetStringArray(valuesKey);
        if (keys == null || values == null) return _entries;

        for (int i = 0; i < Math.Min(keys.Length, values.Length); i++) _entries[keys[i]] = values[i];
        return _entries;
    }

    private void Persist()
    {
        var data = SaveState.Data();
        if (data == null || _entries == null) return;
        data.SetStringArray(keysKey, [.. _entries.Keys]);
        data.SetStringArray(valuesKey, [.. _entries.Values]);
    }
}
