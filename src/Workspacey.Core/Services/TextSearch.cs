using System.Globalization;

namespace Workspacey.Core.Services;

/// <summary>Forgiving "contains" for user search boxes.</summary>
public static class TextSearch
{
    /// <summary>
    /// Case- and accent-insensitive ("ubersicht" finds "Übersicht"). Culture-aware comparison also
    /// ignores invisible characters such as zero-width spaces, which some game titles contain.
    /// Every whitespace-separated term of <paramref name="query"/> must occur in one of the fields.
    /// </summary>
    public static bool Matches(string query, params ReadOnlySpan<string?> fields)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return true;

        var compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

        foreach (var term in terms)
        {
            var found = false;
            foreach (var field in fields)
            {
                if (!string.IsNullOrEmpty(field) && compare.IndexOf(field, term, options) >= 0)
                {
                    found = true;
                    break;
                }
            }

            if (!found) return false;
        }

        return true;
    }
}
