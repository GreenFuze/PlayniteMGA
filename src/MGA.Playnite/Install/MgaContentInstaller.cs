using MGA.Playnite.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MGA.Playnite.Install
{
    /// <summary>
    /// Downloads a game's files from MGA into a folder on this machine.
    ///
    /// What this deliberately does not do is unpack, run installers, choose
    /// emulators or decide how a game starts. MGA-106 puts those on Playnite
    /// and the device, and it is right to: MGA has never seen this machine's
    /// disks, emulators or preferences. This brings the bytes across, records
    /// exactly what it wrote, and stops.
    /// </summary>
    internal sealed class MgaContentInstaller
    {
        private readonly MgaContentClient content;
        private readonly InstalledCopyStore records;
        private readonly string libraryRoot;

        public MgaContentInstaller(MgaContentClient content, InstalledCopyStore records, string libraryRoot)
        {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            this.records = records ?? throw new ArgumentNullException(nameof(records));
            if (string.IsNullOrWhiteSpace(libraryRoot))
            {
                throw new ArgumentException("An install location is required.", nameof(libraryRoot));
            }
            this.libraryRoot = Path.GetFullPath(libraryRoot);
        }

        public async Task<InstalledCopy> InstallAsync(
            string gameId,
            string gameName,
            string copyId,
            IProgress<InstallProgress> progress,
            CancellationToken cancelToken)
        {
            var manifest = await content.GetManifestAsync(copyId, cancelToken).ConfigureAwait(false);
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                throw new MgaApiException(
                    MgaFailure.Server,
                    "MyGamesAnywhere lists no files for this game, so there is nothing to download.");
            }

            await EnsureReadyAsync(manifest, copyId, progress, cancelToken).ConfigureAwait(false);

            var installDirectory = Path.Combine(libraryRoot, SafeFolderName(gameName, gameId));
            Directory.CreateDirectory(installDirectory);

            var totalBytes = manifest.Files.Sum(file => Math.Max(file.Length, 0));
            var completedBytes = 0L;
            var written = new List<InstalledFile>(manifest.Files.Count);

            foreach (var file in manifest.Files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                cancelToken.ThrowIfCancellationRequested();

                var destination = ResolveWithin(installDirectory, file.RelativePath);
                var fileStart = completedBytes;

                Report(progress, gameName, file.Name, completedBytes, totalBytes);
                await content.DownloadFileAsync(
                    copyId,
                    file,
                    destination,
                    (done, _) => Report(progress, gameName, file.Name, fileStart + done, totalBytes),
                    cancelToken).ConfigureAwait(false);

                VerifyLength(destination, file);

                completedBytes = fileStart + Math.Max(file.Length, 0);
                written.Add(new InstalledFile
                {
                    RelativePath = file.RelativePath,
                    Length = file.Length,
                    Revision = file.Revision
                });
            }

            // Re-read the manifest at the end. If its revision moved while the
            // download ran, the source changed underneath us and what is on
            // disk may be a mixture of two versions.
            var after = await content.GetManifestAsync(copyId, cancelToken).ConfigureAwait(false);
            if (after != null && !string.IsNullOrWhiteSpace(after.Revision) &&
                !string.Equals(after.Revision, manifest.Revision, StringComparison.Ordinal))
            {
                throw new MgaApiException(
                    MgaFailure.Server,
                    "The files for this game changed on the server while they were being downloaded, so the copy on this machine may be a mixture of two versions. Install it again.");
            }

            var record = new InstalledCopy
            {
                GameId = gameId,
                CopyId = copyId,
                ManifestRevision = manifest.Revision,
                InstallDirectory = installDirectory,
                Files = written,
                LaunchRelativePath = ChooseLaunchTarget(manifest.Files)
            };
            records.Save(record);
            return record;
        }

        /// <summary>
        /// Some sources — a cloud drive, for instance — hold their bytes
        /// somewhere MGA cannot stream from directly, and must fetch them into
        /// its own cache first. That is a server-side job with its own
        /// progress, and cancelling the install cancels it too rather than
        /// leaving the server working on something nobody is waiting for.
        /// </summary>
        private async Task EnsureReadyAsync(
            ManifestDto manifest,
            string copyId,
            IProgress<InstallProgress> progress,
            CancellationToken cancelToken)
        {
            if (manifest.Delivery == null || !manifest.Delivery.MaterializationRequired || manifest.Delivery.Ready)
            {
                return;
            }

            var started = await content.StartMaterializationAsync(copyId, cancelToken).ConfigureAwait(false);
            if (started == null || started.Immediate || started.Job == null || string.IsNullOrWhiteSpace(started.Job.JobId))
            {
                return;
            }

            var jobId = started.Job.JobId;
            try
            {
                while (true)
                {
                    cancelToken.ThrowIfCancellationRequested();

                    var job = await content.GetMaterializationAsync(jobId, cancelToken).ConfigureAwait(false);
                    if (job == null)
                    {
                        return;
                    }

                    if (string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new MgaApiException(
                            MgaFailure.Server,
                            "MyGamesAnywhere could not fetch this game's files from its source. " +
                            (job.Message ?? string.Empty));
                    }
                    if (string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(job.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                    if (string.Equals(job.Status, "cancelled", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(job.Status, "canceled", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new OperationCanceledException(cancelToken);
                    }

                    progress?.Report(new InstallProgress(
                        "Waiting for MyGamesAnywhere to fetch this game's files",
                        job.ProgressCurrent,
                        job.ProgressTotal));

                    await Task.Delay(TimeSpan.FromSeconds(2), cancelToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                await TryCancelAsync(jobId).ConfigureAwait(false);
                throw;
            }
        }

        private async Task TryCancelAsync(string jobId)
        {
            try
            {
                // Its own token: the caller's is already cancelled, and a
                // cancellation request that cannot be sent is worse than one
                // that takes a moment.
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    await content.CancelMaterializationAsync(jobId, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Nothing useful to do: the install is being abandoned anyway.
            }
        }

        /// <summary>
        /// Confirms the file on disk is the length the manifest promised.
        ///
        /// This is the honest limit of verification here. MGA emits a checksum
        /// only when a source recorded one as a sha256 digest, and scanned
        /// drives and shares do not — so a content hash is usually absent, and
        /// claiming checksum verification would be claiming a check that never
        /// runs. Length plus the manifest revision catches a truncated
        /// transfer and a source that changed; it does not catch silent
        /// corruption, and this comment is here so nobody later believes it
        /// does.
        /// </summary>
        private static void VerifyLength(string path, ManifestFileDto file)
        {
            if (file.Length <= 0)
            {
                return;
            }
            var actual = new FileInfo(path).Length;
            if (actual != file.Length)
            {
                throw new MgaApiException(
                    MgaFailure.Server,
                    "'" + file.Name + "' arrived as " + actual + " bytes but should be " + file.Length +
                    ". The download was incomplete, so the game has not been installed.");
            }
        }

        /// <summary>
        /// The file a play action should point at: the one the source marked as
        /// the root, else the largest executable. Null when nothing looks
        /// runnable, which is normal — a ROM or a folder of data is launched by
        /// an emulator Playnite configures, not by this plugin.
        /// </summary>
        public static string ChooseLaunchTarget(List<ManifestFileDto> files)
        {
            if (files == null || files.Count == 0)
            {
                return null;
            }

            var root = files.FirstOrDefault(file =>
                file != null && string.Equals(file.Role, "root", StringComparison.OrdinalIgnoreCase) &&
                IsExecutable(file));
            if (root != null)
            {
                return root.RelativePath;
            }

            var executable = files
                .Where(file => file != null && IsExecutable(file))
                .OrderByDescending(file => file.Length)
                .FirstOrDefault();
            return executable == null ? null : executable.RelativePath;
        }

        private static bool IsExecutable(ManifestFileDto file)
        {
            if (string.Equals(file.Kind, "executable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var name = file.Name ?? file.RelativePath ?? string.Empty;
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Joins a manifest-relative path onto the install directory, refusing
        /// anything that would land outside it. Manifest paths come from a
        /// scanned filesystem, so "..\\..\\Windows\\System32" is a shape a
        /// malformed or hostile source could produce, and a downloader that
        /// honoured it would write wherever it was told.
        /// </summary>
        public static string ResolveWithin(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new ArgumentException("A file path is required.", nameof(relativePath));
            }

            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var combined = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "'" + relativePath + "' points outside the install folder and was refused.");
            }
            return combined;
        }

        /// <summary>
        /// A folder name from the game's title, with the id appended so two
        /// games sharing a title cannot share a folder.
        /// </summary>
        public static string SafeFolderName(string gameName, string gameId)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string((gameName ?? string.Empty)
                .Select(character => invalid.Contains(character) ? '_' : character)
                .ToArray())
                .Trim()
                .TrimEnd('.');
            if (cleaned.Length > 60)
            {
                cleaned = cleaned.Substring(0, 60).Trim();
            }

            var suffix = (gameId ?? string.Empty).Replace("-", string.Empty);
            if (suffix.Length > 8)
            {
                suffix = suffix.Substring(0, 8);
            }
            if (cleaned.Length == 0)
            {
                cleaned = "game";
            }
            return suffix.Length == 0 ? cleaned : cleaned + " (" + suffix + ")";
        }

        private static void Report(IProgress<InstallProgress> progress, string gameName, string fileName, long done, long total)
        {
            progress?.Report(new InstallProgress(
                "Downloading " + fileName,
                done,
                total));
        }
    }

    internal sealed class InstallProgress
    {
        public InstallProgress(string message, long current, long total)
        {
            Message = message;
            Current = current;
            Total = total;
        }

        public string Message { get; }

        public long Current { get; }

        public long Total { get; }
    }
}
