using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.Orders;

namespace Ozakboy.TradeKit.Shioaji.Portfolio;

/// <summary>
/// 帳務回應的對映。
/// Mapping of portfolio responses.
/// </summary>
/// <remarks>
/// <para>
/// <b>回應外形依文件推定,尚未對真 server 實測。</b>文件給的是欄位清單與 <c>settlements</c> 的一個陣列範例,
/// 沒有逐字的回應範例,所以列表型端點三種形狀都收:裸物件陣列、包著陣列的物件(<c>items</c>、<c>data</c>、
/// <c>results</c>、<c>list</c>、<c>rows</c>)、以及 Python SDK 那種欄位陣列物件(<c>{"code":[…],"quantity":[…]}</c>);
/// 單一物件型端點(保證金、交易額度、銀行餘額)收一個物件、只有一個物件的陣列、或只有一列的欄位陣列。
/// 空本文、<c>null</c>、<c>[]</c>、<c>{}</c> 對列表端點是成功的空清單。
/// <b>The response shapes are inferred from the documentation and have not been measured against a real server.</b>
/// The documentation gives field lists and one array example for <c>settlements</c> but no verbatim responses, so the
/// list endpoints accept three shapes: a bare array of objects, an object wrapping an array (<c>items</c>,
/// <c>data</c>, <c>results</c>, <c>list</c>, <c>rows</c>), and the column-array object of the Python SDK
/// (<c>{"code":[…],"quantity":[…]}</c>); the single-object endpoints (margin, trading limits, bank balance) accept
/// an object, an array holding one object, or a column array of one row. An empty body, <c>null</c>, <c>[]</c> and
/// <c>{}</c> are a successful empty list for a list endpoint.
/// </para>
/// <para>
/// <b>嚴格度。</b>每列只有識別欄位(代碼與數量,或日期與金額)缺漏時整筆失敗;其餘欄位缺漏或讀不懂都是
/// <see langword="null"/>(或列舉的 <c>Unknown</c>),絕不補 0。
/// <b>Strictness.</b> Only the identifying fields of a row (code and quantity, or date and amount) fail the whole
/// response when absent; every other field reads as <see langword="null"/> (or <c>Unknown</c> for an enum) when
/// absent or unreadable and is never filled with zero.
/// </para>
/// </remarks>
internal static class ShioajiPortfolioParser
{
    private static readonly string[] ListWrapperNames = ["items", "data", "results", "list", "rows"];

    private static readonly string[] SummaryWrapperNames = ["profitloss_sum", "profitloss_summary", "items", "data", "results", "list", "rows"];

    private static readonly ShioajiWireMap<ShioajiPositionCondition> ConditionMap = new(
        (ShioajiPositionCondition.Cash, "Cash"),
        (ShioajiPositionCondition.Netting, "Netting"),
        (ShioajiPositionCondition.MarginTrading, "MarginTrading"),
        (ShioajiPositionCondition.ShortSelling, "ShortSelling"),
        (ShioajiPositionCondition.Emerging, "Emerging"));

    private static readonly ShioajiWireMap<ShioajiProfitLossTradeType> TradeTypeMap = new(
        (ShioajiProfitLossTradeType.Common, "Common"),
        (ShioajiProfitLossTradeType.DayTrade, "DayTrade"));

    internal static readonly ShioajiWireMap<ShioajiPositionUnit> UnitMap = new(
        (ShioajiPositionUnit.Common, "Common"),
        (ShioajiPositionUnit.Share, "Share"));

    // ───────────────────────── 列表型端點 / list endpoints ─────────────────────────

    public static Result<IReadOnlyList<ShioajiStockPosition>> ParseStockPositions(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioPositionUnit, ListWrapperNames, MapStockPosition);

    public static Result<IReadOnlyList<ShioajiFuturesPosition>> ParseFuturesPositions(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioPositionUnit, ListWrapperNames, MapFuturesPosition);

    public static Result<IReadOnlyList<ShioajiStockPositionDetail>> ParseStockPositionDetails(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioPositionDetail, ListWrapperNames, MapStockPositionDetail);

    public static Result<IReadOnlyList<ShioajiFuturesPositionDetail>> ParseFuturesPositionDetails(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioPositionDetail, ListWrapperNames, MapFuturesPositionDetail);

    public static Result<IReadOnlyList<ShioajiStockProfitLoss>> ParseStockProfitLoss(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioProfitLoss, ListWrapperNames, MapStockProfitLoss);

    public static Result<IReadOnlyList<ShioajiFuturesProfitLoss>> ParseFuturesProfitLoss(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioProfitLoss, ListWrapperNames, MapFuturesProfitLoss);

    public static Result<IReadOnlyList<ShioajiStockProfitLossDetail>> ParseStockProfitLossDetails(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioProfitLossDetail, ListWrapperNames, MapStockProfitLossDetail);

    public static Result<IReadOnlyList<ShioajiFuturesProfitLossDetail>> ParseFuturesProfitLossDetails(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioProfitLossDetail, ListWrapperNames, MapFuturesProfitLossDetail);

    public static Result<IReadOnlyList<ShioajiSettlement>> ParseSettlements(string body) =>
        ParseRows(body, ShioajiApiPaths.PortfolioSettlements, ListWrapperNames, MapSettlement);

    // ───────────────────────── 彙總 / summaries ─────────────────────────

    public static Result<ShioajiProfitLossSummary<ShioajiStockProfitLossSummaryItem>> ParseStockProfitLossSummary(string body) =>
        ParseSummary(body, MapStockSummaryItem);

    public static Result<ShioajiProfitLossSummary<ShioajiFuturesProfitLossSummaryItem>> ParseFuturesProfitLossSummary(string body) =>
        ParseSummary(body, MapFuturesSummaryItem);

    // ───────────────────────── 單一物件型端點 / single-object endpoints ─────────────────────────

    public static Result<ShioajiTradingLimits> ParseTradingLimits(string body) =>
        ParseObject(body, ShioajiApiPaths.PortfolioTradingLimits, MapTradingLimits);

    public static Result<ShioajiMargin> ParseMargin(string body) =>
        ParseObject(body, ShioajiApiPaths.PortfolioMargin, MapMargin);

    public static Result<ShioajiAccountBalance> ParseAccountBalance(string body) =>
        ParseObject(body, ShioajiApiPaths.PortfolioAccountBalance, MapAccountBalance);

    // ───────────────────────── 形狀處理 / shape handling ─────────────────────────

    private static Result<IReadOnlyList<T>> ParseRows<T>(
        string body,
        string context,
        string[] wrapperNames,
        Func<JsonElement, string, Result<T>> mapRow)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<T>();
        }

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<IReadOnlyList<T>>();
        }

        using var document = parsed.GetValueOrThrow();
        var root = document.RootElement;

        switch (root.ValueKind)
        {
            case JsonValueKind.Null:
                return new List<T>();
            case JsonValueKind.Array:
                return MapArray(root, context, mapRow);
            case JsonValueKind.Object:
                if (!root.EnumerateObject().MoveNext())
                {
                    return new List<T>();
                }

                if (TryFindWrappedArray(root, wrapperNames, out var inner))
                {
                    return MapArray(inner, context, mapRow);
                }

                return ParseColumnarRows(body, context, mapRow);
            default:
                return ShioajiErrors.MalformedResponse($"{context} 的回應不是陣列。The response for {context} is not an array.");
        }
    }

    private static Result<IReadOnlyList<T>> MapArray<T>(
        JsonElement array,
        string context,
        Func<JsonElement, string, Result<T>> mapRow)
    {
        var rows = new List<T>(array.GetArrayLength());

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return ShioajiErrors.MalformedResponse($"{context} 的陣列裡有不是 JSON 物件的元素。An element of the array in {context} is not a JSON object.");
            }

            var mapped = mapRow(item, context);

            if (mapped.IsFailure)
            {
                return mapped.ToFailure<IReadOnlyList<T>>();
            }

            rows.Add(mapped.GetValueOrThrow());
        }

        return rows;
    }

    private static bool TryFindWrappedArray(JsonElement root, string[] wrapperNames, out JsonElement array)
    {
        foreach (var name in wrapperNames)
        {
            if (root.TryGetProperty(name, out var inner) && inner.ValueKind == JsonValueKind.Array)
            {
                array = inner;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static Result<IReadOnlyList<T>> ParseColumnarRows<T>(
        string body,
        string context,
        Func<JsonElement, string, Result<T>> mapRow)
    {
        var result = ShioajiColumnarReader.Read<IReadOnlyList<T>>(
            body,
            context,
            [],
            table =>
            {
                var rows = new List<T>(table.RowCount);

                for (var row = 0; row < table.RowCount; row++)
                {
                    var mapped = mapRow(table.RowAsObject(row), context);

                    if (mapped.IsFailure)
                    {
                        return mapped.ToFailure<IReadOnlyList<T>>();
                    }

                    rows.Add(mapped.GetValueOrThrow());
                }

                return rows;
            });

        return AsMalformedResponse(result);
    }

    private static Result<ShioajiProfitLossSummary<TItem>> ParseSummary<TItem>(
        string body,
        Func<JsonElement, string, Result<TItem>> mapItem)
    {
        const string context = ShioajiApiPaths.PortfolioProfitLossSummary;

        if (string.IsNullOrWhiteSpace(body))
        {
            return new ShioajiProfitLossSummary<TItem> { Items = [], Total = null };
        }

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<ShioajiProfitLossSummary<TItem>>();
        }

        using var document = parsed.GetValueOrThrow();
        var root = document.RootElement;
        JsonElement array;
        ShioajiProfitLossTotal? total = null;

        switch (root.ValueKind)
        {
            case JsonValueKind.Null:
                return new ShioajiProfitLossSummary<TItem> { Items = [], Total = null };
            case JsonValueKind.Array:
                array = root;
                break;
            case JsonValueKind.Object:
                if (!root.EnumerateObject().MoveNext())
                {
                    return new ShioajiProfitLossSummary<TItem> { Items = [], Total = null };
                }

                if (!TryFindWrappedArray(root, SummaryWrapperNames, out array))
                {
                    return ShioajiErrors.MalformedResponse($"{context} 的回應是物件,但找不到彙總陣列。The response for {context} is an object with no summary array in it.");
                }

                if (root.TryGetProperty("total", out var totalElement) && totalElement.ValueKind == JsonValueKind.Object)
                {
                    total = MapTotal(totalElement, context);
                }

                break;
            default:
                return ShioajiErrors.MalformedResponse($"{context} 的回應不是物件。The response for {context} is not an object.");
        }

        var items = MapArray(array, context, mapItem);

        return items.IsFailure
            ? items.ToFailure<ShioajiProfitLossSummary<TItem>>()
            : new ShioajiProfitLossSummary<TItem> { Items = items.GetValueOrThrow(), Total = total };
    }

    private static Result<T> ParseObject<T>(string body, string context, Func<JsonElement, string, Result<T>> map)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return ShioajiErrors.MalformedResponse($"{context} 的回應是空的。The response for {context} is empty.");
        }

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<T>();
        }

        using var document = parsed.GetValueOrThrow();
        var root = document.RootElement;

        switch (root.ValueKind)
        {
            case JsonValueKind.Object:
                return root.EnumerateObject().Any(property => property.Value.ValueKind == JsonValueKind.Array)
                    ? ParseColumnarSingle(body, context, map)
                    : map(root, context);
            case JsonValueKind.Array when root.GetArrayLength() == 1 && root[0].ValueKind == JsonValueKind.Object:
                return map(root[0], context);
            default:
                return ShioajiErrors.MalformedResponse($"{context} 的回應不是單一物件。The response for {context} is not a single object.");
        }
    }

    private static Result<T> ParseColumnarSingle<T>(string body, string context, Func<JsonElement, string, Result<T>> map)
    {
        var result = ShioajiColumnarReader.Read<T>(
            body,
            context,
            [],
            table => table.RowCount == 1
                ? map(table.RowAsObject(0), context)
                : ShioajiErrors.MalformedResponse($"{context} 的欄位陣列應該剛好一列,實際有 {table.RowCount} 列。The column arrays of {context} should hold exactly one row but hold {table.RowCount}."));

        return AsMalformedResponse(result);
    }

    /// <summary>
    /// 欄位陣列讀取器的失敗碼是歷史資料專屬的 <c>shioaji.history.malformed</c>;帳務回應看不懂就是一般的 <c>shioaji.response.malformed</c>。
    /// The column-array reader fails with the history-specific <c>shioaji.history.malformed</c>; an unreadable portfolio
    /// response is the general <c>shioaji.response.malformed</c>.
    /// </summary>
    private static Result<T> AsMalformedResponse<T>(Result<T> result) =>
        result.IsFailure && string.Equals(result.Error.Code, ShioajiErrorCodes.HistoryMalformed, StringComparison.Ordinal)
            ? ShioajiErrors.MalformedResponse(result.Error.Message)
            : result;

    // ───────────────────────── 欄位小工具 / field helpers ─────────────────────────

    /// <summary>
    /// 列舉字串可能帶型別前綴(<c>Action.Buy</c>、<c>StockOrderCond.Cash</c>),只看最後一段。
    /// An enum string may carry a type prefix (<c>Action.Buy</c>, <c>StockOrderCond.Cash</c>); only the last segment
    /// counts.
    /// </summary>
    private static string? EnumTail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        var dot = trimmed.LastIndexOf('.');

        return dot >= 0 ? trimmed[(dot + 1)..] : trimmed;
    }

    private static ShioajiAction Direction(ShioajiFieldReader fields) =>
        ShioajiWire.Action.Parse(EnumTail(fields.OptionalString("direction")));

    private static ShioajiPositionCondition Condition(ShioajiFieldReader fields) =>
        ConditionMap.Parse(EnumTail(fields.OptionalString("cond")));

    private static DateOnly? Date(ShioajiFieldReader fields, string name) =>
        ShioajiTimestamps.TryReadDate(fields.OptionalString(name), out var date) ? date : null;

    // ───────────────────────── 各列對映 / row mappings ─────────────────────────

    private static Result<ShioajiStockPosition> MapStockPosition(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var quantity = fields.RequiredDecimal("quantity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiStockPosition
        {
            Id = fields.OptionalInt64("id"),
            Code = code,
            Direction = Direction(fields),
            Quantity = quantity,
            Price = fields.OptionalDecimal("price"),
            LastPrice = fields.OptionalDecimal("last_price"),
            Pnl = fields.OptionalDecimal("pnl"),
            YdQuantity = fields.OptionalDecimal("yd_quantity"),
            Condition = Condition(fields),
            MarginPurchaseAmount = fields.OptionalDecimal("margin_purchase_amount"),
            Collateral = fields.OptionalDecimal("collateral"),
            ShortSaleMargin = fields.OptionalDecimal("short_sale_margin"),
            Interest = fields.OptionalDecimal("interest"),
        };
    }

    private static Result<ShioajiFuturesPosition> MapFuturesPosition(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var quantity = fields.RequiredDecimal("quantity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiFuturesPosition
        {
            Id = fields.OptionalInt64("id"),
            Code = code,
            Direction = Direction(fields),
            Quantity = quantity,
            Price = fields.OptionalDecimal("price"),
            LastPrice = fields.OptionalDecimal("last_price"),
            Pnl = fields.OptionalDecimal("pnl"),
        };
    }

    private static Result<ShioajiStockPositionDetail> MapStockPositionDetail(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var quantity = fields.RequiredDecimal("quantity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiStockPositionDetail
        {
            Date = Date(fields, "date"),
            Code = code,
            Quantity = quantity,
            Price = fields.OptionalDecimal("price"),
            LastPrice = fields.OptionalDecimal("last_price"),
            Dseq = fields.OptionalString("dseq"),
            Direction = Direction(fields),
            Pnl = fields.OptionalDecimal("pnl"),
            Currency = fields.OptionalString("currency"),
            Fee = fields.OptionalDecimal("fee"),
            Condition = Condition(fields),
            ExDividends = fields.OptionalDecimal("ex_dividends"),
            Interest = fields.OptionalDecimal("interest"),
            MarginTradingAmount = fields.OptionalDecimal("margintrading_amt"),
            Collateral = fields.OptionalDecimal("collateral"),
        };
    }

    private static Result<ShioajiFuturesPositionDetail> MapFuturesPositionDetail(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var quantity = fields.RequiredDecimal("quantity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiFuturesPositionDetail
        {
            Date = Date(fields, "date"),
            Code = code,
            Quantity = quantity,
            Price = fields.OptionalDecimal("price"),
            LastPrice = fields.OptionalDecimal("last_price"),
            Dseq = fields.OptionalString("dseq"),
            Direction = Direction(fields),
            Pnl = fields.OptionalDecimal("pnl"),
            Currency = fields.OptionalString("currency"),
            Fee = fields.OptionalDecimal("fee"),
            EntryQuantity = fields.OptionalDecimal("entry_quantity"),
        };
    }

    private static Result<ShioajiStockProfitLoss> MapStockProfitLoss(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var quantity = fields.RequiredDecimal("quantity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiStockProfitLoss
        {
            Id = fields.OptionalInt64("id"),
            Code = code,
            Quantity = quantity,
            Pnl = fields.OptionalDecimal("pnl"),
            Date = Date(fields, "date"),
            Dseq = fields.OptionalString("dseq"),
            Price = fields.OptionalDecimal("price"),
            PrRatio = fields.OptionalDecimal("pr_ratio"),
            Condition = Condition(fields),
            Seqno = fields.OptionalString("seqno"),
        };
    }

    private static Result<ShioajiFuturesProfitLoss> MapFuturesProfitLoss(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var quantity = fields.RequiredDecimal("quantity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiFuturesProfitLoss
        {
            Id = fields.OptionalInt64("id"),
            Code = code,
            Quantity = quantity,
            Pnl = fields.OptionalDecimal("pnl"),
            Date = Date(fields, "date"),
            EntryPrice = fields.OptionalDecimal("entry_price"),
            CoverPrice = fields.OptionalDecimal("cover_price"),
            Tax = fields.OptionalDecimal("tax"),
            Fee = fields.OptionalDecimal("fee"),
            Direction = Direction(fields),
        };
    }

    private static Result<ShioajiStockProfitLossDetail> MapStockProfitLossDetail(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var quantity = fields.RequiredDecimal("quantity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiStockProfitLossDetail
        {
            Date = Date(fields, "date"),
            Code = code,
            Quantity = quantity,
            Dseq = fields.OptionalString("dseq"),
            Fee = fields.OptionalDecimal("fee"),
            Tax = fields.OptionalDecimal("tax"),
            Currency = fields.OptionalString("currency"),
            Price = fields.OptionalDecimal("price"),
            Cost = fields.OptionalDecimal("cost"),
            RepMarginTradingAmount = fields.OptionalDecimal("rep_margintrading_amt"),
            RepCollateral = fields.OptionalDecimal("rep_collateral"),
            RepMargin = fields.OptionalDecimal("rep_margin"),
            ShortSellingFee = fields.OptionalDecimal("shortselling_fee"),
            ExDividendAmount = fields.OptionalDecimal("ex_dividend_amt"),
            Interest = fields.OptionalDecimal("interest"),
            TradeType = TradeTypeMap.Parse(EnumTail(fields.OptionalString("trade_type"))),
            Condition = Condition(fields),
        };
    }

    private static Result<ShioajiFuturesProfitLossDetail> MapFuturesProfitLossDetail(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");
        var quantity = fields.RequiredDecimal("quantity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiFuturesProfitLossDetail
        {
            Date = Date(fields, "date"),
            Code = code,
            Quantity = quantity,
            Dseq = fields.OptionalString("dseq"),
            Fee = fields.OptionalDecimal("fee"),
            Tax = fields.OptionalDecimal("tax"),
            Currency = fields.OptionalString("currency"),
            Direction = Direction(fields),
            EntryDate = Date(fields, "entry_date"),
            EntryQuantity = fields.OptionalDecimal("entry_quantity"),
            EntrySeqno = fields.OptionalString("entry_seqno"),
            EntryPrice = fields.OptionalDecimal("entry_price"),
            CoverPrice = fields.OptionalDecimal("cover_price"),
            Pnl = fields.OptionalDecimal("pnl"),
        };
    }

    private static Result<ShioajiStockProfitLossSummaryItem> MapStockSummaryItem(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiStockProfitLossSummaryItem
        {
            Code = code,
            Quantity = fields.OptionalDecimal("quantity"),
            EntryPrice = fields.OptionalDecimal("entry_price"),
            CoverPrice = fields.OptionalDecimal("cover_price"),
            Pnl = fields.OptionalDecimal("pnl"),
            Currency = fields.OptionalString("currency"),
            EntryCost = fields.OptionalDecimal("entry_cost"),
            CoverCost = fields.OptionalDecimal("cover_cost"),
            BuyCost = fields.OptionalDecimal("buy_cost"),
            SellCost = fields.OptionalDecimal("sell_cost"),
            PrRatio = fields.OptionalDecimal("pr_ratio"),
            Condition = Condition(fields),
        };
    }

    private static Result<ShioajiFuturesProfitLossSummaryItem> MapFuturesSummaryItem(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var code = fields.RequiredString("code");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiFuturesProfitLossSummaryItem
        {
            Code = code,
            Quantity = fields.OptionalDecimal("quantity"),
            EntryPrice = fields.OptionalDecimal("entry_price"),
            CoverPrice = fields.OptionalDecimal("cover_price"),
            Pnl = fields.OptionalDecimal("pnl"),
            Currency = fields.OptionalString("currency"),
            Direction = Direction(fields),
            Tax = fields.OptionalDecimal("tax"),
            Fee = fields.OptionalDecimal("fee"),
        };
    }

    private static ShioajiProfitLossTotal MapTotal(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);

        return new ShioajiProfitLossTotal
        {
            EntryAmount = fields.OptionalDecimal("entry_amount"),
            CoverAmount = fields.OptionalDecimal("cover_amount"),
            Quantity = fields.OptionalDecimal("quantity"),
            BuyCost = fields.OptionalDecimal("buy_cost"),
            SellCost = fields.OptionalDecimal("sell_cost"),
            Pnl = fields.OptionalDecimal("pnl"),
            PrRatio = fields.OptionalDecimal("pr_ratio"),
        };
    }

    private static Result<ShioajiSettlement> MapSettlement(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var amount = fields.RequiredDecimal("amount");
        var tPlus = fields.OptionalInt64("T") ?? fields.OptionalInt64("t");

        if (!ShioajiTimestamps.TryReadDate(fields.OptionalString("date"), out var date))
        {
            fields.Fail("date");
        }

        if (tPlus is null or < 0 or > int.MaxValue)
        {
            fields.Fail("T");
        }

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiSettlement { Date = date, Amount = amount, TPlus = (int)(tPlus ?? 0L) };
    }

    private static Result<ShioajiTradingLimits> MapTradingLimits(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var available = fields.RequiredDecimal("trading_available");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiTradingLimits
        {
            TradingLimit = fields.OptionalDecimal("trading_limit"),
            TradingUsed = fields.OptionalDecimal("trading_used"),
            TradingAvailable = available,
            MarginLimit = fields.OptionalDecimal("margin_limit"),
            MarginUsed = fields.OptionalDecimal("margin_used"),
            MarginAvailable = fields.OptionalDecimal("margin_available"),
            ShortLimit = fields.OptionalDecimal("short_limit"),
            ShortUsed = fields.OptionalDecimal("short_used"),
            ShortAvailable = fields.OptionalDecimal("short_available"),
        };
    }

    private static Result<ShioajiMargin> MapMargin(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var equity = fields.RequiredDecimal("equity");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiMargin
        {
            YesterdayBalance = fields.OptionalDecimal("yesterday_balance"),
            TodayBalance = fields.OptionalDecimal("today_balance"),
            DepositWithdrawal = fields.OptionalDecimal("deposit_withdrawal"),
            Fee = fields.OptionalDecimal("fee"),
            Tax = fields.OptionalDecimal("tax"),
            InitialMargin = fields.OptionalDecimal("initial_margin"),
            MaintenanceMargin = fields.OptionalDecimal("maintenance_margin"),
            MarginCall = fields.OptionalDecimal("margin_call"),
            RiskIndicator = fields.OptionalDecimal("risk_indicator"),
            RoyaltyRevenueExpenditure = fields.OptionalDecimal("royalty_revenue_expenditure"),
            Equity = equity,
            EquityAmount = fields.OptionalDecimal("equity_amount"),
            OptionOpenBuyMarketValue = fields.OptionalDecimal("option_openbuy_market_value"),
            OptionOpenSellMarketValue = fields.OptionalDecimal("option_opensell_market_value"),
            OptionOpenPosition = fields.OptionalDecimal("option_open_position"),
            OptionSettleProfitLoss = fields.OptionalDecimal("option_settle_profitloss"),
            FutureOpenPosition = fields.OptionalDecimal("future_open_position"),
            TodayFutureOpenPosition = fields.OptionalDecimal("today_future_open_position"),
            FutureSettleProfitLoss = fields.OptionalDecimal("future_settle_profitloss"),
            AvailableMargin = fields.OptionalDecimal("available_margin"),
            PlusMargin = fields.OptionalDecimal("plus_margin"),
            PlusMarginIndicator = fields.OptionalDecimal("plus_margin_indicator"),
            SecurityCollateralAmount = fields.OptionalDecimal("security_collateral_amount"),
            OrderMarginPremium = fields.OptionalDecimal("order_margin_premium"),
            CollateralAmount = fields.OptionalDecimal("collateral_amount"),
        };
    }

    private static Result<ShioajiAccountBalance> MapAccountBalance(JsonElement element, string context)
    {
        var fields = new ShioajiFieldReader(element, context);
        var balance = fields.OptionalDecimal("acc_balance");
        var message = fields.OptionalString("errmsg");

        if (string.IsNullOrWhiteSpace(message))
        {
            message = null;
        }

        // 餘額與錯誤訊息都沒有,就是看不懂的回應;有錯誤訊息時餘額缺漏是預期的(其他銀行、查詢失敗)。
        // With neither a balance nor a message the response is unreadable; with a message a missing balance is
        // expected (an unsupported bank, a failed query).
        if (balance is null && message is null)
        {
            fields.Fail("acc_balance");
            return fields.Error!;
        }

        DateTimeOffset? queriedAt = null;

        if (ShioajiTimestamps.TryFromDateTimeText(fields.OptionalString("date"), out var parsed))
        {
            queriedAt = parsed;
        }

        return new ShioajiAccountBalance { Balance = balance, QueriedAt = queriedAt, ErrorMessage = message };
    }
}
