using MGA.Playnite.Api;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MGA.Playnite.Install
{
    /// <summary>
    /// How a game can be got hold of: from a store, or as files MGA can serve.
    ///
    /// This distinction is the whole of the install design. MGA holds the files
    /// for a drive or a share and can hand them over, so the plugin downloads
    /// those itself. It holds nothing of the sort for a Steam or Xbox title —
    /// only the knowledge that the account has it — so the only honest action
    /// is to send the user to the store that does own it. Offering "Install"
    /// for a Game Pass title would promise something no part of this system can
    /// deliver.
    /// </summary>
    internal enum ContentRouteKind
    {
        None,
        Store,
        Files
    }

    internal sealed class ContentRoute
    {
        private ContentRoute(ContentRouteKind kind, string copyId, string storeKind, string storeId, string storeUrl)
        {
            Kind = kind;
            CopyId = copyId;
            StoreKind = storeKind;
            StoreId = storeId;
            StoreUrl = storeUrl;
        }

        public ContentRouteKind Kind { get; }

        /// <summary>The source game whose files MGA can serve, for a Files route.</summary>
        public string CopyId { get; }

        public string StoreKind { get; }

        public string StoreId { get; }

        public string StoreUrl { get; }

        public static readonly ContentRoute None =
            new ContentRoute(ContentRouteKind.None, null, null, null, null);

        // MGA's plugin ids for connections that read a filesystem. These are the
        // ones whose bytes MGA can actually serve; everything else reports what
        // an account holds rather than what is on a disk.
        private static readonly HashSet<string> FileBackedPlugins = new HashSet<string>(
            new[] { "game-source-google-drive", "game-source-google-drive-desktop", "game-source-smb", "game-source-local" },
            StringComparer.OrdinalIgnoreCase);

        private const string SteamPlugin = "game-source-steam";
        private const string XboxPlugin = "game-source-xbox";

        /// <summary>
        /// Chooses the route for a game.
        ///
        /// Files win over a store when both are possible: if MGA can hand over
        /// the bytes, downloading them is the thing the user asked this plugin
        /// for. A game present on both a drive and Steam is still installable
        /// from the drive.
        /// </summary>
        public static ContentRoute For(GameDto game)
        {
            if (game == null)
            {
                return None;
            }

            var sources = game.SourceGames ?? new List<SourceGameDto>();

            var fileBacked = sources.FirstOrDefault(source =>
                source != null &&
                !string.IsNullOrWhiteSpace(source.Id) &&
                !string.IsNullOrWhiteSpace(source.PluginId) &&
                FileBackedPlugins.Contains(source.PluginId) &&
                IsPresent(source));
            if (fileBacked != null)
            {
                return new ContentRoute(ContentRouteKind.Files, fileBacked.Id.Trim(), null, null, null);
            }

            var steam = sources.FirstOrDefault(source =>
                source != null &&
                string.Equals(source.PluginId, SteamPlugin, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(source.ExternalId));
            if (steam != null)
            {
                return new ContentRoute(
                    ContentRouteKind.Store, null, "steam", steam.ExternalId.Trim(),
                    "steam://rungameid/" + steam.ExternalId.Trim());
            }

            var xbox = sources.Any(source =>
                source != null && string.Equals(source.PluginId, XboxPlugin, StringComparison.OrdinalIgnoreCase));
            if (xbox && !string.IsNullOrWhiteSpace(game.StoreProductId))
            {
                return new ContentRoute(
                    ContentRouteKind.Store, null, "xbox", game.StoreProductId.Trim(),
                    "ms-windows-store://pdp/?ProductId=" + game.StoreProductId.Trim());
            }

            return None;
        }

        /// <summary>
        /// A source that the last scan did not find cannot be downloaded from,
        /// whatever its record says.
        /// </summary>
        private static bool IsPresent(SourceGameDto source)
        {
            return string.IsNullOrWhiteSpace(source.Status) ||
                   string.Equals(source.Status, "found", StringComparison.OrdinalIgnoreCase);
        }
    }
}
