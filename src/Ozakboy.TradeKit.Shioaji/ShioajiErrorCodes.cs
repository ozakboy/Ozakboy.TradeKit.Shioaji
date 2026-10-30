namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 本套件回傳的所有錯誤碼。字串是公開契約,發佈後不再變更 —— 上層的重試、告警與畫面判斷都依它分支。
/// Every error code this package returns. The strings are part of the public contract and do not change once
/// published, because callers branch retry, alerting and display on them.
/// </summary>
public static class ShioajiErrorCodes
{
    /// <summary>
    /// HTTP 狀態碼錯誤的代碼前綴,完整代碼為 <c>shioaji.http.&lt;狀態碼&gt;</c>(例如 <c>shioaji.http.400</c>)。
    /// The prefix of HTTP-status errors; the full code is <c>shioaji.http.&lt;status&gt;</c>, for example
    /// <c>shioaji.http.400</c>.
    /// </summary>
    public const string HttpStatusPrefix = "shioaji.http.";

    /// <summary>
    /// 設定不合法。
    /// A setting is invalid.
    /// </summary>
    public const string InvalidOptions = "shioaji.options.invalid";

    /// <summary>
    /// 連不上本機 server(行程沒起來、位址或 port 不對)。暫時性。
    /// The local server cannot be reached (not running, wrong address or port). Transient.
    /// </summary>
    public const string ServerUnreachable = "shioaji.server_unreachable";

    /// <summary>
    /// 請求逾時。暫時性。對下單類請求,逾時代表「結果未知」,見 <see cref="ShioajiErrorDataKeys.OutcomeUnknown"/>。
    /// The request timed out. Transient. For an order request a timeout means the outcome is unknown; see
    /// <see cref="ShioajiErrorDataKeys.OutcomeUnknown"/>.
    /// </summary>
    public const string RequestTimeout = "shioaji.request_timeout";

    /// <summary>
    /// 呼叫端取消了請求。
    /// The caller cancelled the request.
    /// </summary>
    public const string RequestCancelled = "shioaji.request_cancelled";

    /// <summary>
    /// 管線回報了本套件沒有專屬代碼的失敗;原始代碼放在 <see cref="ShioajiErrorDataKeys.PipelineCode"/>。
    /// The pipeline reported a failure this package has no dedicated code for; the original code is in
    /// <see cref="ShioajiErrorDataKeys.PipelineCode"/>.
    /// </summary>
    public const string RequestFailed = "shioaji.request_failed";

    /// <summary>
    /// 本地的呼叫參數不合法(空白代碼、負數等),請求沒有送出。
    /// A local argument is invalid (blank code, negative number and so on); nothing was sent.
    /// </summary>
    public const string InvalidArgument = "shioaji.validation.invalid_argument";

    /// <summary>
    /// 回應是 2xx,但內容讀不懂(不是 JSON、形狀不對)。重試只會拿到同樣讀不懂的回應,所以不是暫時性。
    /// The response was 2xx but its content could not be read (not JSON, wrong shape). A retry would fetch the
    /// same unreadable response, so this is not transient.
    /// </summary>
    public const string MalformedResponse = "shioaji.response.malformed";

    /// <summary>
    /// 回應缺少必要欄位,或欄位值無法解析。
    /// The response lacks a required field or the field's value cannot be read.
    /// </summary>
    public const string MissingField = "shioaji.response.missing_field";

    /// <summary>
    /// 連續月合約(代碼以 R1 或 R2 結尾)的訂閱沒有 <c>target_code</c>。
    /// 伺服器會回 200 卻只送心跳,所以在本地就拒絕。
    /// A continuous-month futures subscription (code ending in R1 or R2) carries no <c>target_code</c>. The
    /// server answers 200 and then sends only heartbeats, so it is refused locally.
    /// </summary>
    public const string SubscriptionTargetCodeRequired = "shioaji.subscription.target_code_required";

    /// <summary>
    /// 指數只支援 <c>Quote</c> 訂閱。
    /// An index supports the <c>Quote</c> subscription only.
    /// </summary>
    public const string SubscriptionIndexRequiresQuote = "shioaji.subscription.index_requires_quote";

    /// <summary>
    /// 本地訂閱數已達上限(官方 200)。
    /// The local subscription count reached the limit (200 by the broker's rule).
    /// </summary>
    public const string SubscriptionLimitExceeded = "shioaji.subscription.limit_exceeded";

    /// <summary>
    /// 伺服器回 2xx 但 <c>success</c> 為 <see langword="false"/>。
    /// The server answered 2xx with <c>success</c> set to <see langword="false"/>.
    /// </summary>
    public const string SubscriptionRejected = "shioaji.subscription.rejected";

    /// <summary>
    /// 本地限流預算用盡,且等待會超過 <see cref="ShioajiRateBudgetOptions.MaxWait"/>。請求沒有送出。暫時性。
    /// The local rate budget is spent and waiting would exceed <see cref="ShioajiRateBudgetOptions.MaxWait"/>.
    /// Nothing was sent. Transient.
    /// </summary>
    public const string RateBudgetExhausted = "shioaji.rate_budget.exhausted";

    /// <summary>
    /// 委託的 <c>custom_field</c> 不合法(超過 6 字元或含非英數字元)。
    /// The order's <c>custom_field</c> is invalid (longer than 6 characters or not alphanumeric).
    /// </summary>
    public const string OrderInvalidCustomField = "shioaji.order.invalid_custom_field";

    /// <summary>
    /// 委託請求的其他欄位不合法(價格、數量、必填欄位、未定義的列舉值)。請求沒有送出。
    /// Another field of the order request is invalid (price, quantity, a required field, an undefined enum
    /// value). Nothing was sent.
    /// </summary>
    public const string OrderInvalidRequest = "shioaji.order.invalid_request";

    /// <summary>
    /// 下單類請求得到 2xx,但回應讀不懂 —— 單<b>可能已經成功送出</b>,呼叫端必須查委託而不是重送。
    /// An order request got 2xx but the response was unreadable — the order <b>may have been placed</b>, and the
    /// caller must look it up rather than send it again.
    /// </summary>
    public const string OrderResponseUnreadable = "shioaji.order.response_unreadable";

    /// <summary>
    /// 串流事件讀不懂(必要欄位缺漏、數值無法解析)。串流不會因此結束。
    /// A stream event could not be read (a required field is missing or a number is unreadable). The stream
    /// carries on.
    /// </summary>
    public const string StreamMalformedEvent = "shioaji.stream.malformed_event";

    /// <summary>
    /// 同一個 <c>ShioajiStreamReader</c> 已經有一個消費端在讀。
    /// The same <c>ShioajiStreamReader</c> already has a consumer reading.
    /// </summary>
    public const string StreamAlreadyReading = "shioaji.stream.already_reading";

    /// <summary>
    /// 找不到 <c>shioaji</c> 可執行檔。
    /// The <c>shioaji</c> executable cannot be found.
    /// </summary>
    public const string ServerExecutableNotFound = "shioaji.server.executable_not_found";

    /// <summary>
    /// 行程無法啟動(作業系統拒絕)。
    /// The process could not be started (the operating system refused).
    /// </summary>
    public const string ServerStartFailed = "shioaji.server.start_failed";

    /// <summary>
    /// 啟動後在 <see cref="Server.ShioajiServerOptions.StartupTimeout"/> 內 <c>/api/v1/info</c> 沒有回應,行程已被終止。
    /// 錯誤資料帶最後 20 行輸出(已遮罩)。
    /// <c>/api/v1/info</c> did not answer within <see cref="Server.ShioajiServerOptions.StartupTimeout"/> after
    /// the start; the process was killed. The error data carries the last 20 output lines, masked.
    /// </summary>
    public const string ServerStartTimeout = "shioaji.server.start_timeout";

    /// <summary>
    /// 行程在就緒之前就退出了。錯誤資料帶結束碼與最後 20 行輸出(已遮罩)。
    /// The process exited before it was ready. The error data carries the exit code and the last 20 output
    /// lines, masked.
    /// </summary>
    public const string ServerExitedEarly = "shioaji.server.exited_early";

    /// <summary>
    /// 已在執行的 server 的模擬 / 正式模式與設定的 <c>Production</c> 不一致。本套件不會終止別人啟動的 server。
    /// The simulation / production mode of a running server disagrees with the configured <c>Production</c>.
    /// This package never kills a server someone else started.
    /// </summary>
    public const string ServerModeMismatch = "shioaji.server.mode_mismatch";

    /// <summary>
    /// 目前的監督器狀態不允許這個操作(例如已經在啟動中)。
    /// The supervisor's current state does not allow this operation, for example it is already starting.
    /// </summary>
    public const string ServerInvalidState = "shioaji.server.invalid_state";

    /// <summary>
    /// 組出 <c>shioaji.http.&lt;狀態碼&gt;</c> 形式的代碼。
    /// Builds a code of the form <c>shioaji.http.&lt;status&gt;</c>.
    /// </summary>
    /// <param name="statusCode">HTTP 狀態碼。The HTTP status code.</param>
    /// <returns>對應的錯誤碼。The matching error code.</returns>
    public static string ForHttpStatus(int statusCode) =>
        string.Concat(HttpStatusPrefix, statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
