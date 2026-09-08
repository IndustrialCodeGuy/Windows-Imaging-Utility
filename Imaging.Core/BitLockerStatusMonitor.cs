using System.Management;

namespace Imaging.Core;

/// <summary>
/// Lightweight read-only BitLocker change watcher used by Windows Imaging Utility.
/// It replaces the broader shell drive-state infrastructure with only the one
/// event stream Windows Imaging Utility actually needs.
/// </summary>
public sealed class BitLockerStatusMonitor : IDisposable
{
    private const int DebounceMilliseconds = 250;
    private const int StartRetryMilliseconds = 1000;
    private const int MaxStartAttempts = 3;
    private const string NamespacePath = @"\\.\Root\CIMV2\Security\MicrosoftVolumeEncryption";
    private const string ChangeQuery =
        "SELECT * FROM __InstanceModificationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_EncryptableVolume'";

    private readonly SynchronizationContext _uiContext;
    private readonly object _sync = new();
    private ManagementEventWatcher? _watcher;
    private System.Threading.Timer? _debounceTimer;
    private bool _startQueued;
    private int _startAttemptCount;
    private bool _disposed;

    public BitLockerStatusMonitor(SynchronizationContext uiContext)
    {
        _uiContext = uiContext ?? throw new ArgumentNullException(nameof(uiContext));
    }

    public event EventHandler? StatusChanged;

    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || _startQueued || _watcher is not null)
                return;

            _startQueued = true;
            _startAttemptCount++;
        }

        _ = Task.Run(StartCore);
    }

    private void StartCore()
    {
        ManagementEventWatcher? watcher = null;

        try
        {
            ConnectionOptions options = new()
            {
                EnablePrivileges = true,
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy
            };

            ManagementScope scope = new(NamespacePath, options);
            watcher = new ManagementEventWatcher(scope, new WqlEventQuery(ChangeQuery));
            watcher.EventArrived += Watcher_EventArrived;
            watcher.Start();

            lock (_sync)
            {
                if (_disposed || _watcher is not null)
                {
                    _startQueued = false;
                    StopAndDisposeWatcher(watcher);
                    return;
                }

                _watcher = watcher;
                _startQueued = false;
                _startAttemptCount = 0;
                watcher = null;
            }
        }
        catch
        {
            if (watcher is not null)
                StopAndDisposeWatcher(watcher);

            bool retry;
            lock (_sync)
            {
                _startQueued = false;
                retry = !_disposed && _watcher is null && _startAttemptCount < MaxStartAttempts;
            }

            if (retry)
                _ = RetryStartAsync();
        }
    }

    private async Task RetryStartAsync()
    {
        try
        {
            await Task.Delay(StartRetryMilliseconds).ConfigureAwait(false);
            Start();
        }
        catch
        {
            // Best effort only. Manual refresh remains available.
        }
    }

    private void Watcher_EventArrived(object sender, EventArrivedEventArgs e)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            if (_debounceTimer is null)
            {
                _debounceTimer = new System.Threading.Timer(
                    _ => FlushStatusChanged(),
                    null,
                    DebounceMilliseconds,
                    Timeout.Infinite);
            }
            else
            {
                _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    private void FlushStatusChanged()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
        }

        _uiContext.Post(_ =>
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
            }

            StatusChanged?.Invoke(this, EventArgs.Empty);
        }, null);
    }

    public void Dispose()
    {
        ManagementEventWatcher? watcher;
        System.Threading.Timer? debounceTimer;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            watcher = _watcher;
            debounceTimer = _debounceTimer;
            _watcher = null;
            _debounceTimer = null;
        }

        debounceTimer?.Dispose();
        if (watcher is not null)
            StopAndDisposeWatcher(watcher);
    }

    private static void StopAndDisposeWatcher(ManagementEventWatcher watcher)
    {
        try { watcher.Stop(); }
        catch { }

        try { watcher.Dispose(); }
        catch { }
    }
}
