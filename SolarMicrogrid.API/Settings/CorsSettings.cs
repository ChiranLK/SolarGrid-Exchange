/*
 * CorsSettings.cs
 * -----------------------------------------------------------------------------
 * Purpose : Defines the explicit browser origins allowed to call the API.
 * Security: Wildcards, paths, query strings, fragments, and embedded credentials
 *           are rejected before the application starts.
 * -----------------------------------------------------------------------------
 */

namespace SolarMicrogrid.API.Settings;

public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];

    public bool HasValidOrigins(bool requireHttps)
    {
        // Validate exact HTTP(S) origins so configuration cannot silently broaden browser access.
        return AllowedOrigins is not null &&
            AllowedOrigins.All(origin => IsValidOrigin(origin, requireHttps));
    }

    public string[] GetNormalizedOrigins()
    {
        // Remove harmless whitespace and duplicates before constructing the named CORS policy.
        return (AllowedOrigins ?? [])
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsValidOrigin(string origin, bool requireHttps)
    {
        // Accept an absolute origin only; paths and credentials do not belong in a CORS allow-list.
        if (string.IsNullOrWhiteSpace(origin) || origin.Contains('*', StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate(origin.Trim(), UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath)) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return !requireHttps || uri.Scheme == Uri.UriSchemeHttps;
    }
}
