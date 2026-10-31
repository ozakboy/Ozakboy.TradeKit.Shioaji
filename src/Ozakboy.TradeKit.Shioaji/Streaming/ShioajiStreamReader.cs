using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ozakboy.Security.Masking;

namespace Ozakboy.TradeKit.Shioaji.Streaming;

/// <summary>
/// 讀取 Shioaji 的聚合 SSE 串流(<c>GET /api/v1/stream/data</c>):一條連線帶全部具名事件、共用心跳,
/// 只算一條 SSE 連線。
/// Reads Shioaji's aggregate SSE stream (<c>GET /api/v1/stream/data</c>): one connection carries every named event
/// with shared heartbeats and counts as a single SSE connection.
/// </summary>
/// <remarks>
/// <para>
/// <b>用法。</b>先用 <see cref="MarketData.ShioajiQuoteClient"/> 與 <see cref="Auth.ShioajiAuthClient"/> 訂閱,
/// 再 <c>await foreach</c> 讀 <see cref="ReadAsync"/>。讀取器在 <c>ReadAsync</c> 被列舉時才連線,
/// 列舉結束(或取消)就關閉連線;<b>同一時間只能有一個消費端</b>,第二個會立刻得到
/// <see cref="ShioajiErrorCodes.StreamAlreadyReading"/> 然後結束。
/// <b>Usage.</b> Subscribe first through <see cref="MarketData.ShioajiQuoteClient"/> and
/// <see cref="Auth.ShioajiAuthClient"/>, then <c>await foreach</c> over <see cref="ReadAsync"/>. The reader
/// connects only while <c>ReadAsync</c> is being enumerated and closes when the enumeration ends or is cancelled.
/// <b>Only one consumer may read at a time</b>; a second gets <see cref="ShioajiErrorCodes.StreamAlreadyReading"/>
/// at once and ends.
/// </para>
/// <para>
/// <b>斷線重連。</b>連線結束、讀取失敗或 <see cref="ShioajiOptions.StreamIdleTimeout"/> 內沒收到任何一行(含心跳與註解)
/// 時,關閉、退避(<see cref="ShioajiOptions.ReconnectBackoff"/>)、重連。<b>Shioaji 斷線後必須重新訂閱</b>,
/// 所以重連成功之後會呼叫建構時注入的回呼(用來重放行情訂閱與委託回報訂閱);回呼回傳失敗或丟出可預期的
/// 例外只寫日誌,串流照常繼續。每一次連線、斷線、重連都以 <see cref="ShioajiStreamLifecycleEvent"/> 從同一個
/// <c>IAsyncEnumerable</c> 送出。
/// <b>Reconnection.</b> When the connection ends, a read fails, or no line at all, heartbeats and comments included,
/// arrives within <see cref="ShioajiOptions.StreamIdleTimeout"/>, the connection is closed, backed off
/// (<see cref="ShioajiOptions.ReconnectBackoff"/>) and remade. <b>Shioaji needs subscriptions made again after a
/// drop</b>, so the callback injected at construction runs after a successful reconnection (use it to replay
/// market-data and order-report subscriptions); a failed result or an expected exception is only logged and
/// the stream carries on. Every connect, disconnect and reconnect is delivered as a
/// <see cref="ShioajiStreamLifecycleEvent"/> through the same <c>IAsyncEnumerable</c>.
/// </para>
/// <para>
/// <b>背壓。</b>內部佇列有界(<see cref="ShioajiOptions.StreamChannelCapacity"/>),滿了<b>丟棄最舊的</b>並累計到
/// <see cref="DroppedEvents"/>,絕不阻塞讀取 —— 消費端慢,最壞的結果是少看到舊事件,而不是讓 server 的緩衝區塞滿。
/// 佇列裡的生命週期事件也可能被丟棄,所以判斷資料缺口請同時參考 <see cref="DroppedEvents"/>。
/// <b>Backpressure.</b> The internal queue is bounded (<see cref="ShioajiOptions.StreamChannelCapacity"/>); when it
/// is full the <b>oldest is dropped</b> and counted in <see cref="DroppedEvents"/>, and reading never blocks — a
/// slow consumer sees fewer old events at worst, instead of backing the server's buffer up. Lifecycle events in the
/// queue can be dropped too, so judge data gaps together with <see cref="DroppedEvents"/>.
/// </para>
/// <para>
/// <b>未知格式只記一次。</b>註解行、不認得的欄位名、沒有冒號的行,每個種類第一次出現時寫一行日誌(第一個心跳的原文也是),
/// 供實測心跳格式時使用。
/// <b>Unknown formats are logged once.</b> A comment line, an unrecognised field name and a line with no colon each
/// get one log line the first time their kind appears, and so does the text of the first heartbeat, to help measure
/// the heartbeat format.
/// </para>
/// </remarks>
public sealed class ShioajiStreamReader
{
    private const string StreamPath = "api/v1/stream/data";

    private const int MaxLoggedTextLength = 200;

    private readonly HttpClient _http;
    private readonly ShioajiOptions _options;
    private readonly ILogger _logger;
    private readonly Func<CancellationToken, Task<Result>>? _onReconnected;
    private readonly TimeProvider _timeProvider;
    private readonly SecretMasker _masker;
    private readonly Lock _stateGate = new();
    private readonly HashSet<string> _loggedKinds = [];

    private int _reading;
    private ShioajiStreamReaderState _state = ShioajiStreamReaderState.Stopped;
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset? _lastEventAt;
    private int _reconnects;
    private long _dropped;

    /// <summary>
    /// 建立讀取器。
    /// Creates the reader.
    /// </summary>
    /// <param name="httpClient">
    /// 串流專用的 <see cref="HttpClient"/>,逾時必須是 <see cref="Timeout.InfiniteTimeSpan"/>(DI 註冊的具名用戶端
    /// <see cref="ShioajiConstants.StreamHttpClientName"/> 已設定好)。呼叫端負責它的生命週期。
    /// The <see cref="HttpClient"/> dedicated to the stream; its timeout must be
    /// <see cref="Timeout.InfiniteTimeSpan"/> (the named client <see cref="ShioajiConstants.StreamHttpClientName"/>
    /// registered by DI is already so). The caller owns its lifetime.
    /// </param>
    /// <param name="options">連線設定。The connection settings.</param>
    /// <param name="logger">日誌;<see langword="null"/> 時不寫。The logger, or none when <see langword="null"/>.</param>
    /// <param name="onReconnected">
    /// 重連成功之後呼叫的回呼,用來重放訂閱;<see langword="null"/> 表示不需要。
    /// The callback run after a successful reconnection to replay subscriptions, or <see langword="null"/> for none.
    /// </param>
    /// <param name="timeProvider">時間來源;<see langword="null"/> 時用系統時間。The time source, or system time when <see langword="null"/>.</param>
    /// <param name="masker">
    /// 遮罩器,用於寫進日誌與生命週期事件的文字;<see langword="null"/> 時用 <see cref="SecretMasker.Default"/>。
    /// The masker for text that reaches logs and lifecycle events; <see cref="SecretMasker.Default"/> when <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="httpClient"/> 或 <paramref name="options"/> 為 <see langword="null"/> 時擲出。
    /// Thrown when <paramref name="httpClient"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="options"/> 不合法時擲出。Thrown when <paramref name="options"/> is invalid.
    /// </exception>
    public ShioajiStreamReader(
        HttpClient httpClient,
        ShioajiOptions options,
        ILogger<ShioajiStreamReader>? logger = null,
        Func<CancellationToken, Task<Result>>? onReconnected = null,
        TimeProvider? timeProvider = null,
        SecretMasker? masker = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        var validation = options.Validate();

        if (validation.IsFailure)
        {
            throw new ArgumentException(validation.Error.Message, nameof(options));
        }

        _http = httpClient;
        _options = options;
        _logger = logger ?? NullLogger<ShioajiStreamReader>.Instance;
        _onReconnected = onReconnected;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _masker = masker ?? SecretMasker.Default;
    }

    /// <summary>
    /// 累計因佇列滿了而丟棄的事件數。
    /// The cumulative number of events dropped because the queue was full.
    /// </summary>
    public long DroppedEvents => Interlocked.Read(ref _dropped);

    /// <summary>
    /// 目前狀態的快照。
    /// A snapshot of the current state.
    /// </summary>
    public ShioajiStreamReaderStatus Status
    {
        get
        {
            lock (_stateGate)
            {
                return new ShioajiStreamReaderStatus
                {
                    State = _state,
                    ConnectedAt = _connectedAt,
                    LastEventAt = _lastEventAt,
                    Reconnects = _reconnects,
                    DroppedEvents = Interlocked.Read(ref _dropped),
                };
            }
        }
    }

    /// <summary>
    /// 讀取串流。列舉開始時連線,列舉結束或取消時關閉。
    /// Reads the stream. It connects when enumeration starts and closes when it ends or is cancelled.
    /// </summary>
    /// <param name="cancellationToken">
    /// 取消權杖。取消時列舉安靜地結束,不擲出 <see cref="OperationCanceledException"/>。
    /// The cancellation token. Cancelling ends the enumeration quietly without throwing
    /// <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// 事件序列。每一項是成功的事件,或失敗(讀不懂的事件,串流不會因此結束)。
    /// The event sequence. Each item is a successful event or a failure (an unreadable event; the stream carries on).
    /// </returns>
    public async IAsyncEnumerable<Result<ShioajiStreamEvent>> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _reading, 1, 0) != 0)
        {
            yield return new Error(
                ShioajiErrorCodes.StreamAlreadyReading,
                "這個讀取器已經有一個消費端在讀。The reader already has a consumer reading.",
                ErrorCategory.Conflict);
            yield break;
        }

        var channel = Channel.CreateBounded<Result<ShioajiStreamEvent>>(
            new BoundedChannelOptions(_options.StreamChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            },
            OnDropped);

        using var pumpSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = Task.Run(() => PumpAsync(channel.Writer, pumpSource.Token), CancellationToken.None);

        try
        {
            var reader = channel.Reader;

            while (true)
            {
                bool more;

                try
                {
                    more = await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!more)
                {
                    break;
                }

                while (reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            await pumpSource.CancelAsync().ConfigureAwait(false);

            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消是預期的收尾。
                // Cancellation is the expected way to finish.
            }
            finally
            {
                SetState(ShioajiStreamReaderState.Stopped, connectedAt: null);
                Volatile.Write(ref _reading, 0);
            }
        }
    }

    private void OnDropped(Result<ShioajiStreamEvent> item)
    {
        _ = item;
        var total = Interlocked.Increment(ref _dropped);

        if (total == 1 || total % 1000 == 0)
        {
            ShioajiStreamLog.Dropped(_logger, total);
        }
    }

    private async Task PumpAsync(ChannelWriter<Result<ShioajiStreamEvent>> writer, CancellationToken cancellationToken)
    {
        var attempt = 0;
        var everConnected = false;
        var lastReason = string.Empty;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (attempt > 0)
                {
                    SetState(ShioajiStreamReaderState.Reconnecting, connectedAt: null);
                    Emit(writer, Lifecycle(ShioajiStreamLifecycleKind.Reconnecting, attempt, lastReason));

                    var delay = _options.ReconnectBackoff.GetDelay(attempt, NextJitterSample());
                    ShioajiStreamLog.ReconnectScheduled(_logger, attempt, delay, lastReason);

                    try
                    {
                        await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                else
                {
                    SetState(ShioajiStreamReaderState.Connecting, connectedAt: null);
                }

                var outcome = await ConnectAndReadAsync(writer, attempt, everConnected, cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (outcome.Connected)
                {
                    everConnected = true;
                    SetState(ShioajiStreamReaderState.Reconnecting, connectedAt: null);
                    ShioajiStreamLog.Disconnected(_logger, attempt, outcome.Reason);
                    Emit(writer, Lifecycle(ShioajiStreamLifecycleKind.Disconnected, attempt, outcome.Reason));
                }
                else
                {
                    ShioajiStreamLog.ConnectFailed(_logger, outcome.Reason);
                }

                // 一條連線健康地收過資料,下一次中斷就從第 1 次重連重新起算,退避也跟著回到初始值;
                // 連資料都沒收到就斷的連線(例如 server 一接受就關閉)不重置,免得每秒重連打爆 server。
                // A connection that delivered data counts as healthy, so the next outage starts again from
                // reconnection 1 and the backoff returns to its initial value; one that dropped before any data
                // (a server closing right after accepting) does not reset, so it cannot be hammered every second.
                attempt = outcome.ReceivedData ? 1 : attempt + 1;
                lastReason = outcome.Reason;
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task<ConnectionOutcome> ConnectAndReadAsync(
        ChannelWriter<Result<ShioajiStreamEvent>> writer,
        int attempt,
        bool isReconnect,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (_options.BearerToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;

        using (var connectSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connectSource.CancelAfter(_options.RequestTimeout);

            try
            {
                response = await _http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectSource.Token)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                return ConnectionOutcome.Failed(Mask($"連線失敗 / connect failed: {exception.Message}"));
            }
            catch (OperationCanceledException)
            {
                return ConnectionOutcome.Failed(
                    cancellationToken.IsCancellationRequested
                        ? "已取消 / cancelled"
                        : "連線逾時 / connect timed out");
            }
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return ConnectionOutcome.Failed(
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"HTTP {(int)response.StatusCode}"));
            }

            Stream stream;

            try
            {
                stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                return ConnectionOutcome.Failed(Mask($"讀取回應失敗 / reading the response failed: {exception.Message}"));
            }
            catch (OperationCanceledException)
            {
                return ConnectionOutcome.Failed("已取消 / cancelled");
            }

            using (stream)
            {
                var connectedAt = _timeProvider.GetUtcNow();
                SetState(ShioajiStreamReaderState.Connected, connectedAt);

                if (isReconnect)
                {
                    lock (_stateGate)
                    {
                        _reconnects++;
                    }
                }

                ShioajiStreamLog.Connected(_logger, attempt);
                Emit(writer, Lifecycle(ShioajiStreamLifecycleKind.Connected, attempt, reason: null));

                if (isReconnect)
                {
                    await ReplayAsync(cancellationToken).ConfigureAwait(false);
                }

                return await ReadLoopAsync(stream, writer, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ConnectionOutcome> ReadLoopAsync(
        Stream stream,
        ChannelWriter<Result<ShioajiStreamEvent>> writer,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var idle = new CancellationTokenSource(Timeout.InfiniteTimeSpan, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idle.Token);

        var parser = new ShioajiSseParser();
        var receivedData = false;

        while (true)
        {
            idle.CancelAfter(_options.StreamIdleTimeout);

            string? line;

            try
            {
                line = await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return cancellationToken.IsCancellationRequested
                    ? ConnectionOutcome.Ended(receivedData, "已取消 / cancelled")
                    : ConnectionOutcome.Ended(
                        receivedData,
                        string.Create(
                            System.Globalization.CultureInfo.InvariantCulture,
                            $"閒置逾時,{_options.StreamIdleTimeout.TotalSeconds:F0} 秒內沒收到任何資料 / idle timeout, no data within {_options.StreamIdleTimeout.TotalSeconds:F0} s"));
            }
            catch (IOException exception)
            {
                return ConnectionOutcome.Ended(receivedData, Mask($"讀取失敗 / read failed: {exception.Message}"));
            }
            catch (HttpRequestException exception)
            {
                return ConnectionOutcome.Ended(receivedData, Mask($"讀取失敗 / read failed: {exception.Message}"));
            }

            if (line is null)
            {
                return ConnectionOutcome.Ended(receivedData, "伺服器關閉了串流 / the server closed the stream");
            }

            receivedData = true;

            var kind = parser.Feed(line, out var frame, out var fieldName);

            switch (kind)
            {
                case ShioajiSseLineKind.Comment:
                    LogFirst("comment", line);
                    break;
                case ShioajiSseLineKind.UnknownField:
                    LogFirst("unknown-field:" + fieldName, line);
                    break;
            }

            if (frame is not { } complete)
            {
                continue;
            }

            var receivedAt = _timeProvider.GetUtcNow();

            lock (_stateGate)
            {
                _lastEventAt = receivedAt;
            }

            var parsed = ShioajiStreamEventParser.Parse(complete.EventName, complete.Data, receivedAt);

            if (parsed.TryGetValue(out var evt) && evt is ShioajiHeartbeat heartbeat)
            {
                LogFirst("heartbeat", $"event={heartbeat.EventName}; data={heartbeat.RawData}");
            }

            writer.TryWrite(parsed);
        }
    }

    private async Task ReplayAsync(CancellationToken cancellationToken)
    {
        if (_onReconnected is null)
        {
            return;
        }

        try
        {
            var result = await _onReconnected(cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                ShioajiStreamLog.ReplayFailed(_logger, Mask(result.Error.ToString()));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 被取消代表整個讀取器正在收尾,不是重放失敗。
            // Cancellation means the whole reader is winding down; it is not a failed replay.
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException or TimeoutException)
        {
            ShioajiStreamLog.ReplayFailed(_logger, Mask(exception.Message));
        }
    }

    private Uri BuildUri()
    {
        var baseUri = _options.BaseUri.AbsoluteUri;

        if (!baseUri.EndsWith('/'))
        {
            baseUri += "/";
        }

        return new Uri(baseUri + StreamPath, UriKind.Absolute);
    }

    private ShioajiStreamLifecycleEvent Lifecycle(ShioajiStreamLifecycleKind kind, int attempt, string? reason) =>
        new()
        {
            ReceivedAt = _timeProvider.GetUtcNow(),
            Kind = kind,
            Attempt = attempt,
            Reason = reason,
        };

    private static void Emit(ChannelWriter<Result<ShioajiStreamEvent>> writer, ShioajiStreamEvent evt) =>
        writer.TryWrite(Result.Success(evt));

    private void SetState(ShioajiStreamReaderState state, DateTimeOffset? connectedAt)
    {
        lock (_stateGate)
        {
            _state = state;
            _connectedAt = connectedAt;
        }
    }

    private void LogFirst(string kind, string text)
    {
        if (!_loggedKinds.Add(kind))
        {
            return;
        }

        var masked = Mask(text);
        ShioajiStreamLog.UnknownFormat(
            _logger,
            kind,
            masked.Length > MaxLoggedTextLength ? masked[..MaxLoggedTextLength] : masked);
    }

    private string Mask(string text) => _masker.MaskText(text) ?? string.Empty;

    private static double NextJitterSample() => RandomNumberGenerator.GetInt32(0, 1_000_001) / 1_000_000d;

    /// <summary>
    /// 一次連線嘗試的結果。
    /// The outcome of one connection attempt.
    /// </summary>
    private readonly record struct ConnectionOutcome(bool Connected, bool ReceivedData, string Reason)
    {
        public static ConnectionOutcome Failed(string reason) => new(false, false, reason);

        public static ConnectionOutcome Ended(bool receivedData, string reason) => new(true, receivedData, reason);
    }
}
