using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class CentreSourceTests
{
    private static readonly string[] Allowed = { "columns", "rows", "list" };

    [Fact]
    public void UnknownValue_UsesFallbackAndFlagsIt()
    {
        string resolved = CentreSource.Resolve("nonsense", Allowed, "columns", out bool unknown);

        Assert.Equal("columns", resolved);
        Assert.True(unknown);
    }

    [Fact]
    public void MissingValue_UsesFallbackAndFlagsIt()
    {
        string resolved = CentreSource.Resolve(null, Allowed, "columns", out bool unknown);

        Assert.Equal("columns", resolved);
        Assert.True(unknown);
    }

    [Fact]
    public void KnownValue_IsKept()
    {
        string resolved = CentreSource.Resolve("columns", Allowed, "rows", out bool unknown);

        Assert.Equal("columns", resolved);
        Assert.False(unknown);
    }
}
