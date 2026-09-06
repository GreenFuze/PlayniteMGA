using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MGA.Playnite.Api
{
    /// <summary>
    /// The whole of this plugin's contact with MGA.
    ///
    /// Everything reaches the server through the scoped frontend API at
    /// /api/frontend/v1, authenticated by one bearer token that names exactly
    /// one profile. There is deliberately no other entry point: the console's
    /// session-authenticated routes and every administrative operation are out
    /// of reach by construction, not by this class choosing not to call them.
    /// </summary>
    internal sealed class MgaApiClient : IDisposable
    {
        private const string ApiPrefix = "/api/frontend/v1";

        private readonly HttpClient httpClient;
        private readonly Uri baseAddress;
        private readonly string token;

        public MgaApiClient(string serverUrl, string token, HttpMessageHandler handler = null)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                throw new ArgumentException("A MyGamesAnywhere server address is required.", nameof(serverUrl));
            }
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("An access key is required.", nameof(token));
            }

            baseAddress = NormalizeServerUrl(serverUrl);
            this.token = token.Trim();
            httpClient = handler == null ? new HttpClient() : new HttpClient(handler, true);

            // A library sync pulls hundreds of records and artwork over a LAN
            // that may be slow rather than broken. Playnite already runs this on
            // a background thread with its own cancellation.
            httpClient.Timeout = TimeSpan.FromMinutes(10);
        }

        /// <summary>
        /// Accepts what a person would actually type — "tv2:8900", "localhost",
        /// a full URL with a trailing slash — and produces a base address. A
        /// bare host gets http, because MGA is a LAN server that is normally
        /// reached over plain HTTP and silently upgrading to https would fail
        /// with a certificate error the user cannot act on.
        /// </summary>
        public static Uri NormalizeServerUrl(string value)
        {
            var trimmed = (value ?? string.Empty).Trim().TrimEnd('/');
            if (trimmed.Length == 0)
            {
                throw new ArgumentException("A MyGamesAnywhere server address is required.", nameof(value));
            }
            if (trimmed.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                trimmed = "http://" + trimmed;
            }

            Uri parsed;
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("'" + value + "' is not a usable server address.", nameof(value));
            }
            return parsed;
        }

        public Task<CapabilitiesResponse> GetCapabilitiesAsync(CancellationToken cancelToken)
        {
            return GetAsync<CapabilitiesResponse>("/capabilities", "read its capabilities", cancelToken);
        }

        /// <summary>
        /// Every game in the profile's library, following the server's paging.
        ///
        /// The page size is asked for rather than assumed, and the loop stops on
        /// the server's own total, so a server that clamps the page size cannot
        /// send this into an endless loop.
        /// </summary>
        public async Task<List<GameDto>> GetAllGamesAsync(int pageSize, CancellationToken cancelToken)
        {
            if (pageSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }

            var games = new List<GameDto>();
            var page = 0;
            while (true)
            {
                cancelToken.ThrowIfCancellationRequested();

                var path = "/games?page=" + page + "&page_size=" + pageSize;
                var response = await GetAsync<ListGamesResponse>(path, "list your games", cancelToken)
                    .ConfigureAwait(false);
                if (response?.Games == null || response.Games.Count == 0)
                {
                    break;
                }

                games.AddRange(response.Games);
                if (games.Count >= response.Total)
                {
                    break;
                }
                page++;
            }
            return games;
        }

        /// <summary>
        /// Artwork bytes for one asset. Returns null when the asset is simply
        /// not there, because a game missing a cover is a normal state and must
        /// not fail the whole library sync.
        /// </summary>
        public async Task<byte[]> GetMediaAsync(int assetId, CancellationToken cancelToken)
        {
            using (var request = CreateRequest(HttpMethod.Get, "/media/" + assetId))
            using (var response = await SendAsync(request, "download artwork", cancelToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw await FailureAsync(response, "download artwork").ConfigureAwait(false);
                }
                return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        private async Task<T> GetAsync<T>(string path, string what, CancellationToken cancelToken) where T : class
        {
            using (var request = CreateRequest(HttpMethod.Get, path))
            using (var response = await SendAsync(request, what, cancelToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw await FailureAsync(response, what).ConfigureAwait(false);
                }

                var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                try
                {
                    using (var stream = new MemoryStream(body))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(T));
                        return (T)serializer.ReadObject(stream);
                    }
                }
                catch (Exception ex)
                {
                    throw new MgaApiException(
                        MgaFailure.Malformed,
                        "MyGamesAnywhere returned a response this version of the plugin could not read while trying to " +
                        what + ". The server may be newer than the plugin.",
                        ex);
                }
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, new Uri(baseAddress, ApiPrefix + path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return request;
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string what, CancellationToken cancelToken)
        {
            try
            {
                return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancelToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Reaching nothing at all is its own outcome. Reporting it as a
                // server error would send the user looking at MGA's logs for a
                // request that never arrived.
                throw new MgaApiException(
                    MgaFailure.Unreachable,
                    "Could not reach MyGamesAnywhere at " + baseAddress + " to " + what +
                    ". Check that the server is running and the address is right.",
                    ex);
            }
        }

        private static async Task<MgaApiException> FailureAsync(HttpResponseMessage response, string what)
        {
            var body = string.Empty;
            try
            {
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                // The status code is the part that decides the remedy; a body
                // that will not read does not change which failure this is.
            }
            return MgaApiException.FromStatus(response.StatusCode, body, what);
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}
