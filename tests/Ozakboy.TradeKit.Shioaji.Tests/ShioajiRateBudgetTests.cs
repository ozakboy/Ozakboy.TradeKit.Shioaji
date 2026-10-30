using Microsoft.Extensions.Time.Testing;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiRateBudgetTests
{
    [TestMethod]
    public async Task Order_AllowsTheWindowLimitImmediately_ThenWaitsForTheNextWindow()
    {
        var clock = new FakeTimeProvider();
        var budget = new ShioajiRateBudget(new ShioajiRateBudgetOptions { OrderLimit = 3, OrderWindow = TimeSpan.FromSeconds(10) }, clock);

        for (var i = 0; i < 3; i++)
        {
            Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Order)).IsSuccess);
        }

        var fourth = budget.AcquireAsync(ShioajiBudgetKind.Order);

        await Task.Delay(50);
        Assert.IsFalse(fourth.IsCompleted, "the fourth call must wait for the window to slide");

        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.IsTrue((await fourth).IsSuccess);
    }

    [TestMethod]
    public async Task EachCategoryHasItsOwnWindowAndDefaults()
    {
        var clock = new FakeTimeProvider();
        var budget = new ShioajiRateBudget(timeProvider: clock);

        // 公告上限:行情查詢 10 秒 50 次、帳務 5 秒 25 次、委託 10 秒 250 次。
        // The published ceilings: market queries 50 per 10 s, account 25 per 5 s, orders 250 per 10 s.
        for (var i = 0; i < 50; i++)
        {
            Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.MarketQuery)).IsSuccess);
        }

        for (var i = 0; i < 25; i++)
        {
            Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Portfolio)).IsSuccess);
        }

        for (var i = 0; i < 250; i++)
        {
            Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Order)).IsSuccess);
        }

        // 各類別互不影響:行情查詢用盡,委託仍然一樣用盡才等,這裡每個類別用盡後下一個都要等。
        // Categories are independent; here each is spent in turn and the next call of each has to wait.
        var waiting = new[]
        {
            budget.AcquireAsync(ShioajiBudgetKind.MarketQuery),
            budget.AcquireAsync(ShioajiBudgetKind.Portfolio),
            budget.AcquireAsync(ShioajiBudgetKind.Order),
        };

        await Task.Delay(50);
        Assert.IsTrue(waiting.All(t => !t.IsCompleted));

        clock.Advance(TimeSpan.FromSeconds(10));

        foreach (var task in waiting)
        {
            Assert.IsTrue((await task).IsSuccess);
        }
    }

    [TestMethod]
    public async Task WindowSlides_OldestRequestFreesFirst()
    {
        var clock = new FakeTimeProvider();
        var budget = new ShioajiRateBudget(new ShioajiRateBudgetOptions { PortfolioLimit = 2, PortfolioWindow = TimeSpan.FromSeconds(5) }, clock);

        Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Portfolio)).IsSuccess);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Portfolio)).IsSuccess);

        var third = budget.AcquireAsync(ShioajiBudgetKind.Portfolio);

        await Task.Delay(50);
        Assert.IsFalse(third.IsCompleted);

        // 最舊的那筆在 2 秒後滑出視窗,第二筆還要再 3 秒。
        // The oldest slides out after 2 more seconds, the second needs 3 more.
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.IsTrue((await third).IsSuccess);
    }

    [TestMethod]
    public async Task WaitBeyondMaxWait_FailsAtOnceWithoutSleeping()
    {
        var clock = new FakeTimeProvider();
        var budget = new ShioajiRateBudget(
            new ShioajiRateBudgetOptions { OrderLimit = 1, OrderWindow = TimeSpan.FromSeconds(60), MaxWait = TimeSpan.FromSeconds(10) },
            clock);

        Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Order)).IsSuccess);

        var second = await budget.AcquireAsync(ShioajiBudgetKind.Order);

        Assert.IsTrue(second.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, second.Error.Code);
        Assert.IsTrue(second.Error.IsTransient);
    }

    [TestMethod]
    public async Task Subscription_CountsUpToTheLimit_AndReleaseGivesTheUnitBack()
    {
        var budget = new ShioajiRateBudget(new ShioajiRateBudgetOptions { SubscriptionLimit = 2 });

        Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Subscription)).IsSuccess);
        Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Subscription)).IsSuccess);
        Assert.AreEqual(2, budget.SubscriptionCount);

        var third = await budget.AcquireAsync(ShioajiBudgetKind.Subscription);

        Assert.IsTrue(third.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.SubscriptionLimitExceeded, third.Error.Code);

        budget.ReleaseSubscription();

        Assert.AreEqual(1, budget.SubscriptionCount);
        Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Subscription)).IsSuccess);
    }

    [TestMethod]
    public void Release_NeverGoesBelowZero_AndDefaultLimitIs200()
    {
        var budget = new ShioajiRateBudget();

        budget.ReleaseSubscription();

        Assert.AreEqual(0, budget.SubscriptionCount);
        Assert.AreEqual(200, new ShioajiRateBudgetOptions().SubscriptionLimit);
    }

    [TestMethod]
    public async Task Cancellation_ReturnsACancelledFailureInsteadOfThrowing()
    {
        var clock = new FakeTimeProvider();
        var budget = new ShioajiRateBudget(new ShioajiRateBudgetOptions { OrderLimit = 1, OrderWindow = TimeSpan.FromSeconds(5) }, clock);
        using var cancellation = new CancellationTokenSource();

        Assert.IsTrue((await budget.AcquireAsync(ShioajiBudgetKind.Order)).IsSuccess);

        var waiting = budget.AcquireAsync(ShioajiBudgetKind.Order, cancellation.Token);

        await Task.Delay(50);
        await cancellation.CancelAsync();

        var result = await waiting;

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.RequestCancelled, result.Error.Code);

        var already = await budget.AcquireAsync(ShioajiBudgetKind.Order, cancellation.Token);

        Assert.AreEqual(ShioajiErrorCodes.RequestCancelled, already.Error!.Code);
    }

    [TestMethod]
    public async Task UndefinedKind_Throws()
    {
        var budget = new ShioajiRateBudget();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => budget.AcquireAsync((ShioajiBudgetKind)99));
    }

    [TestMethod]
    public void InvalidOptions_ThrowOnConstruction()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ShioajiRateBudget(new ShioajiRateBudgetOptions { OrderLimit = 0 }));
        Assert.ThrowsExactly<ArgumentException>(() => new ShioajiRateBudget(new ShioajiRateBudgetOptions { OrderWindow = TimeSpan.Zero }));
        Assert.ThrowsExactly<ArgumentException>(() => new ShioajiRateBudget(new ShioajiRateBudgetOptions { MaxWait = TimeSpan.FromSeconds(-1) }));
    }

    [TestMethod]
    public async Task ConcurrentAcquirers_NeverExceedTheLimitInOneWindow()
    {
        var budget = new ShioajiRateBudget(new ShioajiRateBudgetOptions { OrderLimit = 10, OrderWindow = TimeSpan.FromHours(1), MaxWait = TimeSpan.Zero });

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => budget.AcquireAsync(ShioajiBudgetKind.Order))));

        Assert.AreEqual(10, results.Count(r => r.IsSuccess));
        Assert.AreEqual(30, results.Count(r => r.IsFailure));
    }
}
