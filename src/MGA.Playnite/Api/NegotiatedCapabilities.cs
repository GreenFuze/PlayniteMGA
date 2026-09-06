using System;
using System.Collections.Generic;
using System.Linq;

namespace MGA.Playnite.Api
{
    /// <summary>
    /// What this connection is actually allowed to do, resolved once against the
    /// server rather than guessed from the scopes on the key.
    ///
    /// MGA answers capability discovery with a token but no particular scope, so
    /// a client can always learn what it is missing without already having it.
    /// Both halves are kept: what is reachable, and what exists but is withheld
    /// along with the permission that would unlock it. Discarding the second
    /// half would reduce every refusal to a bare 403 with no remedy.
    /// </summary>
    internal sealed class NegotiatedCapabilities
    {
        public const string CatalogProjection = "catalog-projection";
        public const string MetadataMedia = "metadata-media";
        public const string ContentDelivery = "content-delivery";
        public const string CachePreparation = "cache-preparation";

        private readonly HashSet<string> available;
        private readonly Dictionary<string, string> withheldScopeByFeature;

        private NegotiatedCapabilities(
            string apiVersion,
            string profileId,
            string clientName,
            HashSet<string> available,
            Dictionary<string, string> withheldScopeByFeature)
        {
            ApiVersion = apiVersion;
            ProfileId = profileId;
            ClientName = clientName;
            this.available = available;
            this.withheldScopeByFeature = withheldScopeByFeature;
        }

        public string ApiVersion { get; }

        public string ProfileId { get; }

        public string ClientName { get; }

        public bool Has(string feature)
        {
            return available.Contains(feature);
        }

        /// <summary>
        /// The permission that would unlock a feature this key does not reach,
        /// or null if the feature is available or the server never mentioned it.
        /// </summary>
        public string MissingScopeFor(string feature)
        {
            string scope;
            return withheldScopeByFeature.TryGetValue(feature, out scope) ? scope : null;
        }

        public static NegotiatedCapabilities From(CapabilitiesResponse response)
        {
            if (response == null)
            {
                throw new MgaApiException(
                    MgaFailure.Malformed,
                    "MyGamesAnywhere did not describe its capabilities, so the plugin cannot tell what this key may do.");
            }

            var available = new HashSet<string>(
                (response.Features ?? new List<Feature>())
                    .Where(feature => feature != null && !string.IsNullOrWhiteSpace(feature.Name))
                    .Select(feature => feature.Name),
                StringComparer.OrdinalIgnoreCase);

            var withheld = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var feature in response.UnavailableFeatures ?? new List<Feature>())
            {
                if (feature == null || string.IsNullOrWhiteSpace(feature.Name))
                {
                    continue;
                }
                withheld[feature.Name] = feature.Scope;
            }

            return new NegotiatedCapabilities(
                response.Api == null ? null : response.Api.Version,
                response.Client == null ? null : response.Client.ProfileId,
                response.Client == null ? null : response.Client.Name,
                available,
                withheld);
        }

        /// <summary>
        /// Why a library sync cannot run, or null when it can. Reading the
        /// catalog is the one capability without which there is nothing to
        /// import; artwork is merely nice to have and its absence is reported
        /// per game rather than stopping the sync.
        /// </summary>
        public string BlockingReason()
        {
            if (Has(CatalogProjection))
            {
                return null;
            }

            var scope = MissingScopeFor(CatalogProjection);
            return string.IsNullOrWhiteSpace(scope)
                ? "This access key cannot read your MyGamesAnywhere library."
                : "This access key cannot read your MyGamesAnywhere library. Issue a key that includes the '" + scope + "' permission.";
        }
    }
}
