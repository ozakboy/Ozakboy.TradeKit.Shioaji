namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// Shioaji 本機 HTTP server 的協定常數:註冊用的 <see cref="HttpClient"/> 名稱、預設位址與官方限制。
/// Protocol constants for the Shioaji local HTTP server: the registered <see cref="HttpClient"/> names, the
/// default address, and the broker's published limits.
/// </summary>
public static class ShioajiConstants
{
    /// <summary>
    /// 註冊在 <c>IHttpClientFactory</c> 的請求用具名用戶端名稱(經 <c>Ozakboy.Http</c> 管線)。
    /// The named client registered with <c>IHttpClientFactory</c> for requests, going through the
    /// <c>Ozakboy.Http</c> pipeline.
    /// </summary>
    public const string HttpClientName = "Ozakboy.TradeKit.Shioaji";

    /// <summary>
    /// 註冊在 <c>IHttpClientFactory</c> 的串流用具名用戶端名稱。它<b>不</b>經過管線:
    /// 管線會把整個回應讀完才回傳,而 SSE 永遠讀不完。
    /// The named client registered for the stream. It deliberately does <b>not</b> go through the pipeline, which
    /// reads a response to the end before returning and an SSE response has no end.
    /// </summary>
    public const string StreamHttpClientName = "Ozakboy.TradeKit.Shioaji.Stream";

    /// <summary>
    /// <c>shioaji server start</c> 預設的基底位址。
    /// The base address <c>shioaji server start</c> binds by default.
    /// </summary>
    public const string DefaultBaseUri = "http://127.0.0.1:8080";

    /// <summary>
    /// <c>SJ_HTTP_ADDR</c> 的預設值。
    /// The default of <c>SJ_HTTP_ADDR</c>.
    /// </summary>
    public const string DefaultBindAddress = "127.0.0.1:8080";

    /// <summary>
    /// 單一帳戶最多同時訂閱的行情數(官方限制)。
    /// The most market-data subscriptions an account may hold at once (the broker's limit).
    /// </summary>
    public const int MaxSubscriptions = 200;

    /// <summary>
    /// 委託的 <c>custom_field</c> 最長字元數。
    /// The longest <c>custom_field</c> of an order, in characters.
    /// </summary>
    public const int MaxCustomFieldLength = 6;

    /// <summary>
    /// 1 分 K 單次查詢的最長天數(含頭尾兩天的日曆日數,官方限制 30 天)。
    /// The longest span of one 1-minute-bar query in calendar days, both ends included (the broker's limit is 30
    /// days).
    /// </summary>
    public const int MaxKbarRangeDays = 30;

    /// <summary>
    /// 單次快照查詢最多幾檔商品(官方限制 500)。
    /// The most contracts one snapshot query may carry (the broker's limit is 500).
    /// </summary>
    public const int MaxSnapshotContracts = 500;

    /// <summary>
    /// 排行掃描一次最多回傳幾筆(官方範圍 0 到 200)。
    /// The most rows one scanner query may ask for (the broker's range is 0 to 200).
    /// </summary>
    public const int MaxScannerCount = 200;

    /// <summary>
    /// 每日流量上限的重置時刻(台北時間的小時,08:00)。
    /// The hour, in Taipei time, at which the daily traffic allowance resets (08:00).
    /// </summary>
    public const int TrafficResetHour = 8;

    /// <summary>
    /// 台北時間相對於 UTC 的固定偏移(+08:00)。台灣自 1979 年起不實施日光節約時間,
    /// 所以沒有夏令時間的例外;Shioaji 的 <c>date</c>、<c>time</c>、<c>datetime</c> 都是這個時區的牆上時間。
    /// The fixed offset of Taipei time from UTC (+08:00). Taiwan has observed no daylight saving since 1979, so
    /// there is no seasonal exception; Shioaji's <c>date</c>, <c>time</c> and <c>datetime</c> are wall-clock times
    /// in this zone.
    /// </summary>
    public static readonly TimeSpan TaipeiUtcOffset = TimeSpan.FromHours(8);
}
