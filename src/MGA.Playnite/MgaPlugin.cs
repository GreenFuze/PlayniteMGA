using MGA.Playnite.Api;
using MGA.Playnite.GameImport;
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
    /// Slice 1 imports the library. Installing and launching content is slice 2;
    /// until then games import as not-installed, which is honest — the plugin
    /// genuinely cannot put bytes on this machine yet.
    /// </summary>
    public sealed class MgaPlugin : LibraryPlugin
    {
        public static readonly Guid PluginId = Guid.Parse("02db01ef-50e6-49cd-8460-9ee06d324b74");

        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly ProtectedTokenStore tokenStore;
        private readonly MgaGameMetadataFactory metadataFactory;
        private readonly MgaLibraryReconciler reconciler;

        public MgaPlugin(IPlayniteAPI playniteApi)
            : base(playniteApi ?? throw new ArgumentNullException(nameof(playniteApi)))
        {
            Properties = new LibraryPluginProperties { HasSettings = true };

            tokenStore = new ProtectedTokenStore(
                Path.Combine(GetPluginUserDataPath(), "mga-access.key"),
                PluginId);
            metadataFactory = new MgaGameMetadataFactory(Name);
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
                    AttachArtwork(metadata, game, client, cancelToken);
                }
                imported.Add(metadata);
            }

            if (skipped > 0)
            {
                Logger.Warn("Skipped " + skipped + " MGA games that arrived without an id; they cannot be matched on a later refresh.");
            }
            return imported;
        }

        /// <summary>
        /// Artwork is best-effort by design. A cover that will not download is
        /// a cosmetic loss, and failing the whole import over one would turn a
        /// missing thumbnail into a library that will not sync.
        /// </summary>
        private void AttachArtwork(GameMetadata metadata, GameDto game, MgaApiClient client, CancellationToken cancelToken)
        {
            var coverId = MgaGameMetadataFactory.CoverAssetId(game);
            if (coverId > 0)
            {
                var bytes = TryDownload(client, coverId, cancelToken);
                if (bytes != null)
                {
                    metadata.CoverImage = new MetadataFile("mga-cover-" + coverId, bytes);
                }
            }

            var backgroundId = MgaGameMetadataFactory.BackgroundAssetId(game);
            if (backgroundId > 0)
            {
                var bytes = TryDownload(client, backgroundId, cancelToken);
                if (bytes != null)
                {
                    metadata.BackgroundImage = new MetadataFile("mga-background-" + backgroundId, bytes);
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
