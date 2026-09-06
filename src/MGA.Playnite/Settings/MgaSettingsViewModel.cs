using MGA.Playnite.Api;
using MGA.Playnite.Storage;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Threading;

namespace MGA.Playnite.Settings
{
    /// <summary>
    /// The settings screen's behaviour.
    ///
    /// The access key is handled apart from the rest of the settings on
    /// purpose. Playnite persists <see cref="MgaSettings"/> as plain JSON, so
    /// the key goes to <see cref="ProtectedTokenStore"/> instead, and is only
    /// ever held here as text the user just typed and has not yet saved.
    /// </summary>
    public sealed class MgaSettingsViewModel : ObservableObject, ISettings
    {
        private readonly MgaPlugin plugin;
        private readonly ProtectedTokenStore tokenStore;

        private MgaSettings editingClone;
        private MgaSettings settings;
        private string pendingAccessKey = string.Empty;
        private bool pendingDisconnect;
        private bool busy;
        private string connectionStatus;

        // Internal because it takes the token store, which is internal. The
        // class itself is public only because Playnite binds a view to it.
        internal MgaSettingsViewModel(MgaPlugin plugin, ProtectedTokenStore tokenStore)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));

            Settings = plugin.LoadPluginSettings<MgaSettings>() ?? new MgaSettings();
            TestConnectionCommand = new RelayCommand(() => TestConnection(), () => !Busy);
            DisconnectCommand = new RelayCommand(() => Disconnect(), () => !Busy);
            RefreshStatus();
        }

        public MgaSettings Settings
        {
            get { return settings; }
            private set { SetValue(ref settings, value); }
        }

        /// <summary>
        /// The key as typed. Write-only from the view's perspective: once saved
        /// it is never read back out for display, because an access key shown on
        /// screen is an access key in a screenshot.
        /// </summary>
        public string PendingAccessKey
        {
            get { return pendingAccessKey; }
            set { SetValue(ref pendingAccessKey, value); }
        }

        public bool Busy
        {
            get { return busy; }
            private set { SetValue(ref busy, value); }
        }

        public string ConnectionStatus
        {
            get { return connectionStatus; }
            private set { SetValue(ref connectionStatus, value); }
        }

        public RelayCommand TestConnectionCommand { get; }

        public RelayCommand DisconnectCommand { get; }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
            PendingAccessKey = string.Empty;
            pendingDisconnect = false;
            RefreshStatus();
        }

        public void CancelEdit()
        {
            Settings = editingClone ?? new MgaSettings();
            PendingAccessKey = string.Empty;
            pendingDisconnect = false;
            RefreshStatus();
        }

        public void EndEdit()
        {
            // Order matters: a disconnect requested in this session is applied
            // before a newly typed key is stored, so "disconnect, then paste a
            // new key" ends connected rather than empty.
            if (pendingDisconnect)
            {
                tokenStore.Clear();
                pendingDisconnect = false;
            }
            if (!string.IsNullOrWhiteSpace(PendingAccessKey))
            {
                tokenStore.Save(PendingAccessKey);
            }

            PendingAccessKey = string.Empty;
            plugin.SavePluginSettings(Settings);
            plugin.OnSettingsChanged();
            RefreshStatus();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();

            try
            {
                MgaApiClient.NormalizeServerUrl(Settings.ServerUrl);
            }
            catch (ArgumentException ex)
            {
                errors.Add(ex.Message);
            }

            var willHaveKey = !string.IsNullOrWhiteSpace(PendingAccessKey) ||
                              (!pendingDisconnect && tokenStore.Exists);
            if (!willHaveKey)
            {
                errors.Add("Paste an access key from the MyGamesAnywhere console (System → Issue client).");
            }

            return errors.Count == 0;
        }

        /// <summary>
        /// Asks the server what this key can do, and says so. Run before saving
        /// so a key is proven while the user is still looking at the field they
        /// pasted it into, rather than failing later during a library refresh.
        /// </summary>
        private void TestConnection()
        {
            var key = !string.IsNullOrWhiteSpace(PendingAccessKey) ? PendingAccessKey : tokenStore.Load();
            if (string.IsNullOrWhiteSpace(key))
            {
                ConnectionStatus = "Paste an access key first.";
                return;
            }

            Busy = true;
            ConnectionStatus = "Checking…";
            try
            {
                using (var client = new MgaApiClient(Settings.ServerUrl, key))
                {
                    var response = client.GetCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult();
                    var capabilities = NegotiatedCapabilities.From(response);
                    ConnectionStatus = Describe(capabilities);
                }
            }
            catch (MgaApiException ex)
            {
                ConnectionStatus = ex.Message;
            }
            catch (Exception ex)
            {
                ConnectionStatus = "Could not check this connection: " + ex.Message;
            }
            finally
            {
                Busy = false;
            }
        }

        private static string Describe(NegotiatedCapabilities capabilities)
        {
            var library = capabilities.Has(NegotiatedCapabilities.CatalogProjection)
                ? "can read your library"
                : "cannot read your library";
            var artwork = capabilities.Has(NegotiatedCapabilities.MetadataMedia)
                ? "can download artwork"
                : "cannot download artwork";

            var summary = "Connected to " + (capabilities.ClientName ?? "MyGamesAnywhere") +
                          " (API " + (capabilities.ApiVersion ?? "v1") + "): " + library + ", " + artwork + ".";

            var blocking = capabilities.BlockingReason();
            return blocking == null ? summary : summary + " " + blocking;
        }

        private void Disconnect()
        {
            pendingDisconnect = true;
            PendingAccessKey = string.Empty;
            ConnectionStatus = "The stored access key will be removed when you save.";
        }

        private void RefreshStatus()
        {
            ConnectionStatus = tokenStore.Exists
                ? "An access key is stored for this server."
                : "No access key stored yet.";
        }
    }
}
