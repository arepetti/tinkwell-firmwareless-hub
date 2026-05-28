using Google.Protobuf;
using Tinkwell.Firmwareless.Hosting.Router.Ipc;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Xunit;

namespace Tinkwell.Firmwareless.Hosting.Tests;

public class IpcProtocolTests
{
    [Fact]
    public void Frame_produces_correct_header()
    {
        var envelope = new IpcEnvelope
        {
            Ready = new Ready()
        };

        var frame = IpcProtocol.Frame(envelope);

        Assert.True(frame.Length > 4);
        var payloadLength = BitConverter.ToUInt32(frame, 0);
        Assert.Equal((uint)(frame.Length - 4), payloadLength);
    }

    [Fact]
    public async Task WriteAsync_ReadAsync_roundtrip()
    {
        var envelope = new IpcEnvelope
        {
            RegisterClient = new RegisterClient { HostId = "test-host-42" }
        };

        using var stream = new MemoryStream();
        await IpcProtocol.WriteAsync(stream, envelope);
        stream.Position = 0;

        var read = await IpcProtocol.ReadAsync(stream);

        Assert.NotNull(read);
        Assert.Equal(IpcEnvelope.PayloadOneofCase.RegisterClient, read.PayloadCase);
        Assert.Equal("test-host-42", read.RegisterClient.HostId);
    }

    [Fact]
    public async Task ReadAsync_returns_null_on_empty_stream()
    {
        using var stream = new MemoryStream();
        var result = await IpcProtocol.ReadAsync(stream);
        Assert.Null(result);
    }

    [Fact]
    public async Task Multiple_messages_roundtrip()
    {
        using var stream = new MemoryStream();

        var msg1 = new IpcEnvelope { RegisterClient = new RegisterClient { HostId = "h1" } };
        var msg2 = new IpcEnvelope { Ready = new Ready() };
        var msg3 = new IpcEnvelope { RegisterClient = new RegisterClient { HostId = "h3" } };

        await IpcProtocol.WriteAsync(stream, msg1);
        await IpcProtocol.WriteAsync(stream, msg2);
        await IpcProtocol.WriteAsync(stream, msg3);

        stream.Position = 0;

        var r1 = await IpcProtocol.ReadAsync(stream);
        var r2 = await IpcProtocol.ReadAsync(stream);
        var r3 = await IpcProtocol.ReadAsync(stream);
        var r4 = await IpcProtocol.ReadAsync(stream);

        Assert.NotNull(r1);
        Assert.Equal("h1", r1.RegisterClient.HostId);
        Assert.NotNull(r2);
        Assert.Equal(IpcEnvelope.PayloadOneofCase.Ready, r2.PayloadCase);
        Assert.NotNull(r3);
        Assert.Equal("h3", r3.RegisterClient.HostId);
        Assert.Null(r4);
    }

    [Fact]
    public void Frame_size_matches_payload()
    {
        var envelope = new IpcEnvelope
        {
            Shutdown = new Shutdown { Reason = "test reason with some text" }
        };

        var frame = IpcProtocol.Frame(envelope);
        var payload = envelope.ToByteArray();

        Assert.Equal(4 + payload.Length, frame.Length);

        var headerLen = BitConverter.ToUInt32(frame, 0);
        Assert.Equal((uint)payload.Length, headerLen);
    }
}
