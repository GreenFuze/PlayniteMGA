using System;
using System.Net;

namespace MGA.Playnite.Api
{
    /// <summary>
    /// A call to MGA that did not succeed, carrying enough to tell the user
    /// which of the three very different problems they have: the server is not
    /// reachable, the token is no longer accepted, or the token is accepted but
    /// lacks the scope for what was asked.
    ///
    /// These are separated because the remedies are unrelated — check the
    /// address, issue a new client, or re-issue with more scopes — and a single
    /// "could not sync" message would leave the user guessing between them.
    /// </summary>
    internal sealed class MgaApiException : Exception
    {
        public MgaApiException(MgaFailure failure, string message, Exception inner = null)
            : base(message, inner)
        {
            Failure = failure;
        }

        public MgaFailure Failure { get; }

        public static MgaApiException FromStatus(HttpStatusCode status, string body, string what)
        {
            switch (status)
            {
                case HttpStatusCode.Unauthorized:
                    return new MgaApiException(
                        MgaFailure.Unauthenticated,
                        "MyGamesAnywhere rejected this connection. The access key may have been revoked or expired; issue a new one from the MGA console and paste it here.");
                case HttpStatusCode.Forbidden:
                    return new MgaApiException(
                        MgaFailure.Forbidden,
                        "This access key is valid but is not permitted to " + what + ". Issue a key that includes the required permission.");
                default:
                    return new MgaApiException(
                        MgaFailure.Server,
                        "MyGamesAnywhere could not " + what + " (HTTP " + (int)status + "). " + Summarize(body));
            }
        }

        /// <summary>
        /// Server error bodies can be long; a message box is not a log file.
        /// </summary>
        private static string Summarize(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return string.Empty;
            }

            var trimmed = body.Trim();
            return trimmed.Length <= 200 ? trimmed : trimmed.Substring(0, 200) + "…";
        }
    }

    internal enum MgaFailure
    {
        Unreachable,
        Unauthenticated,
        Forbidden,
        Server,
        Malformed
    }
}
