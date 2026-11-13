using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class OfferTalkFlowTests
{
    [Fact]
    public void Begin_WhenIdle_IsTrue()
    {
        OfferTalkFlow flow = new();

        Assert.False(flow.Talking);
        Assert.False(flow.Finished);
        Assert.True(flow.Begin());
        Assert.True(flow.Talking);
        Assert.False(flow.Finished);
    }

    [Fact]
    public void Begin_Twice_SecondIsFalse()
    {
        OfferTalkFlow flow = new();

        Assert.True(flow.Begin());
        Assert.False(flow.Begin());
        Assert.True(flow.Talking);
    }

    [Fact]
    public void End_AfterBegin_FirstIsTrue_SecondIsFalse()
    {
        OfferTalkFlow flow = new();
        Assert.True(flow.Begin());

        // The game reports the end twice: onDialogueEnd and onDialogueCancelled.
        Assert.True(flow.End());
        Assert.False(flow.End());
        Assert.False(flow.Talking);
        Assert.True(flow.Finished);
    }

    [Fact]
    public void End_WithoutBegin_IsFalse()
    {
        OfferTalkFlow flow = new();

        Assert.False(flow.End());
        Assert.False(flow.Talking);
        Assert.False(flow.Finished);
    }

    [Fact]
    public void Begin_AfterFinished_IsFalse()
    {
        OfferTalkFlow flow = new();
        Assert.True(flow.Begin());
        Assert.True(flow.End());

        Assert.False(flow.Begin());
        Assert.False(flow.Talking);
    }

    [Fact]
    public void Rearm_AfterFinished_AllowsBegin()
    {
        OfferTalkFlow flow = new();
        Assert.True(flow.Begin());
        Assert.True(flow.End());

        flow.Rearm();

        Assert.False(flow.Talking);
        Assert.False(flow.Finished);
        Assert.True(flow.Begin());
        Assert.True(flow.Talking);
    }

    [Fact]
    public void Abort_StopsTalking_AndBlocksUntilRearm()
    {
        OfferTalkFlow flow = new();
        Assert.True(flow.Begin());

        flow.Abort();

        Assert.False(flow.Talking);
        Assert.True(flow.Finished);
        Assert.False(flow.End());
        Assert.False(flow.Begin());

        flow.Rearm();

        Assert.True(flow.Begin());
    }
}
