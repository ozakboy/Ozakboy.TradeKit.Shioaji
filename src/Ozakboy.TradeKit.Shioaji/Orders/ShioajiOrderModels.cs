using Ozakboy.TradeKit.Shioaji.Contracts;

namespace Ozakboy.TradeKit.Shioaji.Orders;

/// <summary>
/// 下單用的帳號;省略時 server 用預設帳號(帳號列表中第一個證券帳號與第一個期貨帳號)。
/// The account an order goes to; when omitted the server uses the default (the first securities account and the
/// first futures account in the list).
/// </summary>
public sealed record ShioajiOrderAccount
{
    /// <summary>
    /// 券商代號。
    /// The broker id.
    /// </summary>
    public required string BrokerId { get; init; }

    /// <summary>
    /// 帳號。
    /// The account id.
    /// </summary>
    public required string AccountId { get; init; }
}

/// <summary>
/// 股票下單請求。
/// A stock order request.
/// </summary>
/// <remarks>
/// 四個影響成交方式的欄位(<see cref="PriceType"/>、<see cref="OrderType"/>、<see cref="OrderLot"/>、
/// <see cref="OrderCondition"/>)都是必填且<b>沒有預設值</b>:下單時漏填一個,
/// 結果可能是現股變融資、限價變市價,不應該由預設值默默決定。
/// The four fields that decide how an order executes (<see cref="PriceType"/>, <see cref="OrderType"/>,
/// <see cref="OrderLot"/>, <see cref="OrderCondition"/>) are required and <b>have no default</b>: forgetting one
/// could turn cash into margin or limit into market, and a default should not decide that quietly.
/// </remarks>
public sealed record ShioajiStockOrderRequest
{
    /// <summary>
    /// 交易所代號(<c>TSE</c> 或 <c>OTC</c>)。
    /// The exchange code (<c>TSE</c> or <c>OTC</c>).
    /// </summary>
    public required string Exchange { get; init; }

    /// <summary>
    /// 股票代碼。
    /// The stock code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 買賣方向。
    /// The side.
    /// </summary>
    public required ShioajiAction Action { get; init; }

    /// <summary>
    /// 委託價。限價單必須大於 0;市價單通常填 0。
    /// The order price. A limit order must be above 0; a market order is usually 0.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// 委託數量:整股為張數,零股為股數。必須是正整數。
    /// The order quantity: lots for round lots, shares for odd lots. Must be a positive whole number.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 價格類型。
    /// The price type.
    /// </summary>
    public required ShioajiPriceType PriceType { get; init; }

    /// <summary>
    /// 委託有效期限類型。
    /// The time-in-force.
    /// </summary>
    public required ShioajiOrderType OrderType { get; init; }

    /// <summary>
    /// 交易單位。
    /// The lot kind.
    /// </summary>
    public required ShioajiOrderLot OrderLot { get; init; }

    /// <summary>
    /// 委託條件(現股、融資、融券、借券)。
    /// The order condition (cash, margin, short, securities-borrowing).
    /// </summary>
    public required ShioajiOrderCondition OrderCondition { get; init; }

    /// <summary>
    /// 是否為現股當沖賣出(先賣後買)。預設 <see langword="false"/>。
    /// Whether this is a day-trade sell-first on cash stock. <see langword="false"/> by default.
    /// </summary>
    public bool DayTradeShort { get; init; }

    /// <summary>
    /// 自訂欄位,最多 6 個英數字元;會原樣出現在委託回報中,可以拿來對應策略或訊號。<see langword="null"/> 表示不帶。
    /// A custom field of at most 6 alphanumeric characters, echoed in the order reports; useful for tying an order
    /// to a strategy or signal. <see langword="null"/> sends none.
    /// </summary>
    public string? CustomField { get; init; }

    /// <summary>
    /// 下單帳號;<see langword="null"/> 表示用 server 的預設帳號。
    /// The account; <see langword="null"/> uses the server's default.
    /// </summary>
    public ShioajiOrderAccount? Account { get; init; }
}

/// <summary>
/// 期貨下單請求。
/// A futures order request.
/// </summary>
public sealed record ShioajiFuturesOrderRequest
{
    /// <summary>
    /// 交易所代號(<c>TAIFEX</c>)。
    /// The exchange code (<c>TAIFEX</c>).
    /// </summary>
    public required string Exchange { get; init; }

    /// <summary>
    /// 期貨合約代碼(實際合約,例如 <c>TXFF6</c>;連續月別名請先查出 <c>TargetCode</c>)。
    /// The futures contract code, the actual contract such as <c>TXFF6</c>; look up the <c>TargetCode</c> of a
    /// continuous-month alias first.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// 買賣方向。
    /// The side.
    /// </summary>
    public required ShioajiAction Action { get; init; }

    /// <summary>
    /// 委託價。限價單必須大於 0。
    /// The order price. A limit order must be above 0.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// 委託口數。必須是正整數。
    /// The number of contracts. Must be a positive whole number.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 價格類型。
    /// The price type.
    /// </summary>
    public required ShioajiPriceType PriceType { get; init; }

    /// <summary>
    /// 委託有效期限類型。
    /// The time-in-force.
    /// </summary>
    public required ShioajiOrderType OrderType { get; init; }

    /// <summary>
    /// 開平倉類型。
    /// The open/close type.
    /// </summary>
    public required ShioajiFuturesOcType OcType { get; init; }

    /// <summary>
    /// 下單帳號;<see langword="null"/> 表示用 server 的預設帳號。
    /// The account; <see langword="null"/> uses the server's default.
    /// </summary>
    public ShioajiOrderAccount? Account { get; init; }
}

/// <summary>
/// 一筆委託的內容(<c>order</c>)。除了 <see cref="Id"/> 之外的欄位 server 沒給時都是 <see langword="null"/>,
/// 不會補預設值。
/// The content of an order (<c>order</c>). Every field except <see cref="Id"/> is <see langword="null"/> when the
/// server gave none, and none is filled with a default.
/// </summary>
public sealed record ShioajiOrder
{
    /// <summary>
    /// 委託的識別碼,也就是改價、改量、撤單時用的 <c>trade_id</c>。
    /// The order's identifier, which is the <c>trade_id</c> that repricing, resizing and cancelling use.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 委託序號。
    /// The order sequence number.
    /// </summary>
    public required string? Seqno { get; init; }

    /// <summary>
    /// 委託書號。
    /// The order document number.
    /// </summary>
    public required string? Ordno { get; init; }

    /// <summary>
    /// 買賣方向;讀不懂時為 <see cref="ShioajiAction.Unknown"/>。
    /// The side; <see cref="ShioajiAction.Unknown"/> when unreadable.
    /// </summary>
    public required ShioajiAction Action { get; init; }

    /// <summary>
    /// 委託價。
    /// The order price.
    /// </summary>
    public required decimal? Price { get; init; }

    /// <summary>
    /// 委託數量。
    /// The order quantity.
    /// </summary>
    public required decimal? Quantity { get; init; }

    /// <summary>
    /// 價格類型;讀不懂時為 <see cref="ShioajiPriceType.Unknown"/>。
    /// The price type; <see cref="ShioajiPriceType.Unknown"/> when unreadable.
    /// </summary>
    public required ShioajiPriceType PriceType { get; init; }

    /// <summary>
    /// 有效期限類型;讀不懂時為 <see cref="ShioajiOrderType.Unknown"/>。
    /// The time-in-force; <see cref="ShioajiOrderType.Unknown"/> when unreadable.
    /// </summary>
    public required ShioajiOrderType OrderType { get; init; }

    /// <summary>
    /// 股票的交易單位;期貨或讀不懂時為 <see cref="ShioajiOrderLot.Unknown"/>。
    /// A stock's lot kind; <see cref="ShioajiOrderLot.Unknown"/> for futures or when unreadable.
    /// </summary>
    public required ShioajiOrderLot OrderLot { get; init; }

    /// <summary>
    /// 股票的委託條件;期貨或讀不懂時為 <see cref="ShioajiOrderCondition.Unknown"/>。
    /// A stock's order condition; <see cref="ShioajiOrderCondition.Unknown"/> for futures or when unreadable.
    /// </summary>
    public required ShioajiOrderCondition OrderCondition { get; init; }

    /// <summary>
    /// 期貨的開平倉類型;股票或讀不懂時為 <see cref="ShioajiFuturesOcType.Unknown"/>。
    /// A futures order's open/close type; <see cref="ShioajiFuturesOcType.Unknown"/> for stocks or when unreadable.
    /// </summary>
    public required ShioajiFuturesOcType OcType { get; init; }

    /// <summary>
    /// 下單時帶的自訂欄位。
    /// The custom field sent with the order.
    /// </summary>
    public required string? CustomField { get; init; }

    /// <summary>
    /// 委託所屬帳號;server 沒給時為 <see langword="null"/>。
    /// The account the order belongs to; <see langword="null"/> when the server gave none.
    /// </summary>
    public required ShioajiOrderAccount? Account { get; init; }
}

/// <summary>
/// 委託狀態中的一筆成交(<c>status.deals</c> 的元素)。
/// One fill inside an order's status, an element of <c>status.deals</c>.
/// </summary>
public sealed record ShioajiDeal
{
    /// <summary>
    /// 成交序號。
    /// The fill sequence.
    /// </summary>
    public required string? Seq { get; init; }

    /// <summary>
    /// 成交價。
    /// The fill price.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// 成交數量。
    /// The fill quantity.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// 成交時間(UTC);server 沒給或格式讀不懂時為 <see langword="null"/>。
    /// The fill time in UTC; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateTimeOffset? Ts { get; init; }
}

/// <summary>
/// 委託狀態(<c>status</c>)。
/// The state of an order (<c>status</c>).
/// </summary>
public sealed record ShioajiOrderStatus
{
    /// <summary>
    /// 對應委託的識別碼。
    /// The identifier of the order this belongs to.
    /// </summary>
    public required string? Id { get; init; }

    /// <summary>
    /// 狀態;讀不懂時為 <see cref="ShioajiOrderStatusKind.Unknown"/>。
    /// The state; <see cref="ShioajiOrderStatusKind.Unknown"/> when unreadable.
    /// </summary>
    public required ShioajiOrderStatusKind Status { get; init; }

    /// <summary>
    /// 狀態碼。
    /// The status code.
    /// </summary>
    public required string? StatusCode { get; init; }

    /// <summary>
    /// 委託時間(UTC);server 沒給或格式讀不懂時為 <see langword="null"/>。
    /// 數字視為 Unix 秒,字串視為台北時間(見 <c>ShioajiTimestamps</c> 的規則)。
    /// The order time in UTC; <see langword="null"/> when absent or unreadable. A number is Unix seconds and a
    /// string is Taipei time (see the rules of <c>ShioajiTimestamps</c>).
    /// </summary>
    public required DateTimeOffset? OrderTs { get; init; }

    /// <summary>
    /// 改價後的價格。
    /// The price after a repricing.
    /// </summary>
    public required decimal? ModifiedPrice { get; init; }

    /// <summary>
    /// 已取消(減量)的數量。
    /// The quantity cancelled or reduced.
    /// </summary>
    public required decimal? CancelQuantity { get; init; }

    /// <summary>
    /// 委託數量。
    /// The order quantity.
    /// </summary>
    public required decimal? OrderQuantity { get; init; }

    /// <summary>
    /// 已成交數量。
    /// The quantity filled.
    /// </summary>
    public required decimal? DealQuantity { get; init; }

    /// <summary>
    /// server 附帶的訊息(例如失敗原因)。
    /// The message the server attached, such as a failure reason.
    /// </summary>
    public required string? Message { get; init; }

    /// <summary>
    /// 成交明細。
    /// The fills.
    /// </summary>
    public required IReadOnlyList<ShioajiDeal> Deals { get; init; }
}

/// <summary>
/// 一筆委託加上它的狀態(<c>Trade</c>)。
/// An order together with its state (<c>Trade</c>).
/// </summary>
public sealed record ShioajiTrade
{
    /// <summary>
    /// 商品;server 沒給時為 <see langword="null"/>。
    /// The contract; <see langword="null"/> when the server gave none.
    /// </summary>
    public required ShioajiContract? Contract { get; init; }

    /// <summary>
    /// 委託內容。
    /// The order.
    /// </summary>
    public required ShioajiOrder Order { get; init; }

    /// <summary>
    /// 委託狀態。
    /// The state.
    /// </summary>
    public required ShioajiOrderStatus Status { get; init; }
}
