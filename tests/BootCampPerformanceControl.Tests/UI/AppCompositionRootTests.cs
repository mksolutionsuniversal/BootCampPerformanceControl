using BootCampPerformanceControl.Tests.TestDoubles;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.UI;

public sealed class AppCompositionRootTests
{
    [Fact]
    public void CreateMainViewModel_SucceedsWithoutSmcAccess_DefersOwnershipStoreAccess()
    {
        var logger = new TestApplicationLogger();

        var viewModel = AppCompositionRoot.CreateMainViewModel(logger);

        Assert.NotNull(viewModel);
        Assert.NotNull(viewModel.RefreshCommand);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("Not detected", viewModel.MacModel);
        Assert.Equal("No pending fan recovery.", viewModel.FanRecoveryStatus);
        Assert.Empty(logger.Errors);
    }
}
