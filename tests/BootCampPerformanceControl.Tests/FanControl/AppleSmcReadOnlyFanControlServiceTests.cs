using BootCampPerformanceControl.FanControl;
using BootCampPerformanceControl.FanControl.Smc;
using BootCampPerformanceControl.HardwareDetection;

namespace BootCampPerformanceControl.Tests.FanControl;

public sealed class AppleSmcReadOnlyFanControlServiceTests
{
    [Fact]
    public async Task ReadStatusAsync_UnsupportedModel_DoesNotOpenAppleSmcSession()
    {
        var sessionOpenCount = 0;
        var service = new AppleSmcReadOnlyFanControlService(
            new FanSafetyPolicy(),
            _ =>
            {
                sessionOpenCount++;
                throw new InvalidOperationException("Session must not be opened.");
            });

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro14_3,
            CancellationToken.None);

        Assert.False(status.IsAvailable);
        Assert.Contains("read-only unavailable", status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("not verified", status.DisplayText, StringComparison.Ordinal);
        Assert.Equal(0, sessionOpenCount);
    }

    [Fact]
    public async Task ReadStatusAsync_PreCanceled_PropagatesWithoutOpeningAppleSmcSession()
    {
        var sessionOpenCount = 0;
        var service = new AppleSmcReadOnlyFanControlService(
            new FanSafetyPolicy(),
            _ =>
            {
                sessionOpenCount++;
                throw new InvalidOperationException("Session must not be opened.");
            });
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ReadStatusAsync(
                VerifiedHardwareModels.MacBookPro16_1,
                cancellationSource.Token));

        Assert.Equal(0, sessionOpenCount);
    }

    [Fact]
    public async Task ReadStatusAsync_VerifiedModel_UsesReadPipelineAndDisposesSession()
    {
        var transport = new FakeSmcTransport();
        var sessionOpenCount = 0;
        var service = new AppleSmcReadOnlyFanControlService(
            new FanSafetyPolicy(),
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                sessionOpenCount++;
                return Task.FromResult<ISmcTransport>(transport);
            });

        var status = await service.ReadStatusAsync(
            VerifiedHardwareModels.MacBookPro16_1,
            CancellationToken.None);

        Assert.True(status.IsAvailable);
        Assert.Contains("read-only verified", status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("Fan 0: 1840 / 5616 RPM", status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("Fan 1: 1691 / 5200 RPM", status.DisplayText, StringComparison.Ordinal);
        Assert.Contains("Write control is not enabled", status.DisplayText, StringComparison.Ordinal);
        Assert.Equal(1, sessionOpenCount);
        Assert.Equal(1, transport.ProtocolCalls);
        Assert.Equal(9, transport.KeyInfoCalls);
        Assert.Equal(9, transport.ReadCalls);
        Assert.True(transport.IsDisposed);
    }

    private sealed class FakeSmcTransport : ISmcTransport
    {
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public FakeSmcTransport()
        {
            SetUInt8("FNum", 2, 0x80);
            SetFloat32("F0Mx", 5616f, 0x85);
            SetFloat32("F1Mx", 5200f, 0x85);
            SetFloat32("F0Ac", 1840f, 0x84);
            SetFloat32("F1Ac", 1691f, 0x84);
            SetUInt8("F0Md", 0, 0xD0);
            SetUInt8("F1Md", 0, 0xD0);
            SetFloat32("F0Tg", 1900f, 0xD4);
            SetFloat32("F1Tg", 1760f, 0xD4);
        }

        public int ProtocolCalls { get; private set; }

        public int KeyInfoCalls { get; private set; }

        public int ReadCalls { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task<SmcTransportProtocol> GetProtocolAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProtocolCalls++;
            return Task.FromResult(SmcTransportProtocol.Mmio);
        }

        public Task<SmcKeyInfo> GetKeyInfoAsync(
            string key,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KeyInfoCalls++;
            var entry = _entries[key];
            return Task.FromResult(new SmcKeyInfo(
                key,
                checked((byte)entry.Data.Length),
                entry.Type,
                entry.Attributes));
        }

        public Task<ReadOnlyMemory<byte>> ReadKeyAsync(
            string key,
            byte length,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            var entry = _entries[key];
            Assert.Equal(entry.Data.Length, length);
            return Task.FromResult<ReadOnlyMemory<byte>>(entry.Data);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        private void SetUInt8(string key, byte value, byte attributes)
        {
            _entries[key] = new Entry("ui8 ", attributes, [value]);
        }

        private void SetFloat32(string key, float value, byte attributes)
        {
            _entries[key] = new Entry("flt ", attributes, BitConverter.GetBytes(value));
        }

        private sealed record Entry(
            string Type,
            byte Attributes,
            byte[] Data);
    }
}
