using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Ozakboy.TradeKit.Shioaji.Tests.Support;

/// <summary>
/// 一個請求的紀錄,供斷言用。
/// A record of one request, for assertions.
/// </summary>
/// <param name="Method">HTTP 方法。The HTTP method.</param>
/// <param name="Path">絕對路徑(不含查詢字串)。The absolute path without the query string.</param>
/// <param name="Query">查詢字串(含開頭的 ?,沒有時為空字串)。The query string including the leading ?, or empty.</param>
/// <param name="Body">請求本文。The request body.</param>
/// <param name="Headers">請求標頭(鍵不分大小寫)。The request headers, with case-insensitive keys.</param>
public sealed record FakeRequest(
    string Method,
    string Path,
    string Query,
    string Body,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// 假伺服器要回的內容。
/// What the fake server answers with.
/// </summary>
/// <param name="Status">狀態碼。The status code.</param>
/// <param name="Body">回應本文。The response body.</param>
/// <param name="ContentType">內容類型。The content type.</param>
/// <param name="Chunked">是否用分塊傳輸(不帶 Content-Length)。Whether to use chunked transfer, with no Content-Length.</param>
public sealed record FakeResponse(int Status, string Body, string ContentType = "application/json", bool Chunked = false);

/// <summary>
/// 心跳的寫法。真 server 的心跳格式<b>尚未實測</b>(<c>docs/external/shioaji-http-api.md</c> §4 與 §9),所以兩種都能模擬。
/// How a heartbeat is written. The real server's heartbeat format is <b>unmeasured</b>
/// (<c>docs/external/shioaji-http-api.md</c> §4 and §9), so both are simulated.
/// </summary>
public enum FakeHeartbeatStyle
{
    /// <summary>
    /// 具名事件:<c>event:heartbeat</c> 加一行 <c>data:</c>。
    /// A named event: <c>event:heartbeat</c> plus a <c>data:</c> line.
    /// </summary>
    NamedEvent = 0,

    /// <summary>
    /// SSE 註解行:<c>: heartbeat</c>。
    /// An SSE comment line: <c>: heartbeat</c>.
    /// </summary>
    Comment = 1,
}

/// <summary>
/// 假伺服器上的一條 SSE 連線。
/// One SSE connection on the fake server.
/// </summary>
public sealed class FakeSseConnection
{
    private readonly HttpListenerResponse _response;
    private readonly Lock _writeLock = new();
    private int _closed;

    internal FakeSseConnection(HttpListenerResponse response)
    {
        _response = response;
    }

    /// <summary>
    /// 連線是否已經關閉。
    /// Whether the connection is closed.
    /// </summary>
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>
    /// 寫出原始文字,不做任何包裝。
    /// Writes raw text with no wrapping.
    /// </summary>
    /// <param name="text">要寫的文字。The text to write.</param>
    /// <returns>寫成功為 true;連線已斷為 false。True when written, false when the connection is gone.</returns>
    public Task<bool> WriteRawAsync(string text) => Task.FromResult(WriteRaw(text));

    private bool WriteRaw(string text)
    {
        if (IsClosed)
        {
            return false;
        }

        // 測試替身:同步寫入就夠了,而且用一把鎖就能保證兩個事件不會交錯寫成一行。
        // A test double: synchronous writes are enough, and a lock keeps two events from interleaving into one line.
        lock (_writeLock)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                _response.OutputStream.Write(bytes, 0, bytes.Length);
                _response.OutputStream.Flush();

                return true;
            }
            catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
            {
                Volatile.Write(ref _closed, 1);
                return false;
            }
        }
    }

    /// <summary>
    /// 寫出一個 SSE 事件。格式依 <c>docs/external/shioaji-http-api.md</c> §4:<c>event:&lt;名&gt;</c> 與
    /// <c>data:&lt;JSON&gt;</c> 各一行(冒號後<b>沒有</b>空白),再加一個空行結束。
    /// Writes one SSE event. The format follows <c>docs/external/shioaji-http-api.md</c> §4: an
    /// <c>event:&lt;name&gt;</c> line and a <c>data:&lt;JSON&gt;</c> line (with <b>no</b> space after the colon),
    /// then a blank line to end it.
    /// </summary>
    /// <param name="eventName">事件名。The event name.</param>
    /// <param name="data">資料(JSON 文字)。The data, as JSON text.</param>
    /// <returns>寫成功為 true。True when written.</returns>
    public Task<bool> SendEventAsync(string eventName, string data) =>
        WriteRawAsync($"event:{eventName}\ndata:{data}\n\n");

    /// <summary>
    /// 寫出一個心跳。
    /// Writes a heartbeat.
    /// </summary>
    /// <param name="style">心跳的寫法。How the heartbeat is written.</param>
    /// <returns>寫成功為 true。True when written.</returns>
    public Task<bool> SendHeartbeatAsync(FakeHeartbeatStyle style = FakeHeartbeatStyle.NamedEvent) =>
        style == FakeHeartbeatStyle.Comment
            ? WriteRawAsync(": heartbeat\n\n")
            : WriteRawAsync("event:heartbeat\ndata:{}\n\n");

    /// <summary>
    /// 正常關閉串流(用戶端讀到串流結束)。
    /// Closes the stream gracefully; the client reads end-of-stream.
    /// </summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        try
        {
            _response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 用戶端可能已經先斷線。
            // The client may have disconnected first.
        }
    }

    /// <summary>
    /// 粗暴地中斷連線(用戶端讀到連線被重設)。
    /// Tears the connection down abruptly; the client sees a reset.
    /// </summary>
    public void Abort()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        try
        {
            _response.Abort();
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 同上。
            // Same as above.
        }
    }
}

/// <summary>
/// 用 BCL 的 <see cref="HttpListener"/> 在 <c>127.0.0.1:&lt;隨機 port&gt;</c> 起的 Shioaji server 替身。
/// A stand-in for the Shioaji server on <c>127.0.0.1:&lt;random port&gt;</c>, built on the BCL's
/// <see cref="HttpListener"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>行為依據</b>(<c>docs/external/shioaji-http-api.md</c> 的段落):
/// <b>Behaviours and where they come from</b>, by section of <c>docs/external/shioaji-http-api.md</c>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// §4 SSE:聚合串流 <c>GET /api/v1/stream/data</c>,每個事件是 <c>event:&lt;名&gt;</c> + <c>data:&lt;JSON&gt;</c> 兩行加空行。
/// 冒號後沒有空白是文件範例的寫法。
/// §4 SSE: the aggregate stream <c>GET /api/v1/stream/data</c>, each event an <c>event:&lt;name&gt;</c> line plus a
/// <c>data:&lt;JSON&gt;</c> line plus a blank line, with no space after the colon as in the documentation's example.
/// </description></item>
/// <item><description>
/// §4「連續月必帶 target_code,否則 200 但只收到 heartbeat」與 §2「未訂閱時 order_event 只有 heartbeat」:
/// <see cref="Publish"/> 只有在訂閱存在時才送出資料事件,否則改送一個心跳;預設的 <c>stream/subscribe</c> 與
/// <c>auth/subscribe_trade</c> 處理器會登記訂閱。
/// §4 "a continuous-month contract needs target_code, or the answer is 200 with only heartbeats" and §2 "without a
/// trade subscription order_event carries only heartbeats": <see cref="Publish"/> sends a data event only when a
/// matching subscription exists and a heartbeat otherwise; the default <c>stream/subscribe</c> and
/// <c>auth/subscribe_trade</c> handlers register the subscriptions.
/// </description></item>
/// <item><description>
/// §4 訂閱回應 <c>{success, message, subscription}</c>;§2 帳號是陣列、欄位 <c>account_type</c> 等;
/// §7 沒有的帳務端點本替身也沒有。
/// §4 subscription answer <c>{success, message, subscription}</c>; §2 accounts as an array with fields such as
/// <c>account_type</c>; the account endpoints of §7 the package does not call are not here either.
/// </description></item>
/// <item><description>
/// §5 歷史與查詢:<c>kbars</c> / <c>ticks</c> 的欄位陣列回應與其餘查詢端點的預設樣本,見 <c>MapHistoryDefaults</c>。
/// §5 history and queries: the column-array answers of <c>kbars</c> and <c>ticks</c> and default samples for the
/// other query endpoints, see <c>MapHistoryDefaults</c>.
/// </description></item>
/// <item><description>
/// §1「設定 <c>SJ_HTTP_AUTH</c> 後要 <c>Authorization: Bearer &lt;key&gt;:&lt;secret&gt;</c>」:
/// <see cref="RequireBearer"/> 開啟後缺標頭或不符回 401。錯誤 JSON 的形狀<b>文件沒寫</b>(§9),此處用
/// FastAPI 慣例的 <c>{"detail": ...}</c>,只是為了讓錯誤本文有東西可截取。
/// §1 "with <c>SJ_HTTP_AUTH</c> set, requests need <c>Authorization: Bearer &lt;key&gt;:&lt;secret&gt;</c>":
/// after <see cref="RequireBearer"/> a missing or wrong header answers 401. The error JSON's shape is <b>not
/// documented</b> (§9); the FastAPI-style <c>{"detail": ...}</c> here only gives the error body something to
/// truncate.
/// </description></item>
/// </list>
/// <para>
/// 心跳的格式文件沒寫(§4、§9),所以 <see cref="HeartbeatStyle"/> 兩種都能模擬。
/// The heartbeat format is not documented (§4, §9), so <see cref="HeartbeatStyle"/> simulates both.
/// </para>
/// </remarks>
public sealed class FakeShioajiHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<(string Method, string Path), Func<FakeRequest, FakeResponse>> _routes = [];
    private readonly List<FakeRequest> _requests = [];
    private readonly List<FakeSseConnection> _connections = [];
    private readonly HashSet<string> _subscribedCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;
    private string? _bearer;
    private int _connectionTotal;
    private bool _tradeSubscribed;

    /// <summary>
    /// 啟動假伺服器。
    /// Starts the fake server.
    /// </summary>
    public FakeShioajiHttpServer()
    {
        // HttpListener 不能綁 port 0,所以先向系統要一個空的 port 再綁。極少數情況下被別人搶走就換一個重試。
        // HttpListener cannot bind port 0, so a free port is asked from the system first. If someone takes it in the
        // gap, another is tried.
        for (var attempt = 0; ; attempt++)
        {
            var port = FindFreePort();

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                _listener.Start();
                BaseUri = new Uri($"http://127.0.0.1:{port}/");
                break;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                // 換一個 port 再試。
                // Try another port.
            }
        }

        MapDefaults();
        _loop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>
    /// 伺服器的基底位址(含結尾斜線)。
    /// The server's base address, with a trailing slash.
    /// </summary>
    public Uri BaseUri { get; }

    /// <summary>
    /// 綁定位址的 <c>host:port</c> 形式,對應 <c>SJ_HTTP_ADDR</c>。
    /// The bound address as <c>host:port</c>, matching <c>SJ_HTTP_ADDR</c>.
    /// </summary>
    public string BindAddress => $"{BaseUri.Host}:{BaseUri.Port}";

    /// <summary>
    /// 心跳的寫法;預設是具名事件。
    /// How heartbeats are written; a named event by default.
    /// </summary>
    public FakeHeartbeatStyle HeartbeatStyle { get; set; } = FakeHeartbeatStyle.NamedEvent;

    /// <summary>
    /// 收到的所有請求(快照)。
    /// A snapshot of every request received.
    /// </summary>
    public IReadOnlyList<FakeRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>
    /// 累計接受過的 SSE 連線數。
    /// The number of SSE connections accepted so far.
    /// </summary>
    public int SseConnectionTotal => Volatile.Read(ref _connectionTotal);

    /// <summary>
    /// 目前還開著的 SSE 連線(快照)。
    /// A snapshot of the SSE connections still open.
    /// </summary>
    public IReadOnlyList<FakeSseConnection> OpenConnections
    {
        get
        {
            lock (_gate)
            {
                return [.. _connections.Where(c => !c.IsClosed)];
            }
        }
    }

    /// <summary>
    /// 已登記的行情訂閱代碼(<c>code</c> 與 <c>target_code</c> 都算)。
    /// The registered market-data subscription codes, counting both <c>code</c> and <c>target_code</c>.
    /// </summary>
    public IReadOnlyCollection<string> SubscribedCodes
    {
        get
        {
            lock (_gate)
            {
                return [.. _subscribedCodes];
            }
        }
    }

    /// <summary>
    /// 要求 <c>Authorization: Bearer &lt;token&gt;</c>;不符回 401。
    /// Requires <c>Authorization: Bearer &lt;token&gt;</c> and answers 401 otherwise.
    /// </summary>
    /// <param name="token">預期的 token(<c>key:secret</c>)。The expected token (<c>key:secret</c>).</param>
    public void RequireBearer(string token) => _bearer = token;

    /// <summary>
    /// 登記一個固定回應的路由,覆蓋預設行為。
    /// Registers a route with a fixed answer, overriding the default behaviour.
    /// </summary>
    /// <param name="method">HTTP 方法。The HTTP method.</param>
    /// <param name="path">絕對路徑。The absolute path.</param>
    /// <param name="status">狀態碼。The status code.</param>
    /// <param name="body">回應本文。The response body.</param>
    public void Map(string method, string path, int status, string body) =>
        Map(method, path, _ => new FakeResponse(status, body));

    /// <summary>
    /// 登記一個由函式決定回應的路由。
    /// Registers a route whose answer comes from a function.
    /// </summary>
    /// <param name="method">HTTP 方法。The HTTP method.</param>
    /// <param name="path">絕對路徑。The absolute path.</param>
    /// <param name="handler">依請求產生回應。Produces the answer from the request.</param>
    public void Map(string method, string path, Func<FakeRequest, FakeResponse> handler)
    {
        lock (_gate)
        {
            _routes[(method.ToUpperInvariant(), path)] = handler;
        }
    }

    /// <summary>
    /// 某個方法加路徑收到過幾次請求。
    /// How many requests a method and path received.
    /// </summary>
    /// <param name="method">HTTP 方法。The HTTP method.</param>
    /// <param name="path">絕對路徑。The absolute path.</param>
    /// <returns>次數。The count.</returns>
    public int Count(string method, string path) =>
        Requests.Count(r => string.Equals(r.Method, method, StringComparison.OrdinalIgnoreCase) && r.Path == path);

    /// <summary>
    /// 等到至少有 <paramref name="count"/> 條 SSE 連線累計接受過。
    /// Waits until at least <paramref name="count"/> SSE connections have been accepted in total.
    /// </summary>
    /// <param name="count">連線數。The number of connections.</param>
    /// <param name="timeout">等待上限。The longest to wait.</param>
    /// <returns>等到為 true。True when reached.</returns>
    public async Task<bool> WaitForConnectionsAsync(int count, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (SseConnectionTotal >= count)
            {
                return true;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return SseConnectionTotal >= count;
    }

    /// <summary>
    /// 對所有開著的連線發佈一個資料事件。<paramref name="code"/> 沒有對應的訂閱(<c>order_event</c> 則是沒訂閱委託回報)
    /// 時,改送一個心跳 —— 這是真 server 的行為。
    /// Publishes a data event to every open connection. When <paramref name="code"/> has no matching subscription
    /// (for <c>order_event</c>, when no order-report subscription exists), a heartbeat is sent instead, as the real
    /// server does.
    /// </summary>
    /// <param name="eventName">事件名。The event name.</param>
    /// <param name="data">資料(JSON 文字)。The data, as JSON text.</param>
    /// <param name="code">這個事件所屬的商品代碼;<see langword="null"/> 表示不檢查訂閱。The contract code the event belongs to, or <see langword="null"/> to skip the subscription check.</param>
    /// <returns>代表非同步作業的工作。A task for the asynchronous operation.</returns>
    public async Task PublishAsync(string eventName, string data, string? code = null)
    {
        bool allowed;

        lock (_gate)
        {
            allowed = eventName == "order_event"
                ? _tradeSubscribed
                : code is null || _subscribedCodes.Contains(code);
        }

        foreach (var connection in OpenConnections)
        {
            _ = allowed
                ? await connection.SendEventAsync(eventName, data).ConfigureAwait(false)
                : await connection.SendHeartbeatAsync(HeartbeatStyle).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 對所有開著的連線送一個心跳。
    /// Sends a heartbeat to every open connection.
    /// </summary>
    /// <returns>代表非同步作業的工作。A task for the asynchronous operation.</returns>
    public async Task PublishHeartbeatAsync()
    {
        foreach (var connection in OpenConnections)
        {
            _ = await connection.SendHeartbeatAsync(HeartbeatStyle).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 正常關閉所有 SSE 連線,模擬 server 端斷線。
    /// Gracefully closes every SSE connection, simulating a server-side drop.
    /// </summary>
    public void DropConnections()
    {
        foreach (var connection in OpenConnections)
        {
            connection.Close();
        }
    }

    /// <summary>
    /// 清掉所有行情與委託回報訂閱,模擬 server 重啟後訂閱消失。
    /// Clears every market-data and order-report subscription, simulating subscriptions lost across a server restart.
    /// </summary>
    public void ForgetSubscriptions()
    {
        lock (_gate)
        {
            _subscribedCodes.Clear();
            _tradeSubscribed = false;
        }
    }

    /// <summary>
    /// 停止伺服器並關閉所有連線。
    /// Stops the server and closes every connection.
    /// </summary>
    public void Dispose()
    {
        _stopping.Cancel();
        DropConnections();

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // 已經關閉。
            // Already closed.
        }

        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // 接受迴圈因為監聽器關閉而結束,不需要處理。
            // The accept loop ended because the listener closed; nothing to handle.
        }

        _stopping.Dispose();
    }

    /// <summary>
    /// 向系統要一個目前空著的 loopback port。
    /// Asks the system for a loopback port that is free right now.
    /// </summary>
    /// <returns>port 號碼。The port number.</returns>
    public static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        try
        {
            probe.Start();

            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private void MapDefaults()
    {
        Map("GET", "/api/v1/info", _ => new FakeResponse(200, """
            {"name":"shioaji","version":"1.7.10","description":"Shioaji HTTP server","protocols":["http","sse"],"scheme":"http","uds_plaintext":false,"simulation":true}
            """));

        Map("GET", "/api/v1/auth/accounts", _ => new FakeResponse(200, """
            [{"account_type":"S","person_id":"A123456789","broker_id":"9A95","account_id":"1234567","signed":true,"username":"TEST"},
             {"account_type":"F","person_id":"A123456789","broker_id":"F002000","account_id":"7654321","signed":true,"username":"TEST"}]
            """));

        Map("GET", "/api/v1/auth/usage", _ => new FakeResponse(200, """
            {"connections":1,"bytes":1024,"limit_bytes":2147483648,"remaining_bytes":2147482624}
            """));

        Map("GET", "/api/v1/stream/status", _ => new FakeResponse(200, """
            {"active_connections":1,"timestamp":1761800000.5,"status":"ok"}
            """));

        Map("POST", "/api/v1/auth/subscribe_trade", _ =>
        {
            lock (_gate)
            {
                _tradeSubscribed = true;
            }

            return new FakeResponse(200, "{}");
        });

        Map("POST", "/api/v1/auth/unsubscribe_trade", _ =>
        {
            lock (_gate)
            {
                _tradeSubscribed = false;
            }

            return new FakeResponse(200, "{}");
        });

        MapHistoryDefaults();

        Map("POST", "/api/v1/stream/subscribe", request =>
        {
            RegisterSubscription(request.Body, add: true);

            return new FakeResponse(200, """{"success":true,"message":"subscribed","subscription":{}}""");
        });

        Map("POST", "/api/v1/stream/unsubscribe", request =>
        {
            RegisterSubscription(request.Body, add: false);

            return new FakeResponse(200, """{"success":true,"message":"unsubscribed","subscription":{}}""");
        });
    }

    /// <summary>
    /// 歷史資料與查詢端點的預設回應,依 <c>docs/external/shioaji-http-api.md</c> §5。
    /// <c>kbars</c> 與 <c>ticks</c> 是<b>欄位陣列</b>(§5 寫明 <c>{datetime[], Open[], High[], Low[], Close[], Volume[], Amount[]}</c>
    /// 與 <c>datetime, close, volume, bid_price, bid_volume, ask_price, ask_volume, tick_type</c>);
    /// 其餘端點的回應形狀文件沒寫,用物件陣列(Python SDK 的回傳型別)。
    /// <c>daily_quotes</c> 的路徑與形狀文件都沒有,是本套件 0.2.0 的推定(見 <c>ShioajiApiPaths.DailyQuotes</c>)。
    /// 這裡只放最小的樣本;逐欄位的情境用 <c>Fixtures</c> 目錄的檔案透過 <see cref="Map(string, string, int, string)"/> 覆蓋。
    /// The default answers of the historical-data and query endpoints, from <c>docs/external/shioaji-http-api.md</c>
    /// §5. <c>kbars</c> and <c>ticks</c> are <b>column arrays</b> (§5 spells out the columns); the other endpoints'
    /// shapes are not documented, so arrays of objects (the Python SDK's return types) are used. The path and shape
    /// of <c>daily_quotes</c> are in no document and are this package's 0.2.0 inference. Only minimal samples live
    /// here; scenario-level detail overrides them with the files in <c>Fixtures</c> through
    /// <see cref="Map(string, string, int, string)"/>.
    /// </summary>
    private void MapHistoryDefaults()
    {
        Map("POST", "/api/v1/data/kbars", _ => new FakeResponse(200, """
            {"datetime":["2026-10-29T09:00:00","2026-10-29T09:01:00"],"Open":[1000,1001.5],"High":[1002,1003],"Low":[999.5,1001],"Close":[1001.5,1002],"Volume":[120,85],"Amount":[120180000,85170000]}
            """));

        Map("POST", "/api/v1/data/ticks", _ => new FakeResponse(200, """
            {"datetime":["2026-10-29T09:00:00.123456","2026-10-29T09:00:01"],"close":[1000.5,1001],"volume":[3,1],"bid_price":[1000,1000.5],"bid_volume":[40,22],"ask_price":[1000.5,1001],"ask_volume":[18,30],"tick_type":[1,2]}
            """));

        Map("POST", "/api/v1/data/snapshots", _ => new FakeResponse(200, """
            [{"ts":"2026-10-30T13:30:00","code":"2330","exchange":"TSE","close":1002,"volume":2,"total_volume":2000}]
            """));

        Map("POST", "/api/v1/data/daily_quotes", _ => new FakeResponse(200, """
            [{"code":"2330","open":1000.5,"high":1005,"low":998.5,"close":1002,"volume":18000}]
            """));

        Map("POST", "/api/v1/data/scanner", _ => new FakeResponse(200, """
            [{"code":"2330","name":"TSMC","close":1002,"change_rate":0.15}]
            """));

        Map("POST", "/api/v1/data/short_stock_sources", _ => new FakeResponse(200, """
            [{"code":"2330","short_stock_source":1500}]
            """));

        Map("POST", "/api/v1/data/credit_enquire", _ => new FakeResponse(200, """
            [{"stock_id":"2330","margin_unit":12000,"short_unit":300}]
            """));

        Map("GET", "/api/v1/data/regulatory_punish", _ => new FakeResponse(200, """
            [{"code":"1234","name":"Sample A"}]
            """));

        Map("GET", "/api/v1/data/regulatory_notice", _ => new FakeResponse(200, """
            [{"code":"5678","name":"Sample B"}]
            """));
    }

    private void RegisterSubscription(string body, bool add)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            // 連續月沒帶 target_code 時 server 一樣回 200,只是之後收不到資料:這裡只登記 target_code,
            // 沒有 target_code 的連續月就不登記任何代碼,Publish 因此只會送心跳。(§4)
            // A continuous-month subscription without target_code still gets a 200 but later receives no data:
            // only the target_code is registered then, so Publish sends heartbeats only. (§4)
            var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            var target = root.TryGetProperty("target_code", out var targetElement) ? targetElement.GetString() : null;
            var continuous = code is not null && (code.EndsWith("R1", StringComparison.OrdinalIgnoreCase) || code.EndsWith("R2", StringComparison.OrdinalIgnoreCase));

            lock (_gate)
            {
                foreach (var candidate in new[] { continuous ? null : code, target })
                {
                    if (string.IsNullOrEmpty(candidate))
                    {
                        continue;
                    }

                    _ = add ? _subscribedCodes.Add(candidate) : _subscribedCodes.Remove(candidate);
                }
            }
        }
        catch (JsonException)
        {
            // 本文不是 JSON:什麼都不登記。
            // The body is not JSON: nothing is registered.
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var request = await ReadRequestAsync(context.Request).ConfigureAwait(false);

            lock (_gate)
            {
                _requests.Add(request);
            }

            if (_bearer is not null
                && !(request.Headers.TryGetValue("Authorization", out var authorization)
                     && authorization == "Bearer " + _bearer))
            {
                await WriteAsync(context.Response, new FakeResponse(401, """{"detail":"Unauthorized"}""")).ConfigureAwait(false);
                return;
            }

            if (request.Method == "GET" && request.Path == "/api/v1/stream/data")
            {
                await ServeSseAsync(context).ConfigureAwait(false);
                return;
            }

            Func<FakeRequest, FakeResponse>? handler;

            lock (_gate)
            {
                _routes.TryGetValue((request.Method, request.Path), out handler);
            }

            await WriteAsync(
                context.Response,
                handler is null ? new FakeResponse(404, """{"detail":"Not Found"}""") : handler(request)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpListenerException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // 用戶端中途斷線(例如測試取消了請求)不是替身的錯誤。
            // A client disconnecting midway, such as a test cancelling the request, is not the fake's fault.
        }
    }

    private async Task ServeSseAsync(HttpListenerContext context)
    {
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;

        var connection = new FakeSseConnection(response);

        // 先登記連線、再寫開場註解:用戶端一收到標頭就可能去問 OpenConnections,那時它必須已經在清單裡。
        // The connection is registered before the opening comment is written: the client may ask OpenConnections
        // the moment it sees the headers, and the connection must already be listed.
        lock (_gate)
        {
            _connections.Add(connection);
        }

        Interlocked.Increment(ref _connectionTotal);

        // 開場寫一行註解,讓標頭立刻送出(HttpListener 在第一次寫入才送標頭),用戶端的 ResponseHeadersRead 才會回來。
        // 真 server 是否也這樣開場未知,這只是替身的送標頭手段。
        // An opening comment line makes the headers go out at once (HttpListener sends them on the first write) so
        // the client's ResponseHeadersRead returns. Whether the real server opens this way is unknown; this is
        // merely the fake's way of flushing headers.
        _ = await connection.WriteRawAsync(": open\n\n").ConfigureAwait(false);

        // 連線由發佈端或 Close / Abort 結束;這裡只要等到它關閉或伺服器停止。
        // The connection ends through the publisher or Close / Abort; this only waits for that or for the stop.
        while (!connection.IsClosed && !_stopping.IsCancellationRequested)
        {
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    private static async Task<FakeRequest> ReadRequestAsync(HttpListenerRequest request)
    {
        string body;

        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            body = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in request.Headers.AllKeys)
        {
            if (key is not null)
            {
                headers[key] = request.Headers[key] ?? string.Empty;
            }
        }

        return new FakeRequest(
            request.HttpMethod.ToUpperInvariant(),
            Uri.UnescapeDataString(request.Url!.AbsolutePath),
            request.Url.Query,
            body,
            headers);
    }

    private static async Task WriteAsync(HttpListenerResponse response, FakeResponse fake)
    {
        var bytes = Encoding.UTF8.GetBytes(fake.Body);
        response.StatusCode = fake.Status;
        response.ContentType = fake.ContentType;

        // 分塊傳輸時沒有 Content-Length,用戶端只能數實際讀到的位元組(流量計的第二條路徑)。
        // Chunked transfer has no Content-Length, so the client can only count the bytes it actually read (the
        // second path of the traffic meter).
        if (fake.Chunked)
        {
            response.SendChunked = true;
        }
        else
        {
            response.ContentLength64 = bytes.Length;
        }

        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        response.Close();
    }
}
