using MGA.Playnite.Api;
using MGA.Playnite.GameImport;
using MGA.Playnite.Install;
using MGA.Playnite.Settings;
using MGA.Playnite.Storage;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Controls;

namespace MGA.Playnite
{
    /// <summary>
    /// MyGamesAnywhere as a Playnite library.
    ///
    /// MGA is headless: it owns the library and every provider connection, and
    /// this plugin is one of its faces. Everything it knows comes from the
    /// scoped frontend API, so the games Playnite shows and the games the MGA
    /// console shows cannot drift apart.
    ///
    /// Games whose files MGA holds — a drive, a share, a local folder — can be
    /// downloaded through this plugin. Games that belong to a store are handed
    /// to that store instead, because it owns installing and launching them and
    /// MGA holds nothing of theirs but the knowledge that the account has them.
    /// </summary>
    public sealed class MgaPlugin : LibraryPlugin
    {
        public static readonly Guid PluginId = Guid.Parse("02db01ef-50e6-49cd-8460-9ee06d324b74");

        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly ProtectedTokenStore tokenStore;
        private readonly MgaGameMetadataFactory metadataFactory;
        private readonly MgaLibraryReconciler reconciler;
        private readonly InstalledCopyStore installRecords;
        private readonly InstallableCopyIndex installableCopyIds;

        public MgaPlugin(IPlayniteAPI playniteApi)
            : base(playniteApi ?? throw new ArgumentNullException(nameof(playniteApi)))
        {
            Properties = new LibraryPluginProperties { HasSettings = true };

            tokenStore = new ProtectedTokenStore(
                Path.Combine(GetPluginUserDataPath(), "mga-access.key"),
                PluginId);
            metadataFactory = new MgaGameMetadataFactory(Name);
            installRecords = new InstalledCopyStore(Path.Combine(GetPluginUserDataPath(), "installed"));
            installableCopyIds = new InstallableCopyIndex(Path.Combine(GetPluginUserDataPath(), "installable.json"));
            reconciler = new MgaLibraryReconciler(playniteApi.Database, new MgaLibraryReconciliationPlanner());
            SettingsViewModel = new MgaSettingsViewModel(this, tokenStore);
        }

        public override Guid Id
        {
            get { return PluginId; }
        }

        public override string Name
        {
            get { return "MyGamesAnywhere"; }
        }

        public override string LibraryIcon
        {
            get
            {
                return Path.Combine(
                    Path.GetDirectoryName(typeof(MgaPlugin).Assembly.Location),
                    "Resources",
                    "mga.png");
            }
        }

        public MgaSettingsViewModel SettingsViewModel { get; }

        /// <summary>
        /// Install is offered only for games whose files MGA can actually hand
        /// over. A Steam or Xbox title has no install action here at all: its
        /// store owns installation, and an Install button that cannot install
        /// is worse than no button.
        /// </summary>
        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args?.Game == null || args.Game.PluginId != PluginId)
            {
                yield break;
            }

            var copyId = installableCopyIds.Get(args.Game.GameId);
            if (string.IsNullOrWhiteSpace(copyId))
            {
                yield break;
            }

            yield return new MgaInstallController(args.Game, PlayniteApi, CreateInstaller, copyId);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args?.Game == null || args.Game.PluginId != PluginId)
            {
                yield break;
            }

            yield return new MgaUninstallController(args.Game, PlayniteApi, new MgaContentUninstaller(installRecords));
        }

        /// <summary>
        /// Built per install rather than held, because it needs the current
        /// server address and access key, and both can change in settings
        /// between one download and the next.
        /// </summary>
        private MgaContentInstaller CreateInstaller()
        {
            var settings = SettingsViewModel.Settings;
            var token = tokenStore.Load();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "No MyGamesAnywhere access key is stored. Open its settings and paste a key from the MGA console.");
            }

            var client = new MgaApiClient(settings.ServerUrl, token);
            return new MgaContentInstaller(
                new MgaContentClient(client),
                installRecords,
                settings.EffectiveInstallRoot(GetPluginUserDataPath()));
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return SettingsViewModel;
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            return new MgaSettingsView();
        }

        internal void OnSettingsChanged()
        {
            // Nothing cached between refreshes yet. The hook exists so that a
            // later slice which does cache has one obvious place to invalidate.
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var cancelToken = args == null ? CancellationToken.None : args.CancelToken;
            var settings = SettingsViewModel.Settings;

            if (string.IsNullOrWhiteSpace(settings.ServerUrl))
            {
                throw new Exception(
                    "MyGamesAnywhere is not configured yet. Open its settings and enter your server address and access key.");
            }

            var token = tokenStore.Load();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new Exception(
                    "No MyGamesAnywhere access key is stored. Open its settings and paste a key from the MGA console (System → Issue client).");
            }

            List<GameDto> games;
            NegotiatedCapabilities capabilities;
            using (var client = new MgaApiClient(settings.ServerUrl, token))
            {
                capabilities = NegotiatedCapabilities.From(
                    client.GetCapabilitiesAsync(cancelToken).GetAwaiter().GetResult());

                var blocked = capabilities.BlockingReason();
                if (blocked != null)
                {
                    throw new Exception(blocked);
                }

                games = client
                    .GetAllGamesAsync(settings.PageSize, settings.HideLapsedSubscriptionGames, cancelToken)
                    .GetAwaiter().GetResult();

                var imported = Project(games, client, capabilities, settings.DownloadArtwork, cancelToken);

                // Reconcile only after a complete, successful sync. A partial or
                // failed listing must never be read as "the library shrank" —
                // that is the difference between tidying up and deleting
                // someone's collection.
                cancelToken.ThrowIfCancellationRequested();
                RecordInstallableCopies(games);
                ReconcileQuietly(games);
                return imported;
            }
        }

        private List<GameMetadata> Project(
            List<GameDto> games,
            MgaApiClient client,
            NegotiatedCapabilities capabilities,
            bool wantArtwork,
            CancellationToken cancelToken)
        {
            var artworkAvailable = wantArtwork && capabilities.Has(NegotiatedCapabilities.MetadataMedia);
            if (wantArtwork && !artworkAvailable)
            {
                Logger.Warn("MGA access key cannot download artwork; importing games without it. Missing scope: " +
                            (capabilities.MissingScopeFor(NegotiatedCapabilities.MetadataMedia) ?? "unknown"));
            }

            var imported = new List<GameMetadata>(games.Count);
            var skipped = 0;
            var artworkWanted = 0;
            var artworkFetched = 0;
            foreach (var game in games)
            {
                cancelToken.ThrowIfCancellationRequested();

                var metadata = metadataFactory.Create(game);
                if (metadata == null)
                {
                    skipped++;
                    continue;
                }
                if (artworkAvailable)
                {
                    AttachArtwork(metadata, game, client, cancelToken, ref artworkWanted, ref artworkFetched);
                }
                imported.Add(metadata);
            }

            if (skipped > 0)
            {
                Logger.Warn("Skipped " + skipped + " MGA games that arrived without an id; they cannot be matched on a later refresh.");
            }
            if (artworkWanted > artworkFetched)
            {
                // Not an error, and worth saying plainly: MGA holds the record
                // but not the file, so it pointed at the original provider and
                // this plugin declined to go there. Asking has queued a repair
                // on the server, so the next sync usually brings the artwork.
                Logger.Info("MGA supplied " + artworkFetched + " of " + artworkWanted +
                            " artwork images. The rest are not in MGA's cache yet; asking for them has queued a redownload, so try another library update later.");
            }
            return imported;
        }

        /// <summary>
        /// Artwork is best-effort by design. A cover that will not download is
        /// a cosmetic loss, and failing the whole import over one would turn a
        /// missing thumbnail into a library that will not sync.
        /// </summary>
        private void AttachArtwork(
            GameMetadata metadata,
            GameDto game,
            MgaApiClient client,
            CancellationToken cancelToken,
            ref int wanted,
            ref int fetched)
        {
            var coverId = MgaGameMetadataFactory.CoverAssetId(game);
            if (coverId > 0)
            {
                wanted++;
                var bytes = TryDownload(client, coverId, cancelToken);
                if (bytes != null)
                {
                    metadata.CoverImage = new MetadataFile("mga-cover-" + coverId, bytes);
                    fetched++;
                }
            }

            var backgroundId = MgaGameMetadataFactory.BackgroundAssetId(game);
            if (backgroundId > 0)
            {
                wanted++;
                var bytes = TryDownload(client, backgroundId, cancelToken);
                if (bytes != null)
                {
                    metadata.BackgroundImage = new MetadataFile("mga-background-" + backgroundId, bytes);
                    fetched++;
                }
            }
        }

        private byte[] TryDownload(MgaApiClient client, int assetId, CancellationToken cancelToken)
        {
            try
            {
                return client.GetMediaAsync(assetId, cancelToken).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not download MGA artwork asset " + assetId + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Remembers which games MGA can serve files for, so Playnite can be
        /// asked whether a game has an install action without a network call
        /// behind every menu.
        /// </summary>
        private void RecordInstallableCopies(List<GameDto> games)
        {
            try
            {
                var installable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var game in games)
                {
                    var gameId = MgaGameIdentity.ToGameId(game == null ? null : game.Id);
                    if (gameId == null)
                    {
                        continue;
                    }
                    var route = ContentRoute.For(game);
                    if (route.Kind == ContentRouteKind.Files)
                    {
                        installable[gameId] = route.CopyId;
                    }
                }
                installableCopyIds.Replace(installable);
                Logger.Info("MGA can supply files for " + installable.Count + " of " + games.Count + " games; the rest belong to a store.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "MGA could not record which games are installable; install actions may be missing until the next library update.");
            }
        }

        /// <summary>
        /// Tidying up must not be able to fail an otherwise good import: the
        /// games have already been read successfully, and refusing to return
        /// them because a tag could not be written would be a worse outcome
        /// than a stale record.
        /// </summary>
        private void ReconcileQuietly(List<GameDto> games)
        {
            try
            {
                var presentIds = new HashSet<string>(
                    games
                        .Select(game => MgaGameIdentity.ToCanonicalGameId(game == null ? null : game.Id))
                        .Where(id => id != null),
                    StringComparer.OrdinalIgnoreCase);

                var result = reconciler.Reconcile(PluginId, presentIds);
                if (result.Removed > 0 || result.MarkedUnavailable > 0 || result.MarkedAvailable > 0)
                {
                    Logger.Info("MGA reconciliation: removed " + result.Removed +
                                ", marked unavailable " + result.MarkedUnavailable +
                                ", restored " + result.MarkedAvailable + ".");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "MGA library reconciliation failed; imported games are unaffected.");
            }
        }
    }
}
