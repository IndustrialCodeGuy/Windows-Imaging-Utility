using Imaging.Core;
using System.Diagnostics;

namespace Windows.Imaging.Utility;

public partial class MainForm
{
    private ImagingBitLockerVolumeInfo? GetBitLockerVolumeForPartition(ImagingPartitionInfo partition)
    {
        ImagingDiskInfo? disk = _disks.FirstOrDefault(candidate => candidate.Partitions.Any(p => ReferenceEquals(p, partition)))
            ?? GetSelectedDisk();
        if (disk == null || partition.DriveLetters.Count == 0)
            return null;

        foreach (string drive in partition.DriveLetters)
        {
            string normalizedDrive = ImagingPath.NormalizeDriveRoot(drive);
            if (normalizedDrive.Length == 0)
                continue;

            ImagingBitLockerVolumeInfo? volume = disk.BitLockerVolumes.FirstOrDefault(v =>
                string.Equals(
                    ImagingPath.NormalizeDriveRoot(v.MountPoint),
                    normalizedDrive,
                    StringComparison.OrdinalIgnoreCase));

            if (volume != null)
                return volume;
        }

        return null;
    }

    private async Task UnlockSelectedPartitionAsync()
    {
        ImagingDiskInfo? disk = GetSelectedDisk();
        ImagingPartitionInfo? partition = GetSelectedPartition();
        ImagingBitLockerVolumeInfo? volume = partition == null ? null : GetBitLockerVolumeForPartition(partition);
        if (disk == null || partition == null || volume?.IsLocked != true || _operationActive)
            return;

        string mountPoint = ImagingPath.NormalizeDriveRoot(volume.MountPoint);
        if (mountPoint.Length == 0)
        {
            MessageBox.Show(
                this,
                "Windows requires the locked volume to have a drive letter before the Explorer BitLocker unlock interface can be opened.",
                "Unlock Drive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        int diskNumber = disk.DiskNumber;
        int partitionNumber = partition.PartitionNumber;
        if (!TryBeginOperation("Unlock", disk))
            return;

        UpdateSelectedDiskPanel();
        _lblStatus.Text = "Opening the Windows BitLocker unlock interface...";
        _lblStatus.Visible = true;
        LayoutDiskDetails(_rightPanel);

        try
        {
            // Invoke the actual BitLocker Unlock Drive shell verb. Launching the
            // drive root without a verb performs its default Open action, which
            // fails with "Location is not accessible" while the volume is locked.
            ProcessStartInfo startInfo = new()
            {
                FileName = mountPoint,
                Verb = "unlock-bde",
                UseShellExecute = true
            };

            Process.Start(startInfo)?.Dispose();

            // Give the shell a moment to create the unlock UI. Subsequent lock-state
            // changes are also picked up by the normal storage/BitLocker monitor.
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Windows could not open the BitLocker unlock interface for " +
                mountPoint.TrimEnd('\\') + ".\n\n" + ex.Message,
                "Unlock Drive",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            EndOperation();
            await RequestDiskRefreshAsync(diskNumber);
            SelectPartitionByNumber(partitionNumber);
        }
    }

}
