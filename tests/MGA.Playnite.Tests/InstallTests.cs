using MGA.Playnite.Api;
using MGA.Playnite.Install;
using System;
using System.Collections.Generic;
using System.IO;
using static MGA.Playnite.Tests.Harness;

namespace MGA.Playnite.Tests
{
    internal static class InstallTests
    {
        public static void Run()
        {
            Console.WriteLine("Routing — which games this plugin may offer to install");
            RoutingTests();

            Console.WriteLine();
            Console.WriteLine("Paths — where downloaded files are allowed to land");
            PathTests();

            Console.WriteLine();
            Console.WriteLine("Uninstall — what may be deleted");
            UninstallTests();
        }

        // ── Routing ───────────────────────────────────────────────────────

        private static void RoutingTests()
        {
            Test("a game on a drive is installable, and names the copy to fetch", () =>
            {
                var game = new GameDto
                {
                    Id = "game-1",
                    SourceGames = new List<SourceGameDto>
                    {
                        new SourceGameDto { Id = "scan:abc", PluginId = "game-source-google-drive", Status = "found" }
                    }
                };
                var route = ContentRoute.For(game);
                AssertEqual(ContentRouteKind.Files, route.Kind, "a drive-backed game was not routed to files");
                AssertEqual("scan:abc", route.CopyId, "the copy id was lost");
            });

            Test("a Steam game goes to Steam, not to a download", () =>
            {
                // MGA holds no bytes for a Steam title, only the knowledge that
                // the account owns it. Offering Install would be a button that
                // cannot do what it says.
                var game = new GameDto
                {
                    Id = "game-1",
                    SourceGames = new List<SourceGameDto>
                    {
                        new SourceGameDto { Id = "scan:s", PluginId = "game-source-steam", ExternalId = "346010" }
                    }
                };
                var route = ContentRoute.For(game);
                AssertEqual(ContentRouteKind.Store, route.Kind, "a Steam game was offered as a download");
                AssertEqual("steam://rungameid/346010", route.StoreUrl, "the Steam launch URI is wrong");
            });

            Test("an Xbox game goes to the Microsoft Store", () =>
            {
                var game = new GameDto
                {
                    Id = "game-1",
                    StoreProductId = "9NGLST31DG26",
                    SourceGames = new List<SourceGameDto>
                    {
                        new SourceGameDto { Id = "scan:x", PluginId = "game-source-xbox", ExternalId = "2031971566" }
                    }
                };
                var route = ContentRoute.For(game);
                AssertEqual(ContentRouteKind.Store, route.Kind, "an Xbox game was offered as a download");
                AssertTrue(route.StoreUrl.Contains("9NGLST31DG26"), "the store URI does not name the product: " + route.StoreUrl);
            });

            Test("files win when a game is both on a drive and in a store", () =>
            {
                var game = new GameDto
                {
                    Id = "game-1",
                    SourceGames = new List<SourceGameDto>
                    {
                        new SourceGameDto { Id = "scan:s", PluginId = "game-source-steam", ExternalId = "1" },
                        new SourceGameDto { Id = "scan:d", PluginId = "game-source-google-drive", Status = "found" }
                    }
                };
                AssertEqual(ContentRouteKind.Files, ContentRoute.For(game).Kind,
                    "a game MGA can actually serve was sent to a store instead");
            });

            Test("a source the last scan could not find is not offered", () =>
            {
                var game = new GameDto
                {
                    Id = "game-1",
                    SourceGames = new List<SourceGameDto>
                    {
                        new SourceGameDto { Id = "scan:d", PluginId = "game-source-google-drive", Status = "not_found" }
                    }
                };
                AssertEqual(ContentRouteKind.None, ContentRoute.For(game).Kind,
                    "a game whose files are missing was offered as a download");
            });

            Test("an Xbox game with no store product has no action at all", () =>
            {
                var game = new GameDto
                {
                    Id = "game-1",
                    SourceGames = new List<SourceGameDto>
                    {
                        new SourceGameDto { Id = "scan:x", PluginId = "game-source-xbox" }
                    }
                };
                AssertEqual(ContentRouteKind.None, ContentRoute.For(game).Kind,
                    "a store URI was invented without a product id");
            });
        }

        // ── Paths ─────────────────────────────────────────────────────────

        private static void PathTests()
        {
            Test("a file escaping the install folder is refused", () =>
            {
                // Manifest paths come from a scanned filesystem, so this shape
                // is something a malformed or hostile source can produce. A
                // downloader that honoured it would write wherever it was told.
                var refused = false;
                try
                {
                    MgaContentInstaller.ResolveWithin(@"C:\Games\Alpha", @"..\..\Windows\System32\evil.dll");
                }
                catch (InvalidOperationException)
                {
                    refused = true;
                }
                AssertTrue(refused, "a path traversing out of the install folder was accepted");
            });

            Test("an ordinary nested path resolves inside the install folder", () =>
            {
                var resolved = MgaContentInstaller.ResolveWithin(@"C:\Games\Alpha", "data/textures/hero.png");
                AssertTrue(resolved.StartsWith(@"C:\Games\Alpha\", StringComparison.OrdinalIgnoreCase),
                    "a normal path did not land inside the install folder: " + resolved);
                AssertTrue(resolved.EndsWith(@"hero.png", StringComparison.OrdinalIgnoreCase),
                    "the file name was lost: " + resolved);
            });

            Test("two games with the same title get different folders", () =>
            {
                var first = MgaContentInstaller.SafeFolderName("Alone in the Dark", "11111111-2222-3333-4444-555555555555");
                var second = MgaContentInstaller.SafeFolderName("Alone in the Dark", "99999999-8888-7777-6666-555555555555");
                AssertTrue(first != second, "two different games would share an install folder");
            });

            Test("a title full of illegal characters still makes a folder name", () =>
            {
                var name = MgaContentInstaller.SafeFolderName("Where: is my <hero>? \\ /", "abc");
                foreach (var illegal in Path.GetInvalidFileNameChars())
                {
                    AssertTrue(name.IndexOf(illegal) < 0, "the folder name kept an illegal character: " + name);
                }
            });

            Test("the root executable is chosen as the launch target", () =>
            {
                var files = new List<ManifestFileDto>
                {
                    new ManifestFileDto { RelativePath = "readme.txt", Name = "readme.txt", Length = 10 },
                    new ManifestFileDto { RelativePath = "tools/edit.exe", Name = "edit.exe", Kind = "executable", Length = 100 },
                    new ManifestFileDto { RelativePath = "game.exe", Name = "game.exe", Kind = "executable", Role = "root", Length = 50 }
                };
                AssertEqual("game.exe", MgaContentInstaller.ChooseLaunchTarget(files),
                    "the source's own root file was not preferred");
            });

            Test("a copy with nothing runnable gets no launch target", () =>
            {
                // A ROM or a folder of data is launched by an emulator Playnite
                // configures. Inventing a play action here would point it at
                // whatever file happened to be biggest.
                var files = new List<ManifestFileDto>
                {
                    new ManifestFileDto { RelativePath = "game.rom", Name = "game.rom", Length = 100 }
                };
                AssertNull(MgaContentInstaller.ChooseLaunchTarget(files), "a launch target was invented for a ROM");
            });
        }

        // ── Uninstall ─────────────────────────────────────────────────────

        private static void UninstallTests()
        {
            Test("only the files we downloaded are removed", () =>
            {
                using (var sandbox = new Sandbox())
                {
                    var installDirectory = sandbox.Directory("install");
                    File.WriteAllText(Path.Combine(installDirectory, "game.exe"), "1234567890");
                    File.WriteAllText(Path.Combine(installDirectory, "savegame.sav"), "someone's progress");

                    var records = new InstalledCopyStore(sandbox.Directory("records"));
                    records.Save(new InstalledCopy
                    {
                        GameId = "game-1",
                        InstallDirectory = installDirectory,
                        Files = new List<InstalledFile>
                        {
                            new InstalledFile { RelativePath = "game.exe", Length = 10 }
                        }
                    });

                    var outcome = new MgaContentUninstaller(records).Uninstall("game-1");

                    AssertEqual(1, outcome.FilesRemoved, "the downloaded file was not removed");
                    AssertTrue(!File.Exists(Path.Combine(installDirectory, "game.exe")), "the downloaded file survived");
                    AssertTrue(File.Exists(Path.Combine(installDirectory, "savegame.sav")),
                        "a save file that this plugin never wrote was deleted");
                }
            });

            Test("a file changed since we wrote it is kept", () =>
            {
                using (var sandbox = new Sandbox())
                {
                    var installDirectory = sandbox.Directory("install");
                    File.WriteAllText(Path.Combine(installDirectory, "game.exe"), "this has been patched since");

                    var records = new InstalledCopyStore(sandbox.Directory("records"));
                    records.Save(new InstalledCopy
                    {
                        GameId = "game-1",
                        InstallDirectory = installDirectory,
                        Files = new List<InstalledFile>
                        {
                            new InstalledFile { RelativePath = "game.exe", Length = 10 }
                        }
                    });

                    var outcome = new MgaContentUninstaller(records).Uninstall("game-1");

                    AssertEqual(0, outcome.FilesRemoved, "a modified file was deleted");
                    AssertEqual(1, outcome.FilesKept, "the modified file was not reported as kept");
                    AssertTrue(File.Exists(Path.Combine(installDirectory, "game.exe")), "a patched or modded file was destroyed");
                }
            });

            Test("with no record of installing it, nothing is deleted", () =>
            {
                using (var sandbox = new Sandbox())
                {
                    var installDirectory = sandbox.Directory("install");
                    File.WriteAllText(Path.Combine(installDirectory, "someone-elses-game.exe"), "not ours");

                    var records = new InstalledCopyStore(sandbox.Directory("records"));
                    var outcome = new MgaContentUninstaller(records).Uninstall("game-1");

                    AssertTrue(!outcome.HadRecord, "an install record was invented");
                    AssertEqual(0, outcome.FilesRemoved, "files were deleted with no record of having written them");
                    AssertTrue(File.Exists(Path.Combine(installDirectory, "someone-elses-game.exe")),
                        "a file this plugin never wrote was deleted");
                }
            });

            Test("an install record survives being written and read back", () =>
            {
                using (var sandbox = new Sandbox())
                {
                    var records = new InstalledCopyStore(sandbox.Directory("records"));
                    records.Save(new InstalledCopy
                    {
                        GameId = "game-1",
                        CopyId = "scan:abc",
                        ManifestRevision = "rev-1",
                        InstallDirectory = @"C:\Games\Alpha",
                        LaunchRelativePath = "game.exe",
                        Files = new List<InstalledFile> { new InstalledFile { RelativePath = "game.exe", Length = 42 } }
                    });

                    var loaded = records.Find("game-1");
                    AssertNotNull(loaded, "the install record could not be read back");
                    AssertEqual("scan:abc", loaded.CopyId, "the copy id was lost");
                    AssertEqual("rev-1", loaded.ManifestRevision, "the manifest revision was lost");
                    AssertEqual(42L, loaded.TotalBytes, "the recorded size was lost");
                }
            });
        }

        /// <summary>A temporary directory that removes itself.</summary>
        private sealed class Sandbox : IDisposable
        {
            private readonly string root;

            public Sandbox()
            {
                root = Path.Combine(Path.GetTempPath(), "mga-tests-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(root);
            }

            public string Directory(string name)
            {
                var path = Path.Combine(root, name);
                System.IO.Directory.CreateDirectory(path);
                return path;
            }

            public void Dispose()
            {
                try
                {
                    System.IO.Directory.Delete(root, true);
                }
                catch (Exception)
                {
                    // A leftover temp folder is not worth failing a test run.
                }
            }
        }
    }
}
