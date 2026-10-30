using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.Contracts;

namespace Ozakboy.TradeKit.Shioaji.Orders;

/// <summary>
/// 委託回應的對映。除了委託識別碼之外,欄位缺漏一律讀成 <see langword="null"/> 或 <c>Unknown</c>:
/// 這些回應常常是「單已經送出之後」才讀,為了一個次要欄位把整筆回應判為失敗,
/// 會讓呼叫端以為沒下成功而重送。
/// Mapping of order responses. Apart from the order identifier a missing field reads as <see langword="null"/>
/// or <c>Unknown</c>: these responses are usually read after the order already left, and failing the whole
/// response for a minor field would make the caller think it was not placed and send it again.
/// </summary>
internal static class ShioajiTradeParser
{
    private static readonly string[] ListWrapperNames = ["trades", "items", "data", "results", "list"];

    public static Result<ShioajiTrade> ParseTrade(string body, string context)
    {
        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<ShioajiTrade>();
        }

        using var document = parsed.GetValueOrThrow();

        return ParseTrade(document.RootElement, context);
    }

    public static Result<IReadOnlyList<ShioajiTrade>> ParseTradeList(string body, string context)
    {
        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<IReadOnlyList<ShioajiTrade>>();
        }

        using var document = parsed.GetValueOrThrow();

        var array = document.RootElement;

        if (array.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in ListWrapperNames)
            {
                if (array.TryGetProperty(name, out var inner) && inner.ValueKind == JsonValueKind.Array)
                {
                    array = inner;
                    break;
                }
            }
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return ShioajiErrors.MalformedResponse($"{context} 的回應不是陣列。The response for {context} is not an array.");
        }

        var trades = new List<ShioajiTrade>(array.GetArrayLength());

        foreach (var item in array.EnumerateArray())
        {
            var trade = ParseTrade(item, context);

            if (trade.IsFailure)
            {
                return trade.ToFailure<IReadOnlyList<ShioajiTrade>>();
            }

            trades.Add(trade.GetValueOrThrow());
        }

        return trades;
    }

    public static Result<ShioajiTrade> ParseTrade(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return ShioajiErrors.MalformedResponse($"{context} 的委託不是 JSON 物件。A trade in {context} is not a JSON object.");
        }

        var fields = new ShioajiFieldReader(element, context);

        if (!fields.TryGet("order", out var orderElement))
        {
            return ShioajiErrors.MissingField("order", context);
        }

        var order = ParseOrder(orderElement, context);

        if (order.IsFailure)
        {
            return order.ToFailure<ShioajiTrade>();
        }

        ShioajiContract? contract = null;

        if (fields.TryGet("contract", out var contractElement))
        {
            // 商品只是附帶資訊:讀不懂就留空,不要讓它拖垮整筆委託。
            // The contract is incidental; if it cannot be read it is left empty rather than failing the order.
            var parsedContract = ShioajiContractParser.ParseContract(contractElement, context);
            contract = parsedContract.IsSuccess ? parsedContract.GetValueOrThrow() : null;
        }

        fields.TryGet("status", out var statusElement);
        var status = ParseStatus(statusElement, order.GetValueOrThrow().Id, context);

        return new ShioajiTrade
        {
            Contract = contract,
            Order = order.GetValueOrThrow(),
            Status = status,
        };
    }

    public static Result<ShioajiOrder> ParseOrder(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return ShioajiErrors.MalformedResponse($"{context} 的 order 不是 JSON 物件。The order in {context} is not a JSON object.");
        }

        var fields = new ShioajiFieldReader(element, context);
        var id = fields.RequiredString("id");

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiOrder
        {
            Id = id,
            Seqno = fields.OptionalString("seqno"),
            Ordno = fields.OptionalString("ordno"),
            Action = ShioajiWire.Action.Parse(fields.OptionalString("action")),
            Price = fields.OptionalDecimal("price"),
            Quantity = fields.OptionalDecimal("quantity"),
            PriceType = ShioajiWire.PriceType.Parse(fields.OptionalString("price_type")),
            OrderType = ShioajiWire.OrderType.Parse(fields.OptionalString("order_type")),
            OrderLot = ShioajiWire.OrderLot.Parse(fields.OptionalString("order_lot")),
            OrderCondition = ShioajiWire.OrderCondition.Parse(fields.OptionalString("order_cond")),
            OcType = ShioajiWire.FuturesOcType.Parse(fields.OptionalString("octype")),
            CustomField = fields.OptionalString("custom_field"),
            Account = ParseAccount(fields),
        };
    }

    public static ShioajiOrderAccount? ParseAccount(ShioajiFieldReader orderFields)
    {
        if (!orderFields.TryGet("account", out var account) || account.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var accountFields = new ShioajiFieldReader(account, "account");
        var brokerId = accountFields.OptionalString("broker_id");
        var accountId = accountFields.OptionalString("account_id");

        return string.IsNullOrWhiteSpace(brokerId) || string.IsNullOrWhiteSpace(accountId)
            ? null
            : new ShioajiOrderAccount { BrokerId = brokerId, AccountId = accountId };
    }

    private static ShioajiOrderStatus ParseStatus(JsonElement element, string orderId, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new ShioajiOrderStatus
            {
                Id = orderId,
                Status = ShioajiOrderStatusKind.Unknown,
                StatusCode = null,
                OrderTs = null,
                ModifiedPrice = null,
                CancelQuantity = null,
                OrderQuantity = null,
                DealQuantity = null,
                Message = null,
                Deals = [],
            };
        }

        var fields = new ShioajiFieldReader(element, context);

        DateTimeOffset? orderTs = null;

        if (fields.TryGet("order_ts", out var ts) && ShioajiTimestamps.TryRead(ts, out var read))
        {
            orderTs = read;
        }
        else if (fields.TryGet("order_datetime", out var datetime) && ShioajiTimestamps.TryRead(datetime, out var read2))
        {
            orderTs = read2;
        }

        return new ShioajiOrderStatus
        {
            Id = fields.OptionalString("id") ?? orderId,
            Status = ParseStatusKind(fields.OptionalString("status")),
            StatusCode = fields.OptionalString("status_code"),
            OrderTs = orderTs,
            ModifiedPrice = fields.OptionalDecimal("modified_price"),
            CancelQuantity = fields.OptionalDecimal("cancel_quantity"),
            OrderQuantity = fields.OptionalDecimal("order_quantity"),
            DealQuantity = fields.OptionalDecimal("deal_quantity"),
            Message = fields.OptionalString("msg") ?? fields.OptionalString("message"),
            Deals = ParseDeals(fields),
        };
    }

    /// <summary>
    /// 狀態字串可能是 <c>Submitted</c>,也可能帶著列舉型別前綴(<c>Status.Submitted</c>);只看最後一段。
    /// The status string may be <c>Submitted</c> or carry an enum-type prefix (<c>Status.Submitted</c>); only the
    /// last segment counts.
    /// </summary>
    public static ShioajiOrderStatusKind ParseStatusKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ShioajiOrderStatusKind.Unknown;
        }

        var last = text.Trim();
        var dot = last.LastIndexOf('.');

        return ShioajiWire.OrderStatus.Parse(dot >= 0 ? last[(dot + 1)..] : last);
    }

    private static List<ShioajiDeal> ParseDeals(ShioajiFieldReader statusFields)
    {
        var deals = new List<ShioajiDeal>();

        if (!statusFields.TryGet("deals", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return deals;
        }

        foreach (var item in array.EnumerateArray())
        {
            var fields = new ShioajiFieldReader(item, "status.deals");
            var price = fields.RequiredDecimal("price");
            var quantity = fields.RequiredDecimal("quantity");

            if (!fields.IsValid)
            {
                // 一筆讀不懂的成交不能默默略過 —— 成交明細少一筆就是成交數量對不起來。
                // 但這個函式在委託已送出之後執行,也不能因此丟掉整筆委託,所以這筆以零值以外的方式表達不了,
                // 改為略過並讓 DealQuantity(server 自己的合計)去反映差異。
                // An unreadable fill cannot be skipped quietly, yet this runs after the order already left and must
                // not discard the whole order; there is no honest value to put here, so the fill is omitted and
                // DealQuantity, the server's own total, is what shows the difference.
                continue;
            }

            DateTimeOffset? ts = null;

            if (fields.TryGet("ts", out var stamp) && ShioajiTimestamps.TryRead(stamp, out var read))
            {
                ts = read;
            }

            deals.Add(new ShioajiDeal
            {
                Seq = fields.OptionalString("seq"),
                Price = price,
                Quantity = quantity,
                Ts = ts,
            });
        }

        return deals;
    }
}
