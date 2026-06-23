using System.Text.RegularExpressions;

namespace Dimes.Api.Services;

/// <summary>Project-key rules in one place: the format (^[A-Z][A-Z0-9]{1,5}$ — 2–6 chars, leading
/// letter, uppercase), normalization of a user-entered key, and derivation of a unique key from a
/// project name (used as a create-time fallback and by the startup backfill).</summary>
public static class ProjectKeys
{
    private const string Pattern = "^[A-Z][A-Z0-9]{1,5}$";

    public static bool IsValid(string key) => Regex.IsMatch(key, Pattern);

    /// <summary>Trim + uppercase a user-entered key and validate it; throws <see cref="BadRequestException"/>
    /// if it doesn't match the format.</summary>
    public static string Normalize(string? key)
    {
        var k = (key ?? string.Empty).Trim().ToUpperInvariant();
        if (!IsValid(k))
        {
            throw new BadRequestException("Project key must be 2–6 letters or digits and start with a letter.");
        }
        return k;
    }

    /// <summary>Derive a valid, unique key from a project name: uppercase alphanumerics, ensure a leading
    /// letter, cap at 6, then resolve collisions against <paramref name="taken"/> by appending a numeric
    /// suffix while staying within the cap. Falls back to PRJ-based keys for degenerate names.</summary>
    public static string DeriveUnique(string name, IReadOnlySet<string> taken)
    {
        var cleaned = new string((name ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length == 0 || !char.IsLetter(cleaned[0]))
        {
            cleaned = "PRJ" + cleaned;
        }
        var baseKey = cleaned.Length >= 2 ? cleaned[..Math.Min(6, cleaned.Length)] : cleaned + "X";

        if (IsValid(baseKey) && !taken.Contains(baseKey))
        {
            return baseKey;
        }

        for (var i = 2; i < 1_000_000; i++)
        {
            var suffix = i.ToString();
            var stemLen = Math.Max(1, Math.Min(baseKey.Length, 6 - suffix.Length));
            var candidate = baseKey[..stemLen] + suffix;
            if (candidate.Length <= 6 && IsValid(candidate) && !taken.Contains(candidate))
            {
                return candidate;
            }
        }
        return "PRJ" + (Math.Abs(name?.GetHashCode() ?? 0) % 1000); // practically unreachable
    }
}
