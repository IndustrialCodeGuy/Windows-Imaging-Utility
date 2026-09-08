namespace Windows.Imaging.Utility;

// Dialog measurements actually used by Windows Imaging Utility. The generic shell
// dialog ownership/activation helpers are intentionally not carried forward.
internal static class ShellDialogChrome
{
    public const int ButtonWidth = 80;
    public const int BodyLineHeight = 18;
    public const int HeaderLineHeight = 22;
    private const int ButtonHeight = 25;
    private const float HeaderFontSizeDelta = 1.5f;

    public static Font DialogFont => SystemFonts.MessageBoxFont ?? Control.DefaultFont;

    public static void ApplyFixedDialogDefaults(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.Font = DialogFont;
        form.StartPosition = FormStartPosition.Manual;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
    }

    public static void ApplyTextSafeButton(Button button, int minimumWidth = ButtonWidth)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Width = minimumWidth;
        button.Height = ButtonHeight;

        Size preferred = button.GetPreferredSize(Size.Empty);
        Size text = TextRenderer.MeasureText(button.Text ?? string.Empty, button.Font);
        button.Width = Math.Max(button.Width, Math.Max(preferred.Width, text.Width + 20));
        button.Height = Math.Max(button.Height, Math.Max(preferred.Height, text.Height + 8));
    }

    public static void ApplyHeaderFont(Control lifetimeOwner, params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(lifetimeOwner);
        if (controls == null || controls.Length == 0)
            return;

        Font baseFont = lifetimeOwner.Font ?? DialogFont;
        Font headerFont = new(baseFont.FontFamily, baseFont.Size + HeaderFontSizeDelta, FontStyle.Regular, baseFont.Unit);

        foreach (Control control in controls)
        {
            if (control != null)
                control.Font = headerFont;
        }

        lifetimeOwner.Disposed += (_, _) => headerFont.Dispose();
    }
}
