using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class ConfigRulesTests
{
    [Fact]
    public void SchemaScale_AboveTwo_FallsBackToOne()
    {
        Assert.Equal(1f, ConfigRules.NormalizeSchemaScale(3.5f));
    }

    [Fact]
    public void SchemaScale_InRange_IsKept()
    {
        Assert.Equal(1.5f, ConfigRules.NormalizeSchemaScale(1.5f));
    }

    // Migration to the 0.373 default: every legacy value (old 0.11 fraction and the
    // older absolute multiplier) must come out as the new default, not stay small.
    [Theory]
    [InlineData(0.11f)]
    [InlineData(0.05f)]
    [InlineData(1.0f)]
    [InlineData(0.5f)]
    public void DotSize_LegacyValue_BecomesDefault(float legacy)
    {
        Assert.Equal(0.373f, ConfigRules.NormalizeDotSize(legacy), 5);
    }

    [Fact]
    public void DotSize_CurrentValue_IsKept()
    {
        Assert.Equal(0.373f, ConfigRules.NormalizeDotSize(0.373f), 5);
    }

    [Theory]
    [InlineData(0.11f)]
    [InlineData(0.174f)]
    public void LockedDotSize_LegacyValue_BecomesLockedDefault(float raw)
    {
        Assert.Equal(0.174f, ConfigRules.NormalizeLockedDotSize(raw), 5);
    }
}
