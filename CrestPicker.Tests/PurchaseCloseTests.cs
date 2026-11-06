using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class PurchaseCloseTests
{
    [Fact]
    public void MessageSeen_ClosesHalfSecondAfterItShows()
    {
        Assert.Equal(
            CloseDecision.None,
            PurchaseClose.Decide(elapsed: 0.69f, messageSeen: true, messageShownAt: 0.2f, emergencySent: false));
        Assert.Equal(
            CloseDecision.CloseWindow,
            PurchaseClose.Decide(elapsed: 0.7f, messageSeen: true, messageShownAt: 0.2f, emergencySent: false));
    }

    [Fact]
    public void MessageNeverSeen_FallsBackToThreeSeconds()
    {
        Assert.Equal(
            CloseDecision.None,
            PurchaseClose.Decide(elapsed: 2.9f, messageSeen: false, messageShownAt: 0f, emergencySent: false));
        Assert.Equal(
            CloseDecision.CloseWindow,
            PurchaseClose.Decide(elapsed: 3f, messageSeen: false, messageShownAt: 0f, emergencySent: false));
    }

    [Fact]
    public void AfterCloseSent_ForcesIdleAtFiveSeconds()
    {
        Assert.Equal(
            CloseDecision.None,
            PurchaseClose.Decide(elapsed: 4.9f, messageSeen: true, messageShownAt: 0.2f, emergencySent: true));
        Assert.Equal(
            CloseDecision.ForceIdle,
            PurchaseClose.Decide(elapsed: 5f, messageSeen: true, messageShownAt: 0.2f, emergencySent: true));
    }

    [Fact]
    public void BeforeThresholds_DoesNothing()
    {
        Assert.Equal(
            CloseDecision.None,
            PurchaseClose.Decide(elapsed: 0.1f, messageSeen: true, messageShownAt: 0.05f, emergencySent: false));
    }
}
