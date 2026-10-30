using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiAuthClientTests
{
    private static readonly ShioajiAccount StockAccount = new()
    {
        AccountType = ShioajiAccountType.Stock,
        PersonId = "A123456789",
        BrokerId = "9A95",
        AccountId = "1234567",
        IsSigned = true,
        Username = "TEST",
    };

    [TestMethod]
    public async Task GetInfo_MapsTheServerIdentityAndMode()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var info = (await host.Auth.GetInfoAsync()).GetValueOrThrow();

        Assert.AreEqual("shioaji", info.Name);
        Assert.AreEqual("1.7.10", info.Version);
        Assert.IsTrue(info.Simulation);
        Assert.AreEqual("http,sse", string.Join(',', info.Protocols));
    }

    [TestMethod]
    public async Task GetInfo_WithoutTheSimulationFlag_IsAFailure_NotADefault()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/info", 200, """{"name":"shioaji","version":"1"}""");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Auth.GetInfoAsync();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.MissingField, result.Error.Code);
        Assert.AreEqual("simulation", result.Error.Data![ShioajiErrorDataKeys.Field]);
    }

    [TestMethod]
    public async Task GetInfo_ToleratesAStringProtocolAndRejectsNonObjects()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/info", 200, """{"name":"s","version":"1","simulation":"false","protocols":"http"}""");
        using var host = ShioajiTestHost.Create(server);

        var info = (await host.Auth.GetInfoAsync()).GetValueOrThrow();

        Assert.IsFalse(info.Simulation, "a string-typed boolean must be readable");
        Assert.AreEqual("http", string.Join(',', info.Protocols));

        server.Map("GET", "/api/v1/info", 200, "[1]");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Auth.GetInfoAsync()).Error!.Code);

        server.Map("GET", "/api/v1/info", 200, "{not json");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Auth.GetInfoAsync()).Error!.Code);
    }

    [TestMethod]
    public async Task GetAccounts_MapsStockAndFuturesAccounts()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var accounts = (await host.Auth.GetAccountsAsync()).GetValueOrThrow();

        Assert.HasCount(2, accounts);
        Assert.AreEqual(ShioajiAccountType.Stock, accounts[0].AccountType);
        Assert.AreEqual("9A95", accounts[0].BrokerId);
        Assert.AreEqual("1234567", accounts[0].AccountId);
        Assert.IsTrue(accounts[0].IsSigned);
        Assert.AreEqual(ShioajiAccountType.Futures, accounts[1].AccountType);
    }

    [TestMethod]
    public async Task GetAccounts_UnsignedOrMissingFlag_IsNotSigned_AndAWrappedArrayIsAccepted()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/auth/accounts", 200, """{"accounts":[{"account_type":"H","broker_id":"b","account_id":"a"}]}""");
        using var host = ShioajiTestHost.Create(server);

        var accounts = (await host.Auth.GetAccountsAsync()).GetValueOrThrow();

        Assert.HasCount(1, accounts);
        Assert.AreEqual(ShioajiAccountType.Foreign, accounts[0].AccountType);
        Assert.IsFalse(accounts[0].IsSigned, "an absent flag must read as unsigned, the safe direction");
        Assert.AreEqual(string.Empty, accounts[0].PersonId);
    }

    [TestMethod]
    public async Task GetAccounts_RejectsAnAccountWithoutAnId_AndNonArrays()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/auth/accounts", 200, """[{"account_type":"S","broker_id":"b"}]""");
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.Auth.GetAccountsAsync()).Error!.Code);

        server.Map("GET", "/api/v1/auth/accounts", 200, "{}");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Auth.GetAccountsAsync()).Error!.Code);
    }

    [TestMethod]
    public async Task GetUsage_MapsTheCountersAndConsumesTheAccountBudget()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o => o.RateBudget.PortfolioLimit = 1);

        var usage = (await host.Auth.GetUsageAsync()).GetValueOrThrow();

        Assert.AreEqual(1L, usage.Connections);
        Assert.AreEqual(1024L, usage.Bytes);
        Assert.AreEqual(2_147_483_648L, usage.LimitBytes);
        Assert.AreEqual(2_147_482_624L, usage.RemainingBytes);

        host.Options.RateBudget.MaxWait = TimeSpan.Zero;
        var second = await host.Auth.GetUsageAsync();

        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, second.Error!.Code);
        Assert.AreEqual(1, server.Count("GET", "/api/v1/auth/usage"), "the refused call must never reach the server");
    }

    [TestMethod]
    public async Task GetUsage_MissingCounter_IsAFailure()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/auth/usage", 200, """{"connections":1,"bytes":2}""");
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.Auth.GetUsageAsync()).Error!.Code);
    }

    [TestMethod]
    public async Task GetStreamStatus_MapsTheTimestampWhateverItsShape()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var numeric = (await host.Auth.GetStreamStatusAsync()).GetValueOrThrow();

        Assert.AreEqual(1L, numeric.ActiveConnections);
        Assert.AreEqual("ok", numeric.Status);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1_761_800_000_500), numeric.Timestamp);

        server.Map("GET", "/api/v1/stream/status", 200, """{"active_connections":0,"timestamp":"2026-10-30T09:00:00","status":"idle"}""");
        var textual = (await host.Auth.GetStreamStatusAsync()).GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero), textual.Timestamp);

        server.Map("GET", "/api/v1/stream/status", 200, """{"active_connections":0,"timestamp":"never"}""");
        var unreadable = (await host.Auth.GetStreamStatusAsync()).GetValueOrThrow();

        Assert.IsNull(unreadable.Timestamp);
        Assert.AreEqual(string.Empty, unreadable.Status);
    }

    [TestMethod]
    public async Task SubscribeTrade_PostsTheAccountAndNeverReadsTheResponse()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/auth/subscribe_trade", 200, "this is not json");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Auth.SubscribeTradeAsync(StockAccount);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"broker_id":"9A95","account_id":"1234567","account_type":"S"}""", server.Requests[0].Body);
    }

    [TestMethod]
    public async Task UnsubscribeTrade_UsesItsOwnEndpoint_AndFuturesIsSentAsF()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var futures = StockAccount with { AccountType = ShioajiAccountType.Futures, BrokerId = "F002000", AccountId = "7654321" };

        Assert.IsTrue((await host.Auth.UnsubscribeTradeAsync(futures)).IsSuccess);
        Assert.AreEqual("/api/v1/auth/unsubscribe_trade", server.Requests[0].Path);
        Assert.Contains("\"account_type\":\"F\"", server.Requests[0].Body);
    }

    [TestMethod]
    public async Task SubscribeTrade_FailureAndLocalValidation()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/auth/subscribe_trade", 500, "{}");
        using var host = ShioajiTestHost.Create(server);

        var failed = await host.Auth.SubscribeTradeAsync(StockAccount);

        Assert.AreEqual("shioaji.http.500", failed.Error!.Code);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/auth/subscribe_trade"), "subscriptions are never retried");

        var foreign = await host.Auth.SubscribeTradeAsync(StockAccount with { AccountType = ShioajiAccountType.Foreign });
        var unknown = await host.Auth.SubscribeTradeAsync(StockAccount with { AccountType = ShioajiAccountType.Unknown });
        var noBroker = await host.Auth.SubscribeTradeAsync(StockAccount with { BrokerId = " " });
        var noAccount = await host.Auth.SubscribeTradeAsync(StockAccount with { AccountId = string.Empty });

        foreach (var refused in new[] { foreign, unknown, noBroker, noAccount })
        {
            Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, refused.Error!.Code);
        }

        Assert.AreEqual(1, server.Count("POST", "/api/v1/auth/subscribe_trade"));
    }

    [TestMethod]
    public void Constructor_RejectsNulls()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiAuthClient(null!, host.Budget));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiAuthClient(host.Api, null!));
    }
}
