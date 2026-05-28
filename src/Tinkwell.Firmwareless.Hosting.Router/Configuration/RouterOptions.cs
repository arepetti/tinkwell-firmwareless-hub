namespace Tinkwell.Firmwareless.Hosting.Router.Configuration;

public sealed class RouterOptions
{
    public int TcpPort { get; set; } = 9500;
    public string SupervisorPipeName { get; set; } = "";
    public int SystemTimeoutSeconds { get; set; } = 120;
    public int HeartbeatIntervalSeconds { get; set; } = 5;
}
