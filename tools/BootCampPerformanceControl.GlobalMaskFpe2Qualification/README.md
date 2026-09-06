# GlobalMaskFpe2 physical qualification fixture

This developer-only console tool performs the first controlled one-fan `GlobalMaskFpe2` write/readback/Apple Auto qualification. It is not connected to the BCPC UI, profiles, startup recovery, installer, or release package.

## Dry run (default)

```powershell
dotnet run --project tools/BootCampPerformanceControl.GlobalMaskFpe2Qualification/BootCampPerformanceControl.GlobalMaskFpe2Qualification.csproj -c Release -r win-x64
```

Dry run reads and reports the live fingerprint but never calls the write backend. It finishes with a zero write-attempt count.

## Physical execute syntax

Do not execute this during development or automated testing. A reviewed physical qualification requires both exact gates:

```powershell
dotnet run --project tools/BootCampPerformanceControl.GlobalMaskFpe2Qualification/BootCampPerformanceControl.GlobalMaskFpe2Qualification.csproj -c Release -r win-x64 -- --execute --confirm GLOBALMASK-FPE2-PHYSICAL-WRITE
```

Execute mode refuses unless the detected system is exactly Apple Inc. / Intel / `MacBookPro12,1`, AppleSMC is already Running, no BCPC or Macs Fan Control process is running, and the complete one-fan MMIO `GlobalMaskFpe2` fingerprint matches the reviewed physical evidence including `F0Mx raw=60DC` and initial `FS! raw=0000`.

The only permitted attempted write sequence is:

```text
FS!  0001
F0Tg <fresh exact two-byte F0Mx payload>
FS!  0000 (non-cancellable finally path)
```

The last Auto write is attempted even after cancellation or any failure after the first write boundary, and exact `FS! raw=0000` readback is required. Logs are created without overwrite on the Desktop as `BCPC-GLOBALMASK-FPE2-QUALIFICATION-<UTC timestamp>.txt`.

Safety-critical writes are independent of console/file output success. Reporting exceptions are retained in memory and force a FAIL/INCONCLUSIVE result, while the non-cancellable `FS! 0000` backend call and exact readback verification still proceed.
