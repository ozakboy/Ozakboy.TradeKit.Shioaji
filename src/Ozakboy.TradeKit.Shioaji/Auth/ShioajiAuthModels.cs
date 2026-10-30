namespace Ozakboy.TradeKit.Shioaji.Auth;

/// <summary>
/// <c>GET /api/v1/info</c> 的回應:server 的身分與模式。
/// The response of <c>GET /api/v1/info</c>: the server's identity and mode.
/// </summary>
public sealed record ShioajiServerInfo
{
    /// <summary>
    /// server 名稱。
    /// The server name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// server 版本。
    /// The server version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// 是否為模擬環境(<c>SJ_PRODUCTION</c> 為 false)。<see langword="false"/> 代表連的是正式環境。
    /// Whether this is the simulation environment (<c>SJ_PRODUCTION</c> false). <see langword="false"/> means
    /// the production environment.
    /// </summary>
    public required bool Simulation { get; init; }

    /// <summary>
    /// server 支援的協定。
    /// The protocols the server supports.
    /// </summary>
    public required IReadOnlyList<string> Protocols { get; init; }
}

/// <summary>
/// 一個券商帳號(<c>GET /api/v1/auth/accounts</c> 的一筆)。
/// One broker account, an entry of <c>GET /api/v1/auth/accounts</c>.
/// </summary>
public sealed record ShioajiAccount
{
    /// <summary>
    /// 帳號類型。
    /// The account type.
    /// </summary>
    public required ShioajiAccountType AccountType { get; init; }

    /// <summary>
    /// 身分證字號(個資,不要寫進日誌)。
    /// The national ID (personal data; keep it out of logs).
    /// </summary>
    public required string PersonId { get; init; }

    /// <summary>
    /// 券商代號。
    /// The broker id.
    /// </summary>
    public required string BrokerId { get; init; }

    /// <summary>
    /// 帳號。
    /// The account id.
    /// </summary>
    public required string AccountId { get; init; }

    /// <summary>
    /// 是否已簽署 API 條款並完成測試報告。<see langword="false"/> 的帳號無法下單。
    /// Whether the API terms are signed and the test report is done. An account with <see langword="false"/>
    /// cannot place orders.
    /// </summary>
    public required bool IsSigned { get; init; }

    /// <summary>
    /// 帳號持有人名稱(個資,不要寫進日誌)。
    /// The account holder's name (personal data; keep it out of logs).
    /// </summary>
    public required string Username { get; init; }
}

/// <summary>
/// <c>GET /api/v1/auth/usage</c> 的回應:今日流量用量。
/// The response of <c>GET /api/v1/auth/usage</c>: today's traffic usage.
/// </summary>
public sealed record ShioajiUsage
{
    /// <summary>
    /// 目前的連線數。
    /// The current number of connections.
    /// </summary>
    public required long Connections { get; init; }

    /// <summary>
    /// 今日已用位元組。
    /// Bytes used today.
    /// </summary>
    public required long Bytes { get; init; }

    /// <summary>
    /// 今日位元組上限(依近 30 日成交金額分級,08:00 重置)。
    /// Today's byte ceiling, tiered by the last 30 days' turnover and reset at 08:00.
    /// </summary>
    public required long LimitBytes { get; init; }

    /// <summary>
    /// 今日剩餘位元組。超量時行情查詢會回空值。
    /// Bytes remaining today. Market queries return empty values once it is exhausted.
    /// </summary>
    public required long RemainingBytes { get; init; }
}

/// <summary>
/// <c>GET /api/v1/stream/status</c> 的回應。訂了行情卻收不到時,先看 <see cref="ActiveConnections"/> 是否為 0。
/// The response of <c>GET /api/v1/stream/status</c>. When subscriptions deliver nothing, look first at whether
/// <see cref="ActiveConnections"/> is 0.
/// </summary>
public sealed record ShioajiStreamStatus
{
    /// <summary>
    /// 目前的串流連線數。
    /// The current number of stream connections.
    /// </summary>
    public required long ActiveConnections { get; init; }

    /// <summary>
    /// server 回報的時間;server 沒給或格式讀不懂時為 <see langword="null"/>。
    /// The time the server reported, or <see langword="null"/> when absent or unreadable.
    /// </summary>
    public required DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// server 回報的狀態字串。
    /// The status string the server reported.
    /// </summary>
    public required string Status { get; init; }
}
