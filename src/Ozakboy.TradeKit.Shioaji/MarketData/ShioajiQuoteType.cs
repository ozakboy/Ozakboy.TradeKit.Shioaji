namespace Ozakboy.TradeKit.Shioaji.MarketData;

/// <summary>
/// 行情訂閱的種類,對應 server 的 <c>quote_type</c>。
/// The kind of market-data subscription, matching the server's <c>quote_type</c>.
/// </summary>
public enum ShioajiQuoteType
{
    /// <summary>
    /// 未指定。訂閱時會被本地拒絕。
    /// Unspecified. A subscription with it is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 逐筆成交(<c>Tick</c>)。
    /// Trade ticks (<c>Tick</c>).
    /// </summary>
    Tick = 1,

    /// <summary>
    /// 五檔委託簿(<c>BidAsk</c>)。
    /// The five-level order book (<c>BidAsk</c>).
    /// </summary>
    BidAsk = 2,

    /// <summary>
    /// 合併報價(<c>Quote</c>):成交與五檔在同一個事件。指數只支援這一種。
    /// The combined quote (<c>Quote</c>): trade and book in one event. The only kind an index supports.
    /// </summary>
    Quote = 3,
}
