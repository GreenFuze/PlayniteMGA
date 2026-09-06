using MGA.Playnite.Api;
using MGA.Playnite.GameImport;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using static MGA.Playnite.Tests.Harness;

namespace MGA.Playnite.Tests
{
    internal static class Program
    {
        private static readonly Guid MgaPluginId = Guid.Parse("02db01ef-50e6-49cd-8460-9ee06d324b74");
        private static readonly Guid OtherPluginId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        private static readonly Guid UnavailableTag = MgaLibraryReconciler.UnavailableTagId;

        private static int Main(string[] args)
        {
            // The unit tests must stay hermetic, so talking to a real server is
            // an explicit, separate mode rather than something the suite does
            // when a server happens to be reachable.
            if (args != null && args.Length > 0 && args[0] == "--live")
            {
                return LiveCheck.Run(
                    args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null);
            }

            Console.WriteLine("Reconciliation — what happens to a game MGA stops listing");
            ReconciliationTests();

            Console.WriteLine();
            Console.WriteLine("Identity — whether a refresh updates a record or duplicates it");
            IdentityTests();

            Console.WriteLine();
            Console.WriteLine("Capabilities — what this key may do, and what it may not");
            CapabilityTests();

            Console.WriteLine();
            Console.WriteLine("Metadata — the record Playnite imports");
            MetadataTests();

            Console.WriteLine();
            Console.WriteLine("Server address — what a person can reasonably type");
            ServerUrlTests();

            return Summarize();
        }

        // ── Reconciliation ────────────────────────────────────────────────
        //
        // This is the rule that can destroy a library, so it is tested hardest.
        // The case that matters most is an installed game vanishing from MGA:
        // a server pointed at the wrong profile, mid-reauthentication, or simply
        // misconfigured looks exactly like a library that legitimately shrank,
        // and the bytes on this machine are not MGA's to delete.

        private static void ReconciliationTests()
        {
            Test("an installed game MGA no longer lists is tagged, never removed", () =>
            {
                var game = MgaGame("game-1", installed: true);
                var plan = Plan(new[] { game }, present: new string[0]);

                AssertEqual(0, plan.GamesToRemove.Count, "an installed game was removed");
                AssertEqual(1, plan.GamesToMarkUnavailable.Count, "the installed game was not tagged unavailable");
                AssertTrue(ReferenceEquals(game, plan.GamesToMarkUnavailable[0]), "the wrong game was tagged");
            });

            Test("a game that was never installed is removed once MGA stops listing it", () =>
            {
                var game = MgaGame("game-1", installed: false);
                var plan = Plan(new[] { game }, present: new string[0]);

                AssertEqual(1, plan.GamesToRemove.Count, "a stale, uninstalled record was kept");
                AssertEqual(0, plan.GamesToMarkUnavailable.Count, "an uninstalled game was tagged instead of removed");
            });

            Test("a game that comes back has its unavailable tag cleared", () =>
            {
                var game = MgaGame("game-1", installed: true, tags: new[] { UnavailableTag });
                var plan = Plan(new[] { game }, present: new[] { "game-1" });

                AssertEqual(1, plan.GamesToMarkAvailable.Count, "a returning game kept its unavailable tag");
                AssertEqual(0, plan.GamesToRemove.Count, "a returning game was removed");
            });

            Test("an unchanged library produces no writes at all", () =>
            {
                // Churn is the failure this guards. A refresh that rewrites every
                // record touches modification dates, wakes Playnite's own change
                // handling, and makes a no-op sync look like a busy one.
                var games = new[]
                {
                    MgaGame("game-1", installed: true),
                    MgaGame("game-2", installed: false)
                };
                var plan = Plan(games, present: new[] { "game-1", "game-2" });

                AssertTrue(plan.IsEmpty, "a sync that changed nothing still planned writes");
            });

            Test("games belonging to another library are never touched", () =>
            {
                var foreign = new Game("Someone else's game")
                {
                    PluginId = OtherPluginId,
                    GameId = "game-1",
                    IsInstalled = false
                };
                var plan = Plan(new[] { foreign }, present: new string[0]);

                AssertTrue(plan.IsEmpty, "the planner reached into another plugin's records");
            });

            Test("a record with no id is never deleted", () =>
            {
                // An id-less record cannot be matched against MGA, so "not in
                // the list" says nothing about it. Removing it would delete a
                // record on the strength of a comparison never made.
                var game = new Game("No id") { PluginId = MgaPluginId, GameId = null, IsInstalled = true };
                var plan = Plan(new[] { game }, present: new[] { "game-1" });

                AssertEqual(0, plan.GamesToRemove.Count, "a record with no id was removed");
                AssertEqual(1, plan.GamesToMarkUnavailable.Count, "an installed id-less record should be tagged, not removed");
            });

            Test("an already-tagged unavailable game is not tagged twice", () =>
            {
                var game = MgaGame("game-1", installed: true, tags: new[] { UnavailableTag });
                var plan = Plan(new[] { game }, present: new string[0]);

                AssertTrue(plan.IsEmpty, "an already-tagged game was rewritten");
            });
        }

        // ── Identity ──────────────────────────────────────────────────────

        private static void IdentityTests()
        {
            Test("a Playnite id round-trips to the same MGA id", () =>
            {
                const string canonical = "7d1a4756-30fb-4a2a-acaf-b7cc82d09fc2";
                var gameId = MgaGameIdentity.ToGameId(canonical);
                AssertEqual(canonical, gameId, "the id was altered on the way out");
                AssertEqual(canonical, MgaGameIdentity.ToCanonicalGameId(gameId), "the id was altered on the way back");
            });

            Test("a blank id is unusable rather than silently becoming empty", () =>
            {
                AssertNull(MgaGameIdentity.ToGameId(null), "null became a usable id");
                AssertNull(MgaGameIdentity.ToGameId("   "), "whitespace became a usable id");
                AssertTrue(!MgaGameIdentity.IsUsable(""), "an empty id was reported usable");
            });
        }

        // ── Capabilities ──────────────────────────────────────────────────

        private static void CapabilityTests()
        {
            Test("a withheld feature reports the permission that would unlock it", () =>
            {
                var capabilities = NegotiatedCapabilities.From(new CapabilitiesResponse
                {
                    Api = new ApiIdentity { Version = "v1" },
                    Client = new ClientPrincipal { Name = "Playnite", ProfileId = "profile-1" },
                    Features = new List<Feature> { new Feature { Name = NegotiatedCapabilities.CatalogProjection } },
                    UnavailableFeatures = new List<Feature>
                    {
                        new Feature { Name = NegotiatedCapabilities.MetadataMedia, Scope = "metadata.read" }
                    }
                });

                AssertTrue(capabilities.Has(NegotiatedCapabilities.CatalogProjection), "an available feature was reported missing");
                AssertTrue(!capabilities.Has(NegotiatedCapabilities.MetadataMedia), "a withheld feature was reported available");
                AssertEqual("metadata.read", capabilities.MissingScopeFor(NegotiatedCapabilities.MetadataMedia), "the unlocking scope was lost");
            });

            Test("a key that cannot read the library says so, and names the scope", () =>
            {
                var capabilities = NegotiatedCapabilities.From(new CapabilitiesResponse
                {
                    Features = new List<Feature>(),
                    UnavailableFeatures = new List<Feature>
                    {
                        new Feature { Name = NegotiatedCapabilities.CatalogProjection, Scope = "catalog.read" }
                    }
                });

                var reason = capabilities.BlockingReason();
                AssertNotNull(reason, "a key with no catalog access was treated as usable");
                AssertTrue(reason.Contains("catalog.read"), "the reason did not name the missing scope: " + reason);
            });

            Test("a key that can read the library is not blocked", () =>
            {
                var capabilities = NegotiatedCapabilities.From(new CapabilitiesResponse
                {
                    Features = new List<Feature> { new Feature { Name = NegotiatedCapabilities.CatalogProjection } }
                });
                AssertNull(capabilities.BlockingReason(), "a usable key was reported as blocked");
            });
        }

        // ── Metadata ──────────────────────────────────────────────────────

        private static void MetadataTests()
        {
            var factory = new MgaGameMetadataFactory("MyGamesAnywhere");

            Test("a game with no id is skipped rather than imported", () =>
            {
                AssertNull(factory.Create(new GameDto { Id = "  ", Title = "Nameless" }),
                    "a game with no id was imported, and would duplicate on every refresh");
            });

            Test("the owner's chosen cover wins over the scanned one", () =>
            {
                var game = new GameDto
                {
                    Id = "game-1",
                    Media = new List<MediaDto> { new MediaDto { AssetId = 10, Type = "cover" } },
                    CoverOverride = new MediaDto { AssetId = 99, Type = "cover" }
                };
                AssertEqual(99, MgaGameMetadataFactory.CoverAssetId(game), "a deliberate cover choice was ignored");
            });

            Test("a game with no artwork imports anyway", () =>
            {
                var game = new GameDto { Id = "game-1", Title = "Bare" };
                AssertEqual(0, MgaGameMetadataFactory.CoverAssetId(game), "an absent cover was invented");
                AssertNotNull(factory.Create(game), "a game without artwork failed to import");
            });

            Test("an unparseable release date is dropped, not guessed", () =>
            {
                AssertNull(MgaGameMetadataFactory.ParseReleaseDate("sometime in the nineties"),
                    "a nonsense date was parsed into a real one");
                AssertNull(MgaGameMetadataFactory.ParseReleaseDate(""), "an empty date became a value");

                var parsed = MgaGameMetadataFactory.ParseReleaseDate("2022-03-24T00:00:00Z");
                AssertNotNull(parsed, "a valid RFC 3339 date failed to parse");
                AssertEqual(2022, parsed.Value.Year, "the parsed year is wrong");
            });

            Test("a rating outside the score range is dropped rather than clamped", () =>
            {
                var wild = factory.Create(new GameDto { Id = "game-1", Title = "Wild", Rating = 4200 });
                AssertNull(wild.CommunityScore, "an out-of-range rating was shown anyway");

                var sane = factory.Create(new GameDto { Id = "game-2", Title = "Sane", Rating = 83 });
                AssertEqual(83, sane.CommunityScore, "a valid rating was lost");
            });

            Test("each connection a game came from becomes a tag", () =>
            {
                var game = new GameDto
                {
                    Id = "game-1",
                    Title = "Tagged",
                    SourceGames = new List<SourceGameDto>
                    {
                        new SourceGameDto { IntegrationLabel = "Xbox" },
                        new SourceGameDto { IntegrationLabel = "Xbox" },
                        new SourceGameDto { IntegrationLabel = "GF Google Drive" }
                    }
                };
                var metadata = factory.Create(game);
                AssertNotNull(metadata.Tags, "a game with connections got no tags");
                AssertEqual(2, metadata.Tags.Count, "duplicate connections produced duplicate tags");
            });
        }

        // ── Server address ────────────────────────────────────────────────

        private static void ServerUrlTests()
        {
            Test("a bare host and port becomes a usable http address", () =>
            {
                var uri = MgaApiClient.NormalizeServerUrl("tv2:8900");
                AssertEqual("http", uri.Scheme, "a bare host was not given a scheme");
                AssertEqual(8900, uri.Port, "the port was lost");
            });

            Test("a trailing slash and surrounding space are tolerated", () =>
            {
                var uri = MgaApiClient.NormalizeServerUrl("  http://localhost:8900/  ");
                AssertEqual("localhost", uri.Host, "the host was misread");
                AssertEqual(8900, uri.Port, "the port was lost");
            });

            Test("an https address is left as https", () =>
            {
                var uri = MgaApiClient.NormalizeServerUrl("https://mga.example.com");
                AssertEqual("https", uri.Scheme, "an explicit https address was downgraded");
            });

            Test("the lapsed-subscription filter is sent only when it is wanted", () =>
            {
                var hiding = MgaApiClient.BuildGamesPath(0, 200, hideLapsed: true);
                AssertTrue(hiding.Contains("hide_lapsed=true"),
                    "the filter was not sent, so lapsed subscription games would be imported anyway: " + hiding);

                var showing = MgaApiClient.BuildGamesPath(0, 200, hideLapsed: false);
                AssertTrue(!showing.Contains("hide_lapsed"),
                    "the filter was sent when everything was wanted: " + showing);
            });

            Test("a redirect is a redirect, but 304 Not Modified is not", () =>
            {
                // MGA answers 307 for artwork it has a record of but no file,
                // pointing at the original provider. Following that would make
                // a LAN plugin fetch from third parties, so it is treated as
                // "not available". 304 shares the 3xx range and means the
                // opposite, so the check is by status, not by range.
                AssertTrue(MgaApiClient.IsRedirect(HttpStatusCode.TemporaryRedirect), "307 was not recognised as a redirect");
                AssertTrue(MgaApiClient.IsRedirect(HttpStatusCode.Found), "302 was not recognised as a redirect");
                AssertTrue(!MgaApiClient.IsRedirect(HttpStatusCode.NotModified), "304 was treated as a redirect");
                AssertTrue(!MgaApiClient.IsRedirect(HttpStatusCode.OK), "200 was treated as a redirect");
            });

            Test("paging is carried on the request, not assumed by the server", () =>
            {
                var path = MgaApiClient.BuildGamesPath(3, 50, hideLapsed: false);
                AssertTrue(path.Contains("page=3"), "the page number was lost: " + path);
                AssertTrue(path.Contains("page_size=50"), "the page size was lost: " + path);
            });

            Test("nonsense is refused rather than half-accepted", () =>
            {
                var refused = false;
                try
                {
                    MgaApiClient.NormalizeServerUrl("   ");
                }
                catch (ArgumentException)
                {
                    refused = true;
                }
                AssertTrue(refused, "an empty address was accepted");
            });
        }

        // ── Fixtures ──────────────────────────────────────────────────────

        private static MgaLibraryReconciliationPlan Plan(IEnumerable<Game> games, IEnumerable<string> present)
        {
            var planner = new MgaLibraryReconciliationPlanner();
            return planner.CreatePlan(
                games,
                MgaPluginId,
                new HashSet<string>(present, StringComparer.OrdinalIgnoreCase),
                game => game.IsInstalled,
                UnavailableTag);
        }

        private static Game MgaGame(string gameId, bool installed, Guid[] tags = null)
        {
            return new Game("Game " + gameId)
            {
                PluginId = MgaPluginId,
                GameId = gameId,
                IsInstalled = installed,
                TagIds = tags == null ? null : tags.ToList()
            };
        }
    }
}
