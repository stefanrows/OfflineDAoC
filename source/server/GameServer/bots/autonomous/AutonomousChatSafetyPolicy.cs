using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>Guards authored autonomous chat and names against identity slurs.</summary>
public static partial class AutonomousChatSafetyPolicy
{
    private static readonly HashSet<string> BlockedTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "nigger", "nigga", "faggot", "fag", "kike", "chink", "gook", "spic", "wetback",
        "beaner", "coon", "paki", "raghead", "towelhead", "retard", "tranny", "dyke",
    };

    public static bool ContainsBlockedTerm(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string normalized = text.ToLowerInvariant()
            .Replace('0', 'o').Replace('1', 'i').Replace('3', 'e').Replace('4', 'a')
            .Replace('5', 's').Replace('7', 't').Replace('@', 'a').Replace('$', 's');
        return WordRegex().Matches(normalized).Cast<Match>()
            .Select(match => match.Value)
            .Any(word => BlockedTerms.Contains(word));
    }

    public static string Sanitize(string text, string fallback = "Forget that; back to the hunt.") =>
        ContainsBlockedTerm(text) ? fallback : (text ?? string.Empty).Trim();

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
