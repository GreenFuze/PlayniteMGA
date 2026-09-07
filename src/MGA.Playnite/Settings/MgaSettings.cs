using MGA.Playnite.Storage;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
// Playnite's ObservableObject is declared in System.Collections.Generic rather
// than a Playnite namespace, so this using is load-bearing despite appearances.
using System.Collections.Generic;

namespace MGA.Playnite.Settings
{
    /// <summary>
    /// The plugin's stored settings.
    ///
    /// The access key is deliberately absent: Playnite writes this object to a
    /// plain JSON file, and a key granting read access to a whole library does
    /// not belong there. It lives in <see cref="ProtectedTokenStore"/> instead.
    /// </summary>
    public sealed class MgaSettings : ObservableObject
    {
        private string serverUrl = string.Empty;
        private int pageSize = 200;
        private bool downloadArtwork = true;
        private bool hideLapsedSubscriptionGames = true;
        private string installRoot = string.Empty;
        private string profileDisplayName = string.Empty;

        /// <summary>
        /// Version of this settings shape. Persisted so a future change can
        /// migrate rather than silently reinterpret an old file.
        /// </summary>
        public int SettingsVersion { get; set; } = 1;

        public string ServerUrl
        {
            get { return serverUrl; }
            set { SetValue(ref serverUrl, value); }
        }

        /// <summary>
        /// How many games to ask for per request. The server decides the real
        /// limit; this only controls how the sync is chunked.
        /// </summary>
        public int PageSize
        {
            get { return pageSize; }
            set { SetValue(ref pageSize, value <= 0 ? 200 : value); }
        }

        public bool DownloadArtwork
        {
            get { return downloadArtwork; }
            set { SetValue(ref downloadArtwork, value); }
        }

        /// <summary>
        /// Leave out games a subscription no longer carries.
        ///
        /// A subscription source reports what an account has played, not what it
        /// owns, so a library built from it includes titles that were tried once
        /// and cannot be started today without buying them. MGA's own console
        /// hides them by default and this matches it, so the same library does
        /// not have two different sizes depending on where it is viewed.
        ///
        /// Reversible: turning it off asks the server for everything again, and
        /// the games return on the next library update.
        /// </summary>
        public bool HideLapsedSubscriptionGames
        {
            get { return hideLapsedSubscriptionGames; }
            set { SetValue(ref hideLapsedSubscriptionGames, value); }
        }

        /// <summary>
        /// Where downloaded games are put. Blank means a folder under the
        /// plugin's own data directory, which is somewhere that always exists
        /// and is always writable — but games are large, and the disk Playnite
        /// happens to be installed on is rarely the one with room for them,
        /// so this is meant to be changed.
        /// </summary>
        /// <summary>
        /// Which player this connection is signed in as, kept only so the
        /// settings screen can say so. The key itself is stored encrypted
        /// elsewhere; this is a label, not a credential.
        /// </summary>
        public string ProfileDisplayName
        {
            get { return profileDisplayName; }
            set { SetValue(ref profileDisplayName, value); }
        }

        public string InstallRoot
        {
            get { return installRoot; }
            set { SetValue(ref installRoot, value); }
        }

        public string EffectiveInstallRoot(string pluginDataPath)
        {
            var chosen = (InstallRoot ?? string.Empty).Trim();
            return chosen.Length > 0
                ? chosen
                : System.IO.Path.Combine(pluginDataPath ?? string.Empty, "Games");
        }
    }
}
