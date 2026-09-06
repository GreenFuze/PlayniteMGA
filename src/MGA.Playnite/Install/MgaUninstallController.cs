using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Threading.Tasks;

namespace MGA.Playnite.Install
{
    internal sealed class MgaUninstallController : UninstallController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI playniteApi;
        private readonly MgaContentUninstaller uninstaller;

        public MgaUninstallController(Game game, IPlayniteAPI playniteApi, MgaContentUninstaller uninstaller)
            : base(game)
        {
            this.playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            this.uninstaller = uninstaller ?? throw new ArgumentNullException(nameof(uninstaller));
            Name = "Remove the copy MyGamesAnywhere downloaded";
        }

        public override async void Uninstall(UninstallActionArgs args)
        {
            try
            {
                var outcome = await Task.Run(() => uninstaller.Uninstall(Game.GameId)).ConfigureAwait(false);

                if (!outcome.HadRecord)
                {
                    // Marking it uninstalled is still right — Playnite's flag
                    // was wrong — but nothing was deleted, and saying so avoids
                    // the user believing a folder has been cleared when it has
                    // not.
                    playniteApi.Notifications.Add(
                        "mga-uninstall-norecord-" + Game.Id,
                        Game.Name + " is now marked as not installed. MyGamesAnywhere has no record of downloading it, so no files were removed.",
                        NotificationType.Info);
                }
                else if (outcome.FilesKept > 0)
                {
                    playniteApi.Notifications.Add(
                        "mga-uninstall-kept-" + Game.Id,
                        Game.Name + ": removed " + outcome.FilesRemoved + " downloaded file(s). " +
                        outcome.FilesKept + " were left in " + outcome.InstallDirectory +
                        " because they had changed since they were downloaded — saves, patches or mods are kept rather than deleted.",
                        NotificationType.Info);
                }

                InvokeOnUninstalled(new GameUninstalledEventArgs());
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "MyGamesAnywhere failed to uninstall " + Game.Name + ".");
                playniteApi.Notifications.Add(
                    "mga-uninstall-" + Game.Id,
                    "MyGamesAnywhere did not remove " + Game.Name + ": " + exception.GetBaseException().Message,
                    NotificationType.Error);
            }
        }
    }
}
