using MGA.Playnite.Api;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MGA.Playnite.GameImport
{
    /// <summary>
    /// Turns one MGA game into the record Playnite imports.
    ///
    /// Everything here is a projection of what MGA already decided. The plugin
    /// does not re-derive titles, platforms or artwork choices: MGA is the
    /// source of truth for the library, and a second opinion formed here would
    /// show up as a game that looks different in Playnite than in the console.
    /// </summary>
    internal sealed class MgaGameMetadataFactory
    {
        private readonly string sourceName;

        public MgaGameMetadataFactory(string sourceName)
        {
            this.sourceName = string.IsNullOrWhiteSpace(sourceName) ? "MyGamesAnywhere" : sourceName;
        }

        /// <summary>
        /// The Playnite record for one game, or null when the record cannot be
        /// identified — a game with no id could not be matched again on the next
        /// refresh, so importing it would create a duplicate every time.
        /// </summary>
        public GameMetadata Create(GameDto game)
        {
            if (game == null)
            {
                return null;
            }

            var gameId = MgaGameIdentity.ToGameId(game.Id);
            if (gameId == null)
            {
                return null;
            }

            var metadata = new GameMetadata
            {
                GameId = gameId,
                Name = string.IsNullOrWhiteSpace(game.Title) ? gameId : game.Title.Trim(),
                Source = new MetadataNameProperty(sourceName),
                IsInstalled = false,
                Description = NullIfBlank(game.Description),
                Developers = NameSet(game.Developer),
                Publishers = NameSet(game.Publisher),
                Genres = NameSet(game.Genres),
                Platforms = NameSet(game.Platform),
                Favorite = game.Favorite
            };

            var released = ParseReleaseDate(game.ReleaseDate);
            if (released.HasValue)
            {
                metadata.ReleaseDate = new ReleaseDate(released.Value);
            }

            // MGA rates out of 100 already; Playnite's CommunityScore is the
            // same scale, so a value outside it is dropped rather than clamped —
            // a wrong score shown confidently is worse than no score.
            if (game.Rating > 0 && game.Rating <= 100)
            {
                metadata.CommunityScore = (int)Math.Round(game.Rating);
            }

            metadata.Tags = BuildTags(game);
            return metadata;
        }

        /// <summary>
        /// Which asset carries this game's cover, preferring the owner's own
        /// override because choosing it was a deliberate act in the console.
        /// Returns 0 when there is none — a game without artwork imports fine.
        /// </summary>
        public static int CoverAssetId(GameDto game)
        {
            if (game == null)
            {
                return 0;
            }
            if (game.CoverOverride != null && game.CoverOverride.AssetId > 0)
            {
                return game.CoverOverride.AssetId;
            }
            return FirstAssetOfType(game.Media, "cover");
        }

        public static int BackgroundAssetId(GameDto game)
        {
            if (game == null)
            {
                return 0;
            }
            if (game.BackgroundOverride != null && game.BackgroundOverride.AssetId > 0)
            {
                return game.BackgroundOverride.AssetId;
            }
            return FirstAssetOfType(game.Media, "background");
        }

        private static int FirstAssetOfType(List<MediaDto> media, string type)
        {
            if (media == null)
            {
                return 0;
            }
            var match = media.FirstOrDefault(item =>
                item != null &&
                item.AssetId > 0 &&
                string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase));
            return match == null ? 0 : match.AssetId;
        }

        /// <summary>
        /// Where the game comes from, as tags, so the library stays filterable
        /// once MGA is the only connector and "which of my accounts is this on"
        /// can no longer be answered by which library a game sits in.
        /// </summary>
        private static HashSet<MetadataProperty> BuildTags(GameDto game)
        {
            var labels = (game.SourceGames ?? new List<SourceGameDto>())
                .Where(source => source != null && !string.IsNullOrWhiteSpace(source.IntegrationLabel))
                .Select(source => "MGA: " + source.IntegrationLabel.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (labels.Count == 0)
            {
                return null;
            }
            return new HashSet<MetadataProperty>(labels.Select(label => (MetadataProperty)new MetadataNameProperty(label)));
        }

        private static HashSet<MetadataProperty> NameSet(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : new HashSet<MetadataProperty> { new MetadataNameProperty(value.Trim()) };
        }

        private static HashSet<MetadataProperty> NameSet(IEnumerable<string> values)
        {
            if (values == null)
            {
                return null;
            }
            var names = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0)
            {
                return null;
            }
            return new HashSet<MetadataProperty>(names.Select(name => (MetadataProperty)new MetadataNameProperty(name)));
        }

        /// <summary>
        /// MGA sends RFC 3339. A date that will not parse is dropped rather than
        /// guessed at, because a wrong release year silently reorders a library
        /// sorted by date.
        /// </summary>
        public static DateTime? ParseReleaseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            DateTime parsed;
            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out parsed))
            {
                return parsed.Date;
            }
            return null;
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
