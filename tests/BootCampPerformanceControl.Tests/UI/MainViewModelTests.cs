using System.Reflection;
using BootCampPerformanceControl.SettingsBackup;
using BootCampPerformanceControl.UI;

namespace BootCampPerformanceControl.Tests.UI;

public sealed class MainViewModelTests
{
    [Fact]
    public void MainViewModel_DoesNotDependOnRestoreSnapshotStore()
    {
        var constructorParameters = typeof(MainViewModel)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters());
        var instanceFields = typeof(MainViewModel).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.DoesNotContain(
            constructorParameters,
            parameter => typeof(IRestoreSnapshotStore).IsAssignableFrom(parameter.ParameterType));
        Assert.DoesNotContain(
            instanceFields,
            field => typeof(IRestoreSnapshotStore).IsAssignableFrom(field.FieldType));
    }
}
