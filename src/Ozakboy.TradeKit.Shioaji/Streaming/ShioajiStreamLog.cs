using Microsoft.Extensions.Logging;

namespace Ozakboy.TradeKit.Shioaji.Streaming;

/// <summary>
/// 串流讀取器的日誌行。事件編號 2000–2099。
/// The log lines of the stream reader. Event ids 2000–2099.
/// </summary>
/// <remarks>
/// 傳進來的字串(原因、未知行的內容)呼叫端已經過遮罩器;這裡不再處理。
/// The strings passed in (a reason, the text of an unknown line) have already been through the masker by the
/// caller; nothing more is done here.
/// </remarks>
internal static class ShioajiStreamLog
{
    private static readonly Action<ILogger, int, Exception?> ConnectedCore =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(2001, "StreamConnected"),
            "Shioaji 串流已連線 / stream connected。重連編號 {Attempt} / attempt number。");

    private static readonly Action<ILogger, int, string, Exception?> DisconnectedCore =
        LoggerMessage.Define<int, string>(
            LogLevel.Warning,
            new EventId(2002, "StreamDisconnected"),
            "Shioaji 串流中斷 / stream disconnected。重連編號 {Attempt} / attempt number;原因 {Reason} / reason。");

    private static readonly Action<ILogger, int, double, string, Exception?> ReconnectScheduledCore =
        LoggerMessage.Define<int, double, string>(
            LogLevel.Information,
            new EventId(2003, "StreamReconnectScheduled"),
            "Shioaji 串流將於退避後重連 / reconnecting after backoff。第 {Attempt} 次 / attempt;等待 {DelaySeconds:F1} 秒 / seconds;上次原因 {Reason} / last reason。");

    private static readonly Action<ILogger, string, Exception?> ReplayFailedCore =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2004, "StreamReplayFailed"),
            "重連後的重放回呼失敗,串流照常繼續 / the post-reconnect replay callback failed and the stream carries on。{Reason}");

    private static readonly Action<ILogger, string, string, Exception?> UnknownFormatCore =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(2005, "StreamUnknownFormat"),
            "串流第一次出現未知格式 / first unknown stream format seen。種類 {Kind} / kind;內容 {Text} / text。這個種類只記這一次 / logged once per kind。");

    private static readonly Action<ILogger, long, Exception?> DroppedCore =
        LoggerMessage.Define<long>(
            LogLevel.Warning,
            new EventId(2006, "StreamEventsDropped"),
            "串流佇列已滿,丟棄最舊的事件 / the stream queue is full and the oldest events are being dropped。累計 {Dropped} 則 / total so far。");

    private static readonly Action<ILogger, string, Exception?> ConnectFailedCore =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2007, "StreamConnectFailed"),
            "Shioaji 串流連線失敗 / stream connect failed。{Reason}");

    public static void Connected(ILogger logger, int attempt) => ConnectedCore(logger, attempt, null);

    public static void Disconnected(ILogger logger, int attempt, string reason) => DisconnectedCore(logger, attempt, reason, null);

    public static void ReconnectScheduled(ILogger logger, int attempt, TimeSpan delay, string reason) =>
        ReconnectScheduledCore(logger, attempt, delay.TotalSeconds, reason, null);

    public static void ReplayFailed(ILogger logger, string reason) => ReplayFailedCore(logger, reason, null);

    public static void UnknownFormat(ILogger logger, string kind, string text) => UnknownFormatCore(logger, kind, text, null);

    public static void Dropped(ILogger logger, long total) => DroppedCore(logger, total, null);

    public static void ConnectFailed(ILogger logger, string reason) => ConnectFailedCore(logger, reason, null);
}
