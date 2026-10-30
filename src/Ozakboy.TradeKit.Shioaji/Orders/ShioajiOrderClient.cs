using System.Globalization;
using System.Text.Json;
using Ozakboy.TradeKit.Shioaji.Auth;

namespace Ozakboy.TradeKit.Shioaji.Orders;

/// <summary>
/// 委託相關的端點:下單、改價、改量、撤單、列出委託。
/// The order endpoints: place, reprice, resize, cancel, and list.
/// </summary>
/// <remarks>
/// <para>
/// <b>下單類請求永不重試。</b>全部是 POST,而 <see cref="ShioajiApiClient"/> 對 POST 不重試。重送一張下單請求
/// 的後果是多一個部位,所以「不確定有沒有成功」時唯一正確的動作是查委託(<see cref="ListTradesAsync"/>),
/// 不是重送。
/// <b>Order requests are never retried.</b> They are all POST and <see cref="ShioajiApiClient"/> never retries a
/// POST. Repeating an order means an extra position, so when it is unclear whether one went through the only
/// correct move is to look it up (<see cref="ListTradesAsync"/>), not to send it again.
/// </para>
/// <para>
/// <b>「結果未知」的標記。</b>逾時與 5xx 的失敗在 <see cref="Error.Data"/> 上帶
/// <see cref="ShioajiErrorDataKeys.OutcomeUnknown"/> = <c>true</c>:請求可能已被券商收下。連線被拒(請求沒到達)
/// 與 4xx(明確拒絕)不帶。2xx 但回應讀不懂是
/// <see cref="ShioajiErrorCodes.OrderResponseUnreadable"/>,同樣代表單可能已經成立。
/// <b>The unknown-outcome flag.</b> A timeout or 5xx failure carries
/// <see cref="ShioajiErrorDataKeys.OutcomeUnknown"/> = <c>true</c> in <see cref="Error.Data"/>: the broker may have
/// accepted the request. A refused connection (never arrived) and a 4xx (an explicit refusal) do not. A 2xx with an
/// unreadable response is <see cref="ShioajiErrorCodes.OrderResponseUnreadable"/> and likewise means the order may
/// stand.
/// </para>
/// <para>
/// 預算:下單、改價、改量、撤單計入 <see cref="ShioajiBudgetKind.Order"/>;<see cref="ListTradesAsync"/> 在
/// <c>refresh</c> 為 true 時會讓 server 向券商更新狀態,計入較緊的 <see cref="ShioajiBudgetKind.Portfolio"/>,
/// 否則計入 <see cref="ShioajiBudgetKind.Order"/>。
/// Budget: placing, repricing, resizing and cancelling count against <see cref="ShioajiBudgetKind.Order"/>;
/// <see cref="ListTradesAsync"/> with <c>refresh</c> true makes the server update from the broker and counts
/// against the tighter <see cref="ShioajiBudgetKind.Portfolio"/>, otherwise against
/// <see cref="ShioajiBudgetKind.Order"/>.
/// </para>
/// </remarks>
public sealed class ShioajiOrderClient
{
    private const string PlaceOrderPath = "/api/v1/order/place_order";

    private const string UpdatePricePath = "/api/v1/order/update_price";

    private const string UpdateQuantityPath = "/api/v1/order/update_qty";

    private const string CancelOrderPath = "/api/v1/order/cancel_order";

    private const string TradesPath = "/api/v1/order/trades";

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
    public ShioajiOrderClient(ShioajiApiClient api, ShioajiRateBudget budget)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(budget);

        _api = api;
        _budget = budget;
    }

    /// <summary>
    /// 送出股票委託(<c>POST /api/v1/order/place_order</c>)。
    /// Places a stock order (<c>POST /api/v1/order/place_order</c>).
    /// </summary>
    /// <param name="request">委託內容。The order.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>成立的委託與狀態,或失敗(見類別說明的「結果未知」)。The resulting trade, or a failure; see the class remarks on unknown outcomes.</returns>
    public async Task<Result<ShioajiTrade>> PlaceStockOrderAsync(
        ShioajiStockOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = ValidateStock(request);

        if (invalid is not null)
        {
            return invalid;
        }

        return await PlaceAsync(WriteStockBody(request), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 送出期貨委託(<c>POST /api/v1/order/place_order</c>)。
    /// Places a futures order (<c>POST /api/v1/order/place_order</c>).
    /// </summary>
    /// <param name="request">委託內容。The order.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>成立的委託與狀態,或失敗。The resulting trade, or a failure.</returns>
    public async Task<Result<ShioajiTrade>> PlaceFuturesOrderAsync(
        ShioajiFuturesOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = ValidateFutures(request);

        if (invalid is not null)
        {
            return invalid;
        }

        return await PlaceAsync(WriteFuturesBody(request), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 改價(<c>POST /api/v1/order/update_price</c>)。盤中零股不能改價。
    /// Reprices an order (<c>POST /api/v1/order/update_price</c>). An intraday odd lot cannot be repriced.
    /// </summary>
    /// <param name="tradeId">委託識別碼(<see cref="ShioajiOrder.Id"/>)。The order identifier (<see cref="ShioajiOrder.Id"/>).</param>
    /// <param name="price">新價格。The new price.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>
    /// 更新後的委託;server 的回應不是委託形狀時,成功但值為 <see langword="null"/>(2xx 即代表券商收下了請求)。
    /// The updated trade; when the server's answer is not shaped like a trade, success with a <see langword="null"/>
    /// value (a 2xx means the request was accepted).
    /// </returns>
    public Task<Result<ShioajiTrade?>> UpdatePriceAsync(
        string tradeId,
        decimal price,
        CancellationToken cancellationToken = default)
    {
        if (price <= 0m)
        {
            return Task.FromResult<Result<ShioajiTrade?>>(
                Error.Validation(ShioajiErrorCodes.OrderInvalidRequest, "新價格必須大於 0。The new price must be above 0."));
        }

        return ModifyAsync(
            UpdatePricePath,
            tradeId,
            writer => writer.WriteRawValue(Precision.ToPlainString(price)),
            "price",
            cancellationToken);
    }

    /// <summary>
    /// 改量(<c>POST /api/v1/order/update_qty</c>)。只能減量。
    /// Resizes an order (<c>POST /api/v1/order/update_qty</c>). Reducing only.
    /// </summary>
    /// <param name="tradeId">委託識別碼。The order identifier.</param>
    /// <param name="quantity">新數量(正整數,必須比原本小)。The new quantity: a positive whole number smaller than the original.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>見 <see cref="UpdatePriceAsync"/>。See <see cref="UpdatePriceAsync"/>.</returns>
    public Task<Result<ShioajiTrade?>> UpdateQuantityAsync(
        string tradeId,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0m || quantity != decimal.Truncate(quantity))
        {
            return Task.FromResult<Result<ShioajiTrade?>>(
                Error.Validation(ShioajiErrorCodes.OrderInvalidRequest, "新數量必須是正整數。The new quantity must be a positive whole number."));
        }

        return ModifyAsync(
            UpdateQuantityPath,
            tradeId,
            writer => writer.WriteRawValue(Precision.ToPlainString(quantity)),
            "quantity",
            cancellationToken);
    }

    /// <summary>
    /// 撤單(<c>POST /api/v1/order/cancel_order</c>)。
    /// Cancels an order (<c>POST /api/v1/order/cancel_order</c>).
    /// </summary>
    /// <param name="tradeId">委託識別碼。The order identifier.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>見 <see cref="UpdatePriceAsync"/>。See <see cref="UpdatePriceAsync"/>.</returns>
    public Task<Result<ShioajiTrade?>> CancelOrderAsync(string tradeId, CancellationToken cancellationToken = default) =>
        ModifyAsync(CancelOrderPath, tradeId, valueWriter: null, valueName: null, cancellationToken);

    /// <summary>
    /// 列出委託(<c>POST /api/v1/order/trades</c>)。
    /// Lists orders (<c>POST /api/v1/order/trades</c>).
    /// </summary>
    /// <param name="accountType">帳號類型(證券或期貨)。The account type, securities or futures.</param>
    /// <param name="brokerId">券商代號;<see langword="null"/> 表示用預設帳號。The broker id, or <see langword="null"/> for the default account.</param>
    /// <param name="accountId">帳號;<see langword="null"/> 表示用預設帳號。The account id, or <see langword="null"/> for the default account.</param>
    /// <param name="refresh">是否讓 server 先向券商更新狀態。Whether the server first updates the status from the broker.</param>
    /// <param name="cancellationToken">取消權杖。The cancellation token.</param>
    /// <returns>委託清單(含成交明細),或已對映的失敗。The trades with their fills, or a mapped failure.</returns>
    public async Task<Result<IReadOnlyList<ShioajiTrade>>> ListTradesAsync(
        ShioajiAccountType accountType,
        string? brokerId = null,
        string? accountId = null,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (accountType is not (ShioajiAccountType.Stock or ShioajiAccountType.Futures))
        {
            return ShioajiErrors.InvalidArgument(
                "列出委託只支援證券(Stock)或期貨(Futures)帳號。Listing trades supports Stock or Futures accounts only.");
        }

        if (string.IsNullOrWhiteSpace(brokerId) != string.IsNullOrWhiteSpace(accountId))
        {
            return ShioajiErrors.InvalidArgument(
                "brokerId 與 accountId 必須同時提供或同時省略。brokerId and accountId must be given together or omitted together.");
        }

        var permit = await _budget
            .AcquireAsync(refresh ? ShioajiBudgetKind.Portfolio : ShioajiBudgetKind.Order, cancellationToken)
            .ConfigureAwait(false);

        if (permit.IsFailure)
        {
            return permit.Error;
        }

        _ = ShioajiWire.AccountType.TryGetWire(accountType, out var wire);

        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("account_type", wire);

            if (!string.IsNullOrWhiteSpace(brokerId))
            {
                writer.WriteString("broker_id", brokerId.Trim());
                writer.WriteString("account_id", accountId!.Trim());
            }

            writer.WriteBoolean("refresh", refresh);
            writer.WriteEndObject();
        });

        var response = await _api.PostStringAsync(TradesPath, json, cancellationToken).ConfigureAwait(false);

        return response.IsFailure
            ? ShioajiErrors.WithOutcomeFlag(response.Error)
            : ShioajiTradeParser.ParseTradeList(response.GetValueOrThrow(), TradesPath);
    }

    private async Task<Result<ShioajiTrade>> PlaceAsync(string json, CancellationToken cancellationToken)
    {
        var permit = await _budget.AcquireAsync(ShioajiBudgetKind.Order, cancellationToken).ConfigureAwait(false);

        if (permit.IsFailure)
        {
            return permit.Error;
        }

        var response = await _api.PostStringAsync(PlaceOrderPath, json, cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            return ShioajiErrors.WithOutcomeFlag(response.Error);
        }

        var trade = ShioajiTradeParser.ParseTrade(response.GetValueOrThrow(), PlaceOrderPath);

        if (trade.IsSuccess)
        {
            return trade;
        }

        // 2xx 但讀不懂:單可能已經成立。這是下單路徑上最危險的一種失敗,錯誤碼與訊息都要讓呼叫端知道「去查,不要重送」。
        // A 2xx that cannot be read: the order may stand. This is the most dangerous failure on the order path, and
        // both the code and the message must tell the caller to look it up and not to resend.
        return new Error(
                ShioajiErrorCodes.OrderResponseUnreadable,
                "券商回了 2xx,但回應讀不懂 —— 單可能已經成立,請查詢委託,不要重送。The server answered 2xx but the response is unreadable; the order may have been placed, so look it up and do not resend.",
                ErrorCategory.Unexpected)
            .WithData(ShioajiErrorDataKeys.OutcomeUnknown, true)
            .WithData(ShioajiErrorDataKeys.PipelineCode, trade.Error.Code);
    }

    private async Task<Result<ShioajiTrade?>> ModifyAsync(
        string path,
        string tradeId,
        Action<Utf8JsonWriter>? valueWriter,
        string? valueName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tradeId))
        {
            return ShioajiErrors.BlankArgument(nameof(tradeId));
        }

        var permit = await _budget.AcquireAsync(ShioajiBudgetKind.Order, cancellationToken).ConfigureAwait(false);

        if (permit.IsFailure)
        {
            return permit.Error;
        }

        var json = ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("trade_id", tradeId.Trim());

            if (valueWriter is not null && valueName is not null)
            {
                writer.WritePropertyName(valueName);
                valueWriter(writer);
            }

            writer.WriteEndObject();
        });

        var response = await _api.PostStringAsync(path, json, cancellationToken).ConfigureAwait(false);

        if (response.IsFailure)
        {
            return ShioajiErrors.WithOutcomeFlag(response.Error);
        }

        // 這幾個端點的回應格式沒有實測:2xx 就代表請求已被收下。讀得出委託就帶回去,讀不出就回 null,
        // 不能因為回應長得不一樣而把一個已經生效的撤單報成失敗。
        // The response format of these endpoints is unmeasured: a 2xx means the request was accepted. A readable
        // trade is passed back and an unreadable one gives null; an effective cancellation must not be reported as
        // a failure just because the response looks different.
        var trade = ShioajiTradeParser.ParseTrade(response.GetValueOrThrow(), path);

        return trade.IsSuccess ? trade.GetValueOrThrow() : (ShioajiTrade?)null;
    }

    private static Error? ValidateStock(ShioajiStockOrderRequest request)
    {
        var common = ValidateCommon(request.Exchange, request.Code, request.Action, request.Price, request.Quantity, request.PriceType, request.OrderType, request.Account);

        if (common is not null)
        {
            return common;
        }

        if (!ShioajiWire.OrderLot.TryGetWire(request.OrderLot, out _))
        {
            return InvalidRequest("必須指定交易單位(OrderLot)。An OrderLot must be given.");
        }

        if (!ShioajiWire.OrderCondition.TryGetWire(request.OrderCondition, out _))
        {
            return InvalidRequest("必須指定委託條件(OrderCondition)。An OrderCondition must be given.");
        }

        return IsValidCustomField(request.CustomField)
            ? null
            : new Error(
                ShioajiErrorCodes.OrderInvalidCustomField,
                $"CustomField 最多 {ShioajiConstants.MaxCustomFieldLength} 個英數字元,請求未送出。CustomField must be at most {ShioajiConstants.MaxCustomFieldLength} alphanumeric characters; nothing was sent.",
                ErrorCategory.Validation);
    }

    private static Error? ValidateFutures(ShioajiFuturesOrderRequest request)
    {
        var common = ValidateCommon(request.Exchange, request.Code, request.Action, request.Price, request.Quantity, request.PriceType, request.OrderType, request.Account);

        if (common is not null)
        {
            return common;
        }

        return ShioajiWire.FuturesOcType.TryGetWire(request.OcType, out _)
            ? null
            : InvalidRequest("必須指定開平倉類型(OcType)。An OcType must be given.");
    }

    private static Error? ValidateCommon(
        string exchange,
        string code,
        ShioajiAction action,
        decimal price,
        decimal quantity,
        ShioajiPriceType priceType,
        ShioajiOrderType orderType,
        ShioajiOrderAccount? account)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            return InvalidRequest("Exchange 不可為空白。Exchange must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return InvalidRequest("Code 不可為空白。Code must not be blank.");
        }

        if (!ShioajiWire.Action.TryGetWire(action, out _))
        {
            return InvalidRequest("必須指定買賣方向(Action)。An Action must be given.");
        }

        if (!ShioajiWire.PriceType.TryGetWire(priceType, out _))
        {
            return InvalidRequest("必須指定價格類型(PriceType)。A PriceType must be given.");
        }

        if (!ShioajiWire.OrderType.TryGetWire(orderType, out _))
        {
            return InvalidRequest("必須指定有效期限類型(OrderType)。An OrderType must be given.");
        }

        if (price < 0m || (priceType == ShioajiPriceType.Limit && price == 0m))
        {
            return InvalidRequest("限價單的價格必須大於 0,且價格不可為負。A limit order's price must be above 0 and no price may be negative.");
        }

        if (quantity <= 0m || quantity != decimal.Truncate(quantity))
        {
            return InvalidRequest("數量必須是正整數。The quantity must be a positive whole number.");
        }

        if (account is not null
            && (string.IsNullOrWhiteSpace(account.BrokerId) || string.IsNullOrWhiteSpace(account.AccountId)))
        {
            return InvalidRequest("Account 的 BrokerId 與 AccountId 都不可為空白。Account's BrokerId and AccountId must both be non-blank.");
        }

        return null;
    }

    private static Error InvalidRequest(string message) =>
        Error.Validation(ShioajiErrorCodes.OrderInvalidRequest, message);

    /// <summary>
    /// <c>custom_field</c> 規則:最多 6 個、ASCII 英文字母或數字。空字串與 <see langword="null"/> 視為沒有。
    /// The <c>custom_field</c> rule: at most 6 ASCII letters or digits. An empty string and <see langword="null"/>
    /// both mean none.
    /// </summary>
    internal static bool IsValidCustomField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        return value.Length <= ShioajiConstants.MaxCustomFieldLength && value.All(char.IsAsciiLetterOrDigit);
    }

    private static string WriteStockBody(ShioajiStockOrderRequest request) =>
        ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            WriteContract(writer, "STK", request.Exchange, request.Code);
            writer.WritePropertyName("stock_order");
            writer.WriteStartObject();
            WriteCommonOrderFields(writer, request.Action, request.Price, request.Quantity, request.PriceType, request.OrderType);
            writer.WriteString("order_lot", Wire(ShioajiWire.OrderLot, request.OrderLot));
            writer.WriteString("order_cond", Wire(ShioajiWire.OrderCondition, request.OrderCondition));
            writer.WriteBoolean("daytrade_short", request.DayTradeShort);

            if (!string.IsNullOrEmpty(request.CustomField))
            {
                writer.WriteString("custom_field", request.CustomField);
            }

            WriteAccount(writer, request.Account);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    private static string WriteFuturesBody(ShioajiFuturesOrderRequest request) =>
        ShioajiJson.Write(writer =>
        {
            writer.WriteStartObject();
            WriteContract(writer, "FUT", request.Exchange, request.Code);
            writer.WritePropertyName("futures_order");
            writer.WriteStartObject();
            WriteCommonOrderFields(writer, request.Action, request.Price, request.Quantity, request.PriceType, request.OrderType);
            writer.WriteString("octype", Wire(ShioajiWire.FuturesOcType, request.OcType));
            WriteAccount(writer, request.Account);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    private static void WriteContract(Utf8JsonWriter writer, string securityType, string exchange, string code)
    {
        writer.WritePropertyName("contract");
        writer.WriteStartObject();
        writer.WriteString("security_type", securityType);
        writer.WriteString("exchange", exchange.Trim());
        writer.WriteString("code", code.Trim());
        writer.WriteEndObject();
    }

    private static void WriteCommonOrderFields(
        Utf8JsonWriter writer,
        ShioajiAction action,
        decimal price,
        decimal quantity,
        ShioajiPriceType priceType,
        ShioajiOrderType orderType)
    {
        writer.WriteString("action", Wire(ShioajiWire.Action, action));

        // 價格與數量以原始十進位文字寫出,不經過 double:Precision.ToPlainString 保證沒有科學記號與尾隨零。
        // Price and quantity are written as raw decimal text, never through double: Precision.ToPlainString
        // guarantees no exponent and no trailing zeros.
        writer.WritePropertyName("price");
        writer.WriteRawValue(Precision.ToPlainString(price));
        writer.WritePropertyName("quantity");
        writer.WriteRawValue(Precision.ToPlainString(quantity));
        writer.WriteString("price_type", Wire(ShioajiWire.PriceType, priceType));
        writer.WriteString("order_type", Wire(ShioajiWire.OrderType, orderType));
    }

    private static void WriteAccount(Utf8JsonWriter writer, ShioajiOrderAccount? account)
    {
        if (account is null)
        {
            return;
        }

        writer.WritePropertyName("account");
        writer.WriteStartObject();
        writer.WriteString("broker_id", account.BrokerId.Trim());
        writer.WriteString("account_id", account.AccountId.Trim());
        writer.WriteEndObject();
    }

    private static string Wire<TEnum>(ShioajiWireMap<TEnum> map, TEnum value)
        where TEnum : struct, Enum =>
        map.TryGetWire(value, out var wire)
            ? wire
            : throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"未驗證的列舉值 {value}。An unvalidated enum value {value}."));
}
