using System;

namespace MGA.Playnite.GameImport
{
    /// <summary>
    /// How an MGA game and a Playnite record are tied together.
    ///
    /// Playnite matches an imported game to an existing record by
    /// (PluginId, GameId), so GameId decides whether a refresh updates a record
    /// or creates a second one beside it. It is therefore MGA's canonical game
    /// id verbatim: that id already survives a source being re-scanned, moved
    /// between folders, split from or merged into another game, which is exactly
    /// the set of events that would otherwise duplicate a library.
    ///
    /// Nothing is derived from the title. Two games can share a title, one game
    /// can be renamed by a metadata refresh, and either would silently split or
    /// merge records on the next import.
    /// </summary>
    internal static class MgaGameIdentity
    {
        /// <summary>
        /// The Playnite GameId for an MGA game, or null when the server sent a
        /// record with no usable id — which must be skipped rather than
        /// imported under an invented id that the next refresh would not match.
        /// </summary>
        public static string ToGameId(string canonicalGameId)
        {
            if (string.IsNullOrWhiteSpace(canonicalGameId))
            {
                return null;
            }
            return canonicalGameId.Trim();
        }

        /// <summary>
        /// The MGA game a Playnite record refers to. The inverse of
        /// <see cref="ToGameId"/>, and deliberately just as literal: any
        /// encoding here would be a second place for the two sides to disagree.
        /// </summary>
        public static string ToCanonicalGameId(string gameId)
        {
            return string.IsNullOrWhiteSpace(gameId) ? null : gameId.Trim();
        }

        public static bool IsUsable(string canonicalGameId)
        {
            return ToGameId(canonicalGameId) != null;
        }
    }
}
