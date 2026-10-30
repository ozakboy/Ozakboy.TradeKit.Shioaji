using System.Globalization;
using System.Net;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 連線 Shioaji 本機 HTTP server 所需的設定。
/// The settings required to talk to the Shioaji local HTTP server.
/// </summary>
/// <remarks>
/// <para>
/// <b>金鑰絕不落地。</b>這個型別不含任何預設憑證,也不會把憑證寫進檔案、日誌或 <see cref="object.ToString"/>。
/// <see cref="ApiKey"/> 與 <see cref="SecretKey"/> 只有在 <see cref="UseBearerAuth"/> 為 <see langword="true"/> 時才用到
/// (server 以 <c>--auth</c> 啟動時);它們由宿主從環境變數或安全設定來源注入。
/// <b>Credentials never land on disk.</b> This type carries no default credentials and writes none to files,
/// logs, or <see cref="object.ToString"/>. <see cref="ApiKey"/> and <see cref="SecretKey"/> matter only when
/// <see cref="UseBearerAuth"/> is <see langword="true"/> (the server was started with <c>--auth</c>) and are
/// injected by the host from environment variables or a secure configuration source.
/// </para>
/// <para>
/// 登入券商用的憑證是另一回事:那由 <see cref="Server.ShioajiServerSupervisor"/> 在啟動子行程的當下才取得,
/// 不經過這個型別。
/// The credentials for logging in to the broker are a different matter: <see cref="Server.ShioajiServerSupervisor"/>
/// obtains them at the instant it starts the child process, and they never pass through this type.
/// </para>
/// </remarks>
public sealed class ShioajiOptions
{
    /// <summary>
    /// server 的基底位址,預設 <c>http://127.0.0.1:8080</c>。
    /// The base address of the server; <c>http://127.0.0.1:8080</c> by default.
    /// </summary>
    public Uri BaseUri { get; set; } = new(ShioajiConstants.DefaultBaseUri);

    /// <summary>
    /// API 金鑰(選填)。只有 <see cref="UseBearerAuth"/> 為 <see langword="true"/> 時才用得到。
    /// The API key, optional; used only when <see cref="UseBearerAuth"/> is <see langword="true"/>.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// API 密鑰(選填)。只有 <see cref="UseBearerAuth"/> 為 <see langword="true"/> 時才用得到。
    /// The API secret, optional; used only when <see cref="UseBearerAuth"/> is <see langword="true"/>.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// 是否在每個請求帶 <c>Authorization: Bearer &lt;ApiKey&gt;:&lt;SecretKey&gt;</c>。
    /// 對應 server 的 <c>--auth</c> / <c>SJ_HTTP_AUTH</c>。預設 <see langword="false"/>。
    /// Whether every request carries <c>Authorization: Bearer &lt;ApiKey&gt;:&lt;SecretKey&gt;</c>, matching the
    /// server's <c>--auth</c> / <c>SJ_HTTP_AUTH</c>. <see langword="false"/> by default.
    /// </summary>
    public bool UseBearerAuth { get; set; }

    /// <summary>
    /// 單一請求(含重試)的逾時,預設 30 秒。
    /// The timeout of one request, retries included; 30 seconds by default.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 串流閒置逾時,預設 45 秒:這麼久沒收到任何事件(含心跳)就視為斷線並重連。
    /// The stream idle timeout; 45 seconds by default. That long without any event, heartbeats included, counts
    /// as a dropped connection and triggers a reconnection.
    /// </summary>
    public TimeSpan StreamIdleTimeout { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// 串流重連的退避設定。
    /// The stream reconnection backoff.
    /// </summary>
    public ShioajiReconnectBackoff ReconnectBackoff { get; } = new();

    /// <summary>
    /// 串流佇列的容量,預設 10,000。滿了丟棄最舊的並計數,不阻塞讀取。
    /// The stream queue capacity; 10,000 by default. When full the oldest is dropped and counted, and reading
    /// never blocks.
    /// </summary>
    public int StreamChannelCapacity { get; set; } = 10_000;

    /// <summary>
    /// 本地限流預算的設定。
    /// The local rate budget settings.
    /// </summary>
    public ShioajiRateBudgetOptions RateBudget { get; } = new();

    /// <summary>
    /// 全市場日 OHLCV 的端點路徑,預設 <see cref="ShioajiApiPaths.DailyQuotes"/>。<b>預設路徑未驗證,待實機 OpenAPI 確認</b>
    /// (文件只提到命令列的 <c>shioaji data daily-quotes</c>);對不上時用這個屬性改成 server <c>/docs</c> 上實際的路徑。
    /// The endpoint path of the all-market daily OHLCV; <see cref="ShioajiApiPaths.DailyQuotes"/> by default.
    /// <b>The default is unverified and awaits confirmation against a real server's OpenAPI</b> (the documentation
    /// mentions only the command line's <c>shioaji data daily-quotes</c>); when it does not match, set the real path
    /// from the server's <c>/docs</c> here.
    /// </summary>
    public string DailyQuotesPath { get; set; } = ShioajiApiPaths.DailyQuotes;

    /// <summary>
    /// 是否「同時有金鑰與密鑰」。
    /// Whether both the key and the secret are present.
    /// </summary>
    internal bool HasCredentials =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SecretKey);

    /// <summary>
    /// 要送出的 <c>Authorization</c> 標頭值;不需要時為 <see langword="null"/>。
    /// The <c>Authorization</c> header value to send, or <see langword="null"/> when none is needed.
    /// </summary>
    internal string? BearerToken =>
        UseBearerAuth && HasCredentials ? string.Concat(ApiKey, ":", SecretKey) : null;

    /// <summary>
    /// 檢查設定是否可用。
    /// Checks that the settings are usable.
    /// </summary>
    /// <returns>可用時為成功,否則為第一個不可用項目的失敗。Success when usable, otherwise the first problem.</returns>
    public Result Validate()
    {
        if (BaseUri is null || !BaseUri.IsAbsoluteUri
            || (BaseUri.Scheme != Uri.UriSchemeHttp && BaseUri.Scheme != Uri.UriSchemeHttps))
        {
            return ShioajiErrors.InvalidOptions("BaseUri 必須是絕對的 http 或 https 位址。BaseUri must be an absolute http or https address.");
        }

        if (UseBearerAuth && !HasCredentials)
        {
            return ShioajiErrors.InvalidOptions("UseBearerAuth 為 true 時必須同時設定 ApiKey 與 SecretKey。ApiKey and SecretKey must both be set when UseBearerAuth is true.");
        }

        // 把 Bearer 金鑰用明文 HTTP 送到非本機位址,等於在網路上公開金鑰。本機 loopback 不經過網路,不在此限。
        // Sending the bearer credential over plain HTTP to a non-local address publishes it on the network.
        // Loopback never leaves the machine and is exempt.
        if (UseBearerAuth && BaseUri.Scheme == Uri.UriSchemeHttp && !IsLoopback(BaseUri))
        {
            return ShioajiErrors.InvalidOptions("不可用明文 http 把 Bearer 憑證送到非本機位址,請改用 https。The bearer credential must not travel over plain http to a non-local address; use https.");
        }

        if (RequestTimeout <= TimeSpan.Zero)
        {
            return ShioajiErrors.InvalidOptions("RequestTimeout 必須為正值。RequestTimeout must be positive.");
        }

        if (StreamIdleTimeout <= TimeSpan.Zero)
        {
            return ShioajiErrors.InvalidOptions("StreamIdleTimeout 必須為正值。StreamIdleTimeout must be positive.");
        }

        if (StreamChannelCapacity < 1)
        {
            return ShioajiErrors.InvalidOptions("StreamChannelCapacity 必須至少為 1。StreamChannelCapacity must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(DailyQuotesPath) || !DailyQuotesPath.StartsWith('/'))
        {
            return ShioajiErrors.InvalidOptions("DailyQuotesPath 必須是以 / 開頭的路徑。DailyQuotesPath must be a path starting with /.");
        }

        return ReconnectBackoff.Validate().Then(RateBudget.Validate);
    }

    private static bool IsLoopback(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>
    /// 不洩漏憑證的字串表示。
    /// A string representation that leaks no credential.
    /// </summary>
    /// <returns>只含位址與逾時的描述。A description of the address and timeouts only.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"ShioajiOptions {{ BaseUri = {BaseUri}, UseBearerAuth = {UseBearerAuth}, RequestTimeout = {RequestTimeout} }}");
}
