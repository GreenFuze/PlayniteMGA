using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MGA.Playnite.GameImport
{
    /// <summary>
    /// What to do about Playnite records whose game MGA no longer lists.
    ///
    /// Deciding this is separated from doing it so the rule can be tested
    /// against ordinary objects, with no Playnite database in the way. The rule
    /// is the part that can quietly destroy someone's library, so it is the part
    /// that gets tested hardest.
    /// </summary>
    internal sealed class MgaLibraryReconciliationPlanner
    {
        /// <summary>
        /// Builds the plan.
        /// </summary>
        /// <param name="allGames">Every game in the Playnite database.</param>
        /// <param name="pluginId">Only this plugin's records are ever touched.</param>
        /// <param name="presentGameIds">The MGA ids returned by a sync that succeeded.</param>
        /// <param name="isInstalled">
        /// Whether a record has local files. An installed game is never removed
        /// however long MGA has stopped listing it: the bytes are on this
        /// machine, the play history belongs to the user, and a server that is
        /// misconfigured, re-authenticating, or pointed at the wrong profile
        /// looks exactly like a library that legitimately shrank.
        /// </param>
        public MgaLibraryReconciliationPlan CreatePlan(
            IEnumerable<Game> allGames,
            Guid pluginId,
            ISet<string> presentGameIds,
            Func<Game, bool> isInstalled,
            Guid unavailableTagId)
        {
            if (presentGameIds == null)
            {
                throw new ArgumentNullException(nameof(presentGameIds));
            }
            if (isInstalled == null)
            {
                throw new ArgumentNullException(nameof(isInstalled));
            }

            var toRemove = new List<Game>();
            var toMarkUnavailable = new List<Game>();
            var toMarkAvailable = new List<Game>();

            foreach (var game in allGames ?? Enumerable.Empty<Game>())
            {
                if (game == null || game.PluginId != pluginId)
                {
                    continue;
                }

                var canonicalId = MgaGameIdentity.ToCanonicalGameId(game.GameId);
                var present = canonicalId != null && presentGameIds.Contains(canonicalId);
                var taggedUnavailable = game.TagIds != null && game.TagIds.Contains(unavailableTagId);

                if (present)
                {
                    // Back in the library: clear the mark, but only if it is
                    // there, so an unchanged game is not rewritten on every
                    // refresh just to set a field to what it already holds.
                    if (taggedUnavailable)
                    {
                        toMarkAvailable.Add(game);
                    }
                    continue;
                }

                if (isInstalled(game))
                {
                    if (!taggedUnavailable)
                    {
                        toMarkUnavailable.Add(game);
                    }
                    continue;
                }

                toRemove.Add(game);
            }

            return new MgaLibraryReconciliationPlan(toRemove, toMarkUnavailable, toMarkAvailable);
        }
    }

    internal sealed class MgaLibraryReconciliationPlan
    {
        public MgaLibraryReconciliationPlan(
            IReadOnlyList<Game> gamesToRemove,
            IReadOnlyList<Game> gamesToMarkUnavailable,
            IReadOnlyList<Game> gamesToMarkAvailable)
        {
            GamesToRemove = gamesToRemove ?? new List<Game>();
            GamesToMarkUnavailable = gamesToMarkUnavailable ?? new List<Game>();
            GamesToMarkAvailable = gamesToMarkAvailable ?? new List<Game>();
        }

        public IReadOnlyList<Game> GamesToRemove { get; }

        public IReadOnlyList<Game> GamesToMarkUnavailable { get; }

        public IReadOnlyList<Game> GamesToMarkAvailable { get; }

        public bool IsEmpty
        {
            get
            {
                return GamesToRemove.Count == 0 &&
                       GamesToMarkUnavailable.Count == 0 &&
                       GamesToMarkAvailable.Count == 0;
            }
        }
    }
}
