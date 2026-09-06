using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace MGA.Playnite.Install
{
    /// <summary>
    /// Which MGA copy backs each Playnite game, for the games MGA can serve
    /// files for.
    ///
    /// Playnite asks whether a game has an install action at arbitrary moments
    /// — opening a menu, drawing a row — not during a library sync, and asking
    /// the server on each of those would put a network call behind a mouse
    /// hover. So the answer is worked out once per sync and written down.
    ///
    /// It is a cache of the server's answer and nothing depends on it being
    /// current: a stale entry produces an install that fails with a clear
    /// message, and a missing one produces no install button until the next
    /// library update.
    /// </summary>
    internal sealed class InstallableCopyIndex
    {
        private readonly string path;
        private readonly object gate = new object();
        private Dictionary<string, string> copyIdByGameId;

        public InstallableCopyIndex(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("An index path is required.", nameof(path));
            }
            this.path = Path.GetFullPath(path);
        }

        public string Get(string gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                return null;
            }

            lock (gate)
            {
                if (copyIdByGameId == null)
                {
                    copyIdByGameId = Load();
                }
                string copyId;
                return copyIdByGameId.TryGetValue(gameId.Trim(), out copyId) ? copyId : null;
            }
        }

        public void Replace(IDictionary<string, string> entries)
        {
            var replacement = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    replacement[entry.Key.Trim()] = entry.Value.Trim();
                }
            }

            lock (gate)
            {
                copyIdByGameId = replacement;
                Save(replacement);
            }
        }

        private Dictionary<string, string> Load()
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(CopyIndexFile));
                    var loaded = (CopyIndexFile)serializer.ReadObject(stream);
                    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in loaded?.Entries ?? new List<CopyIndexEntry>())
                    {
                        if (entry != null && !string.IsNullOrWhiteSpace(entry.GameId) && !string.IsNullOrWhiteSpace(entry.CopyId))
                        {
                            result[entry.GameId] = entry.CopyId;
                        }
                    }
                    return result;
                }
            }
            catch (Exception)
            {
                // An unreadable cache is simply an empty one. The next sync
                // rebuilds it.
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void Save(Dictionary<string, string> entries)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var file = new CopyIndexFile { Entries = new List<CopyIndexEntry>(entries.Count) };
                foreach (var entry in entries)
                {
                    file.Entries.Add(new CopyIndexEntry { GameId = entry.Key, CopyId = entry.Value });
                }

                using (var stream = new MemoryStream())
                {
                    var serializer = new DataContractJsonSerializer(typeof(CopyIndexFile));
                    serializer.WriteObject(stream, file);
                    var staging = path + ".staging";
                    File.WriteAllBytes(staging, stream.ToArray());
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    File.Move(staging, path);
                }
            }
            catch (Exception)
            {
                // Losing the cache costs an install button until the next sync.
                // Failing the sync over it would cost the whole library.
            }
        }

        [DataContract]
        private sealed class CopyIndexFile
        {
            [DataMember(Name = "entries")]
            public List<CopyIndexEntry> Entries { get; set; }
        }

        [DataContract]
        private sealed class CopyIndexEntry
        {
            [DataMember(Name = "game_id")]
            public string GameId { get; set; }

            [DataMember(Name = "copy_id")]
            public string CopyId { get; set; }
        }
    }
}
