using System.Text.Json;
using System.Text.Json.Serialization;
using BootCampPerformanceControl.Logging;
using BootCampPerformanceControl.PowerManagement;
using BootCampPerformanceControl.SettingsBackup;

namespace BootCampPerformanceControl.PowerSmokeTest;

internal static class Program
{
    private static readonly Guid RequiredApplySchemeId = new(
        "381b4222-f694-41f0-9685-ff5bb260df2e");

    private static readonly ProcessorPowerSettings RequiredApplyState = new(
        ProcessorMaximumAc: 95,
        ProcessorMaximumDc: 95,
        BoostModeAc: 2,
        BoostModeDc: 2);

    private static readonly ProcessorPowerSettings ApplyGamingSettings = new(
        ProcessorMaximumAc: 95,
        ProcessorMaximumDc: 95,
        BoostModeAc: 0,
        BoostModeDc: 0);

    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1
            || (args[0] != "apply-noop" && args[0] != "apply-gaming" && args[0] != "restore"))
        {
            PrintUsage();
            return 2;
        }

        var mode = args[0];
        var logger = new FileApplicationLogger();
        logger.Info($"Manual production power smoke test requested. Mode={mode}.");

        try
        {
            return mode switch
            {
                "apply-noop" => await RunApplyAsync(
                    mode,
                    RequiredApplyState,
                    logger).ConfigureAwait(false),
                "apply-gaming" => await RunApplyAsync(
                    mode,
                    ApplyGamingSettings,
                    logger).ConfigureAwait(false),
                _ => await RunRestoreAsync(logger).ConfigureAwait(false)
            };
        }
        catch (Exception exception)
        {
            logger.Error($"Manual production power smoke test failed unexpectedly. Mode={mode}.", exception);
            Console.Error.WriteLine("Smoke test failed unexpectedly. Check the application log.");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<int> RunApplyAsync(
        string mode,
        ProcessorPowerSettings requestedSettings,
        IApplicationLogger logger)
    {
        var snapshotFilePath = GetRestoreSnapshotFilePath();
        var restoreSnapshotStore = new JsonRestoreSnapshotStore(logger);
        var powerManagementService = new WindowsPowerManagementService(
            restoreSnapshotStore,
            logger);

        var currentState = await powerManagementService.ReadCurrentStateAsync(
            CancellationToken.None).ConfigureAwait(false);
        PrintState("Current Windows power state", currentState);

        if (!MatchesRequiredApplyState(currentState))
        {
            Console.Error.WriteLine(
                $"REFUSED: current Windows power state does not exactly match the required {mode} guard state.");
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
            requestedSettings,
            currentState,
            CancellationToken.None).ConfigureAwait(false);

        PrintOperationResult(result);
        Console.WriteLine(
            $"restore-snapshot.json exists after Apply: {File.Exists(snapshotFilePath)}");
        Console.WriteLine(
            $"{mode} complete. Restore was not invoked and the backup was not deleted.");

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

    private static bool MatchesRequiredApplyState(PowerStateSnapshot state)
    {
        return state.SchemeId == RequiredApplySchemeId
            && state.ProcessorMaximumAc == RequiredApplyState.ProcessorMaximumAc
            && state.ProcessorMaximumDc == RequiredApplyState.ProcessorMaximumDc
            && state.BoostModeAc == RequiredApplyState.BoostModeAc
            && state.BoostModeDc == RequiredApplyState.BoostModeDc;
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
        Console.Error.WriteLine("  BootCampPerformanceControl.PowerSmokeTest apply-gaming");
        Console.Error.WriteLine("  BootCampPerformanceControl.PowerSmokeTest restore");
    }
}
