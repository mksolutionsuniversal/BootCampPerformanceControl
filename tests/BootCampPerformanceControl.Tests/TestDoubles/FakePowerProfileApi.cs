using BootCampPerformanceControl.PowerManagement;

namespace BootCampPerformanceControl.Tests.TestDoubles;

internal sealed class FakePowerProfileApi : IPowerProfileApi
{
    private readonly Dictionary<Guid, ProcessorPowerSettings> _settingsByScheme = [];

    public FakePowerProfileApi(Guid activeSchemeId, ProcessorPowerSettings activeSettings)
    {
        ActiveSchemeId = activeSchemeId;
        _settingsByScheme.Add(activeSchemeId, activeSettings);
    }

    public Guid ActiveSchemeId { get; private set; }

    public int NativeWriteCount { get; private set; }

    public int SetActiveSchemeCount { get; private set; }

    public int? FailOnNativeWriteNumber { get; set; }

    public HashSet<int> FailOnNativeWriteNumbers { get; } = [];

    public HashSet<int> IgnoreNativeWriteNumbers { get; } = [];

    public void AddScheme(Guid schemeId, ProcessorPowerSettings settings)
    {
        _settingsByScheme[schemeId] = settings;
    }

    public ProcessorPowerSettings GetSettings(Guid schemeId)
    {
        return _settingsByScheme[schemeId];
    }

    public Guid GetActiveScheme()
    {
        return ActiveSchemeId;
    }

    public uint ReadAcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid)
    {
        var settings = GetSettings(schemeGuid);
        return settingGuid == PowerSettingGuids.ProcessorMaximumThrottle
            ? settings.ProcessorMaximumAc
            : settings.BoostModeAc;
    }

    public uint ReadDcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid)
    {
        var settings = GetSettings(schemeGuid);
        return settingGuid == PowerSettingGuids.ProcessorMaximumThrottle
            ? settings.ProcessorMaximumDc
            : settings.BoostModeDc;
    }

    public void WriteAcValueIndex(
        Guid schemeGuid,
        Guid subgroupGuid,
        Guid settingGuid,
        uint value)
    {
        if (ShouldIgnoreConfiguredWrite())
        {
            return;
        }

        var current = GetSettings(schemeGuid);
        _settingsByScheme[schemeGuid] = settingGuid == PowerSettingGuids.ProcessorMaximumThrottle
            ? current with { ProcessorMaximumAc = value }
            : current with { BoostModeAc = value };
    }

    public void WriteDcValueIndex(
        Guid schemeGuid,
        Guid subgroupGuid,
        Guid settingGuid,
        uint value)
    {
        if (ShouldIgnoreConfiguredWrite())
        {
            return;
        }

        var current = GetSettings(schemeGuid);
        _settingsByScheme[schemeGuid] = settingGuid == PowerSettingGuids.ProcessorMaximumThrottle
            ? current with { ProcessorMaximumDc = value }
            : current with { BoostModeDc = value };
    }

    public void SetActiveScheme(Guid schemeGuid)
    {
        if (!_settingsByScheme.ContainsKey(schemeGuid))
        {
            throw new InvalidOperationException($"Unknown fake scheme {schemeGuid}.");
        }

        SetActiveSchemeCount++;
        ActiveSchemeId = schemeGuid;
    }

    private bool ShouldIgnoreConfiguredWrite()
    {
        NativeWriteCount++;
        if (NativeWriteCount == FailOnNativeWriteNumber
            || FailOnNativeWriteNumbers.Contains(NativeWriteCount))
        {
            throw new InvalidOperationException(
                $"Configured fake failure on native write {NativeWriteCount}.");
        }

        return IgnoreNativeWriteNumbers.Contains(NativeWriteCount);
    }
}
