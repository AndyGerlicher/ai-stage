using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace AiStage.Services;

/// <summary>
/// Best-effort background updater. On every launch, checks GitHub Releases
/// for a newer ai-stage build and (if one is found) downloads it and stages
/// it to be applied the next time the app exits. No-ops in dev builds
/// (where the running exe wasn't installed via Velopack) and silently
/// swallows network errors so a broken connection never blocks startup.
/// </summary>
internal static class UpdateService
{
    private const string RepoUrl = "https://github.com/AndyGerlicher/ai-stage";

    /// <summary>
    /// Fire-and-forget the update check. Safe to call from any thread.
    /// </summary>
    public static Task CheckAsync() => Task.Run(CheckCoreAsync);

    private static async Task CheckCoreAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));

            // In dev (built with `dotnet run` / from `bin\Debug\...`) Velopack hasn't
            // taken over the install layout. Skip — there's nothing to update.
            if (!mgr.IsInstalled) return;

            var info = await mgr.CheckForUpdatesAsync();
            if (info is null) return;

            await mgr.DownloadUpdatesAsync(info);

            // Apply on next exit. The user keeps working in this session; the
            // next launch comes up on the new version. Avoids tearing the
            // window out from under the user mid-task.
            mgr.WaitExitThenApplyUpdates(info);
        }
        catch
        {
            // Best effort. Update will be retried on the next launch.
        }
    }
}
