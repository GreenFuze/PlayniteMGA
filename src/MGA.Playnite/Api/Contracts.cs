using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MGA.Playnite.Api
{
    // These mirror the JSON that MGA's scoped frontend API actually returns.
    // Only the fields this plugin reads are declared: an unknown field is
    // ignored by the serializer, so the server can add fields without breaking
    // an installed plugin, which is the compatibility property that matters for
    // something a user upgrades on their own schedule.

    [DataContract]
    internal sealed class CapabilitiesResponse
    {
        [DataMember(Name = "api")]
        public ApiIdentity Api { get; set; }

        [DataMember(Name = "client")]
        public ClientPrincipal Client { get; set; }

        [DataMember(Name = "features")]
        public List<Feature> Features { get; set; }

        [DataMember(Name = "unavailable_features")]
        public List<Feature> UnavailableFeatures { get; set; }
    }

    [DataContract]
    internal sealed class ApiIdentity
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }
    }

    [DataContract]
    internal sealed class ClientPrincipal
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "profile_id")]
        public string ProfileId { get; set; }

        [DataMember(Name = "scopes")]
        public List<string> Scopes { get; set; }
    }

    /// <summary>
    /// One negotiable capability. A withheld feature still arrives, carrying the
    /// scope that would unlock it, so the plugin can say what is missing instead
    /// of only reporting that something returned 403.
    /// </summary>
    [DataContract]
    internal sealed class Feature
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "scope")]
        public string Scope { get; set; }
    }

    [DataContract]
    internal sealed class ListGamesResponse
    {
        [DataMember(Name = "total")]
        public int Total { get; set; }

        [DataMember(Name = "page")]
        public int Page { get; set; }

        [DataMember(Name = "page_size")]
        public int PageSize { get; set; }

        [DataMember(Name = "games")]
        public List<GameDto> Games { get; set; }
    }

    [DataContract]
    internal sealed class GameDto
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "platform")]
        public string Platform { get; set; }

        [DataMember(Name = "favorite")]
        public bool Favorite { get; set; }

        [DataMember(Name = "description")]
        public string Description { get; set; }

        [DataMember(Name = "release_date")]
        public string ReleaseDate { get; set; }

        [DataMember(Name = "genres")]
        public List<string> Genres { get; set; }

        [DataMember(Name = "developer")]
        public string Developer { get; set; }

        [DataMember(Name = "publisher")]
        public string Publisher { get; set; }

        [DataMember(Name = "rating")]
        public double Rating { get; set; }

        [DataMember(Name = "media")]
        public List<MediaDto> Media { get; set; }

        [DataMember(Name = "cover_override")]
        public MediaDto CoverOverride { get; set; }

        [DataMember(Name = "background_override")]
        public MediaDto BackgroundOverride { get; set; }

        [DataMember(Name = "source_games")]
        public List<SourceGameDto> SourceGames { get; set; }
    }

    [DataContract]
    internal sealed class MediaDto
    {
        [DataMember(Name = "asset_id")]
        public int AssetId { get; set; }

        [DataMember(Name = "type")]
        public string Type { get; set; }

        /// <summary>
        /// The provider's own URL, which is evidence of where the artwork came
        /// from and not a route this plugin may fetch. Bytes come from the
        /// server by <see cref="AssetId"/> through the scoped media route.
        /// </summary>
        [DataMember(Name = "url")]
        public string ProviderUrl { get; set; }
    }

    [DataContract]
    internal sealed class SourceGameDto
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "plugin_id")]
        public string PluginId { get; set; }

        [DataMember(Name = "integration_label")]
        public string IntegrationLabel { get; set; }

        [DataMember(Name = "status")]
        public string Status { get; set; }

        [DataMember(Name = "url")]
        public string Url { get; set; }

        [DataMember(Name = "delivery")]
        public DeliveryDto Delivery { get; set; }
    }

    [DataContract]
    internal sealed class DeliveryDto
    {
        [DataMember(Name = "mode")]
        public string Mode { get; set; }

        [DataMember(Name = "ready")]
        public bool Ready { get; set; }
    }
}
