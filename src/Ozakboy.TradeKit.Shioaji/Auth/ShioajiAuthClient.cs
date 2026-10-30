using System.Text.Json;

namespace Ozakboy.TradeKit.Shioaji.Auth;

/// <summary>
/// 認證與帳號相關的端點:server 資訊、帳號、流量、串流狀態,以及委託回報訂閱。
/// The authentication and account endpoints: server info, accounts, usage, stream status, and the order-report
/// subscription.
/// </summary>
/// <remarks>
/// <para>
/// 登入與登出沒有 HTTP 端點 —— 它們在 <c>shioaji server start</c> 與 <c>stop</c> 時完成,見
/// <see cref="Server.ShioajiServerSupervisor"/>。
/// Logging in and out have no HTTP endpoint: they happen when <c>shioaji server start</c> and <c>stop</c> run;
/// see <see cref="Server.ShioajiServerSupervisor"/>.
/// </para>
/// <para>
/// <b>證券與期貨要各訂閱一次委託回報。</b>沒訂閱時,串流上的 <c>order_event</c> 只有心跳,
/// 下的單不會有任何回報 —— 而且不會有任何錯誤。
/// <b>Securities and futures each need their own order-report subscription.</b> Without it the <c>order_event</c>
/// on the stream carries only heartbeats, so a placed order produces no report — and no error.
/// </para>
/// <para>
/// 預算:<see cref="GetUsageAsync"/> 計入 <see cref="ShioajiBudgetKind.Portfolio"/>(保守起見,它可能會問到券商);
/// 其餘端點是 server 本機狀態或訂閱登記,不計入任何預算。
/// Budget: <see cref="GetUsageAsync"/> counts against <see cref="ShioajiBudgetKind.Portfolio"/> (conservatively,
/// since it may ask the broker); the other endpoints are local server state or subscription bookkeeping and
/// count against nothing.
/// </para>
/// </remarks>
public sealed class ShioajiAuthClient
{
    private readonly ShioajiApiClient _api;
    private readonly ShioajiRateBudget _budget;

    /// <summary>
    /// 建立用戶端。
    /// Creates the client.
    /// </summary>
    /// <param name="api">低階用戶端。The low-level client.</param>
    /// <param name="budget">本地限流預算。The local rate budget.</param>
    /// <exception cref="ArgumentNullException">
    /// 任一參數為 <see langword="null"/> 時擲出。Thrown when an argument is <see langword="null"/>.
    /// </exception>
    public ShioajiAuthClient(ShioajiApiClient api, ShioajiRateBudget budget)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(budget);

        _api = api;
        _budget = budget;
    }

    /// <summary>
    /// 取得 server 的身分與模式(<c>GET /api/v1/info</c>)。
    /// Gets the server's identity and mode (<c>GET /api/v1/info</c>).
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>server 資訊,或已對映的失敗。The server info, or a mapped failure.</returns>
    public async Task<Result<ShioajiServerInfo>> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/info";

        var body = await _api.GetStringAsync(path, cancellationToken).ConfigureAwait(false);

        return body.IsFailure ? body.ToFailure<ShioajiServerInfo>() : ParseInfo(body.GetValueOrThrow());
    }

    /// <summary>
    /// 探測 server 是否已經在回應:一次 <c>GET /api/v1/info</c>,<b>不重試</b>,並以較短的時限放棄。
    /// 給 <see cref="Server.ShioajiServerSupervisor"/> 輪詢啟動狀態用 —— Windows 上連到沒人聽的 port 要將近兩秒才會被拒絕,
    /// 若走會重試的路徑,一次探測就會拖成六秒以上。
    /// Probes whether the server already answers: one <c>GET /api/v1/info</c>, <b>not retried</b>, abandoned after a
    /// short limit. It is for <see cref="Server.ShioajiServerSupervisor"/> polling the start-up: on Windows a
    /// connection to a port nobody listens on takes nearly two seconds to be refused, and the retrying path would
    /// stretch one probe to six seconds or more.
    /// </summary>
    internal async Task<Result<ShioajiServerInfo>> ProbeInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var limited = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limited.CancelAfter(timeout);

        var body = await _api.GetStringOnceAsync("/api/v1/info", limited.Token).ConfigureAwait(false);

        return body.IsFailure ? body.ToFailure<ShioajiServerInfo>() : ParseInfo(body.GetValueOrThrow());
    }

    /// <summary>
    /// 列出帳號(<c>GET /api/v1/auth/accounts</c>)。
    /// Lists the accounts (<c>GET /api/v1/auth/accounts</c>).
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>帳號清單,或已對映的失敗。The accounts, or a mapped failure.</returns>
    public async Task<Result<IReadOnlyList<ShioajiAccount>>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/auth/accounts";

        var body = await _api.GetStringAsync(path, cancellationToken).ConfigureAwait(false);

        return body.IsFailure ? body.ToFailure<IReadOnlyList<ShioajiAccount>>() : ParseAccounts(body.GetValueOrThrow());
    }

    /// <summary>
    /// 取得今日流量用量(<c>GET /api/v1/auth/usage</c>)。
    /// Gets today's traffic usage (<c>GET /api/v1/auth/usage</c>).
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>用量,或已對映的失敗。The usage, or a mapped failure.</returns>
    public async Task<Result<ShioajiUsage>> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/auth/usage";

        var permit = await _budget.AcquireAsync(ShioajiBudgetKind.Portfolio, cancellationToken).ConfigureAwait(false);

        if (permit.IsFailure)
        {
            return permit.Error;
        }

        var body = await _api.GetStringAsync(path, cancellationToken).ConfigureAwait(false);

        return body.IsFailure ? body.ToFailure<ShioajiUsage>() : ParseUsage(body.GetValueOrThrow());
    }

    /// <summary>
    /// 取得串流狀態(<c>GET /api/v1/stream/status</c>)。
    /// Gets the stream status (<c>GET /api/v1/stream/status</c>).
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>串流狀態,或已對映的失敗。The stream status, or a mapped failure.</returns>
    public async Task<Result<ShioajiStreamStatus>> GetStreamStatusAsync(CancellationToken cancellationToken = default)
    {
        const string path = "/api/v1/stream/status";

        var body = await _api.GetStringAsync(path, cancellationToken).ConfigureAwait(false);

        return body.IsFailure ? body.ToFailure<ShioajiStreamStatus>() : ParseStreamStatus(body.GetValueOrThrow());
    }

    /// <summary>
    /// 訂閱帳號的委託回報(<c>POST /api/v1/auth/subscribe_trade</c>)。證券與期貨要各訂一次。<b>永不重試。</b>
    /// Subscribes to an account's order reports (<c>POST /api/v1/auth/subscribe_trade</c>). Securities and
    /// futures need one each. <b>Never retried.</b>
    /// </summary>
    /// <param name="account">要訂閱的帳號;只用到券商代號、帳號與類型。The account; only its broker id, account id and type are used.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 2xx 即成功 —— 這個端點的回應格式尚未實測,所以不讀回應本文。
    /// Any 2xx is success; the response format of this endpoint has not been measured, so the body is not read.
    /// </returns>
    public Task<Result> SubscribeTradeAsync(ShioajiAccount account, CancellationToken cancellationToken = default) =>
        PostTradeSubscriptionAsync("/api/v1/auth/subscribe_trade", account, cancellationToken);

    /// <summary>
    /// 取消帳號的委託回報訂閱(<c>POST /api/v1/auth/unsubscribe_trade</c>)。<b>永不重試。</b>
    /// Cancels an account's order-report subscription (<c>POST /api/v1/auth/unsubscribe_trade</c>). <b>Never
    /// retried.</b>
    /// </summary>
    /// <param name="account">要取消的帳號。The account.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>2xx 即成功。Any 2xx is success.</returns>
    public Task<Result> UnsubscribeTradeAsync(ShioajiAccount account, CancellationToken cancellationToken = default) =>
        PostTradeSubscriptionAsync("/api/v1/auth/unsubscribe_trade", account, cancellationToken);

    private async Task<Result> PostTradeSubscriptionAsync(
        string path,
        ShioajiAccount account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (string.IsNullOrWhiteSpace(account.BrokerId))
        {
            return ShioajiErrors.BlankArgument(nameof(account.BrokerId));
        }

        if (string.IsNullOrWhiteSpace(account.AccountId))
        {
            return ShioajiErrors.BlankArgument(nameof(account.AccountId));
        }

        if (!ShioajiWire.AccountType.TryGetWire(account.AccountType, out var accountType)
            || account.AccountType == ShioajiAccountType.Foreign)
        {
            return ShioajiErrors.InvalidArgument(
                "委託回報只能訂閱證券(Stock)或期貨(Futures)帳號。Order reports can be subscribed for Stock or Futures accounts only.");
        }

        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("broker_id", account.BrokerId);
            writer.WriteString("account_id", account.AccountId);
            writer.WriteString("account_type", accountType);
            writer.WriteEndObject();
        });

        var response = await _api.PostStringAsync(path, json, cancellationToken).ConfigureAwait(false);

        return response.IsFailure ? response.ToResult() : Result.Success();
    }

    internal static Result<ShioajiServerInfo> ParseInfo(string body)
    {
        const string context = "/api/v1/info";

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<ShioajiServerInfo>();
        }

        using var document = parsed.GetValueOrThrow();

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return ShioajiErrors.MalformedResponse($"{context} 的回應不是 JSON 物件。The response for {context} is not a JSON object.");
        }

        var fields = new ShioajiFieldReader(document.RootElement, context);
        var name = fields.RequiredString("name");
        var version = fields.RequiredString("version");

        // simulation 缺漏時不可預設成任何一邊:預設 true 會讓正式環境被當成模擬,預設 false 反之。
        // A missing simulation must not default either way: true would pass production off as simulation, and
        // false would do the reverse.
        var simulation = fields.OptionalBool("simulation");

        if (simulation is null)
        {
            fields.Fail("simulation");
        }

        if (!fields.IsValid)
        {
            return fields.Error!;
        }

        return new ShioajiServerInfo
        {
            Name = name,
            Version = version,
            Simulation = simulation!.Value,
            Protocols = ReadStrings(fields, "protocols"),
        };
    }

    internal static Result<IReadOnlyList<ShioajiAccount>> ParseAccounts(string body)
    {
        const string context = "/api/v1/auth/accounts";

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<IReadOnlyList<ShioajiAccount>>();
        }

        using var document = parsed.GetValueOrThrow();

        var array = document.RootElement;

        // 容許被包在物件裡({ "accounts": [...] }):回應格式沒有官方範例。
        // A wrapping object ({ "accounts": [...] }) is tolerated, since there is no official sample.
        if (array.ValueKind == JsonValueKind.Object && array.TryGetProperty("accounts", out var inner))
        {
            array = inner;
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            return ShioajiErrors.MalformedResponse($"{context} 的回應不是陣列。The response for {context} is not an array.");
        }

        var accounts = new List<ShioajiAccount>(array.GetArrayLength());

        foreach (var item in array.EnumerateArray())
        {
            var fields = new ShioajiFieldReader(item, context);
            var type = ShioajiWire.AccountType.Parse(fields.RequiredString("account_type"));
            var brokerId = fields.RequiredString("broker_id");
            var accountId = fields.RequiredString("account_id");

            if (!fields.IsValid)
            {
                return fields.Error!;
            }

            accounts.Add(new ShioajiAccount
            {
                AccountType = type,
                PersonId = fields.OptionalString("person_id") ?? string.Empty,
                BrokerId = brokerId,
                AccountId = accountId,

                // 缺漏時視為未簽署:未簽署的帳號無法下單,把它誤當成已簽署才會出事。
                // Absent counts as unsigned: an unsigned account cannot trade, and mistaking it for signed is the
                // dangerous direction.
                IsSigned = fields.OptionalBool("signed") ?? false,
                Username = fields.OptionalString("username") ?? string.Empty,
            });
        }

        return accounts;
    }

    internal static Result<ShioajiUsage> ParseUsage(string body)
    {
        const string context = "/api/v1/auth/usage";

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<ShioajiUsage>();
        }

        using var document = parsed.GetValueOrThrow();

        var fields = new ShioajiFieldReader(document.RootElement, context);
        var usage = new ShioajiUsage
        {
            Connections = fields.RequiredInt64("connections"),
            Bytes = fields.RequiredInt64("bytes"),
            LimitBytes = fields.RequiredInt64("limit_bytes"),
            RemainingBytes = fields.RequiredInt64("remaining_bytes"),
        };

        return fields.IsValid ? usage : fields.Error!;
    }

    internal static Result<ShioajiStreamStatus> ParseStreamStatus(string body)
    {
        const string context = "/api/v1/stream/status";

        var parsed = ShioajiJson.Parse(body, context);

        if (parsed.IsFailure)
        {
            return parsed.ToFailure<ShioajiStreamStatus>();
        }

        using var document = parsed.GetValueOrThrow();

        var fields = new ShioajiFieldReader(document.RootElement, context);
        var active = fields.RequiredInt64("active_connections");

        DateTimeOffset? timestamp = null;

        if (fields.TryGet("timestamp", out var stamp) && ShioajiTimestamps.TryRead(stamp, out var read))
        {
            timestamp = read;
        }

        var status = new ShioajiStreamStatus
        {
            ActiveConnections = active,
            Timestamp = timestamp,
            Status = fields.OptionalString("status") ?? string.Empty,
        };

        return fields.IsValid ? status : fields.Error!;
    }

    private static List<string> ReadStrings(ShioajiFieldReader fields, string name)
    {
        var result = new List<string>();

        if (!fields.TryGet(name, out var value))
        {
            return result;
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (ShioajiFieldReader.TryReadString(item, out var text))
                {
                    result.Add(text);
                }
            }
        }
        else if (ShioajiFieldReader.TryReadString(value, out var single))
        {
            result.Add(single);
        }

        return result;
    }
}
