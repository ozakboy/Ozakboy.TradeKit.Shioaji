using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.MarketData;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiQuoteClientTests
{
    private static ShioajiSubscription Stock(string code = "2330", ShioajiQuoteType type = ShioajiQuoteType.Tick) => new()
    {
        SecurityType = ShioajiSecurityType.Stock,
        Exchange = "TSE",
        Code = code,
        QuoteType = type,
    };

    [TestMethod]
    public async Task Subscribe_PostsTheDocumentedBody()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Quotes.SubscribeAsync(Stock());

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual(
            """{"security_type":"STK","exchange":"TSE","code":"2330","quote_type":"Tick","intraday_odd":false}""",
            server.Requests[0].Body);
        Assert.AreEqual("/api/v1/stream/subscribe", server.Requests[0].Path);
        Assert.AreEqual(1, host.Budget.SubscriptionCount);
    }

    [TestMethod]
    public async Task Subscribe_ContinuousMonthWithATargetCode_SendsIt()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Quotes.SubscribeAsync(new ShioajiSubscription
        {
            SecurityType = ShioajiSecurityType.Future,
            Exchange = "TAIFEX",
            Code = "TXFR1",
            QuoteType = ShioajiQuoteType.BidAsk,
            TargetCode = "TXFK6",
            IntradayOdd = true,
        });

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(
            """{"security_type":"FUT","exchange":"TAIFEX","code":"TXFR1","quote_type":"BidAsk","target_code":"TXFK6","intraday_odd":true}""",
            server.Requests[0].Body);
    }

    [TestMethod]
    [DataRow("TXFR1")]
    [DataRow("TXFR2")]
    [DataRow("mxfr1")]
    public async Task Subscribe_ContinuousMonthWithoutATargetCode_IsRefusedLocally_AndNothingIsSent(string code)
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        foreach (var target in new string?[] { null, string.Empty, "  " })
        {
            var result = await host.Quotes.SubscribeAsync(new ShioajiSubscription
            {
                SecurityType = ShioajiSecurityType.Future,
                Exchange = "TAIFEX",
                Code = code,
                QuoteType = ShioajiQuoteType.Tick,
                TargetCode = target,
            });

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(ShioajiErrorCodes.SubscriptionTargetCodeRequired, result.Error.Code);
            Assert.AreEqual("shioaji.subscription.target_code_required", result.Error.Code);
        }

        Assert.IsEmpty(server.Requests, "a refused subscription must never reach the server");
        Assert.AreEqual(0, host.Budget.SubscriptionCount, "and must not consume a subscription unit");
    }

    [TestMethod]
    public async Task Subscribe_ANonContinuousFutureNeedsNoTargetCode()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Quotes.SubscribeAsync(new ShioajiSubscription
        {
            SecurityType = ShioajiSecurityType.Future,
            Exchange = "TAIFEX",
            Code = "TXFK6",
            QuoteType = ShioajiQuoteType.Tick,
        });

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public async Task Subscribe_AnIndexOnlySupportsQuote()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        var index = new ShioajiSubscription { SecurityType = ShioajiSecurityType.Index, Exchange = "TSE", Code = "IX0001", QuoteType = ShioajiQuoteType.Tick };

        var refused = await host.Quotes.SubscribeAsync(index);
        var accepted = await host.Quotes.SubscribeAsync(index with { QuoteType = ShioajiQuoteType.Quote });

        Assert.AreEqual(ShioajiErrorCodes.SubscriptionIndexRequiresQuote, refused.Error!.Code);
        Assert.IsTrue(accepted.IsSuccess);
        Assert.AreEqual(1, server.Requests.Count);
    }

    [TestMethod]
    public async Task Subscribe_LocalValidation()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var cases = new[]
        {
            Stock(code: " "),
            Stock() with { Exchange = string.Empty },
            Stock() with { SecurityType = ShioajiSecurityType.Unknown },
            Stock() with { QuoteType = ShioajiQuoteType.Unknown },
        };

        foreach (var subscription in cases)
        {
            Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Quotes.SubscribeAsync(subscription)).Error!.Code);
        }

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.Quotes.SubscribeAsync(null!));
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task SubscriptionLimit_IsEnforcedLocally_AndDuplicatesAreNotCountedTwice()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o => o.RateBudget.SubscriptionLimit = 2);

        Assert.IsTrue((await host.Quotes.SubscribeAsync(Stock("2330"))).IsSuccess);
        Assert.IsTrue((await host.Quotes.SubscribeAsync(Stock("2330"))).IsSuccess, "re-subscribing the same one is idempotent");
        Assert.AreEqual(1, host.Budget.SubscriptionCount);

        Assert.IsTrue((await host.Quotes.SubscribeAsync(Stock("2317"))).IsSuccess);

        var third = await host.Quotes.SubscribeAsync(Stock("2454"));

        Assert.AreEqual(ShioajiErrorCodes.SubscriptionLimitExceeded, third.Error!.Code);
        Assert.AreEqual("shioaji.subscription.limit_exceeded", third.Error.Code);
        Assert.AreEqual(3, server.Count("POST", "/api/v1/stream/subscribe"), "the refused one never reaches the server");
        Assert.HasCount(2, host.Quotes.ActiveSubscriptions);
    }

    [TestMethod]
    public async Task AFailedSubscribe_GivesTheUnitBack()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/stream/subscribe", 500, "{}");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Quotes.SubscribeAsync(Stock());

        Assert.AreEqual("shioaji.http.500", result.Error!.Code);
        Assert.AreEqual(0, host.Budget.SubscriptionCount);
        Assert.IsEmpty(host.Quotes.ActiveSubscriptions);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/stream/subscribe"));
    }

    [TestMethod]
    public async Task ARejectedSubscribe_SuccessFalse_IsAFailureThatGivesTheUnitBack()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/stream/subscribe", 200, """{"success":false,"message":"quota exceeded"}""");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Quotes.SubscribeAsync(Stock());

        Assert.AreEqual(ShioajiErrorCodes.SubscriptionRejected, result.Error!.Code);
        Assert.Contains("quota exceeded", result.Error.Message);
        Assert.AreEqual(0, host.Budget.SubscriptionCount);
    }

    [TestMethod]
    public async Task AnUnreadableOrFlaglessSuccessAnswer_CountsAsSuccess()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/stream/subscribe", 200, "ok");
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.Quotes.SubscribeAsync(Stock("2330"))).IsSuccess);

        server.Map("POST", "/api/v1/stream/subscribe", 200, """{"message":"fine"}""");
        Assert.IsTrue((await host.Quotes.SubscribeAsync(Stock("2317"))).IsSuccess);
    }

    [TestMethod]
    public async Task Unsubscribe_ReleasesTheUnit_OnlyOnSuccess_AndOnlyForTrackedOnes()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        await host.Quotes.SubscribeAsync(Stock("2330"));
        Assert.AreEqual(1, host.Budget.SubscriptionCount);

        server.Map("POST", "/api/v1/stream/unsubscribe", 500, "{}");
        Assert.IsTrue((await host.Quotes.UnsubscribeAsync(Stock("2330"))).IsFailure);
        Assert.AreEqual(1, host.Budget.SubscriptionCount, "a failed unsubscribe changes nothing");

        server.Map("POST", "/api/v1/stream/unsubscribe", 200, """{"success":true}""");
        Assert.IsTrue((await host.Quotes.UnsubscribeAsync(Stock("2330"))).IsSuccess);
        Assert.AreEqual(0, host.Budget.SubscriptionCount);

        Assert.IsTrue((await host.Quotes.UnsubscribeAsync(Stock("9999"))).IsSuccess);
        Assert.AreEqual(0, host.Budget.SubscriptionCount, "an untracked unsubscribe must not push the count below zero");
        Assert.AreEqual("/api/v1/stream/unsubscribe", server.Requests[^1].Path);
    }

    [TestMethod]
    public async Task Unsubscribe_LocalValidation()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Quotes.UnsubscribeAsync(Stock(" "))).Error!.Code);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task ReplaySubscriptions_ResendsEveryTrackedOne_WithoutCountingAgain()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        await host.Quotes.SubscribeAsync(Stock("2330"));
        await host.Quotes.SubscribeAsync(Stock("2317", ShioajiQuoteType.BidAsk));
        server.ForgetSubscriptions();

        var replay = await host.Quotes.ReplaySubscriptionsAsync();

        Assert.IsTrue(replay.IsSuccess);
        Assert.AreEqual(4, server.Count("POST", "/api/v1/stream/subscribe"));
        Assert.AreEqual(2, host.Budget.SubscriptionCount);
        Assert.AreEqual("2317,2330", string.Join(',', server.SubscribedCodes.Order(StringComparer.Ordinal)));
    }

    [TestMethod]
    public async Task ReplaySubscriptions_AttemptsEveryOne_AndReportsTheFirstFailureWithACount()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        await host.Quotes.SubscribeAsync(Stock("2330"));
        await host.Quotes.SubscribeAsync(Stock("2317"));
        server.Map("POST", "/api/v1/stream/subscribe", 500, "{}");

        var replay = await host.Quotes.ReplaySubscriptionsAsync();

        Assert.IsTrue(replay.IsFailure);
        Assert.AreEqual("2", replay.Error.Data!["failedCount"]);
        Assert.AreEqual(4, server.Count("POST", "/api/v1/stream/subscribe"));
        Assert.AreEqual(2, host.Budget.SubscriptionCount, "a failed replay must not release anything");
    }

    [TestMethod]
    public async Task ReplaySubscriptions_WithNothingTracked_IsASuccess()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.Quotes.ReplaySubscriptionsAsync()).IsSuccess);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public void Constructor_RejectsNulls()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiQuoteClient(null!, host.Budget));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiQuoteClient(host.Api, null!));
    }
}
