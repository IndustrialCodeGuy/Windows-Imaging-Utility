using System.Runtime.InteropServices;

namespace Windows.Imaging.Utility;

/// <summary>
/// Prevents idle system sleep while an imaging/servicing operation is active.
/// This changes only the execution state of the current UI thread; it does not
/// modify the user's selected Windows power plan.
/// </summary>
internal static class WindowsExecutionState
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000
    }

    public static bool TryPreventSystemSleep() =>
        SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired) != 0;

    public static void ClearSystemSleepRequest() =>
        _ = SetThreadExecutionState(ExecutionState.Continuous);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
}
