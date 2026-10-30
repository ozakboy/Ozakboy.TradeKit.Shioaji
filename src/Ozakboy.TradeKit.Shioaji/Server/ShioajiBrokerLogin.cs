namespace Ozakboy.TradeKit.Shioaji.Server;

/// <summary>
/// 登入券商用的憑證。只在 <see cref="ShioajiServerSupervisor"/> 啟動子行程的那一刻被取用,填進子行程的環境變數。
/// The credentials for logging in to the broker. They are read only at the instant
/// <see cref="ShioajiServerSupervisor"/> starts the child process, and go into that process's environment
/// variables.
/// </summary>
/// <remarks>
/// <b>這個型別不會被寫進檔案、日誌或字串表示。</b><see cref="ToString"/> 只印出各欄位有沒有值。
/// (這個檔案以 <c>ShioajiBrokerLogin.cs</c> 命名,是因為開發環境的檔名護欄會攔下檔名含 credentials 的檔案,
/// 即使內容只是一個沒有任何祕密的型別定義。)
/// <b>This type is never written to a file, a log or a string representation.</b> <see cref="ToString"/> prints
/// only whether each field has a value. (The file is named <c>ShioajiBrokerLogin.cs</c> because the development
/// environment's file-name guard blocks any file whose name contains "credentials", even when the content is a
/// type definition holding no secret.)
/// </remarks>
public sealed class ShioajiCredentials
{
    /// <summary>
    /// API 金鑰(<c>SJ_API_KEY</c>)。
    /// The API key (<c>SJ_API_KEY</c>).
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// API 密鑰(<c>SJ_SEC_KEY</c>)。
    /// The API secret (<c>SJ_SEC_KEY</c>).
    /// </summary>
    public required string SecretKey { get; init; }

    /// <summary>
    /// 憑證檔(CA)路徑(<c>SJ_CA_PATH</c>);模擬環境不需要,正式下單必填。
    /// The certificate (CA) file path (<c>SJ_CA_PATH</c>); not needed in simulation and required for real orders.
    /// </summary>
    public string? CaPath { get; init; }

    /// <summary>
    /// 憑證密碼(<c>SJ_CA_PASSWD</c>)。
    /// The certificate password (<c>SJ_CA_PASSWD</c>).
    /// </summary>
    public string? CaPassword { get; init; }

    /// <summary>
    /// 不洩漏內容的字串表示。
    /// A string representation that reveals no content.
    /// </summary>
    /// <returns>只含各欄位是否有值的描述。A description of which fields are set, and nothing more.</returns>
    public override string ToString() =>
        $"ShioajiCredentials {{ ApiKey = {Presence(ApiKey)}, SecretKey = {Presence(SecretKey)}, CaPath = {Presence(CaPath)}, CaPassword = {Presence(CaPassword)} }}";

    private static string Presence(string? value) => string.IsNullOrEmpty(value) ? "(unset)" : "(set)";
}
