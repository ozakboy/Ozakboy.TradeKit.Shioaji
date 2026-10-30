namespace Ozakboy.TradeKit.Shioaji.Streaming;

/// <summary>
/// 成交方向(<c>tick_type</c>)。
/// The direction of a trade (<c>tick_type</c>).
/// </summary>
public enum ShioajiTickType
{
    /// <summary>
    /// 無法判定(<c>0</c>),或讀不懂的值。
    /// Undetermined (<c>0</c>), or an unreadable value.
    /// </summary>
    Undetermined = 0,

    /// <summary>
    /// 外盤(<c>1</c>):以賣價成交,買方主動。
    /// Outside (<c>1</c>): traded at the ask, the buyer being the aggressor.
    /// </summary>
    Outside = 1,

    /// <summary>
    /// 內盤(<c>2</c>):以買價成交,賣方主動。
    /// Inside (<c>2</c>): traded at the bid, the seller being the aggressor.
    /// </summary>
    Inside = 2,
}

/// <summary>
/// 漲跌類型(<c>chg_type</c>)。
/// The change type (<c>chg_type</c>).
/// </summary>
public enum ShioajiChangeType
{
    /// <summary>
    /// 未知或讀不懂的值(包含 server 沒給這個欄位)。
    /// Unknown or an unreadable value, including a field the server did not send.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 漲停(<c>1</c>)。
    /// Limit up (<c>1</c>).
    /// </summary>
    LimitUp = 1,

    /// <summary>
    /// 上漲(<c>2</c>)。
    /// Up (<c>2</c>).
    /// </summary>
    Up = 2,

    /// <summary>
    /// 平盤(<c>3</c>)。
    /// Flat (<c>3</c>).
    /// </summary>
    Flat = 3,

    /// <summary>
    /// 下跌(<c>4</c>)。
    /// Down (<c>4</c>).
    /// </summary>
    Down = 4,

    /// <summary>
    /// 跌停(<c>5</c>)。
    /// Limit down (<c>5</c>).
    /// </summary>
    LimitDown = 5,
}

/// <summary>
/// 行情事件的共同欄位。
/// The fields every market event shares.
/// </summary>
/// <remarks>
/// 時間轉換:<c>date</c> + <c>time</c>(缺漏時用 <c>datetime</c>)是台北牆上時間,固定以 UTC+08:00 換成 UTC,
/// 見 <see cref="ShioajiConstants.TaipeiUtcOffset"/>。
/// Time conversion: <c>date</c> + <c>time</c> (or <c>datetime</c> when absent) is Taipei wall-clock time, converted
/// to UTC at a fixed UTC+08:00; see <see cref="ShioajiConstants.TaipeiUtcOffset"/>.
/// </remarks>
public abstract record ShioajiMarketEvent : ShioajiStreamEvent
{
    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 交易所代號;server 沒給時為 <see langword="null"/>。
    /// The exchange code; <see langword="null"/> when the server gave none.
    /// </summary>
    public required string? Exchange { get; init; }

    /// <summary>
    /// 交易所時間(UTC)。這是撮合時間;接收時間是 <see cref="ShioajiStreamEvent.ReceivedAt"/>。
    /// The exchange time in UTC. This is the match time; the arrival time is <see cref="ShioajiStreamEvent.ReceivedAt"/>.
    /// </summary>
    public required DateTimeOffset Time { get; init; }
}

/// <summary>
/// 股票逐筆成交(<c>tick_stk</c>)。
/// A stock trade tick (<c>tick_stk</c>).
/// </summary>
/// <remarks>
/// 欄位名依 Shioaji 文件;零股盤後、定盤相關的欄位名取自 Python SDK 的 <c>TickSTKv1</c>,尚未在 HTTP 上實測,
/// 對不上時為 <see langword="null"/>。
/// Field names follow the Shioaji documentation; the after-hours odd-lot and fixing fields take their names from
/// the Python SDK's <c>TickSTKv1</c> and have not been measured over HTTP, so a mismatch reads as
/// <see langword="null"/>.
/// </remarks>
public sealed record ShioajiTickStk : ShioajiMarketEvent
{
    /// <summary>
    /// 開盤價。
    /// The open price.
    /// </summary>
    public required decimal? Open { get; init; }

    /// <summary>
    /// 均價。
    /// The average price.
    /// </summary>
    public required decimal? AvgPrice { get; init; }

    /// <summary>
    /// 成交價。
    /// The trade price.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 最高價。
    /// The high.
    /// </summary>
    public required decimal? High { get; init; }

    /// <summary>
    /// 最低價。
    /// The low.
    /// </summary>
    public required decimal? Low { get; init; }

    /// <summary>
    /// 這一筆的成交金額。
    /// The turnover of this trade.
    /// </summary>
    public required decimal? Amount { get; init; }

    /// <summary>
    /// 累計成交金額。
    /// The cumulative turnover.
    /// </summary>
    public required decimal? TotalAmount { get; init; }

    /// <summary>
    /// 這一筆的成交量(整股為張,零股為股)。
    /// The volume of this trade: lots for round lots, shares for odd lots.
    /// </summary>
    public required decimal? Volume { get; init; }

    /// <summary>
    /// 累計成交量。
    /// The cumulative volume.
    /// </summary>
    public required decimal? TotalVolume { get; init; }

    /// <summary>
    /// 成交方向。
    /// The trade direction.
    /// </summary>
    public required ShioajiTickType TickType { get; init; }

    /// <summary>
    /// 漲跌類型。
    /// The change type.
    /// </summary>
    public required ShioajiChangeType ChgType { get; init; }

    /// <summary>
    /// 漲跌價差。
    /// The price change.
    /// </summary>
    public required decimal? PriceChg { get; init; }

    /// <summary>
    /// 漲跌幅(百分比)。
    /// The percentage change.
    /// </summary>
    public required decimal? PctChg { get; init; }

    /// <summary>
    /// 內盤累計成交量。
    /// The cumulative inside volume.
    /// </summary>
    public required decimal? BidSideTotalVol { get; init; }

    /// <summary>
    /// 外盤累計成交量。
    /// The cumulative outside volume.
    /// </summary>
    public required decimal? AskSideTotalVol { get; init; }

    /// <summary>
    /// 內盤累計成交筆數。
    /// The cumulative inside trade count.
    /// </summary>
    public required long? BidSideTotalCnt { get; init; }

    /// <summary>
    /// 外盤累計成交筆數。
    /// The cumulative outside trade count.
    /// </summary>
    public required long? AskSideTotalCnt { get; init; }

    /// <summary>
    /// 盤後零股成交股數。
    /// The after-hours odd-lot traded shares.
    /// </summary>
    public required decimal? ClosingOddlotShares { get; init; }

    /// <summary>
    /// 盤後零股成交價。
    /// The after-hours odd-lot trade price.
    /// </summary>
    public required decimal? ClosingOddlotClose { get; init; }

    /// <summary>
    /// 盤後零股成交金額。
    /// The after-hours odd-lot turnover.
    /// </summary>
    public required decimal? ClosingOddlotAmount { get; init; }

    /// <summary>
    /// 盤後零股買價。
    /// The after-hours odd-lot bid.
    /// </summary>
    public required decimal? ClosingOddlotBidPrice { get; init; }

    /// <summary>
    /// 盤後零股賣價。
    /// The after-hours odd-lot ask.
    /// </summary>
    public required decimal? ClosingOddlotAskPrice { get; init; }

    /// <summary>
    /// 盤後定價成交量。
    /// The after-hours fixed-price volume.
    /// </summary>
    public required decimal? FixedTradeVol { get; init; }

    /// <summary>
    /// 盤後定價成交金額。
    /// The after-hours fixed-price turnover.
    /// </summary>
    public required decimal? FixedTradeAmount { get; init; }

    /// <summary>
    /// 是否為暫停交易。server 沒給時為 <see langword="false"/>。
    /// Whether trading is suspended; <see langword="false"/> when the server gave none.
    /// </summary>
    public required bool Suspend { get; init; }

    /// <summary>
    /// 是否為試撮(模擬撮合,非真實成交)。試撮資料<b>不可</b>併入 K 線。server 沒給時為 <see langword="false"/>。
    /// Whether this is a trial match (a simulated match, not a real trade). Trial-match data must <b>not</b> be
    /// folded into bars. <see langword="false"/> when the server gave none.
    /// </summary>
    public required bool Simtrade { get; init; }

    /// <summary>
    /// 是否為盤中零股。server 沒給時為 <see langword="false"/>。
    /// Whether this is an intraday odd-lot trade; <see langword="false"/> when the server gave none.
    /// </summary>
    public required bool IntradayOdd { get; init; }
}

/// <summary>
/// 股票五檔(<c>bidask_stk</c>)。
/// A stock five-level book (<c>bidask_stk</c>).
/// </summary>
public sealed record ShioajiBidAskStk : ShioajiMarketEvent
{
    /// <summary>
    /// 五檔買價,由高到低,長度 5。
    /// The five bid prices, best first, always 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> BidPrice { get; init; }

    /// <summary>
    /// 五檔買量,長度 5。
    /// The five bid volumes, always 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> BidVolume { get; init; }

    /// <summary>
    /// 五檔買量變動;server 沒給時為 <see langword="null"/>。
    /// The change in the five bid volumes; <see langword="null"/> when the server gave none.
    /// </summary>
    public required IReadOnlyList<decimal>? DiffBidVol { get; init; }

    /// <summary>
    /// 五檔賣價,由低到高,長度 5。
    /// The five ask prices, best first, always 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> AskPrice { get; init; }

    /// <summary>
    /// 五檔賣量,長度 5。
    /// The five ask volumes, always 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> AskVolume { get; init; }

    /// <summary>
    /// 五檔賣量變動;server 沒給時為 <see langword="null"/>。
    /// The change in the five ask volumes; <see langword="null"/> when the server gave none.
    /// </summary>
    public required IReadOnlyList<decimal>? DiffAskVol { get; init; }

    /// <summary>
    /// 是否為暫停交易。
    /// Whether trading is suspended.
    /// </summary>
    public required bool Suspend { get; init; }

    /// <summary>
    /// 是否為試撮。
    /// Whether this is a trial match.
    /// </summary>
    public required bool Simtrade { get; init; }

    /// <summary>
    /// 是否為盤中零股。
    /// Whether this is the intraday odd-lot book.
    /// </summary>
    public required bool IntradayOdd { get; init; }
}

/// <summary>
/// 股票合併報價(<c>quote_stk</c>):成交與五檔在同一個事件。
/// A stock combined quote (<c>quote_stk</c>): trade and book in one event.
/// </summary>
public sealed record ShioajiQuoteStk : ShioajiMarketEvent
{
    /// <summary>
    /// 開盤價。
    /// The open price.
    /// </summary>
    public required decimal? Open { get; init; }

    /// <summary>
    /// 均價。
    /// The average price.
    /// </summary>
    public required decimal? AvgPrice { get; init; }

    /// <summary>
    /// 成交價。
    /// The trade price.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 最高價。
    /// The high.
    /// </summary>
    public required decimal? High { get; init; }

    /// <summary>
    /// 最低價。
    /// The low.
    /// </summary>
    public required decimal? Low { get; init; }

    /// <summary>
    /// 這一筆的成交金額。
    /// The turnover of this trade.
    /// </summary>
    public required decimal? Amount { get; init; }

    /// <summary>
    /// 累計成交金額。
    /// The cumulative turnover.
    /// </summary>
    public required decimal? TotalAmount { get; init; }

    /// <summary>
    /// 這一筆的成交量。
    /// The volume of this trade.
    /// </summary>
    public required decimal? Volume { get; init; }

    /// <summary>
    /// 累計成交量。
    /// The cumulative volume.
    /// </summary>
    public required decimal? TotalVolume { get; init; }

    /// <summary>
    /// 成交方向。
    /// The trade direction.
    /// </summary>
    public required ShioajiTickType TickType { get; init; }

    /// <summary>
    /// 漲跌類型。
    /// The change type.
    /// </summary>
    public required ShioajiChangeType ChgType { get; init; }

    /// <summary>
    /// 漲跌價差。
    /// The price change.
    /// </summary>
    public required decimal? PriceChg { get; init; }

    /// <summary>
    /// 漲跌幅(百分比)。
    /// The percentage change.
    /// </summary>
    public required decimal? PctChg { get; init; }

    /// <summary>
    /// 內盤累計成交量。
    /// The cumulative inside volume.
    /// </summary>
    public required decimal? BidSideTotalVol { get; init; }

    /// <summary>
    /// 外盤累計成交量。
    /// The cumulative outside volume.
    /// </summary>
    public required decimal? AskSideTotalVol { get; init; }

    /// <summary>
    /// 內盤累計成交筆數。
    /// The cumulative inside trade count.
    /// </summary>
    public required long? BidSideTotalCnt { get; init; }

    /// <summary>
    /// 外盤累計成交筆數。
    /// The cumulative outside trade count.
    /// </summary>
    public required long? AskSideTotalCnt { get; init; }

    /// <summary>
    /// 五檔買價,長度 5。
    /// The five bid prices, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> BidPrice { get; init; }

    /// <summary>
    /// 五檔買量,長度 5。
    /// The five bid volumes, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> BidVolume { get; init; }

    /// <summary>
    /// 五檔買量變動;server 沒給時為 <see langword="null"/>。
    /// The change in the bid volumes; <see langword="null"/> when absent.
    /// </summary>
    public required IReadOnlyList<decimal>? DiffBidVol { get; init; }

    /// <summary>
    /// 五檔賣價,長度 5。
    /// The five ask prices, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> AskPrice { get; init; }

    /// <summary>
    /// 五檔賣量,長度 5。
    /// The five ask volumes, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> AskVolume { get; init; }

    /// <summary>
    /// 五檔賣量變動;server 沒給時為 <see langword="null"/>。
    /// The change in the ask volumes; <see langword="null"/> when absent.
    /// </summary>
    public required IReadOnlyList<decimal>? DiffAskVol { get; init; }

    /// <summary>
    /// 可借券數量;server 沒給時為 <see langword="null"/>。
    /// The quantity available to borrow; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? AvailBorrowing { get; init; }

    /// <summary>
    /// 是否為暫停交易。
    /// Whether trading is suspended.
    /// </summary>
    public required bool Suspend { get; init; }

    /// <summary>
    /// 是否為試撮。
    /// Whether this is a trial match.
    /// </summary>
    public required bool Simtrade { get; init; }
}

/// <summary>
/// 期貨與選擇權逐筆成交(<c>tick_fop</c>)。
/// A futures and options trade tick (<c>tick_fop</c>).
/// </summary>
public sealed record ShioajiTickFop : ShioajiMarketEvent
{
    /// <summary>
    /// 開盤價。
    /// The open price.
    /// </summary>
    public required decimal? Open { get; init; }

    /// <summary>
    /// 標的價格。
    /// The underlying price.
    /// </summary>
    public required decimal? UnderlyingPrice { get; init; }

    /// <summary>
    /// 內盤累計成交量。
    /// The cumulative inside volume.
    /// </summary>
    public required decimal? BidSideTotalVol { get; init; }

    /// <summary>
    /// 外盤累計成交量。
    /// The cumulative outside volume.
    /// </summary>
    public required decimal? AskSideTotalVol { get; init; }

    /// <summary>
    /// 均價。
    /// The average price.
    /// </summary>
    public required decimal? AvgPrice { get; init; }

    /// <summary>
    /// 成交價。
    /// The trade price.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 最高價。
    /// The high.
    /// </summary>
    public required decimal? High { get; init; }

    /// <summary>
    /// 最低價。
    /// The low.
    /// </summary>
    public required decimal? Low { get; init; }

    /// <summary>
    /// 這一筆的成交金額。
    /// The turnover of this trade.
    /// </summary>
    public required decimal? Amount { get; init; }

    /// <summary>
    /// 累計成交金額。
    /// The cumulative turnover.
    /// </summary>
    public required decimal? TotalAmount { get; init; }

    /// <summary>
    /// 這一筆的成交量(口)。
    /// The volume of this trade in contracts.
    /// </summary>
    public required decimal? Volume { get; init; }

    /// <summary>
    /// 累計成交量。
    /// The cumulative volume.
    /// </summary>
    public required decimal? TotalVolume { get; init; }

    /// <summary>
    /// 成交方向。
    /// The trade direction.
    /// </summary>
    public required ShioajiTickType TickType { get; init; }

    /// <summary>
    /// 漲跌類型。
    /// The change type.
    /// </summary>
    public required ShioajiChangeType ChgType { get; init; }

    /// <summary>
    /// 漲跌價差。
    /// The price change.
    /// </summary>
    public required decimal? PriceChg { get; init; }

    /// <summary>
    /// 漲跌幅(百分比)。
    /// The percentage change.
    /// </summary>
    public required decimal? PctChg { get; init; }

    /// <summary>
    /// 是否為試撮。
    /// Whether this is a trial match.
    /// </summary>
    public required bool Simtrade { get; init; }
}

/// <summary>
/// 期貨與選擇權五檔(<c>bidask_fop</c>)。
/// A futures and options five-level book (<c>bidask_fop</c>).
/// </summary>
public sealed record ShioajiBidAskFop : ShioajiMarketEvent
{
    /// <summary>
    /// 委買總量;server 沒給時為 <see langword="null"/>。
    /// The total bid volume; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? BidTotalVol { get; init; }

    /// <summary>
    /// 委賣總量;server 沒給時為 <see langword="null"/>。
    /// The total ask volume; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? AskTotalVol { get; init; }

    /// <summary>
    /// 五檔買價,長度 5。
    /// The five bid prices, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> BidPrice { get; init; }

    /// <summary>
    /// 五檔買量,長度 5。
    /// The five bid volumes, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> BidVolume { get; init; }

    /// <summary>
    /// 五檔買量變動;server 沒給時為 <see langword="null"/>。
    /// The change in the bid volumes; <see langword="null"/> when absent.
    /// </summary>
    public required IReadOnlyList<decimal>? DiffBidVol { get; init; }

    /// <summary>
    /// 五檔賣價,長度 5。
    /// The five ask prices, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> AskPrice { get; init; }

    /// <summary>
    /// 五檔賣量,長度 5。
    /// The five ask volumes, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> AskVolume { get; init; }

    /// <summary>
    /// 五檔賣量變動;server 沒給時為 <see langword="null"/>。
    /// The change in the ask volumes; <see langword="null"/> when absent.
    /// </summary>
    public required IReadOnlyList<decimal>? DiffAskVol { get; init; }

    /// <summary>
    /// 衍生委買價(一檔);server 沒給時為 <see langword="null"/>。
    /// The first derived bid price; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? FirstDerivedBidPrice { get; init; }

    /// <summary>
    /// 衍生委賣價(一檔);server 沒給時為 <see langword="null"/>。
    /// The first derived ask price; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? FirstDerivedAskPrice { get; init; }

    /// <summary>
    /// 衍生委買量(一檔);server 沒給時為 <see langword="null"/>。
    /// The first derived bid volume; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? FirstDerivedBidVol { get; init; }

    /// <summary>
    /// 衍生委賣量(一檔);server 沒給時為 <see langword="null"/>。
    /// The first derived ask volume; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? FirstDerivedAskVol { get; init; }

    /// <summary>
    /// 標的價格;server 沒給時為 <see langword="null"/>。
    /// The underlying price; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? UnderlyingPrice { get; init; }

    /// <summary>
    /// 是否為試撮。
    /// Whether this is a trial match.
    /// </summary>
    public required bool Simtrade { get; init; }
}

/// <summary>
/// 期貨與選擇權合併報價(<c>quote_fop</c>)。
/// A futures and options combined quote (<c>quote_fop</c>).
/// </summary>
public sealed record ShioajiQuoteFop : ShioajiMarketEvent
{
    /// <summary>
    /// 開盤價。
    /// The open price.
    /// </summary>
    public required decimal? Open { get; init; }

    /// <summary>
    /// 成交價。
    /// The trade price.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 最高價。
    /// The high.
    /// </summary>
    public required decimal? High { get; init; }

    /// <summary>
    /// 最低價。
    /// The low.
    /// </summary>
    public required decimal? Low { get; init; }

    /// <summary>
    /// 均價。
    /// The average price.
    /// </summary>
    public required decimal? AvgPrice { get; init; }

    /// <summary>
    /// 這一筆的成交金額。
    /// The turnover of this trade.
    /// </summary>
    public required decimal? Amount { get; init; }

    /// <summary>
    /// 累計成交金額。
    /// The cumulative turnover.
    /// </summary>
    public required decimal? TotalAmount { get; init; }

    /// <summary>
    /// 這一筆的成交量。
    /// The volume of this trade.
    /// </summary>
    public required decimal? Volume { get; init; }

    /// <summary>
    /// 累計成交量。
    /// The cumulative volume.
    /// </summary>
    public required decimal? TotalVolume { get; init; }

    /// <summary>
    /// 成交方向。
    /// The trade direction.
    /// </summary>
    public required ShioajiTickType TickType { get; init; }

    /// <summary>
    /// 漲跌類型。
    /// The change type.
    /// </summary>
    public required ShioajiChangeType ChgType { get; init; }

    /// <summary>
    /// 漲跌價差。
    /// The price change.
    /// </summary>
    public required decimal? PriceChg { get; init; }

    /// <summary>
    /// 漲跌幅(百分比)。
    /// The percentage change.
    /// </summary>
    public required decimal? PctChg { get; init; }

    /// <summary>
    /// 標的價格;server 沒給時為 <see langword="null"/>。
    /// The underlying price; <see langword="null"/> when absent.
    /// </summary>
    public required decimal? UnderlyingPrice { get; init; }

    /// <summary>
    /// 五檔買價,長度 5。
    /// The five bid prices, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> BidPrice { get; init; }

    /// <summary>
    /// 五檔買量,長度 5。
    /// The five bid volumes, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> BidVolume { get; init; }

    /// <summary>
    /// 五檔買量變動;server 沒給時為 <see langword="null"/>。
    /// The change in the bid volumes; <see langword="null"/> when absent.
    /// </summary>
    public required IReadOnlyList<decimal>? DiffBidVol { get; init; }

    /// <summary>
    /// 五檔賣價,長度 5。
    /// The five ask prices, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> AskPrice { get; init; }

    /// <summary>
    /// 五檔賣量,長度 5。
    /// The five ask volumes, 5 long.
    /// </summary>
    public required IReadOnlyList<decimal> AskVolume { get; init; }

    /// <summary>
    /// 五檔賣量變動;server 沒給時為 <see langword="null"/>。
    /// The change in the ask volumes; <see langword="null"/> when absent.
    /// </summary>
    public required IReadOnlyList<decimal>? DiffAskVol { get; init; }

    /// <summary>
    /// 是否為試撮。
    /// Whether this is a trial match.
    /// </summary>
    public required bool Simtrade { get; init; }
}

/// <summary>
/// 指數報價(<c>quote_idx</c>)。指數只有這一種訂閱。
/// An index quote (<c>quote_idx</c>). It is the only subscription an index supports.
/// </summary>
/// <remarks>
/// 欄位名取自 Shioaji 的指數報價模型,尚未在 HTTP 上實測;只有 <see cref="ShioajiMarketEvent.Code"/>、時間與
/// <see cref="Close"/> 是必要的,其餘對不上時為 <see langword="null"/>。
/// The field names come from Shioaji's index quote model and have not been measured over HTTP; only
/// <see cref="ShioajiMarketEvent.Code"/>, the time and <see cref="Close"/> are required, and any other field that
/// does not match reads as <see langword="null"/>.
/// </remarks>
public sealed record ShioajiQuoteIdx : ShioajiMarketEvent
{
    /// <summary>
    /// 開盤指數。
    /// The opening index level.
    /// </summary>
    public required decimal? Open { get; init; }

    /// <summary>
    /// 最高指數。
    /// The high index level.
    /// </summary>
    public required decimal? High { get; init; }

    /// <summary>
    /// 最低指數。
    /// The low index level.
    /// </summary>
    public required decimal? Low { get; init; }

    /// <summary>
    /// 目前指數。
    /// The current index level.
    /// </summary>
    public required decimal Close { get; init; }

    /// <summary>
    /// 這一筆的成交金額。
    /// The turnover of this update.
    /// </summary>
    public required decimal? Amount { get; init; }

    /// <summary>
    /// 累計成交金額。
    /// The cumulative turnover.
    /// </summary>
    public required decimal? TotalAmount { get; init; }

    /// <summary>
    /// 這一筆的成交量。
    /// The volume of this update.
    /// </summary>
    public required decimal? Volume { get; init; }

    /// <summary>
    /// 累計成交量。
    /// The cumulative volume.
    /// </summary>
    public required decimal? TotalVolume { get; init; }

    /// <summary>
    /// 漲跌點數。
    /// The change in points.
    /// </summary>
    public required decimal? PriceChg { get; init; }

    /// <summary>
    /// 漲跌幅(百分比)。
    /// The percentage change.
    /// </summary>
    public required decimal? PctChg { get; init; }
}
