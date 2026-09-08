namespace Windows.Imaging.Utility;

internal static class WindowsImagingUtilityActivation
{
    private const string UtilityMutexName = @"Local\WindowsImagingUtility.Windows";
    private const string UtilityActivateEventName = @"Local\WindowsImagingUtility.Windows.Activate";

    public static Mutex? TryAcquireUtilityMutex()
    {
        try
        {
            Mutex mutex = new(initiallyOwned: true, name: UtilityMutexName, createdNew: out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return null;
            }

            return mutex;
        }
        catch
        {
            return null;
        }
    }

    public static EventWaitHandle CreateUtilityActivateEvent() =>
        new(initialState: false, mode: EventResetMode.AutoReset, name: UtilityActivateEventName);

    public static void SignalExistingUtility()
    {
        try
        {
            using EventWaitHandle handle = EventWaitHandle.OpenExisting(UtilityActivateEventName);
            handle.Set();
        }
        catch
        {
        }
    }
}
