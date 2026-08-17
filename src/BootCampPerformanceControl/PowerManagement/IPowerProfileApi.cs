namespace BootCampPerformanceControl.PowerManagement;

internal interface IPowerProfileApi
{
    Guid GetActiveScheme();

    uint ReadAcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid);

    uint ReadDcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid);

    void WriteAcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, uint value);

    void WriteDcValueIndex(Guid schemeGuid, Guid subgroupGuid, Guid settingGuid, uint value);

    void SetActiveScheme(Guid schemeGuid);
}
