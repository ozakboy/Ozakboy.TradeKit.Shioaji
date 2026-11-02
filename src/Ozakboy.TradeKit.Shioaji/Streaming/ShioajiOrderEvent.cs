using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.Orders;

namespace Ozakboy.TradeKit.Shioaji.Streaming;

/// <summary>
/// 委託回報事件的種類。
/// The kinds of order-report event.
/// </summary>
public enum ShioajiOrderEventType
{
    /// <summary>
    /// 無法判斷。
    /// Could not be determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 委託回報(新增、改價、改量、撤單、被拒)。
    /// An order report: new, repriced, resized, cancelled, rejected.
    /// </summary>
    Order = 1,

    /// <summary>
    /// 成交回報。
    /// A deal report.
    /// </summary>
    Deal = 2,
}

/// <summary>
/// 委託回報的操作資訊(<c>operation</c>)。
/// The operation of an order report (<c>operation</c>).
/// </summary>
public sealed record ShioajiOrderOperation
{
    /// <summary>
    /// 操作類型,例如 <c>New</c>、<c>Cancel</c>。
    /// The operation type, such as <c>New</c> or <c>Cancel</c>.
    /// </summary>
    public required string? OpType { get; init; }

    /// <summary>
    /// 操作結果碼;<c>00</c> 通常代表成功。
    /// The operation result code; <c>00</c> usually means success.
    /// </summary>
    public required string? OpCode { get; init; }

    /// <summary>
    /// 操作結果訊息(拒單原因會在這裡)。
    /// The operation result message, where a rejection reason appears.
    /// </summary>
    public required string? OpMsg { get; init; }
}

/// <summary>
/// 委託回報事件裡的狀態(與 <see cref="ShioajiOrderStatus"/> 不同,這是交易所端的回報欄位)。
/// The status inside an order-report event; unlike <see cref="ShioajiOrderStatus"/> these are the exchange-side
/// report fields.
/// </summary>
public sealed record ShioajiOrderEventStatus
{
    /// <summary>
    /// 對應委託的識別碼。
    /// The identifier of the order this belongs to.
    /// </summary>
    public required string? Id { get; init; }

    /// <summary>
    /// 交易所時間(UTC);server 沒給或格式讀不懂時為 <see langword="null"/>。
    /// The exchange time in UTC; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateTimeOffset? ExchangeTs { get; init; }

    /// <summary>
    /// 改價後的價格。
    /// The price after a repricing.
    /// </summary>
    public required decimal? ModifiedPrice { get; init; }

    /// <summary>
    /// 取消(減量)的數量。
    /// The quantity cancelled or reduced.
    /// </summary>
    public required decimal? CancelQuantity { get; init; }

    /// <summary>
    /// 委託數量。
    /// The order quantity.
    /// </summary>
    public required decimal? OrderQuantity { get; init; }

    /// <summary>
    /// 網路單號。
    /// The web order id.
    /// </summary>
    public required string? WebId { get; init; }
}

/// <summary>
/// 成交回報的內容(委託回報事件裡成交的那一部分)。
/// The content of a deal report, the part of an order-report event describing a fill.
/// </summary>
public sealed record ShioajiOrderEventDeal
{
    /// <summary>
    /// 對應委託的識別碼(同 <see cref="ShioajiOrder.Id"/>)。
    /// The identifier of the order, the same as <see cref="ShioajiOrder.Id"/>.
    /// </summary>
    public required string? TradeId { get; init; }

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
    /// 交易所成交序號。
    /// The exchange fill sequence.
    /// </summary>
    public required string? ExchangeSeq { get; init; }

    /// <summary>
    /// 券商代號。
    /// The broker id.
    /// </summary>
    public required string? BrokerId { get; init; }

    /// <summary>
    /// 帳號。
    /// The account id.
    /// </summary>
    public required string? AccountId { get; init; }

    /// <summary>
    /// 買賣方向。
    /// The side.
    /// </summary>
    public required ShioajiAction Action { get; init; }

    /// <summary>
    /// 商品代碼。
    /// The contract code.
    /// </summary>
    public required string? Code { get; init; }

    /// <summary>
    /// 股票的委託條件;期貨或讀不懂時為 <see cref="ShioajiOrderCondition.Unknown"/>。
    /// A stock's order condition; <see cref="ShioajiOrderCondition.Unknown"/> for futures or when unreadable.
    /// </summary>
    public required ShioajiOrderCondition OrderCondition { get; init; }

    /// <summary>
    /// 股票的交易單位;期貨或讀不懂時為 <see cref="ShioajiOrderLot.Unknown"/>。
    /// A stock's lot kind; <see cref="ShioajiOrderLot.Unknown"/> for futures or when unreadable.
    /// </summary>
    public required ShioajiOrderLot OrderLot { get; init; }

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
    /// 網路單號。
    /// The web order id.
    /// </summary>
    public required string? WebId { get; init; }

    /// <summary>
    /// 下單時帶的自訂欄位。
    /// The custom field sent with the order.
    /// </summary>
    public required string? CustomField { get; init; }

    /// <summary>
    /// 成交時間(UTC);server 沒給或格式讀不懂時為 <see langword="null"/>。
    /// The fill time in UTC; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateTimeOffset? Ts { get; init; }
}

/// <summary>
/// 委託或成交回報(<c>order_event</c>)。
/// An order or deal report (<c>order_event</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>以 <see cref="EventId"/> 去重。</b>同一個事件可能重複送達;尾端序號跳號不代表遺失。
/// <b>De-duplicate on <see cref="EventId"/>.</b> The same event may arrive twice; a gap in a trailing sequence
/// number does not mean a loss.
/// </para>
/// <para>
/// 真 server 的外形(2026-11-02 模擬環境實測)是 <c>{"state":"StockOrder","data":{"StockOrder":{…}}}</c>:
/// 先拆開包裝,<c>state</c> 就是 <see cref="EventType"/>(含 Deal 為成交、含 Order 為委託)。沒有包裝時由內容推斷:
/// 有 <c>deal</c> 物件、或同時有 <c>trade_id</c> 與 <c>exchange_seq</c> 的是
/// <see cref="ShioajiOrderEventType.Deal"/>(明確的 <c>event_type</c> 欄位優先),其餘是
/// <see cref="ShioajiOrderEventType.Order"/>。成交回報的包裝外形尚未實測。完整原文一律保留在 <see cref="RawJson"/>。
/// The real server's shape (measured in the simulation on 2026-11-02) is
/// <c>{"state":"StockOrder","data":{"StockOrder":{…}}}</c>: the envelope is unwrapped first and <c>state</c> gives the
/// <see cref="EventType"/> (Deal for fills, Order for orders). Without an envelope the kind is inferred from the
/// content: a <c>deal</c> object, or both <c>trade_id</c> and <c>exchange_seq</c>, is a
/// <see cref="ShioajiOrderEventType.Deal"/> (an explicit <c>event_type</c> field wins), and everything else is an
/// <see cref="ShioajiOrderEventType.Order"/>. The deal envelope is not yet measured. The full original text is always
/// kept in <see cref="RawJson"/>.
/// </para>
/// </remarks>
public sealed record ShioajiOrderEvent : ShioajiStreamEvent
{
    /// <summary>
    /// 事件識別碼,去重用;<b>可能是空字串</b>(官方型別註記允許沒有識別碼的紀錄),這時請自行以 <see cref="RawJson"/> 等內容建立替代鍵。
    /// 實測的值形如 <c>v1:SO:…:7</c>。
    /// The event identifier, for de-duplication; <b>may be an empty string</b> (the official type note allows records
    /// without an identifier), in which case build a substitute key from <see cref="RawJson"/> or similar. Measured
    /// values look like <c>v1:SO:…:7</c>.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// 事件種類。
    /// The kind of event.
    /// </summary>
    public required ShioajiOrderEventType EventType { get; init; }

    /// <summary>
    /// 操作資訊;server 沒給時為 <see langword="null"/>。
    /// The operation; <see langword="null"/> when the server gave none.
    /// </summary>
    public required ShioajiOrderOperation? Operation { get; init; }

    /// <summary>
    /// 委託內容;server 沒給或讀不懂時為 <see langword="null"/>。
    /// The order; <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required ShioajiOrder? Order { get; init; }

    /// <summary>
    /// 交易所端的狀態;server 沒給時為 <see langword="null"/>。
    /// The exchange-side status; <see langword="null"/> when absent.
    /// </summary>
    public required ShioajiOrderEventStatus? Status { get; init; }

    /// <summary>
    /// 成交內容;不是成交回報時為 <see langword="null"/>。
    /// The fill; <see langword="null"/> unless this is a deal report.
    /// </summary>
    public required ShioajiOrderEventDeal? Deal { get; init; }

    /// <summary>
    /// 商品;server 沒給時為 <see langword="null"/>。
    /// The contract; <see langword="null"/> when absent.
    /// </summary>
    public required ShioajiContract? Contract { get; init; }

    /// <summary>
    /// <c>data:</c> 的完整原文。
    /// The complete original <c>data:</c> text.
    /// </summary>
    public required string RawJson { get; init; }
}
