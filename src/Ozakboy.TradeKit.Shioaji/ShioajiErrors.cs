using System.Globalization;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 本套件錯誤的建立方法,統一代碼、分類與訊息格式。訊息一律中英雙語(先中後英)。
/// Factory methods for this package's failures, keeping code, category and message format consistent. Messages
/// are always bilingual, Chinese first.
/// </summary>
internal static class ShioajiErrors
{
    public static Error InvalidOptions(string reason) =>
        Error.Validation(ShioajiErrorCodes.InvalidOptions, reason);

    public static Error InvalidArgument(string message) =>
        Error.Validation(ShioajiErrorCodes.InvalidArgument, message);

    public static Error BlankArgument(string name) =>
        InvalidArgument($"參數 {name} 不可為空白。The argument {name} must not be blank.");

    /// <summary>
    /// 看不懂的回應。分類是 Unexpected 而不是 Unavailable:格式不對通常是對方改了格式或這裡的對映寫錯,
    /// 重試只會再拿到一模一樣看不懂的回應。
    /// An unreadable response. The category is Unexpected rather than Unavailable: a wrong shape usually means
    /// the peer changed or this mapping is wrong, and a retry fetches the same unreadable response again.
    /// </summary>
    public static Error MalformedResponse(string reason) =>
        new(ShioajiErrorCodes.MalformedResponse, reason, ErrorCategory.Unexpected);

    public static Error MissingField(string field, string context) =>
        new Error(
                ShioajiErrorCodes.MissingField,
                $"{context} 缺少欄位「{field}」,或其值無法解析。{context} is missing the field \"{field}\" or its value cannot be read.",
                ErrorCategory.Unexpected)
            .WithData(ShioajiErrorDataKeys.Field, field);

    public static Error MalformedEvent(string eventName, string reason) =>
        new Error(
                ShioajiErrorCodes.StreamMalformedEvent,
                $"串流事件「{eventName}」讀不懂:{reason} The stream event \"{eventName}\" could not be read: {reason}",
                ErrorCategory.Unexpected)
            .WithData(ShioajiErrorDataKeys.EventName, eventName);

    public static Error HttpStatus(int statusCode, string? reasonPhrase, string? maskedBody, string? detail, string path)
    {
        var category = statusCode switch
        {
            401 => ErrorCategory.Unauthorized,
            403 => ErrorCategory.Forbidden,
            404 => ErrorCategory.NotFound,
            408 => ErrorCategory.Timeout,
            409 => ErrorCategory.Conflict,
            429 => ErrorCategory.RateLimited,
            >= 500 and <= 599 => ErrorCategory.Unavailable,
            >= 400 and <= 499 => ErrorCategory.Validation,
            _ => ErrorCategory.Unexpected,
        };

        var phrase = string.IsNullOrWhiteSpace(reasonPhrase) ? string.Empty : " " + reasonPhrase;
        var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $":{detail}";
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"Shioaji server 回應 HTTP {statusCode}{phrase}{suffix}。Shioaji server answered HTTP {statusCode}{phrase}{suffix}.");

        var error = new Error(ShioajiErrorCodes.ForHttpStatus(statusCode), message, category)
            .WithData(ShioajiErrorDataKeys.StatusCode, statusCode)
            .WithData(ShioajiErrorDataKeys.Path, path);

        return string.IsNullOrEmpty(maskedBody) ? error : error.WithData(ShioajiErrorDataKeys.Body, maskedBody);
    }

    public static Error ServerUnreachable(string path, string pipelineCode) =>
        new Error(
                ShioajiErrorCodes.ServerUnreachable,
                "連不上 Shioaji 本機 server,請確認 server 已啟動且位址正確。Cannot reach the Shioaji local server; check that it is running and the address is right.",
                ErrorCategory.Network)
            .WithData(ShioajiErrorDataKeys.Path, path)
            .WithData(ShioajiErrorDataKeys.PipelineCode, pipelineCode);

    public static Error RequestTimeout(string path, string pipelineCode) =>
        new Error(
                ShioajiErrorCodes.RequestTimeout,
                "請求逾時,Shioaji server 沒有在時限內回應。The request timed out; the Shioaji server did not answer in time.",
                ErrorCategory.Timeout)
            .WithData(ShioajiErrorDataKeys.Path, path)
            .WithData(ShioajiErrorDataKeys.PipelineCode, pipelineCode);

    public static Error RequestCancelled(string path) =>
        new Error(
                ShioajiErrorCodes.RequestCancelled,
                "請求已取消。The request was cancelled.",
                ErrorCategory.Cancelled)
            .WithData(ShioajiErrorDataKeys.Path, path);

    public static Error RequestFailed(Error pipelineError, string path) =>
        new Error(
                ShioajiErrorCodes.RequestFailed,
                $"請求失敗(管線代碼 {pipelineError.Code})。The request failed (pipeline code {pipelineError.Code}).",
                pipelineError.Category)
            .WithData(ShioajiErrorDataKeys.Path, path)
            .WithData(ShioajiErrorDataKeys.PipelineCode, pipelineError.Code);

    public static Error RateBudgetExhausted(string kind, TimeSpan wait, TimeSpan maxWait) =>
        new(
            ShioajiErrorCodes.RateBudgetExhausted,
            string.Create(
                CultureInfo.InvariantCulture,
                $"本地限流預算「{kind}」已用盡,需再等 {wait.TotalSeconds:F1} 秒,超過上限 {maxWait.TotalSeconds:F1} 秒,請求未送出。The local rate budget \"{kind}\" is spent; waiting another {wait.TotalSeconds:F1} s would exceed the {maxWait.TotalSeconds:F1} s ceiling, so nothing was sent."),
            ErrorCategory.RateLimited);

    public static Error SubscriptionLimitExceeded(int limit) =>
        new(
            ShioajiErrorCodes.SubscriptionLimitExceeded,
            string.Create(
                CultureInfo.InvariantCulture,
                $"本地訂閱數已達上限 {limit},請求未送出。The local subscription count reached the limit of {limit}; nothing was sent."),
            ErrorCategory.Exhausted);

    /// <summary>
    /// 在下單類請求的失敗上標記「結果未知」。逾時與 5xx 才標:連線被拒代表請求沒到達,不是未知。
    /// Flags an order-type failure as having an unknown outcome. Only timeouts and 5xx answers: a refused
    /// connection means the request never arrived, which is not unknown.
    /// </summary>
    public static Error WithOutcomeFlag(Error error)
    {
        var unknown = string.Equals(error.Code, ShioajiErrorCodes.RequestTimeout, StringComparison.Ordinal)
            || (error.TryGetInt64(ShioajiErrorDataKeys.StatusCode, out var status) && status >= 500);

        return unknown ? error.WithData(ShioajiErrorDataKeys.OutcomeUnknown, true) : error;
    }
}
