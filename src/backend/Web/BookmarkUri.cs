// C# 12 : quelles adresses de favoris peuvent être confiées au navigateur. Fonction pure, couverte par tests/SecurityHardening.
using System;

namespace PanneauxGlass;

internal static class BookmarkUri
{
    // Seuls les http/https absolus sont acceptés. file: (un hôte UNC dans file://server/share fait s'authentifier Windows auprès de ce
    // serveur), javascript:, data:, ms-settings: et similaires sont refusés.
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
