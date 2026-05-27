using System.Text.RegularExpressions;

namespace AiBot.Core;

public static partial class TextTokenizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "і", "й", "та", "або", "у", "в", "на", "по", "для", "до", "з", "зі", "про",
        "що", "як", "які", "який", "це", "мені", "треба", "потрібно", "будь", "ласка"
    };

    public static IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return WordRegex()
            .Matches(text.ToLowerInvariant())
            .Select(match => NormalizeToken(match.Value))
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .ToArray();
    }

    private static string NormalizeToken(string token)
    {
        return token
            .Replace("ё", "е", StringComparison.Ordinal)
            .Replace("’", "'", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[\p{L}\p{Nd}#\+']+", RegexOptions.Compiled)]
    private static partial Regex WordRegex();
}
