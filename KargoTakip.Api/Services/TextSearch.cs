using System.Text;

namespace KargoTakip.Api.Services;

/// <summary>
/// Makes searching forgiving for Turkish text: "İstanbul", "istanbul" and "ISTANBUL" all match,
/// and so do "Çankırı" and "cankiri". Done with an explicit letter map (instead of Unicode normalization)
/// so it behaves the same on every machine and culture setting.
/// </summary>
public static class TextSearch
{
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                'ç' or 'Ç' => 'c',
                'ğ' or 'Ğ' => 'g',
                'ı' or 'İ' or 'I' or 'î' or 'Î' => 'i',
                'ö' or 'Ö' => 'o',
                'ş' or 'Ş' => 's',
                'ü' or 'Ü' or 'û' or 'Û' => 'u',
                'â' or 'Â' => 'a',
                _ => char.ToLowerInvariant(ch)
            });
        }
        return sb.ToString();
    }

    /// <summary>True when every word of <paramref name="term"/> appears in at least one of the fields.</summary>
    public static bool MatchesAll(string term, params string?[] fields)
    {
        var haystack = fields.Select(Fold).ToArray();
        return Fold(term)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Any(f => f.Contains(word, StringComparison.Ordinal)));
    }
}
