using MGA.Playnite.Api;
using MGA.Playnite.Storage;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace MGA.Playnite.Settings
{
    /// <summary>
    /// The settings screen's behaviour: point at a server, choose a player,
    /// enter their password, and be connected.
    ///
    /// Neither the password nor the access key is part of the settings object.
    /// Playnite writes that to a plain JSON file, and neither belongs there —
    /// the password is used for one request and discarded, and the key it buys
    /// goes to <see cref="ProtectedTokenStore"/>.
    /// </summary>
    public sealed class MgaSettingsViewModel : ObservableObject, ISettings
    {
        private readonly MgaPlugin plugin;
        private readonly ProtectedTokenStore tokenStore;

        private MgaSettings editingClone;
        private MgaSettings settings;
        private string password = string.Empty;
        private ProfileDto selectedProfile;
        private bool busy;
        private string connectionStatus;
        private bool pendingDisconnect;
        private string pendingToken;

        internal MgaSettingsViewModel(MgaPlugin plugin, ProtectedTokenStore tokenStore)
        {
            this.plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            this.tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));

            Settings = plugin.LoadPluginSettings<MgaSettings>() ?? new MgaSettings();
            Profiles = new ObservableCollection<ProfileDto>();
            LoadProfilesCommand = new RelayCommand(() => LoadProfiles(), () => !Busy);
            SignInCommand = new RelayCommand(() => SignIn(), () => !Busy);
            DisconnectCommand = new RelayCommand(() => Disconnect(), () => !Busy);
            RefreshStatus();
        }

        public MgaSettings Settings
        {
            get { return settings; }
            private set { SetValue(ref settings, value); }
        }

        public ObservableCollection<ProfileDto> Profiles { get; }

        public ProfileDto SelectedProfile
        {
            get { return selectedProfile; }
            set { SetValue(ref selectedProfile, value); }
        }

        /// <summary>
        /// Held only until the sign-in request is made, then cleared. It is
        /// never written anywhere.
        /// </summary>
        public string Password
        {
            get { return password; }
            set { SetValue(ref password, value); }
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

        public RelayCommand LoadProfilesCommand { get; }

        public RelayCommand SignInCommand { get; }

        public RelayCommand DisconnectCommand { get; }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
            Password = string.Empty;
            pendingDisconnect = false;
            pendingToken = null;
            RefreshStatus();
        }

        public void CancelEdit()
        {
            Settings = editingClone ?? new MgaSettings();
            Password = string.Empty;
            pendingDisconnect = false;
            pendingToken = null;
            RefreshStatus();
        }

        public void EndEdit()
        {
            // A disconnect requested in this session is applied before a key
            // obtained in it, so "disconnect, then sign in again" ends
            // connected rather than empty.
            if (pendingDisconnect)
            {
                tokenStore.Clear();
                pendingDisconnect = false;
            }
            if (!string.IsNullOrWhiteSpace(pendingToken))
            {
                tokenStore.Save(pendingToken);
                pendingToken = null;
            }

            Password = string.Empty;
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

            var willHaveKey = !string.IsNullOrWhiteSpace(pendingToken) ||
                              (!pendingDisconnect && tokenStore.Exists);
            if (!willHaveKey)
            {
                errors.Add("Choose a player and sign in before saving.");
            }

            return errors.Count == 0;
        }

        /// <summary>
        /// Asks the server who can sign in. Deliberately a separate step from
        /// signing in: the list tells the user their server address is right
        /// before they are asked to type a password into it.
        /// </summary>
        private void LoadProfiles()
        {
            Busy = true;
            ConnectionStatus = "Looking for players…";
            try
            {
                using (var signIn = new MgaSignIn(Settings.ServerUrl))
                {
                    var found = signIn.ListProfilesAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Profiles.Clear();
                    foreach (var profile in found)
                    {
                        if (profile != null && !string.IsNullOrWhiteSpace(profile.Id))
                        {
                            Profiles.Add(profile);
                        }
                    }

                    if (Profiles.Count == 0)
                    {
                        ConnectionStatus = "That server has no players to sign in as.";
                        return;
                    }
                    if (Profiles.Count == 1)
                    {
                        SelectedProfile = Profiles[0];
                    }
                    ConnectionStatus = "Choose a player and enter their password.";
                }
            }
            catch (MgaApiException ex)
            {
                ConnectionStatus = ex.Message;
            }
            catch (Exception ex)
            {
                ConnectionStatus = "Could not read the list of players: " + ex.Message;
            }
            finally
            {
                Busy = false;
            }
        }

        private void SignIn()
        {
            var profile = SelectedProfile;
            if (profile == null)
            {
                ConnectionStatus = "Choose a player first.";
                return;
            }

            Busy = true;
            ConnectionStatus = "Signing in…";
            try
            {
                using (var signIn = new MgaSignIn(Settings.ServerUrl))
                {
                    var issued = signIn
                        .SignInAsync(profile.Id, Password, MgaSignIn.SuggestClientName(), CancellationToken.None)
                        .GetAwaiter().GetResult();

                    // Held until Save, so cancelling the settings dialog leaves
                    // the previous connection exactly as it was.
                    pendingToken = issued.Token;
                    pendingDisconnect = false;
                    Password = string.Empty;
                    Settings.ProfileDisplayName = profile.DisplayName;

                    ConnectionStatus = "Signed in as " + profile + ". Save to finish connecting" +
                        DescribeScopes(issued) + ".";
                }
            }
            catch (MgaApiException ex)
            {
                ConnectionStatus = ex.Message;
            }
            catch (Exception ex)
            {
                ConnectionStatus = "Could not sign in: " + ex.Message;
            }
            finally
            {
                Busy = false;
            }
        }

        /// <summary>
        /// Says what the key can do, because the answer is not always what the
        /// user expects: an older server may issue fewer permissions than this
        /// version of the plugin can use.
        /// </summary>
        private static string DescribeScopes(IssuedClientDto issued)
        {
            if (issued.Scopes == null || issued.Scopes.Count == 0)
            {
                return string.Empty;
            }
            var canInstall = issued.Scopes.Contains("content.read");
            return canInstall
                ? ". This connection can read your library and download games"
                : ". This connection can read your library, but not download games";
        }

        private void Disconnect()
        {
            pendingDisconnect = true;
            pendingToken = null;
            Password = string.Empty;
            Settings.ProfileDisplayName = string.Empty;
            ConnectionStatus = "The stored connection will be removed when you save.";
        }

        private void RefreshStatus()
        {
            if (!tokenStore.Exists)
            {
                ConnectionStatus = "Not connected yet. Enter your server address, then look up its players.";
                return;
            }
            ConnectionStatus = string.IsNullOrWhiteSpace(Settings.ProfileDisplayName)
                ? "Connected."
                : "Connected as " + Settings.ProfileDisplayName + ".";
        }
    }
}
