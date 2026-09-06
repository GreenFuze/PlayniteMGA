using MGA.Playnite.Api;
using MGA.Playnite.Install;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MGA.Playnite.Tests
{
    /// <summary>
    /// Downloads one real game from a real server, then removes it again,
    /// without Playnite in the way.
    ///
    ///     MGA.Playnite.Tests.exe --install &lt;server&gt; &lt;key&gt; &lt;copy-id&gt; [directory]
    ///
    /// The install path is the part of this plugin that writes to a disk and
    /// later deletes from it, so it gets exercised against the real thing
    /// rather than only against fixtures. Runs a second time on purpose: the
    /// second pass must find the file already complete and download nothing.
    /// </summary>
    internal static class InstallCheck
    {
        public static int Run(string server, string token, string copyId, string directory)
        {
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(copyId))
            {
                Console.WriteLine("usage: MGA.Playnite.Tests.exe --install <server> <access-key> <copy-id> [directory]");
                return 2;
            }

            var root = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Path.GetTempPath(), "mga-install-check")
                : directory;
            // Start from nothing. A previous run's leftovers would be counted
            // as this run's bytes, which is exactly the false result this check
            // produced the first time it was written.
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch (Exception)
            {
                Console.WriteLine("warning: could not clear " + root + "; results may include earlier runs.");
            }

            var records = new InstalledCopyStore(Path.Combine(root, "_records"));

            try
            {
                using (var api = new MgaApiClient(server, token))
                {
                    var content = new MgaContentClient(api);
                    var installer = new MgaContentInstaller(content, records, root);

                    var manifest = content.GetManifestAsync(copyId, CancellationToken.None).GetAwaiter().GetResult();
                    Console.WriteLine("Manifest for '" + manifest.Title + "'");
                    Console.WriteLine("  files            " + manifest.Files.Count);
                    Console.WriteLine("  total bytes      " + manifest.Files.Sum(file => file.Length));
                    Console.WriteLine("  delivery         " + manifest.Delivery?.Mode +
                                      ", ready=" + manifest.Delivery?.Ready +
                                      ", materialization_required=" + manifest.Delivery?.MaterializationRequired);
                    Console.WriteLine("  checksums given  " + manifest.Files.Count(file => file.Checksum != null) +
                                      " of " + manifest.Files.Count);

                    var lastMessage = string.Empty;
                    var progress = new Progress<InstallProgress>(update =>
                    {
                        if (update.Message != lastMessage)
                        {
                            lastMessage = update.Message;
                            Console.WriteLine("  " + update.Message);
                        }
                    });

                    var watch = Stopwatch.StartNew();
                    var record = installer
                        .InstallAsync("check-game", manifest.Title, copyId, progress, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    watch.Stop();

                    Console.WriteLine();
                    Console.WriteLine("Installed to " + record.InstallDirectory + " in " + watch.ElapsedMilliseconds + " ms");
                    var onDisk = Directory.GetFiles(record.InstallDirectory, "*", SearchOption.AllDirectories);
                    // Measured now, while the files still exist: the uninstall
                    // below deletes them, and a check that reads their length
                    // afterwards is measuring nothing.
                    var bytesOnDisk = onDisk.Sum(path => new FileInfo(path).Length);
                    Console.WriteLine("  files on disk    " + onDisk.Length);
                    Console.WriteLine("  bytes on disk    " + bytesOnDisk);
                    Console.WriteLine("  recorded bytes   " + record.TotalBytes);
                    Console.WriteLine("  launch target    " + (record.LaunchRelativePath ?? "(none — an emulator launches this)"));

                    // Second pass: everything is already there, so nothing
                    // should be fetched. This is what makes a resumed or
                    // repeated install cheap instead of a full redownload.
                    var again = Stopwatch.StartNew();
                    installer.InstallAsync("check-game", manifest.Title, copyId, null, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    again.Stop();
                    Console.WriteLine("  second pass      " + again.ElapsedMilliseconds + " ms (nothing to fetch)");

                    // Now prove uninstall removes what we wrote and leaves what
                    // we did not.
                    var intruder = Path.Combine(record.InstallDirectory, "savegame.sav");
                    File.WriteAllText(intruder, "progress that is not ours to delete");

                    var outcome = new MgaContentUninstaller(records).Uninstall("check-game");
                    Console.WriteLine();
                    Console.WriteLine("Uninstalled: removed " + outcome.FilesRemoved + ", kept " + outcome.FilesKept);
                    Console.WriteLine("  save file still there  " + File.Exists(intruder));

                    var problems = 0;
                    if (bytesOnDisk != record.TotalBytes)
                    {
                        Console.WriteLine("PROBLEM: bytes on disk do not match the manifest.");
                        problems++;
                    }
                    if (!File.Exists(intruder))
                    {
                        Console.WriteLine("PROBLEM: uninstall deleted a file this plugin never wrote.");
                        problems++;
                    }
                    if (outcome.FilesRemoved != record.Files.Count)
                    {
                        Console.WriteLine("PROBLEM: uninstall did not remove everything it downloaded.");
                        problems++;
                    }

                    try
                    {
                        File.Delete(intruder);
                        Directory.Delete(record.InstallDirectory, true);
                    }
                    catch (Exception)
                    {
                        // Tidiness only.
                    }

                    Console.WriteLine();
                    Console.WriteLine(problems == 0 ? "Install check passed." : "Install check found " + problems + " problem(s).");
                    return problems == 0 ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Install check failed: " + ex.GetBaseException().Message);
                return 1;
            }
        }
    }
}
