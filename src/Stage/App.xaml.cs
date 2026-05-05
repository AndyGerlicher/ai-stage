using System.IO;
using System.Windows;
using Stage.Services;
using Velopack;

namespace Stage;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        VelopackApp.Build().Run();

        ShutdownMode = ShutdownMode.OnMainWindowClose;

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
        };
        MainWindow = window;
        window.Show();
    }
}
