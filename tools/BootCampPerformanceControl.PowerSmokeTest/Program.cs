using System.Text.Json;
using System.Text.Json.Serialization;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.PowerSmokeTest;

internal static class Program
{
    private static readonly Guid RequiredApplyNoopSchemeId = new(
        "381b4222-f694-41f0-9685-ff5bb260df2e");

    private static readonly ProcessorPowerSettings RequiredApplyNoopSettings = new(
        ProcessorMaximumAc: 95,
        ProcessorMaximumDc: 95,
        BoostModeAc: 2,
        BoostModeDc: 2);

    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || (args[0] != "apply-noop" && args[0] != "restore"))
        {
            PrintUsage();
            return 2;
        }

        var mode = args[0];
        var logger = new FileApplicationLogger();
        logger.Info($"Manual production power smoke test requested. Mode={mode}.");

        try
        {
            return mode == "apply-noop"
                ? await RunApplyNoopAsync(logger).ConfigureAwait(false)
                : await RunRestoreAsync(logger).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.Error($"Manual production power smoke test failed unexpectedly. Mode={mode}.", exception);
            Console.Error.WriteLine("Smoke test failed unexpectedly. Check the application log.");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<int> RunApplyNoopAsync(IApplicationLogger logger)
    {
        var snapshotFilePath = GetRestoreSnapshotFilePath();
        var restoreSnapshotStore = new JsonRestoreSnapshotStore(logger);
        var powerManagementService = new WindowsPowerManagementService(
            restoreSnapshotStore,
            logger);

        var currentState = await powerManagementService.ReadCurrentStateAsync(
            CancellationToken.None).ConfigureAwait(false);
        PrintState("Current Windows power state", currentState);

        if (!MatchesRequiredApplyNoopState(currentState))
        {
            Console.Error.WriteLine(
                "REFUSED: current Windows power state does not exactly match the required apply-noop guard state.");
            Console.Error.WriteLine("No Windows power write was attempted.");
            return 1;
        }

        if (File.Exists(snapshotFilePath) || restoreSnapshotStore.HasOriginalRestoreSnapshot)
        {
            Console.Error.WriteLine(
                $"REFUSED: an original restore snapshot already exists at {snapshotFilePath} or is cached in memory.");
            Console.Error.WriteLine("No Windows power write was attempted.");
            return 1;
        }

        // Recheck immediately before Apply to narrow the guard-to-write race window.
        if (File.Exists(snapshotFilePath))
        {
            Console.Error.WriteLine(
                $"REFUSED: restore-snapshot.json appeared before Apply at {snapshotFilePath}.");
            Console.Error.WriteLine("No Windows power write was attempted.");
            return 1;
        }

        var result = await powerManagementService.ApplyProcessorSettingsAsync(
            RequiredApplyNoopSettings,
            currentState,
            CancellationToken.None).ConfigureAwait(false);

        PrintOperationResult(result);
        Console.WriteLine(
            $"restore-snapshot.json exists after Apply: {File.Exists(snapshotFilePath)}");
        Console.WriteLine("apply-noop complete. Restore was not invoked and the backup was not deleted.");

        return result.IsSuccessful ? 0 : 1;
    }

    private static async Task<int> RunRestoreAsync(IApplicationLogger logger)
    {
        var snapshotFilePath = GetRestoreSnapshotFilePath();
        var restoreSnapshotStore = new JsonRestoreSnapshotStore(logger);

        if (!File.Exists(snapshotFilePath))
        {
            Console.Error.WriteLine($"REFUSED: restore snapshot does not exist at {snapshotFilePath}.");
            Console.Error.WriteLine("No Windows power write was attempted.");
            return 1;
        }

        var originalSnapshot = await restoreSnapshotStore.GetOriginalRestoreSnapshotAsync(
            CancellationToken.None).ConfigureAwait(false);
        if (originalSnapshot is null)
        {
            Console.Error.WriteLine(
                "REFUSED: restore-snapshot.json exists but is not a valid, loadable original snapshot.");
            Console.Error.WriteLine("No Windows power write was attempted.");
            return 1;
        }

        PrintState("Original restore snapshot", originalSnapshot);

        var powerManagementService = new WindowsPowerManagementService(
            restoreSnapshotStore,
            logger);
        var result = await powerManagementService.RestoreOriginalSettingsAsync(
            CancellationToken.None).ConfigureAwait(false);

        PrintOperationResult(result);
        Console.WriteLine(
            $"restore-snapshot.json exists after Restore: {File.Exists(snapshotFilePath)}");

        return result.IsSuccessful ? 0 : 1;
    }

    private static bool MatchesRequiredApplyNoopState(PowerStateSnapshot state)
    {
        return state.SchemeId == RequiredApplyNoopSchemeId
            && state.ProcessorMaximumAc == RequiredApplyNoopSettings.ProcessorMaximumAc
            && state.ProcessorMaximumDc == RequiredApplyNoopSettings.ProcessorMaximumDc
            && state.BoostModeAc == RequiredApplyNoopSettings.BoostModeAc
            && state.BoostModeDc == RequiredApplyNoopSettings.BoostModeDc;
    }

    private static void PrintState(string heading, PowerStateSnapshot state)
    {
        Console.WriteLine(heading);
        Console.WriteLine($"  SchemeId: {state.SchemeId}");
        Console.WriteLine($"  ProcessorMaximumAc: {state.ProcessorMaximumAc}");
        Console.WriteLine($"  ProcessorMaximumDc: {state.ProcessorMaximumDc}");
        Console.WriteLine($"  BoostModeAc: {state.BoostModeAc}");
        Console.WriteLine($"  BoostModeDc: {state.BoostModeDc}");
        Console.WriteLine($"  CapturedAt: {state.CapturedAt:O}");
    }

    private static void PrintOperationResult(PowerOperationResult result)
    {
        Console.WriteLine("Structured operation result");
        Console.WriteLine(JsonSerializer.Serialize(result, ResultJsonOptions));
    }

    private static string GetRestoreSnapshotFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            localAppData,
            "BootCampPerformanceControl",
            "Backups",
            "restore-snapshot.json");
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Developer-only production power smoke test.");
        Console.Error.WriteLine("Run exactly one explicit mode:");
        Console.Error.WriteLine("  BootCampPerformanceControl.PowerSmokeTest apply-noop");
        Console.Error.WriteLine("  BootCampPerformanceControl.PowerSmokeTest restore");
    }
}
