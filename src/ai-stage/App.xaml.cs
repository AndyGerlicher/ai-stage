using System.IO;
using System.Windows;
using AiStage.Services;

namespace AiStage;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // VelopackApp.Build().Run() runs in Program.Main before this point.
        // Fire-and-forget background update check. Applies on next exit.
        _ = UpdateService.CheckAsync();

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        // First run: no persisted config yet → walk the user through the
        // settings dialog before opening MainWindow. If they cancel we let
        // them in with sensible defaults but don't persist, so they get the
        // prompt again next launch (until they Save once).
        bool isFirstRun = !ConfigService.Exists();
        if (isFirstRun)
        {
            var seed = new StageConfig();
            var dlg = new SettingsDialog(seed);
            if (dlg.ShowDialog() == true && dlg.Result is not null)
                ConfigService.Save(dlg.Result);
        }

        var config = ConfigService.Load();
        string rootPath = config.RootPath;

        if (e.Args.Length > 0)
        {
            string candidate = e.Args[0];
            if (Directory.Exists(candidate))
                rootPath = Path.GetFullPath(candidate);
        }

        var window = new MainWindow
        {
            RootPath = rootPath,
            DefaultAgentProvider = config.DefaultAgentProvider,
            // Nullable on the JSON model to distinguish "missing key" (→ default)
            // from "explicit empty" (→ no prefix); ConfigService.Load guarantees
            // a non-null normalized value here.
            BranchPrefix = config.BranchPrefix!,
            WorktreeResetCommands = config.WorktreeResetCommands ?? StageConfig.DefaultWorktreeResetCommands,
            ConsoleShell = config.ConsoleShell,
            ConsoleInitCommand = config.ConsoleInitCommand,
            AgentArgs = config.AgentArgs ?? new System.Collections.Generic.Dictionary<string, string>(),
        };
        MainWindow = window;
        window.Show();
    }
}
