using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MGA.Playnite.Api
{
    /// <summary>
    /// The content half of the MGA conversation: manifests, materialization
    /// jobs, and file bytes.
    ///
    /// Separate from <see cref="MgaApiClient"/> because it is used at a
    /// different time, under different permissions, and for very different
    /// request shapes — a library sync is many small JSON reads, an install is
    /// one long byte stream that must survive being interrupted.
    /// </summary>
    internal sealed class MgaContentClient
    {
        private readonly MgaApiClient client;

        public MgaContentClient(MgaApiClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public Task<ManifestDto> GetManifestAsync(string copyId, CancellationToken cancelToken)
        {
            return client.GetJsonAsync<ManifestDto>(
                "/content/copies/" + Uri.EscapeDataString(copyId) + "/manifest",
                "read the file list for this game",
                cancelToken);
        }

        public Task<PrepareResponseDto> StartMaterializationAsync(string copyId, CancellationToken cancelToken)
        {
            return client.PostJsonAsync<PrepareResponseDto>(
                "/content/copies/" + Uri.EscapeDataString(copyId) + "/materializations",
                "ask the server to fetch this game's files",
                cancelToken);
        }

        public Task<MaterializationJobDto> GetMaterializationAsync(string jobId, CancellationToken cancelToken)
        {
            return client.GetJsonAsync<MaterializationJobDto>(
                "/content/materializations/" + Uri.EscapeDataString(jobId),
                "check on the server fetching this game's files",
                cancelToken);
        }

        public Task<MaterializationJobDto> CancelMaterializationAsync(string jobId, CancellationToken cancelToken)
        {
            return client.PostJsonAsync<MaterializationJobDto>(
                "/content/materializations/" + Uri.EscapeDataString(jobId) + "/cancel",
                "stop the server fetching this game's files",
                cancelToken);
        }

        /// <summary>
        /// Downloads one file to <paramref name="destinationPath"/>, resuming
        /// from whatever is already there.
        ///
        /// Resume is a Range request from the current length of the partial
        /// file. Two things make that safe rather than merely convenient:
        ///
        /// - the manifest revision is sent as If-Match style evidence by the
        ///   caller, so a source that changed between attempts is caught before
        ///   two different versions are spliced together;
        /// - a server that ignores Range and answers 200 is detected, and the
        ///   partial file is discarded rather than appended to, which would
        ///   otherwise produce a file of the right length made of the wrong
        ///   bytes.
        /// </summary>
        public async Task DownloadFileAsync(
            string copyId,
            ManifestFileDto file,
            string destinationPath,
            Action<long, long> onProgress,
            CancellationToken cancelToken)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var alreadyHave = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0L;
            if (file.Length > 0 && alreadyHave > file.Length)
            {
                // Longer than the manifest says it should be: this is not a
                // partial download of this file, so it is not resumable.
                File.Delete(destinationPath);
                alreadyHave = 0;
            }
            if (file.Length > 0 && alreadyHave == file.Length)
            {
                onProgress?.Invoke(alreadyHave, file.Length);
                return;
            }

            var path = "/content/copies/" + Uri.EscapeDataString(copyId) +
                       "/files/" + Uri.EscapeDataString(file.Id);

            using (var request = client.CreateContentRequest(HttpMethod.Get, path))
            {
                if (alreadyHave > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(alreadyHave, null);
                }

                using (var response = await client.SendContentAsync(request, "download this game's files", cancelToken)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw await MgaApiClient.ContentFailureAsync(response, "download this game's files")
                            .ConfigureAwait(false);
                    }

                    var appending = alreadyHave > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                    if (alreadyHave > 0 && !appending)
                    {
                        // The server sent the whole file despite the Range
                        // request. Start over rather than append.
                        alreadyHave = 0;
                    }

                    var mode = appending ? FileMode.Append : FileMode.Create;
                    var written = appending ? alreadyHave : 0L;

                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var target = new FileStream(destinationPath, mode, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                    {
                        var buffer = new byte[81920];
                        while (true)
                        {
                            cancelToken.ThrowIfCancellationRequested();
                            var read = await source.ReadAsync(buffer, 0, buffer.Length, cancelToken).ConfigureAwait(false);
                            if (read <= 0)
                            {
                                break;
                            }
                            await target.WriteAsync(buffer, 0, read, cancelToken).ConfigureAwait(false);
                            written += read;
                            onProgress?.Invoke(written, file.Length);
                        }
                        await target.FlushAsync(cancelToken).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
