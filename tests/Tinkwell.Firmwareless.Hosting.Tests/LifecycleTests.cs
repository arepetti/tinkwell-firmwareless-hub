using Google.Protobuf;
using Tinkwell.Firmwareless.Hosting.Ipc.Proto;
using Xunit;

namespace Tinkwell.Firmwareless.Hosting.Tests;

public class LifecycleTests
{
    [Fact]
    public void FirmletState_enum_values()
    {
        Assert.Equal(0, (int)FirmletState.Loading);
        Assert.Equal(1, (int)FirmletState.Initialized);
        Assert.Equal(2, (int)FirmletState.Running);
        Assert.Equal(3, (int)FirmletState.Suspended);
        Assert.Equal(4, (int)FirmletState.Stopped);
    }

    [Fact]
    public void LifecycleEvent_enum_values()
    {
        Assert.Equal(0, (int)LifecycleEvent.Initialize);
        Assert.Equal(1, (int)LifecycleEvent.Start);
        Assert.Equal(2, (int)LifecycleEvent.Stop);
    }

    [Fact]
    public void LifecycleReason_enum_values()
    {
        Assert.Equal(0, (int)LifecycleReason.Start);
        Assert.Equal(1, (int)LifecycleReason.FirstTimeSetup);
        Assert.Equal(2, (int)LifecycleReason.Restart);
        Assert.Equal(3, (int)LifecycleReason.Resume);
        Assert.Equal(4, (int)LifecycleReason.Suspend);
        Assert.Equal(5, (int)LifecycleReason.Uninstalling);
        Assert.Equal(6, (int)LifecycleReason.Quit);
        Assert.Equal(7, (int)LifecycleReason.Terminating);
    }

    [Fact]
    public void FirmletStateChanged_round_trips_through_serialization()
    {
        var original = new FirmletStateChanged
        {
            State = FirmletState.Running,
            LastEvent = LifecycleEvent.Start,
            LastReason = LifecycleReason.Resume,
        };

        var bytes = original.ToByteArray();
        var parsed = FirmletStateChanged.Parser.ParseFrom(bytes);

        Assert.Equal(original.State, parsed.State);
        Assert.Equal(original.LastEvent, parsed.LastEvent);
        Assert.Equal(original.LastReason, parsed.LastReason);
    }

    [Fact]
    public void SuspendFirmlet_and_ResumeFirmlet_round_trip()
    {
        var suspend = new SuspendFirmlet { Reason = "low power" };
        var s2 = SuspendFirmlet.Parser.ParseFrom(suspend.ToByteArray());
        Assert.Equal("low power", s2.Reason);

        var resume = new ResumeFirmlet();
        var r2 = ResumeFirmlet.Parser.ParseFrom(resume.ToByteArray());
        Assert.NotNull(r2);
    }

    [Fact]
    public void IpcEnvelope_carries_SuspendFirmlet_ResumeFirmlet_and_FirmletStateChanged()
    {
        var envSuspend = new IpcEnvelope { SuspendFirmlet = new SuspendFirmlet { Reason = "test" } };
        var es = IpcEnvelope.Parser.ParseFrom(envSuspend.ToByteArray());
        Assert.Equal(IpcEnvelope.PayloadOneofCase.SuspendFirmlet, es.PayloadCase);
        Assert.Equal("test", es.SuspendFirmlet.Reason);

        var envResume = new IpcEnvelope { ResumeFirmlet = new ResumeFirmlet() };
        var er = IpcEnvelope.Parser.ParseFrom(envResume.ToByteArray());
        Assert.Equal(IpcEnvelope.PayloadOneofCase.ResumeFirmlet, er.PayloadCase);

        var envState = new IpcEnvelope
        {
            FirmletStateChanged = new FirmletStateChanged
            {
                State = FirmletState.Initialized,
                LastEvent = LifecycleEvent.Initialize,
                LastReason = LifecycleReason.FirstTimeSetup,
            },
        };
        var est = IpcEnvelope.Parser.ParseFrom(envState.ToByteArray());
        Assert.Equal(IpcEnvelope.PayloadOneofCase.FirmletStateChanged, est.PayloadCase);
        Assert.Equal(FirmletState.Initialized, est.FirmletStateChanged.State);
    }

    [Fact]
    public void HealthSnapshot_with_HostHealthReport_round_trips()
    {
        var report = new HostHealthReport
        {
            AssetId = "asset-1",
            FirmletName = "demo",
            FirmletState = FirmletState.Running,
            CpuPercent = 12.5,
            WorkingSetBytes = 99_000_000,
            ThreadCount = 8,
            Status = "ok",
            UptimeMs = 60_000,
            RestartCount = 0,
        };
        report.Services.Add(new ServiceHealthInfo
        {
            FullName = "svc.A",
            State = ServiceState.Ready,
        });

        var original = new HealthSnapshot
        {
            TimestampMs = 123456789,
            Router = new HostHealthReport { AssetId = "router", FirmletName = "router" },
        };
        original.Hosts.Add(report);

        var bytes = original.ToByteArray();
        var parsed = HealthSnapshot.Parser.ParseFrom(bytes);

        Assert.Equal(123456789ul, parsed.TimestampMs);
        Assert.NotNull(parsed.Router);
        Assert.Equal("router", parsed.Router.AssetId);
        Assert.Single(parsed.Hosts);
        var h = parsed.Hosts[0];
        Assert.Equal("asset-1", h.AssetId);
        Assert.Equal("demo", h.FirmletName);
        Assert.Equal(FirmletState.Running, h.FirmletState);
        Assert.Equal(12.5, h.CpuPercent);
        Assert.Equal(99_000_000ul, h.WorkingSetBytes);
        Assert.Equal(8, h.ThreadCount);
        Assert.Equal("ok", h.Status);
        Assert.Equal(60_000ul, h.UptimeMs);
        Assert.Equal(0, h.RestartCount);
        Assert.Single(h.Services);
        Assert.Equal("svc.A", h.Services[0].FullName);
        Assert.Equal(ServiceState.Ready, h.Services[0].State);
    }
}
