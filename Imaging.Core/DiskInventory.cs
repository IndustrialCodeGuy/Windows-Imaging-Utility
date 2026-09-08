using System.Globalization;
using System.Management;

namespace Imaging.Core;

public sealed class DiskInventory
{
    private const string StorageNamespace = @"root\Microsoft\Windows\Storage";

    public ImagingInventorySnapshot GetInventory()
    {
        BitLockerStatusSnapshot bitLockerStatus = BitLockerStatusReader.GetSnapshot();
        IReadOnlyList<ImagingBitLockerVolumeInfo> bitLockerVolumes = bitLockerStatus.Volumes;
        Dictionary<string, ImagingVolumeInfo> volumesByRoot = GetDriveVolumesBestEffort();

        IReadOnlyList<ImagingDiskStorageInfo> storageDisks = GetStorageDisks();
        Dictionary<int, List<ImagingPartitionInfo>> partitionsByDisk = GetPartitions(volumesByRoot);
        List<ImagingDiskInfo> disks = new(storageDisks.Count);

        foreach (ImagingDiskStorageInfo storage in storageDisks)
        {
            int diskNumber = storage.Number;
            partitionsByDisk.TryGetValue(diskNumber, out List<ImagingPartitionInfo>? partitions);
            partitions ??= new List<ImagingPartitionInfo>();

            // Empty card-reader slots and similar placeholder devices can appear as
            // zero-byte MSFT_Disk instances. They are not actionable imaging targets.
            if (storage.SizeBytes == 0 && partitions.Count == 0 && storage.NumberOfPartitions == 0)
                continue;

            string[] driveLetters = partitions
                .SelectMany(static partition => partition.DriveLetters)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            ImagingBitLockerVolumeInfo[] diskBitLockerVolumes = bitLockerVolumes
                .Where(volume => driveLetters.Any(drive =>
                    string.Equals(
                        ImagingPath.NormalizeDriveRoot(volume.MountPoint),
                        ImagingPath.NormalizeDriveRoot(drive),
                        StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static volume => volume.MountPoint, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            disks.Add(new ImagingDiskInfo
            {
                DiskNumber = diskNumber,
                DevicePath = $@"\\.\PhysicalDrive{diskNumber}",
                Model = FirstNonEmpty(storage.Model, storage.FriendlyName),
                SerialNumber = storage.SerialNumber,
                SizeBytes = storage.SizeBytes,
                IsOffline = storage.IsOffline,
                StorageInfo = storage,
                Partitions = partitions.OrderBy(static partition => partition.PartitionNumber).ToArray(),
                BitLockerVolumes = diskBitLockerVolumes,
                BitLockerStatusAvailable = bitLockerStatus.Available,
                BitLockerStatusError = bitLockerStatus.Error
            });
        }

        ImagingVolumeInfo[] opticalVolumes = volumesByRoot.Values
            .Where(static volume => volume.DriveType == DriveType.CDRom && volume.IsReady)
            .OrderBy(static volume => volume.MountPoint, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ImagingInventorySnapshot
        {
            Disks = disks.OrderBy(static disk => disk.DiskNumber).ToArray(),
            OpticalVolumes = opticalVolumes
        };
    }

    public IReadOnlyList<ImagingDiskInfo> GetDisks() => GetInventory().Disks;

    public static ImagingDiskInfo? FindDiskForPath(IEnumerable<ImagingDiskInfo> disks, string? path)
    {
        string? root = ImagingPath.TryGetDriveRootForPath(path);
        if (root == null)
            return null;

        return disks.FirstOrDefault(disk => disk.ContainsDrive(root));
    }

    private static IReadOnlyList<ImagingDiskStorageInfo> GetStorageDisks()
    {
        try
        {
            List<ImagingDiskStorageInfo> result = new();
            using ManagementObjectSearcher searcher = new(StorageNamespace, "SELECT * FROM MSFT_Disk");
            using ManagementObjectCollection disks = searcher.Get();

            foreach (ManagementObject disk in disks.Cast<ManagementObject>())
            {
                using (disk)
                {
                    result.Add(new ImagingDiskStorageInfo
                    {
                        Number = ReadInt32(GetPropertyValueSafe(disk, "Number")),
                        Path = ReadString(GetPropertyValueSafe(disk, "Path")),
                        Location = ReadString(GetPropertyValueSafe(disk, "Location")),
                        FriendlyName = ReadString(GetPropertyValueSafe(disk, "FriendlyName")),
                        UniqueId = ReadString(GetPropertyValueSafe(disk, "UniqueId")),
                        UniqueIdFormat = FormatUniqueIdFormat(ReadNullableUInt16(GetPropertyValueSafe(disk, "UniqueIdFormat"))),
                        SerialNumber = ReadString(GetPropertyValueSafe(disk, "SerialNumber")),
                        FirmwareVersion = ReadString(GetPropertyValueSafe(disk, "FirmwareVersion")),
                        Manufacturer = ReadString(GetPropertyValueSafe(disk, "Manufacturer")),
                        Model = ReadString(GetPropertyValueSafe(disk, "Model")),
                        SizeBytes = ReadUInt64(GetPropertyValueSafe(disk, "Size")),
                        AllocatedSizeBytes = ReadUInt64(GetPropertyValueSafe(disk, "AllocatedSize")),
                        LogicalSectorSize = ReadUInt32(GetPropertyValueSafe(disk, "LogicalSectorSize")),
                        PhysicalSectorSize = ReadUInt32(GetPropertyValueSafe(disk, "PhysicalSectorSize")),
                        LargestFreeExtentBytes = ReadUInt64(GetPropertyValueSafe(disk, "LargestFreeExtent")),
                        NumberOfPartitions = ReadUInt32(GetPropertyValueSafe(disk, "NumberOfPartitions")),
                        ProvisioningType = FormatProvisioningType(ReadNullableUInt16(GetPropertyValueSafe(disk, "ProvisioningType"))),
                        OperationalStatus = FormatDiskOperationalStatus(GetPropertyValueSafe(disk, "OperationalStatus")),
                        HealthStatus = FormatHealthStatus(ReadNullableUInt16(GetPropertyValueSafe(disk, "HealthStatus"))),
                        BusType = FormatBusType(ReadNullableUInt16(GetPropertyValueSafe(disk, "BusType"))),
                        PartitionStyle = FormatPartitionStyle(ReadNullableUInt16(GetPropertyValueSafe(disk, "PartitionStyle"))),
                        Signature = ReadNullableUInt32(GetPropertyValueSafe(disk, "Signature")),
                        Guid = ReadString(GetPropertyValueSafe(disk, "Guid")),
                        IsOffline = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsOffline")),
                        OfflineReason = FormatOfflineReason(ReadNullableUInt16(GetPropertyValueSafe(disk, "OfflineReason"))),
                        IsReadOnly = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsReadOnly")),
                        IsSystem = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsSystem")),
                        IsClustered = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsClustered")),
                        IsBoot = ReadNullableBoolean(GetPropertyValueSafe(disk, "IsBoot")),
                        BootFromDisk = ReadNullableBoolean(GetPropertyValueSafe(disk, "BootFromDisk"))
                    });
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The Windows Storage Management Provider (MSFT_Disk) could not be queried.", ex);
        }
    }

    private static Dictionary<int, List<ImagingPartitionInfo>> GetPartitions(
        IReadOnlyDictionary<string, ImagingVolumeInfo> volumesByRoot)
    {
        try
        {
            Dictionary<int, List<ImagingPartitionInfo>> result = new();
            using ManagementObjectSearcher searcher = new(StorageNamespace, "SELECT * FROM MSFT_Partition");
            using ManagementObjectCollection partitions = searcher.Get();

            foreach (ManagementObject partition in partitions.Cast<ManagementObject>())
            {
                using (partition)
                {
                    string rawGptType = ReadString(GetPropertyValueSafe(partition, "GptType"));

                    ImagingPartitionStorageInfo storage = new()
                    {
                        DiskNumber = ReadInt32(GetPropertyValueSafe(partition, "DiskNumber")),
                        PartitionNumber = ReadInt32(GetPropertyValueSafe(partition, "PartitionNumber")),
                        DriveLetter = ReadDriveLetter(GetPropertyValueSafe(partition, "DriveLetter")),
                        AccessPaths = ReadStringArray(GetPropertyValueSafe(partition, "AccessPaths")),
                        OperationalStatus = FormatPartitionOperationalStatus(GetPropertyValueSafe(partition, "OperationalStatus")),
                        TransitionState = FormatPartitionTransitionState(ReadNullableUInt16(GetPropertyValueSafe(partition, "TransitionState"))),
                        SizeBytes = ReadUInt64(GetPropertyValueSafe(partition, "Size")),
                        OffsetBytes = ReadUInt64(GetPropertyValueSafe(partition, "Offset")),
                        MbrType = FormatMbrType(ReadNullableUInt16(GetPropertyValueSafe(partition, "MbrType"))),
                        GptTypeGuid = NormalizeGptTypeGuid(rawGptType),
                        GptType = FormatGptType(rawGptType),
                        Guid = ReadString(GetPropertyValueSafe(partition, "Guid")),
                        IsReadOnly = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsReadOnly")),
                        IsOffline = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsOffline")),
                        IsSystem = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsSystem")),
                        IsBoot = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsBoot")),
                        IsActive = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsActive")),
                        IsHidden = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsHidden")),
                        IsShadowCopy = ReadNullableBoolean(GetPropertyValueSafe(partition, "IsShadowCopy")),
                        NoDefaultDriveLetter = ReadNullableBoolean(GetPropertyValueSafe(partition, "NoDefaultDriveLetter"))
                    };

                    IReadOnlyList<string> driveLetters = GetPartitionDriveLetters(storage);
                    ImagingVolumeInfo[] partitionVolumes = driveLetters
                        .Select(root =>
                        {
                            string normalized = ImagingPath.NormalizeDriveRoot(root);
                            if (normalized.Length > 0 && volumesByRoot.TryGetValue(normalized, out ImagingVolumeInfo? volume))
                                return volume;

                            return ProbeVolumeBestEffort(normalized);
                        })
                        .OfType<ImagingVolumeInfo>()
                        .ToArray();

                    ImagingPartitionInfo info = new()
                    {
                        PartitionNumber = storage.PartitionNumber,
                        SizeBytes = storage.SizeBytes,
                        DriveLetters = driveLetters,
                        Volumes = partitionVolumes,
                        StorageInfo = storage
                    };

                    if (!result.TryGetValue(storage.DiskNumber, out List<ImagingPartitionInfo>? list))
                    {
                        list = new List<ImagingPartitionInfo>();
                        result.Add(storage.DiskNumber, list);
                    }

                    list.Add(info);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "The Windows Storage Management Provider (MSFT_Partition) could not be queried.", ex);
        }
    }

    private static IReadOnlyList<string> GetPartitionDriveLetters(ImagingPartitionStorageInfo storage)
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        AddDriveRoot(roots, storage.DriveLetter);

        foreach (string accessPath in storage.AccessPaths)
            AddDriveRoot(roots, accessPath);

        return roots.OrderBy(static root => root, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddDriveRoot(ISet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string normalized = ImagingPath.NormalizeDriveRoot(path);
        if (normalized.Length == 3 && normalized[1] == ':')
            roots.Add(normalized);
    }

    private static Dictionary<string, ImagingVolumeInfo> GetDriveVolumesBestEffort()
    {
        Dictionary<string, ImagingVolumeInfo> result = new(StringComparer.OrdinalIgnoreCase);

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            return result;
        }

        foreach (DriveInfo drive in drives)
        {
            ImagingVolumeInfo? volume = ProbeVolumeBestEffort(drive);
            if (volume == null || string.IsNullOrWhiteSpace(volume.MountPoint))
                continue;

            result[volume.MountPoint] = volume;
        }

        return result;
    }

    private static ImagingVolumeInfo? ProbeVolumeBestEffort(string? driveRoot)
    {
        string normalized = ImagingPath.NormalizeDriveRoot(driveRoot);
        if (normalized.Length == 0)
            return null;

        try
        {
            return ProbeVolumeBestEffort(new DriveInfo(normalized));
        }
        catch
        {
            return new ImagingVolumeInfo { MountPoint = normalized };
        }
    }

    private static ImagingVolumeInfo? ProbeVolumeBestEffort(DriveInfo drive)
    {
        string root;
        try
        {
            root = ImagingPath.NormalizeDriveRoot(drive.Name);
        }
        catch
        {
            return null;
        }

        if (root.Length == 0)
            return null;

        DriveType driveType = DriveType.Unknown;
        bool isReady = false;
        string volumeLabel = string.Empty;
        string driveFormat = string.Empty;
        ulong total = 0;
        ulong totalFree = 0;
        ulong availableFree = 0;

        try { driveType = drive.DriveType; } catch { }
        try { isReady = drive.IsReady; } catch { }

        if (isReady)
        {
            try { volumeLabel = drive.VolumeLabel ?? string.Empty; } catch { }
            try { driveFormat = drive.DriveFormat ?? string.Empty; } catch { }
            try { total = (ulong)Math.Max(0L, drive.TotalSize); } catch { }
            try { totalFree = (ulong)Math.Max(0L, drive.TotalFreeSpace); } catch { }
            try { availableFree = (ulong)Math.Max(0L, drive.AvailableFreeSpace); } catch { }
        }

        bool isRunningSystemDrive = IsRunningSystemDrive(root);
        bool canContainWindowsInstall = driveType is DriveType.Fixed or DriveType.Removable;
        bool containsOfflineWindows = isReady &&
                                      canContainWindowsInstall &&
                                      !isRunningSystemDrive &&
                                      ContainsOfflineWindowsInstall(root);

        return new ImagingVolumeInfo
        {
            MountPoint = root,
            DriveType = driveType,
            IsReady = isReady,
            VolumeLabel = volumeLabel,
            DriveFormat = driveFormat,
            TotalSizeBytes = total,
            TotalFreeSpaceBytes = totalFree,
            AvailableFreeSpaceBytes = availableFree,
            ContainsOfflineWindowsInstall = containsOfflineWindows,
            IsRunningSystemDrive = isRunningSystemDrive
        };
    }

    private static bool IsRunningSystemDrive(string driveRoot)
    {
        try
        {
            string systemRoot = ImagingPath.NormalizeDriveRoot(
                Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty);
            return string.Equals(driveRoot, systemRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsOfflineWindowsInstall(string driveRoot)
    {
        try
        {
            return File.Exists(Path.Combine(driveRoot, "Windows", "System32", "Config", "SYSTEM"));
        }
        catch
        {
            return false;
        }
    }

    private static object? GetPropertyValueSafe(ManagementObject obj, string propertyName)
    {
        try { return obj.Properties[propertyName]?.Value; }
        catch { return null; }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string ReadString(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

    private static IReadOnlyList<string> ReadStringArray(object? value)
    {
        if (value is string[] strings)
            return strings.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).ToArray();
        if (value is IEnumerable<string> sequence)
            return sequence.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).ToArray();
        return Array.Empty<string>();
    }

    private static string ReadDriveLetter(object? value)
    {
        if (value is char character && character != '\0')
            return char.ToUpperInvariant(character) + ":";

        try
        {
            if (value is ushort or short or uint or int)
            {
                char numericCharacter = Convert.ToChar(value, CultureInfo.InvariantCulture);
                if (numericCharacter != '\0' && char.IsLetter(numericCharacter))
                    return char.ToUpperInvariant(numericCharacter) + ":";
            }
        }
        catch
        {
        }

        string text = ReadString(value).TrimEnd(':');
        return text.Length == 1 && char.IsLetter(text[0]) ? char.ToUpperInvariant(text[0]) + ":" : string.Empty;
    }

    private static int ReadInt32(object? value)
    {
        try { return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    private static uint ReadUInt32(object? value)
    {
        try { return value == null ? 0U : Convert.ToUInt32(value, CultureInfo.InvariantCulture); }
        catch { return 0U; }
    }

    private static uint? ReadNullableUInt32(object? value)
    {
        try { return value == null ? null : Convert.ToUInt32(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static ushort? ReadNullableUInt16(object? value)
    {
        try { return value == null ? null : Convert.ToUInt16(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static IReadOnlyList<ushort> ReadUInt16Values(object? value)
    {
        if (value == null)
            return Array.Empty<ushort>();

        if (value is ushort[] values)
            return values;

        if (value is Array array)
        {
            List<ushort> result = new(array.Length);
            foreach (object? item in array)
            {
                try
                {
                    if (item != null)
                        result.Add(Convert.ToUInt16(item, CultureInfo.InvariantCulture));
                }
                catch
                {
                }
            }
            return result;
        }

        ushort? single = ReadNullableUInt16(value);
        return single.HasValue ? new[] { single.Value } : Array.Empty<ushort>();
    }

    private static ulong ReadUInt64(object? value)
    {
        try { return value == null ? 0UL : Convert.ToUInt64(value, CultureInfo.InvariantCulture); }
        catch { return 0UL; }
    }

    private static bool? ReadNullableBoolean(object? value)
    {
        try { return value == null ? null : Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static string FormatStatusValues(object? value, Func<ushort, string> formatter)
    {
        return string.Join(", ", ReadUInt16Values(value)
            .Select(formatter)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string FormatUniqueIdFormat(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Vendor specific",
        1 => "Vendor ID",
        2 => "EUI-64",
        3 => "FCPH name",
        8 => "SCSI name string",
        _ => $"Unknown ({value})"
    };

    private static string FormatProvisioningType(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Unknown",
        1 => "Thin",
        2 => "Fixed",
        _ => $"Unknown ({value})"
    };

    private static string FormatHealthStatus(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Healthy",
        1 => "Warning",
        2 => "Unhealthy",
        5 => "Unknown",
        _ => $"Unknown ({value})"
    };

    private static string FormatDiskOperationalStatus(object? value) =>
        FormatStatusValues(value, FormatDiskOperationalStatusValue);

    private static string FormatDiskOperationalStatusValue(ushort value) => value switch
    {
        0 => "Unknown",
        1 => "Other",
        2 => "OK",
        3 => "Degraded",
        4 => "Stressed",
        5 => "Predictive failure",
        6 => "Error",
        7 => "Non-recoverable error",
        8 => "Starting",
        9 => "Stopping",
        10 => "Stopped",
        11 => "In service",
        12 => "No contact",
        13 => "Lost communication",
        14 => "Aborted",
        15 => "Dormant",
        16 => "Supporting entity in error",
        17 => "Completed",
        18 => "Power mode",
        0xD010 => "Online",
        0xD011 => "Not ready",
        0xD012 => "No media",
        0xD013 => "Offline",
        0xD014 => "Failed",
        _ => $"Unknown (0x{value:X4})"
    };

    private static string FormatBusType(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Unknown",
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "IEEE 1394",
        5 => "SSA",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "File-backed virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => $"Unknown ({value})"
    };

    private static string FormatPartitionStyle(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "RAW",
        1 => "MBR",
        2 => "GPT",
        _ => $"Unknown ({value})"
    };

    private static string FormatOfflineReason(ushort? value) => value switch
    {
        null => string.Empty,
        0 => string.Empty,
        1 => "Policy",
        2 => "Redundant path",
        3 => "Snapshot",
        4 => "Signature/identifier collision",
        5 => "Resource exhaustion",
        6 => "Critical write failures",
        7 => "Data integrity scan required",
        _ => $"Unknown ({value})"
    };

    private static string FormatPartitionOperationalStatus(object? value) =>
        FormatStatusValues(value, FormatPartitionOperationalStatusValue);

    private static string FormatPartitionOperationalStatusValue(ushort value) => value switch
    {
        0 => "Unknown",
        1 => "Online",
        3 => "No media",
        4 => "Offline",
        5 => "Failed",
        _ => $"Unknown ({value})"
    };

    private static string FormatPartitionTransitionState(ushort? value) => value switch
    {
        null => string.Empty,
        0 => "Unknown / reserved",
        1 => "Stable",
        2 => "Extending",
        3 => "Shrinking",
        4 => "Reconfiguring",
        8 => "Restriping",
        _ => $"Unknown ({value})"
    };

    private static string FormatMbrType(ushort? value) => value switch
    {
        null => string.Empty,
        0 => string.Empty,
        1 => "FAT12 (0x01)",
        4 => "FAT16 (0x04)",
        5 => "Extended (0x05)",
        6 => "Huge (0x06)",
        7 => "IFS / NTFS / exFAT (0x07)",
        12 => "FAT32 (0x0C)",
        _ => $"0x{value:X2}"
    };

    private static string NormalizeGptTypeGuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().Trim('{', '}').ToLowerInvariant();
    }

    private static string FormatGptType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = NormalizeGptTypeGuid(value);
        string name = normalized switch
        {
            "c12a7328-f81f-11d2-ba4b-00a0c93ec93b" => "EFI System",
            "e3c9e316-0b5c-4db8-817d-f92df00215ae" => "Microsoft Reserved",
            "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7" => "Basic data",
            "5808c8aa-7e8f-42e0-85d2-e1e90434cfb3" => "LDM metadata",
            "af9b60a0-1431-4f62-bc68-3311714a69ad" => "LDM data",
            "de94bba4-06d1-4d40-a16a-bfd50179d6ac" => "Microsoft Recovery",
            _ => string.Empty
        };

        return string.IsNullOrEmpty(name) ? value.Trim() : $"{name} ({value.Trim()})";
    }
}
