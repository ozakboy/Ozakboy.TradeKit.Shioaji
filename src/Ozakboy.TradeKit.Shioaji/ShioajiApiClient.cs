using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Ozakboy.Http;
using Ozakboy.Http.Retry;
using Ozakboy.Security.Masking;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 低階用戶端:送出 JSON 請求、把失敗收斂成 <see cref="Result{T}"/>。各區塊的 client 都建構在它之上。
/// The low-level client: sends JSON requests and folds failures into <see cref="Result{T}"/>. Every area client
/// is built on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>重試規則只有一條:GET 可重試,POST 永不重試。</b>下單、改價、改量、撤單、訂閱都是 POST;
/// 重送一張下單請求的後果是多一個部位,所以 POST 除了被標成非冪等,還額外釘上 <see cref="RetryPolicy.NoRetry"/>
/// —— 兩道防線,萬一有人把管線的預設策略換成「什麼都重試」,下單仍然只會送一次。
/// <b>There is exactly one retry rule: GET may be retried and POST never is.</b> Orders, price and quantity
/// changes, cancellations and subscriptions are all POST; repeating an order request means an extra position, so
/// a POST is not only marked non-idempotent but also pinned to <see cref="RetryPolicy.NoRetry"/>. Two guards:
/// should someone swap the pipeline's default policy for one that retries everything, an order still goes out
/// once.
/// </para>
/// <para>
/// <b>錯誤本文不帶祕密。</b>非 2xx 的回應本文先經過遮罩器、再截斷到 2 KB,才放進 <see cref="Error.Data"/>。
/// 先遮罩再截斷,祕密才不會剛好被截在邊界上、留下一半在輸出裡。
/// <b>Error bodies carry no secret.</b> The body of a non-2xx answer goes through the masker and is then
/// truncated to 2 KB before it lands in <see cref="Error.Data"/>. Masking first means a secret cannot be cut
/// at the boundary and leave half of itself in the output.
/// </para>
/// </remarks>
public sealed class ShioajiApiClient
{
    private const int MaxBodyCharacters = 2048;

    private const int MaxDetailCharacters = 200;

    private static readonly string[] DetailFieldNames = ["detail", "message", "error", "msg"];

    private readonly HttpPipelineClient _http;
    private readonly SecretMasker _masker;
    private readonly ShioajiTrafficMeter? _trafficMeter;

    /// <summary>
    /// 建立低階用戶端。
    /// Creates the low-level client.
    /// </summary>
    /// <param name="http">已組好管線的門面。The facade with the pipeline assembled.</param>
    /// <param name="masker">
    /// 遮罩器,請傳入具名用戶端的那一個(<c>provider.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName)</c>),
    /// 否則只認得 <see cref="SecretMasker.Default"/> 上的祕密。<see langword="null"/> 時用預設。
    /// The masker; pass the named client's own, or only secrets on <see cref="SecretMasker.Default"/> are
    /// recognised. <see cref="SecretMasker.Default"/> when <see langword="null"/>.
    /// </param>
    /// <param name="trafficMeter">
    /// 流量計;每個收到的回應(含非 2xx)的本文位元組都會記進去。<see langword="null"/> 表示不計流量。
    /// The traffic meter; the body bytes of every response received, non-2xx included, are recorded in it.
    /// <see langword="null"/> measures no traffic.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="http"/> 為 <see langword="null"/> 時擲出。Thrown when <paramref name="http"/> is <see langword="null"/>.
    /// </exception>
    public ShioajiApiClient(HttpPipelineClient http, SecretMasker? masker = null, ShioajiTrafficMeter? trafficMeter = null)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
        _masker = masker ?? SecretMasker.Default;
        _trafficMeter = trafficMeter;
    }

    /// <summary>
    /// 送出 GET 並把回應本文反序列化成 <typeparamref name="T"/>。
    /// Sends a GET and deserializes the response body into <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">
    /// 目標型別。屬性名以 snake_case 對映,數字可以來自 JSON 數字或字串,<see cref="decimal"/> 由原始文字讀入。
    /// The target type. Property names map as snake_case, numbers may come from JSON numbers or strings, and a
    /// <see cref="decimal"/> is read from raw text.
    /// </typeparam>
    /// <param name="path">端點路徑,例如 <c>/api/v1/info</c>。The endpoint path, such as <c>/api/v1/info</c>.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>反序列化的結果,或已對映的失敗。The deserialized value, or a mapped failure.</returns>
    public async Task<Result<T>> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var body = await GetStringAsync(path, cancellationToken).ConfigureAwait(false);

        return body.IsFailure ? body.ToFailure<T>() : Deserialize<T>(body.GetValueOrThrow(), path);
    }

    /// <summary>
    /// 送出 POST(JSON 本文)並把回應本文反序列化成 <typeparamref name="T"/>。<b>永不重試。</b>
    /// Sends a POST with a JSON body and deserializes the response into <typeparamref name="T"/>. <b>Never
    /// retried.</b>
    /// </summary>
    /// <typeparam name="T">目標型別。The target type.</typeparam>
    /// <param name="path">端點路徑。The endpoint path.</param>
    /// <param name="body">
    /// 請求本文,以 snake_case 序列化並略過 <see langword="null"/> 屬性;<see langword="null"/> 表示不帶本文。
    /// The request body, serialized as snake_case with <see langword="null"/> properties skipped;
    /// <see langword="null"/> sends no body.
    /// </param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>反序列化的結果,或已對映的失敗。The deserialized value, or a mapped failure.</returns>
    public async Task<Result<T>> PostAsync<T>(string path, object? body, CancellationToken cancellationToken = default)
    {
        var response = await PostStringAsync(path, Serialize(body), cancellationToken).ConfigureAwait(false);

        return response.IsFailure ? response.ToFailure<T>() : Deserialize<T>(response.GetValueOrThrow(), path);
    }

    /// <summary>
    /// 送出 POST(JSON 本文),2xx 即成功,不讀回應。<b>永不重試。</b>
    /// Sends a POST with a JSON body; any 2xx is success and the response is not read. <b>Never retried.</b>
    /// </summary>
    /// <param name="path">端點路徑。The endpoint path.</param>
    /// <param name="body">請求本文;<see langword="null"/> 表示不帶本文。The request body, or <see langword="null"/> for none.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>成功,或已對映的失敗。Success, or a mapped failure.</returns>
    public async Task<Result> PostAsync(string path, object? body, CancellationToken cancellationToken = default)
    {
        var response = await PostStringAsync(path, Serialize(body), cancellationToken).ConfigureAwait(false);

        return response.IsFailure ? response.ToFailure<string>().ToResult() : Result.Success();
    }

    /// <summary>
    /// 送出 GET,回傳原始回應本文。
    /// Sends a GET and returns the raw response body.
    /// </summary>
    internal Task<Result<string>> GetStringAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, jsonBody: null, allowRetry: true, cancellationToken);

    /// <summary>
    /// 送出 GET,<b>不重試</b>,回傳原始回應本文。給「探測 server 起來了沒」用:探測本來就預期會失敗,
    /// 重試只會把一次探測拖成好幾次逾時。
    /// Sends a GET <b>without retrying</b> and returns the raw response body. It is for probing whether the server
    /// is up: a probe is expected to fail sometimes, and retrying only turns one probe into several timeouts.
    /// </summary>
    internal Task<Result<string>> GetStringOnceAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, jsonBody: null, allowRetry: false, cancellationToken);

    /// <summary>
    /// 送出 POST,回傳原始回應本文。<paramref name="jsonBody"/> 是已經序列化好的 JSON 文字。
    /// Sends a POST and returns the raw response body. <paramref name="jsonBody"/> is already-serialized JSON
    /// text.
    /// </summary>
    internal Task<Result<string>> PostStringAsync(string path, string? jsonBody, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, jsonBody, allowRetry: false, cancellationToken);

    /// <summary>
    /// 送出 GET,回傳原始回應本文與它的位元組數。歷史資料的回應要帶著大小回給呼叫端,讓它知道這一筆吃了多少流量。
    /// Sends a GET and returns the raw response body with its byte count. A historical-data answer carries its size
    /// back to the caller so it knows how much traffic that one cost.
    /// </summary>
    internal Task<Result<ShioajiResponseBody>> GetBodyAsync(string path, CancellationToken cancellationToken) =>
        SendBodyAsync(HttpMethod.Get, path, jsonBody: null, allowRetry: true, cancellationToken);

    /// <summary>
    /// 送出 POST(<b>永不重試</b>),回傳原始回應本文與它的位元組數。
    /// Sends a POST (<b>never retried</b>) and returns the raw response body with its byte count.
    /// </summary>
    internal Task<Result<ShioajiResponseBody>> PostBodyAsync(string path, string? jsonBody, CancellationToken cancellationToken) =>
        SendBodyAsync(HttpMethod.Post, path, jsonBody, allowRetry: false, cancellationToken);

    private static string? Serialize(object? body) =>
        body is null ? null : JsonSerializer.Serialize(body, ShioajiJson.Options);

    private static Result<T> Deserialize<T>(string body, string path)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return ShioajiErrors.MalformedResponse(
                $"{path} 的回應是空的。The response for {path} is empty.");
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(body, ShioajiJson.Options);

            return value is null
                ? ShioajiErrors.MalformedResponse($"{path} 的回應是 JSON null。The response for {path} is JSON null.")
                : value;
        }
        catch (JsonException exception)
        {
            return ShioajiErrors.MalformedResponse(
                $"{path} 的回應無法對映成 {typeof(T).Name}({exception.Message})。The response for {path} cannot be mapped to {typeof(T).Name} ({exception.Message}).");
        }
    }

    private async Task<Result<string>> SendAsync(
        HttpMethod method,
        string path,
        string? jsonBody,
        bool allowRetry,
        CancellationToken cancellationToken)
    {
        var body = await SendBodyAsync(method, path, jsonBody, allowRetry, cancellationToken).ConfigureAwait(false);

        return body.Map(static value => value.Text);
    }

    private async Task<Result<ShioajiResponseBody>> SendBodyAsync(
        HttpMethod method,
        string path,
        string? jsonBody,
        bool allowRetry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ShioajiErrors.BlankArgument(nameof(path));
        }

        // 開頭的斜線拿掉,讓請求位址相對於 BaseAddress 解析:基底位址若有路徑前綴(例如反向代理),前綴才不會被吃掉。
        // The leading slash is dropped so the request resolves relative to BaseAddress: a path prefix on the base
        // (a reverse proxy, say) then survives.
        var relative = path.TrimStart('/');

        // 請求必須在送出完成之後才釋放,所以這裡一定要 await。
        // The request may only be disposed once the send has completed, so this must await.
        using var request = new HttpRequestMessage(method, new Uri(relative, UriKind.Relative));

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // 只有 GET 而且呼叫端允許時才可重試;其他一律非冪等並釘上不重試的策略。
        // Only a GET the caller allows may be retried; everything else is non-idempotent and pinned to no retry.
        if (method == HttpMethod.Get && allowRetry)
        {
            request.AsIdempotent();
        }
        else
        {
            request.AsNonIdempotent().WithRetryPolicy(RetryPolicy.NoRetry);
        }

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        var sent = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (sent.IsFailure)
        {
            return MapPipelineError(sent.Error, path);
        }

        using (var response = sent.GetValueOrThrow())
        {
            string text;

            try
            {
                text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return ShioajiErrors.ServerUnreachable(path, HttpErrorCodes.Network);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return ShioajiErrors.RequestCancelled(path);
            }

            // 流量是「收到多少」:Content-Length 優先,沒有(chunked)就用實際讀到的 UTF-8 位元組數。非 2xx 的回應也算。
            // Traffic is "how much arrived": Content-Length first, and the UTF-8 bytes actually read when there is
            // none (chunked). A non-2xx response counts too.
            var bytes = response.Content.Headers.ContentLength ?? Encoding.UTF8.GetByteCount(text);
            _trafficMeter?.Record(bytes);

            if (response.IsSuccessStatusCode)
            {
                return new ShioajiResponseBody(text, bytes);
            }

            var masked = _masker.MaskText(text) ?? string.Empty;
            var snippet = masked.Length > MaxBodyCharacters ? masked[..MaxBodyCharacters] : masked;

            return ShioajiErrors.HttpStatus(
                (int)response.StatusCode,
                response.ReasonPhrase,
                snippet,
                ExtractDetail(masked),
                path);
        }
    }

    private static Error MapPipelineError(Error error, string path) =>
        error.Code switch
        {
            HttpErrorCodes.Network => ShioajiErrors.ServerUnreachable(path, error.Code),
            HttpErrorCodes.Timeout or HttpErrorCodes.AttemptTimeout => ShioajiErrors.RequestTimeout(path, error.Code),
            HttpErrorCodes.Cancelled => ShioajiErrors.RequestCancelled(path),
            _ when error.Category == ErrorCategory.Cancelled => ShioajiErrors.RequestCancelled(path),
            _ => ShioajiErrors.RequestFailed(error, path),
        };

    /// <summary>
    /// 從錯誤本文挑一句人看得懂的說明。錯誤本文的結構還沒實測過,所以只認常見的欄位名,認不得就不放說明,
    /// 完整本文仍在 <see cref="ShioajiErrorDataKeys.Body"/>。
    /// Picks a human-readable sentence out of an error body. The shape of that body has not been measured, so only
    /// the usual field names are recognised; if none matches there is no sentence and the whole body is still in
    /// <see cref="ShioajiErrorDataKeys.Body"/>.
    /// </summary>
    private static string? ExtractDetail(string maskedBody)
    {
        if (string.IsNullOrWhiteSpace(maskedBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(maskedBody);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in DetailFieldNames)
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    var text = value.GetString()!;
                    return text.Length > MaxDetailCharacters ? text[..MaxDetailCharacters] : text;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
