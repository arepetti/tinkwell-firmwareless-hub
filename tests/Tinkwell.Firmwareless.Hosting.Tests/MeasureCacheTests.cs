using Tinkwell.Firmwareless.Hosting.WasmHost.Measures;
using Xunit;

namespace Tinkwell.Firmwareless.Hosting.Tests;

public class MeasureCacheTests
{
    [Fact]
    public void Set_and_Get_round_trip()
    {
        var cache = new MeasureCache();
        cache.Set("temp", 23.5, "C", 1_700_000_000_000);

        var got = cache.Get("temp");
        Assert.NotNull(got);
        Assert.Equal("temp", got.Name);
        Assert.Equal(23.5, got.Value);
        Assert.Equal("C", got.Unit);
        Assert.Equal(1_700_000_000_000ul, got.TimestampMs);
    }

    [Fact]
    public void Get_returns_null_for_unknown_name()
    {
        var cache = new MeasureCache();
        Assert.Null(cache.Get("missing"));
    }

    [Fact]
    public void GetAll_returns_all_cached_values()
    {
        var cache = new MeasureCache();
        cache.Set("a", 1, "u1", 100);
        cache.Set("b", 2, "u2", 200);

        var all = cache.GetAll();
        Assert.Equal(2, all.Count);
        var byName = all.OrderBy(m => m.Name).ToList();
        Assert.Equal("a", byName[0].Name);
        Assert.Equal(1, byName[0].Value);
        Assert.Equal("b", byName[1].Name);
        Assert.Equal(2, byName[1].Value);
    }

    [Fact]
    public void Set_overwrites_previous_value()
    {
        var cache = new MeasureCache();
        cache.Set("x", 1, "m", 10);
        cache.Set("x", 99, "m", 20);

        var got = cache.Get("x");
        Assert.NotNull(got);
        Assert.Equal(99, got.Value);
        Assert.Equal(20ul, got.TimestampMs);
    }

    [Fact]
    public void Clear_empties_the_cache()
    {
        var cache = new MeasureCache();
        cache.Set("k", 1, "u", 1);
        cache.Clear();

        Assert.Null(cache.Get("k"));
        Assert.Empty(cache.GetAll());
    }
}
