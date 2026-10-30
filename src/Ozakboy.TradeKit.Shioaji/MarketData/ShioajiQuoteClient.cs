using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.Contracts;

namespace Ozakboy.TradeKit.Shioaji.MarketData;

/// <summary>
/// 行情訂閱的端點。訂閱之後事件從 <see cref="Streaming.ShioajiStreamReader"/> 送出。
/// The market-data subscription endpoints. After subscribing, events arrive through
/// <see cref="Streaming.ShioajiStreamReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>本地先擋三種會「靜默失敗」的訂閱。</b>連續月合約沒有 <c>target_code</c>,server 回 200 卻只送心跳;
/// 指數訂閱 Tick 或 BidAsk,同樣不會有資料;訂閱數超過 200 會被券商拒絕。這三種都在請求送出之前以明確的
/// 錯誤碼拒絕,不佔用任何額度。
/// <b>Three silently failing subscriptions are refused locally.</b> A continuous-month contract without a
/// <c>target_code</c> earns a 200 and then only heartbeats; an index subscribed for Tick or BidAsk yields
/// nothing either; more than 200 subscriptions are refused by the broker. All three are rejected before the
/// request leaves, with a distinct code, and consume no budget.
/// </para>
/// <para>
/// <b>訂閱數的本地計數。</b>這個 client 追蹤自己訂了哪些,新的一筆才計入 <see cref="ShioajiRateBudget"/>,
/// 重複訂同一筆不重複計;請求失敗會把剛計入的還回去。斷線重連後 server 端的訂閱需要重放,
/// 呼叫 <see cref="ReplaySubscriptionsAsync"/> 即可,它不會重複計數。
/// <b>Counting subscriptions locally.</b> The client tracks what it has subscribed; only a new one is counted
/// against <see cref="ShioajiRateBudget"/>, a repeat is not, and a failed request returns the unit it just took.
/// After a reconnection the server-side subscriptions must be replayed, and <see cref="ReplaySubscriptionsAsync"/>
/// does that without counting twice.
/// </para>
/// <para>
/// 所有訂閱請求都是 POST,<b>永不重試</b>。
/// Every subscription request is a POST and is <b>never retried</b>.
/// </para>
/// </remarks>
public sealed class ShioajiQuoteClient
{
    private const string SubscribePath = "/api/v1/stream/subscribe";

    private const string UnsubscribePath = "/api/v1/stream/unsubscribe";

    private readonly ShioajiApiClient _api;
    private readonly ShioajiRateBudget _budget;
    private readonly Dictionary<string, ShioajiSubscription> _active = [];
    private readonly Lock _gate = new();

    /// <summary>
    /// 建立用戶端。
    /// Creates the client.
    /// </summary>
    /// <param name="api">低階用戶端。The low-level client.</param>
    /// <param name="budget">本地限流預算。The local rate budget.</param>
    /// <exception cref="ArgumentNullException">
    /// 任一參數為 <see langword="null"/> 時擲出。Thrown when an argument is <see langword="null"/>.
    /// </exception>
    public ShioajiQuoteClient(ShioajiApiClient api, ShioajiRateBudget budget)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(budget);

        _api = api;
        _budget = budget;
    }

    /// <summary>
    /// 目前這個 client 追蹤中的訂閱(快照)。
    /// A snapshot of the subscriptions this client is currently tracking.
    /// </summary>
    public IReadOnlyList<ShioajiSubscription> ActiveSubscriptions
    {
        get
        {
            lock (_gate)
            {
                return [.. _active.Values];
            }
        }
    }

    /// <summary>
    /// 訂閱行情(<c>POST /api/v1/stream/subscribe</c>)。
    /// Subscribes to market data (<c>POST /api/v1/stream/subscribe</c>).
    /// </summary>
    /// <param name="subscription">要訂閱的內容。What to subscribe to.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 成功,或失敗。失敗時本地計數不變。Success or failure; on failure the local count is unchanged.
    /// </returns>
    public async Task<Result> SubscribeAsync(ShioajiSubscription subscription, CancellationToken cancellationToken = default)
    {
        var invalid = Validate(subscription);

        if (invalid is not null)
        {
            return invalid;
        }

        var key = subscription.Key;
        bool isNew;

        lock (_gate)
        {
            isNew = !_active.ContainsKey(key);
        }

        if (isNew)
        {
            var permit = await _budget.AcquireAsync(ShioajiBudgetKind.Subscription, cancellationToken).ConfigureAwait(false);

            if (permit.IsFailure)
            {
                return permit;
            }

            lock (_gate)
            {
                // 在取得額度到登記之間,另一個呼叫可能已經登記了同一筆:那就把多拿的一個還回去。
                // Between taking the unit and registering, another call may have registered the same one; return
                // the extra unit then.
                if (!_active.TryAdd(key, subscription))
                {
                    _budget.ReleaseSubscription();
                    isNew = false;
                }
            }
        }

        var result = await PostAsync(SubscribePath, subscription, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure && isNew)
        {
            lock (_gate)
            {
                _active.Remove(key);
            }

            _budget.ReleaseSubscription();
        }

        return result;
    }

    /// <summary>
    /// 取消訂閱(<c>POST /api/v1/stream/unsubscribe</c>)。
    /// Cancels a subscription (<c>POST /api/v1/stream/unsubscribe</c>).
    /// </summary>
    /// <param name="subscription">要取消的內容(與訂閱時相同)。What to cancel, identical to what was subscribed.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>成功,或失敗。失敗時本地計數不變。Success or failure; on failure the local count is unchanged.</returns>
    public async Task<Result> UnsubscribeAsync(ShioajiSubscription subscription, CancellationToken cancellationToken = default)
    {
        var invalid = Validate(subscription);

        if (invalid is not null)
        {
            return invalid;
        }

        var result = await PostAsync(UnsubscribePath, subscription, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            bool removed;

            lock (_gate)
            {
                removed = _active.Remove(subscription.Key);
            }

            if (removed)
            {
                _budget.ReleaseSubscription();
            }
        }

        return result;
    }

    /// <summary>
    /// 把追蹤中的訂閱全部重送一次,給串流斷線重連後使用(伺服器端的訂閱需要重新訂)。不重複計入預算。
    /// Resends every tracked subscription, for use after a stream reconnection when the server-side subscriptions
    /// must be made again. They are not counted against the budget a second time.
    /// </summary>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 全部成功時為成功;否則為第一個失敗(其餘的仍會嘗試),<see cref="Error.Data"/> 帶失敗筆數。
    /// Success when all succeed, otherwise the first failure with the others still attempted, and the failed count
    /// in <see cref="Error.Data"/>.
    /// </returns>
    public async Task<Result> ReplaySubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = ActiveSubscriptions;
        Error? first = null;
        var failed = 0;

        foreach (var subscription in snapshot)
        {
            var result = await PostAsync(SubscribePath, subscription, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                failed++;
                first ??= result.Error;
            }
        }

        return first is null
            ? Result.Success()
            : first.WithData("failedCount", failed);
    }

    private static Error? Validate(ShioajiSubscription? subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (string.IsNullOrWhiteSpace(subscription.Code))
        {
            return ShioajiErrors.BlankArgument(nameof(subscription.Code));
        }

        if (string.IsNullOrWhiteSpace(subscription.Exchange))
        {
            return ShioajiErrors.BlankArgument(nameof(subscription.Exchange));
        }

        if (!ShioajiWire.SecurityType.TryGetWire(subscription.SecurityType, out _))
        {
            return ShioajiErrors.InvalidArgument("必須指定商品類別。A security type must be given.");
        }

        if (!ShioajiWire.QuoteType.TryGetWire(subscription.QuoteType, out _))
        {
            return ShioajiErrors.InvalidArgument("必須指定訂閱種類。A quote type must be given.");
        }

        if (subscription.SecurityType == ShioajiSecurityType.Index && subscription.QuoteType != ShioajiQuoteType.Quote)
        {
            return new Error(
                ShioajiErrorCodes.SubscriptionIndexRequiresQuote,
                "指數只支援 Quote 訂閱,Tick 與 BidAsk 不會有資料,請求未送出。An index supports the Quote subscription only; Tick and BidAsk would deliver nothing, so nothing was sent.",
                ErrorCategory.Validation);
        }

        if (subscription.SecurityType == ShioajiSecurityType.Future
            && IsContinuousMonth(subscription.Code)
            && string.IsNullOrWhiteSpace(subscription.TargetCode))
        {
            return new Error(
                ShioajiErrorCodes.SubscriptionTargetCodeRequired,
                $"連續月合約 {subscription.Code} 必須帶 TargetCode(先用商品查詢取得實際合約代碼),否則 server 只會送心跳,請求未送出。The continuous-month contract {subscription.Code} needs a TargetCode (look up the actual contract first), or the server sends only heartbeats; nothing was sent.",
                ErrorCategory.Validation);
        }

        return null;
    }

    private static bool IsContinuousMonth(string code)
    {
        var trimmed = code.Trim();

        return trimmed.EndsWith("R1", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("R2", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Result> PostAsync(string path, ShioajiSubscription subscription, CancellationToken cancellationToken)
    {
        // 前面已驗證過,所以查表一定查得到。
        // Validated above, so the lookups cannot miss.
        _ = ShioajiWire.SecurityType.TryGetWire(subscription.SecurityType, out var securityType);
        _ = ShioajiWire.QuoteType.TryGetWire(subscription.QuoteType, out var quoteType);

        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("security_type", securityType);
            writer.WriteString("exchange", subscription.Exchange.Trim());
            writer.WriteString("code", subscription.Code.Trim());
            writer.WriteString("quote_type", quoteType);

            if (!string.IsNullOrWhiteSpace(subscription.TargetCode))
            {
                writer.WriteString("target_code", subscription.TargetCode.Trim());
            }

            writer.WriteBoolean("intraday_odd", subscription.IntradayOdd);
            writer.WriteEndObject();
        });

        var response = await _api.PostStringAsync(path, json, cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            return response.ToResult();
        }

        return CheckSuccessFlag(response.GetValueOrThrow());
    }

    /// <summary>
    /// 回應是 <c>{success, message, subscription}</c>。2xx 但 <c>success</c> 明確為 false 才算失敗;
    /// 本文讀不懂、沒有這個欄位都當成功 —— 回應格式沒有完整實測,只相信明確的拒絕。
    /// The response is <c>{success, message, subscription}</c>. Only a 2xx whose <c>success</c> is explicitly false
    /// counts as failure; an unreadable body or a missing field counts as success, since the format is not fully
    /// measured and only an explicit refusal is believed.
    /// </summary>
    private static Result CheckSuccessFlag(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            var fields = new ShioajiFieldReader(document.RootElement, SubscribePath);

            if (fields.OptionalBool("success") is false)
            {
                var message = fields.OptionalString("message") ?? string.Empty;
                var shortened = message.Length > 200 ? message[..200] : message;

                return new Error(
                    ShioajiErrorCodes.SubscriptionRejected,
                    $"server 拒絕了這筆訂閱:{shortened} The server refused the subscription: {shortened}",
                    ErrorCategory.Validation);
            }
        }
        catch (JsonException)
        {
            return Result.Success();
        }

        return Result.Success();
    }
}
