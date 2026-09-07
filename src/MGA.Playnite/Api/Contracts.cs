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

    /// <summary>
    /// A profile as the picker shows it, from the server's unauthenticated
    /// profile list. It carries only identity — no credential material — which
    /// is what makes it safe to read before anyone has signed in.
    /// </summary>
    // Public because the settings screen binds a list of these to a combo box,
    // and WPF cannot reach an internal type from a public view model. It is the
    // only contract here that a user ever sees.
    [DataContract]
    public sealed class ProfileDto
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "display_name")]
        public string DisplayName { get; set; }

        [DataMember(Name = "role")]
        public string Role { get; set; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;
        }
    }

    [DataContract]
    internal sealed class SignInRequest
    {
        [DataMember(Name = "profile_id")]
        public string ProfileId { get; set; }

        [DataMember(Name = "credential")]
        public string Credential { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// The key the server issues in exchange for a profile password. The token
    /// is shown once and never again, so it is stored immediately.
    /// </summary>
    [DataContract]
    internal sealed class IssuedClientDto
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "profile_id")]
        public string ProfileId { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "scopes")]
        public List<string> Scopes { get; set; }

        [DataMember(Name = "token")]
        public string Token { get; set; }
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

        /// <summary>
        /// The Microsoft Store product this game is, when MGA knows it. Present
        /// for Xbox records and the only stable handle this plugin has for
        /// sending someone to the store.
        /// </summary>
        [DataMember(Name = "store_product_id")]
        public string StoreProductId { get; set; }

        [DataMember(Name = "xcloud_available")]
        public bool XCloudAvailable { get; set; }

        [DataMember(Name = "xcloud_url")]
        public string XCloudUrl { get; set; }
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

        /// <summary>
        /// The provider's own id for this game — a Steam appid, an Xbox title
        /// id. It is what a store URI is built from.
        /// </summary>
        [DataMember(Name = "external_id")]
        public string ExternalId { get; set; }

        [DataMember(Name = "integration_label")]
        public string IntegrationLabel { get; set; }

        [DataMember(Name = "status")]
        public string Status { get; set; }

        [DataMember(Name = "url")]
        public string Url { get; set; }

        [DataMember(Name = "delivery")]
        public SourceDeliveryDto Delivery { get; set; }
    }

    /// <summary>
    /// How a source game's bytes can be reached, per delivery profile. This is
    /// a list rather than a single mode: the same copy can be direct for one
    /// profile and require materialization for another.
    /// </summary>
    [DataContract]
    internal sealed class SourceDeliveryDto
    {
        [DataMember(Name = "profiles")]
        public List<DeliveryProfileDto> Profiles { get; set; }
    }

    [DataContract]
    internal sealed class DeliveryProfileDto
    {
        [DataMember(Name = "profile")]
        public string Profile { get; set; }

        [DataMember(Name = "mode")]
        public string Mode { get; set; }

        [DataMember(Name = "prepare_required")]
        public bool PrepareRequired { get; set; }

        [DataMember(Name = "ready")]
        public bool Ready { get; set; }

        [DataMember(Name = "root_file_id")]
        public string RootFileId { get; set; }
    }

    // ── Content delivery ──────────────────────────────────────────────────

    [DataContract]
    internal sealed class ManifestDto
    {
        [DataMember(Name = "schema_version")]
        public int SchemaVersion { get; set; }

        [DataMember(Name = "copy_id")]
        public string CopyId { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        /// <summary>
        /// Identifies this exact set of files. If it changes between starting a
        /// download and finishing one, the source moved underneath us and a
        /// resumed transfer would splice two different versions together.
        /// </summary>
        [DataMember(Name = "revision")]
        public string Revision { get; set; }

        [DataMember(Name = "etag")]
        public string ETag { get; set; }

        [DataMember(Name = "delivery")]
        public ManifestDeliveryDto Delivery { get; set; }

        [DataMember(Name = "files")]
        public List<ManifestFileDto> Files { get; set; }
    }

    [DataContract]
    internal sealed class ManifestDeliveryDto
    {
        [DataMember(Name = "mode")]
        public string Mode { get; set; }

        [DataMember(Name = "ready")]
        public bool Ready { get; set; }

        [DataMember(Name = "materialization_required")]
        public bool MaterializationRequired { get; set; }
    }

    [DataContract]
    internal sealed class ManifestFileDto
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "relative_path")]
        public string RelativePath { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "role")]
        public string Role { get; set; }

        [DataMember(Name = "kind")]
        public string Kind { get; set; }

        [DataMember(Name = "length")]
        public long Length { get; set; }

        [DataMember(Name = "revision")]
        public string Revision { get; set; }

        [DataMember(Name = "etag")]
        public string ETag { get; set; }

        /// <summary>
        /// A content hash, when the source provided one. Usually absent: MGA
        /// only emits it when a file's stored revision or object id is literally
        /// a sha256 digest, and no file in a scanned Drive or share carries one.
        /// Verification therefore rests on length and revision, and a checksum
        /// is an extra check when it happens to be there.
        /// </summary>
        [DataMember(Name = "checksum")]
        public ChecksumDto Checksum { get; set; }
    }

    [DataContract]
    internal sealed class ChecksumDto
    {
        [DataMember(Name = "algorithm")]
        public string Algorithm { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }
    }

    [DataContract]
    internal sealed class PrepareResponseDto
    {
        [DataMember(Name = "immediate")]
        public bool Immediate { get; set; }

        [DataMember(Name = "job")]
        public MaterializationJobDto Job { get; set; }
    }

    [DataContract]
    internal sealed class MaterializationJobDto
    {
        [DataMember(Name = "job_id")]
        public string JobId { get; set; }

        [DataMember(Name = "copy_id")]
        public string CopyId { get; set; }

        [DataMember(Name = "status")]
        public string Status { get; set; }

        [DataMember(Name = "message")]
        public string Message { get; set; }

        [DataMember(Name = "error_code")]
        public string ErrorCode { get; set; }

        [DataMember(Name = "progress_current")]
        public int ProgressCurrent { get; set; }

        [DataMember(Name = "progress_total")]
        public int ProgressTotal { get; set; }
    }
}
