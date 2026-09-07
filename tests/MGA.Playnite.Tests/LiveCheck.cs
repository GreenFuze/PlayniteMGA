using MGA.Playnite.Api;
using MGA.Playnite.GameImport;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace MGA.Playnite.Tests
{
    /// <summary>
    /// Exercises the plugin's whole server conversation against a real MGA,
    /// without Playnite in the way.
    ///
    /// The unit tests prove the rules; this proves the wire. They answer
    /// different questions and neither substitutes for the other: a DTO can be
    /// perfectly tested and still not match what the server sends.
    ///
    ///     MGA.Playnite.Tests.exe --live &lt;server&gt; &lt;access-key&gt;
    ///
    /// The key is passed as an argument and never stored, so this leaves no
    /// credential behind in the repository.
    /// </summary>
    internal static class LiveCheck
    {
        public static int Run(string server, string token)
        {
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(token))
            {
                Console.WriteLine("usage: MGA.Playnite.Tests.exe --live <server> <access-key>");
                return 2;
            }

            try
            {
                using (var client = new MgaApiClient(server, token))
                {
                    var capabilities = NegotiatedCapabilities.From(
                        client.GetCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult());

                    Console.WriteLine("Connected as '" + capabilities.ClientName + "' to API " + capabilities.ApiVersion);
                    Console.WriteLine("  profile          " + capabilities.ProfileId);
                    Console.WriteLine("  read library     " + capabilities.Has(NegotiatedCapabilities.CatalogProjection));
                    Console.WriteLine("  download artwork " + capabilities.Has(NegotiatedCapabilities.MetadataMedia));
                    Console.WriteLine("  install content  " + capabilities.Has(NegotiatedCapabilities.ContentDelivery) +
                                      MissingNote(capabilities, NegotiatedCapabilities.ContentDelivery));

                    var blocked = capabilities.BlockingReason();
                    if (blocked != null)
                    {
                        Console.WriteLine();
                        Console.WriteLine("BLOCKED: " + blocked);
                        return 1;
                    }

                    var stopwatch = Stopwatch.StartNew();
                    var games = client.GetAllGamesAsync(200, true, CancellationToken.None).GetAwaiter().GetResult();
                    stopwatch.Stop();

                    Console.WriteLine();
                    Console.WriteLine("Read " + games.Count + " games in " + stopwatch.ElapsedMilliseconds + " ms");

                    // Both settings, against the real server, because a query
                    // parameter the server quietly ignores looks exactly like
                    // one it honours until someone compares the two answers.
                    var everything = client.GetAllGamesAsync(200, false, CancellationToken.None).GetAwaiter().GetResult();
                    Console.WriteLine("  with lapsed subscriptions left out " + games.Count);
                    Console.WriteLine("  with everything shown              " + everything.Count);
                    if (everything.Count == games.Count)
                    {
                        Console.WriteLine("  (this library has no lapsed subscription games, so the setting changes nothing here)");
                    }

                    // The two properties that decide whether a refresh churns:
                    // every game must be identifiable, and no two may collide.
                    var unusable = games.Count(game => !MgaGameIdentity.IsUsable(game.Id));
                    var distinct = games
                        .Select(game => MgaGameIdentity.ToGameId(game.Id))
                        .Where(id => id != null)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    Console.WriteLine("  without a usable id   " + unusable);
                    Console.WriteLine("  distinct Playnite ids " + distinct);

                    var factory = new MgaGameMetadataFactory("MyGamesAnywhere");
                    var projected = games.Select(factory.Create).Where(metadata => metadata != null).ToList();
                    Console.WriteLine("  importable records    " + projected.Count);
                    Console.WriteLine("  with a cover          " + games.Count(g => MgaGameMetadataFactory.CoverAssetId(g) > 0));
                    Console.WriteLine("  with an icon          " + games.Count(g => MgaGameMetadataFactory.IconAssetId(g) > 0));
                    var mediaTypes = games
                        .SelectMany(g => g.Media ?? new System.Collections.Generic.List<MediaDto>())
                        .Where(m => m != null)
                        .GroupBy(m => m.Type ?? "(none)")
                        .OrderByDescending(group => group.Count());
                    Console.WriteLine("  media types seen      " +
                        string.Join(", ", mediaTypes.Select(group => group.Key + "=" + group.Count())));

                    var withCover = games.FirstOrDefault(g => MgaGameMetadataFactory.CoverAssetId(g) > 0);
                    if (withCover != null && capabilities.Has(NegotiatedCapabilities.MetadataMedia))
                    {
                        var assetId = MgaGameMetadataFactory.CoverAssetId(withCover);
                        var bytes = client.GetMediaAsync(assetId, CancellationToken.None).GetAwaiter().GetResult();
                        Console.WriteLine("  sample cover asset " + assetId + ": " +
                                          (bytes == null ? "not found" : bytes.Length + " bytes"));
                    }

                    var problems = 0;
                    if (unusable > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("PROBLEM: " + unusable + " games have no id and would be skipped on every refresh.");
                        problems++;
                    }
                    if (distinct != projected.Count)
                    {
                        Console.WriteLine();
                        Console.WriteLine("PROBLEM: ids collide — " + projected.Count + " records share " + distinct +
                                          " ids, so a refresh would merge or duplicate games.");
                        problems++;
                    }

                    Console.WriteLine();
                    Console.WriteLine(problems == 0 ? "Live check passed." : "Live check found " + problems + " problem(s).");
                    return problems == 0 ? 0 : 1;
                }
            }
            catch (MgaApiException ex)
            {
                Console.WriteLine("Live check failed (" + ex.Failure + "): " + ex.Message);
                return 1;
            }
        }

        private static string MissingNote(NegotiatedCapabilities capabilities, string feature)
        {
            var scope = capabilities.MissingScopeFor(feature);
            return string.IsNullOrWhiteSpace(scope) ? string.Empty : "  (needs " + scope + ")";
        }
    }
}
