using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MGA.Playnite.Install
{
    /// <summary>
    /// A record of exactly what this plugin put on the disk for one game.
    ///
    /// Uninstall reads this and removes only what is listed. That is the whole
    /// reason it exists: without it, "uninstall" would mean deleting an
    /// install directory, and an install directory can contain saves, mods,
    /// configuration and files a game's own installer wrote. Removing what we
    /// are certain we wrote is the only removal that cannot destroy someone
    /// else's work.
    /// </summary>
    [DataContract]
    internal sealed class InstalledCopy
    {
        [DataMember(Name = "schema_version")]
        public int SchemaVersion { get; set; } = 1;

        [DataMember(Name = "game_id")]
        public string GameId { get; set; }

        [DataMember(Name = "copy_id")]
        public string CopyId { get; set; }

        /// <summary>
        /// The manifest revision these files came from. If MGA later reports a
        /// different one, what is on disk is a previous version of the game.
        /// </summary>
        [DataMember(Name = "manifest_revision")]
        public string ManifestRevision { get; set; }

        [DataMember(Name = "install_directory")]
        public string InstallDirectory { get; set; }

        [DataMember(Name = "files")]
        public List<InstalledFile> Files { get; set; } = new List<InstalledFile>();

        /// <summary>
        /// The file a play action should point at, relative to the install
        /// directory, or null when nothing in the copy is obviously runnable.
        /// </summary>
        [DataMember(Name = "launch_relative_path")]
        public string LaunchRelativePath { get; set; }

        [DataMember(Name = "installed_at_utc")]
        public string InstalledAtUtc { get; set; } = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        public long TotalBytes
        {
            get { return Files == null ? 0 : Files.Sum(file => file.Length); }
        }
    }

    [DataContract]
    internal sealed class InstalledFile
    {
        [DataMember(Name = "relative_path")]
        public string RelativePath { get; set; }

        [DataMember(Name = "length")]
        public long Length { get; set; }

        [DataMember(Name = "revision")]
        public string Revision { get; set; }
    }

    /// <summary>
    /// Where install records live: one JSON file per game, under the plugin's
    /// own data directory rather than beside the game, so a user who deletes a
    /// game folder by hand does not leave MGA believing the game is installed
    /// and does not lose the record of what was there.
    /// </summary>
    internal sealed class InstalledCopyStore
    {
        private readonly string rootPath;

        public InstalledCopyStore(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("A records directory is required.", nameof(rootPath));
            }
            this.rootPath = Path.GetFullPath(rootPath);
        }

        public InstalledCopy Find(string gameId)
        {
            var path = PathFor(gameId);
            if (path == null || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                using (var stream = new MemoryStream(bytes))
                {
                    var serializer = new DataContractJsonSerializer(typeof(InstalledCopy));
                    return (InstalledCopy)serializer.ReadObject(stream);
                }
            }
            catch (Exception)
            {
                // A record we cannot read is worse than none: acting on half of
                // it could delete the wrong files. Report "not installed" and
                // leave the disk alone.
                return null;
            }
        }

        public void Save(InstalledCopy copy)
        {
            if (copy == null)
            {
                throw new ArgumentNullException(nameof(copy));
            }
            var path = PathFor(copy.GameId);
            if (path == null)
            {
                throw new ArgumentException("The install record has no game id.", nameof(copy));
            }

            Directory.CreateDirectory(rootPath);
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(InstalledCopy));
                serializer.WriteObject(stream, copy);
                var staging = path + ".staging";
                File.WriteAllBytes(staging, stream.ToArray());
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(staging, path);
            }
        }

        public void Remove(string gameId)
        {
            var path = PathFor(gameId);
            if (path != null && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// A file name derived from the game id. MGA ids are GUIDs, but the
        /// name is sanitised anyway rather than trusted: a record path is used
        /// to write and delete files, and an id containing a path separator
        /// would escape the records directory.
        /// </summary>
        private string PathFor(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return null;
            }
            var safe = new StringBuilder(gameId.Length);
            foreach (var character in gameId.Trim())
            {
                safe.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_' ? character : '_');
            }
            return Path.Combine(rootPath, safe + ".json");
        }
    }
}
