namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiReconnectBackoffTests
{
    [TestMethod]
    public void GrowsExponentiallyUntilTheCeiling()
    {
        var backoff = new ShioajiReconnectBackoff { JitterRatio = 0d };

        Assert.AreEqual(TimeSpan.FromSeconds(1), backoff.GetDelay(1, 0.5));
        Assert.AreEqual(TimeSpan.FromSeconds(2), backoff.GetDelay(2, 0.5));
        Assert.AreEqual(TimeSpan.FromSeconds(4), backoff.GetDelay(3, 0.5));
        Assert.AreEqual(TimeSpan.FromSeconds(16), backoff.GetDelay(5, 0.5));
        Assert.AreEqual(TimeSpan.FromSeconds(30), backoff.GetDelay(6, 0.5));
        Assert.AreEqual(TimeSpan.FromSeconds(30), backoff.GetDelay(500, 0.5));
    }

    [TestMethod]
    public void JitterStaysWithinTheConfiguredRatio()
    {
        var backoff = new ShioajiReconnectBackoff { JitterRatio = 0.2d };

        Assert.AreEqual(0.8d, backoff.GetDelay(1, 0d).TotalSeconds, 1e-9);
        Assert.AreEqual(1.0d, backoff.GetDelay(1, 0.5d).TotalSeconds, 1e-9);
        Assert.AreEqual(1.2d, backoff.GetDelay(1, 1d).TotalSeconds, 1e-9);
    }

    [TestMethod]
    public void JitterNeverPushesPastTheCeiling()
    {
        var backoff = new ShioajiReconnectBackoff { JitterRatio = 0.5d };

        Assert.AreEqual(backoff.Max, backoff.GetDelay(50, 1d));
    }

    [TestMethod]
    public void RejectsOutOfRangeArguments()
    {
        var backoff = new ShioajiReconnectBackoff();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => backoff.GetDelay(0, 0.5));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => backoff.GetDelay(1, -0.1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => backoff.GetDelay(1, 1.1));
    }

    [TestMethod]
    public void Validate_CatchesEachBadField()
    {
        Assert.IsTrue(new ShioajiReconnectBackoff().Validate().IsSuccess);
        Assert.IsTrue(new ShioajiReconnectBackoff { Initial = TimeSpan.Zero }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiReconnectBackoff { Max = TimeSpan.FromMilliseconds(1) }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiReconnectBackoff { JitterRatio = 1.5d }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiReconnectBackoff { JitterRatio = double.NaN }.Validate().IsFailure);
    }
}
