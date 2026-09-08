using Imaging.Core;

namespace Windows.Imaging.Utility;

public partial class MainForm
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

    private readonly System.Windows.Forms.Timer _topologyRefreshTimer = new() { Interval = 150 };
    private BitLockerStatusMonitor? _bitLockerStatusMonitor;

    private void InitializeStorageChangeMonitoring(SynchronizationContext uiContext)
    {
        _topologyRefreshTimer.Tick += TopologyRefreshTimer_Tick;

        _bitLockerStatusMonitor = new BitLockerStatusMonitor(uiContext);
        _bitLockerStatusMonitor.StatusChanged += BitLockerStatusMonitor_StatusChanged;
    }

    private void StartStorageChangeMonitoring()
    {
        _bitLockerStatusMonitor?.Start();
    }

    private void DisposeStorageChangeMonitoring()
    {
        _topologyRefreshTimer.Stop();
        _topologyRefreshTimer.Tick -= TopologyRefreshTimer_Tick;
        _topologyRefreshTimer.Dispose();

        if (_bitLockerStatusMonitor is not null)
        {
            _bitLockerStatusMonitor.StatusChanged -= BitLockerStatusMonitor_StatusChanged;
            _bitLockerStatusMonitor.Dispose();
            _bitLockerStatusMonitor = null;
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg != WM_DEVICECHANGE || _initialInventoryLoading || IsDisposed || Disposing)
            return;

        int code = m.WParam.ToInt32();
        if (code is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE or DBT_DEVNODES_CHANGED)
        {
            _topologyRefreshTimer.Stop();
            _topologyRefreshTimer.Start();
        }
    }

    private void TopologyRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _topologyRefreshTimer.Stop();
        _ = RequestDiskRefreshAsync();
    }

    private void BitLockerStatusMonitor_StatusChanged(object? sender, EventArgs e)
    {
        if (_initialInventoryLoading || IsDisposed || Disposing)
            return;

        _ = RequestDiskRefreshAsync();
    }
}
