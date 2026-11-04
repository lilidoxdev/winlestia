// C# 12: which bookmark addresses may be handed to the browser. Pure function, covered by tests/SecurityHardening.
using System;

namespace PanneauxGlass.Tools;

internal static class BookmarkUri
{
    // Only absolute http/https. file: (a UNC host in file://server/share makes Windows authenticate to that server), javascript:, data:, ms-settings: and the like are refused.
    internal static bool IsOpenable(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        uri = parsed;
        return true;
    }
}
