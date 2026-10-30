namespace Ozakboy.TradeKit.Shioaji.Server;

/// <summary>
/// <see cref="ShioajiServerSupervisor"/> 的狀態。
/// The state of a <see cref="ShioajiServerSupervisor"/>.
/// </summary>
public enum ShioajiServerState
{
    /// <summary>
    /// 沒有在跑(尚未啟動,或已停止)。
    /// Not running: not started yet, or stopped.
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// 啟動中:正在探測、啟動行程、等 server 就緒。
    /// Starting: probing, launching the process, waiting for the server to be ready.
    /// </summary>
    Starting = 1,

    /// <summary>
    /// 行程由本監督器啟動,server 已就緒。
    /// The process was started by this supervisor and the server is ready.
    /// </summary>
    Running = 2,

    /// <summary>
    /// server 已經在跑,本監督器只是接手使用,不管理它的行程(外部啟動的,或啟動後自行脫離的)。
    /// The server was already running and this supervisor merely uses it without managing its process: started
    /// externally, or detached by itself after the start.
    /// </summary>
    Attached = 3,

    /// <summary>
    /// 行程非預期退出,正在退避等待重啟。
    /// The process exited unexpectedly and a restart is waiting out its backoff.
    /// </summary>
    Restarting = 4,

    /// <summary>
    /// 重啟次數超過上限,已放棄。需要人工介入,再呼叫 <see cref="ShioajiServerSupervisor.StartAsync"/> 可重新嘗試。
    /// The restart limit was exceeded and the supervisor gave up. It needs human attention; calling
    /// <see cref="ShioajiServerSupervisor.StartAsync"/> again tries afresh.
    /// </summary>
    Faulted = 5,
}

/// <summary>
/// <see cref="ShioajiServerSupervisor.Status"/> 的快照。
/// A snapshot returned by <see cref="ShioajiServerSupervisor.Status"/>.
/// </summary>
public sealed record ShioajiServerStatus
{
    /// <summary>
    /// 目前的狀態。
    /// The current state.
    /// </summary>
    public required ShioajiServerState State { get; init; }

    /// <summary>
    /// 本監督器啟動的行程編號;沒有時為 <see langword="null"/>。
    /// The id of the process this supervisor started; <see langword="null"/> when there is none.
    /// </summary>
    public required int? ProcessId { get; init; }

    /// <summary>
    /// 目前這個行程就緒的時刻;沒有時為 <see langword="null"/>。
    /// The instant the current process became ready; <see langword="null"/> when there is none.
    /// </summary>
    public required DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// 累計重啟次數。
    /// The cumulative number of restarts.
    /// </summary>
    public required int Restarts { get; init; }

    /// <summary>
    /// 最近一次非預期退出的結束碼。
    /// The exit code of the most recent unexpected exit.
    /// </summary>
    public required int? LastExitCode { get; init; }

    /// <summary>
    /// 最近一次失敗的說明(已遮罩)。
    /// A description of the most recent failure, masked.
    /// </summary>
    public required string? LastError { get; init; }
}
