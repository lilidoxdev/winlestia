// Pure decisions for messaging views (no Store, no WebView2): linked into tests/SocialPolicy.
using System;

namespace PanneauxGlass.Social;

static class SocialPolicy
{
    internal static readonly TimeSpan IdleRelease = TimeSpan.FromSeconds(60);

    // Création au démarrage : uniquement si l'utilisateur garde les messageries actives en arrière-plan.
    internal static bool ShouldPreload(bool background, bool enabled, bool connected) =>
        background && enabled && connected;

    // Libération d'une vue cachée depuis longtemps quand l'arrière-plan n'est pas demandé.
    internal static bool ShouldRelease(
        bool visible,
        DateTime hiddenSinceUtc,
        DateTime nowUtc,
        bool background,
        bool connected
    ) =>
        connected
        && !background
        && !visible
        && hiddenSinceUtc != DateTime.MinValue
        && nowUtc - hiddenSinceUtc > IdleRelease;
}
