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

    [Fact]
    public void DotSize_LegacyMultiplier_IsConverted()
    {
        Assert.Equal(0.11f, ConfigRules.NormalizeDotSize(1.0f), 5);
    }

    [Fact]
    public void DotSize_CurrentValue_IsKept()
    {
        Assert.Equal(0.2f, ConfigRules.NormalizeDotSize(0.2f));
    }
}
