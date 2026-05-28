using System.Diagnostics;

namespace Tinkwell.Firmwareless.Hosting.Supervisor.Monitoring;

public static class ResourceMetrics
{
    public static double GetCpuPercent(Process process, TimeSpan previousCpuTime, TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds <= 0)
            return 0;
        try
        {
            var cpuDelta = process.TotalProcessorTime - previousCpuTime;
            return Math.Clamp(
                cpuDelta.TotalMilliseconds / elapsed.TotalMilliseconds * 100 / Environment.ProcessorCount,
                0, 100);
        }
        catch
        {
            return 0;
        }
    }

    public static long GetWorkingSetBytes(Process process)
    {
        try { return process.WorkingSet64; }
        catch
        {
            return 0;
        }
    }

    public static int GetThreadCount(Process process)
    {
        try { return process.Threads.Count; }
        catch
        {
            return 0;
        }
    }
}
