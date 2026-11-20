using System.Globalization;

namespace Flyleaf.Controls.WPF;

internal sealed record FlyleafLanguageChoice(Language Value, string DisplayName, string SearchText);

/// <summary>Uses the same cultures and ISO aliases as FlyleafLib.Language, without a second language model.</summary>
internal static class FlyleafLanguageSuggestions
{
    static readonly Lazy<FlyleafLanguageChoice[]> choices = new(Create);

    internal static IReadOnlyList<FlyleafLanguageChoice> Find(string query, IEnumerable<Language> selected, int maximum = 16)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var existing = selected.ToArray();
        return choices.Value.Where(c => !existing.Any(language => language == c.Value) && c.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Value.CultureName?.Equals(query, StringComparison.OrdinalIgnoreCase) == true || c.Value.IdSubLanguage.Equals(query, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.Value.Culture?.IsNeutralCulture == true)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase).Take(maximum).ToArray();
    }

    static FlyleafLanguageChoice[] Create()
    {
        var result = new List<FlyleafLanguageChoice>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures).Where(c => c.Name.Length > 0))
        {
            var value = FlyleafLib.Language.Get(culture);
            var aliases = FlyleafLib.Language.ISO639_2T_TO_2B.GetValueOrDefault(culture.ThreeLetterISOLanguageName) ?? "";
            if (culture.Name.Equals("pt-BR", StringComparison.OrdinalIgnoreCase)) aliases += " pob";
            if (culture.Name.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase)) aliases += " zht";
            if (culture.ThreeLetterISOLanguageName == "srp") aliases += " scc";
            if (culture.ThreeLetterISOLanguageName == "fil") aliases += " tgl";
            var search = $"{culture.Name} {culture.EnglishName} {culture.NativeName} {culture.TwoLetterISOLanguageName} {culture.ThreeLetterISOLanguageName} {aliases}";
            result.Add(new(value, $"{culture.EnglishName} · {culture.Name} / {culture.ThreeLetterISOLanguageName}", search));
        }
        result.Add(new(FlyleafLib.Language.Unknown, "Unknown language · und", "unknown und undetermined"));
        return result.ToArray();
    }
}
