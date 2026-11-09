using System;
using I2.Loc;

namespace CustomDemonstrators.Localization;

internal static class I2Terms
{
    // The first loaded source that defines the term, along with the term itself.
    internal static (LanguageSourceData? Source, TermData? Term) Find(string term)
    {
        foreach (var s in LocalizationManager.Sources)
        {
            if (s == null) continue;
            var t = s.GetTermData(term);
            if (t != null) return (s, t);
        }
        return (null, null);
    }

    // The term's text in one language, by I2 language code, from the first source that has that language.
    internal static string? ValueFor(string term, string code)
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
