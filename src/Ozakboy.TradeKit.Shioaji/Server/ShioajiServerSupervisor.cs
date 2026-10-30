using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ozakboy.Security.Masking;
using Ozakboy.TradeKit.Shioaji.Auth;

namespace Ozakboy.TradeKit.Shioaji.Server;

/// <summary>
/// 啟動並監督 Shioaji 官方的本機 HTTP server 行程(<c>shioaji server start --no-open</c>)。
/// Starts and supervises the official Shioaji local HTTP server process (<c>shioaji server start --no-open</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>憑證只走子行程的環境變數。</b>建構時收的是 <c>Func&lt;ShioajiCredentials&gt;</c>,只在啟動子行程的那一刻呼叫,
/// 值填進 <see cref="ProcessStartInfo.Environment"/>(<c>SJ_API_KEY</c>、<c>SJ_SEC_KEY</c>、<c>SJ_CA_PATH</c>、
/// <c>SJ_CA_PASSWD</c>)—— <b>不寫任何檔案、不進日誌</b>。四個值一取得就登記到遮罩器,子行程的 stdout 與 stderr
/// 逐行遮罩之後才寫入日誌;遮罩是字面替換,所以短於 8 個字元的值無法登記(會大量誤遮一般文字),
/// 請使用夠長的憑證與密碼。
/// <b>Credentials travel only through the child process's environment.</b> The constructor takes a
/// <c>Func&lt;ShioajiCredentials&gt;</c> that is called only at the instant the child starts, and its values go
/// into <see cref="ProcessStartInfo.Environment"/> (<c>SJ_API_KEY</c>, <c>SJ_SEC_KEY</c>, <c>SJ_CA_PATH</c>,
/// <c>SJ_CA_PASSWD</c>) — <b>never into a file and never into a log</b>. All four values are registered on the
/// masker as soon as they are obtained, and the child's stdout and stderr are masked line by line before they are
/// logged. Masking is literal replacement, so a value shorter than 8 characters cannot be registered (it would mask
/// swathes of ordinary text); use credentials and passwords that are long enough.
/// </para>
/// <para>
/// <b>三種接手方式。</b>(1) 啟動時 <c>/api/v1/info</c> 已經有回應、且 <c>simulation</c> 與 <see cref="ShioajiServerOptions.Production"/>
/// 相符:視為外部已啟動,進入 <see cref="ShioajiServerState.Attached"/>,<b>不管理那個行程</b>,停止時也不動它。
/// (2) 有回應但模式不符:拒絕,絕不終止別人啟動的 server,也不在它上面下單。
/// (3) 沒有回應:啟動新行程。啟動後行程自行結束(結束碼 0)但 server 仍在回應,同樣視為 Attached(server 自己脫離了),
/// 此時停止會呼叫 <c>shioaji server stop</c>。
/// <b>Three ways to take over.</b> (1) <c>/api/v1/info</c> already answers at start and <c>simulation</c> matches
/// <see cref="ShioajiServerOptions.Production"/>: it counts as started externally, enters
/// <see cref="ShioajiServerState.Attached"/>, <b>does not manage that process</b> and leaves it alone on stop.
/// (2) It answers but the mode disagrees: refused, never killing a server someone else started and never trading
/// on it. (3) It does not answer: a new process is started. If that process ends by itself with exit code 0 while
/// the server still answers, that is also Attached (the server detached itself), and a stop then calls
/// <c>shioaji server stop</c>.
/// </para>
/// <para>
/// <b>重啟。</b>就緒之後行程非預期退出,以指數退避重啟(<see cref="ShioajiServerOptions.RestartBackoff"/>,上限 60 秒);
/// 連續重啟超過 <see cref="ShioajiServerOptions.MaxRestarts"/> 就進入 <see cref="ShioajiServerState.Faulted"/> 並放棄。
/// 行程穩定跑過 <see cref="ShioajiServerOptions.StableAfter"/> 之後再退出,連續計數歸零。
/// <b>Restarts.</b> If the process exits unexpectedly after it was ready it is restarted with exponential backoff
/// (<see cref="ShioajiServerOptions.RestartBackoff"/>, capped at 60 seconds); more than
/// <see cref="ShioajiServerOptions.MaxRestarts"/> consecutive restarts put the supervisor in
/// <see cref="ShioajiServerState.Faulted"/> and it gives up. A later exit after the process ran for
/// <see cref="ShioajiServerOptions.StableAfter"/> starts the consecutive count over.
/// </para>
/// <para>
/// <b>沒有實機驗證。</b>這個型別只對「假的可執行檔」測試過,從未啟動過真正的 <c>shioaji</c>;
/// <c>server start</c> 在前景執行還是自行脫離、輸出的格式、<c>server stop</c> 的行為都來自 CLI 的 <c>--help</c>
/// 與文件,第一次實機執行請看日誌。
/// <b>Not verified on a real machine.</b> This type has been exercised only against a fake executable and never
/// started the real <c>shioaji</c>; whether <c>server start</c> stays in the foreground or detaches, its output
/// format, and the behaviour of <c>server stop</c> come from the CLI's <c>--help</c> and the documentation, so read
/// the log on the first real run.
/// </para>
/// </remarks>
public sealed class ShioajiServerSupervisor : IAsyncDisposable
{
    private const int TailLines = 20;

    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly ShioajiServerOptions _options;
    private readonly Func<ShioajiCredentials> _credentialsProvider;
    private readonly Func<CancellationToken, Task<Result<ShioajiServerInfo>>> _probe;
    private readonly ILogger _logger;
    private readonly SecretMasker _masker;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _stateGate = new();
    private readonly Lock _tailGate = new();
    private readonly Queue<string> _tail = new();

    private ShioajiServerState _state = ShioajiServerState.Stopped;
    private Process? _process;
    private DateTimeOffset? _startedAt;
    private int _restarts;
    private int _consecutiveRestarts;
    private int? _lastExitCode;
    private string? _lastError;
    private bool _ownsServer;
    private CancellationTokenSource? _monitorSource;
    private Task? _monitorTask;
    private bool _disposed;

    /// <summary>
    /// 建立監督器。
    /// Creates the supervisor.
    /// </summary>
    /// <param name="options">行程設定。The process settings.</param>
    /// <param name="credentialsProvider">
    /// 憑證提供者,只在啟動子行程的那一刻被呼叫。
    /// The credentials provider, called only at the instant the child process starts.
    /// </param>
    /// <param name="authClient">用來探測 <c>/api/v1/info</c> 的用戶端。The client used to probe <c>/api/v1/info</c>.</param>
    /// <param name="logger">日誌;<see langword="null"/> 時不寫。The logger, or none when <see langword="null"/>.</param>
    /// <param name="masker">
    /// 遮罩器;請傳入具名用戶端的那一個(<c>provider.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName)</c>),
    /// 這樣憑證在 HTTP 錯誤裡也會被遮罩。<see langword="null"/> 時用 <see cref="SecretMasker.Default"/>。
    /// The masker; pass the named client's own so the credentials are masked in HTTP errors too.
    /// <see cref="SecretMasker.Default"/> when <see langword="null"/>.
    /// </param>
    /// <param name="timeProvider">時間來源;<see langword="null"/> 時用系統時間。The time source, or system time when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// 必要參數為 <see langword="null"/> 時擲出。Thrown when a required argument is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> 不合法時擲出。Thrown when <paramref name="options"/> is invalid.
    /// </exception>
    public ShioajiServerSupervisor(
        ShioajiServerOptions options,
        Func<ShioajiCredentials> credentialsProvider,
        ShioajiAuthClient authClient,
        ILogger<ShioajiServerSupervisor>? logger = null,
        SecretMasker? masker = null,
        TimeProvider? timeProvider = null)
        : this(options, credentialsProvider, ProbeOf(authClient), logger, masker, timeProvider)
    {
    }

    internal ShioajiServerSupervisor(
        ShioajiServerOptions options,
        Func<ShioajiCredentials> credentialsProvider,
        Func<CancellationToken, Task<Result<ShioajiServerInfo>>> probe,
        ILogger? logger,
        SecretMasker? masker,
        TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentialsProvider);
        ArgumentNullException.ThrowIfNull(probe);

        var validation = options.Validate();

        if (validation.IsFailure)
        {
            throw new ArgumentException(validation.Error.Message, nameof(options));
        }

        _options = options;
        _credentialsProvider = credentialsProvider;
        _probe = probe;
        _logger = logger ?? NullLogger.Instance;
        _masker = masker ?? SecretMasker.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// 目前狀態的快照。
    /// A snapshot of the current state.
    /// </summary>
    public ShioajiServerStatus Status
    {
        get
        {
            lock (_stateGate)
            {
                return new ShioajiServerStatus
                {
                    State = _state,
                    ProcessId = SafeProcessId(_process),
                    StartedAt = _startedAt,
                    Restarts = _restarts,
                    LastExitCode = _lastExitCode,
                    LastError = _lastError,
                };
            }
        }
    }

    /// <summary>
    /// 啟動(或接手)server,等到 <c>/api/v1/info</c> 回應為止。
    /// Starts, or attaches to, the server and waits until <c>/api/v1/info</c> answers.
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 就緒(<see cref="ShioajiServerState.Running"/> 或 <see cref="ShioajiServerState.Attached"/>)時為成功;
    /// 否則為失敗,代碼見 <see cref="ShioajiErrorCodes"/> 的 <c>shioaji.server.*</c>。已在 Running 或 Attached 時直接成功。
    /// Success once ready, in <see cref="ShioajiServerState.Running"/> or <see cref="ShioajiServerState.Attached"/>;
    /// otherwise a failure with one of the <c>shioaji.server.*</c> codes of <see cref="ShioajiErrorCodes"/>.
    /// Already Running or Attached succeeds at once.
    /// </returns>
    /// <exception cref="ObjectDisposedException">監督器已釋放。The supervisor is disposed.</exception>
    public async Task<Result> StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ShioajiErrors.RequestCancelled("server.start");
        }

        try
        {
            switch (CurrentState)
            {
                case ShioajiServerState.Running or ShioajiServerState.Attached:
                    return Result.Success();
                case ShioajiServerState.Starting or ShioajiServerState.Restarting:
                    return new Error(
                        ShioajiErrorCodes.ServerInvalidState,
                        "監督器正在啟動或重啟中。The supervisor is already starting or restarting.",
                        ErrorCategory.Conflict);
            }

            lock (_stateGate)
            {
                _consecutiveRestarts = 0;
            }

            return await StartCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetState(ShioajiServerState.Stopped);
            return ShioajiErrors.RequestCancelled("server.start");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 停止 server。先呼叫 <c>shioaji server stop</c>(等 <see cref="ShioajiServerOptions.StopTimeout"/>),
    /// 沒停就終止整棵行程樹。接手(Attach)別人啟動的 server 時什麼都不動。
    /// Stops the server. <c>shioaji server stop</c> is called first, waiting up to
    /// <see cref="ShioajiServerOptions.StopTimeout"/>, and the whole process tree is killed if it has not stopped.
    /// A server attached to but started by someone else is left alone.
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>成功。Success.</returns>
    public async Task<Result> StopAsync(CancellationToken cancellationToken = default)
    {
        // 先取消監控,讓正在重啟中(持有 gate)的監控迴圈盡快放手;之後取得 gate 再取消一次,
        // 涵蓋「取消之後才有新監控被建起來」的競爭。
        // The monitor is cancelled first so a monitor loop in the middle of a restart, which holds the gate, lets go
        // quickly; it is cancelled again once the gate is held to cover a monitor created after the first cancel.
        await CancelMonitorAsync().ConfigureAwait(false);

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ShioajiErrors.RequestCancelled("server.stop");
        }

        try
        {
            var monitorTask = await CancelMonitorAsync().ConfigureAwait(false);

            if (monitorTask is not null)
            {
                try
                {
                    await monitorTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 監控因取消而結束是預期的。
                    // The monitor ending because of the cancellation is expected.
                }
            }

            lock (_stateGate)
            {
                _monitorSource?.Dispose();
                _monitorSource = null;
                _monitorTask = null;
            }

            await StopProcessAsync(cancellationToken).ConfigureAwait(false);
            SetState(ShioajiServerState.Stopped);

            return Result.Success();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 停止 server 並釋放資源。
    /// Stops the server and releases resources.
    /// </summary>
    /// <returns>代表非同步作業的工作。A task for the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _ = await StopAsync(CancellationToken.None).ConfigureAwait(false);

        _gate.Dispose();
    }

    private ShioajiServerState CurrentState
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    private static Func<CancellationToken, Task<Result<ShioajiServerInfo>>> ProbeOf(ShioajiAuthClient authClient)
    {
        ArgumentNullException.ThrowIfNull(authClient);

        return cancellationToken => authClient.ProbeInfoAsync(ProbeTimeout, cancellationToken);
    }

    private static int? SafeProcessId(Process? process)
    {
        try
        {
            return process?.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int? SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 行程在這之前就結束了。
            // The process had already ended.
        }
        catch (Win32Exception)
        {
            // 行程正在結束,或沒有權限;兩者都沒有更多可以做的。
            // The process is ending or access was denied; there is nothing more to do either way.
        }
    }

    /// <summary>
    /// 取消監控迴圈並回傳它的工作(沒有監控時為 <see langword="null"/>)。可以重複呼叫。
    /// Cancels the monitor loop and returns its task, or <see langword="null"/> when there is none. Safe to call
    /// repeatedly.
    /// </summary>
    private async Task<Task?> CancelMonitorAsync()
    {
        CancellationTokenSource? source;
        Task? task;

        lock (_stateGate)
        {
            source = _monitorSource;
            task = _monitorTask;
        }

        if (source is not null)
        {
            try
            {
                await source.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // 已經釋放代表已經取消過。
                // Disposed means it was already cancelled.
            }
        }

        return task;
    }

    private void SetState(ShioajiServerState state)
    {
        lock (_stateGate)
        {
            _state = state;
        }
    }

    private void RecordError(Error error)
    {
        lock (_stateGate)
        {
            _lastError = error.Message;
        }
    }

    private async Task<Result> StartCoreAsync(CancellationToken cancellationToken)
    {
        SetState(ShioajiServerState.Starting);

        lock (_stateGate)
        {
            _lastError = null;
        }

        // 先探測:server 已經在跑就接手,不要再啟動第二個。
        // Probe first: a server already running is attached to, and a second is not started.
        var probed = await _probe(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (probed.TryGetValue(out var info))
        {
            if (info.Simulation == !_options.Production)
            {
                lock (_stateGate)
                {
                    _ownsServer = false;
                }

                SetState(ShioajiServerState.Attached);
                ShioajiServerLog.Attached(_logger, _options.Production);

                return Result.Success();
            }

            var mismatch = ModeMismatch(info.Simulation);
            RecordError(mismatch);
            SetState(ShioajiServerState.Stopped);

            return mismatch;
        }

        var executable = ResolveExecutable(_options.ExecutablePath);

        if (executable is null)
        {
            var missing = new Error(
                ShioajiErrorCodes.ServerExecutableNotFound,
                "找不到 shioaji 可執行檔(依序找 ExecutablePath、%USERPROFILE%\\.local\\bin、PATH)。可用 `uv tool install shioaji` 安裝。The shioaji executable was not found (ExecutablePath, then %USERPROFILE%\\.local\\bin, then PATH); install it with `uv tool install shioaji`.",
                ErrorCategory.NotFound);

            RecordError(missing);
            SetState(ShioajiServerState.Stopped);

            return missing;
        }

        var spawned = await SpawnAsync(executable, cancellationToken).ConfigureAwait(false);

        if (spawned.IsFailure)
        {
            RecordError(spawned.Error);
            SetState(ShioajiServerState.Stopped);

            return spawned;
        }

        StartMonitor();

        return Result.Success();
    }

    private void StartMonitor()
    {
        lock (_stateGate)
        {
            if (_process is null)
            {
                return;
            }

            _monitorSource = new CancellationTokenSource();
            var token = _monitorSource.Token;
            _monitorTask = Task.Run(() => MonitorAsync(token), CancellationToken.None);
        }
    }

    private Error ModeMismatch(bool serverSimulation) =>
        new Error(
            ShioajiErrorCodes.ServerModeMismatch,
            string.Create(
                CultureInfo.InvariantCulture,
                $"server 回報 simulation={serverSimulation},但設定的 Production={_options.Production},兩者不一致,已拒絕。本套件不會終止別人啟動的 server。The server reports simulation={serverSimulation} but the configured Production={_options.Production}; refused. This package never kills a server someone else started."),
            ErrorCategory.Conflict);

    /// <summary>
    /// 啟動一個行程並等到 server 就緒。成功時 <c>_process</c> 與狀態都已設好。
    /// Starts one process and waits until the server is ready. On success <c>_process</c> and the state are set.
    /// </summary>
    private async Task<Result> SpawnAsync(string executable, CancellationToken cancellationToken)
    {
        // 憑證在這一刻才取得,馬上登記進遮罩器 —— 之後任何輸出、錯誤、例外文字出現它們都會被換掉。
        // The credentials are obtained only now and registered on the masker at once, so any output, error or
        // exception text that contains them from here on is replaced.
        ShioajiCredentials? credentials;

        try
        {
            credentials = _credentialsProvider();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 憑證提供者是呼叫端的程式碼,什麼都可能丟。這裡不攔,重啟迴圈會整個死掉而狀態停在 Restarting。
            // 例外訊息可能夾帶憑證,先過遮罩器;型別名稱本身不含祕密。
            // The credentials provider is the caller's code and may throw anything. Left uncaught here, the restart
            // loop would die with the state stuck at Restarting. The message may carry a credential, so it goes
            // through the masker first; a type name holds no secret.
            return StartFailed($"憑證提供者擲出 {exception.GetType().Name}:{exception.Message} / the credentials provider threw {exception.GetType().Name}: {exception.Message}");
        }

        if (credentials is null)
        {
            return StartFailed("憑證提供者回傳了 null。 / The credentials provider returned null.");
        }

        RegisterSecrets(credentials);

        var startInfo = BuildStartInfo(executable, credentials);

        lock (_tailGate)
        {
            _tail.Clear();
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => HandleLine(e.Data, isError: false);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data, isError: true);

        try
        {
            if (!process.Start())
            {
                process.Dispose();

                return new Error(
                    ShioajiErrorCodes.ServerStartFailed,
                    "作業系統沒有啟動 shioaji 行程。The operating system did not start the shioaji process.",
                    ErrorCategory.Unavailable);
            }
        }
        catch (Win32Exception exception)
        {
            process.Dispose();

            return StartFailed(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            process.Dispose();

            return StartFailed(exception.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        Result<bool> ready;

        try
        {
            ready = await WaitUntilReadyAsync(process, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            process.Dispose();
            throw;
        }

        if (ready.IsFailure)
        {
            KillQuietly(process);
            process.Dispose();

            return ready.ToResult();
        }

        var alive = ready.GetValueOrThrow();

        lock (_stateGate)
        {
            _ownsServer = true;

            if (alive)
            {
                _process = process;
                _startedAt = _timeProvider.GetUtcNow();
                _state = ShioajiServerState.Running;
            }
            else
            {
                // 行程自己結束了但 server 還在回應:它自行脫離了,沒有行程可以監控。
                // The process ended by itself while the server still answers: it detached, and there is no process
                // left to watch.
                process.Dispose();
                _process = null;
                _startedAt = _timeProvider.GetUtcNow();
                _state = ShioajiServerState.Attached;
            }
        }

        if (alive)
        {
            ShioajiServerLog.Started(_logger, SafeProcessId(process) ?? 0, _options.Production);
        }
        else
        {
            ShioajiServerLog.Attached(_logger, _options.Production);
        }

        return Result.Success();
    }

    /// <summary>
    /// 等 server 就緒。回傳 <see langword="true"/> 代表行程還活著;<see langword="false"/> 代表行程已正常結束、
    /// 但 server 仍在回應(自行脫離)。
    /// Waits for the server to be ready. <see langword="true"/> means the process is still alive;
    /// <see langword="false"/> means it ended normally while the server still answers (it detached itself).
    /// </summary>
    private async Task<Result<bool>> WaitUntilReadyAsync(Process process, CancellationToken cancellationToken)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(_options.StartupTimeout);

        try
        {
            while (true)
            {
                if (SafeHasExited(process))
                {
                    // 等輸出讀完再取最後幾行,否則失敗訊息會少掉最關鍵的最後一行。
                    // Let the output drain before taking the last lines, or the failure would miss the one that
                    // matters most.
                    await process.WaitForExitAsync(startup.Token).ConfigureAwait(false);

                    var exitCode = SafeExitCode(process);
                    var late = await _probe(startup.Token).ConfigureAwait(false);

                    if (exitCode == 0 && late.TryGetValue(out var lateInfo))
                    {
                        if (lateInfo.Simulation == !_options.Production)
                        {
                            return false;
                        }

                        return ModeMismatch(lateInfo.Simulation);
                    }

                    return ExitedEarly(exitCode);
                }

                var probed = await _probe(startup.Token).ConfigureAwait(false);

                if (probed.TryGetValue(out var info))
                {
                    if (info.Simulation == !_options.Production)
                    {
                        return true;
                    }

                    return ModeMismatch(info.Simulation);
                }

                await Task.Delay(_options.StartupPollInterval, _timeProvider, startup.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Error(
                    ShioajiErrorCodes.ServerStartTimeout,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"shioaji server 在 {_options.StartupTimeout.TotalSeconds:F0} 秒內沒有就緒,行程已終止。最後輸出見 Data。The shioaji server was not ready within {_options.StartupTimeout.TotalSeconds:F0} s and the process was killed; the last output is in Data."),
                    ErrorCategory.Timeout)
                .WithData(ShioajiErrorDataKeys.LastOutput, TailText());
        }
    }

    private Error ExitedEarly(int? exitCode)
    {
        var error = new Error(
            ShioajiErrorCodes.ServerExitedEarly,
            string.Create(
                CultureInfo.InvariantCulture,
                $"shioaji 行程在就緒之前就退出了(結束碼 {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "未知"})。最後輸出見 Data。The shioaji process exited before it was ready (exit code {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}); the last output is in Data."),
            ErrorCategory.Unavailable);

        if (exitCode is { } code)
        {
            error = error.WithData(ShioajiErrorDataKeys.ExitCode, code);
        }

        return error.WithData(ShioajiErrorDataKeys.LastOutput, TailText());
    }

    private Error StartFailed(string reason) =>
        new Error(
            ShioajiErrorCodes.ServerStartFailed,
            $"無法啟動 shioaji 行程 / could not start the shioaji process:{Mask(reason)}",
            ErrorCategory.Unavailable);

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Process? process;
            DateTimeOffset? startedAt;

            lock (_stateGate)
            {
                process = _process;
                startedAt = _startedAt;
            }

            if (process is null)
            {
                return;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var exitCode = SafeExitCode(process);
            var ranFor = startedAt is { } started ? _timeProvider.GetUtcNow() - started : TimeSpan.Zero;

            lock (_stateGate)
            {
                _lastExitCode = exitCode;
                _lastError = string.Create(
                    CultureInfo.InvariantCulture,
                    $"行程非預期退出,結束碼 {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "未知"} / the process exited unexpectedly, exit code {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");

                if (ranFor >= _options.StableAfter)
                {
                    _consecutiveRestarts = 0;
                }
            }

            if (!await RestartAsync(exitCode ?? -1, cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>
    /// 反覆嘗試重啟,直到成功(回傳 <see langword="true"/>)、被取消或超過上限(回傳 <see langword="false"/>)。
    /// Tries to restart repeatedly until it succeeds (<see langword="true"/>), is cancelled, or exceeds the limit
    /// (<see langword="false"/>).
    /// </summary>
    private async Task<bool> RestartAsync(int exitCode, CancellationToken cancellationToken)
    {
        while (true)
        {
            int attempt;

            lock (_stateGate)
            {
                _consecutiveRestarts++;
                attempt = _consecutiveRestarts;
            }

            if (attempt > _options.MaxRestarts)
            {
                ReleaseExitedProcess();
                SetState(ShioajiServerState.Faulted);
                ShioajiServerLog.Faulted(_logger, _options.MaxRestarts);

                return false;
            }

            var delay = RestartDelay(attempt);

            lock (_stateGate)
            {
                _restarts++;
                _state = ShioajiServerState.Restarting;
            }

            ShioajiServerLog.Exited(_logger, exitCode, attempt, delay);

            try
            {
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                ReleaseExitedProcess();

                var executable = ResolveExecutable(_options.ExecutablePath);

                if (executable is null)
                {
                    RecordError(new Error(ShioajiErrorCodes.ServerExecutableNotFound, "找不到 shioaji 可執行檔。The shioaji executable was not found.", ErrorCategory.NotFound));
                    continue;
                }

                var spawned = await SpawnAsync(executable, cancellationToken).ConfigureAwait(false);

                if (spawned.IsSuccess)
                {
                    return CurrentState == ShioajiServerState.Running;
                }

                RecordError(spawned.Error);
                ShioajiServerLog.RestartFailed(_logger, Mask(spawned.Error.Message));
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private TimeSpan RestartDelay(int attempt)
    {
        var exponent = Math.Min(attempt - 1, 20);
        var milliseconds = _options.RestartBackoff.TotalMilliseconds * Math.Pow(2d, exponent);

        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, MaxRestartDelay.TotalMilliseconds));
    }

    private void ReleaseExitedProcess()
    {
        Process? process;

        lock (_stateGate)
        {
            process = _process;
            _process = null;
            _startedAt = null;
        }

        process?.Dispose();
    }

    private async Task StopProcessAsync(CancellationToken cancellationToken)
    {
        Process? process;
        bool owns;

        lock (_stateGate)
        {
            process = _process;
            owns = _ownsServer;
        }

        if (process is null && !owns)
        {
            // 外部啟動、只是接手的 server,或根本沒在跑:什麼都不動。
            // A server started externally and merely attached to, or nothing running: nothing is touched.
            return;
        }

        if (owns)
        {
            await RunStopCommandAsync(cancellationToken).ConfigureAwait(false);
        }

        if (process is not null)
        {
            if (!SafeHasExited(process))
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wait.CancelAfter(_options.StopTimeout);

                try
                {
                    await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    KillQuietly(process);
                    ShioajiServerLog.Killed(_logger);

                    using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                    try
                    {
                        await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // 終止後仍沒結束,沒有更多可以做的;行程物件照樣釋放。
                        // Still not gone after the kill; nothing more can be done, and the object is released anyway.
                    }
                }
            }
        }

        ReleaseExitedProcess();

        lock (_stateGate)
        {
            _ownsServer = false;
            _startedAt = null;
        }
    }

    private async Task RunStopCommandAsync(CancellationToken cancellationToken)
    {
        var executable = ResolveExecutable(_options.ExecutablePath);

        if (executable is null)
        {
            return;
        }

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("server");
        startInfo.ArgumentList.Add("stop");
        ApplyNonSecretEnvironment(startInfo);

        using var stop = new Process { StartInfo = startInfo };
        stop.OutputDataReceived += (_, e) => HandleLine(e.Data, isError: false);
        stop.ErrorDataReceived += (_, e) => HandleLine(e.Data, isError: true);

        try
        {
            if (!stop.Start())
            {
                return;
            }
        }
        catch (Win32Exception)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        stop.BeginOutputReadLine();
        stop.BeginErrorReadLine();

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(_options.StopTimeout);

        try
        {
            await stop.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(stop);
        }
    }

    private ProcessStartInfo BuildStartInfo(string executable, ShioajiCredentials credentials)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // 不帶 --no-open 會自動開瀏覽器,所以一律帶。
        // Without --no-open the CLI opens a browser, so it is always passed.
        startInfo.ArgumentList.Add("server");
        startInfo.ArgumentList.Add("start");
        startInfo.ArgumentList.Add("--no-open");

        if (_options.Production)
        {
            startInfo.ArgumentList.Add("--production");
        }

        if (_options.RequireAuth)
        {
            startInfo.ArgumentList.Add("--auth");
        }

        if (_options.ReceiveWindowMs is { } window)
        {
            startInfo.ArgumentList.Add("--receive-window");
            startInfo.ArgumentList.Add(window.ToString(CultureInfo.InvariantCulture));
        }

        ApplyNonSecretEnvironment(startInfo);

        var environment = startInfo.Environment;
        environment["SJ_API_KEY"] = credentials.ApiKey;
        environment["SJ_SEC_KEY"] = credentials.SecretKey;

        SetOrRemove(environment, "SJ_CA_PATH", credentials.CaPath);
        SetOrRemove(environment, "SJ_CA_PASSWD", credentials.CaPassword);

        return startInfo;
    }

    private void ApplyNonSecretEnvironment(ProcessStartInfo startInfo)
    {
        var environment = startInfo.Environment;

        // 模式、位址與認證一律明確設定,不繼承使用者機器上可能殘留的同名環境變數。
        // Mode, address and authentication are always set explicitly rather than inherited from whatever the same
        // variable names hold on the user's machine.
        environment["SJ_PRODUCTION"] = _options.Production ? "true" : "false";
        environment["SJ_HTTP_ADDR"] = _options.BindAddress;
        environment["SJ_HTTP_AUTH"] = _options.RequireAuth ? "true" : "false";

        SetOrRemove(environment, "SJ_HOME_PATH", _options.HomePath, removeWhenEmpty: false);

        foreach (var (key, value) in _options.AdditionalEnvironment)
        {
            environment[key] = value;
        }
    }

    private static void SetOrRemove(
        IDictionary<string, string?> environment,
        string key,
        string? value,
        bool removeWhenEmpty = true)
    {
        if (!string.IsNullOrEmpty(value))
        {
            environment[key] = value;
        }
        else if (removeWhenEmpty)
        {
            environment.Remove(key);
        }
    }

    private void RegisterSecrets(ShioajiCredentials credentials)
    {
        foreach (var value in new[] { credentials.ApiKey, credentials.SecretKey, credentials.CaPath, credentials.CaPassword })
        {
            // 短於 8 個字元的值無法登記(會大量誤遮一般文字),略過;見類別說明。
            // A value shorter than 8 characters cannot be registered and is skipped; see the class remarks.
            if (!string.IsNullOrWhiteSpace(value) && value.Length >= SecretMasker.MinimumKnownSecretLength)
            {
                _ = _masker.RegisterKnownSecret(value);
            }
        }
    }

    private void HandleLine(string? line, bool isError)
    {
        if (line is null)
        {
            return;
        }

        var masked = Mask(line);

        lock (_tailGate)
        {
            _tail.Enqueue(masked);

            while (_tail.Count > TailLines)
            {
                _ = _tail.Dequeue();
            }
        }

        if (isError)
        {
            ShioajiServerLog.ErrorOutput(_logger, masked);
        }
        else
        {
            ShioajiServerLog.Output(_logger, masked);
        }
    }

    private string TailText()
    {
        lock (_tailGate)
        {
            return string.Join('\n', _tail);
        }
    }

    private string Mask(string text) => _masker.MaskText(text) ?? string.Empty;

    /// <summary>
    /// 找 <c>shioaji</c> 可執行檔:指定的路徑、<c>%USERPROFILE%\.local\bin</c>、再來是 <c>PATH</c>。
    /// Finds the <c>shioaji</c> executable: the configured path, then <c>%USERPROFILE%\.local\bin</c>, then <c>PATH</c>.
    /// </summary>
    internal static string? ResolveExecutable(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        var fileName = OperatingSystem.IsWindows() ? "shioaji.exe" : "shioaji";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(home))
        {
            var local = Path.Combine(home, ".local", "bin", fileName);

            if (File.Exists(local))
            {
                return local;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), fileName);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // PATH 裡有不合法的路徑字元,略過那一段。
                // An invalid path character in a PATH entry; that entry is skipped.
            }
        }

        return null;
    }
}
