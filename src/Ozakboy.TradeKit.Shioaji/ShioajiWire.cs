using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.MarketData;
using Ozakboy.TradeKit.Shioaji.Orders;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 列舉與 server 線上字串的對照。寫出去時查表(查不到就是程式錯誤,呼叫端先已驗證過),
/// 讀進來時查表(查不到得到 <c>Unknown</c>,不擲例外)。
/// The mapping between enums and the server's wire strings. Writing looks a value up (a miss is a programming
/// error, since callers validate first); reading looks a string up (a miss yields <c>Unknown</c> and never
/// throws).
/// </summary>
internal sealed class ShioajiWireMap<TEnum>
    where TEnum : struct, Enum
{
    private readonly Dictionary<TEnum, string> _toWire = [];
    private readonly Dictionary<string, TEnum> _fromWire = new(StringComparer.OrdinalIgnoreCase);

    public ShioajiWireMap(params (TEnum Value, string Wire)[] pairs)
    {
        foreach (var (value, wire) in pairs)
        {
            _toWire[value] = wire;
            _fromWire[wire] = value;
        }
    }

    /// <summary>
    /// 取得列舉值的線上字串;<c>Unknown</c> 或未登記的值回傳 <see langword="false"/>。
    /// Gets the wire string of an enum value; <see langword="false"/> for <c>Unknown</c> or an unregistered
    /// value.
    /// </summary>
    public bool TryGetWire(TEnum value, out string wire)
    {
        if (_toWire.TryGetValue(value, out var found))
        {
            wire = found;
            return true;
        }

        wire = string.Empty;
        return false;
    }

    /// <summary>
    /// 線上字串轉列舉;空白或不認得時為 <c>default</c>(也就是 <c>Unknown</c>)。
    /// Converts a wire string to the enum; <c>default</c> (that is, <c>Unknown</c>) when blank or unrecognised.
    /// </summary>
    public TEnum Parse(string? wire) =>
        !string.IsNullOrWhiteSpace(wire) && _fromWire.TryGetValue(wire.Trim(), out var value) ? value : default;
}

/// <summary>
/// 各列舉的對照表實例。
/// The mapping instances for each enum.
/// </summary>
internal static class ShioajiWire
{
    public static readonly ShioajiWireMap<ShioajiAccountType> AccountType = new(
        (ShioajiAccountType.Stock, "S"),
        (ShioajiAccountType.Futures, "F"),
        (ShioajiAccountType.Foreign, "H"));

    public static readonly ShioajiWireMap<ShioajiSecurityType> SecurityType = new(
        (ShioajiSecurityType.Stock, "STK"),
        (ShioajiSecurityType.Future, "FUT"),
        (ShioajiSecurityType.Option, "OPT"),
        (ShioajiSecurityType.Index, "IND"),
        (ShioajiSecurityType.Warrant, "WRT"));

    public static readonly ShioajiWireMap<ShioajiDayTradeEligibility> DayTrade = new(
        (ShioajiDayTradeEligibility.Yes, "Yes"),
        (ShioajiDayTradeEligibility.OnlyBuy, "OnlyBuy"),
        (ShioajiDayTradeEligibility.No, "No"));

    public static readonly ShioajiWireMap<ShioajiQuoteType> QuoteType = new(
        (ShioajiQuoteType.Tick, "Tick"),
        (ShioajiQuoteType.BidAsk, "BidAsk"),
        (ShioajiQuoteType.Quote, "Quote"));

    public static readonly ShioajiWireMap<ShioajiAction> Action = new(
        (ShioajiAction.Buy, "Buy"),
        (ShioajiAction.Sell, "Sell"));

    public static readonly ShioajiWireMap<ShioajiPriceType> PriceType = new(
        (ShioajiPriceType.Limit, "LMT"),
        (ShioajiPriceType.Market, "MKT"),
        (ShioajiPriceType.MarketRange, "MKP"));

    public static readonly ShioajiWireMap<ShioajiOrderType> OrderType = new(
        (ShioajiOrderType.Rod, "ROD"),
        (ShioajiOrderType.Ioc, "IOC"),
        (ShioajiOrderType.Fok, "FOK"));

    public static readonly ShioajiWireMap<ShioajiOrderLot> OrderLot = new(
        (ShioajiOrderLot.Common, "Common"),
        (ShioajiOrderLot.Fixing, "Fixing"),
        (ShioajiOrderLot.Odd, "Odd"),
        (ShioajiOrderLot.IntradayOdd, "IntradayOdd"));

    public static readonly ShioajiWireMap<ShioajiOrderCondition> OrderCondition = new(
        (ShioajiOrderCondition.Cash, "Cash"),
        (ShioajiOrderCondition.MarginTrading, "MarginTrading"),
        (ShioajiOrderCondition.ShortSelling, "ShortSelling"),
        (ShioajiOrderCondition.SblShort, "SBLShort"),
        (ShioajiOrderCondition.SblShortPriceExempt, "SBLShortPriceExempt"));

    public static readonly ShioajiWireMap<ShioajiFuturesOcType> FuturesOcType = new(
        (ShioajiFuturesOcType.Auto, "Auto"),
        (ShioajiFuturesOcType.New, "New"),
        (ShioajiFuturesOcType.Cover, "Cover"),
        (ShioajiFuturesOcType.DayTrade, "DayTrade"));

    public static readonly ShioajiWireMap<ShioajiOrderStatusKind> OrderStatus = new(
        (ShioajiOrderStatusKind.PendingSubmit, "PendingSubmit"),
        (ShioajiOrderStatusKind.PreSubmitted, "PreSubmitted"),
        (ShioajiOrderStatusKind.Submitted, "Submitted"),
        (ShioajiOrderStatusKind.PartFilled, "PartFilled"),
        (ShioajiOrderStatusKind.Filled, "Filled"),
        (ShioajiOrderStatusKind.Cancelled, "Cancelled"),
        (ShioajiOrderStatusKind.Failed, "Failed"),
        (ShioajiOrderStatusKind.Inactive, "Inactive"));
}
