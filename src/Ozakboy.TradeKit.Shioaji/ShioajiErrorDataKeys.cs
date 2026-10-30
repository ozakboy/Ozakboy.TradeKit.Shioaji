namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// <see cref="Error.Data"/> 裡本套件使用的鍵名。與錯誤碼一樣是公開契約。
/// The keys this package uses in <see cref="Error.Data"/>. Like the error codes they are part of the public
/// contract.
/// </summary>
public static class ShioajiErrorDataKeys
{
    /// <summary>
    /// HTTP 狀態碼(數值字串)。
    /// The HTTP status code, as a numeric string.
    /// </summary>
    public const string StatusCode = "statusCode";

    /// <summary>
    /// 伺服器回應本文的前 2 KB,已經過遮罩器處理。
    /// The first 2 KB of the response body, already passed through the masker.
    /// </summary>
    public const string Body = "body";

    /// <summary>
    /// 請求的端點路徑。
    /// The endpoint path of the request.
    /// </summary>
    public const string Path = "path";

    /// <summary>
    /// 管線原本回報的錯誤碼(<c>http.network</c> 這類),在本套件把它換成 <c>shioaji.*</c> 之後留作診斷。
    /// The error code the pipeline originally reported (such as <c>http.network</c>), kept for diagnosis after
    /// this package replaced it with a <c>shioaji.*</c> code.
    /// </summary>
    public const string PipelineCode = "pipelineCode";

    /// <summary>
    /// 為 <c>true</c> 時代表這個下單類請求「結果未知」:請求可能已經被券商收下,呼叫端必須查委託,不可直接重送。
    /// 逾時與 5xx 會標記;連線被拒(請求確定沒到達)不會標記。
    /// <c>true</c> means this order-type request has an unknown outcome: the broker may have accepted it, and the
    /// caller must look it up instead of resending. Timeouts and 5xx answers are flagged; a refused connection,
    /// where the request certainly never arrived, is not.
    /// </summary>
    public const string OutcomeUnknown = "outcomeUnknown";

    /// <summary>
    /// 出問題的欄位名稱。
    /// The name of the offending field.
    /// </summary>
    public const string Field = "field";

    /// <summary>
    /// 串流事件的名稱(<c>event:</c> 那一行)。
    /// The name of a stream event (the <c>event:</c> line).
    /// </summary>
    public const string EventName = "eventName";

    /// <summary>
    /// 一行說明文字(已剝色碼、已遮罩),例如認證失敗時 server 印出的那一行。
    /// A line of explanatory text, stripped of colour codes and masked, such as the line a server prints when
    /// authentication fails.
    /// </summary>
    public const string Detail = "detail";

    /// <summary>
    /// 行程結束碼。
    /// The process exit code.
    /// </summary>
    public const string ExitCode = "exitCode";

    /// <summary>
    /// 行程最後幾行輸出(已遮罩,換行分隔)。
    /// The last lines of process output, masked and newline separated.
    /// </summary>
    public const string LastOutput = "lastOutput";
}
