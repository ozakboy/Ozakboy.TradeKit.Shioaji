using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiContractClientTests
{
    [TestMethod]
    public async Task GetContract_Stock_MapsTheFields_AndAnEmptyTargetCodeIsNull()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/2330", 200, Fixture.Read("contract_stk.json"));
        using var host = ShioajiTestHost.Create(server);

        var contract = (await host.Contracts.GetContractAsync("2330", ShioajiSecurityType.Stock)).GetValueOrThrow();

        Assert.AreEqual(ShioajiSecurityType.Stock, contract.SecurityType);
        Assert.AreEqual("TW", contract.Region);
        Assert.AreEqual("TSE", contract.Exchange);
        Assert.AreEqual("2330", contract.Code);
        Assert.IsNull(contract.TargetCode);
        Assert.AreEqual("?security_type=STK", server.Requests[0].Query);
    }

    [TestMethod]
    public async Task GetContract_ContinuousMonth_CarriesTheTargetCode()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/TXFR1", 200, Fixture.Read("contract_futr1.json"));
        using var host = ShioajiTestHost.Create(server);

        var contract = (await host.Contracts.GetContractAsync("TXFR1", ShioajiSecurityType.Future)).GetValueOrThrow();

        Assert.AreEqual(ShioajiSecurityType.Future, contract.SecurityType);
        Assert.AreEqual("TXFK6", contract.TargetCode);
        Assert.AreEqual("?security_type=FUT", server.Requests[0].Query);
    }

    [TestMethod]
    public async Task GetContract_WithoutASecurityType_SendsNoQuery_AndEscapesTheCode()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/A B", 200, Fixture.Read("contract_stk.json"));
        using var host = ShioajiTestHost.Create(server);

        var unspecified = await host.Contracts.GetContractAsync("A B");
        var unknownType = await host.Contracts.GetContractAsync("A B", ShioajiSecurityType.Unknown);

        Assert.IsTrue(unspecified.IsSuccess, unspecified.Error?.ToString());
        Assert.IsTrue(unknownType.IsSuccess);
        Assert.AreEqual(string.Empty, server.Requests[0].Query);
        Assert.AreEqual(string.Empty, server.Requests[1].Query);
    }

    [TestMethod]
    public async Task GetContract_Failures()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var blank = await host.Contracts.GetContractAsync("  ");
        var missing = await host.Contracts.GetContractAsync("NOPE");

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, blank.Error!.Code);
        Assert.AreEqual("shioaji.http.404", missing.Error!.Code);
        Assert.AreEqual(ErrorCategory.NotFound, missing.Error.Category);

        server.Map("GET", "/api/v1/data/contracts/BAD", 200, """{"security_type":"STK","code":"BAD"}""");
        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.Contracts.GetContractAsync("BAD")).Error!.Code);

        server.Map("GET", "/api/v1/data/contracts/ARR", 200, "[1]");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Contracts.GetContractAsync("ARR")).Error!.Code);
    }

    [TestMethod]
    public async Task GetContractInfo_MapsDayTradeDatePricesAndKeepsTheRest()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/2330/info", 200, Fixture.Read("contract_info_stk.json"));
        using var host = ShioajiTestHost.Create(server);

        var info = (await host.Contracts.GetContractInfoAsync("2330")).GetValueOrThrow();

        Assert.AreEqual("2330", info.Code);
        Assert.AreEqual("台積電", info.Name);
        Assert.AreEqual("TSE", info.Exchange);
        Assert.AreEqual(ShioajiSecurityType.Stock, info.SecurityType);
        Assert.AreEqual(ShioajiDayTradeEligibility.Yes, info.DayTrade);
        Assert.AreEqual("24", info.Category);
        Assert.AreEqual(new DateOnly(2026, 10, 30), info.UpdateDate);
        Assert.AreEqual(600.5m, info.Reference);
        Assert.AreEqual(660.5m, info.LimitUp, "a string-typed price must be read");
        Assert.AreEqual(540.5m, info.LimitDown);
        Assert.AreEqual(1000m, info.Unit);
        Assert.IsNull(info.TargetCode);
        Assert.AreEqual("TSE2330", info.Raw.GetProperty("symbol").GetString());
        Assert.AreEqual(0, info.Raw.GetProperty("margin_trading_balance").GetInt32());
    }

    [TestMethod]
    [DataRow("Yes", ShioajiDayTradeEligibility.Yes)]
    [DataRow("OnlyBuy", ShioajiDayTradeEligibility.OnlyBuy)]
    [DataRow("No", ShioajiDayTradeEligibility.No)]
    [DataRow("Maybe", ShioajiDayTradeEligibility.Unknown)]
    public async Task DayTrade_UnreadableValuesStayUnknown_NeverYes(string wire, ShioajiDayTradeEligibility expected)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/X/info", 200, $$"""{"code":"X","day_trade":"{{wire}}"}""");
        using var host = ShioajiTestHost.Create(server);

        var info = (await host.Contracts.GetContractInfoAsync("X")).GetValueOrThrow();

        Assert.AreEqual(expected, info.DayTrade);
    }

    [TestMethod]
    public async Task GetContractInfo_SparseResponse_HasNullsAndAnUnknownDayTrade()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/IX0001/info", 200, """{"code":"IX0001","update_date":"garbled","reference":"n/a"}""");
        using var host = ShioajiTestHost.Create(server);

        var info = (await host.Contracts.GetContractInfoAsync("IX0001")).GetValueOrThrow();

        Assert.AreEqual(ShioajiDayTradeEligibility.Unknown, info.DayTrade);
        Assert.IsNull(info.UpdateDate);
        Assert.IsNull(info.Reference);
        Assert.AreEqual(string.Empty, info.Name);
    }

    [TestMethod]
    public async Task GetContractInfo_Failures()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/NOCODE/info", 200, "{}");
        server.Map("GET", "/api/v1/data/contracts/ARR/info", 200, "[]");
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Contracts.GetContractInfoAsync("")).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.Contracts.GetContractInfoAsync("NOCODE")).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Contracts.GetContractInfoAsync("ARR")).Error!.Code);
    }

    [TestMethod]
    public async Task ListContracts_WrappedPage_CarriesTheServersPagingFields()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", 200, """
            {"items":[{"security_type":"STK","exchange":"TSE","code":"1101"},{"security_type":"STK","exchange":"TSE","code":"2330"}],"page":2,"page_size":2,"total":1800}
            """);
        using var host = ShioajiTestHost.Create(server);

        var page = (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Stock, page: 2, pageSize: 2)).GetValueOrThrow();

        Assert.HasCount(2, page.Items);
        Assert.AreEqual("2330", page.Items[1].Code);
        Assert.AreEqual(2, page.Page);
        Assert.AreEqual(2, page.PageSize);
        Assert.AreEqual(1800L, page.Total);
        Assert.AreEqual("?security_type=STK&page=2&page_size=2", server.Requests[0].Query);
    }

    [TestMethod]
    public async Task ListContracts_BareArray_FallsBackToTheRequestedPaging()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", 200, """[{"security_type":"IND","exchange":"TSE","code":"IX0001"}]""");
        using var host = ShioajiTestHost.Create(server);

        var page = (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Index, page: 1, pageSize: 50)).GetValueOrThrow();

        Assert.HasCount(1, page.Items);
        Assert.AreEqual(1, page.Page);
        Assert.AreEqual(50, page.PageSize);
        Assert.IsNull(page.Total);

        var all = (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Index)).GetValueOrThrow();

        Assert.IsNull(all.Page);
        Assert.AreEqual("?security_type=IND", server.Requests[1].Query);
    }

    [TestMethod]
    public async Task ListContracts_Failures()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Unknown)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Stock, page: 0)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Stock, pageSize: -1)).Error!.Code);
        Assert.IsEmpty(server.Requests);

        server.Map("GET", "/api/v1/data/contracts", 200, """{"nothing":[]}""");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Stock)).Error!.Code);

        server.Map("GET", "/api/v1/data/contracts", 200, "42");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Stock)).Error!.Code);

        server.Map("GET", "/api/v1/data/contracts", 200, """[{"security_type":"STK"}]""");
        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.Contracts.ListContractsAsync(ShioajiSecurityType.Stock)).Error!.Code);
    }

    // ───────────────────────────── ListAllContractsAsync ─────────────────────────────

    private static string ContractItems(int firstIndex, int count) =>
        string.Join(
            ',',
            Enumerable.Range(firstIndex, count).Select(i =>
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $$"""{"security_type":"STK","exchange":"TSE","code":"C{{i:D5}}"}""")));

    private static int PageOf(FakeRequest request) =>
        int.Parse(
            request.Query.TrimStart('?').Split('&').Single(p => p.StartsWith("page=", StringComparison.Ordinal))["page=".Length..],
            System.Globalization.CultureInfo.InvariantCulture);

    [TestMethod]
    public async Task ListAllContracts_WalksEveryPage_At500PerPage_UntilAShortPage()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", request => PageOf(request) switch
        {
            1 => new FakeResponse(200, $$"""{"items":[{{ContractItems(0, 500)}}],"page":1,"page_size":500}"""),
            2 => new FakeResponse(200, $$"""{"items":[{{ContractItems(500, 500)}}],"page":2,"page_size":500}"""),
            3 => new FakeResponse(200, $$"""{"items":[{{ContractItems(1000, 120)}}],"page":3,"page_size":500}"""),
            _ => new FakeResponse(200, """{"items":[]}"""),
        });
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Stock);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var all = result.GetValueOrThrow();
        Assert.HasCount(1120, all);
        Assert.AreEqual("C00000", all[0].Code);
        Assert.AreEqual("C01119", all[^1].Code);
        Assert.AreEqual(1120, all.Select(c => c.Code).Distinct().Count());
        Assert.HasCount(3, server.Requests, "the short third page ends the walk");
        Assert.AreEqual("?security_type=STK&page=1&page_size=500", server.Requests[0].Query);
        Assert.AreEqual("?security_type=STK&page=3&page_size=500", server.Requests[2].Query);
    }

    [TestMethod]
    public async Task ListAllContracts_StopsAtTheReportedTotal_WithoutAskingForAnEmptyPage()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", 200, $$"""{"items":[{{ContractItems(0, 500)}}],"page":1,"page_size":500,"total":500}""");
        using var host = ShioajiTestHost.Create(server);

        var all = (await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Stock)).GetValueOrThrow();

        Assert.HasCount(500, all);
        Assert.HasCount(1, server.Requests);
    }

    [TestMethod]
    public async Task ListAllContracts_AFullPageWithNoTotal_NeedsOneMoreRequestToSeeTheEnd()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", request =>
            PageOf(request) == 1 ? new FakeResponse(200, $"[{ContractItems(0, 500)}]") : new FakeResponse(200, "[]"));
        using var host = ShioajiTestHost.Create(server);

        var all = (await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Stock)).GetValueOrThrow();

        Assert.HasCount(500, all);
        Assert.HasCount(2, server.Requests);
    }

    [TestMethod]
    public async Task ListAllContracts_ServerThatIgnoresPaging_ReturnsEverythingFromTheFirstAnswer()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", 200, $"[{ContractItems(0, 700)}]");
        using var host = ShioajiTestHost.Create(server);

        var all = (await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Stock)).GetValueOrThrow();

        Assert.HasCount(700, all);
        Assert.HasCount(1, server.Requests, "700 is more than a page: the server returned everything at once");
    }

    [TestMethod]
    public async Task ListAllContracts_APageThatRepeatsThePreviousOne_IsAFailureNotAnEndlessLoop()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", 200, $"[{ContractItems(0, 500)}]");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Stock);

        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, result.Error!.Code);
        Assert.HasCount(2, server.Requests, "the second page is the one that repeats the first");
    }

    [TestMethod]
    public async Task ListAllContracts_PagingThatNeverEnds_StopsAt200Pages()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", request =>
            new FakeResponse(200, $"[{ContractItems((PageOf(request) - 1) * 500, 500)}]"));
        using var host = ShioajiTestHost.Create(server, o => o.RateBudget.MarketQueryLimit = 1000);

        var result = await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Stock);

        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, result.Error!.Code);
        Assert.HasCount(200, server.Requests);
    }

    [TestMethod]
    public async Task ListAllContracts_AFailureMidWay_ReturnsTheFailure_NotAHalfList()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", request =>
            PageOf(request) == 1 ? new FakeResponse(200, $"[{ContractItems(0, 500)}]") : new FakeResponse(400, """{"detail":"bad page"}"""));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Stock);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("shioaji.http.400", result.Error!.Code);
    }

    [TestMethod]
    public async Task ListAllContracts_AnUnknownType_IsRefusedBeforeAnyRequest()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Unknown);

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task ListAllContracts_EveryPageGoesThroughTheMarketQueryBudget()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts", request => PageOf(request) switch
        {
            1 => new FakeResponse(200, $"[{ContractItems(0, 500)}]"),
            2 => new FakeResponse(200, $"[{ContractItems(500, 500)}]"),
            _ => new FakeResponse(200, $"[{ContractItems(1000, 10)}]"),
        });
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.MarketQueryLimit = 2;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        var result = await host.Contracts.ListAllContractsAsync(ShioajiSecurityType.Stock);

        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, result.Error!.Code, "the third page found the budget spent");
        Assert.HasCount(2, server.Requests, "the page the budget refused was never sent");

        // 不給預算的 client 不受限:其他商品查詢本來就不計預算。
        // A client built with no budget is unrestricted: the other contract lookups never counted anyway.
        var unlimited = new ShioajiContractClient(host.Api);
        var all = (await unlimited.ListAllContractsAsync(ShioajiSecurityType.Stock)).GetValueOrThrow();

        Assert.HasCount(1010, all);
    }

    [TestMethod]
    public async Task OtherContractLookups_StillDoNotTouchTheBudget()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/2330", 200, Fixture.Read("contract_stk.json"));
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.MarketQueryLimit = 1;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        for (var i = 0; i < 3; i++)
        {
            Assert.IsTrue((await host.Contracts.GetContractAsync("2330")).IsSuccess);
        }
    }

    [TestMethod]
    public async Task ListFutures_BuildsTheQuery_AndMapsAnArrayOrAWrappedList()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/contracts/futures", 200, """[{"security_type":"FUT","exchange":"TAIFEX","code":"TXFK6"},{"security_type":"FUT","exchange":"TAIFEX","code":"TXFL6"}]""");
        using var host = ShioajiTestHost.Create(server);

        var both = (await host.Contracts.ListFuturesAsync("TXF", "IX0001")).GetValueOrThrow();
        var none = (await host.Contracts.ListFuturesAsync()).GetValueOrThrow();

        Assert.HasCount(2, both);
        Assert.HasCount(2, none);
        Assert.AreEqual("?root=TXF&underlying_code=IX0001", server.Requests[0].Query);
        Assert.AreEqual(string.Empty, server.Requests[1].Query);

        await host.Contracts.ListFuturesAsync(underlyingCode: "IX0001");
        Assert.AreEqual("?underlying_code=IX0001", server.Requests[2].Query);

        server.Map("GET", "/api/v1/data/contracts/futures", 200, """{"contracts":[{"security_type":"FUT","exchange":"TAIFEX","code":"TXFK6"}]}""");
        Assert.HasCount(1, (await host.Contracts.ListFuturesAsync("TXF")).GetValueOrThrow());
    }

    [TestMethod]
    public void Constructor_RejectsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiContractClient(null!));
    }
}
