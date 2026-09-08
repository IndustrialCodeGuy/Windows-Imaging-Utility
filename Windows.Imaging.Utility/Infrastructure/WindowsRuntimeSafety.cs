using Imaging.Core;

namespace Windows.Imaging.Utility;

internal static class WindowsRuntimeSafety
{
    public static bool IsRunningWindowsDisk(ImagingDiskInfo? disk) =>
        disk != null && disk.Partitions.Any(IsRunningWindowsPartition);

    public static bool IsRunningWindowsPartition(ImagingPartitionInfo? partition) =>
        partition != null && partition.Volumes.Any(static volume => volume.IsRunningSystemDrive);

    public static string GetDiskProtectionMessage(ImagingDiskInfo disk) =>
        $"Disk {disk.DiskNumber} contains the Windows installation that is currently running. " +
        "Whole-disk capture, apply, and deployment operations are disabled for the live Windows disk. " +
        "Boot to WinPE to image or replace this disk offline.";

    public static string GetPartitionProtectionMessage(ImagingPartitionInfo partition) =>
        $"Partition {partition.PartitionNumber} contains the Windows installation that is currently running. " +
        "WIM capture and apply operations are disabled for the live Windows partition. " +
        "Boot to WinPE to image or replace this partition offline.";
}
