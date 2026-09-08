using System.Management;

namespace Imaging.Core;

public enum BitLockerVisualState
{
    Unknown,
    None,
    Locked,
    Unlocked,
    ProtectionOff
}

internal readonly record struct BitLockerStatusSnapshot(
    IReadOnlyList<ImagingBitLockerVolumeInfo> Volumes,
    bool Available,
    string Error);

/// <summary>
/// Read-only BitLocker inventory for Windows Imaging Utility. Full Windows already owns
/// interactive BitLocker operations, so Imaging.Core only needs enough structured
/// status to classify volumes and display capture warnings/details.
/// </summary>
internal static class BitLockerStatusReader
{
    private const string NamespacePath = @"root\CIMV2\Security\MicrosoftVolumeEncryption";
    private const string QueryText = "SELECT * FROM Win32_EncryptableVolume";

    public static BitLockerStatusSnapshot GetSnapshot()
    {
        try
        {
            ManagementScope scope = new(NamespacePath);
            scope.Connect();

            using ManagementObjectSearcher searcher = new(scope, new ObjectQuery(QueryText));
            using ManagementObjectCollection results = searcher.Get();
            List<ImagingBitLockerVolumeInfo> volumes = new();

            foreach (ManagementObject volume in results.Cast<ManagementObject>())
            {
                using (volume)
                {
                    ImagingBitLockerVolumeInfo? info = ReadVolume(volume);
                    if (info != null)
                        volumes.Add(info);
                }
            }

            return new BitLockerStatusSnapshot(
                volumes.OrderBy(static v => v.MountPoint, StringComparer.OrdinalIgnoreCase).ToArray(),
                Available: true,
                Error: string.Empty);
        }
        catch (Exception ex)
        {
            return new BitLockerStatusSnapshot(
                Array.Empty<ImagingBitLockerVolumeInfo>(),
                Available: false,
                Error: ex.Message);
        }
    }

    private static ImagingBitLockerVolumeInfo? ReadVolume(ManagementObject volume)
    {
        string? mountPoint = NormalizeMountPoint(Convert.ToString(GetPropertyValueSafe(volume, "DriveLetter")));
        if (string.IsNullOrWhiteSpace(mountPoint))
            return null;

        uint? volumeType = ReadUInt32Property(volume, "VolumeType");
        uint? lockStatus = TryInvokeSingleUInt32(volume, "GetLockStatus", "LockStatus")
            ?? ReadUInt32Property(volume, "LockStatus");
        uint? protectionStatus = TryInvokeSingleUInt32(volume, "GetProtectionStatus", "ProtectionStatus")
            ?? ReadUInt32Property(volume, "ProtectionStatus");
        uint? encryptionMethod = TryInvokeSingleUInt32(volume, "GetEncryptionMethod", "EncryptionMethod")
            ?? ReadUInt32Property(volume, "EncryptionMethod");
        ConversionSnapshot conversion = ReadConversionStatus(volume);

        bool? isLocked = lockStatus switch
        {
            1 => true,
            0 => false,
            _ => null
        };

        bool encryptionMethodKnown = encryptionMethod.HasValue && encryptionMethod.Value != uint.MaxValue;
        bool encryptedByMethod = encryptionMethodKnown && encryptionMethod!.Value != 0;
        bool encryptedByConversion = conversion.Status is >= 1 and <= 5 || conversion.EncryptionPercentage.GetValueOrDefault() > 0;
        bool isEncrypted = isLocked == true || encryptedByMethod || encryptedByConversion;
        bool encryptionStateKnown = encryptionMethodKnown || conversion.Status.HasValue || isLocked == true;
        bool isBitLockerCapable = isEncrypted || protectionStatus == 1 || isLocked == true;

        BitLockerVisualState visualState;
        if (isLocked == true)
        {
            visualState = BitLockerVisualState.Locked;
        }
        else if (isLocked is null)
        {
            visualState = BitLockerVisualState.Unknown;
        }
        else if (!encryptionStateKnown)
        {
            visualState = BitLockerVisualState.Unknown;
        }
        else if (!isEncrypted)
        {
            visualState = BitLockerVisualState.None;
        }
        else
        {
            visualState = protectionStatus == 1
                ? BitLockerVisualState.Unlocked
                : BitLockerVisualState.ProtectionOff;
        }

        bool isSystemVolume = volumeType == 0;

        return new ImagingBitLockerVolumeInfo
        {
            MountPoint = mountPoint,
            VolumeLabel = GetVolumeLabelSafe(mountPoint),
            IsLocked = isLocked,
            IsEncrypted = isEncrypted,
            IsBitLockerCapable = isBitLockerCapable,
            IsSystemVolume = isSystemVolume,
            VolumeTypeText = MapVolumeType(volumeType),
            VisualState = visualState,
            EncryptionPercentage = NormalizePercentage(conversion.EncryptionPercentage, conversion.Status),
            ConversionStatus = MapConversionStatus(conversion.Status),
            EncryptionType = MapEncryptionType(isEncrypted, conversion.EncryptionFlags),
            ProtectionStatus = MapProtectionStatus(protectionStatus)
        };
    }

    private static ConversionSnapshot ReadConversionStatus(ManagementObject volume)
    {
        uint? status = ReadUInt32Property(volume, "ConversionStatus");
        uint? percentage = status switch
        {
            0 => 0,
            1 => 100,
            _ => null
        };
        uint? flags = null;

        try
        {
            using ManagementBaseObject inParams = volume.GetMethodParameters("GetConversionStatus");
            inParams["PrecisionFactor"] = 0u;
            using ManagementBaseObject? outParams = volume.InvokeMethod("GetConversionStatus", inParams, null);

            if (outParams != null &&
                TryReadUInt32(outParams, "ReturnValue", out uint returnValue) &&
                returnValue == 0)
            {
                if (TryReadUInt32(outParams, "ConversionStatus", out uint currentStatus))
                    status = currentStatus;
                if (TryReadUInt32(outParams, "EncryptionPercentage", out uint currentPercentage))
                    percentage = currentPercentage;
                if (TryReadUInt32(outParams, "EncryptionFlags", out uint currentFlags))
                    flags = currentFlags;
            }
        }
        catch
        {
            // Locked volumes can reject GetConversionStatus. The class-level
            // ConversionStatus property remains a useful best-effort fallback.
        }

        return new ConversionSnapshot(status, percentage, flags);
    }

    private static uint? TryInvokeSingleUInt32(ManagementObject volume, string methodName, string outParamName)
    {
        try
        {
            using ManagementBaseObject? outParams = volume.InvokeMethod(methodName, null, null);
            if (outParams == null ||
                !TryReadUInt32(outParams, "ReturnValue", out uint returnValue) ||
                returnValue != 0)
            {
                return null;
            }

            return TryReadUInt32(outParams, outParamName, out uint value) ? value : null;
        }
        catch
        {
            return null;
        }
    }

    private static object? GetPropertyValueSafe(ManagementObject obj, string propertyName)
    {
        try { return obj.Properties[propertyName]?.Value; }
        catch { return null; }
    }

    private static uint? ReadUInt32Property(ManagementObject obj, string propertyName)
    {
        object? value = GetPropertyValueSafe(obj, propertyName);
        if (value == null)
            return null;

        try { return Convert.ToUInt32(value); }
        catch { return null; }
    }

    private static bool TryReadUInt32(ManagementBaseObject obj, string propertyName, out uint value)
    {
        value = 0;
        try
        {
            object? raw = obj[propertyName];
            if (raw == null)
                return false;

            value = Convert.ToUInt32(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int? NormalizePercentage(uint? percentage, uint? conversionStatus)
    {
        if (percentage.HasValue)
            return Math.Clamp((int)percentage.Value, 0, 100);

        return conversionStatus switch
        {
            0 => 0,
            1 => 100,
            _ => null
        };
    }

    private static string MapConversionStatus(uint? value)
    {
        return value switch
        {
            0 => "Fully Decrypted",
            1 => "Fully Encrypted",
            2 => "Encryption In Progress",
            3 => "Decryption In Progress",
            4 => "Encryption Paused",
            5 => "Decryption Paused",
            _ => "Unknown"
        };
    }

    private static string MapEncryptionType(bool isEncrypted, uint? encryptionFlags)
    {
        if (!isEncrypted)
            return "None";

        if (!encryptionFlags.HasValue)
            return string.Empty;

        return (encryptionFlags.Value & 0x00000001) != 0
            ? "Used Space Only"
            : "Full Volume";
    }

    private static string MapProtectionStatus(uint? value)
    {
        return value switch
        {
            0 => "Protection Off",
            1 => "Protection On",
            _ => "Unknown"
        };
    }

    private static string MapVolumeType(uint? value)
    {
        return value switch
        {
            0 => "System",
            1 => "Fixed",
            2 => "Removable",
            _ => "Unknown"
        };
    }

    private static string GetVolumeLabelSafe(string mountPoint)
    {
        try
        {
            DriveInfo drive = new(mountPoint);
            return drive.IsReady ? drive.VolumeLabel ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? NormalizeMountPoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string trimmed = value.Trim();
        return trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':'
            ? trimmed + @"\"
            : trimmed;
    }

    private readonly record struct ConversionSnapshot(
        uint? Status,
        uint? EncryptionPercentage,
        uint? EncryptionFlags);
}
