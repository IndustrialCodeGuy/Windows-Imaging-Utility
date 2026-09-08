using System.Runtime.InteropServices;

namespace Windows.Imaging.Utility;

// Windows Imaging Utility only needs a handful of fixed Windows resource icons.
// Keep that narrow surface instead of the former generic shell icon/association
// library and system image-list COM interop.
internal static class WindowsIconProvider
{
    private const int ApplicationIconIndex = 30;
    private static readonly int[] StandardResourceSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    public static Icon? CreateApplicationIcon(int size = 32) => ExtractIcon(ApplicationIconIndex, size);
    public static Image? CreateDiskImage(int size) => ExtractImage(30, size);
    public static Image? CreatePartitionImage(DriveVisualKind kind, int size) => ExtractImage(GetImageresIconIndex(kind), size);

    private static int GetImageresIconIndex(DriveVisualKind kind) => kind switch
    {
        DriveVisualKind.SystemBitLockerProtectionOff => 214,
        DriveVisualKind.SystemBitLockerUnlocked => 213,
        DriveVisualKind.System => 31,
        DriveVisualKind.BitLockerLocked => 211,
        DriveVisualKind.BitLockerStatusUnknown => 70,
        DriveVisualKind.BitLockerUnlocked => 210,
        DriveVisualKind.BitLockerProtectionOff => 212,
        DriveVisualKind.Network => 28,
        DriveVisualKind.Removable => 30,
        DriveVisualKind.Optical => 25,
        _ => 30
    };

    private static Icon? ExtractIcon(int index, int size)
    {
        IntPtr hIcon = ExtractHandle(index, size);
        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            using Icon borrowed = Icon.FromHandle(hIcon);
            return (Icon)borrowed.Clone();
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Image? ExtractImage(int index, int size)
    {
        int targetSize = Math.Max(1, size);
        IntPtr hIcon = ExtractHandle(index, targetSize);
        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            using Icon borrowed = Icon.FromHandle(hIcon);
            using Bitmap source = borrowed.ToBitmap();
            Bitmap result = new(targetSize, targetSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            result.SetResolution(96f, 96f);

            using Graphics graphics = Graphics.FromImage(result);
            graphics.Clear(Color.Transparent);
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, new Rectangle(0, 0, targetSize, targetSize));
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static IntPtr ExtractHandle(int index, int targetSize)
    {
        string path = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        int extractSize = SelectResourceSize(targetSize);
        IntPtr[] icons = new IntPtr[1];
        uint[] ids = new uint[1];

        try
        {
            uint extracted = PrivateExtractIcons(path, index, extractSize, extractSize, icons, ids, 1, 0);
            if (extracted == 0 || icons[0] == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr result = icons[0];
            icons[0] = IntPtr.Zero;
            return result;
        }
        catch
        {
            return IntPtr.Zero;
        }
        finally
        {
            if (icons[0] != IntPtr.Zero)
                DestroyIcon(icons[0]);
        }
    }

    private static int SelectResourceSize(int targetSize)
    {
        targetSize = Math.Max(1, targetSize);
        foreach (int size in StandardResourceSizes)
        {
            if (targetSize <= size)
                return size;
        }

        return targetSize;
    }

    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string fileName, int iconIndex, int cxIcon, int cyIcon, IntPtr[] icons, uint[] iconIds, uint iconCount, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
