using System.Globalization;
using System.Management;

namespace BootCampPerformanceControl.HardwareDetection;

public sealed class HardwareDetectionService : IHardwareDetectionService
{
    public async Task<HardwareSnapshot> DetectAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => Detect(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (ManagementException exception)
        {
            throw new InvalidOperationException(
                "Hardware detection failed while querying Windows Management Instrumentation.",
                exception);
        }
    }

    public ModelVerificationResult VerifyModel(HardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var manufacturer = snapshot.ComputerSystem?.Manufacturer ?? "Unknown";
        var model = snapshot.ComputerSystem?.Model ?? "Unknown";

        if (string.IsNullOrWhiteSpace(manufacturer)
            || string.Equals(manufacturer.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelVerificationResult(
                manufacturer,
                model,
                PlatformSupportStatus.DetectionIncomplete,
                "Hardware detection was incomplete. Platform compatibility could not be determined.");
        }

        var isApple = manufacturer.Contains("Apple", StringComparison.OrdinalIgnoreCase);
        if (!isApple)
        {
            return new ModelVerificationResult(
                manufacturer,
                model,
                PlatformSupportStatus.UnsupportedNonApple,
                "BootCamp Performance Control requires an Apple Mac running Windows through Boot Camp.");
        }

        if (snapshot.Processor is null
            || (string.IsNullOrWhiteSpace(snapshot.Processor.Name)
                && string.IsNullOrWhiteSpace(snapshot.Processor.Manufacturer)))
        {
            return new ModelVerificationResult(
                manufacturer,
                model,
                PlatformSupportStatus.DetectionIncomplete,
                "Processor information could not be determined from hardware detection.");
        }

        var isIntel = snapshot.Processor.IsIntel;
        if (!isIntel)
        {
            return new ModelVerificationResult(
                manufacturer,
                model,
                PlatformSupportStatus.UnsupportedNonIntel,
                "BootCamp Performance Control requires an Intel processor on Apple hardware.");
        }

        return new ModelVerificationResult(
            manufacturer,
            model,
            PlatformSupportStatus.SupportedIntelMac,
            string.Empty);
    }

    private static HardwareSnapshot Detect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var computerSystem = ReadComputerSystem(cancellationToken);
        var processor = ReadProcessor(cancellationToken);
        var videoControllers = ReadVideoControllers(cancellationToken);
        var operatingSystem = ReadOperatingSystem(cancellationToken);

        return new HardwareSnapshot(
            computerSystem,
            processor,
            videoControllers,
            operatingSystem,
            DateTimeOffset.Now);
    }

    private static ComputerSystemInfo ReadComputerSystem(CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Manufacturer, Model, SystemType FROM Win32_ComputerSystem");
        using var results = searcher.Get();

        foreach (ManagementObject item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var managementObject = item;
            return new ComputerSystemInfo(
                ReadString(managementObject, "Manufacturer"),
                ReadString(managementObject, "Model"),
                ReadString(managementObject, "SystemType"));
        }

        return new ComputerSystemInfo("Unknown", "Unknown", "Unknown");
    }

    private static ProcessorInfo? ReadProcessor(CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
        using var results = searcher.Get();

        foreach (ManagementObject item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var managementObject = item;
            return new ProcessorInfo(
                ReadString(managementObject, "Name"),
                ReadString(managementObject, "Manufacturer"),
                ReadUInt32(managementObject, "NumberOfCores"),
                ReadUInt32(managementObject, "NumberOfLogicalProcessors"),
                ReadUInt32(managementObject, "MaxClockSpeed"));
        }

        return null;
    }

    private static IReadOnlyList<VideoControllerInfo> ReadVideoControllers(CancellationToken cancellationToken)
    {
        var controllers = new List<VideoControllerInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
        using var results = searcher.Get();

        foreach (ManagementObject item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var managementObject = item;
            controllers.Add(new VideoControllerInfo(
                ReadString(managementObject, "Name"),
                ReadString(managementObject, "DriverVersion"),
                ReadNullableUInt64(managementObject, "AdapterRAM")));
        }

        return controllers;
    }

    private static OperatingSystemInfo? ReadOperatingSystem(CancellationToken cancellationToken)
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem");
        using var results = searcher.Get();

        foreach (ManagementObject item in results)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var managementObject = item;
            return new OperatingSystemInfo(
                ReadString(managementObject, "Caption"),
                ReadString(managementObject, "Version"),
                ReadString(managementObject, "BuildNumber"),
                ReadString(managementObject, "OSArchitecture"));
        }

        return null;
    }

    private static string ReadString(ManagementBaseObject source, string propertyName)
    {
        var value = source[propertyName]?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    }

    private static uint ReadUInt32(ManagementBaseObject source, string propertyName)
    {
        var value = source[propertyName];
        return value is null ? 0 : Convert.ToUInt32(value, CultureInfo.InvariantCulture);
    }

    private static ulong? ReadNullableUInt64(ManagementBaseObject source, string propertyName)
    {
        var value = source[propertyName];
        return value is null ? null : Convert.ToUInt64(value, CultureInfo.InvariantCulture);
    }
}
