using Microsoft.Extensions.Logging;

namespace Ozakboy.TradeKit.Shioaji.Server;

/// <summary>
/// 行程監督器的日誌行。事件編號 3000–3099。
/// The log lines of the process supervisor. Event ids 3000–3099.
/// </summary>
/// <remarks>
/// <b>這裡的每個字串參數,呼叫端都已經過遮罩器。</b>子行程的輸出一行一行遮罩之後才傳進來;
/// 憑證本身根本不會被傳進任何日誌方法。
/// <b>Every string parameter here has already been through the masker.</b> Child-process output is masked line by
/// line before it arrives, and the credentials themselves are never passed to any log method.
/// </remarks>
internal static class ShioajiServerLog
{
    private static readonly Action<ILogger, string, Exception?> OutputCore =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3001, "ServerOutput"),
            "shioaji: {Line}");

    private static readonly Action<ILogger, string, Exception?> ErrorOutputCore =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3002, "ServerErrorOutput"),
            "shioaji(stderr): {Line}");

    private static readonly Action<ILogger, int, bool, Exception?> StartedCore =
        LoggerMessage.Define<int, bool>(
            LogLevel.Information,
            new EventId(3003, "ServerStarted"),
            "shioaji server 已就緒 / the shioaji server is ready。行程 {ProcessId} / process;正式環境 {Production} / production。");

    private static readonly Action<ILogger, bool, Exception?> AttachedCore =
        LoggerMessage.Define<bool>(
            LogLevel.Information,
            new EventId(3004, "ServerAttached"),
            "shioaji server 已在執行,改為接手使用,不管理其行程 / the shioaji server is already running; attaching without managing its process。正式環境 {Production} / production。");

    private static readonly Action<ILogger, int, int, double, Exception?> ExitedCore =
        LoggerMessage.Define<int, int, double>(
            LogLevel.Warning,
            new EventId(3005, "ServerExited"),
            "shioaji server 行程非預期退出 / the shioaji server process exited unexpectedly。結束碼 {ExitCode} / exit code;第 {Restart} 次重啟 / restart number;等待 {DelaySeconds:F1} 秒 / seconds。");

    private static readonly Action<ILogger, int, Exception?> FaultedCore =
        LoggerMessage.Define<int>(
            LogLevel.Error,
            new EventId(3006, "ServerFaulted"),
            "shioaji server 重啟次數超過上限,監督器放棄 / the restart limit was exceeded and the supervisor gave up。上限 {MaxRestarts} / limit。");

    private static readonly Action<ILogger, string, Exception?> RestartFailedCore =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3007, "ServerRestartFailed"),
            "shioaji server 重啟失敗 / a restart failed。{Reason}");

    private static readonly Action<ILogger, Exception?> KilledCore =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(3008, "ServerKilled"),
            "shioaji server 沒有在時限內停止,已終止整棵行程樹 / the shioaji server did not stop in time and its whole process tree was killed。");

    public static void Output(ILogger logger, string line) => OutputCore(logger, line, null);

    public static void ErrorOutput(ILogger logger, string line) => ErrorOutputCore(logger, line, null);

    public static void Started(ILogger logger, int processId, bool production) => StartedCore(logger, processId, production, null);

    public static void Attached(ILogger logger, bool production) => AttachedCore(logger, production, null);

    public static void Exited(ILogger logger, int exitCode, int restart, TimeSpan delay) =>
        ExitedCore(logger, exitCode, restart, delay.TotalSeconds, null);

    public static void Faulted(ILogger logger, int maxRestarts) => FaultedCore(logger, maxRestarts, null);

    public static void RestartFailed(ILogger logger, string reason) => RestartFailedCore(logger, reason, null);

    public static void Killed(ILogger logger) => KilledCore(logger, null);
}
