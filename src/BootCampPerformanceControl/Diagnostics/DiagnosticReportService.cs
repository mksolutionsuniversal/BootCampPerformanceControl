using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BootCampPerformanceControl.HardwareDetection;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.Profiles;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.Diagnostics;

public sealed class DiagnosticReportService : IDiagnosticReportService
{
    private const string Unknown = "Unknown";
    private const string GamingOptimisedProfileId = "gaming-optimised";
    private static readonly Regex EmailAddressRegex = new(
        @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsPathRegex = new(
        @"\b[A-Z]:[\\/][^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UserHomePathRegex = new(
        @"(?<!\w)/(?:Users|home)/[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UncPathRegex = new(
        @"\\\\[^\\/\s]+[\\/][^\s,;]+",
        RegexOptions.CultureInvariant);
    private static readonly Regex IpAddressRegex = new(
        @"\b(?:\d{1,3}\.){3}\d{1,3}\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex MacAddressRegex = new(
        @"\b[0-9A-F]{2}([-:])[0-9A-F]{2}(?:\1[0-9A-F]{2}){4}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsProductKeyRegex = new(
        @"\b[A-Z0-9]{5}(?:-[A-Z0-9]{5}){4}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DomainUserRegex = new(
        @"\b[A-Z0-9_.-]+\\[A-Z0-9_.-]+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CommonWindowsHostNameRegex = new(
        @"\b(?:DESKTOP|LAPTOP|WIN)-[A-Z0-9-]+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
        var profileSupport = ResolveProfileSupport(verificationResult);

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
        catch (Exception exception)
        {
            _logger.Error("Diagnostic report generation failed while checking restore snapshot presence.", exception);
            return null;
        }
    }

    private DiagnosticProfileSupport ResolveProfileSupport(ModelVerificationResult verificationResult)
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
                    GamingOptimisedVerified: false,
                    HardwareWritesAllowed: false);
            }

            var resolution = _profileExecutionResolver.ResolveProcessorSettings(
                profile,
                verificationResult);

            return new DiagnosticProfileSupport(
                GamingOptimisedVerified: profile.IsAvailableForDetectedModel && resolution.IsExecutable,
                HardwareWritesAllowed: resolution.IsExecutable);
        }
        catch (Exception exception)
        {
            _logger.Error("Diagnostic report generation failed while resolving profile support.", exception);
            return new DiagnosticProfileSupport(
                GamingOptimisedVerified: false,
                HardwareWritesAllowed: false);
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
        builder.AppendLine("Profile Support");
        builder.AppendLine("---------------");
        builder.AppendLine($"Apple hardware detected: {FormatYesNo(verificationResult.IsApple)}");
        builder.AppendLine($"Model verified: {FormatYesNo(verificationResult.IsVerified)}");
        builder.AppendLine($"Model verification status: {verificationResult.Status}");
        builder.AppendLine($"Model verification message: {FormatValue(verificationResult.Message)}");
        builder.AppendLine($"Gaming Optimised verified: {FormatYesNo(profileSupport.GamingOptimisedVerified)}");
        builder.AppendLine(
            $"Model-specific processor power writes allowed: {FormatYesNo(profileSupport.HardwareWritesAllowed)}");

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

        if (!string.Equals(version, Unknown, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(buildNumber, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"{version} (Build {buildNumber})");
        }
        else if (!string.Equals(version, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(version);
        }
        else if (!string.Equals(buildNumber, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"Build {buildNumber}");
        }

        if (!string.Equals(architecture, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(architecture);
        }

        return parts.Count == 0 ? Unknown : string.Join(", ", parts);
    }

    private static string GetApplicationVersion()
    {
        var assembly = typeof(DiagnosticReportService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return FormatValue(informationalVersion);
        }

        return FormatValue(assembly.GetName().Version?.ToString());
    }

    private static string CreateSuggestedFileName(string? model)
    {
        return $"BootCampPerformanceControl-Diagnostics-{CreateSafeFileNameSegment(model)}.txt";
    }

    private static string CreateSafeFileNameSegment(string? value)
    {
        var formattedValue = FormatValue(value);
        if (string.Equals(formattedValue, Unknown, StringComparison.OrdinalIgnoreCase))
        {
            return Unknown;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(formattedValue.Length);
        foreach (var character in formattedValue)
        {
            builder.Append(
                invalidCharacters.Contains(character) || char.IsControl(character)
                    ? '_'
                    : character);
        }

        var fileNameSegment = builder.ToString().Trim(' ', '.');
        return string.IsNullOrWhiteSpace(fileNameSegment) ? Unknown : fileNameSegment;
    }

    private static string FormatPowerScheme(PowerStateSnapshot? powerState)
    {
        return powerState?.SchemeId.ToString() ?? Unknown;
    }

    private static string FormatPercentage(uint? value)
    {
        return value is null ? Unknown : $"{value.Value.ToString(CultureInfo.InvariantCulture)}%";
    }

    private static string FormatUInt32(uint? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? Unknown;
    }

    private static string FormatYesNo(bool? value)
    {
        return value switch
        {
            true => "Yes",
            false => "No",
            _ => Unknown
        };
    }

    private static string FormatValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unknown;
        }

        var trimmedValue = value.Trim();
        var redactedValue = RedactPrivacySensitiveValues(trimmedValue);
        return string.IsNullOrWhiteSpace(redactedValue) ? Unknown : redactedValue;
    }

    private static string RedactPrivacySensitiveValues(string value)
    {
        var redactedValue = EmailAddressRegex.Replace(value, "[Redacted email]");
        redactedValue = WindowsPathRegex.Replace(redactedValue, "[Redacted path]");
        redactedValue = UserHomePathRegex.Replace(redactedValue, "[Redacted path]");
        redactedValue = UncPathRegex.Replace(redactedValue, "[Redacted path]");
        redactedValue = IpAddressRegex.Replace(redactedValue, "[Redacted IP address]");
        redactedValue = MacAddressRegex.Replace(redactedValue, "[Redacted MAC address]");
        redactedValue = WindowsProductKeyRegex.Replace(redactedValue, "[Redacted Windows product key]");
        redactedValue = DomainUserRegex.Replace(redactedValue, "[Redacted domain user]");
        return CommonWindowsHostNameRegex.Replace(redactedValue, "[Redacted hostname]");
    }

    private sealed record DiagnosticProfileSupport(
        bool GamingOptimisedVerified,
        bool HardwareWritesAllowed);
}
