using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MGA.Playnite.Install
{
    /// <summary>
    /// Removes what this plugin downloaded, and nothing else.
    ///
    /// An install directory is not ours to delete wholesale. By the time
    /// someone uninstalls, it can hold saves, mods, configuration, screenshots
    /// and files a game's own installer wrote. So removal works from the record
    /// of what was written: each file that is still exactly as we left it goes,
    /// anything else stays, and directories are removed only once they are
    /// empty.
    /// </summary>
    internal sealed class MgaContentUninstaller
    {
        private readonly InstalledCopyStore records;

        public MgaContentUninstaller(InstalledCopyStore records)
        {
            this.records = records ?? throw new ArgumentNullException(nameof(records));
        }

        public UninstallOutcome Uninstall(string gameId)
        {
            var record = records.Find(gameId);
            if (record == null)
            {
                // Nothing recorded: there is no set of files we can claim to
                // have written, so there is nothing we may delete.
                return new UninstallOutcome(null, 0, 0, hadRecord: false);
            }

            var removed = 0;
            var kept = 0;
            foreach (var file in record.Files ?? new List<InstalledFile>())
            {
                if (file == null || string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    continue;
                }

                string path;
                try
                {
                    path = MgaContentInstaller.ResolveWithin(record.InstallDirectory, file.RelativePath);
                }
                catch (Exception)
                {
                    // A recorded path that no longer resolves inside the
                    // install directory is not deleted. Refusing is the only
                    // safe reading of a record we cannot trust.
                    kept++;
                    continue;
                }

                if (!File.Exists(path))
                {
                    continue;
                }

                if (file.Length > 0 && new FileInfo(path).Length != file.Length)
                {
                    // Changed since we wrote it — patched, modded, or replaced.
                    // Whatever it is now, it is not the file we downloaded.
                    kept++;
                    continue;
                }

                try
                {
                    File.Delete(path);
                    removed++;
                }
                catch (Exception)
                {
                    kept++;
                }
            }

            RemoveEmptyDirectories(record.InstallDirectory);
            records.Remove(gameId);
            return new UninstallOutcome(record.InstallDirectory, removed, kept, hadRecord: true);
        }

        /// <summary>
        /// Prunes directories that are now empty, deepest first, stopping at
        /// the install directory itself. Anything still holding a file is left
        /// exactly where it is.
        /// </summary>
        private static void RemoveEmptyDirectories(string installDirectory)
        {
            if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            {
                return;
            }

            try
            {
                var directories = Directory
                    .GetDirectories(installDirectory, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length)
                    .ToList();
                directories.Add(installDirectory);

                foreach (var directory in directories)
                {
                    if (Directory.Exists(directory) &&
                        !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
            }
            catch (Exception)
            {
                // Leaving an empty folder behind is harmless; failing an
                // uninstall over one is not.
            }
        }
    }

    internal sealed class UninstallOutcome
    {
        public UninstallOutcome(string installDirectory, int filesRemoved, int filesKept, bool hadRecord)
        {
            InstallDirectory = installDirectory;
            FilesRemoved = filesRemoved;
            FilesKept = filesKept;
            HadRecord = hadRecord;
        }

        /// <summary>Whether there was a record of an install to act on at all.</summary>
        public bool HadRecord { get; }

        public string InstallDirectory { get; }

        public int FilesRemoved { get; }

        /// <summary>
        /// Files left alone because they had changed since we wrote them, or
        /// could not be removed. Worth telling the user about: they are the
        /// difference between a folder that disappeared and one that did not.
        /// </summary>
        public int FilesKept { get; }

    }
}
