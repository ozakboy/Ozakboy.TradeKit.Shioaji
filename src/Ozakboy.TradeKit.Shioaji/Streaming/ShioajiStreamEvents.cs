namespace Ozakboy.TradeKit.Shioaji.Streaming;

/// <summary>
/// 串流上所有事件的共同基底。
/// The common base of every event on the stream.
/// </summary>
public abstract record ShioajiStreamEvent
{
    /// <summary>
    /// 本套件收到這個事件的時刻(UTC)。這是接收時間,不是交易所的撮合時間。
    /// The instant (UTC) this package received the event. It is the arrival time, not the exchange's match time.
    /// </summary>
    public required DateTimeOffset ReceivedAt { get; init; }
}

/// <summary>
/// 連線生命週期的種類。
/// The kinds of connection lifecycle event.
/// </summary>
public enum ShioajiStreamLifecycleKind
{
    /// <summary>
    /// 串流連上了(第一次連線或重連成功)。
    /// The stream connected, for the first time or after a reconnection.
    /// </summary>
    Connected = 0,

    /// <summary>
    /// 串流斷了(伺服器關閉、讀取錯誤或閒置逾時)。
    /// The stream dropped: the server closed it, a read failed, or it idled out.
    /// </summary>
    Disconnected = 1,

    /// <summary>
    /// 即將在退避等待之後重連。
    /// A reconnection is about to start after the backoff wait.
    /// </summary>
    Reconnecting = 2,
}

/// <summary>
/// 串流的連線生命週期事件,與行情事件從同一個 <c>IAsyncEnumerable</c> 送出。
/// A connection lifecycle event of the stream, delivered through the same <c>IAsyncEnumerable</c> as the market
/// events.
/// </summary>
/// <remarks>
/// <b>收到 <see cref="ShioajiStreamLifecycleKind.Disconnected"/> 之後、下一個 <see cref="ShioajiStreamLifecycleKind.Connected"/>
/// 之前,期間的行情一律沒有收到。</b>策略應把這段視為資料缺口。
/// <b>Everything between a <see cref="ShioajiStreamLifecycleKind.Disconnected"/> and the next
/// <see cref="ShioajiStreamLifecycleKind.Connected"/> was not received.</b> A strategy should treat that stretch
/// as a data gap.
/// </remarks>
public sealed record ShioajiStreamLifecycleEvent : ShioajiStreamEvent
{
    /// <summary>
    /// 種類。
    /// The kind.
    /// </summary>
    public required ShioajiStreamLifecycleKind Kind { get; init; }

    /// <summary>
    /// 這次中斷以來的重連次數編號:第一次連線為 0,之後每次重連嘗試加 1;一條連線健康地收過資料之後,
    /// 下一次中斷從 1 重新起算。<see cref="ShioajiStreamLifecycleKind.Connected"/> 與
    /// <see cref="ShioajiStreamLifecycleKind.Disconnected"/> 帶的是那條連線所屬的編號。
    /// The reconnection attempt number within the current outage: 0 for the very first connection, then one more
    /// for each reconnection attempt; after a connection has delivered data healthily, the next outage counts from
    /// 1 again. <see cref="ShioajiStreamLifecycleKind.Connected"/> and <see cref="ShioajiStreamLifecycleKind.Disconnected"/>
    /// carry the number of the connection they belong to.
    /// </summary>
    public required int Attempt { get; init; }

    /// <summary>
    /// 原因說明(已遮罩);<see cref="ShioajiStreamLifecycleKind.Connected"/> 時為 <see langword="null"/>。
    /// A description of the cause, masked; <see langword="null"/> for <see cref="ShioajiStreamLifecycleKind.Connected"/>.
    /// </summary>
    public required string? Reason { get; init; }
}

/// <summary>
/// 心跳。Shioaji 的心跳格式尚未實測,所以凡是事件名為 <c>heartbeat</c> 或 <c>ping</c>、事件名空白、
/// 或事件名不認得且 <c>data</c> 不是 JSON 物件的,都歸為心跳並保留原文。已知的行情事件名卻帶著不是 JSON 物件的
/// <c>data</c>,則是讀不懂的事件(失敗),不當心跳吞掉。
/// A heartbeat. The format of Shioaji's heartbeat has not been measured, so anything whose event name is
/// <c>heartbeat</c> or <c>ping</c>, whose event name is blank, or whose name is unrecognised and whose
/// <c>data</c> is not a JSON object, counts as a heartbeat and keeps its original text. A known market-event name
/// carrying a <c>data</c> that is not a JSON object is an unreadable event (a failure) and is not swallowed as a
/// heartbeat.
/// </summary>
public sealed record ShioajiHeartbeat : ShioajiStreamEvent
{
    /// <summary>
    /// 事件名(<c>event:</c> 那一行),可能是空字串。
    /// The event name (the <c>event:</c> line), possibly empty.
    /// </summary>
    public required string EventName { get; init; }

    /// <summary>
    /// <c>data:</c> 的原文,可能是空字串。
    /// The original <c>data:</c> text, possibly empty.
    /// </summary>
    public required string RawData { get; init; }
}

/// <summary>
/// 事件名本套件不認得、且 <c>data</c> 是 JSON 物件的事件(例如 <c>kbar</c>、<c>scanner</c>)。原樣送出,不丟棄。
/// An event whose name this package does not recognise and whose <c>data</c> is a JSON object, such as
/// <c>kbar</c> or <c>scanner</c>. It is passed through, not dropped.
/// </summary>
public sealed record ShioajiUnknownEvent : ShioajiStreamEvent
{
    /// <summary>
    /// 事件名。
    /// The event name.
    /// </summary>
    public required string EventName { get; init; }

    /// <summary>
    /// <c>data:</c> 的原文。
    /// The original <c>data:</c> text.
    /// </summary>
    public required string RawData { get; init; }
}
