using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BootCampPerformanceControl.Diagnostics;

internal static class DiagnosticPrivacySanitizer
{
    internal const string Unknown = "Unknown";

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
    private static readonly Regex LabeledHostNameRegex = new(
        @"\b(?:computer\s+name|host\s*name|hostname)\s*[:=]\s*[A-Z0-9_.-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LabeledSerialNumberRegex = new(
        @"\b(?:serial(?:\s+number)?|service\s+tag)\s*[:=]\s*[A-Z0-9-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EnvironmentVariableRegex = new(
        @"\b(?:USERNAME|USERPROFILE|COMPUTERNAME|USERDOMAIN|HOMEPATH|APPDATA|LOCALAPPDATA|PATH)=[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string FormatValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Unknown;
        }

        var trimmedValue = value.Trim();
        var redactedValue = RedactPrivacySensitiveValues(trimmedValue);
        return string.IsNullOrWhiteSpace(redactedValue) ? Unknown : redactedValue;
    }

    internal static string RedactPrivacySensitiveValues(string value)
    {
        var redactedValue = EmailAddressRegex.Replace(value, "[Redacted email]");
        redactedValue = EnvironmentVariableRegex.Replace(redactedValue, "[Redacted environment variable]");
        redactedValue = WindowsPathRegex.Replace(redactedValue, "[Redacted path]");
        redactedValue = UserHomePathRegex.Replace(redactedValue, "[Redacted path]");
        redactedValue = UncPathRegex.Replace(redactedValue, "[Redacted path]");
        redactedValue = IpAddressRegex.Replace(redactedValue, "[Redacted IP address]");
        redactedValue = MacAddressRegex.Replace(redactedValue, "[Redacted MAC address]");
        redactedValue = WindowsProductKeyRegex.Replace(redactedValue, "[Redacted Windows product key]");
        redactedValue = DomainUserRegex.Replace(redactedValue, "[Redacted domain user]");
        redactedValue = LabeledSerialNumberRegex.Replace(redactedValue, "[Redacted serial number]");
        redactedValue = LabeledHostNameRegex.Replace(redactedValue, "[Redacted hostname]");
        return CommonWindowsHostNameRegex.Replace(redactedValue, "[Redacted hostname]");
    }

    internal static string CreateSafeFileNameSegment(string? value)
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
}
