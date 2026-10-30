namespace Ozakboy.TradeKit.Shioaji.Server;

/// <summary>
/// <see cref="ShioajiServerSupervisor"/> 的設定。
/// The settings of <see cref="ShioajiServerSupervisor"/>.
/// </summary>
public sealed class ShioajiServerOptions
{
    private static readonly string[] ReservedEnvironmentKeys = ["SJ_API_KEY", "SJ_SEC_KEY", "SJ_CA_PATH", "SJ_CA_PASSWD"];

    /// <summary>
    /// <c>shioaji</c> 可執行檔的路徑。<see langword="null"/> 時依序找 <c>%USERPROFILE%\.local\bin\shioaji.exe</c>,
    /// 再找 <c>PATH</c> 上的 <c>shioaji</c>。
    /// The path of the <c>shioaji</c> executable. When <see langword="null"/>, <c>%USERPROFILE%\.local\bin\shioaji.exe</c>
    /// is tried first and then <c>shioaji</c> on the <c>PATH</c>.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// server 綁定的位址(<c>SJ_HTTP_ADDR</c>),預設 <c>127.0.0.1:8080</c>。
    /// 它必須與 <see cref="ShioajiOptions.BaseUri"/> 指向同一處,這由呼叫端保證。
    /// The address the server binds (<c>SJ_HTTP_ADDR</c>); <c>127.0.0.1:8080</c> by default. It must point at the
    /// same place as <see cref="ShioajiOptions.BaseUri"/>, which is the caller's responsibility.
    /// </summary>
    public string BindAddress { get; set; } = ShioajiConstants.DefaultBindAddress;

    /// <summary>
    /// 是否連正式環境(<c>SJ_PRODUCTION</c> 與 <c>--production</c>)。預設 <see langword="false"/>,也就是模擬。
    /// Whether to use the production environment (<c>SJ_PRODUCTION</c> and <c>--production</c>). <see langword="false"/>
    /// by default, which is simulation.
    /// </summary>
    /// <remarks>
    /// 預設刻意是模擬。預設若是正式環境,忘了設定的下場是真的下出單;預設是模擬,忘了設定的下場是單進了模擬盤,
    /// 很快就會被發現。
    /// The default is deliberately simulation. Defaulting to production means a forgotten setting places real
    /// orders; defaulting to simulation means the orders land in the simulator, where the mistake shows up soon.
    /// </remarks>
    public bool Production { get; set; }

    /// <summary>
    /// token 快取、商品檔與快取的目錄(<c>SJ_HOME_PATH</c>);<see langword="null"/> 表示用 server 的預設。
    /// The directory for token cache, contract files and caches (<c>SJ_HOME_PATH</c>); <see langword="null"/>
    /// uses the server's default.
    /// </summary>
    public string? HomePath { get; set; }

    /// <summary>
    /// 是否要求 HTTP 請求帶 Bearer 認證(<c>--auth</c>)。開啟時 <see cref="ShioajiOptions"/> 也要設
    /// <see cref="ShioajiOptions.UseBearerAuth"/>。
    /// Whether HTTP requests must carry Bearer authentication (<c>--auth</c>). When on,
    /// <see cref="ShioajiOptions.UseBearerAuth"/> must be set on <see cref="ShioajiOptions"/> as well.
    /// </summary>
    public bool RequireAuth { get; set; }

    /// <summary>
    /// 時間戳接受視窗,毫秒(<c>--receive-window</c>);<see langword="null"/> 表示不傳。
    /// The timestamp receive window in milliseconds (<c>--receive-window</c>); <see langword="null"/> sends none.
    /// </summary>
    public int? ReceiveWindowMs { get; set; }

    /// <summary>
    /// 啟動後等 <c>/api/v1/info</c> 回應的上限,預設 60 秒。登入券商與載入商品檔可能要一陣子。
    /// How long to wait for <c>/api/v1/info</c> after the start; 60 seconds by default. Logging in to the broker
    /// and loading contract files can take a while.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 停止時等 <c>shioaji server stop</c> 與行程結束的上限,預設 10 秒;逾時就終止整棵行程樹。
    /// How long a stop waits for <c>shioaji server stop</c> and the process to end; 10 seconds by default. After
    /// that the whole process tree is killed.
    /// </summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 連續非預期退出時最多重啟幾次,預設 5;超過就進入 <see cref="ShioajiServerState.Faulted"/> 並停止重啟。
    /// How many restarts consecutive unexpected exits get; 5 by default. Beyond that the supervisor enters
    /// <see cref="ShioajiServerState.Faulted"/> and stops restarting.
    /// </summary>
    public int MaxRestarts { get; set; } = 5;

    /// <summary>
    /// 第一次重啟前的等待,預設 2 秒;之後每次加倍,上限 60 秒。
    /// The wait before the first restart; 2 seconds by default, doubling each time up to 60 seconds.
    /// </summary>
    public TimeSpan RestartBackoff { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 行程穩定執行超過這段時間後再退出,連續重啟的計數歸零,預設 5 分鐘。
    /// After the process has run this long, a later exit starts the consecutive restart count again; 5 minutes by
    /// default.
    /// </summary>
    public TimeSpan StableAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 啟動後輪詢 <c>/api/v1/info</c> 的間隔,預設 500 毫秒。
    /// How often <c>/api/v1/info</c> is polled after the start; 500 milliseconds by default.
    /// </summary>
    public TimeSpan StartupPollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 額外傳給子行程的環境變數,例如 <c>SJ_AGENT_HARNESS</c>(語意未知,只提供透傳)。
    /// 不可設憑證用的四個鍵,憑證只能走 <see cref="ShioajiServerSupervisor"/> 的憑證提供者。
    /// Extra environment variables for the child process, such as <c>SJ_AGENT_HARNESS</c> (its semantics are
    /// unknown, so it is passed through only). The four credential keys are refused; credentials travel only
    /// through the supervisor's credentials provider.
    /// </summary>
    public IDictionary<string, string> AdditionalEnvironment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 檢查設定是否可用。
    /// Checks that the settings are usable.
    /// </summary>
    /// <returns>可用時為成功。Success when usable.</returns>
    public Result Validate()
    {
        if (!IsValidBindAddress(BindAddress))
        {
            return ShioajiErrors.InvalidOptions("BindAddress 必須是 host:port 形式。BindAddress must be of the form host:port.");
        }

        if (StartupTimeout <= TimeSpan.Zero || StopTimeout <= TimeSpan.Zero || StartupPollInterval <= TimeSpan.Zero)
        {
            return ShioajiErrors.InvalidOptions("StartupTimeout、StopTimeout 與 StartupPollInterval 必須為正值。StartupTimeout, StopTimeout and StartupPollInterval must be positive.");
        }

        if (MaxRestarts < 0 || RestartBackoff < TimeSpan.Zero || StableAfter < TimeSpan.Zero)
        {
            return ShioajiErrors.InvalidOptions("MaxRestarts、RestartBackoff 與 StableAfter 不可為負值。MaxRestarts, RestartBackoff and StableAfter must not be negative.");
        }

        if (ReceiveWindowMs is <= 0)
        {
            return ShioajiErrors.InvalidOptions("ReceiveWindowMs 必須為正值。ReceiveWindowMs must be positive.");
        }

        foreach (var key in AdditionalEnvironment.Keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return ShioajiErrors.InvalidOptions("AdditionalEnvironment 的鍵不可為空白。An AdditionalEnvironment key must not be blank.");
            }

            if (ReservedEnvironmentKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                return ShioajiErrors.InvalidOptions("AdditionalEnvironment 不可設定憑證用的鍵,請改用憑證提供者。AdditionalEnvironment must not set a credential key; use the credentials provider.");
            }
        }

        return Result.Success();
    }

    private static bool IsValidBindAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var colon = address.LastIndexOf(':');

        return colon > 0
            && colon < address.Length - 1
            && int.TryParse(address[(colon + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
            && port is > 0 and <= 65535;
    }
}
