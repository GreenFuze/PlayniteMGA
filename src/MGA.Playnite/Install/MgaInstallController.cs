using MGA.Playnite.Api;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MGA.Playnite.Install
{
    /// <summary>
    /// Playnite's Install button for a game whose files MGA can serve.
    ///
    /// Only offered for those. A Steam or Xbox title gets no install action at
    /// all, because its store owns installation and pretending otherwise would
    /// put a button in front of the user that cannot do what it says.
    /// </summary>
    internal sealed class MgaInstallController : InstallController
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private readonly IPlayniteAPI playniteApi;
        private readonly Func<MgaContentInstaller> installerFactory;
        private readonly string copyId;

        public MgaInstallController(
            Game game,
            IPlayniteAPI playniteApi,
            Func<MgaContentInstaller> installerFactory,
            string copyId)
            : base(game)
        {
            this.playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            this.installerFactory = installerFactory ?? throw new ArgumentNullException(nameof(installerFactory));
            this.copyId = copyId ?? throw new ArgumentNullException(nameof(copyId));
            Name = "Download from MyGamesAnywhere";
        }

        public override void Install(InstallActionArgs args)
        {
            InstalledCopy record = null;
            try
            {
                var result = playniteApi.Dialogs.ActivateGlobalProgress(
                    progressArgs =>
                    {
                        var installer = installerFactory();
                        var progress = new Progress<InstallProgress>(update =>
                        {
                            progressArgs.Text = update.Message;
                            progressArgs.ProgressMaxValue = update.Total;
                            progressArgs.CurrentProgressValue = update.Current;
                        });

                        record = installer
                            .InstallAsync(Game.GameId, Game.Name, copyId, progress, progressArgs.CancelToken)
                            .GetAwaiter().GetResult();
                        return Task.CompletedTask;
                    },
                    new GlobalProgressOptions("Downloading " + Game.Name + " from MyGamesAnywhere", true)
                    {
                        IsIndeterminate = false
                    });

                if (result.Canceled || result.Error is OperationCanceledException)
                {
                    // A cancelled download leaves its partial files where they
                    // are. They are what makes resuming possible, and deleting
                    // them would turn "cancel" into "throw away the 8 GB you
                    // just fetched".
                    InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
                    return;
                }
                if (result.Error != null)
                {
                    throw result.Error;
                }
                if (record == null)
                {
                    throw new InvalidOperationException("The download finished without recording what it wrote.");
                }

                ApplyLaunchAction(record);
                InvokeOnInstalled(new GameInstalledEventArgs(new GameInstallationData
                {
                    InstallDirectory = record.InstallDirectory
                }));
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "MyGamesAnywhere failed to install " + Game.Name + ".");
                playniteApi.Notifications.Add(
                    "mga-install-" + Game.Id,
                    "MyGamesAnywhere could not download " + Game.Name + ": " +
                    exception.GetBaseException().Message,
                    NotificationType.Error);
                InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
            }
        }

        /// <summary>
        /// Points Playnite at the downloaded executable, when there is one.
        ///
        /// The action is left editable rather than locked to this plugin: what
        /// to run, and with what, is a device-local decision, and the user may
        /// well know better than a file listing does. A copy with no obvious
        /// executable — a ROM, a folder of data — gets no action at all, which
        /// is correct: Playnite's emulator configuration handles those.
        /// </summary>
        private void ApplyLaunchAction(InstalledCopy record)
        {
            if (string.IsNullOrWhiteSpace(record.LaunchRelativePath))
            {
                return;
            }

            var game = playniteApi.Database.Games.Get(Game.Id);
            if (game == null)
            {
                return;
            }

            var fullPath = Path.Combine(record.InstallDirectory, record.LaunchRelativePath);
            if (!File.Exists(fullPath))
            {
                return;
            }

            var alreadyThere = game.GameActions != null &&
                game.GameActions.Any(action =>
                    action != null &&
                    action.Type == GameActionType.File &&
                    string.Equals(action.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            if (alreadyThere)
            {
                return;
            }

            var actions = game.GameActions != null
                ? new System.Collections.ObjectModel.ObservableCollection<GameAction>(game.GameActions)
                : new System.Collections.ObjectModel.ObservableCollection<GameAction>();
            actions.Add(new GameAction
            {
                Name = "Play",
                Type = GameActionType.File,
                Path = fullPath,
                WorkingDir = record.InstallDirectory,
                IsPlayAction = true
            });
            game.GameActions = actions;
            playniteApi.Database.Games.Update(game);
        }
    }
}
