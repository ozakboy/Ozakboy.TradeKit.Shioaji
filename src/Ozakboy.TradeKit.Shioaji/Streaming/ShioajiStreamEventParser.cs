using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.Orders;

namespace Ozakboy.TradeKit.Shioaji.Streaming;

/// <summary>
/// 把一個 SSE 事件(事件名加 <c>data</c> 原文)對映成型別化的事件。
/// Maps one SSE event, an event name plus the raw <c>data</c>, to a typed event.
/// </summary>
/// <remarks>
/// <para>
/// <b>分類順序。</b>
/// <b>The classification order.</b>
/// </para>
/// <list type="number">
/// <item><description>
/// 事件名是 <c>heartbeat</c> 或 <c>ping</c>(不分大小寫)、或事件名空白 → <see cref="ShioajiHeartbeat"/>。
/// The name is <c>heartbeat</c> or <c>ping</c>, case-insensitively, or blank → <see cref="ShioajiHeartbeat"/>.
/// </description></item>
/// <item><description>
/// <c>data</c> 不是 JSON 物件:事件名是已知的行情 / 委託事件 → 失敗(讀不懂的事件,不能當心跳吞掉);
/// 其他事件名 → <see cref="ShioajiHeartbeat"/>(格式未知的心跳)。
/// <c>data</c> is not a JSON object: a known market or order event name → failure (an unreadable event must not
/// be swallowed as a heartbeat); any other name → <see cref="ShioajiHeartbeat"/> (a heartbeat of unknown format).
/// </description></item>
/// <item><description>
/// 已知事件名 → 型別化事件,必要欄位缺漏 → 失敗。
/// A known name → a typed event, with a failure when a required field is missing.
/// </description></item>
/// <item><description>
/// 其他事件名且 <c>data</c> 是 JSON 物件 → <see cref="ShioajiUnknownEvent"/>。
/// Any other name with a JSON-object <c>data</c> → <see cref="ShioajiUnknownEvent"/>.
/// </description></item>
/// </list>
/// </remarks>
internal static class ShioajiStreamEventParser
{
    public const string TickStk = "tick_stk";
    public const string BidAskStk = "bidask_stk";
    public const string QuoteStk = "quote_stk";
    public const string TickFop = "tick_fop";
    public const string BidAskFop = "bidask_fop";
    public const string QuoteFop = "quote_fop";
    public const string QuoteIdx = "quote_idx";
    public const string OrderEvent = "order_event";

    private const int BookDepth = 5;

    public static Result<ShioajiStreamEvent> Parse(string eventName, string data, DateTimeOffset receivedAt)
    {
        var name = eventName.Trim();

        if (name.Length == 0
            || name.Equals("heartbeat", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ping", StringComparison.OrdinalIgnoreCase))
        {
            return Heartbeat(name, data, receivedAt);
        }

        var known = IsKnown(name);
        JsonDocument? document = null;

        try
        {
            if (!TryParseObject(data, out document))
            {
                return known
                    ? ShioajiErrors.MalformedEvent(name, "data 不是 JSON 物件。The data is not a JSON object.")
                    : Heartbeat(name, data, receivedAt);
            }

            var root = document.RootElement;

            return name switch
            {
                TickStk => Wrap(ReadTickStk(root, receivedAt)),
                BidAskStk => Wrap(ReadBidAskStk(root, receivedAt)),
                QuoteStk => Wrap(ReadQuoteStk(root, receivedAt)),
                TickFop => Wrap(ReadTickFop(root, receivedAt)),
                BidAskFop => Wrap(ReadBidAskFop(root, receivedAt)),
                QuoteFop => Wrap(ReadQuoteFop(root, receivedAt)),
                QuoteIdx => Wrap(ReadQuoteIdx(root, receivedAt)),
                OrderEvent => Wrap(ReadOrderEvent(root, data, receivedAt)),
                _ => new ShioajiUnknownEvent { ReceivedAt = receivedAt, EventName = name, RawData = data },
            };
        }
        finally
        {
            document?.Dispose();
        }
    }

    private static bool IsKnown(string name) =>
        name is TickStk or BidAskStk or QuoteStk or TickFop or BidAskFop or QuoteFop or QuoteIdx or OrderEvent;

    private static Result<ShioajiStreamEvent> Heartbeat(string name, string data, DateTimeOffset receivedAt) =>
        new ShioajiHeartbeat { ReceivedAt = receivedAt, EventName = name, RawData = data };

    private static Result<ShioajiStreamEvent> Wrap<T>(Result<T> result)
        where T : ShioajiStreamEvent =>
        result.IsFailure ? result.ToFailure<ShioajiStreamEvent>() : result.GetValueOrThrow();

    private static bool TryParseObject(string data, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out JsonDocument? document)
    {
        document = null;

        if (string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        try
        {
            var parsed = JsonDocument.Parse(data);

            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }

            document = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- 股票
    // ---------------------------------------------------------------- Stocks

    private static Result<ShioajiTickStk> ReadTickStk(JsonElement root, DateTimeOffset receivedAt)
    {
        var f = new ShioajiFieldReader(root, TickStk);
        var code = f.RequiredString("code");
        var time = RequiredTime(f);
        var close = f.RequiredDecimal("close");

        var tick = new ShioajiTickStk
        {
            ReceivedAt = receivedAt,
            Code = code,
            Exchange = f.OptionalString("exchange"),
            Time = time,
            Open = f.OptionalDecimal("open"),
            AvgPrice = f.OptionalDecimal("avg_price"),
            Close = close,
            High = f.OptionalDecimal("high"),
            Low = f.OptionalDecimal("low"),
            Amount = f.OptionalDecimal("amount"),
            TotalAmount = f.OptionalDecimal("total_amount"),
            Volume = f.OptionalDecimal("volume"),
            TotalVolume = f.OptionalDecimal("total_volume"),
            TickType = TickType(f),
            ChgType = ChgType(f),
            PriceChg = f.OptionalDecimal("price_chg"),
            PctChg = f.OptionalDecimal("pct_chg"),
            BidSideTotalVol = f.OptionalDecimal("bid_side_total_vol"),
            AskSideTotalVol = f.OptionalDecimal("ask_side_total_vol"),
            BidSideTotalCnt = f.OptionalInt64("bid_side_total_cnt"),
            AskSideTotalCnt = f.OptionalInt64("ask_side_total_cnt"),
            ClosingOddlotShares = f.OptionalDecimal("closing_oddlot_shares"),
            ClosingOddlotClose = f.OptionalDecimal("closing_oddlot_close"),
            ClosingOddlotAmount = f.OptionalDecimal("closing_oddlot_amount"),
            ClosingOddlotBidPrice = f.OptionalDecimal("closing_oddlot_bid_price"),
            ClosingOddlotAskPrice = f.OptionalDecimal("closing_oddlot_ask_price"),
            FixedTradeVol = f.OptionalDecimal("fixed_trade_vol"),
            FixedTradeAmount = f.OptionalDecimal("fixed_trade_amount"),
            Suspend = f.OptionalBool("suspend") ?? false,
            Simtrade = f.OptionalBool("simtrade") ?? false,
            IntradayOdd = f.OptionalBool("intraday_odd") ?? false,
        };

        return Finish(f, tick);
    }

    private static Result<ShioajiBidAskStk> ReadBidAskStk(JsonElement root, DateTimeOffset receivedAt)
    {
        var f = new ShioajiFieldReader(root, BidAskStk);
        var code = f.RequiredString("code");
        var time = RequiredTime(f);
        var bidPrice = RequiredBook(f, "bid_price");
        var bidVolume = RequiredBook(f, "bid_volume");
        var askPrice = RequiredBook(f, "ask_price");
        var askVolume = RequiredBook(f, "ask_volume");

        var book = new ShioajiBidAskStk
        {
            ReceivedAt = receivedAt,
            Code = code,
            Exchange = f.OptionalString("exchange"),
            Time = time,
            BidPrice = bidPrice,
            BidVolume = bidVolume,
            DiffBidVol = OptionalBook(f, "diff_bid_vol"),
            AskPrice = askPrice,
            AskVolume = askVolume,
            DiffAskVol = OptionalBook(f, "diff_ask_vol"),
            Suspend = f.OptionalBool("suspend") ?? false,
            Simtrade = f.OptionalBool("simtrade") ?? false,
            IntradayOdd = f.OptionalBool("intraday_odd") ?? false,
        };

        return Finish(f, book);
    }

    private static Result<ShioajiQuoteStk> ReadQuoteStk(JsonElement root, DateTimeOffset receivedAt)
    {
        var f = new ShioajiFieldReader(root, QuoteStk);
        var code = f.RequiredString("code");
        var time = RequiredTime(f);
        var close = f.RequiredDecimal("close");
        var bidPrice = RequiredBook(f, "bid_price");
        var bidVolume = RequiredBook(f, "bid_volume");
        var askPrice = RequiredBook(f, "ask_price");
        var askVolume = RequiredBook(f, "ask_volume");

        var quote = new ShioajiQuoteStk
        {
            ReceivedAt = receivedAt,
            Code = code,
            Exchange = f.OptionalString("exchange"),
            Time = time,
            Open = f.OptionalDecimal("open"),
            AvgPrice = f.OptionalDecimal("avg_price"),
            Close = close,
            High = f.OptionalDecimal("high"),
            Low = f.OptionalDecimal("low"),
            Amount = f.OptionalDecimal("amount"),
            TotalAmount = f.OptionalDecimal("total_amount"),
            Volume = f.OptionalDecimal("volume"),
            TotalVolume = f.OptionalDecimal("total_volume"),
            TickType = TickType(f),
            ChgType = ChgType(f),
            PriceChg = f.OptionalDecimal("price_chg"),
            PctChg = f.OptionalDecimal("pct_chg"),
            BidSideTotalVol = f.OptionalDecimal("bid_side_total_vol"),
            AskSideTotalVol = f.OptionalDecimal("ask_side_total_vol"),
            BidSideTotalCnt = f.OptionalInt64("bid_side_total_cnt"),
            AskSideTotalCnt = f.OptionalInt64("ask_side_total_cnt"),
            BidPrice = bidPrice,
            BidVolume = bidVolume,
            DiffBidVol = OptionalBook(f, "diff_bid_vol"),
            AskPrice = askPrice,
            AskVolume = askVolume,
            DiffAskVol = OptionalBook(f, "diff_ask_vol"),
            AvailBorrowing = f.OptionalDecimal("avail_borrowing"),
            Suspend = f.OptionalBool("suspend") ?? false,
            Simtrade = f.OptionalBool("simtrade") ?? false,
        };

        return Finish(f, quote);
    }

    // ---------------------------------------------------------------- 期貨與選擇權
    // ---------------------------------------------------------------- Futures and options

    private static Result<ShioajiTickFop> ReadTickFop(JsonElement root, DateTimeOffset receivedAt)
    {
        var f = new ShioajiFieldReader(root, TickFop);
        var code = f.RequiredString("code");
        var time = RequiredTime(f);
        var close = f.RequiredDecimal("close");

        var tick = new ShioajiTickFop
        {
            ReceivedAt = receivedAt,
            Code = code,
            Exchange = f.OptionalString("exchange"),
            Time = time,
            Open = f.OptionalDecimal("open"),
            UnderlyingPrice = f.OptionalDecimal("underlying_price"),
            BidSideTotalVol = f.OptionalDecimal("bid_side_total_vol"),
            AskSideTotalVol = f.OptionalDecimal("ask_side_total_vol"),
            AvgPrice = f.OptionalDecimal("avg_price"),
            Close = close,
            High = f.OptionalDecimal("high"),
            Low = f.OptionalDecimal("low"),
            Amount = f.OptionalDecimal("amount"),
            TotalAmount = f.OptionalDecimal("total_amount"),
            Volume = f.OptionalDecimal("volume"),
            TotalVolume = f.OptionalDecimal("total_volume"),
            TickType = TickType(f),
            ChgType = ChgType(f),
            PriceChg = f.OptionalDecimal("price_chg"),
            PctChg = f.OptionalDecimal("pct_chg"),
            Simtrade = f.OptionalBool("simtrade") ?? false,
        };

        return Finish(f, tick);
    }

    private static Result<ShioajiBidAskFop> ReadBidAskFop(JsonElement root, DateTimeOffset receivedAt)
    {
        var f = new ShioajiFieldReader(root, BidAskFop);
        var code = f.RequiredString("code");
        var time = RequiredTime(f);
        var bidPrice = RequiredBook(f, "bid_price");
        var bidVolume = RequiredBook(f, "bid_volume");
        var askPrice = RequiredBook(f, "ask_price");
        var askVolume = RequiredBook(f, "ask_volume");

        var book = new ShioajiBidAskFop
        {
            ReceivedAt = receivedAt,
            Code = code,
            Exchange = f.OptionalString("exchange"),
            Time = time,
            BidTotalVol = f.OptionalDecimal("bid_total_vol"),
            AskTotalVol = f.OptionalDecimal("ask_total_vol"),
            BidPrice = bidPrice,
            BidVolume = bidVolume,
            DiffBidVol = OptionalBook(f, "diff_bid_vol"),
            AskPrice = askPrice,
            AskVolume = askVolume,
            DiffAskVol = OptionalBook(f, "diff_ask_vol"),
            FirstDerivedBidPrice = f.OptionalDecimal("first_derived_bid_price"),
            FirstDerivedAskPrice = f.OptionalDecimal("first_derived_ask_price"),
            FirstDerivedBidVol = f.OptionalDecimal("first_derived_bid_vol"),
            FirstDerivedAskVol = f.OptionalDecimal("first_derived_ask_vol"),
            UnderlyingPrice = f.OptionalDecimal("underlying_price"),
            Simtrade = f.OptionalBool("simtrade") ?? false,
        };

        return Finish(f, book);
    }

    private static Result<ShioajiQuoteFop> ReadQuoteFop(JsonElement root, DateTimeOffset receivedAt)
    {
        var f = new ShioajiFieldReader(root, QuoteFop);
        var code = f.RequiredString("code");
        var time = RequiredTime(f);
        var close = f.RequiredDecimal("close");
        var bidPrice = RequiredBook(f, "bid_price");
        var bidVolume = RequiredBook(f, "bid_volume");
        var askPrice = RequiredBook(f, "ask_price");
        var askVolume = RequiredBook(f, "ask_volume");

        var quote = new ShioajiQuoteFop
        {
            ReceivedAt = receivedAt,
            Code = code,
            Exchange = f.OptionalString("exchange"),
            Time = time,
            Open = f.OptionalDecimal("open"),
            Close = close,
            High = f.OptionalDecimal("high"),
            Low = f.OptionalDecimal("low"),
            AvgPrice = f.OptionalDecimal("avg_price"),
            Amount = f.OptionalDecimal("amount"),
            TotalAmount = f.OptionalDecimal("total_amount"),
            Volume = f.OptionalDecimal("volume"),
            TotalVolume = f.OptionalDecimal("total_volume"),
            TickType = TickType(f),
            ChgType = ChgType(f),
            PriceChg = f.OptionalDecimal("price_chg"),
            PctChg = f.OptionalDecimal("pct_chg"),
            UnderlyingPrice = f.OptionalDecimal("underlying_price"),
            BidPrice = bidPrice,
            BidVolume = bidVolume,
            DiffBidVol = OptionalBook(f, "diff_bid_vol"),
            AskPrice = askPrice,
            AskVolume = askVolume,
            DiffAskVol = OptionalBook(f, "diff_ask_vol"),
            Simtrade = f.OptionalBool("simtrade") ?? false,
        };

        return Finish(f, quote);
    }

    private static Result<ShioajiQuoteIdx> ReadQuoteIdx(JsonElement root, DateTimeOffset receivedAt)
    {
        var f = new ShioajiFieldReader(root, QuoteIdx);
        var code = f.RequiredString("code");
        var time = RequiredTime(f);
        var close = f.RequiredDecimal("close");

        var quote = new ShioajiQuoteIdx
        {
            ReceivedAt = receivedAt,
            Code = code,
            Exchange = f.OptionalString("exchange"),
            Time = time,
            Open = f.OptionalDecimal("open"),
            High = f.OptionalDecimal("high"),
            Low = f.OptionalDecimal("low"),
            Close = close,
            Amount = f.OptionalDecimal("amount"),
            TotalAmount = f.OptionalDecimal("total_amount"),
            Volume = f.OptionalDecimal("volume"),
            TotalVolume = f.OptionalDecimal("total_volume"),
            PriceChg = f.OptionalDecimal("price_chg"),
            PctChg = f.OptionalDecimal("pct_chg"),
        };

        return Finish(f, quote);
    }

    // ---------------------------------------------------------------- 委託回報
    // ---------------------------------------------------------------- Order reports

    private static Result<ShioajiOrderEvent> ReadOrderEvent(JsonElement root, string rawJson, DateTimeOffset receivedAt)
    {
        var f = new ShioajiFieldReader(root, OrderEvent);

        // event_id 是去重用的鍵:缺漏就無法去重,同一筆回報重複送達會被當成兩筆,所以不補任何值。
        // event_id is the de-duplication key: without it a repeated report counts twice, so nothing is invented.
        var eventId = f.RequiredString("event_id");

        if (!f.IsValid)
        {
            return f.Error!;
        }

        ShioajiOrderEventDeal? deal = null;
        var isDeal = false;

        if (f.TryGet("deal", out var dealElement) && dealElement.ValueKind == JsonValueKind.Object)
        {
            deal = ReadDeal(dealElement);
            isDeal = true;
        }
        else if (f.TryGet("trade_id", out _) && f.TryGet("exchange_seq", out _))
        {
            deal = ReadDeal(root);
            isDeal = true;
        }

        var explicitType = f.OptionalString("event_type");
        var type = explicitType is not null
            ? ParseEventType(explicitType)
            : (isDeal ? ShioajiOrderEventType.Deal : ShioajiOrderEventType.Order);

        ShioajiOrder? order = null;

        if (f.TryGet("order", out var orderElement))
        {
            var parsedOrder = ShioajiTradeParser.ParseOrder(orderElement, OrderEvent);
            order = parsedOrder.IsSuccess ? parsedOrder.GetValueOrThrow() : null;
        }

        ShioajiContract? contract = null;

        if (f.TryGet("contract", out var contractElement))
        {
            var parsedContract = ShioajiContractParser.ParseContract(contractElement, OrderEvent);
            contract = parsedContract.IsSuccess ? parsedContract.GetValueOrThrow() : null;
        }

        return new ShioajiOrderEvent
        {
            ReceivedAt = receivedAt,
            EventId = eventId,
            EventType = type,
            Operation = ReadOperation(f),
            Order = order,
            Status = ReadEventStatus(f),
            Deal = deal,
            Contract = contract,
            RawJson = rawJson,
        };
    }

    private static ShioajiOrderEventType ParseEventType(string text)
    {
        var lowered = text.Trim().ToUpperInvariant();

        if (lowered.Contains("DEAL", StringComparison.Ordinal))
        {
            return ShioajiOrderEventType.Deal;
        }

        return lowered.Contains("ORDER", StringComparison.Ordinal) ? ShioajiOrderEventType.Order : ShioajiOrderEventType.Unknown;
    }

    private static ShioajiOrderOperation? ReadOperation(ShioajiFieldReader f)
    {
        if (!f.TryGet("operation", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var o = new ShioajiFieldReader(element, "order_event.operation");

        return new ShioajiOrderOperation
        {
            OpType = o.OptionalString("op_type"),
            OpCode = o.OptionalString("op_code"),
            OpMsg = o.OptionalString("op_msg"),
        };
    }

    private static ShioajiOrderEventStatus? ReadEventStatus(ShioajiFieldReader f)
    {
        if (!f.TryGet("status", out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var s = new ShioajiFieldReader(element, "order_event.status");
        DateTimeOffset? exchangeTs = null;

        if (s.TryGet("exchange_ts", out var ts) && ShioajiTimestamps.TryRead(ts, out var read))
        {
            exchangeTs = read;
        }

        return new ShioajiOrderEventStatus
        {
            Id = s.OptionalString("id"),
            ExchangeTs = exchangeTs,
            ModifiedPrice = s.OptionalDecimal("modified_price"),
            CancelQuantity = s.OptionalDecimal("cancel_quantity"),
            OrderQuantity = s.OptionalDecimal("order_quantity"),
            WebId = s.OptionalString("web_id"),
        };
    }

    private static ShioajiOrderEventDeal? ReadDeal(JsonElement element)
    {
        var d = new ShioajiFieldReader(element, "order_event.deal");
        var price = d.OptionalDecimal("price");
        var quantity = d.OptionalDecimal("quantity");

        // 成交沒有價或量就不是成交:回 null,讓 Order / Status 照常送出,RawJson 仍可看到原文。
        // A fill without a price or quantity is not a fill: null, so Order and Status are still delivered and the
        // original text is still in RawJson.
        if (price is null || quantity is null)
        {
            return null;
        }

        DateTimeOffset? ts = null;

        if (d.TryGet("ts", out var stamp) && ShioajiTimestamps.TryRead(stamp, out var read))
        {
            ts = read;
        }

        return new ShioajiOrderEventDeal
        {
            TradeId = d.OptionalString("trade_id"),
            Seqno = d.OptionalString("seqno"),
            Ordno = d.OptionalString("ordno"),
            ExchangeSeq = d.OptionalString("exchange_seq"),
            BrokerId = d.OptionalString("broker_id"),
            AccountId = d.OptionalString("account_id"),
            Action = ShioajiWire.Action.Parse(d.OptionalString("action")),
            Code = d.OptionalString("code"),
            OrderCondition = ShioajiWire.OrderCondition.Parse(d.OptionalString("order_cond")),
            OrderLot = ShioajiWire.OrderLot.Parse(d.OptionalString("order_lot")),
            Price = price.Value,
            Quantity = quantity.Value,
            WebId = d.OptionalString("web_id"),
            CustomField = d.OptionalString("custom_field"),
            Ts = ts,
        };
    }

    // ---------------------------------------------------------------- 共用
    // ---------------------------------------------------------------- Shared

    private static Result<T> Finish<T>(ShioajiFieldReader fields, T value)
        where T : ShioajiStreamEvent =>
        fields.IsValid ? value : fields.Error!;

    private static DateTimeOffset RequiredTime(ShioajiFieldReader f)
    {
        if (ShioajiTimestamps.TryFromDateAndTime(f.OptionalString("date"), f.OptionalString("time"), out var utc))
        {
            return utc;
        }

        if (f.TryGet("datetime", out var element) && ShioajiTimestamps.TryRead(element, out utc))
        {
            return utc;
        }

        f.Fail("datetime");
        return default;
    }

    private static ShioajiTickType TickType(ShioajiFieldReader f) =>
        f.OptionalInt64("tick_type") switch
        {
            1 => ShioajiTickType.Outside,
            2 => ShioajiTickType.Inside,
            _ => ShioajiTickType.Undetermined,
        };

    private static ShioajiChangeType ChgType(ShioajiFieldReader f) =>
        f.OptionalInt64("chg_type") switch
        {
            1 => ShioajiChangeType.LimitUp,
            2 => ShioajiChangeType.Up,
            3 => ShioajiChangeType.Flat,
            4 => ShioajiChangeType.Down,
            5 => ShioajiChangeType.LimitDown,
            _ => ShioajiChangeType.Unknown,
        };

    /// <summary>
    /// 讀五檔陣列:必須是長度剛好 5 的數字陣列。長度不對代表 server 的格式與預期不同,
    /// 寧可讓這一則事件失敗,也不要把殘缺的五檔送給策略。
    /// Reads a book array: it must be an array of numbers exactly 5 long. Any other length means the server's format
    /// differs from what is expected, and failing this one event beats handing a strategy a broken book.
    /// </summary>
    private static List<decimal> RequiredBook(ShioajiFieldReader f, string name)
    {
        if (TryReadBook(f, name, out var book))
        {
            return book;
        }

        f.Fail(name);
        return [];
    }

    private static List<decimal>? OptionalBook(ShioajiFieldReader f, string name) =>
        TryReadBook(f, name, out var book) ? book : null;

    private static bool TryReadBook(ShioajiFieldReader f, string name, out List<decimal> book)
    {
        book = [];

        if (!f.TryGet(name, out var element) || element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != BookDepth)
        {
            return false;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (!ShioajiFieldReader.TryReadDecimal(item, out var number))
            {
                book = [];
                return false;
            }

            book.Add(number);
        }

        return true;
    }
}
