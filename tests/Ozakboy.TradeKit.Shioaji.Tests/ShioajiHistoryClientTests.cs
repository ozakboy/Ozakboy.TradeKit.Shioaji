using System.Text;
using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.History;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiHistoryClientTests
{
    private static ShioajiContractRef Stock(string code = "2330") =>
        new() { SecurityType = ShioajiSecurityType.Stock, Exchange = "TSE", Code = code };

    private static JsonDocument SentBody(FakeShioajiHttpServer server, string path) =>
        JsonDocument.Parse(server.Requests.Single(r => r.Path == path).Body);

    // ───────────────────────────── kbars ─────────────────────────────

    [TestMethod]
    public async Task GetKbars_MapsTheColumnArrays_ConvertsTaipeiTimeToUtc_AndSendsTheDocumentedBody()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/kbars", 200, Fixture.Read("kbars.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetKbarsAsync(Stock(), new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 29));

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var page = result.GetValueOrThrow();

        Assert.HasCount(3, page.Items);
        Assert.IsFalse(page.IsEmpty);
        Assert.IsFalse(page.PossiblyTrafficExhausted);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(Fixture.Read("kbars.json")), page.ResponseBytes, "the page carries the size of the response");

        var first = page.Items[0];
        Assert.AreEqual(new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero), first.Time, "09:00 Taipei is 01:00 UTC");
        Assert.AreEqual(1000m, first.Open);
        Assert.AreEqual(1002m, first.High);
        Assert.AreEqual(999.5m, first.Low);
        Assert.AreEqual(1001.5m, first.Close);
        Assert.AreEqual(120m, first.Volume);
        Assert.AreEqual(120_180_000m, first.Amount);

        Assert.AreEqual(1001.5m, page.Items[1].Open);
        Assert.AreEqual(1002m, page.Items[2].Open, "a string-typed number must be read");
        Assert.AreEqual(60m, page.Items[2].Volume);

        using var body = SentBody(server, "/api/v1/data/kbars");
        var contract = body.RootElement.GetProperty("contract");
        Assert.AreEqual("STK", contract.GetProperty("security_type").GetString());
        Assert.AreEqual("TSE", contract.GetProperty("exchange").GetString());
        Assert.AreEqual("2330", contract.GetProperty("code").GetString());
        Assert.AreEqual("2026-10-29", body.RootElement.GetProperty("start").GetString());
        Assert.AreEqual("2026-10-29", body.RootElement.GetProperty("end").GetString());
    }

    [TestMethod]
    public async Task GetKbars_AFutureIsSentWithItsOwnSecurityType_AndAContractCanBeConverted()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        var contract = new ShioajiContract
        {
            SecurityType = ShioajiSecurityType.Future,
            Region = "TW",
            Exchange = "TAIFEX",
            Code = "TXFR1",
            TargetCode = "TXFK6",
        };

        var result = await host.History.GetKbarsAsync(contract.ToRef(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2));

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());

        using var body = SentBody(server, "/api/v1/data/kbars");
        var sent = body.RootElement.GetProperty("contract");
        Assert.AreEqual("FUT", sent.GetProperty("security_type").GetString());
        Assert.AreEqual("TXFR1", sent.GetProperty("code").GetString(), "history is queried by the code itself, not the target_code");
        Assert.AreEqual(ShioajiContractRef.From(contract), contract.ToRef());
    }

    [TestMethod]
    public async Task GetKbars_ThirtyDaysIsAllowed_ThirtyOneIsRefusedLocally_AndNothingIsSent()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var thirty = await host.History.GetKbarsAsync(Stock(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30));
        Assert.IsTrue(thirty.IsSuccess, thirty.Error?.ToString());
        Assert.AreEqual(1, server.Count("POST", "/api/v1/data/kbars"));

        var thirtyOne = await host.History.GetKbarsAsync(Stock(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

        Assert.IsTrue(thirtyOne.IsFailure);
        Assert.AreEqual("shioaji.history.range_too_long", thirtyOne.Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.HistoryRangeTooLong, thirtyOne.Error.Code);
        Assert.AreEqual(ErrorCategory.Validation, thirtyOne.Error.Category);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/data/kbars"), "a refused range must never reach the server");

        var year = await host.History.GetKbarsAsync(Stock(), new DateOnly(2025, 10, 30), new DateOnly(2026, 10, 30));
        Assert.AreEqual(ShioajiErrorCodes.HistoryRangeTooLong, year.Error!.Code);
    }

    [TestMethod]
    public async Task GetKbars_LocalArgumentChecks()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        var day = new DateOnly(2026, 10, 29);

        var reversed = await host.History.GetKbarsAsync(Stock(), day.AddDays(1), day);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, reversed.Error!.Code);

        var unknownType = await host.History.GetKbarsAsync(new ShioajiContractRef { SecurityType = ShioajiSecurityType.Unknown, Exchange = "TSE", Code = "2330" }, day, day);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, unknownType.Error!.Code);

        var blankExchange = await host.History.GetKbarsAsync(new ShioajiContractRef { SecurityType = ShioajiSecurityType.Stock, Exchange = " ", Code = "2330" }, day, day);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, blankExchange.Error!.Code);

        var blankCode = await host.History.GetKbarsAsync(new ShioajiContractRef { SecurityType = ShioajiSecurityType.Stock, Exchange = "TSE", Code = "" }, day, day);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, blankCode.Error!.Code);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.History.GetKbarsAsync(null!, day, day));
        Assert.IsEmpty(server.Requests, "every local refusal happens before the request");
    }

    [TestMethod]
    [DataRow(400, "shioaji.http.400", ErrorCategory.Validation)]
    [DataRow(404, "shioaji.http.404", ErrorCategory.NotFound)]
    [DataRow(429, "shioaji.http.429", ErrorCategory.RateLimited)]
    [DataRow(500, "shioaji.http.500", ErrorCategory.Unavailable)]
    public async Task GetKbars_NonSuccessStatus_IsMappedAndNeverRetried(int status, string code, ErrorCategory category)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/kbars", status, """{"detail":"nope"}""");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetKbarsAsync(Stock(), new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 29));

        Assert.AreEqual(code, result.Error!.Code);
        Assert.AreEqual(category, result.Error.Category);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/data/kbars"), "a POST is never retried");
    }

    [TestMethod]
    public async Task GetKbars_ConsumesTheMarketQueryBudget_AndLocalRefusalsDoNot()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.MarketQueryLimit = 1;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });
        var day = new DateOnly(2026, 10, 29);

        // 被本地拒絕的呼叫不佔額度。
        // A call refused locally takes no unit.
        Assert.IsTrue((await host.History.GetKbarsAsync(Stock(), day.AddDays(1), day)).IsFailure);
        Assert.IsTrue((await host.History.GetKbarsAsync(Stock(), day.AddDays(-60), day)).IsFailure);

        var first = await host.History.GetKbarsAsync(Stock(), day, day);
        var second = await host.History.GetKbarsAsync(Stock(), day, day);

        Assert.IsTrue(first.IsSuccess, first.Error?.ToString());
        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, second.Error!.Code);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/data/kbars"), "the call the budget refused never reaches the broker");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("  ")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("""{"datetime":null,"Open":null}""")]
    [DataRow("""{"datetime":[],"Open":[],"High":[],"Low":[],"Close":[],"Volume":[],"Amount":[]}""")]
    public async Task GetKbars_EmptyAnswers_AreASuccessfulEmptyPage(string body)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/kbars", 200, body);
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetKbarsAsync(Stock(), new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 29));

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.IsTrue(result.GetValueOrThrow().IsEmpty);
        Assert.IsTrue(result.GetValueOrThrow().PossiblyTrafficExhausted, "with no usage report an empty answer cannot rule out exhaustion");
    }

    [TestMethod]
    public async Task GetKbars_EmptyAnswer_PossiblyTrafficExhausted_FollowsTheServersReportedUsage()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/kbars", 200, "{}");
        using var host = ShioajiTestHost.Create(server);
        var day = new DateOnly(2026, 10, 29);

        // 還沒問過 server 用量:無從排除。
        // Nobody asked the server for its usage yet: nothing can be ruled out.
        Assert.IsTrue((await host.History.GetKbarsAsync(Stock(), day, day)).GetValueOrThrow().PossiblyTrafficExhausted);

        // server 說還有兩 GB 多:空回應不是流量用完。
        // The server says over two gigabytes remain: an empty answer is not exhaustion.
        server.Map("GET", "/api/v1/auth/usage", 200, """{"connections":1,"bytes":1024,"limit_bytes":2147483648,"remaining_bytes":2147482624}""");
        Assert.IsTrue((await host.Auth.GetUsageAsync()).IsSuccess);
        Assert.IsFalse((await host.History.GetKbarsAsync(Stock(), day, day)).GetValueOrThrow().PossiblyTrafficExhausted);

        // server 說只剩 0:空回應很可能就是用完了。
        // The server says nothing remains: an empty answer is very likely exhaustion.
        server.Map("GET", "/api/v1/auth/usage", 200, """{"connections":1,"bytes":2147483648,"limit_bytes":2147483648,"remaining_bytes":0}""");
        Assert.IsTrue((await host.Auth.GetUsageAsync()).IsSuccess);
        Assert.IsTrue((await host.History.GetKbarsAsync(Stock(), day, day)).GetValueOrThrow().PossiblyTrafficExhausted);
    }

    [TestMethod]
    public async Task GetKbars_NonEmptyAnswer_IsNeverFlaggedAsExhausted()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var page = (await host.History.GetKbarsAsync(Stock(), new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 29))).GetValueOrThrow();

        Assert.HasCount(2, page.Items, "the fake's default sample");
        Assert.IsFalse(page.PossiblyTrafficExhausted);
    }

    [TestMethod]
    public async Task GetKbars_ColumnsOfDifferentLengths_FailTheWholeResponse()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/kbars", 200, """
            {"datetime":["2026-10-29T09:00:00","2026-10-29T09:01:00"],"Open":[1,2],"High":[1,2],"Low":[1,2],"Close":[1],"Volume":[1,2],"Amount":[1,2]}
            """);
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetKbarsAsync(Stock(), new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 29));

        Assert.AreEqual("shioaji.history.malformed", result.Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.HistoryMalformed, result.Error.Code);
        Assert.Contains("Close=1", result.Error.Message, "the message says which column is short");
    }

    [TestMethod]
    public async Task GetKbars_AnUnreadableCellFailsWithTheColumnAndTheRow_AndNeverBecomesZero()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/kbars", 200, """
            {"datetime":["2026-10-29T09:00:00","2026-10-29T09:01:00"],"Open":[1,2],"High":[1,2],"Low":[1,2],"Close":[1,null],"Volume":[1,2],"Amount":[1,2]}
            """);
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetKbarsAsync(Stock(), new DateOnly(2026, 10, 29), new DateOnly(2026, 10, 29));

        Assert.AreEqual(ShioajiErrorCodes.MissingField, result.Error!.Code);
        Assert.IsTrue(result.Error.TryGetData(ShioajiErrorDataKeys.Field, out var field));
        Assert.AreEqual("Close", field);
        Assert.IsTrue(result.Error.TryGetData(ShioajiErrorDataKeys.Row, out var row));
        Assert.AreEqual("1", row);
    }

    // ───────────────────────────── ticks ─────────────────────────────

    [TestMethod]
    public async Task GetTicks_AllDay_MapsTheColumnArrays_AndSendsOnlyTheFieldsOfItsType()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/ticks", 200, Fixture.Read("ticks.json"));
        using var host = ShioajiTestHost.Create(server);

        // LastCount 帶了也不該被送出:它和 AllDay 無關。
        // The LastCount must not be sent either: it has nothing to do with AllDay.
        var query = new ShioajiTicksQuery { Type = ShioajiTicksQueryType.AllDay, LastCount = 5 };
        var result = await host.History.GetTicksAsync(Stock(), new DateOnly(2026, 10, 29), query);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var ticks = result.GetValueOrThrow().Items;

        Assert.HasCount(3, ticks);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 29, 1, 0, 0, TimeSpan.Zero).AddTicks(1_234_567), ticks[0].Time, "fractions finer than a tick are dropped");
        Assert.AreEqual(1000.5m, ticks[0].Close);
        Assert.AreEqual(3m, ticks[0].Volume);
        Assert.AreEqual(1000m, ticks[0].BidPrice);
        Assert.AreEqual(40m, ticks[0].BidVolume);
        Assert.AreEqual(1000.5m, ticks[0].AskPrice);
        Assert.AreEqual(18m, ticks[0].AskVolume);
        Assert.AreEqual(1, ticks[0].TickType);
        Assert.IsNull(ticks[2].AskPrice, "a null optional cell stays null");
        Assert.IsNull(ticks[2].AskVolume);
        Assert.AreEqual(2, ticks[2].TickType);

        using var body = SentBody(server, "/api/v1/data/ticks");
        Assert.AreEqual("AllDay", body.RootElement.GetProperty("query_type").GetString());
        Assert.AreEqual("2026-10-29", body.RootElement.GetProperty("date").GetString());
        Assert.AreEqual("2330", body.RootElement.GetProperty("contract").GetProperty("code").GetString());
        Assert.IsFalse(body.RootElement.TryGetProperty("last_cnt", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("time_start", out _));
    }

    [TestMethod]
    public async Task GetTicks_RangeTimeAndLastCount_SendTheirOwnFields()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        var day = new DateOnly(2026, 10, 29);

        var range = await host.History.GetTicksAsync(Stock(), day, ShioajiTicksQuery.Range(new TimeOnly(9, 0), new TimeOnly(9, 30, 15)));
        var last = await host.History.GetTicksAsync(Stock(), day, ShioajiTicksQuery.Last(5));

        Assert.IsTrue(range.IsSuccess, range.Error?.ToString());
        Assert.IsTrue(last.IsSuccess, last.Error?.ToString());

        using var first = JsonDocument.Parse(server.Requests[0].Body);
        Assert.AreEqual("RangeTime", first.RootElement.GetProperty("query_type").GetString());
        Assert.AreEqual("09:00:00", first.RootElement.GetProperty("time_start").GetString());
        Assert.AreEqual("09:30:15", first.RootElement.GetProperty("time_end").GetString());
        Assert.IsFalse(first.RootElement.TryGetProperty("last_cnt", out _));

        using var second = JsonDocument.Parse(server.Requests[1].Body);
        Assert.AreEqual("LastCount", second.RootElement.GetProperty("query_type").GetString());
        Assert.AreEqual(5, second.RootElement.GetProperty("last_cnt").GetInt32());
        Assert.IsFalse(second.RootElement.TryGetProperty("time_start", out _));
    }

    [TestMethod]
    public async Task GetTicks_LocalChecks_RefuseBeforeAnythingIsSent()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        var day = new DateOnly(2026, 10, 29);

        var cases = new[]
        {
            new ShioajiTicksQuery { Type = ShioajiTicksQueryType.Unknown },
            new ShioajiTicksQuery { Type = ShioajiTicksQueryType.RangeTime },
            new ShioajiTicksQuery { Type = ShioajiTicksQueryType.RangeTime, TimeStart = new TimeOnly(9, 0) },
            new ShioajiTicksQuery { Type = ShioajiTicksQueryType.RangeTime, TimeEnd = new TimeOnly(9, 0) },
            ShioajiTicksQuery.Range(new TimeOnly(10, 0), new TimeOnly(9, 0)),
            ShioajiTicksQuery.Range(new TimeOnly(9, 0), new TimeOnly(9, 0)),
            new ShioajiTicksQuery { Type = ShioajiTicksQueryType.LastCount },
            ShioajiTicksQuery.Last(0),
            ShioajiTicksQuery.Last(-3),
        };

        foreach (var query in cases)
        {
            var result = await host.History.GetTicksAsync(Stock(), day, query);

            Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, result.Error!.Code, query.ToString());
        }

        var badContract = await host.History.GetTicksAsync(new ShioajiContractRef { SecurityType = ShioajiSecurityType.Unknown, Exchange = "TSE", Code = "2330" }, day, ShioajiTicksQuery.AllDay);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, badContract.Error!.Code);

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.History.GetTicksAsync(Stock(), day, null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.History.GetTicksAsync(null!, day, ShioajiTicksQuery.AllDay));
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task GetTicks_FailuresAndEmptyAnswers()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/ticks", 503, "busy");
        using var host = ShioajiTestHost.Create(server);
        var day = new DateOnly(2026, 10, 29);

        var failed = await host.History.GetTicksAsync(Stock(), day, ShioajiTicksQuery.AllDay);
        Assert.AreEqual("shioaji.http.503", failed.Error!.Code);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/data/ticks"));

        server.Map("POST", "/api/v1/data/ticks", 200, """{"datetime":[],"close":[],"volume":[]}""");
        var empty = (await host.History.GetTicksAsync(Stock(), day, ShioajiTicksQuery.AllDay)).GetValueOrThrow();
        Assert.IsTrue(empty.IsEmpty);

        server.Map("POST", "/api/v1/data/ticks", 200, """{"datetime":["2026-10-29T09:00:00"],"close":[1,2],"volume":[1]}""");
        Assert.AreEqual(ShioajiErrorCodes.HistoryMalformed, (await host.History.GetTicksAsync(Stock(), day, ShioajiTicksQuery.AllDay)).Error!.Code);

        server.Map("POST", "/api/v1/data/ticks", 200, """{"datetime":["2026-10-29T09:00:00"],"close":[1]}""");
        var missing = await host.History.GetTicksAsync(Stock(), day, ShioajiTicksQuery.AllDay);
        Assert.AreEqual(ShioajiErrorCodes.MissingField, missing.Error!.Code, "a required column that is absent while rows exist");
    }

    [TestMethod]
    public async Task GetTicks_OnlyTheThreeRequiredColumns_LeaveTheOptionalOnesNull()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/ticks", 200, """{"datetime":["2026-10-29T09:00:00"],"close":[29.8],"volume":[7]}""");
        using var host = ShioajiTestHost.Create(server);

        var tick = (await host.History.GetTicksAsync(Stock(), new DateOnly(2026, 10, 29), ShioajiTicksQuery.AllDay)).GetValueOrThrow().Items.Single();

        Assert.AreEqual(29.8m, tick.Close);
        Assert.IsNull(tick.BidPrice);
        Assert.IsNull(tick.AskVolume);
        Assert.IsNull(tick.TickType);
    }

    // ───────────────────────────── snapshots ─────────────────────────────

    [TestMethod]
    public async Task GetSnapshots_MapsEveryDocumentedFieldAndKeepsTheRest()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/snapshots", 200, Fixture.Read("snapshots.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetSnapshotsAsync([Stock("2330"), Stock("2317")]);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var items = result.GetValueOrThrow().Items;
        Assert.HasCount(2, items);

        var tsmc = items[0];
        Assert.AreEqual("2330", tsmc.Code);
        Assert.AreEqual("TSE", tsmc.Exchange);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 5, 30, 0, TimeSpan.Zero), tsmc.Time);
        Assert.AreEqual(1000.5m, tsmc.Open);
        Assert.AreEqual(1005m, tsmc.High);
        Assert.AreEqual(998.5m, tsmc.Low);
        Assert.AreEqual(1002m, tsmc.Close);
        Assert.AreEqual(1, tsmc.TickType);
        Assert.AreEqual(1.5m, tsmc.ChangePrice);
        Assert.AreEqual(0.15m, tsmc.ChangeRate);
        Assert.AreEqual(2, tsmc.ChangeType);
        Assert.AreEqual(1001.25m, tsmc.AveragePrice);
        Assert.AreEqual(2m, tsmc.Volume);
        Assert.AreEqual(2000m, tsmc.TotalVolume);
        Assert.AreEqual(2_004_000m, tsmc.Amount);
        Assert.AreEqual(2_004_000_000m, tsmc.TotalAmount, "a string-typed number must be read");
        Assert.AreEqual(18000m, tsmc.YesterdayVolume);
        Assert.AreEqual(0.11m, tsmc.VolumeRatio);
        Assert.AreEqual(1001.5m, tsmc.BuyPrice);
        Assert.AreEqual(40m, tsmc.BuyVolume);
        Assert.AreEqual(1002m, tsmc.SellPrice);
        Assert.AreEqual(18m, tsmc.SellVolume);
        Assert.AreEqual("kept in Raw", tsmc.Raw.GetProperty("unlisted_extra").GetString());

        var sparse = items[1];
        Assert.AreEqual(29.8m, sparse.Close);
        Assert.IsNull(sparse.Open);
        Assert.IsNull(sparse.TickType);

        using var body = SentBody(server, "/api/v1/data/snapshots");
        var sent = body.RootElement.GetProperty("contracts");
        Assert.AreEqual(2, sent.GetArrayLength());
        Assert.AreEqual("2317", sent[1].GetProperty("code").GetString());
        Assert.AreEqual("STK", sent[1].GetProperty("security_type").GetString());
    }

    [TestMethod]
    public async Task GetSnapshots_FiveHundredAreAllowed_FiveHundredAndOneAreRefusedLocally()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var allowed = Enumerable.Range(0, 500).Select(i => Stock((1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var tooMany = Enumerable.Range(0, 501).Select(i => Stock((1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();

        Assert.IsTrue((await host.History.GetSnapshotsAsync(allowed)).IsSuccess);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/data/snapshots"));

        var refused = await host.History.GetSnapshotsAsync(tooMany);

        Assert.AreEqual("shioaji.history.too_many_contracts", refused.Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.HistoryTooManyContracts, refused.Error.Code);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/data/snapshots"), "nothing was sent for the 501");
    }

    [TestMethod]
    public async Task GetSnapshots_LocalChecksAndFailures()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetSnapshotsAsync([])).Error!.Code);
        Assert.AreEqual(
            ShioajiErrorCodes.InvalidArgument,
            (await host.History.GetSnapshotsAsync([Stock(), new ShioajiContractRef { SecurityType = ShioajiSecurityType.Unknown, Exchange = "TSE", Code = "X" }])).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetSnapshotsAsync([Stock(), null!])).Error!.Code);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.History.GetSnapshotsAsync(null!));
        Assert.IsEmpty(server.Requests);

        server.Map("POST", "/api/v1/data/snapshots", 500, "{}");
        Assert.AreEqual("shioaji.http.500", (await host.History.GetSnapshotsAsync([Stock()])).Error!.Code);

        server.Map("POST", "/api/v1/data/snapshots", 200, """[{"code":"2330"}]""");
        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.History.GetSnapshotsAsync([Stock()])).Error!.Code, "close is required and never zero-filled");

        server.Map("POST", "/api/v1/data/snapshots", 200, "[1]");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.History.GetSnapshotsAsync([Stock()])).Error!.Code);

        server.Map("POST", "/api/v1/data/snapshots", 200, "42");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.History.GetSnapshotsAsync([Stock()])).Error!.Code);

        server.Map("POST", "/api/v1/data/snapshots", 200, """{"unexpected":true}""");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.History.GetSnapshotsAsync([Stock()])).Error!.Code);

        server.Map("POST", "/api/v1/data/snapshots", 200, "not json");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await host.History.GetSnapshotsAsync([Stock()])).Error!.Code);

        server.Map("POST", "/api/v1/data/snapshots", 200, """{"items":[{"code":"2330","close":1}],"total":1}""");
        Assert.HasCount(1, (await host.History.GetSnapshotsAsync([Stock()])).GetValueOrThrow().Items, "a wrapping object is tolerated");

        foreach (var empty in new[] { "", "null", "[]", "{}" })
        {
            server.Map("POST", "/api/v1/data/snapshots", 200, empty);
            var page = (await host.History.GetSnapshotsAsync([Stock()])).GetValueOrThrow();
            Assert.IsTrue(page.IsEmpty, $"'{empty}' is an empty answer");
            Assert.IsTrue(page.PossiblyTrafficExhausted);
        }
    }

    // ───────────────────────────── daily quotes ─────────────────────────────

    [TestMethod]
    public async Task GetDailyQuotes_UsesTheDefaultPath_MapsTheSixFieldsAndKeepsTheRestInRaw()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/daily_quotes", 200, Fixture.Read("daily_quotes.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetDailyQuotesAsync(new DateOnly(2026, 10, 30));

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var items = result.GetValueOrThrow().Items;
        Assert.HasCount(2, items);
        Assert.AreEqual("2330", items[0].Code);
        Assert.AreEqual(1000.5m, items[0].Open);
        Assert.AreEqual(1005m, items[0].High);
        Assert.AreEqual(998.5m, items[0].Low);
        Assert.AreEqual(1002m, items[0].Close);
        Assert.AreEqual(18000m, items[0].Volume);
        Assert.AreEqual("台積電", items[0].Raw.GetProperty("name").GetString());
        Assert.AreEqual(29.5m, items[1].Open, "string-typed prices must be read");
        Assert.AreEqual(29.8m, items[1].Close);

        using var body = SentBody(server, "/api/v1/data/daily_quotes");
        Assert.AreEqual("2026-10-30", body.RootElement.GetProperty("date").GetString());
        Assert.IsTrue(body.RootElement.GetProperty("exclude_warrant").GetBoolean(), "warrants are excluded by default");
    }

    [TestMethod]
    public async Task GetDailyQuotes_ExcludeWarrantFalse_IsSent()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.History.GetDailyQuotesAsync(new DateOnly(2026, 10, 30), excludeWarrant: false)).IsSuccess);

        using var body = SentBody(server, "/api/v1/data/daily_quotes");
        Assert.IsFalse(body.RootElement.GetProperty("exclude_warrant").GetBoolean());
    }

    [TestMethod]
    public async Task GetDailyQuotes_ThePathCanBeOverridden_BecauseTheDefaultIsUnverified()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/daily-quotes", 200, Fixture.Read("daily_quotes.json"));
        using var host = ShioajiTestHost.Create(server, o => o.DailyQuotesPath = "/api/v1/data/daily-quotes");

        var result = await host.History.GetDailyQuotesAsync(new DateOnly(2026, 10, 30));

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual(1, server.Count("POST", "/api/v1/data/daily-quotes"));
        Assert.AreEqual(0, server.Count("POST", ShioajiApiPaths.DailyQuotes));
    }

    [TestMethod]
    public async Task GetDailyQuotes_AWrongPathAnswers404_AndABadRowIsAFailure()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o => o.DailyQuotesPath = "/api/v1/data/not-there");

        var missingPath = await host.History.GetDailyQuotesAsync(new DateOnly(2026, 10, 30));
        Assert.AreEqual("shioaji.http.404", missingPath.Error!.Code);

        using var other = new FakeShioajiHttpServer();
        other.Map("POST", ShioajiApiPaths.DailyQuotes, 200, """[{"code":"2330","open":1,"high":1,"low":1,"close":1}]""");
        using var otherHost = ShioajiTestHost.Create(other);

        var noVolume = await otherHost.History.GetDailyQuotesAsync(new DateOnly(2026, 10, 30));
        Assert.AreEqual(ShioajiErrorCodes.MissingField, noVolume.Error!.Code);

        other.Map("POST", ShioajiApiPaths.DailyQuotes, 200, """{"nope":1}""");
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, (await otherHost.History.GetDailyQuotesAsync(new DateOnly(2026, 10, 30))).Error!.Code);
    }

    // ───────────────────────────── scanner ─────────────────────────────

    [TestMethod]
    public async Task GetScanner_SendsTheDocumentedBody_AndMapsTheItems()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/scanner", 200, Fixture.Read("scanner.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetScannerAsync(ShioajiScannerType.VolumeRank, new DateOnly(2026, 10, 30), ascending: true, count: 20);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var items = result.GetValueOrThrow().Items;
        Assert.HasCount(2, items);
        Assert.AreEqual("2330", items[0].Code);
        Assert.AreEqual("台積電", items[0].Name);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 5, 30, 0, TimeSpan.Zero), items[0].Time);
        Assert.AreEqual(1002m, items[0].Close);
        Assert.AreEqual(1.5m, items[0].ChangePrice);
        Assert.AreEqual(0.15m, items[0].ChangeRate);
        Assert.AreEqual(1001.25m, items[0].AveragePrice);
        Assert.AreEqual(2000m, items[0].TotalVolume);
        Assert.AreEqual(2_004_000_000m, items[0].TotalAmount);
        Assert.AreEqual(1.8m, items[0].VolumeRatio);
        Assert.AreEqual(3, items[0].Raw.GetProperty("bid_orders").GetArrayLength());
        Assert.IsNull(items[1].Open);
        Assert.AreEqual(29.8m, items[1].Close);

        using var body = SentBody(server, "/api/v1/data/scanner");
        Assert.AreEqual("VolumeRank", body.RootElement.GetProperty("scanner_type").GetString());
        Assert.AreEqual("2026-10-30", body.RootElement.GetProperty("date").GetString());
        Assert.IsTrue(body.RootElement.GetProperty("ascending").GetBoolean());
        Assert.AreEqual(20, body.RootElement.GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task GetScanner_DefaultsAndLocalChecks()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.History.GetScannerAsync(ShioajiScannerType.AmountRank)).IsSuccess);

        using (var body = SentBody(server, "/api/v1/data/scanner"))
        {
            Assert.IsFalse(body.RootElement.TryGetProperty("date", out _), "no date means the server's default");
            Assert.IsFalse(body.RootElement.GetProperty("ascending").GetBoolean());
            Assert.AreEqual(100, body.RootElement.GetProperty("count").GetInt32());
        }

        Assert.IsTrue((await host.History.GetScannerAsync(ShioajiScannerType.AmountRank, count: 0)).IsSuccess);
        Assert.IsTrue((await host.History.GetScannerAsync(ShioajiScannerType.AmountRank, count: 200)).IsSuccess);

        var before = server.Requests.Count;
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetScannerAsync(ShioajiScannerType.AmountRank, count: 201)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetScannerAsync(ShioajiScannerType.AmountRank, count: -1)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetScannerAsync(ShioajiScannerType.Unknown)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetScannerAsync((ShioajiScannerType)99)).Error!.Code);
        Assert.HasCount(before, server.Requests, "nothing was sent for the refused calls");

        server.Map("POST", "/api/v1/data/scanner", 500, "{}");
        Assert.AreEqual("shioaji.http.500", (await host.History.GetScannerAsync(ShioajiScannerType.AmountRank)).Error!.Code);
    }

    // ───────────────────────────── short stock sources, credit enquire ─────────────────────────────

    [TestMethod]
    public async Task GetShortStockSources_MapsAndSendsStocksOnly()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/short_stock_sources", 200, Fixture.Read("short_stock_sources.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetShortStockSourcesAsync([Stock("2330"), Stock("2317")]);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var items = result.GetValueOrThrow().Items;
        Assert.HasCount(2, items);
        Assert.AreEqual("2330", items[0].Code);
        Assert.AreEqual(1500m, items[0].Source);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 0, 30, 0, TimeSpan.Zero), items[0].UpdateTime);
        Assert.AreEqual(0m, items[1].Source, "a string-typed zero is a real zero the server sent");
        Assert.IsNull(items[1].UpdateTime);

        using var body = SentBody(server, "/api/v1/data/short_stock_sources");
        Assert.AreEqual(2, body.RootElement.GetProperty("contracts").GetArrayLength());
    }

    [TestMethod]
    public async Task GetCreditEnquire_MapsAndSendsStocksOnly()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/data/credit_enquire", 200, Fixture.Read("credit_enquire.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetCreditEnquireAsync([Stock("2330"), Stock("2317")]);

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var items = result.GetValueOrThrow().Items;
        Assert.HasCount(2, items);
        Assert.AreEqual("2330", items[0].Code);
        Assert.AreEqual("0", items[0].MarginSystem);
        Assert.AreEqual(12000m, items[0].MarginUnit);
        Assert.AreEqual(300m, items[0].ShortUnit);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 0, 30, 0, TimeSpan.Zero), items[0].UpdateTime);
        Assert.AreEqual("2317", items[1].Code);
        Assert.IsNull(items[1].MarginUnit);

        server.Map("POST", "/api/v1/data/credit_enquire", 200, """[{"code":"1101"}]""");
        Assert.AreEqual("1101", (await host.History.GetCreditEnquireAsync([Stock("1101")])).GetValueOrThrow().Items.Single().Code, "code is accepted when stock_id is absent");

        server.Map("POST", "/api/v1/data/credit_enquire", 200, """[{"margin_unit":1}]""");
        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.History.GetCreditEnquireAsync([Stock("1101")])).Error!.Code);
    }

    [TestMethod]
    public async Task StockOnlyQueries_RefuseAFutureAndAnEmptyList_BeforeAnythingIsSent()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        var future = new ShioajiContractRef { SecurityType = ShioajiSecurityType.Future, Exchange = "TAIFEX", Code = "TXFR1" };

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetShortStockSourcesAsync([Stock(), future])).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetCreditEnquireAsync([future])).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetShortStockSourcesAsync([])).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetCreditEnquireAsync([])).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, (await host.History.GetCreditEnquireAsync([null!])).Error!.Code);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.History.GetShortStockSourcesAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.History.GetCreditEnquireAsync(null!));
        Assert.IsEmpty(server.Requests);

        server.Map("POST", "/api/v1/data/short_stock_sources", 500, "{}");
        server.Map("POST", "/api/v1/data/credit_enquire", 502, "{}");
        Assert.AreEqual("shioaji.http.500", (await host.History.GetShortStockSourcesAsync([Stock()])).Error!.Code);
        Assert.AreEqual("shioaji.http.502", (await host.History.GetCreditEnquireAsync([Stock()])).Error!.Code);
    }

    // ───────────────────────────── regulatory lists ─────────────────────────────

    [TestMethod]
    public async Task GetRegulatoryPunish_AcceptsAnArrayOfObjects()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/regulatory_punish", 200, Fixture.Read("regulatory_punish.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetRegulatoryPunishAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var items = result.GetValueOrThrow().Items;
        Assert.HasCount(2, items);
        Assert.AreEqual(ShioajiRegulatoryKind.Punish, items[0].Kind);
        Assert.AreEqual("1234", items[0].Code);
        Assert.AreEqual("範例甲", items[0].Name);
        Assert.AreEqual("2026-11-11", items[0].Raw.GetProperty("end_date").GetString());
        Assert.AreEqual("範例乙", items[1].Name);
    }

    [TestMethod]
    public async Task GetRegulatoryNotice_AcceptsTheColumnArrayFormToo()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/regulatory_notice", 200, Fixture.Read("regulatory_notice.json"));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.History.GetRegulatoryNoticeAsync();

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        var items = result.GetValueOrThrow().Items;
        Assert.HasCount(2, items);
        Assert.AreEqual(ShioajiRegulatoryKind.Notice, items[1].Kind);
        Assert.AreEqual("1111", items[0].Code);
        Assert.AreEqual("範例丁", items[1].Name);
        Assert.AreEqual("2026-10-29", items[1].Raw.GetProperty("announced_date").GetString(), "each row keeps its own cells in Raw");
    }

    [TestMethod]
    public async Task RegulatoryLists_FailuresAndEmptyAnswers()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/data/regulatory_punish", 500, "{}");
        server.Map("GET", "/api/v1/data/regulatory_notice", 200, """{"code":["1","2"],"name":["only one"]}""");
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual("shioaji.http.500", (await host.History.GetRegulatoryPunishAsync()).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.HistoryMalformed, (await host.History.GetRegulatoryNoticeAsync()).Error!.Code);

        server.Map("GET", "/api/v1/data/regulatory_notice", 200, """{"foo":1}""");
        Assert.AreEqual(ShioajiErrorCodes.HistoryMalformed, (await host.History.GetRegulatoryNoticeAsync()).Error!.Code);

        server.Map("GET", "/api/v1/data/regulatory_notice", 200, """[{"name":"no code"}]""");
        Assert.AreEqual(ShioajiErrorCodes.MissingField, (await host.History.GetRegulatoryNoticeAsync()).Error!.Code);

        server.Map("GET", "/api/v1/data/regulatory_notice", 200, "[]");
        Assert.IsTrue((await host.History.GetRegulatoryNoticeAsync()).GetValueOrThrow().IsEmpty);

        server.Map("GET", "/api/v1/data/regulatory_notice", 200, """{"code":[]}""");
        Assert.IsTrue((await host.History.GetRegulatoryNoticeAsync()).GetValueOrThrow().IsEmpty);
    }

    [TestMethod]
    public async Task EveryQueryTakesOneMarketQueryUnit()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.RateBudget.MarketQueryLimit = 9;
            o.RateBudget.MaxWait = TimeSpan.Zero;
        });
        var day = new DateOnly(2026, 10, 29);

        Assert.IsTrue((await host.History.GetKbarsAsync(Stock(), day, day)).IsSuccess);
        Assert.IsTrue((await host.History.GetTicksAsync(Stock(), day, ShioajiTicksQuery.AllDay)).IsSuccess);
        Assert.IsTrue((await host.History.GetSnapshotsAsync([Stock()])).IsSuccess);
        Assert.IsTrue((await host.History.GetDailyQuotesAsync(day)).IsSuccess);
        Assert.IsTrue((await host.History.GetScannerAsync(ShioajiScannerType.VolumeRank)).IsSuccess);
        Assert.IsTrue((await host.History.GetShortStockSourcesAsync([Stock()])).IsSuccess);
        Assert.IsTrue((await host.History.GetCreditEnquireAsync([Stock()])).IsSuccess);
        Assert.IsTrue((await host.History.GetRegulatoryPunishAsync()).IsSuccess);
        Assert.IsTrue((await host.History.GetRegulatoryNoticeAsync()).IsSuccess);

        var tenth = await host.History.GetRegulatoryNoticeAsync();

        Assert.AreEqual(ShioajiErrorCodes.RateBudgetExhausted, tenth.Error!.Code, "nine queries spent the nine units");
        Assert.AreEqual(9, server.Requests.Count);
    }

    [TestMethod]
    public void Constructor_RejectsNull_AndFallsBackToTheDefaultPath()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiHistoryClient(null!, host.Budget));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiHistoryClient(host.Api, null!));
        Assert.IsNotNull(new ShioajiHistoryClient(host.Api, host.Budget));
    }

    [TestMethod]
    public async Task AClientBuiltWithoutOptionsOrAMeter_UsesTheDefaultPath_AndFlagsEveryEmptyAnswer()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", ShioajiApiPaths.DailyQuotes, 200, "[]");
        using var host = ShioajiTestHost.Create(server);
        var client = new ShioajiHistoryClient(host.Api, host.Budget);

        var page = (await client.GetDailyQuotesAsync(new DateOnly(2026, 10, 30))).GetValueOrThrow();

        Assert.AreEqual(1, server.Count("POST", ShioajiApiPaths.DailyQuotes));
        Assert.IsTrue(page.IsEmpty);
        Assert.IsTrue(page.PossiblyTrafficExhausted);
    }
}
