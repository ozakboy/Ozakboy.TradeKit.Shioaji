using Ozakboy.TradeKit.Shioaji.Orders;
using Ozakboy.TradeKit.Shioaji.Portfolio;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiPortfolioParserTests
{
    // ───────────────────────── 日期 / dates ─────────────────────────

    [TestMethod]
    [DataRow("2026-10-30", 2026, 10, 30)]
    [DataRow("2026/10/30", 2026, 10, 30)]
    [DataRow("20261030", 2026, 10, 30)]
    [DataRow(" 2026-10-30 ", 2026, 10, 30)]
    [DataRow("2026-10-30 23:59:59", 2026, 10, 30)]
    [DataRow("2026-10-30T23:59:59.123456", 2026, 10, 30)]
    [DataRow("2026-05-19 09:21:10.085711", 2026, 5, 19)]
    public void TryReadDateAcceptsTheDocumentedShapesAndKeepsTheWrittenDay(string text, int year, int month, int day)
    {
        Assert.IsTrue(ShioajiTimestamps.TryReadDate(text, out var date));
        Assert.AreEqual(new DateOnly(year, month, day), date, "the day is as written, with no time-zone shift");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not a date")]
    [DataRow("2026-13-40")]
    [DataRow("T09:00:00")]
    public void TryReadDateRejectsBlankAndNonsense(string text)
    {
        Assert.IsFalse(ShioajiTimestamps.TryReadDate(text, out _));
    }

    [TestMethod]
    public void TryReadDateRejectsNull()
    {
        Assert.IsFalse(ShioajiTimestamps.TryReadDate(null, out var date));
        Assert.AreEqual(default, date);
    }

    // ───────────────────────── 列表形狀 / list shapes ─────────────────────────

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("""{"items":[]}""")]
    public void EmptyAnswersAreSuccessfulEmptyLists(string body)
    {
        Assert.IsEmpty(ShioajiPortfolioParser.ParseStockPositions(body).GetValueOrThrow(), body);
        Assert.IsEmpty(ShioajiPortfolioParser.ParseFuturesPositions(body).GetValueOrThrow(), body);
        Assert.IsEmpty(ShioajiPortfolioParser.ParseStockPositionDetails(body).GetValueOrThrow(), body);
        Assert.IsEmpty(ShioajiPortfolioParser.ParseFuturesPositionDetails(body).GetValueOrThrow(), body);
        Assert.IsEmpty(ShioajiPortfolioParser.ParseStockProfitLoss(body).GetValueOrThrow(), body);
        Assert.IsEmpty(ShioajiPortfolioParser.ParseFuturesProfitLoss(body).GetValueOrThrow(), body);
        Assert.IsEmpty(ShioajiPortfolioParser.ParseStockProfitLossDetails(body).GetValueOrThrow(), body);
        Assert.IsEmpty(ShioajiPortfolioParser.ParseFuturesProfitLossDetails(body).GetValueOrThrow(), body);
        Assert.IsEmpty(ShioajiPortfolioParser.ParseSettlements(body).GetValueOrThrow(), body);
    }

    [TestMethod]
    [DataRow("items")]
    [DataRow("data")]
    [DataRow("results")]
    [DataRow("list")]
    [DataRow("rows")]
    public void AWrappedArrayIsReadUnderAnyOfTheKnownNames(string wrapper)
    {
        var body = $$"""{"{{wrapper}}":[{"code":"2330","quantity":1}],"page":1}""";

        var rows = ShioajiPortfolioParser.ParseStockPositions(body).GetValueOrThrow();

        Assert.AreEqual("2330", rows.Single().Code);
    }

    [TestMethod]
    public void AColumnArrayObjectIsReadRowByRow()
    {
        var rows = ShioajiPortfolioParser
            .ParseSettlements("""{"date":["2026-10-30","2026-11-02"],"amount":[100.5,"-20"],"T":[0,1]}""")
            .GetValueOrThrow();

        Assert.HasCount(2, rows);
        Assert.AreEqual(new DateOnly(2026, 11, 2), rows[1].Date);
        Assert.AreEqual(-20m, rows[1].Amount);
        Assert.AreEqual(1, rows[1].TPlus);
    }

    [TestMethod]
    public void AColumnArrayRowWithAMissingRequiredCellFailsTheWholeResponse()
    {
        var noColumn = ShioajiPortfolioParser.ParseStockPositions("""{"code":["2330","2317"]}""");
        var nullCell = ShioajiPortfolioParser.ParseStockPositions("""{"code":["2330","2317"],"quantity":[1,null]}""");

        Assert.AreEqual("quantity", noColumn.Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("quantity", nullCell.Error!.Data![ShioajiErrorDataKeys.Field], "a null cell is not zero");
    }

    [TestMethod]
    public void ColumnArraysOfUnequalLengthFailAsAMalformedResponse()
    {
        var result = ShioajiPortfolioParser.ParseStockPositions("""{"code":["2330","2317"],"quantity":[1]}""");

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, result.Error.Code, "a portfolio response is not a history response");
    }

    [TestMethod]
    [DataRow("""{"hello":"world"}""")]
    [DataRow("""{"code":"2330","quantity":1}""")]
    [DataRow("42")]
    [DataRow("\"text\"")]
    [DataRow("true")]
    public void ShapesThatAreNotListsFailAsAMalformedResponse(string body)
    {
        var result = ShioajiPortfolioParser.ParseStockPositions(body);

        Assert.IsTrue(result.IsFailure, body);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, result.Error.Code, body);
    }

    [TestMethod]
    public void AnArrayElementThatIsNotAnObjectFailsTheWholeResponse()
    {
        var result = ShioajiPortfolioParser.ParseStockPositions("""[{"code":"2330","quantity":1},"oops"]""");

        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, result.Error!.Code);
    }

    [TestMethod]
    public void AnInvalidJsonBodyFailsAsAMalformedResponse()
    {
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, ShioajiPortfolioParser.ParseStockPositions("{oops").Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, ShioajiPortfolioParser.ParseMargin("{oops").Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, ShioajiPortfolioParser.ParseStockProfitLossSummary("{oops").Error!.Code);
    }

    // ───────────────────────── 必填欄位 / required fields ─────────────────────────

    [TestMethod]
    public void AMissingCodeOrQuantityFailsTheResponseAndNeverBecomesZero()
    {
        var noCode = ShioajiPortfolioParser.ParseStockPositions("""[{"quantity":1}]""");
        var noQuantity = ShioajiPortfolioParser.ParseStockPositions("""[{"code":"2330"}]""");
        var badQuantity = ShioajiPortfolioParser.ParseStockPositions("""[{"code":"2330","quantity":"many"}]""");

        Assert.AreEqual(ShioajiErrorCodes.MissingField, noCode.Error!.Code);
        Assert.AreEqual("code", noCode.Error.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual(ShioajiErrorCodes.MissingField, noQuantity.Error!.Code);
        Assert.AreEqual("quantity", noQuantity.Error.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual(ShioajiErrorCodes.MissingField, badQuantity.Error!.Code, "an unreadable quantity is a failure, not zero");
    }

    [TestMethod]
    public void ARowFailureFailsEveryListEndpointNotJustTheFirst()
    {
        const string body = """[{"code":"2330","quantity":1},{"code":"2317"}]""";

        Assert.IsTrue(ShioajiPortfolioParser.ParseStockPositions(body).IsFailure);
        Assert.IsTrue(ShioajiPortfolioParser.ParseFuturesPositions(body).IsFailure);
        Assert.IsTrue(ShioajiPortfolioParser.ParseStockPositionDetails(body).IsFailure);
        Assert.IsTrue(ShioajiPortfolioParser.ParseFuturesPositionDetails(body).IsFailure);
        Assert.IsTrue(ShioajiPortfolioParser.ParseStockProfitLoss(body).IsFailure);
        Assert.IsTrue(ShioajiPortfolioParser.ParseFuturesProfitLoss(body).IsFailure);
        Assert.IsTrue(ShioajiPortfolioParser.ParseStockProfitLossDetails(body).IsFailure);
        Assert.IsTrue(ShioajiPortfolioParser.ParseFuturesProfitLossDetails(body).IsFailure);
    }

    [TestMethod]
    public void ACodeGivenAsANumberIsReadAsItsText()
    {
        var rows = ShioajiPortfolioParser.ParseStockPositions("""[{"code":2330,"quantity":"1"}]""").GetValueOrThrow();

        Assert.AreEqual("2330", rows.Single().Code);
    }

    [TestMethod]
    public void OptionalFieldsOfAMinimalRowAreNullAndEnumsAreUnknown()
    {
        var position = ShioajiPortfolioParser.ParseStockPositions("""[{"code":"2330","quantity":1}]""").GetValueOrThrow().Single();

        Assert.IsNull(position.Id);
        Assert.IsNull(position.Price);
        Assert.IsNull(position.LastPrice);
        Assert.IsNull(position.Pnl);
        Assert.IsNull(position.YdQuantity);
        Assert.IsNull(position.MarginPurchaseAmount);
        Assert.IsNull(position.Collateral);
        Assert.IsNull(position.ShortSaleMargin);
        Assert.IsNull(position.Interest);
        Assert.AreEqual(ShioajiAction.Unknown, position.Direction);
        Assert.AreEqual(ShioajiPositionCondition.Unknown, position.Condition);

        var futures = ShioajiPortfolioParser.ParseFuturesProfitLoss("""[{"code":"TXFK6","quantity":1}]""").GetValueOrThrow().Single();

        Assert.IsNull(futures.Pnl);
        Assert.IsNull(futures.Date);
        Assert.IsNull(futures.EntryPrice);
        Assert.IsNull(futures.Tax);
        Assert.IsNull(futures.Fee);
    }

    [TestMethod]
    public void UnreadableOptionalValuesAreNullAndAnUnparseableDateIsNull()
    {
        var row = ShioajiPortfolioParser
            .ParseStockProfitLoss("""[{"code":"2330","quantity":1,"pnl":"n/a","date":"someday","price":{"x":1},"id":"abc","cond":"Weird"}]""")
            .GetValueOrThrow()
            .Single();

        Assert.IsNull(row.Pnl);
        Assert.IsNull(row.Date);
        Assert.IsNull(row.Price);
        Assert.IsNull(row.Id);
        Assert.AreEqual(ShioajiPositionCondition.Unknown, row.Condition);
    }

    [TestMethod]
    public void NumbersAreExactDecimalsWhetherTheyArriveAsNumbersOrStrings()
    {
        var rows = ShioajiPortfolioParser
            .ParseStockPositions("""[{"code":"A","quantity":1,"price":29.8,"pnl":0.1},{"code":"B","quantity":1,"price":"29.8","pnl":"-0.30000000000000004"}]""")
            .GetValueOrThrow();

        Assert.AreEqual(29.8m, rows[0].Price);
        Assert.AreEqual(0.1m, rows[0].Pnl);
        Assert.AreEqual(29.8m, rows[1].Price);
        Assert.AreEqual(-0.30000000000000004m, rows[1].Pnl);
        Assert.AreEqual(12345678901234.5678m, ShioajiPortfolioParser.ParseStockPositions("""[{"code":"A","quantity":1,"pnl":12345678901234.5678}]""").GetValueOrThrow().Single().Pnl);
    }

    [TestMethod]
    [DataRow("Buy", ShioajiAction.Buy)]
    [DataRow("buy", ShioajiAction.Buy)]
    [DataRow("Action.Buy", ShioajiAction.Buy)]
    [DataRow("Sell", ShioajiAction.Sell)]
    [DataRow("Action.Sell", ShioajiAction.Sell)]
    [DataRow("Hold", ShioajiAction.Unknown)]
    [DataRow("", ShioajiAction.Unknown)]
    public void DirectionReadsAPlainOrPrefixedNameAndAnythingElseIsUnknown(string text, ShioajiAction expected)
    {
        var body = $$"""[{"code":"2330","quantity":1,"direction":"{{text}}"}]""";

        Assert.AreEqual(expected, ShioajiPortfolioParser.ParseStockPositions(body).GetValueOrThrow().Single().Direction);
    }

    [TestMethod]
    [DataRow("Cash", ShioajiPositionCondition.Cash)]
    [DataRow("Netting", ShioajiPositionCondition.Netting)]
    [DataRow("MarginTrading", ShioajiPositionCondition.MarginTrading)]
    [DataRow("ShortSelling", ShioajiPositionCondition.ShortSelling)]
    [DataRow("Emerging", ShioajiPositionCondition.Emerging)]
    [DataRow("StockOrderCond.Emerging", ShioajiPositionCondition.Emerging)]
    [DataRow("SBLShort", ShioajiPositionCondition.Unknown)]
    public void ConditionCoversTheDocumentedStockOrderCondValues(string text, ShioajiPositionCondition expected)
    {
        var body = $$"""[{"code":"2330","quantity":1,"cond":"{{text}}"}]""";

        Assert.AreEqual(expected, ShioajiPortfolioParser.ParseStockPositions(body).GetValueOrThrow().Single().Condition);
    }

    // ───────────────────────── 交割款 / settlements ─────────────────────────

    [TestMethod]
    public void ASettlementNeedsADateAnAmountAndAnOffset()
    {
        Assert.AreEqual("date", ShioajiPortfolioParser.ParseSettlements("""[{"amount":1,"T":0}]""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("date", ShioajiPortfolioParser.ParseSettlements("""[{"date":"someday","amount":1,"T":0}]""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("amount", ShioajiPortfolioParser.ParseSettlements("""[{"date":"2026-10-30","T":0}]""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("T", ShioajiPortfolioParser.ParseSettlements("""[{"date":"2026-10-30","amount":1}]""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("T", ShioajiPortfolioParser.ParseSettlements("""[{"date":"2026-10-30","amount":1,"T":-1}]""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("T", ShioajiPortfolioParser.ParseSettlements("""[{"date":"2026-10-30","amount":1,"T":99999999999}]""").Error!.Data![ShioajiErrorDataKeys.Field]);
    }

    [TestMethod]
    public void ASettlementOffsetMayBeLowerCaseOrAString()
    {
        var rows = ShioajiPortfolioParser
            .ParseSettlements("""[{"date":"2026-10-30","amount":"1.5","t":"2"},{"date":"2026-10-31","amount":2,"T":2.0}]""")
            .GetValueOrThrow();

        Assert.AreEqual(2, rows[0].TPlus);
        Assert.AreEqual(1.5m, rows[0].Amount);
        Assert.AreEqual(2, rows[1].TPlus);
    }

    // ───────────────────────── 單一物件 / single objects ─────────────────────────

    [TestMethod]
    public void ASingleObjectEndpointAcceptsAnObjectAOneElementArrayAndAOneRowColumnArray()
    {
        var asObject = ShioajiPortfolioParser.ParseTradingLimits("""{"trading_available":100}""");
        var asArray = ShioajiPortfolioParser.ParseTradingLimits("""[{"trading_available":100}]""");
        var asColumns = ShioajiPortfolioParser.ParseTradingLimits("""{"trading_available":[100],"trading_used":[5]}""");

        Assert.AreEqual(100m, asObject.GetValueOrThrow().TradingAvailable);
        Assert.AreEqual(100m, asArray.GetValueOrThrow().TradingAvailable);
        Assert.AreEqual(100m, asColumns.GetValueOrThrow().TradingAvailable);
        Assert.AreEqual(5m, asColumns.GetValueOrThrow().TradingUsed);
        Assert.IsNull(asObject.GetValueOrThrow().TradingUsed, "absent is null and never zero");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("""[{"trading_available":1},{"trading_available":2}]""")]
    [DataRow("""{"trading_available":[1,2]}""")]
    [DataRow("""{"trading_available":[]}""")]
    [DataRow("42")]
    [DataRow("[1]")]
    public void ASingleObjectEndpointRefusesEmptyAndAmbiguousAnswers(string body)
    {
        var result = ShioajiPortfolioParser.ParseTradingLimits(body);

        Assert.IsTrue(result.IsFailure, body);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, result.Error.Code, body);
    }

    [TestMethod]
    public void TradingLimitsNeedTheAvailableAmountAndMarginNeedsEquity()
    {
        Assert.AreEqual("trading_available", ShioajiPortfolioParser.ParseTradingLimits("""{"trading_limit":1}""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("equity", ShioajiPortfolioParser.ParseMargin("""{"today_balance":1}""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual(ShioajiErrorCodes.MissingField, ShioajiPortfolioParser.ParseMargin("{}").Error!.Code);
    }

    [TestMethod]
    public void MarginWithOnlyEquityLeavesTheRestNull()
    {
        var margin = ShioajiPortfolioParser.ParseMargin("""{"equity":"123.45"}""").GetValueOrThrow();

        Assert.AreEqual(123.45m, margin.Equity);
        Assert.IsNull(margin.AvailableMargin);
        Assert.IsNull(margin.RiskIndicator);
        Assert.IsNull(margin.MarginCall);
    }

    [TestMethod]
    public void AccountBalanceNeedsABalanceOrAMessage()
    {
        Assert.AreEqual("acc_balance", ShioajiPortfolioParser.ParseAccountBalance("""{"date":"2026-05-19 09:21:10"}""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("acc_balance", ShioajiPortfolioParser.ParseAccountBalance("""{"errmsg":"   "}""").Error!.Data![ShioajiErrorDataKeys.Field], "a blank message is no message");
        Assert.AreEqual(5m, ShioajiPortfolioParser.ParseAccountBalance("""{"acc_balance":"5"}""").GetValueOrThrow().Balance);
        Assert.AreEqual("no", ShioajiPortfolioParser.ParseAccountBalance("""{"errmsg":"no"}""").GetValueOrThrow().ErrorMessage);
    }

    [TestMethod]
    public void AccountBalanceDateWithAnOffsetKeepsItsOwnOffsetAndAnUnreadableDateIsNull()
    {
        var withOffset = ShioajiPortfolioParser.ParseAccountBalance("""{"acc_balance":1,"date":"2026-05-19T09:21:10+09:00"}""").GetValueOrThrow();
        var unreadable = ShioajiPortfolioParser.ParseAccountBalance("""{"acc_balance":1,"date":"yesterday"}""").GetValueOrThrow();

        Assert.AreEqual(new DateTimeOffset(2026, 5, 19, 0, 21, 10, TimeSpan.Zero), withOffset.QueriedAt);
        Assert.IsNull(unreadable.QueriedAt);
    }

    // ───────────────────────── 彙總 / summaries ─────────────────────────

    [TestMethod]
    public void ASummaryAcceptsABareArrayAnEmptyAnswerAndEitherWrapperName()
    {
        var bare = ShioajiPortfolioParser.ParseStockProfitLossSummary("""[{"code":"2330","pnl":1}]""").GetValueOrThrow();
        var renamed = ShioajiPortfolioParser.ParseStockProfitLossSummary("""{"profitloss_summary":[{"code":"2330"}],"total":{"pnl":3}}""").GetValueOrThrow();

        Assert.AreEqual(1m, bare.Items.Single().Pnl);
        Assert.IsNull(bare.Total, "a bare array has no total");
        Assert.AreEqual("2330", renamed.Items.Single().Code);
        Assert.AreEqual(3m, renamed.Total!.Pnl);

        foreach (var body in new[] { string.Empty, "null", "{}" })
        {
            var empty = ShioajiPortfolioParser.ParseFuturesProfitLossSummary(body).GetValueOrThrow();

            Assert.IsEmpty(empty.Items, body);
            Assert.IsNull(empty.Total, body);
        }
    }

    [TestMethod]
    public void ASummaryWithoutTotalOrWithAnEmptyListIsStillASuccess()
    {
        var noTotal = ShioajiPortfolioParser.ParseStockProfitLossSummary("""{"profitloss_sum":[{"code":"2330"}]}""").GetValueOrThrow();
        var noItems = ShioajiPortfolioParser.ParseStockProfitLossSummary("""{"profitloss_sum":[],"total":{"quantity":0}}""").GetValueOrThrow();
        var badTotal = ShioajiPortfolioParser.ParseStockProfitLossSummary("""{"profitloss_sum":[],"total":"n/a"}""").GetValueOrThrow();

        Assert.IsNull(noTotal.Total);
        Assert.IsEmpty(noItems.Items);
        Assert.AreEqual(0m, noItems.Total!.Quantity);
        Assert.IsNull(badTotal.Total, "a total that is not an object is ignored");
    }

    [TestMethod]
    public void ASummaryItemNeedsACodeAndABrokenShapeFails()
    {
        Assert.AreEqual("code", ShioajiPortfolioParser.ParseStockProfitLossSummary("""{"profitloss_sum":[{"pnl":1}]}""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual("code", ShioajiPortfolioParser.ParseFuturesProfitLossSummary("""[{"pnl":1}]""").Error!.Data![ShioajiErrorDataKeys.Field]);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, ShioajiPortfolioParser.ParseStockProfitLossSummary("""{"total":{"pnl":1}}""").Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, ShioajiPortfolioParser.ParseStockProfitLossSummary("42").Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, ShioajiPortfolioParser.ParseStockProfitLossSummary("""{"profitloss_sum":[1]}""").Error!.Code);
    }

    // ───────────────────────── 期貨與明細的欄位 / futures and detail fields ─────────────────────────

    [TestMethod]
    public void FuturesDetailDatesAcceptEveryDocumentedWritingAndTheEntryDateIsIndependent()
    {
        var row = ShioajiPortfolioParser
            .ParseFuturesProfitLossDetails("""[{"code":"TXFK6","quantity":1,"date":"2026/10/30","entry_date":"20261029","entry_seqno":123,"pnl":"8000"}]""")
            .GetValueOrThrow()
            .Single();

        Assert.AreEqual(new DateOnly(2026, 10, 30), row.Date);
        Assert.AreEqual(new DateOnly(2026, 10, 29), row.EntryDate);
        Assert.AreEqual("123", row.EntrySeqno, "a numeric sequence number is kept as text");
        Assert.AreEqual(8000m, row.Pnl);
    }

    [TestMethod]
    public void StockDetailsKeepEveryDocumentedFieldAndLeaveTheAbsentOnesNull()
    {
        var row = ShioajiPortfolioParser
            .ParseStockPositionDetails("""[{"code":"2330","quantity":"2","currency":"USD","ex_dividends":"1.5","margintrading_amt":900}]""")
            .GetValueOrThrow()
            .Single();

        Assert.AreEqual("USD", row.Currency);
        Assert.AreEqual(1.5m, row.ExDividends);
        Assert.AreEqual(900m, row.MarginTradingAmount);
        Assert.IsNull(row.Date);
        Assert.IsNull(row.Fee);
        Assert.IsNull(row.Dseq);
        Assert.IsNull(row.Collateral);
    }
}
