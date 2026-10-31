using Microsoft.Extensions.DependencyInjection;
using Ozakboy.TradeKit.Shioaji.Orders;
using Ozakboy.TradeKit.Shioaji.Portfolio;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiPortfolioClientTests
{
    private static readonly DateOnly Begin = new(2026, 10, 1);

    private static readonly DateOnly End = new(2026, 10, 31);

    private static readonly ShioajiPortfolioAccount FullAccount = new()
    {
        BrokerId = "9A95",
        AccountId = "1234567",
        PersonId = "A123456789",
    };

    private static async Task<Error?> ErrorOf<T>(Task<Result<T>> call)
    {
        var result = await call;

        return result.IsFailure ? result.Error : null;
    }

    private static object[] Case(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call) =>
        [name, path, accountType, call];

    /// <summary>
    /// 十四個公開查詢方法與它們的路徑、帳號類型。
    /// The fourteen public query methods with their paths and account types.
    /// </summary>
    public static IEnumerable<object[]> Cases() =>
    [
        Case("ListStockPositions", ShioajiApiPaths.PortfolioPositionUnit, "S", (c, a) => ErrorOf(c.ListStockPositionsAsync(account: a))),
        Case("ListFuturesPositions", ShioajiApiPaths.PortfolioPositionUnit, "F", (c, a) => ErrorOf(c.ListFuturesPositionsAsync(a))),
        Case("ListStockPositionDetails", ShioajiApiPaths.PortfolioPositionDetail, "S", (c, a) => ErrorOf(c.ListStockPositionDetailsAsync(7, a))),
        Case("ListFuturesPositionDetails", ShioajiApiPaths.PortfolioPositionDetail, "F", (c, a) => ErrorOf(c.ListFuturesPositionDetailsAsync(7, a))),
        Case("ListStockProfitLoss", ShioajiApiPaths.PortfolioProfitLoss, "S", (c, a) => ErrorOf(c.ListStockProfitLossAsync(Begin, End, account: a))),
        Case("ListFuturesProfitLoss", ShioajiApiPaths.PortfolioProfitLoss, "F", (c, a) => ErrorOf(c.ListFuturesProfitLossAsync(Begin, End, a))),
        Case("ListStockProfitLossDetails", ShioajiApiPaths.PortfolioProfitLossDetail, "S", (c, a) => ErrorOf(c.ListStockProfitLossDetailsAsync(3, account: a))),
        Case("ListFuturesProfitLossDetails", ShioajiApiPaths.PortfolioProfitLossDetail, "F", (c, a) => ErrorOf(c.ListFuturesProfitLossDetailsAsync(3, a))),
        Case("GetStockProfitLossSummary", ShioajiApiPaths.PortfolioProfitLossSummary, "S", (c, a) => ErrorOf(c.GetStockProfitLossSummaryAsync(Begin, End, a))),
        Case("GetFuturesProfitLossSummary", ShioajiApiPaths.PortfolioProfitLossSummary, "F", (c, a) => ErrorOf(c.GetFuturesProfitLossSummaryAsync(Begin, End, a))),
        Case("ListSettlements", ShioajiApiPaths.PortfolioSettlements, "S", (c, a) => ErrorOf(c.ListSettlementsAsync(a))),
        Case("GetTradingLimits", ShioajiApiPaths.PortfolioTradingLimits, "S", (c, a) => ErrorOf(c.GetTradingLimitsAsync(a))),
        Case("GetMargin", ShioajiApiPaths.PortfolioMargin, "F", (c, a) => ErrorOf(c.GetMarginAsync(a))),
        Case("GetAccountBalance", ShioajiApiPaths.PortfolioAccountBalance, "S", (c, a) => ErrorOf(c.GetAccountBalanceAsync(a))),
    ];

    // ───────────────────────── 每個端點都適用 / applies to every endpoint ─────────────────────────

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointSendsOneRetryFreePostWithItsAccountType(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var error = await call(host.Portfolio, null);

        Assert.IsNull(error, $"{name}: {error}");
        Assert.HasCount(1, server.Requests);
        Assert.AreEqual("POST", server.Requests[0].Method);
        Assert.AreEqual(path, server.Requests[0].Path);

        using var body = TestJson.Parse(server.Requests[0].Body);

        Assert.AreEqual(accountType, body.RootElement.GetProperty("account_type").GetString(), name);
        Assert.IsFalse(body.RootElement.TryGetProperty("broker_id", out _), "no account given, none sent");
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointSendsAGivenAccount(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var error = await call(host.Portfolio, FullAccount);

        Assert.IsNull(error, $"{name}: {error}");

        using var body = TestJson.Parse(server.Requests[0].Body);

        Assert.AreEqual("9A95", body.RootElement.GetProperty("broker_id").GetString(), path);
        Assert.AreEqual("1234567", body.RootElement.GetProperty("account_id").GetString());
        Assert.AreEqual("A123456789", body.RootElement.GetProperty("person_id").GetString());
        Assert.AreEqual(accountType, body.RootElement.GetProperty("account_type").GetString());
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointMapsAnHttpErrorAndNeverRetries(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", path, 500, """{"detail":"upstream broke"}""");
        using var host = ShioajiTestHost.Create(server);

        var error = await call(host.Portfolio, null);

        Assert.IsNotNull(error, $"{name} ({accountType})");
        Assert.AreEqual("shioaji.http.500", error.Code);
        Assert.AreEqual(1, server.Count("POST", path), "a POST is never retried");
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointMapsAClientErrorAsAFailureToo(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", path, 422, """{"detail":"account_type must be S"}""");
        using var host = ShioajiTestHost.Create(server);

        var error = await call(host.Portfolio, null);

        Assert.IsNotNull(error, $"{name} ({accountType})");
        Assert.AreEqual("shioaji.http.422", error.Code);
        Assert.AreEqual(ErrorCategory.Validation, error.Category);
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointCountsAgainstThePortfolioBudget(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.PortfolioLimit = 1;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        var first = await call(host.Portfolio, null);
        var second = await call(host.Portfolio, null);

        Assert.IsNull(first, $"{name} ({accountType})");
        Assert.IsNotNull(second);
        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, second.Code);
        Assert.AreEqual(1, server.Count("POST", path), "the refused call never reached the server");
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointLeavesTheOtherBudgetsAlone(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.MarketQueryLimit = 1;
            o.RateBudget.OrderLimit = 1;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.IsNull(await call(host.Portfolio, null), $"{name} ({accountType}) {path}, attempt {attempt}");
        }

        Assert.IsTrue((await host.Budget.AcquireAsync(ShioajiBudgetKind.MarketQuery)).IsSuccess, "the market-query budget is untouched");
        Assert.IsTrue((await host.Budget.AcquireAsync(ShioajiBudgetKind.Order)).IsSuccess, "the order budget is untouched");
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointRefusesAHalfGivenAccountLocally(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.PortfolioLimit = 1;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        var brokerOnly = await call(host.Portfolio, new ShioajiPortfolioAccount { BrokerId = "9A95" });
        var accountOnly = await call(host.Portfolio, new ShioajiPortfolioAccount { AccountId = "1234567" });

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, brokerOnly?.Code, $"{name} ({accountType}) {path}");
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, accountOnly?.Code);
        Assert.IsEmpty(server.Requests);
        Assert.IsNull(await call(host.Portfolio, null), "the refusals consumed no budget");
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointPassesAThrottledServerThroughAsARateLimitedFailure(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", path, 429, """{"detail":"slow down"}""");
        using var host = ShioajiTestHost.Create(server);

        var error = await call(host.Portfolio, null);

        Assert.IsNotNull(error, $"{name} ({accountType})");
        Assert.AreEqual(ErrorCategory.RateLimited, error.Category);
        Assert.AreEqual(1, server.Count("POST", path), "a 429 is not retried either");
    }

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task EveryEndpointReportsAnUnreadableBodyAsAMalformedResponse(
        string name,
        string path,
        string accountType,
        Func<ShioajiPortfolioClient, ShioajiPortfolioAccount?, Task<Error?>> call)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", path, 200, "<html>not json</html>");
        using var host = ShioajiTestHost.Create(server);

        var error = await call(host.Portfolio, null);

        Assert.IsNotNull(error, $"{name} ({accountType})");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, error.Code);
    }

    // ───────────────────────── 庫存 / positions ─────────────────────────

    [TestMethod]
    public async Task ListStockPositionsSendsTheUnitAndMapsTheDocumentedFields()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListStockPositionsAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"S","unit":"Common"}""", server.Requests[0].Body);

        var row = result.GetValueOrThrow().Single();

        Assert.AreEqual(0L, row.Id, "an id of 0 is a real id and is not the same as absent");
        Assert.AreEqual("2330", row.Code);
        Assert.AreEqual(ShioajiAction.Buy, row.Direction);
        Assert.AreEqual(2m, row.Quantity);
        Assert.AreEqual(600.5m, row.Price);
        Assert.AreEqual(610m, row.LastPrice);
        Assert.AreEqual(18999m, row.Pnl);
        Assert.AreEqual(2m, row.YdQuantity);
        Assert.AreEqual(ShioajiPositionCondition.Cash, row.Condition);
        Assert.AreEqual(0m, row.MarginPurchaseAmount);
        Assert.AreEqual(0m, row.Collateral);
        Assert.AreEqual(0m, row.ShortSaleMargin);
        Assert.AreEqual(0m, row.Interest);
    }

    [TestMethod]
    public async Task ListStockPositionsSendsTheOddLotUnitAndOmitsAnUnknownUnit()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        await host.Portfolio.ListStockPositionsAsync(ShioajiPositionUnit.Share, FullAccount);
        await host.Portfolio.ListStockPositionsAsync(ShioajiPositionUnit.Unknown);

        Assert.AreEqual(
            """{"account_type":"S","unit":"Share","broker_id":"9A95","account_id":"1234567","person_id":"A123456789"}""",
            server.Requests[0].Body);
        Assert.AreEqual("""{"account_type":"S"}""", server.Requests[1].Body, "Unknown sends no unit and the server defaults");
    }

    [TestMethod]
    public async Task ListStockPositionsReadsStringNumbersTypePrefixesAndMissingFields()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", ShioajiApiPaths.PortfolioPositionUnit, 200, Fixture.Read("portfolio_positions_stock.json"));
        using var host = ShioajiTestHost.Create(server);

        var rows = (await host.Portfolio.ListStockPositionsAsync()).GetValueOrThrow();

        Assert.HasCount(2, rows);

        var first = rows[0];

        Assert.AreEqual(5L, first.Id);
        Assert.AreEqual(ShioajiAction.Sell, first.Direction, "the Action. prefix is stripped");
        Assert.AreEqual(ShioajiPositionCondition.MarginTrading, first.Condition, "the StockOrderCond. prefix is stripped");
        Assert.AreEqual(3m, first.Quantity, "a string-typed number is read");
        Assert.AreEqual(29.8m, first.Price, "29.8 is exactly 29.8m and never went through double");
        Assert.AreEqual(29.85m, first.LastPrice);
        Assert.AreEqual(-150.5m, first.Pnl);
        Assert.AreEqual(50000m, first.MarginPurchaseAmount);
        Assert.AreEqual(12.5m, first.Interest);

        var second = rows[1];

        Assert.IsNull(second.Id);
        Assert.AreEqual("2317", second.Code);
        Assert.AreEqual(ShioajiPositionCondition.Netting, second.Condition);
        Assert.IsNull(second.Price, "an absent price is null and never zero");
        Assert.IsNull(second.Pnl);
        Assert.IsNull(second.Interest);
    }

    [TestMethod]
    public async Task ListStockPositionsReadsColumnArrays()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", ShioajiApiPaths.PortfolioPositionUnit, 200, Fixture.Read("portfolio_positions_stock_columnar.json"));
        using var host = ShioajiTestHost.Create(server);

        var rows = (await host.Portfolio.ListStockPositionsAsync()).GetValueOrThrow();

        Assert.HasCount(2, rows);
        Assert.AreEqual("2330", rows[0].Code);
        Assert.AreEqual(600.5m, rows[0].Price);
        Assert.AreEqual(610m, rows[0].LastPrice);
        Assert.AreEqual(1m, rows[1].Quantity, "a string cell in a column array is read as a number");
        Assert.AreEqual(105.25m, rows[1].Price);
        Assert.IsNull(rows[1].LastPrice, "a null cell is absent");
        Assert.AreEqual(ShioajiAction.Sell, rows[1].Direction);
        Assert.AreEqual(ShioajiPositionCondition.ShortSelling, rows[1].Condition);
    }

    [TestMethod]
    public async Task ListStockPositionsOfAnEmptyPortfolioIsASuccessfulEmptyList()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        foreach (var body in new[] { "[]", "{}", "null", string.Empty })
        {
            server.Map("POST", ShioajiApiPaths.PortfolioPositionUnit, 200, body);

            var result = await host.Portfolio.ListStockPositionsAsync();

            Assert.IsTrue(result.IsSuccess, $"body '{body}': {result.Error}");
            Assert.IsEmpty(result.GetValueOrThrow(), $"body '{body}'");
        }
    }

    [TestMethod]
    public async Task ListFuturesPositionsMapsTheFuturePositionFieldsAndSendsNoUnit()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListFuturesPositionsAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"F"}""", server.Requests[0].Body);

        var row = result.GetValueOrThrow().Single();

        Assert.AreEqual(0L, row.Id);
        Assert.AreEqual("TXFK6", row.Code);
        Assert.AreEqual(ShioajiAction.Sell, row.Direction);
        Assert.AreEqual(1m, row.Quantity);
        Assert.AreEqual(23000m, row.Price);
        Assert.AreEqual(22950m, row.LastPrice);
        Assert.AreEqual(10000m, row.Pnl);
    }

    [TestMethod]
    public async Task ListStockPositionDetailsSendsTheDetailIdAndMapsTheDocumentedFields()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListStockPositionDetailsAsync(7);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"S","detail_id":7}""", server.Requests[0].Body);

        var row = result.GetValueOrThrow().Single();

        Assert.AreEqual(new DateOnly(2026, 10, 30), row.Date);
        Assert.AreEqual("2330", row.Code);
        Assert.AreEqual(2m, row.Quantity);
        Assert.AreEqual(600.5m, row.Price);
        Assert.AreEqual(610m, row.LastPrice);
        Assert.AreEqual("AB123", row.Dseq);
        Assert.AreEqual(ShioajiAction.Buy, row.Direction);
        Assert.AreEqual(18999m, row.Pnl);
        Assert.AreEqual("TWD", row.Currency);
        Assert.AreEqual(1711m, row.Fee);
        Assert.AreEqual(ShioajiPositionCondition.Cash, row.Condition);
        Assert.AreEqual(0m, row.ExDividends);
        Assert.AreEqual(0m, row.Interest);
        Assert.AreEqual(0m, row.MarginTradingAmount);
        Assert.AreEqual(0m, row.Collateral);
    }

    [TestMethod]
    public async Task ListFuturesPositionDetailsMapsTheDocumentedFields()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListFuturesPositionDetailsAsync(0);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"F","detail_id":0}""", server.Requests[0].Body, "a detail id of 0 is sent");

        var row = result.GetValueOrThrow().Single();

        Assert.AreEqual(new DateOnly(2026, 10, 30), row.Date);
        Assert.AreEqual("TXFK6", row.Code);
        Assert.AreEqual(1m, row.Quantity);
        Assert.AreEqual("FA001", row.Dseq);
        Assert.AreEqual(ShioajiAction.Sell, row.Direction);
        Assert.AreEqual(10000m, row.Pnl);
        Assert.AreEqual(40m, row.Fee);
        Assert.AreEqual(1m, row.EntryQuantity);
    }

    // ───────────────────────── 已實現損益 / realized profit and loss ─────────────────────────

    [TestMethod]
    public async Task ListStockProfitLossSendsTheDatesAndUnitAndMapsTheRows()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListStockProfitLossAsync(Begin, End);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"S","begin_date":"2026-10-01","end_date":"2026-10-31","unit":"Common"}""", server.Requests[0].Body);

        var row = result.GetValueOrThrow().Single();

        Assert.AreEqual(1L, row.Id);
        Assert.AreEqual("2330", row.Code);
        Assert.AreEqual(1m, row.Quantity);
        Assert.AreEqual(4500m, row.Pnl);
        Assert.AreEqual(new DateOnly(2026, 10, 30), row.Date);
        Assert.AreEqual("AB124", row.Dseq);
        Assert.AreEqual(605m, row.Price);
        Assert.AreEqual(0.75m, row.PrRatio);
        Assert.AreEqual(ShioajiPositionCondition.Cash, row.Condition);
        Assert.AreEqual("000123", row.Seqno);
    }

    [TestMethod]
    public async Task ListStockProfitLossReadsAnObjectWrappingTheArray()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", ShioajiApiPaths.PortfolioProfitLoss, 200, Fixture.Read("portfolio_profit_loss_stock_wrapped.json"));
        using var host = ShioajiTestHost.Create(server);

        var rows = (await host.Portfolio.ListStockProfitLossAsync(Begin, End, ShioajiPositionUnit.Share)).GetValueOrThrow();

        Assert.HasCount(2, rows);
        Assert.AreEqual(4500.25m, rows[0].Pnl);
        Assert.AreEqual(new DateOnly(2026, 10, 29), rows[1].Date, "yyyyMMdd is read");
        Assert.AreEqual(-87.5m, rows[1].Pnl, "a string-typed negative is read");
        Assert.AreEqual(ShioajiPositionCondition.Emerging, rows[1].Condition);
        Assert.IsNull(rows[1].Price);
        Assert.IsNull(rows[1].Seqno);
        Assert.Contains("\"unit\":\"Share\"", server.Requests[0].Body);
    }

    [TestMethod]
    public async Task ListFuturesProfitLossSendsNoUnitAndMapsTheRows()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListFuturesProfitLossAsync(Begin, End);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"F","begin_date":"2026-10-01","end_date":"2026-10-31"}""", server.Requests[0].Body);

        var row = result.GetValueOrThrow().Single();

        Assert.AreEqual(2L, row.Id);
        Assert.AreEqual("TXFK6", row.Code);
        Assert.AreEqual(8000m, row.Pnl);
        Assert.AreEqual(23000m, row.EntryPrice);
        Assert.AreEqual(23040m, row.CoverPrice);
        Assert.AreEqual(10m, row.Tax);
        Assert.AreEqual(40m, row.Fee);
        Assert.AreEqual(ShioajiAction.Buy, row.Direction);
    }

    [TestMethod]
    public async Task ProfitLossQueriesRefuseAnInvertedRangeLocally()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.PortfolioLimit = 1;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        var stock = await host.Portfolio.ListStockProfitLossAsync(End, Begin);
        var futures = await host.Portfolio.ListFuturesProfitLossAsync(End, Begin);
        var stockSummary = await host.Portfolio.GetStockProfitLossSummaryAsync(End, Begin);
        var futuresSummary = await host.Portfolio.GetFuturesProfitLossSummaryAsync(End, Begin);

        foreach (var error in new[] { stock.Error, futures.Error, stockSummary.Error, futuresSummary.Error })
        {
            Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, error?.Code);
        }

        Assert.IsEmpty(server.Requests);
        Assert.IsTrue((await host.Portfolio.ListStockProfitLossAsync(Begin, Begin)).IsSuccess, "one single day is a valid range and the refusals took no budget");
    }

    [TestMethod]
    public async Task ListStockProfitLossDetailsCarriesTheTradeType()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListStockProfitLossDetailsAsync(3);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"S","detail_id":3,"unit":"Common"}""", server.Requests[0].Body);

        var row = result.GetValueOrThrow().Single();

        Assert.AreEqual(new DateOnly(2026, 10, 30), row.Date);
        Assert.AreEqual("2330", row.Code);
        Assert.AreEqual(1m, row.Quantity);
        Assert.AreEqual("AB124", row.Dseq);
        Assert.AreEqual(855m, row.Fee);
        Assert.AreEqual(1815m, row.Tax);
        Assert.AreEqual("TWD", row.Currency);
        Assert.AreEqual(605m, row.Price);
        Assert.AreEqual(600500m, row.Cost);
        Assert.AreEqual(0m, row.RepMarginTradingAmount);
        Assert.AreEqual(0m, row.RepCollateral);
        Assert.AreEqual(0m, row.RepMargin);
        Assert.AreEqual(0m, row.ShortSellingFee);
        Assert.AreEqual(0m, row.ExDividendAmount);
        Assert.AreEqual(0m, row.Interest);
        Assert.AreEqual(ShioajiProfitLossTradeType.DayTrade, row.TradeType);
        Assert.AreEqual(ShioajiPositionCondition.Cash, row.Condition);
    }

    [TestMethod]
    public async Task ListStockProfitLossDetailsReadsAnOrdinaryTradeTypeAndAnUnknownOne()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", ShioajiApiPaths.PortfolioProfitLossDetail, 200, """
            [{"code":"2330","quantity":1,"trade_type":"TradeType.Common"},{"code":"2317","quantity":1,"trade_type":"Mystery"},{"code":"0050","quantity":1}]
            """);
        using var host = ShioajiTestHost.Create(server);

        var rows = (await host.Portfolio.ListStockProfitLossDetailsAsync(1, ShioajiPositionUnit.Unknown)).GetValueOrThrow();

        Assert.AreEqual(ShioajiProfitLossTradeType.Common, rows[0].TradeType);
        Assert.AreEqual(ShioajiProfitLossTradeType.Unknown, rows[1].TradeType);
        Assert.AreEqual(ShioajiProfitLossTradeType.Unknown, rows[2].TradeType);
        Assert.IsNull(rows[2].Fee);
        Assert.AreEqual("""{"account_type":"S","detail_id":1}""", server.Requests[0].Body, "Unknown sends no unit");
    }

    [TestMethod]
    public async Task ListFuturesProfitLossDetailsMapsTheDocumentedFields()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListFuturesProfitLossDetailsAsync(3);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"F","detail_id":3}""", server.Requests[0].Body);

        var row = result.GetValueOrThrow().Single();

        Assert.AreEqual(new DateOnly(2026, 10, 30), row.Date);
        Assert.AreEqual("TXFK6", row.Code);
        Assert.AreEqual(1m, row.Quantity);
        Assert.AreEqual("FA002", row.Dseq);
        Assert.AreEqual(40m, row.Fee);
        Assert.AreEqual(10m, row.Tax);
        Assert.AreEqual("TWD", row.Currency);
        Assert.AreEqual(ShioajiAction.Buy, row.Direction);
        Assert.AreEqual(new DateOnly(2026, 10, 29), row.EntryDate);
        Assert.AreEqual(1m, row.EntryQuantity);
        Assert.AreEqual("000045", row.EntrySeqno);
        Assert.AreEqual(23000m, row.EntryPrice);
        Assert.AreEqual(23040m, row.CoverPrice);
        Assert.AreEqual(8000m, row.Pnl);
    }

    [TestMethod]
    public async Task GetStockProfitLossSummaryMapsTheItemsAndTheTotal()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.GetStockProfitLossSummaryAsync(Begin, End);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"S","begin_date":"2026-10-01","end_date":"2026-10-31"}""", server.Requests[0].Body);

        var summary = result.GetValueOrThrow();
        var item = summary.Items.Single();

        Assert.AreEqual("2330", item.Code);
        Assert.AreEqual(1m, item.Quantity);
        Assert.AreEqual(600.5m, item.EntryPrice);
        Assert.AreEqual(605m, item.CoverPrice);
        Assert.AreEqual(4500m, item.Pnl);
        Assert.AreEqual("TWD", item.Currency);
        Assert.AreEqual(600500m, item.EntryCost);
        Assert.AreEqual(605000m, item.CoverCost);
        Assert.AreEqual(600855m, item.BuyCost);
        Assert.AreEqual(603185m, item.SellCost);
        Assert.AreEqual(0.38m, item.PrRatio);
        Assert.AreEqual(ShioajiPositionCondition.Cash, item.Condition);

        Assert.IsNotNull(summary.Total);
        Assert.AreEqual(600500m, summary.Total.EntryAmount);
        Assert.AreEqual(605000m, summary.Total.CoverAmount);
        Assert.AreEqual(1m, summary.Total.Quantity);
        Assert.AreEqual(600855m, summary.Total.BuyCost);
        Assert.AreEqual(603185m, summary.Total.SellCost);
        Assert.AreEqual(2330m, summary.Total.Pnl);
        Assert.AreEqual(0.39m, summary.Total.PrRatio);
    }

    [TestMethod]
    public async Task GetStockProfitLossSummaryReadsMissingFieldsAndStringNumbers()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", ShioajiApiPaths.PortfolioProfitLossSummary, 200, Fixture.Read("portfolio_profitloss_sum_stock.json"));
        using var host = ShioajiTestHost.Create(server);

        var summary = (await host.Portfolio.GetStockProfitLossSummaryAsync(Begin, End)).GetValueOrThrow();

        Assert.HasCount(2, summary.Items);
        Assert.AreEqual(2m, summary.Items[1].Quantity);
        Assert.AreEqual(-87.5m, summary.Items[1].Pnl);
        Assert.IsNull(summary.Items[1].EntryPrice, "absent is null and never zero");
        Assert.IsNull(summary.Items[1].PrRatio);
        Assert.AreEqual(ShioajiPositionCondition.Unknown, summary.Items[1].Condition);
        Assert.AreEqual(600855m, summary.Total!.BuyCost, "a string-typed number in the total is read");
    }

    [TestMethod]
    public async Task GetFuturesProfitLossSummaryMapsTheItemsAndTheTotal()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.GetFuturesProfitLossSummaryAsync(Begin, End);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());

        var summary = result.GetValueOrThrow();
        var item = summary.Items.Single();

        Assert.AreEqual("TXFK6", item.Code);
        Assert.AreEqual(1m, item.Quantity);
        Assert.AreEqual(23000m, item.EntryPrice);
        Assert.AreEqual(23040m, item.CoverPrice);
        Assert.AreEqual(8000m, item.Pnl);
        Assert.AreEqual("TWD", item.Currency);
        Assert.AreEqual(ShioajiAction.Buy, item.Direction);
        Assert.AreEqual(10m, item.Tax);
        Assert.AreEqual(40m, item.Fee);
        Assert.AreEqual(7950m, summary.Total!.Pnl);
    }

    // ───────────────────────── 交割、額度、保證金、餘額 / settlements, limits, margin, balance ─────────────────────────

    [TestMethod]
    public async Task ListSettlementsMapsTheDocumentedExampleAndANegativeAmount()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.ListSettlementsAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"S"}""", server.Requests[0].Body);

        var rows = result.GetValueOrThrow();

        Assert.HasCount(2, rows);
        Assert.AreEqual(new DateOnly(2026, 5, 21), rows[0].Date);
        Assert.AreEqual(100000m, rows[0].Amount);
        Assert.AreEqual(0, rows[0].TPlus);
        Assert.AreEqual(-2500.5m, rows[1].Amount);
        Assert.AreEqual(1, rows[1].TPlus);
    }

    [TestMethod]
    public async Task GetTradingLimitsMapsTheThreeLimitSets()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.GetTradingLimitsAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"S"}""", server.Requests[0].Body);

        var limits = result.GetValueOrThrow();

        Assert.AreEqual(5000000m, limits.TradingLimit);
        Assert.AreEqual(1200000m, limits.TradingUsed);
        Assert.AreEqual(3800000m, limits.TradingAvailable);
        Assert.AreEqual(2000000m, limits.MarginLimit);
        Assert.AreEqual(0m, limits.MarginUsed);
        Assert.AreEqual(2000000m, limits.MarginAvailable);
        Assert.AreEqual(1000000m, limits.ShortLimit);
        Assert.AreEqual(0m, limits.ShortUsed);
        Assert.AreEqual(1000000m, limits.ShortAvailable);
    }

    [TestMethod]
    public async Task GetMarginMapsEveryDocumentedField()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.GetMarginAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"F"}""", server.Requests[0].Body);

        var margin = result.GetValueOrThrow();

        Assert.AreEqual(500000m, margin.YesterdayBalance);
        Assert.AreEqual(510000m, margin.TodayBalance);
        Assert.AreEqual(0m, margin.DepositWithdrawal);
        Assert.AreEqual(40m, margin.Fee);
        Assert.AreEqual(10m, margin.Tax);
        Assert.AreEqual(184000m, margin.InitialMargin);
        Assert.AreEqual(141000m, margin.MaintenanceMargin);
        Assert.AreEqual(0m, margin.MarginCall);
        Assert.AreEqual(277.2m, margin.RiskIndicator);
        Assert.AreEqual(0m, margin.RoyaltyRevenueExpenditure);
        Assert.AreEqual(509950m, margin.Equity);
        Assert.AreEqual(509950m, margin.EquityAmount);
        Assert.AreEqual(0m, margin.OptionOpenBuyMarketValue);
        Assert.AreEqual(0m, margin.OptionOpenSellMarketValue);
        Assert.AreEqual(0m, margin.OptionOpenPosition);
        Assert.AreEqual(0m, margin.OptionSettleProfitLoss);
        Assert.AreEqual(10000m, margin.FutureOpenPosition);
        Assert.AreEqual(10000m, margin.TodayFutureOpenPosition);
        Assert.AreEqual(8000m, margin.FutureSettleProfitLoss);
        Assert.AreEqual(325950m, margin.AvailableMargin);
        Assert.AreEqual(0m, margin.PlusMargin);
        Assert.AreEqual(0m, margin.PlusMarginIndicator);
        Assert.AreEqual(0m, margin.SecurityCollateralAmount);
        Assert.AreEqual(0m, margin.OrderMarginPremium);
        Assert.AreEqual(0m, margin.CollateralAmount);
    }

    [TestMethod]
    public async Task GetAccountBalanceMapsTheDocumentedExample()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.GetAccountBalanceAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual("""{"account_type":"S"}""", server.Requests[0].Body);

        var balance = result.GetValueOrThrow();

        Assert.AreEqual(100000m, balance.Balance);
        Assert.IsNull(balance.ErrorMessage, "an empty errmsg is no message");

        // 無時區的牆上時間視為台北時間,減 8 小時;微秒保留到 100 奈秒的刻度。
        // A wall-clock time without an offset is Taipei time, minus 8 hours; the microseconds are kept to the 100 ns tick.
        Assert.AreEqual(
            new DateTimeOffset(2026, 5, 19, 1, 21, 10, TimeSpan.Zero).AddTicks(857_110),
            balance.QueriedAt);
    }

    [TestMethod]
    public async Task GetAccountBalanceKeepsAnInBandErrorMessageAsASuccess()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", ShioajiApiPaths.PortfolioAccountBalance, 200, """{"errmsg":"bank not supported","date":"2026-05-19 09:21:10"}""");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Portfolio.GetAccountBalanceAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.IsNull(result.GetValueOrThrow().Balance, "no balance is null and never zero");
        Assert.AreEqual("bank not supported", result.GetValueOrThrow().ErrorMessage);
    }

    // ───────────────────────── 註冊與建構 / registration and construction ─────────────────────────

    [TestMethod]
    public void TheClientIsRegisteredAsASingleton()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.AreSame(host.Portfolio, host.Services.GetRequiredService<ShioajiPortfolioClient>());
    }

    [TestMethod]
    public void TheConstructorRejectsNulls()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiPortfolioClient(null!, host.Budget));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiPortfolioClient(host.Api, null!));
        Assert.IsNotNull(new ShioajiPortfolioClient(host.Api, host.Budget));
    }

    [TestMethod]
    public async Task AllFourteenQueriesShareOneBudgetWindow()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.PortfolioLimit = 3;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });

        Assert.IsTrue((await host.Portfolio.ListSettlementsAsync()).IsSuccess);
        Assert.IsTrue((await host.Portfolio.GetMarginAsync()).IsSuccess);
        Assert.IsTrue((await host.Portfolio.ListStockPositionsAsync()).IsSuccess);

        var fourth = await host.Portfolio.GetAccountBalanceAsync();

        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, fourth.Error!.Code, "different endpoints draw on the same Portfolio budget");
        Assert.AreEqual(3, server.Requests.Count);
    }
}
