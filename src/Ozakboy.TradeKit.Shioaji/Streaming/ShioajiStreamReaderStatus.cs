namespace Ozakboy.TradeKit.Shioaji.Streaming;

/// <summary>
/// <see cref="ShioajiStreamReader"/> 的狀態。
/// The state of a <see cref="ShioajiStreamReader"/>.
/// </summary>
public enum ShioajiStreamReaderState
{
    /// <summary>
    /// 沒有在讀(尚未開始,或消費端已結束)。
    /// Not reading: not started yet, or the consumer has finished.
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// 第一次連線中。
    /// Making the first connection.
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// 已連線,正在讀。
    /// Connected and reading.
    /// </summary>
    Connected = 2,

    /// <summary>
    /// 斷線後正在退避或重連。
    /// Backing off or reconnecting after a drop.
    /// </summary>
    Reconnecting = 3,
}

/// <summary>
/// <see cref="ShioajiStreamReader.Status"/> 的快照。
/// A snapshot returned by <see cref="ShioajiStreamReader.Status"/>.
/// </summary>
public sealed record ShioajiStreamReaderStatus
{
    /// <summary>
    /// 目前的狀態。
    /// The current state.
    /// </summary>
    public required ShioajiStreamReaderState State { get; init; }

    /// <summary>
    /// 目前這條連線連上的時刻;尚未連上或已斷線時為 <see langword="null"/>。
    /// The instant the current connection was made; <see langword="null"/> when not connected.
    /// </summary>
    public required DateTimeOffset? ConnectedAt { get; init; }

    /// <summary>
    /// 最後收到事件(含心跳)的時刻;還沒收過時為 <see langword="null"/>。
    /// The instant the last event, heartbeats included, arrived; <see langword="null"/> before the first.
    /// </summary>
    public required DateTimeOffset? LastEventAt { get; init; }

    /// <summary>
    /// 累計重連成功次數(第一次連線不算)。
    /// The cumulative number of successful reconnections; the first connection does not count.
    /// </summary>
    public required int Reconnects { get; init; }

    /// <summary>
    /// 累計因佇列滿了而丟棄的事件數。
    /// The cumulative number of events dropped because the queue was full.
    /// </summary>
    public required long DroppedEvents { get; init; }
}
