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
    }
}
