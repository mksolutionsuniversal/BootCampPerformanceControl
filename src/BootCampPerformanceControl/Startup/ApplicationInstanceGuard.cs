namespace BootCampPerformanceControl.Startup;

internal sealed class ApplicationInstanceGuard : IDisposable
{
    internal const string MainApplicationMutexName =
        @"Local\BootCampPerformanceControl.MainApplication";

    private readonly Mutex _mutex;
    private bool _hasOwnership = true;
    private bool _isDisposed;

    private ApplicationInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
    }

    internal static ApplicationInstanceGuard? TryAcquire(
        string mutexName = MainApplicationMutexName)
    {
        if (string.IsNullOrWhiteSpace(mutexName))
        {
            throw new ArgumentException(
                "A mutex name is required.",
                nameof(mutexName));
        }

        var mutex = new Mutex(initiallyOwned: false, mutexName);

        try
        {
            try
            {
                if (!mutex.WaitOne(TimeSpan.Zero))
                {
                    mutex.Dispose();
                    return null;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous process died while holding the mutex; this process now owns it.
            }

            return new ApplicationInstanceGuard(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_hasOwnership)
        {
            _mutex.ReleaseMutex();
            _hasOwnership = false;
        }

        _mutex.Dispose();
    }
}
