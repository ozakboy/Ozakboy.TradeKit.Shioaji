using Ozakboy.TradeKit.Shioaji.Orders;
using Ozakboy.TradeKit.Shioaji.Streaming;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiStreamEventParserTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 30, 1, 2, 3, TimeSpan.Zero);

    private static string Levels(IEnumerable<decimal> values) =>
        string.Join(',', values.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static Result<ShioajiStreamEvent> Parse(string name, string data) =>
        ShioajiStreamEventParser.Parse(name, data, Received);

    [TestMethod]
    public void TickStk_MapsEveryField_AndConvertsTaipeiTimeToUtc()
    {
        var tick = (ShioajiTickStk)TestEvents.Parse("tick_stk", "tick_stk.json");

        Assert.AreEqual("2330", tick.Code);
        Assert.AreEqual("TSE", tick.Exchange);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 1, 0, 1, TimeSpan.Zero).AddTicks(2_345_670), tick.Time);
        Assert.AreEqual(1000.5m, tick.Open);
        Assert.AreEqual(1001.25m, tick.AvgPrice);
        Assert.AreEqual(1002m, tick.Close);
        Assert.AreEqual(1005m, tick.High);
        Assert.AreEqual(998.5m, tick.Low);
        Assert.AreEqual(2m, tick.Volume);
        Assert.AreEqual(2000m, tick.TotalVolume);
        Assert.AreEqual(2_004_000_000m, tick.TotalAmount, "a string-typed number must be readable");
        Assert.AreEqual(ShioajiTickType.Outside, tick.TickType);
        Assert.AreEqual(ShioajiChangeType.Up, tick.ChgType);
        Assert.AreEqual(1.5m, tick.PriceChg);
        Assert.AreEqual(0.15m, tick.PctChg);
        Assert.AreEqual(410L, tick.BidSideTotalCnt);
        Assert.IsFalse(tick.Suspend);
        Assert.IsFalse(tick.Simtrade);
        Assert.IsFalse(tick.IntradayOdd);
        Assert.AreEqual(DateTimeOffset.UnixEpoch, tick.ReceivedAt);
    }

    [TestMethod]
    public void Prices_AreParsedFromTheRawTextAndNeverThroughDouble()
    {
        // 29.8 經過 double 會變成 29.800000000000000710542735760100185871124267578125,轉回 decimal 可能差一個最小單位。
        // Through a double, 29.8 becomes 29.8000000000000007105...; converting back to decimal can be off by one unit.
        var quote = (ShioajiQuoteStk)TestEvents.Parse("quote_stk", "quote_stk.json");

        Assert.AreEqual(29.8m, quote.Close);
        Assert.AreEqual("29.8", quote.Close.ToString(System.Globalization.CultureInfo.InvariantCulture));

        const string highPrecision = """{"code":"X","date":"2026-10-30","time":"09:00:00","close":0.1234567890123456789012345}""";
        var tick = (ShioajiTickStk)Parse("tick_stk", highPrecision).GetValueOrThrow();

        Assert.AreEqual(0.1234567890123456789012345m, tick.Close);
    }

    [TestMethod]
    public void BidAskStk_HasFiveLevelsOnEachSide()
    {
        var book = (ShioajiBidAskStk)TestEvents.Parse("bidask_stk", "bidask_stk.json");

        Assert.AreEqual("1001.5,1001,1000.5,1000,999.5", Levels(book.BidPrice));
        Assert.AreEqual("10,20,30,40,50", Levels(book.BidVolume));
        Assert.AreEqual("1,0,-2,0,3", Levels(book.DiffBidVol!));
        Assert.AreEqual("1002,1002.5,1003,1003.5,1004", Levels(book.AskPrice));
        Assert.AreEqual("5,15,25,35,45", Levels(book.AskVolume));
        Assert.HasCount(5, book.DiffAskVol!);
    }

    [TestMethod]
    public void QuoteStk_CarriesTradeAndBook_AndTheTrialMatchFlag()
    {
        var quote = (ShioajiQuoteStk)TestEvents.Parse("quote_stk", "quote_stk.json");

        Assert.AreEqual(ShioajiTickType.Inside, quote.TickType);
        Assert.AreEqual(ShioajiChangeType.Down, quote.ChgType);
        Assert.AreEqual(-0.5m, quote.PriceChg);
        Assert.HasCount(5, quote.BidPrice);
        Assert.HasCount(5, quote.AskVolume);
        Assert.AreEqual(1234m, quote.AvailBorrowing);
        Assert.IsTrue(quote.Simtrade);
    }

    [TestMethod]
    public void TickFop_BidAskFop_QuoteFop_AndQuoteIdx_Map()
    {
        var tick = (ShioajiTickFop)TestEvents.Parse("tick_fop", "tick_fop.json");
        Assert.AreEqual("TXFK6", tick.Code);
        Assert.AreEqual(23002m, tick.Close);
        Assert.AreEqual(22990.25m, tick.UnderlyingPrice);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 0, 45, 0, TimeSpan.Zero).AddTicks(1_000_000), tick.Time);

        var book = (ShioajiBidAskFop)TestEvents.Parse("bidask_fop", "bidask_fop.json");
        Assert.AreEqual(77m, book.BidTotalVol);
        Assert.AreEqual(23000.5m, book.FirstDerivedBidPrice);
        Assert.AreEqual(2m, book.FirstDerivedAskVol);
        Assert.HasCount(5, book.AskPrice);

        var quote = (ShioajiQuoteFop)TestEvents.Parse("quote_fop", "quote_fop.json");
        Assert.AreEqual(ShioajiChangeType.Flat, quote.ChgType);
        Assert.HasCount(5, quote.BidVolume);

        var index = (ShioajiQuoteIdx)TestEvents.Parse("quote_idx", "quote_idx.json");
        Assert.AreEqual("IX0001", index.Code);
        Assert.AreEqual(22995.67m, index.Close);
        Assert.AreEqual(0.07m, index.PctChg);
    }

    [TestMethod]
    public void OrderEvent_Order_MapsOperationOrderAndStatus()
    {
        var evt = (ShioajiOrderEvent)TestEvents.Parse("order_event", "order_event_order.json");

        Assert.AreEqual("evt-0001", evt.EventId);
        Assert.AreEqual(ShioajiOrderEventType.Order, evt.EventType);
        Assert.AreEqual("New", evt.Operation!.OpType);
        Assert.AreEqual("00", evt.Operation.OpCode);
        Assert.AreEqual("a1b2c3d4", evt.Order!.Id);
        Assert.AreEqual(ShioajiAction.Buy, evt.Order.Action);
        Assert.AreEqual(600.5m, evt.Order.Price);
        Assert.AreEqual(ShioajiOrderCondition.Cash, evt.Order.OrderCondition);
        Assert.AreEqual("sig001", evt.Order.CustomField);
        Assert.AreEqual("9A95", evt.Order.Account!.BrokerId);
        Assert.AreEqual(1m, evt.Status!.OrderQuantity);
        Assert.AreEqual("137", evt.Status.WebId);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1_761_800_400_123), evt.Status.ExchangeTs);
        Assert.AreEqual("2330", evt.Contract!.Code);
        Assert.IsNull(evt.Deal);
        Assert.IsTrue(evt.RawJson.StartsWith("{\"event_id\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void OrderEvent_Deal_IsRecognisedFromTheTopLevelDealFields()
    {
        var evt = (ShioajiOrderEvent)TestEvents.Parse("order_event", "order_event_deal.json");

        Assert.AreEqual("evt-0002", evt.EventId);
        Assert.AreEqual(ShioajiOrderEventType.Deal, evt.EventType);
        Assert.AreEqual("a1b2c3d4", evt.Deal!.TradeId);
        Assert.AreEqual("X000123", evt.Deal.ExchangeSeq);
        Assert.AreEqual(600.5m, evt.Deal.Price);
        Assert.AreEqual(1m, evt.Deal.Quantity);
        Assert.AreEqual(ShioajiAction.Buy, evt.Deal.Action);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1_761_800_401_500), evt.Deal.Ts);
        Assert.IsNull(evt.Order);
    }

    [TestMethod]
    public void OrderEvent_DealNestedUnderADealObject_AndExplicitEventTypeWin()
    {
        const string nested = """{"event_id":"e3","event_type":"StockDeal","deal":{"trade_id":"t","price":"10.5","quantity":2,"code":"2330"}}""";

        var evt = (ShioajiOrderEvent)Parse("order_event", nested).GetValueOrThrow();

        Assert.AreEqual(ShioajiOrderEventType.Deal, evt.EventType);
        Assert.AreEqual(10.5m, evt.Deal!.Price);

        const string orderByName = """{"event_id":"e4","event_type":"FuturesOrder","trade_id":"t","exchange_seq":"x","price":1,"quantity":1}""";

        Assert.AreEqual(ShioajiOrderEventType.Order, ((ShioajiOrderEvent)Parse("order_event", orderByName).GetValueOrThrow()).EventType);

        const string unknownType = """{"event_id":"e5","event_type":"Something"}""";

        Assert.AreEqual(ShioajiOrderEventType.Unknown, ((ShioajiOrderEvent)Parse("order_event", unknownType).GetValueOrThrow()).EventType);
    }

    [TestMethod]
    public void OrderEvent_WithoutAnEventId_IsAFailure_BecauseItCannotBeDeduplicated()
    {
        var result = Parse("order_event", """{"order":{"id":"x"}}""");

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.MissingField, result.Error.Code);
    }

    [TestMethod]
    public void OrderEvent_ADealWithoutAPriceIsNotADeal_ButTheEventStillArrives()
    {
        var evt = (ShioajiOrderEvent)Parse("order_event", """{"event_id":"e","trade_id":"t","exchange_seq":"x","quantity":1}""").GetValueOrThrow();

        Assert.IsNull(evt.Deal);
    }

    [TestMethod]
    [DataRow("heartbeat", "")]
    [DataRow("heartbeat", "{}")]
    [DataRow("HEARTBEAT", "{\"ts\":1}")]
    [DataRow("ping", "pong")]
    [DataRow("", "{\"anything\":1}")]
    [DataRow("   ", "")]
    [DataRow("keepalive", "not json at all")]
    [DataRow("keepalive", "[1,2,3]")]
    [DataRow("keepalive", "")]
    public void Heartbeats_AreRecognisedInEveryFormTheDocumentationLeavesOpen(string name, string data)
    {
        var result = Parse(name, data);

        Assert.IsTrue(result.IsSuccess);

        var heartbeat = (ShioajiHeartbeat)result.GetValueOrThrow();

        Assert.AreEqual(name.Trim(), heartbeat.EventName);
        Assert.AreEqual(data, heartbeat.RawData, "the original text must be kept");
        Assert.AreEqual(Received, heartbeat.ReceivedAt);
    }

    [TestMethod]
    public void UnknownEventNameWithAJsonObject_IsPassedThroughVerbatim()
    {
        var result = Parse("kbar", """{"code":"2330","note":"later"}""");

        var unknown = (ShioajiUnknownEvent)result.GetValueOrThrow();

        Assert.AreEqual("kbar", unknown.EventName);
        Assert.AreEqual("""{"code":"2330","note":"later"}""", unknown.RawData);
    }

    [TestMethod]
    public void KnownEventNameWithoutAJsonObject_IsAFailure_NotAHeartbeat()
    {
        foreach (var data in new[] { "garbage", "", "[1]", "42" })
        {
            var result = Parse("tick_stk", data);

            Assert.IsTrue(result.IsFailure, data);
            Assert.AreEqual(ShioajiErrorCodes.StreamMalformedEvent, result.Error.Code);
            Assert.AreEqual("tick_stk", result.Error.Data![ShioajiErrorDataKeys.EventName]);
        }
    }

    [TestMethod]
    public void MissingRequiredFields_AreFailures_NeverZero()
    {
        Assert.AreEqual(
            ShioajiErrorCodes.MissingField,
            Parse("tick_stk", """{"code":"2330","date":"2026-10-30","time":"09:00:00"}""").Error!.Code);
        Assert.AreEqual(
            ShioajiErrorCodes.MissingField,
            Parse("tick_stk", """{"date":"2026-10-30","time":"09:00:00","close":1}""").Error!.Code);
        Assert.AreEqual(
            ShioajiErrorCodes.MissingField,
            Parse("tick_stk", """{"code":"2330","close":1}""").Error!.Code);
        Assert.AreEqual(
            ShioajiErrorCodes.MissingField,
            Parse("quote_idx", """{"code":"IX0001","date":"2026-10-30","time":"09:00:00"}""").Error!.Code);
    }

    [TestMethod]
    public void BookArrays_MustBeExactlyFiveNumbers()
    {
        const string head = """{"code":"2330","date":"2026-10-30","time":"09:00:00",""";

        var fourLevels = Parse("bidask_stk", head + """ "bid_price":[1,2,3,4],"bid_volume":[1,2,3,4,5],"ask_price":[1,2,3,4,5],"ask_volume":[1,2,3,4,5]}""");
        var notNumbers = Parse("bidask_stk", head + """ "bid_price":[1,2,3,4,"x"],"bid_volume":[1,2,3,4,5],"ask_price":[1,2,3,4,5],"ask_volume":[1,2,3,4,5]}""");
        var missing = Parse("bidask_fop", head + """ "bid_price":[1,2,3,4,5]}""");

        Assert.IsTrue(fourLevels.IsFailure);
        Assert.IsTrue(notNumbers.IsFailure);
        Assert.IsTrue(missing.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.MissingField, fourLevels.Error.Code);
    }

    [TestMethod]
    public void OptionalFlagsDefaultToFalse_AndUnknownTickTypesToUndetermined()
    {
        var tick = (ShioajiTickFop)Parse("tick_fop", """{"code":"TXFK6","datetime":"2026-10-30 08:45:00.5","close":23002,"tick_type":9,"chg_type":"x"}""").GetValueOrThrow();

        Assert.IsFalse(tick.Simtrade);
        Assert.AreEqual(ShioajiTickType.Undetermined, tick.TickType);
        Assert.AreEqual(ShioajiChangeType.Unknown, tick.ChgType);
        Assert.IsNull(tick.Open);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 0, 45, 0, 500, TimeSpan.Zero), tick.Time, "datetime is the fallback when date and time are absent");
    }

    [TestMethod]
    public void AnUnreadableTime_IsAFailure()
    {
        var result = Parse("tick_stk", """{"code":"2330","date":"nope","time":"nope","datetime":"nope","close":1}""");

        Assert.IsTrue(result.IsFailure);
    }
}
