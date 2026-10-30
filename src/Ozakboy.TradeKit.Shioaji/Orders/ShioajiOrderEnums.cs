namespace Ozakboy.TradeKit.Shioaji.Orders;

/// <summary>
/// 買賣方向(<c>action</c>)。
/// The side of an order (<c>action</c>).
/// </summary>
public enum ShioajiAction
{
    /// <summary>
    /// 讀不懂或未指定。下單時會被本地拒絕。
    /// Unreadable or unspecified. An order with it is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 買進(<c>Buy</c>)。
    /// Buy (<c>Buy</c>).
    /// </summary>
    Buy = 1,

    /// <summary>
    /// 賣出(<c>Sell</c>)。
    /// Sell (<c>Sell</c>).
    /// </summary>
    Sell = 2,
}

/// <summary>
/// 價格類型(<c>price_type</c>)。
/// The price type (<c>price_type</c>).
/// </summary>
public enum ShioajiPriceType
{
    /// <summary>
    /// 讀不懂或未指定。下單時會被本地拒絕。
    /// Unreadable or unspecified. An order with it is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 限價(<c>LMT</c>)。
    /// Limit (<c>LMT</c>).
    /// </summary>
    Limit = 1,

    /// <summary>
    /// 市價(<c>MKT</c>)。
    /// Market (<c>MKT</c>).
    /// </summary>
    Market = 2,

    /// <summary>
    /// 範圍市價(<c>MKP</c>)。
    /// Market-with-protection (<c>MKP</c>).
    /// </summary>
    MarketRange = 3,
}

/// <summary>
/// 委託有效期限類型(<c>order_type</c>)。
/// The time-in-force of an order (<c>order_type</c>).
/// </summary>
public enum ShioajiOrderType
{
    /// <summary>
    /// 讀不懂或未指定。下單時會被本地拒絕。
    /// Unreadable or unspecified. An order with it is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 當日有效(<c>ROD</c>)。
    /// Rest of day (<c>ROD</c>).
    /// </summary>
    Rod = 1,

    /// <summary>
    /// 立即成交否則取消,可部分成交(<c>IOC</c>)。
    /// Immediate-or-cancel, partial fills allowed (<c>IOC</c>).
    /// </summary>
    Ioc = 2,

    /// <summary>
    /// 全部成交否則取消(<c>FOK</c>)。
    /// Fill-or-kill (<c>FOK</c>).
    /// </summary>
    Fok = 3,
}

/// <summary>
/// 股票的交易單位(<c>order_lot</c>)。
/// The lot kind of a stock order (<c>order_lot</c>).
/// </summary>
public enum ShioajiOrderLot
{
    /// <summary>
    /// 讀不懂或未指定。下單時會被本地拒絕。
    /// Unreadable or unspecified. An order with it is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 整股(<c>Common</c>)。
    /// A round lot (<c>Common</c>).
    /// </summary>
    Common = 1,

    /// <summary>
    /// 定盤(<c>Fixing</c>)。
    /// Fixing (<c>Fixing</c>).
    /// </summary>
    Fixing = 2,

    /// <summary>
    /// 盤後零股(<c>Odd</c>)。
    /// After-hours odd lot (<c>Odd</c>).
    /// </summary>
    Odd = 3,

    /// <summary>
    /// 盤中零股(<c>IntradayOdd</c>)。
    /// Intraday odd lot (<c>IntradayOdd</c>).
    /// </summary>
    IntradayOdd = 4,
}

/// <summary>
/// 股票的委託條件(<c>order_cond</c>)。
/// The condition of a stock order (<c>order_cond</c>).
/// </summary>
public enum ShioajiOrderCondition
{
    /// <summary>
    /// 讀不懂或未指定。下單時會被本地拒絕。
    /// Unreadable or unspecified. An order with it is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 現股(<c>Cash</c>)。
    /// Cash (<c>Cash</c>).
    /// </summary>
    Cash = 1,

    /// <summary>
    /// 融資(<c>MarginTrading</c>)。
    /// Margin purchase (<c>MarginTrading</c>).
    /// </summary>
    MarginTrading = 2,

    /// <summary>
    /// 融券(<c>ShortSelling</c>)。
    /// Short selling (<c>ShortSelling</c>).
    /// </summary>
    ShortSelling = 3,

    /// <summary>
    /// 借券賣出(<c>SBLShort</c>)。
    /// Securities-borrowing short (<c>SBLShort</c>).
    /// </summary>
    SblShort = 4,

    /// <summary>
    /// 借券賣出,不受升降單位限制(<c>SBLShortPriceExempt</c>)。
    /// Securities-borrowing short exempt from the price rule (<c>SBLShortPriceExempt</c>).
    /// </summary>
    SblShortPriceExempt = 5,
}

/// <summary>
/// 期貨的開平倉類型(<c>octype</c>)。
/// The open/close type of a futures order (<c>octype</c>).
/// </summary>
public enum ShioajiFuturesOcType
{
    /// <summary>
    /// 讀不懂或未指定。下單時會被本地拒絕。
    /// Unreadable or unspecified. An order with it is refused locally.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 自動(<c>Auto</c>)。
    /// Automatic (<c>Auto</c>).
    /// </summary>
    Auto = 1,

    /// <summary>
    /// 新倉(<c>New</c>)。
    /// Open a new position (<c>New</c>).
    /// </summary>
    New = 2,

    /// <summary>
    /// 平倉(<c>Cover</c>)。
    /// Close a position (<c>Cover</c>).
    /// </summary>
    Cover = 3,

    /// <summary>
    /// 當沖(<c>DayTrade</c>)。
    /// Day trade (<c>DayTrade</c>).
    /// </summary>
    DayTrade = 4,
}

/// <summary>
/// 委託狀態(<c>status.status</c>)。
/// The state of an order (<c>status.status</c>).
/// </summary>
public enum ShioajiOrderStatusKind
{
    /// <summary>
    /// 讀不懂的狀態字串。呼叫端<b>不可</b>把它當成已成交或已取消。
    /// An unreadable status string. A caller must <b>never</b> read it as filled or cancelled.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 傳送中(<c>PendingSubmit</c>)。
    /// Being sent (<c>PendingSubmit</c>).
    /// </summary>
    PendingSubmit = 1,

    /// <summary>
    /// 預約單(<c>PreSubmitted</c>),盤前不回報,08:30 才放單。
    /// A reserved order (<c>PreSubmitted</c>), unreported before the open and released at 08:30.
    /// </summary>
    PreSubmitted = 2,

    /// <summary>
    /// 已送出(<c>Submitted</c>)。
    /// Submitted (<c>Submitted</c>).
    /// </summary>
    Submitted = 3,

    /// <summary>
    /// 部分成交(<c>PartFilled</c>)。
    /// Partly filled (<c>PartFilled</c>).
    /// </summary>
    PartFilled = 4,

    /// <summary>
    /// 全部成交(<c>Filled</c>)。
    /// Fully filled (<c>Filled</c>).
    /// </summary>
    Filled = 5,

    /// <summary>
    /// 已取消(<c>Cancelled</c>)。
    /// Cancelled (<c>Cancelled</c>).
    /// </summary>
    Cancelled = 6,

    /// <summary>
    /// 失敗(<c>Failed</c>)。
    /// Failed (<c>Failed</c>).
    /// </summary>
    Failed = 7,

    /// <summary>
    /// 無效(<c>Inactive</c>)。
    /// Inactive (<c>Inactive</c>).
    /// </summary>
    Inactive = 8,
}
