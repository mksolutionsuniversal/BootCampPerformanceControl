using System.Globalization;
using System.Text;
using BootCampPerformanceControl.ApplicationInfo;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.Diagnostics;

public sealed class DiagnosticReportService : IDiagnosticReportService
{
    private const string Unknown = DiagnosticPrivacySanitizer.Unknown;
    private const string GamingOptimisedProfileId = "gaming-optimised";

    private readonly IHardwareDetectionService _hardwareDetectionService;
    private readonly IPowerManagementService _powerManagementService;
    private readonly IRestoreSnapshotStore _restoreSnapshotStore;
    private readonly IProfileCatalog _profileCatalog;
    private readonly ProfileExecutionResolver _profileExecutionResolver;
    private readonly IApplicationLogger _logger;

    public DiagnosticReportService(
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

    public async Task<DiagnosticReportResult> GenerateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hardwareSnapshot = await ReadHardwareSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);
        var verificationResult = VerifyModel(hardwareSnapshot);
        var powerState = await ReadPowerStateAsync(cancellationToken)
            .ConfigureAwait(false);
        var restoreSnapshotPresent = ReadRestoreSnapshotPresence(cancellationToken);
        var profileSupport = ResolveProfileSupport(verificationResult, powerState);

        return new DiagnosticReportResult(
            BuildReport(
                hardwareSnapshot,
                verificationResult,
                powerState,
                restoreSnapshotPresent,
                profileSupport),
            CreateSuggestedFileName(hardwareSnapshot?.ComputerSystem.Model));
    }

    private async Task<HardwareSnapshot?> ReadHardwareSnapshotAsync(CancellationToken cancellationToken)
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
            _logger.Error("Diagnostic report generation failed while reading hardware details.", exception);
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
            _logger.Error("Diagnostic report generation failed while verifying the hardware model.", exception);
            return ModelVerificationResult.Unknown();
        }
    }

    private async Task<PowerStateSnapshot?> ReadPowerStateAsync(CancellationToken cancellationToken)
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
            _logger.Error("Diagnostic report generation failed while reading current power settings.", exception);
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
            _logger.Error("Diagnostic report generation failed while checking restore snapshot presence.", exception);
            return null;
        }
    }

    private DiagnosticProfileSupport ResolveProfileSupport(
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
                return new DiagnosticProfileSupport(
                    PlatformSupported: false,
                    PowerStateReadable: powerState is not null,
                    GamingOptimisedEligible: false);
            }

            var resolution = _profileExecutionResolver.ResolveProcessorSettings(
                profile,
                verificationResult);

            var isPlatformSupported = profile.IsAvailableForDetectedModel && resolution.IsExecutable;
            var isPowerStateReadable = powerState is not null;

            return new DiagnosticProfileSupport(
                PlatformSupported: isPlatformSupported,
                PowerStateReadable: isPowerStateReadable,
                GamingOptimisedEligible: isPlatformSupported && isPowerStateReadable);
        }
        catch (Exception exception)
        {
            _logger.Error("Diagnostic report generation failed while resolving profile support.", exception);
            return new DiagnosticProfileSupport(
                PlatformSupported: false,
                PowerStateReadable: false,
                GamingOptimisedEligible: false);
        }
    }

    private static string BuildReport(
        HardwareSnapshot? hardwareSnapshot,
        ModelVerificationResult verificationResult,
        PowerStateSnapshot? powerState,
        bool? restoreSnapshotPresent,
        DiagnosticProfileSupport profileSupport)
    {
        var builder = new StringBuilder();
        builder.AppendLine("BootCamp Performance Control Diagnostics");
        builder.AppendLine("========================================");
        builder.AppendLine();
        builder.AppendLine("App");
        builder.AppendLine("---");
        builder.AppendLine($"Version: {GetApplicationVersion()}");
        builder.AppendLine();
        builder.AppendLine("Operating System");
        builder.AppendLine("----------------");
        builder.AppendLine($"Windows: {FormatOperatingSystem(hardwareSnapshot?.OperatingSystem)}");
        builder.AppendLine();
        builder.AppendLine("Hardware");
        builder.AppendLine("--------");
        builder.AppendLine($"Manufacturer: {FormatValue(hardwareSnapshot?.ComputerSystem.Manufacturer)}");
        builder.AppendLine($"Mac Model: {FormatValue(hardwareSnapshot?.ComputerSystem.Model)}");
        builder.AppendLine($"CPU: {FormatValue(hardwareSnapshot?.Processor?.Name)}");
        builder.AppendLine("GPU:");
        AppendGpuLines(builder, hardwareSnapshot?.VideoControllers);
        builder.AppendLine();
        builder.AppendLine("Power");
        builder.AppendLine("-----");
        builder.AppendLine($"Active Power Scheme: {FormatPowerScheme(powerState)}");
        builder.AppendLine();
        builder.AppendLine("PROCTHROTTLEMAX");
        builder.AppendLine($"  AC: {FormatPercentage(powerState?.ProcessorMaximumAc)}");
        builder.AppendLine($"  DC: {FormatPercentage(powerState?.ProcessorMaximumDc)}");
        builder.AppendLine();
        builder.AppendLine("PERFBOOSTMODE");
        builder.AppendLine($"  AC: {FormatUInt32(powerState?.BoostModeAc)}");
        builder.AppendLine($"  DC: {FormatUInt32(powerState?.BoostModeDc)}");
        builder.AppendLine();
        builder.AppendLine("Restore");
        builder.AppendLine("-------");
        builder.AppendLine($"Original restore snapshot present: {FormatYesNo(restoreSnapshotPresent)}");
        builder.AppendLine();
        builder.AppendLine("Platform & Profile Support");
        builder.AppendLine("--------------------------");
        builder.AppendLine($"Platform support: {verificationResult.PlatformSupport}");
        if (!verificationResult.IsSupportedIntelMac
            && !string.IsNullOrWhiteSpace(verificationResult.Message))
        {
            builder.AppendLine($"Platform details: {FormatValue(verificationResult.Message)}");
        }
        builder.AppendLine($"Processor power settings readable: {FormatYesNo(profileSupport.PowerStateReadable)}");
        builder.AppendLine($"Gaming Optimised eligible: {FormatYesNo(profileSupport.GamingOptimisedEligible)}");

        return builder.ToString();
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
        return $"BootCampPerformanceControl-Diagnostics-{CreateSafeFileNameSegment(model)}.txt";
    }

    private static string CreateSafeFileNameSegment(string? value)
    {
        return DiagnosticPrivacySanitizer.CreateSafeFileNameSegment(value);
    }

    private sealed record DiagnosticProfileSupport(
        bool PlatformSupported,
        bool PowerStateReadable,
        bool GamingOptimisedEligible);
}
