using System.Security.Principal;

namespace Windows.Imaging.Utility;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ShellTheme.ConfigureFromArgs(args);

        if (!IsRunningAsAdministrator())
        {
            MessageBox.Show(
                "Windows Imaging Utility must be run as administrator.",
                "Windows Imaging Utility",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        using Mutex? instanceMutex = WindowsImagingUtilityActivation.TryAcquireUtilityMutex();
        if (instanceMutex == null)
        {
            WindowsImagingUtilityActivation.SignalExistingUtility();
            return 0;
        }

        using EventWaitHandle activateEvent = WindowsImagingUtilityActivation.CreateUtilityActivateEvent();
        using MainForm form = new();
        form.StartActivationListener(activateEvent);
        Application.Run(form);
        return 0;
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
