using System.Globalization;
using System.Text;
using BootCampPerformanceControl.ApplicationInfo;
using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.Diagnostics;

public sealed class CompatibilityReportService : ICompatibilityReportService
{
    private const string GamingOptimisedProfileId = "gaming-optimised";
    private const string Unknown = DiagnosticPrivacySanitizer.Unknown;
    private const string Unavailable = "Unavailable";

    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IPowerManagementService _powerManagementService;
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileExecutionResolver _profileExecutionResolver;
    private readonly IApplicationLogger _logger;

    public CompatibilityReportService(
        IHardwareDetectionService hardwareDetectionService,
        IPowerManagementService powerManagementService,
        IRestoreSnapshotStore restoreSnapshotStore,
        IProfileCatalog profileCatalog,
        ProfileExecutionResolver profileExecutionResolver,
        IApplicationLogger logger)
    {
        ArgumentNullException.ThrowIfNull(hardwareDetectionService);
        ArgumentNullException.ThrowIfNull(powerManagementService);
        ArgumentNullException.ThrowIfNull(restoreSnapshotStore);
        ArgumentNullException.ThrowIfNull(profileCatalog);
        ArgumentNullException.ThrowIfNull(profileExecutionResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _hardwareDetectionService = hardwareDetectionService;
        _powerManagementService = powerManagementService;
        _restoreSnapshotStore = restoreSnapshotStore;
        _profileCatalog = profileCatalog;
        _profileExecutionResolver = profileExecutionResolver;
        _logger = logger;
    }

    public async Task<CompatibilityReportResult> GenerateAsync(
        FanControlStatus currentFanStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentFanStatus);
        cancellationToken.ThrowIfCancellationRequested();

        var hardwareSnapshot = await ReadHardwareSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        var verificationResult = VerifyModel(hardwareSnapshot);
        var powerState = await ReadPowerStateAsync(cancellationToken)
            .ConfigureAwait(false);
        var restoreSnapshotPresent = ReadRestoreSnapshotPresence(cancellationToken);
        var profileSupport = ResolveProfileSupport(verificationResult, powerState);

        return new CompatibilityReportResult(
            BuildReport(
                hardwareSnapshot,
                verificationResult,
                powerState,
                restoreSnapshotPresent,
                profileSupport,
                currentFanStatus),
            CreateSuggestedFileName(hardwareSnapshot?.ComputerSystem.Model));
    }

    private async Task<HardwareSnapshot?> ReadHardwareSnapshotAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _hardwareDetectionService
                .DetectAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Compatibility report generation failed while reading hardware details.", exception);
            return null;
        }
    }

    private ModelVerificationResult VerifyModel(HardwareSnapshot? hardwareSnapshot)
    {
        if (hardwareSnapshot is null)
        {
            return ModelVerificationResult.Unknown();
        }

        try
        {
            return _hardwareDetectionService.VerifyModel(hardwareSnapshot);
        }
        catch (Exception exception)
        {
            _logger.Error("Compatibility report generation failed while verifying the hardware model.", exception);
            return ModelVerificationResult.Unknown();
        }
    }

    private async Task<PowerStateSnapshot?> ReadPowerStateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _powerManagementService
                .ReadCurrentStateAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Compatibility report generation failed while reading current power settings.", exception);
            return null;
        }
    }

    private bool? ReadRestoreSnapshotPresence(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _restoreSnapshotStore.HasOriginalRestoreSnapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.Error("Compatibility report generation failed while checking restore snapshot presence.", exception);
            return null;
        }
    }

    private CompatibilityProfileSupport ResolveProfileSupport(
        ModelVerificationResult verificationResult,
        PowerStateSnapshot? powerState)
    {
        try
        {
            var profile = _profileCatalog
                .GetProfiles(verificationResult)
                .FirstOrDefault(profile => string.Equals(
                    profile.Id,
                    GamingOptimisedProfileId,
                    StringComparison.OrdinalIgnoreCase));

            if (profile is null)
            {
                return new CompatibilityProfileSupport(
                    PowerStateReadable: powerState is not null,
                    GamingOptimisedEligible: false);
            }

            var resolution = _profileExecutionResolver.ResolveProcessorSettings(
                profile,
                verificationResult);
            var isPlatformSupported = profile.IsAvailableForDetectedModel && resolution.IsExecutable;
            var isPowerStateReadable = powerState is not null;

            return new CompatibilityProfileSupport(
                PowerStateReadable: isPowerStateReadable,
                GamingOptimisedEligible: isPlatformSupported && isPowerStateReadable);
        }
        catch (Exception exception)
        {
            _logger.Error("Compatibility report generation failed while resolving profile support.", exception);
            return new CompatibilityProfileSupport(
                PowerStateReadable: false,
                GamingOptimisedEligible: false);
        }
    }

    private static string BuildReport(
        HardwareSnapshot? hardwareSnapshot,
        ModelVerificationResult verificationResult,
        PowerStateSnapshot? powerState,
        bool? restoreSnapshotPresent,
        CompatibilityProfileSupport profileSupport,
        FanControlStatus fanStatus)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BootCamp Performance Control Compatibility Report");
        builder.AppendLine("=================================================");
        builder.AppendLine();
        builder.AppendLine("No report is uploaded automatically. Review this text before sharing it.");
        builder.AppendLine("To report an issue, copy this report, open a GitHub issue, and paste it there.");
        builder.AppendLine();
        builder.AppendLine("App");
        builder.AppendLine("---");
        builder.AppendLine($"BCPC version: {GetApplicationVersion()}");
        builder.AppendLine();
        builder.AppendLine("Hardware");
        builder.AppendLine("--------");
        builder.AppendLine($"Manufacturer: {FormatValue(hardwareSnapshot?.ComputerSystem.Manufacturer)}");
        builder.AppendLine($"Mac model identifier: {FormatValue(hardwareSnapshot?.ComputerSystem.Model)}");
        builder.AppendLine($"CPU: {FormatValue(hardwareSnapshot?.Processor?.Name)}");
        builder.AppendLine($"Core/thread count: {FormatCoreThreadCount(hardwareSnapshot?.Processor)}");
        builder.AppendLine("GPU(s):");
        AppendGpuLines(builder, hardwareSnapshot?.VideoControllers);
        builder.AppendLine($"Windows version/build: {FormatOperatingSystem(hardwareSnapshot?.OperatingSystem)}");
        builder.AppendLine();
        builder.AppendLine("Power");
        builder.AppendLine("-----");
        builder.AppendLine($"Active power scheme: {FormatPowerScheme(powerState)}");
        builder.AppendLine($"PROCTHROTTLEMAX AC: {FormatPercentage(powerState?.ProcessorMaximumAc)}");
        builder.AppendLine($"PROCTHROTTLEMAX DC: {FormatPercentage(powerState?.ProcessorMaximumDc)}");
        builder.AppendLine($"PERFBOOSTMODE AC: {FormatUInt32(powerState?.BoostModeAc)}");
        builder.AppendLine($"PERFBOOSTMODE DC: {FormatUInt32(powerState?.BoostModeDc)}");
        builder.AppendLine($"Processor state readable: {FormatYesNo(profileSupport.PowerStateReadable)}");
        builder.AppendLine();
        builder.AppendLine("Power validation");
        builder.AppendLine("----------------");
        builder.AppendLine($"Gaming Optimised eligibility: {FormatYesNo(profileSupport.GamingOptimisedEligible)}");
        builder.AppendLine($"Model validation level: {verificationResult.ValidationLevel}");
        builder.AppendLine($"Platform support: {verificationResult.PlatformSupport}");
        builder.AppendLine($"Validation details: {FormatValue(verificationResult.Message)}");
        builder.AppendLine();
        builder.AppendLine("Restore");
        builder.AppendLine("-------");
        builder.AppendLine($"Original Restore snapshot present: {FormatYesNo(restoreSnapshotPresent)}");
        builder.AppendLine();
        builder.AppendLine("Fan compatibility");
        builder.AppendLine("-----------------");
        builder.AppendLine($"AppleSMC backend state: {fanStatus.BackendDisplayText}");
        builder.AppendLine($"Transport: {fanStatus.TransportDisplayText}");
        builder.AppendLine($"FNum / discovered fan count: {FormatFanCount(fanStatus.ReportedFanCount)} / {FormatFanCount(fanStatus.DiscoveredFanCount)}");
        builder.AppendLine($"Fan safety state: {fanStatus.SafetyDisplayText}");
        AppendFanRpmLines(builder, fanStatus);
        builder.AppendLine($"Mode: {FormatFanMode(fanStatus)}");
        builder.AppendLine($"Write control state: {fanStatus.WriteControlDisplayText}");
        builder.AppendLine($"Fan status/details: {FormatValue(fanStatus.Details)}");

        return DiagnosticPrivacySanitizer.RedactPrivacySensitiveValues(
            builder.ToString());
    }

    private static void AppendGpuLines(
        StringBuilder builder,
        IReadOnlyList<VideoControllerInfo>? videoControllers)
    {
        var gpuNames = videoControllers?
            .Select(videoController => FormatValue(videoController.Name))
            .Where(name => !string.Equals(name, Unknown, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (gpuNames is null || gpuNames.Count == 0)
        {
            builder.AppendLine("  - Unknown");
            return;
        }

        foreach (var gpuName in gpuNames)
        {
            builder.AppendLine($"  - {gpuName}");
        }
    }

    private static string FormatOperatingSystem(OperatingSystemInfo? operatingSystem)
    {
        if (operatingSystem is null)
        {
            return Unknown;
        }

        var caption = FormatValue(operatingSystem.Caption);
        var version = FormatValue(operatingSystem.Version);
        var buildNumber = FormatValue(operatingSystem.BuildNumber);
        var architecture = FormatValue(operatingSystem.OSArchitecture);
        var parts = new List<string>();

        if (!string.Equals(caption, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(caption);
        }

        var versionParts = new List<string>();
        if (!string.Equals(version, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            versionParts.Add(version);
        }

        if (!string.Equals(buildNumber, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            versionParts.Add($"(Build {buildNumber})");
        }

        if (versionParts.Count > 0)
        {
            parts.Add(string.Join(" ", versionParts));
        }

        if (!string.Equals(architecture, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(architecture);
        }

        return parts.Count == 0 ? Unknown : string.Join(", ", parts);
    }

    private static string FormatCoreThreadCount(ProcessorInfo? processor)
    {
        return processor is null
            ? Unknown
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} cores / {1} threads",
                processor.NumberOfCores,
                processor.NumberOfLogicalProcessors);
    }

    private static void AppendFanRpmLines(
        StringBuilder builder,
        FanControlStatus fanStatus)
    {
        if (!fanStatus.IsAvailable)
        {
            builder.AppendLine($"Fans: {Unavailable}");
            return;
        }

        if (fanStatus.Fans.Count == 0)
        {
            builder.AppendLine("Fans: None reported (passive topology)");
            return;
        }

        foreach (var fan in fanStatus.Fans)
        {
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Fan {0} RPM: {1:0} / {2:0} RPM",
                fan.Index,
                fan.Reading.ActualRpm,
                fan.Reading.MaximumRpm));
        }
    }

    private static string FormatFanMode(FanControlStatus fanStatus)
    {
        return fanStatus.IsAvailable ? fanStatus.ModeDisplayText : Unavailable;
    }

    private static string FormatFanCount(int? fanCount)
    {
        return fanCount?.ToString(CultureInfo.InvariantCulture) ?? Unavailable;
    }

    private static string FormatPowerScheme(PowerStateSnapshot? powerState)
    {
        return powerState?.SchemeId.ToString() ?? Unknown;
    }

    private static string FormatPercentage(uint? value)
    {
        return value.HasValue ? $"{value.Value}%" : Unknown;
    }

    private static string FormatUInt32(uint? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : Unknown;
    }

    private static string FormatYesNo(bool? value)
    {
        return value switch
        {
            true => "Yes",
            false => "No",
            null => Unknown
        };
    }

    private static string FormatValue(string? value)
    {
        return DiagnosticPrivacySanitizer.FormatValue(value);
    }

    private static string GetApplicationVersion()
    {
        return ApplicationVersionProvider.GetInformationalVersion();
    }

    private static string CreateSuggestedFileName(string? model)
    {
        var modelSegment = DiagnosticPrivacySanitizer.CreateSafeFileNameSegment(model);
        var versionSegment = DiagnosticPrivacySanitizer.CreateSafeFileNameSegment(GetApplicationVersion());
        return $"BootCampPerformanceControl-Compatibility-{modelSegment}-{versionSegment}.txt";
    }

    private sealed record CompatibilityProfileSupport(
        bool PowerStateReadable,
        bool GamingOptimisedEligible);
}
