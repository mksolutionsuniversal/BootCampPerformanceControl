namespace BootCampPerformanceControl.PowerManagement;

public sealed class WindowsPowerManagementService : IPowerManagementService
{
    public async Task<PowerStateSnapshot> ReadCurrentStateAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(ReadCurrentState, cancellationToken).ConfigureAwait(false);
    }

    private static PowerStateSnapshot ReadCurrentState()
    {
        var schemeId = NativePowerProfileApi.GetActiveScheme();
        var subgroup = PowerSettingGuids.ProcessorSettingsSubgroup;
        var processorMaximum = PowerSettingGuids.ProcessorMaximumThrottle;
        var boostMode = PowerSettingGuids.ProcessorPerformanceBoostMode;

        return new PowerStateSnapshot(
            schemeId,
            NativePowerProfileApi.ReadAcValueIndex(schemeId, subgroup, processorMaximum),
            NativePowerProfileApi.ReadDcValueIndex(schemeId, subgroup, processorMaximum),
            NativePowerProfileApi.ReadAcValueIndex(schemeId, subgroup, boostMode),
            NativePowerProfileApi.ReadDcValueIndex(schemeId, subgroup, boostMode),
            DateTimeOffset.Now);
    }
}
