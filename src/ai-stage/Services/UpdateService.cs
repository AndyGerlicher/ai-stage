using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace AiStage.Services;

internal enum UpdateStatus
{
    /// <summary>No check has completed yet (or service hasn't started).</summary>
    Unknown,
    /// <summary>Running from a non-Velopack layout (e.g. <c>dotnet run</c>); the dot is hidden.</summary>
    NotInstalled,
    /// <summary>A check is in flight. We don't render this state — keeps the title bar quiet.</summary>
    Checking,
    /// <summary>Last check found no newer release.</summary>
    UpToDate,
    /// <summary>A newer release was downloaded and is staged; clicking the dot will restart into it.</summary>
    UpdateAvailable,
    /// <summary>Last check failed (network, GitHub down, etc.). Will be retried on the next interval.</summary>
    Error,
}

internal sealed record UpdateStatusInfo(
    UpdateStatus Status,
    string? AvailableVersion = null,
    string? ErrorMessage = null,
    DateTime? LastCheckedUtc = null);

/// <summary>
/// Long-lived background updater. Periodically (every <see cref="CheckInterval"/>) asks GitHub
/// Releases for a newer ai-stage build, downloads it, and parks it for apply-on-restart.
/// Exposes a <see cref="StatusChanged"/> event so the title strip can render an up-to-date /
/// update-available indicator. No-ops cleanly in dev (where Velopack hasn't taken over the
/// install layout) and silently swallows network errors so a broken connection never blocks
/// startup or causes UI noise.
/// </summary>
internal static class UpdateService
{
    private const string RepoUrl = "https://github.com/AndyGerlicher/ai-stage";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    private static readonly object _gate = new();
    private static UpdateStatusInfo _current = new(UpdateStatus.Unknown);
    private static UpdateManager? _manager;
    private static UpdateInfo? _pending;
    private static SynchronizationContext? _syncContext;
    private static CancellationTokenSource? _cts;
    private static bool _started;

    /// <summary>
    /// Raised whenever the cached status changes. Marshaled onto whichever
    /// <see cref="SynchronizationContext"/> was current when <see cref="Start"/> was called
    /// (typically the WPF UI context), so subscribers can touch UI directly without
    /// dispatching themselves.
    /// </summary>
    public static event EventHandler<UpdateStatusInfo>? StatusChanged;

    /// <summary>Snapshot of the latest known status. Thread-safe.</summary>
    public static UpdateStatusInfo CurrentStatus
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>
    /// Kicks off the periodic check loop. Idempotent — repeated calls are ignored.
    /// Must be called from the UI thread once at startup so subsequent
    /// <see cref="StatusChanged"/> events are marshaled back to it. Falls back to
    /// <see cref="Application.Current"/>'s dispatcher context if
    /// <see cref="SynchronizationContext.Current"/> is null at call time.
    /// </summary>
    public static void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _syncContext = SynchronizationContext.Current
                ?? new System.Windows.Threading.DispatcherSynchronizationContext(
                    System.Windows.Application.Current?.Dispatcher
                    ?? System.Windows.Threading.Dispatcher.CurrentDispatcher);
            _cts = new CancellationTokenSource();
        }
        _ = RunLoopAsync(_cts!.Token);
    }

    /// <summary>
    /// Stops the periodic check loop. Best-effort; any in-flight HTTP work is allowed to
    /// finish on its own. Used from app shutdown.
    /// </summary>
    public static void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate) { cts = _cts; _cts = null; _started = false; }
        try { cts?.Cancel(); } catch { /* disposed */ }
    }

    /// <summary>
    /// When a download is staged (<see cref="UpdateStatus.UpdateAvailable"/>), tell Velopack
    /// to apply it on exit and relaunch us. Returns immediately if no update is pending.
    /// The actual process replacement happens after WPF shutdown completes — the caller is
    /// expected to call <c>Application.Current.Shutdown()</c> right after awaiting this.
    /// </summary>
    public static Task ApplyAndRestartAsync()
    {
        UpdateInfo? pending;
        UpdateManager? mgr;
        lock (_gate) { pending = _pending; mgr = _manager; }
        if (pending is null || mgr is null) return Task.CompletedTask;

        // WaitExitThenApplyUpdates spawns Update.exe which polls for our exit and then
        // performs the swap + relaunch. Do it on a worker thread so we don't block the
        // UI dispatcher even briefly.
        return Task.Run(() => mgr.WaitExitThenApplyUpdates(pending, silent: false, restart: true, restartArgs: null));
    }

    private static async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));

            // In dev (built with `dotnet run` / from `bin\Debug\...`) Velopack hasn't taken
            // over the install layout. Surface NotInstalled and stop — there's nothing to
            // poll for and the UI will hide the dot.
            if (!_manager.IsInstalled)
            {
                Publish(new UpdateStatusInfo(UpdateStatus.NotInstalled));
                return;
            }
        }
        catch (Exception ex)
        {
            Publish(new UpdateStatusInfo(UpdateStatus.Error, ErrorMessage: ex.Message));
            return;
        }

        // Use PeriodicTimer's WaitForNextTickAsync so cancellation propagates cleanly via
        // the OperationCanceledException path. First tick fires immediately by running
        // CheckOnceAsync before entering the loop.
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            await CheckOnceAsync(ct).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await CheckOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() was called.
        }
    }

    private static async Task CheckOnceAsync(CancellationToken ct)
    {
        var mgr = _manager;
        if (mgr is null) return;

        // Don't surface "Checking" once we've already reached a steady state — flickering
        // the dot back to gray on every interval would be noisy. We only show Checking
        // before the first real result lands.
        UpdateStatus prev;
        lock (_gate) prev = _current.Status;
        if (prev == UpdateStatus.Unknown)
            Publish(new UpdateStatusInfo(UpdateStatus.Checking));

        try
        {
            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            if (info is null)
            {
                lock (_gate) _pending = null;
                Publish(new UpdateStatusInfo(UpdateStatus.UpToDate, LastCheckedUtc: DateTime.UtcNow));
                return;
            }

            // Already-downloaded check: if we previously staged this same version, don't
            // re-download. Velopack handles dedup internally too, but skipping here keeps
            // the UI from flicking between states unnecessarily.
            UpdateInfo? alreadyPending;
            lock (_gate) alreadyPending = _pending;
            if (alreadyPending is null ||
                !string.Equals(alreadyPending.TargetFullRelease.FileName, info.TargetFullRelease.FileName, StringComparison.OrdinalIgnoreCase))
            {
                await mgr.DownloadUpdatesAsync(info).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }

            lock (_gate) _pending = info;
            string version = info.TargetFullRelease.Version?.ToString() ?? "?";
            Publish(new UpdateStatusInfo(UpdateStatus.UpdateAvailable, AvailableVersion: version, LastCheckedUtc: DateTime.UtcNow));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best effort — the next interval will retry. Keep any previously-staged
            // pending update intact so the user can still click to install even if a
            // later check temporarily fails.
            Publish(new UpdateStatusInfo(UpdateStatus.Error, ErrorMessage: ex.Message, LastCheckedUtc: DateTime.UtcNow));
        }
    }

    private static void Publish(UpdateStatusInfo info)
    {
        lock (_gate) _current = info;

        var handler = StatusChanged;
        if (handler is null) return;

        var ctx = _syncContext;
        if (ctx is not null && SynchronizationContext.Current != ctx)
            ctx.Post(static state =>
            {
                var (h, i) = ((EventHandler<UpdateStatusInfo>, UpdateStatusInfo))state!;
                h.Invoke(null, i);
            }, (handler, info));
        else
            handler.Invoke(null, info);
    }
}
