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
    /// Signing in to a server: listing its profiles, and exchanging one
    /// profile's password for an access key.
    ///
    /// Separate from <see cref="MgaApiClient"/> because it is the one
    /// conversation that happens <em>before</em> there is a key to talk with.
    /// Everything else this plugin does presents a bearer token; these two
    /// requests are how that token comes to exist.
    ///
    /// The password is used for exactly one request and never stored. What is
    /// stored is the key the server returns, which reaches only the scoped
    /// frontend API and can be revoked in the MGA console without anyone
    /// changing a password.
    /// </summary>
    internal sealed class MgaSignIn : IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly Uri baseAddress;

        public MgaSignIn(string serverUrl, HttpMessageHandler handler = null)
        {
            baseAddress = MgaApiClient.NormalizeServerUrl(serverUrl);
            httpClient = handler == null
                ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, true)
                : new HttpClient(handler, true);
            httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// The profiles this server offers. Needs no credentials — it is the
        /// same list the console shows on its own profile picker, and carries
        /// only identity.
        /// </summary>
        public async Task<List<ProfileDto>> ListProfilesAsync(CancellationToken cancelToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseAddress, "/api/profiles")))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using (var response = await SendAsync(request, "read the list of players", cancelToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw await FailureAsync(response, "read the list of players").ConfigureAwait(false);
                    }
                    var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    return Deserialize<List<ProfileDto>>(body, "read the list of players") ?? new List<ProfileDto>();
                }
            }
        }

        /// <summary>
        /// Exchanges a profile's password for an access key.
        ///
        /// The name is what the MGA console will show in its list of connected
        /// applications, so it says which machine this is: someone revoking a
        /// key needs to know which one they are revoking.
        /// </summary>
        public async Task<IssuedClientDto> SignInAsync(string profileId, string password, string clientName, CancellationToken cancelToken)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                throw new MgaApiException(MgaFailure.Unauthenticated, "Choose a player before signing in.");
            }

            var payload = Serialize(new SignInRequest
            {
                ProfileId = profileId,
                Credential = password ?? string.Empty,
                Name = string.IsNullOrWhiteSpace(clientName) ? "Playnite" : clientName.Trim()
            });

            using (var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseAddress, "/api/auth/frontend-clients")))
            {
                request.Content = new ByteArrayContent(payload);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using (var response = await SendAsync(request, "sign in", cancelToken).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new MgaApiException(MgaFailure.Unauthenticated,
                            "That password was not accepted for this player.");
                    }
                    if (response.StatusCode == (HttpStatusCode)429)
                    {
                        throw new MgaApiException(MgaFailure.Unauthenticated,
                            "Too many sign-in attempts. Wait a moment and try again.");
                    }
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        throw new MgaApiException(MgaFailure.Unauthenticated,
                            "This player has no password set yet. Set one in the MGA console first.");
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        // An older server has no sign-in exchange. Saying which
                        // side is out of date is more use than "not found".
                        throw new MgaApiException(MgaFailure.Server,
                            "This MyGamesAnywhere server is too old to sign in from Playnite. Update the server, or paste an access key issued from its console.");
                    }
                    if (!response.IsSuccessStatusCode)
                    {
                        throw await FailureAsync(response, "sign in").ConfigureAwait(false);
                    }

                    var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    var issued = Deserialize<IssuedClientDto>(body, "sign in");
                    if (issued == null || string.IsNullOrWhiteSpace(issued.Token))
                    {
                        throw new MgaApiException(MgaFailure.Malformed,
                            "The server accepted the sign-in but returned no access key.");
                    }
                    return issued;
                }
            }
        }

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string what, CancellationToken cancelToken)
        {
            try
            {
                return await httpClient.SendAsync(request, cancelToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
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
                // The status decides the remedy; an unreadable body does not.
            }
            return MgaApiException.FromStatus(response.StatusCode, body, what);
        }

        private static byte[] Serialize<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        private static T Deserialize<T>(byte[] body, string what) where T : class
        {
            try
            {
                using (var stream = new MemoryStream(body))
                {
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
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

        /// <summary>
        /// A name for this connection that identifies the machine, so a key can
        /// be recognised later in the console's list.
        /// </summary>
        public static string SuggestClientName()
        {
            var machine = Environment.MachineName;
            return string.IsNullOrWhiteSpace(machine) ? "Playnite" : "Playnite on " + machine;
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }
    }
}
