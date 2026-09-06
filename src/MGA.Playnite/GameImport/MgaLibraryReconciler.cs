using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MGA.Playnite.GameImport
{
    /// <summary>
    /// Applies a reconciliation plan to the Playnite database.
    ///
    /// A game MGA has stopped listing is marked with a tag, not deleted, when
    /// it is installed. MGA-106 requires that removal or unavailability never
    /// silently deletes local files or history, and a tag is the form of that
    /// which survives: the user sees why the game is greyed out, can still play
    /// it, and can remove it themselves if they mean to.
    /// </summary>
    internal sealed class MgaLibraryReconciler
    {
        public static readonly Guid UnavailableTagId = Guid.Parse("6f0f2c02-9d4f-4a3f-8f0f-2c029d4f4a3f");
        public const string UnavailableTagName = "MyGamesAnywhere: no longer in your library";

        private readonly IGameDatabaseAPI database;
        private readonly MgaLibraryReconciliationPlanner planner;

        public MgaLibraryReconciler(IGameDatabaseAPI database, MgaLibraryReconciliationPlanner planner)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.planner = planner ?? throw new ArgumentNullException(nameof(planner));
        }

        /// <summary>
        /// Reconciles against the ids from a sync that <em>succeeded</em>.
        ///
        /// The caller must not invoke this after a failed or partial sync. An
        /// empty id set means "MGA genuinely lists nothing", and treating a
        /// failed request as that would empty the library.
        /// </summary>
        public MgaReconciliationResult Reconcile(Guid pluginId, ISet<string> presentGameIds)
        {
            if (presentGameIds == null)
            {
                throw new ArgumentNullException(nameof(presentGameIds));
            }

            var plan = planner.CreatePlan(
                database.Games.ToList(),
                pluginId,
                presentGameIds,
                game => game.IsInstalled,
                UnavailableTagId);

            if (plan.IsEmpty)
            {
                return new MgaReconciliationResult(0, 0, 0);
            }

            using (database.BufferedUpdate())
            {
                if (plan.GamesToMarkUnavailable.Count > 0)
                {
                    EnsureUnavailableTagExists();
                    foreach (var game in plan.GamesToMarkUnavailable)
                    {
                        var tagIds = game.TagIds != null ? game.TagIds.ToList() : new List<Guid>();
                        tagIds.Add(UnavailableTagId);
                        game.TagIds = tagIds.Distinct().ToList();
                        database.Games.Update(game);
                    }
                }

                foreach (var game in plan.GamesToMarkAvailable)
                {
                    game.TagIds = game.TagIds == null
                        ? null
                        : game.TagIds.Where(tagId => tagId != UnavailableTagId).ToList();
                    database.Games.Update(game);
                }

                if (plan.GamesToRemove.Count > 0)
                {
                    database.Games.Remove(plan.GamesToRemove.ToList());
                }
            }

            return new MgaReconciliationResult(
                plan.GamesToRemove.Count,
                plan.GamesToMarkUnavailable.Count,
                plan.GamesToMarkAvailable.Count);
        }

        private void EnsureUnavailableTagExists()
        {
            if (database.Tags.Get(UnavailableTagId) != null)
            {
                return;
            }
            database.Tags.Add(new Tag(UnavailableTagName) { Id = UnavailableTagId });
        }
    }

    internal sealed class MgaReconciliationResult
    {
        public MgaReconciliationResult(int removed, int markedUnavailable, int markedAvailable)
        {
            Removed = removed;
            MarkedUnavailable = markedUnavailable;
            MarkedAvailable = markedAvailable;
        }

        public int Removed { get; }

        public int MarkedUnavailable { get; }

        public int MarkedAvailable { get; }
    }
}
