using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MGA.Playnite.Storage
{
    /// <summary>
    /// Where the MGA access key lives on this machine.
    ///
    /// The key grants read access to a whole game library, so it is never
    /// written in the clear and never kept in Playnite's own settings file,
    /// which is plain JSON that gets copied around in backups and support
    /// bundles. DPAPI ties it to the current Windows user; the plugin id is
    /// mixed in as entropy so a file lifted from another extension's folder
    /// cannot be unprotected by this one.
    ///
    /// Writes go to a staging file and are then moved into place, so an
    /// interrupted save leaves the previous key intact rather than a truncated
    /// file that unprotects to nothing.
    /// </summary>
    internal sealed class ProtectedTokenStore
    {
        private readonly string tokenPath;
        private readonly byte[] entropy;

        public ProtectedTokenStore(string tokenPath, Guid pluginId)
        {
            if (string.IsNullOrWhiteSpace(tokenPath))
            {
                throw new ArgumentException("A token path is required.", nameof(tokenPath));
            }

            this.tokenPath = Path.GetFullPath(tokenPath);
            entropy = Encoding.UTF8.GetBytes(pluginId.ToString("N"));
        }

        public bool Exists
        {
            get { return File.Exists(tokenPath); }
        }

        /// <summary>
        /// The stored key, or null when none is stored or the stored one cannot
        /// be read back. A key protected under a different Windows user, or
        /// corrupted, is reported as absent: the remedy is the same either way —
        /// paste a new one — and there is nothing recoverable to salvage.
        /// </summary>
        public string Load()
        {
            if (!Exists)
            {
                return null;
            }

            try
            {
                var protectedBytes = File.ReadAllBytes(tokenPath);
                var clearBytes = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
                var token = Encoding.UTF8.GetString(clearBytes).Trim();
                return token.Length == 0 ? null : token;
            }
            catch (CryptographicException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void Save(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("An access key is required.", nameof(token));
            }

            var directory = Path.GetDirectoryName(tokenPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var clearBytes = Encoding.UTF8.GetBytes(token.Trim());
            var protectedBytes = ProtectedData.Protect(clearBytes, entropy, DataProtectionScope.CurrentUser);

            var stagingPath = tokenPath + ".staging";
            File.WriteAllBytes(stagingPath, protectedBytes);
            if (File.Exists(tokenPath))
            {
                File.Delete(tokenPath);
            }
            File.Move(stagingPath, tokenPath);
        }

        public void Clear()
        {
            if (File.Exists(tokenPath))
            {
                File.Delete(tokenPath);
            }
        }
    }
}
