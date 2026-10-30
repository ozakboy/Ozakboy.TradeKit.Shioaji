using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ozakboy.Http;
using Ozakboy.Security.Masking;
using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.MarketData;
using Ozakboy.TradeKit.Shioaji.Orders;
using Ozakboy.TradeKit.Shioaji.Server;
using Ozakboy.TradeKit.Shioaji.Streaming;

namespace Ozakboy.TradeKit.Shioaji;

/// <summary>
/// 把 Shioaji 用戶端註冊到相依性注入容器。
/// Registers the Shioaji clients with a dependency injection container.
/// </summary>
public static class ShioajiServiceCollectionExtensions
{
    /// <summary>
    /// 註冊 Shioaji 本機 HTTP server 的用戶端:選項、經 <c>Ozakboy.Http</c> 管線的具名 <see cref="HttpClient"/>、
    /// 低階與各區塊 client、串流讀取器與本地限流預算。
    /// Registers the clients for the Shioaji local HTTP server: the options, a named <see cref="HttpClient"/>
    /// going through the <c>Ozakboy.Http</c> pipeline, the low-level and area clients, the stream reader, and the
    /// local rate budget.
    /// </summary>
    /// <param name="services">服務集合。The service collection.</param>
    /// <param name="configure">
    /// 設定回呼。<b>Bearer 金鑰請由此從環境變數或安全設定來源注入</b>,本套件不含任何預設憑證。
    /// The configuration callback. <b>Inject the bearer credential here from environment variables or a secure
    /// configuration source</b>; this package ships none.
    /// </param>
    /// <returns>服務集合本身,便於串接。The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// 任一參數為 <see langword="null"/> 時擲出。Thrown when either argument is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// 設定不合法時擲出。Thrown when the resulting configuration is invalid.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>管線的組成。</b>不簽章(server 不用簽章,認證靠 <c>Authorization: Bearer</c> 標頭)、不限流(限流是
    /// <see cref="ShioajiRateBudget"/> 的事,而且它要在請求前就擋,管線內的限流是排隊而不是拒絕)、重試只對 GET、
    /// <b>POST 永不重試</b>(見 <see cref="ShioajiApiClient"/>)。
    /// <b>How the pipeline is composed.</b> No signing (the server needs none; authentication is the
    /// <c>Authorization: Bearer</c> header), no rate limiting (that is <see cref="ShioajiRateBudget"/>'s job, and it
    /// must refuse before the request, whereas rate limiting inside the pipeline queues), retries for GET only, and
    /// <b>POST never retried</b> (see <see cref="ShioajiApiClient"/>).
    /// </para>
    /// <para>
    /// <b>祕密。</b><see cref="ShioajiOptions.UseBearerAuth"/> 開啟時,金鑰、密鑰與 <c>金鑰:密鑰</c> 三個字面值會登記到
    /// 這個用戶端的遮罩器,錯誤與日誌裡出現它們都會被換成遮罩字串。短於 8 個字元的值無法登記(會大量誤遮一般文字)而被略過。
    /// <b>Secrets.</b> With <see cref="ShioajiOptions.UseBearerAuth"/> on, the key, the secret and the literal
    /// <c>key:secret</c> are registered on this client's masker, so any of them turns into the mask string in an
    /// error or log. A value shorter than 8 characters cannot be registered (it would mask swathes of ordinary
    /// text) and is skipped.
    /// </para>
    /// <para>
    /// <b>串流用另一個具名用戶端。</b>管線會把整個回應讀完才回傳,SSE 永遠讀不完,所以
    /// <see cref="ShioajiStreamReader"/> 用 <see cref="ShioajiConstants.StreamHttpClientName"/> 這個不經管線的用戶端。
    /// 要在重連後重放訂閱,請在容器裡註冊一個
    /// <c>Func&lt;CancellationToken, Task&lt;Result&gt;&gt;</c>,讀取器建立時會取用。
    /// <b>The stream uses a separate named client.</b> The pipeline reads a response to its end and an SSE response
    /// has none, so <see cref="ShioajiStreamReader"/> uses <see cref="ShioajiConstants.StreamHttpClientName"/>,
    /// which bypasses it. To replay subscriptions after a reconnection, register a
    /// <c>Func&lt;CancellationToken, Task&lt;Result&gt;&gt;</c> in the container and the reader picks it up.
    /// </para>
    /// <para>
    /// 全部註冊為<b>單例</b>:預算與訂閱計數、串流狀態都掛在實例上,註冊成 scoped 或 transient 會讓每個請求各算各的。
    /// Everything is registered as a <b>singleton</b>: the budget, the subscription count and the stream state live
    /// on the instances, and a scoped or transient registration would give every request its own.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddShioaji(this IServiceCollection services, Action<ShioajiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new ShioajiOptions();
        configure(options);

        var validation = options.Validate();

        if (validation.IsFailure)
        {
            throw new ArgumentException(validation.Error.Message, nameof(configure));
        }

        var baseAddress = WithTrailingSlash(options.BaseUri);
        var bearer = options.BearerToken;

        services.AddSingleton(options);

        services
            .AddHttpClient(ShioajiConstants.HttpClientName, client =>
            {
                client.BaseAddress = baseAddress;

                if (bearer is not null)
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                }
            })
            .AddOzakboyHttpPipeline(pipeline =>
            {
                pipeline.EnableSigning = false;
                pipeline.EnableRateLimiting = false;
                pipeline.Timeouts.AttemptTimeout = options.RequestTimeout;
                pipeline.Timeouts.OverallTimeout = options.RequestTimeout;

                foreach (var secret in CollectKnownSecrets(options))
                {
                    pipeline.KnownSecrets.Add(secret);
                }
            });

        services
            .AddHttpClient(ShioajiConstants.StreamHttpClientName, client =>
            {
                client.BaseAddress = baseAddress;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .RemoveAllLoggers();

        services.AddSingleton(provider =>
            new ShioajiRateBudget(options.RateBudget, provider.GetService<TimeProvider>()));

        // 門面交給 Ozakboy.Http 從註冊處建立:它帶入這個具名用戶端的遮罩器、同一份逾時設定與容器裡的時間來源。
        // The facade is built by Ozakboy.Http from the registration, which brings in this client's masker, the same
        // timeout settings, and the container's time source.
        services.AddSingleton(provider => provider.CreateOzakboyHttpPipelineClient(ShioajiConstants.HttpClientName));

        services.AddSingleton(provider => new ShioajiApiClient(
            provider.GetRequiredService<HttpPipelineClient>(),
            provider.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName)));

        services.AddSingleton(provider => new ShioajiAuthClient(
            provider.GetRequiredService<ShioajiApiClient>(),
            provider.GetRequiredService<ShioajiRateBudget>()));

        services.AddSingleton(provider => new ShioajiContractClient(provider.GetRequiredService<ShioajiApiClient>()));

        services.AddSingleton(provider => new ShioajiQuoteClient(
            provider.GetRequiredService<ShioajiApiClient>(),
            provider.GetRequiredService<ShioajiRateBudget>()));

        services.AddSingleton(provider => new ShioajiOrderClient(
            provider.GetRequiredService<ShioajiApiClient>(),
            provider.GetRequiredService<ShioajiRateBudget>()));

        services.AddSingleton(provider => new ShioajiStreamReader(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(ShioajiConstants.StreamHttpClientName),
            options,
            provider.GetService<ILogger<ShioajiStreamReader>>(),
            provider.GetService<Func<CancellationToken, Task<Result>>>(),
            provider.GetService<TimeProvider>(),
            provider.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName)));

        return services;
    }

    /// <summary>
    /// 註冊 <see cref="ShioajiServerSupervisor"/>。必須先呼叫 <see cref="AddShioaji"/>。
    /// Registers the <see cref="ShioajiServerSupervisor"/>. <see cref="AddShioaji"/> must be called first.
    /// </summary>
    /// <param name="services">服務集合。The service collection.</param>
    /// <param name="configure">行程設定回呼。The process settings callback.</param>
    /// <param name="credentialsFactory">
    /// 取得憑證提供者的工廠。回傳的 <c>Func&lt;ShioajiCredentials&gt;</c> 只會在啟動子行程的那一刻被呼叫。
    /// 憑證請從環境變數或安全儲存讀取,不要寫在程式碼或設定檔裡。
    /// A factory returning the credentials provider. The <c>Func&lt;ShioajiCredentials&gt;</c> it returns is called
    /// only at the instant the child process starts. Read credentials from the environment or a secure store, never
    /// from code or a configuration file.
    /// </param>
    /// <returns>服務集合本身。The same collection.</returns>
    /// <exception cref="ArgumentNullException">
    /// 任一參數為 <see langword="null"/> 時擲出。Thrown when an argument is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// 行程設定不合法時擲出。Thrown when the process settings are invalid.
    /// </exception>
    public static IServiceCollection AddShioajiServerSupervisor(
        this IServiceCollection services,
        Action<ShioajiServerOptions> configure,
        Func<IServiceProvider, Func<ShioajiCredentials>> credentialsFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(credentialsFactory);

        var serverOptions = new ShioajiServerOptions();
        configure(serverOptions);

        var validation = serverOptions.Validate();

        if (validation.IsFailure)
        {
            throw new ArgumentException(validation.Error.Message, nameof(configure));
        }

        services.AddSingleton(serverOptions);

        services.AddSingleton(provider => new ShioajiServerSupervisor(
            serverOptions,
            credentialsFactory(provider),
            provider.GetRequiredService<ShioajiAuthClient>(),
            provider.GetService<ILogger<ShioajiServerSupervisor>>() ?? NullLogger<ShioajiServerSupervisor>.Instance,
            provider.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName),
            provider.GetService<TimeProvider>()));

        return services;
    }

    private static Uri WithTrailingSlash(Uri uri)
    {
        var text = uri.AbsoluteUri;

        return text.EndsWith('/') ? uri : new Uri(text + "/", UriKind.Absolute);
    }

    private static List<string> CollectKnownSecrets(ShioajiOptions options)
    {
        var secrets = new List<string>();

        if (options.BearerToken is null)
        {
            return secrets;
        }

        foreach (var candidate in new[] { options.ApiKey!, options.SecretKey!, options.BearerToken })
        {
            if (candidate.Length >= SecretMasker.MinimumKnownSecretLength && !secrets.Contains(candidate, StringComparer.Ordinal))
            {
                secrets.Add(candidate);
            }
        }

        return secrets;
    }
}
