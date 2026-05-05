using System;
using Velopack;

namespace AiStage;

/// <summary>
/// Custom entry point so VelopackApp.Build().Run() runs before any WPF
/// types are touched. Velopack hooks here for first-run, install, and
/// update scenarios, and emits a warning if it's invoked from anywhere
/// other than the app's entry method.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
