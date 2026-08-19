using BootCampPerformanceControl.Startup;

namespace BootCampPerformanceControl.Tests.Startup;

public sealed class ApplicationInstanceGuardTests
{
    [Fact]
    public void TryAcquire_WhenNoInstanceOwnsMutex_ReturnsGuard()
    {
        using var guard = ApplicationInstanceGuard.TryAcquire(CreateMutexName());

        Assert.NotNull(guard);
    }

    [Fact]
    public void TryAcquire_WhenMutexIsAlreadyOwned_ReturnsNull()
    {
        var mutexName = CreateMutexName();
        using var firstGuard = ApplicationInstanceGuard.TryAcquire(mutexName);

        var secondAcquired = TryAcquireOnWorkerThread(mutexName);

        Assert.NotNull(firstGuard);
        Assert.False(secondAcquired);
    }

    [Fact]
    public void Dispose_ReleasesMutexForLaterAcquisition()
    {
        var mutexName = CreateMutexName();
        using (var firstGuard = ApplicationInstanceGuard.TryAcquire(mutexName))
        {
            Assert.NotNull(firstGuard);
        }

        using var secondGuard = ApplicationInstanceGuard.TryAcquire(mutexName);

        Assert.NotNull(secondGuard);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void TryAcquire_EmptyMutexName_Throws(string mutexName)
    {
        Assert.Throws<ArgumentException>(
            () => ApplicationInstanceGuard.TryAcquire(mutexName));
    }

    private static string CreateMutexName()
    {
        return $@"Local\BootCampPerformanceControl.Tests.{Guid.NewGuid():N}";
    }

    private static bool TryAcquireOnWorkerThread(
        string mutexName)
    {
        var acquired = false;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var guard = ApplicationInstanceGuard.TryAcquire(mutexName);
                acquired = guard is not null;
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }
        });

        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }

        return acquired;
    }
}
