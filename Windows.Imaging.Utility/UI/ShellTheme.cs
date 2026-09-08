namespace Windows.Imaging.Utility;

// Small application-local palette. This replaces the former Shared.Shell
// theme dependency while preserving the Windows Imaging Utility appearance.
internal static class ShellTheme
{
    public static bool DarkMode { get; private set; }

    public static void ConfigureFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            if (arg.Equals("--dark", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--dark-mode", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--darkmode", StringComparison.OrdinalIgnoreCase))
            {
                DarkMode = true;
                continue;
            }

            if (arg.Equals("--light", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--light-mode", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--lightmode", StringComparison.OrdinalIgnoreCase))
            {
                DarkMode = false;
                continue;
            }

            if ((arg.Equals("--theme", StringComparison.OrdinalIgnoreCase) ||
                 arg.Equals("/theme", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                string value = args[++i]?.Trim() ?? string.Empty;
                if (value.Equals("dark", StringComparison.OrdinalIgnoreCase))
                    DarkMode = true;
                else if (value.Equals("light", StringComparison.OrdinalIgnoreCase))
                    DarkMode = false;
            }
        }
    }

    private static readonly Color ItemSelectedBackLight = Color.FromArgb(204, 228, 247);
    private static readonly Color ItemHoverBackLight = Color.FromArgb(224, 238, 249);
    private static readonly Color ItemSelectedBorderLight = Color.FromArgb(0, 114, 203);
    private static readonly Color ItemSelectedBackDark = Color.FromArgb(152, 198, 230);
    private static readonly Color ItemHoverBackDark = Color.FromArgb(148, 184, 208);
    private static readonly Color ItemSelectedBorderDark = Color.FromArgb(0, 114, 203);

    public static Color WindowBack => DarkMode ? SystemColors.ControlDarkDark : SystemColors.ControlLight;
    public static Color TextColor => SystemColors.WindowText;
    public static Color ContentBack => DarkMode ? SystemColors.ControlDark : SystemColors.ControlLightLight;
    public static Color ContentBorder => DarkMode ? SystemColors.ControlDarkDark : SystemColors.ControlDark;
    public static Color ItemSelectedBack => DarkMode ? ItemSelectedBackDark : ItemSelectedBackLight;
    public static Color ItemHoverBack => DarkMode ? ItemHoverBackDark : ItemHoverBackLight;
    public static Color ItemSelectedBorder => DarkMode ? ItemSelectedBorderDark : ItemSelectedBorderLight;
}
