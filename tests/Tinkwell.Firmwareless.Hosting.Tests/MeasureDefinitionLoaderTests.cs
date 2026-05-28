using Microsoft.Extensions.Logging;
using Tinkwell.Firmwareless.Hosting.WasmHost.Measures;
using Xunit;

namespace Tinkwell.Firmwareless.Hosting.Tests;

public class MeasureDefinitionLoaderTests
{
    private static MeasureDefinitionLoader CreateLoader() =>
        new(NoOpLogger.Instance);

    private sealed class NoOpLogger : ILogger
    {
        public static readonly ILogger Instance = new NoOpLogger();

        private NoOpLogger() { }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    [Fact]
    public void Parse_measures_tw_with_one_measure()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                measure "temperature" {
                    quantity_type="Temperature";
                    unit="DegreesCelsius";
                }
                """);

            var loader = CreateLoader();
            loader.LoadFromFile(path);

            var m = loader.Find("temperature");
            Assert.NotNull(m);
            Assert.Equal("Temperature", m.QuantityType);
            Assert.Equal("DegreesCelsius", m.Unit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_multiple_measures()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                measure "temperature" {
                    quantity_type="Temperature";
                    unit="DegreesCelsius";
                    description="Ambient temperature";
                    category="environment";
                    minimum="-40";
                    maximum="85";
                    precision="2";
                    ttl="300";
                }

                measure "humidity" {
                    quantity_type="RelativeHumidity";
                    unit="Percent";
                }
                """);

            var loader = CreateLoader();
            loader.LoadFromFile(path);

            Assert.Equal(2, loader.ListAll().Count);
            var t = loader.Find("temperature");
            Assert.NotNull(t);
            Assert.Equal("RelativeHumidity", loader.Find("humidity")!.QuantityType);
            Assert.Equal("Percent", loader.Find("humidity")!.Unit);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_empty_file_yields_no_definitions()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "");

            var loader = CreateLoader();
            loader.LoadFromFile(path);

            Assert.Empty(loader.ListAll());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_missing_path_does_not_throw()
    {
        var loader = CreateLoader();
        var ex = Record.Exception(() => loader.LoadFromFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_no_measures.tw")));
        Assert.Null(ex);
        Assert.Empty(loader.ListAll());
    }

    [Fact]
    public void Parse_measure_with_all_properties()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                measure "temperature" {
                    quantity_type="Temperature";
                    unit="DegreesCelsius";
                    description="Ambient temperature";
                    category="environment";
                    minimum="-40";
                    maximum="85";
                    precision="2";
                    ttl="300";
                }
                """);

            var loader = CreateLoader();
            loader.LoadFromFile(path);

            var m = loader.Find("temperature");
            Assert.NotNull(m);
            Assert.Equal("Temperature", m.QuantityType);
            Assert.Equal("DegreesCelsius", m.Unit);
            Assert.Equal("Ambient temperature", m.Description);
            Assert.Equal("environment", m.Category);
            Assert.Equal(-40, m.Minimum);
            Assert.Equal(85, m.Maximum);
            Assert.Equal(2, m.Precision);
            Assert.Equal(300, m.TtlSeconds);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
