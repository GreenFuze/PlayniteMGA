using MGA.Playnite.Api;
using System;
using System.Linq;
using System.Threading;

namespace MGA.Playnite.Tests
{
    /// <summary>
    /// Walks the whole sign-in the settings screen performs, against a real
    /// server, without Playnite in the way.
    ///
    ///     MGA.Playnite.Tests.exe --signin &lt;server&gt; &lt;profile-name&gt; &lt;password&gt;
    ///
    /// The password is an argument and is never stored. The key it produces is
    /// printed only as a length and a prefix: this is a diagnostic, and a
    /// diagnostic that prints credentials ends up pasted into a bug report.
    /// </summary>
    internal static class SignInCheck
    {
        public static int Run(string server, string profileName, string password)
        {
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(profileName))
            {
                Console.WriteLine("usage: MGA.Playnite.Tests.exe --signin <server> <profile-name> <password>");
                return 2;
            }

            try
            {
                using (var signIn = new MgaSignIn(server))
                {
                    var profiles = signIn.ListProfilesAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Console.WriteLine("Players this server offers: " +
                        string.Join(", ", profiles.Select(profile => profile.DisplayName)));

                    var chosen = profiles.FirstOrDefault(profile =>
                        string.Equals(profile.DisplayName, profileName, StringComparison.OrdinalIgnoreCase));
                    if (chosen == null)
                    {
                        Console.WriteLine("No player called '" + profileName + "' on this server.");
                        return 1;
                    }

                    var issued = signIn
                        .SignInAsync(chosen.Id, password, "Playnite sign-in check", CancellationToken.None)
                        .GetAwaiter().GetResult();

                    Console.WriteLine();
                    Console.WriteLine("Signed in as " + chosen.DisplayName);
                    Console.WriteLine("  key name   " + issued.Name);
                    Console.WriteLine("  scopes     " + string.Join(", ", issued.Scopes ?? new System.Collections.Generic.List<string>()));
                    Console.WriteLine("  key        " + issued.Token.Length + " chars, starts " + issued.Token.Substring(0, 8));

                    // The key must actually work, or the sign-in only looked
                    // successful.
                    using (var client = new MgaApiClient(server, issued.Token))
                    {
                        var capabilities = NegotiatedCapabilities.From(
                            client.GetCapabilitiesAsync(CancellationToken.None).GetAwaiter().GetResult());
                        Console.WriteLine();
                        Console.WriteLine("The key works:");
                        Console.WriteLine("  read library     " + capabilities.Has(NegotiatedCapabilities.CatalogProjection));
                        Console.WriteLine("  download artwork " + capabilities.Has(NegotiatedCapabilities.MetadataMedia));
                        Console.WriteLine("  install content  " + capabilities.Has(NegotiatedCapabilities.ContentDelivery));

                        var games = client.GetAllGamesAsync(200, true, CancellationToken.None).GetAwaiter().GetResult();
                        Console.WriteLine("  games readable   " + games.Count);

                        if (!capabilities.Has(NegotiatedCapabilities.CatalogProjection) || games.Count == 0)
                        {
                            Console.WriteLine();
                            Console.WriteLine("PROBLEM: the key was issued but cannot read the library.");
                            return 1;
                        }
                    }

                    Console.WriteLine();
                    Console.WriteLine("Sign-in check passed.");
                    return 0;
                }
            }
            catch (MgaApiException ex)
            {
                Console.WriteLine("Sign-in check failed (" + ex.Failure + "): " + ex.Message);
                return 1;
            }
        }
    }
}
