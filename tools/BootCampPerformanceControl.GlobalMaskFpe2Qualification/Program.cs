using System.ComponentModel;
using BootCampPerformanceControl.FanControl.Smc.CrystalIdea;
using BootCampPerformanceControl.GlobalMaskFpe2Qualification;
using BootCampPerformanceControl.HardwareDetection;

var options = QualificationOptions.Parse(args);
var utcNow = DateTimeOffset.UtcNow;
using var output = QualificationTextOutput.Create(utcNow);
output.WriteLine(
    $"Qualification log: {Path.GetFileName(output.LogPath)} (Desktop by default)");
GlobalMaskQualificationOrchestrator.PrintBanner(output, options);
GlobalMaskQualificationOrchestrator? orchestrator = null;

try
{
    var hardwareDetection = new HardwareDetectionService(new ModelSupportRegistry());
    var hardware = await hardwareDetection.DetectAsync(CancellationToken.None);
    var verification = hardwareDetection.VerifyModel(hardware);
    var windowsVersion = hardware.OperatingSystem is { } operatingSystem
        ? $"{operatingSystem.Caption}; version {operatingSystem.Version}; build {operatingSystem.BuildNumber}; {operatingSystem.OSArchitecture}"
        : Environment.OSVersion.VersionString;
    var machine = new QualificationMachine(
        verification.Manufacturer,
        verification.Model,
        verification.IsSupportedIntelMac,
        windowsVersion,
        utcNow,
        utcNow.ToLocalTime());
    var conflicts = options.IsExecuteArmed
        ? QualificationProcessConflictDetector.Find()
        : Array.Empty<QualificationProcessConflict>();

    if (!machine.IsAppleIntel)
    {
        output.WriteLine("READ ONLY / DRY RUN - qualification requires Apple + Intel.");
        output.WriteLine("SMC writes issued: 0");
        output.WriteLine("Total SMC write attempts: 0");
        output.WriteLine("Keys written:");
        output.WriteLine("(none)");
        return 2;
    }

    if (options.IsExecuteArmed && conflicts.Count > 0)
    {
        foreach (var conflict in conflicts)
        {
            output.WriteLine(
                $"BLOCKING PROCESS: {conflict.ProcessName} (PID {conflict.ProcessId})");
        }

        output.WriteLine("REFUSED: close every blocking process; the tool will not terminate it.");
        output.WriteLine("SMC writes issued: 0");
        output.WriteLine("Total SMC write attempts: 0");
        output.WriteLine("Keys written:");
        output.WriteLine("(none)");
        return 3;
    }

    await using var session = PhysicalQualificationSession.OpenAlreadyRunning();
    orchestrator = new GlobalMaskQualificationOrchestrator(
        session,
        output,
        printBanner: false);
    using var cancellationSource = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellationSource.Cancel();
    };

    var result = await orchestrator.RunAsync(
        options,
        machine,
        conflicts,
        cancellationSource.Token);
    return result.Outcome switch
    {
        QualificationOutcome.DryRun => 0,
        QualificationOutcome.Pass => 0,
        QualificationOutcome.Refused => 3,
        _ => 4
    };
}
catch (AppleSmcServiceStateException exception)
{
    output.WriteLine(
        $"REFUSED: AppleSMC is {exception.State}; it must already be Running. The tool did not start or modify the service.");
}
catch (Win32Exception exception)
{
    output.WriteLine($"REFUSED: Windows AppleSMC device access failed: {exception.Message}");
}
catch (Exception exception)
{
    output.WriteLine($"QUALIFICATION SETUP FAILED: {exception.GetType().Name}: {exception.Message}");
}

if (orchestrator is null)
{
    output.WriteLine("SMC writes issued: 0");
    output.WriteLine("Total SMC write attempts: 0");
    output.WriteLine("Keys written:");
    output.WriteLine("(none)");
}
else
{
    orchestrator.Writes.PrintSummary();
}

return 5;
