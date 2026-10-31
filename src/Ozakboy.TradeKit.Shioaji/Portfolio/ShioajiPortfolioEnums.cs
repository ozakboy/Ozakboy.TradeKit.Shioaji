namespace Ozakboy.TradeKit.Shioaji.Portfolio;

/// <summary>
/// 證券庫存與損益的計量單位(請求欄位 <c>unit</c>)。
/// The unit of measure of stock positions and profit and loss (the request field <c>unit</c>).
/// </summary>
public enum ShioajiPositionUnit
{
    /// <summary>
    /// 未指定。請求不帶 <c>unit</c>,由 server 用它的預設值(文件寫預設是整股)。
    /// Unspecified. The request carries no <c>unit</c> and the server uses its default (the documentation says round
    /// lots).
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 整股(<c>Common</c>),數量單位是張。
    /// Round lots (<c>Common</c>); quantities are in lots.
    /// </summary>
    Common = 1,

    /// <summary>
    /// 零股(<c>Share</c>),數量單位是股。
    /// Odd lots (<c>Share</c>); quantities are in shares.
    /// </summary>
    Share = 2,
}

/// <summary>
/// 證券庫存與損益的委託條件(回應欄位 <c>cond</c>)。
/// The order condition of a stock position or profit and loss row (the response field <c>cond</c>).
/// </summary>
/// <remarks>
/// 和下單用的 <c>ShioajiOrderCondition</c> 是兩個不同的集合:帳務回應多了 <c>Netting</c> 與 <c>Emerging</c>,
/// 而下單用的借券賣出在這裡不會出現。值依文件的 <c>StockOrderCond</c> 列舉。
/// This is a different set from the order-placing <c>ShioajiOrderCondition</c>: portfolio responses add
/// <c>Netting</c> and <c>Emerging</c> and the securities-borrowing sells of order placing do not appear here. The
/// values follow the documentation's <c>StockOrderCond</c> enumeration.
/// </remarks>
public enum ShioajiPositionCondition
{
    /// <summary>
    /// 欄位缺漏或值不認得。
    /// The field is absent or its value is not recognised.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 現股(<c>Cash</c>)。
    /// Cash (<c>Cash</c>).
    /// </summary>
    Cash = 1,

    /// <summary>
    /// 餘額交割(<c>Netting</c>)。
    /// Netting settlement (<c>Netting</c>).
    /// </summary>
    Netting = 2,

    /// <summary>
    /// 融資(<c>MarginTrading</c>)。
    /// Margin purchase (<c>MarginTrading</c>).
    /// </summary>
    MarginTrading = 3,

    /// <summary>
    /// 融券(<c>ShortSelling</c>)。
    /// Short sale on margin (<c>ShortSelling</c>).
    /// </summary>
    ShortSelling = 4,

    /// <summary>
    /// 興櫃(<c>Emerging</c>)。
    /// Emerging stock (<c>Emerging</c>).
    /// </summary>
    Emerging = 5,
}

/// <summary>
/// 已實現損益明細的交易類型(回應欄位 <c>trade_type</c>)。
/// The trade type of a realized profit and loss detail (the response field <c>trade_type</c>).
/// </summary>
public enum ShioajiProfitLossTradeType
{
    /// <summary>
    /// 欄位缺漏或值不認得。
    /// The field is absent or its value is not recognised.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 一般交易(<c>Common</c>)。
    /// An ordinary trade (<c>Common</c>).
    /// </summary>
    Common = 1,

    /// <summary>
    /// 當日沖銷(<c>DayTrade</c>)。
    /// A day trade (<c>DayTrade</c>).
    /// </summary>
    DayTrade = 2,
}
