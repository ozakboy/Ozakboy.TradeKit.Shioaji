using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.History;
using Ozakboy.TradeKit.Shioaji.MarketData;
using Ozakboy.TradeKit.Shioaji.Orders;
using Ozakboy.TradeKit.Shioaji.Portfolio;
using Ozakboy.TradeKit.Shioaji.Streaming;

namespace Ozakboy.TradeKit.Shioaji.Tests.Support;

/// <summary>
/// 讀取複製到輸出目錄的 fixture。
/// Reads the fixtures copied to the output directory.
/// </summary>
internal static class Fixture
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)).Trim();
}

/// <summary>
/// 收集日誌訊息的測試用記錄器。
/// A logger that collects messages, for tests.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public string AllText => string.Join('\n', Entries.Select(e => e.Message));

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>
/// 建立指向假伺服器的整組用戶端。
/// Builds the whole set of clients pointing at a fake server.
/// </summary>
internal sealed class ShioajiTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    private ShioajiTestHost(ServiceProvider provider)
    {
        _provider = provider;
    }

    public ShioajiOptions Options => _provider.GetRequiredService<ShioajiOptions>();

    public ShioajiApiClient Api => _provider.GetRequiredService<ShioajiApiClient>();

    public ShioajiAuthClient Auth => _provider.GetRequiredService<ShioajiAuthClient>();

    public ShioajiContractClient Contracts => _provider.GetRequiredService<ShioajiContractClient>();

    public ShioajiQuoteClient Quotes => _provider.GetRequiredService<ShioajiQuoteClient>();

    public ShioajiOrderClient Orders => _provider.GetRequiredService<ShioajiOrderClient>();

    public ShioajiHistoryClient History => _provider.GetRequiredService<ShioajiHistoryClient>();

    public ShioajiPortfolioClient Portfolio => _provider.GetRequiredService<ShioajiPortfolioClient>();

    public ShioajiTrafficMeter Traffic => _provider.GetRequiredService<ShioajiTrafficMeter>();

    public ShioajiStreamReader Stream => _provider.GetRequiredService<ShioajiStreamReader>();

    public ShioajiRateBudget Budget => _provider.GetRequiredService<ShioajiRateBudget>();

    public IServiceProvider Services => _provider;

    public static ShioajiTestHost Create(
        FakeShioajiHttpServer server,
        Action<ShioajiOptions>? tweak = null,
        Action<IServiceCollection>? services = null) =>
        CreateAt(server.BaseUri, tweak, services);

    public static ShioajiTestHost CreateAt(
        Uri baseUri,
        Action<ShioajiOptions>? tweak = null,
        Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        services?.Invoke(collection);

        collection.AddShioaji(options =>
        {
            options.BaseUri = baseUri;

            // 測試要快:逾時與重連退避都縮短。
            // Tests must be quick: the timeouts and the reconnection backoff are shortened.
            options.RequestTimeout = TimeSpan.FromSeconds(2);
            options.StreamIdleTimeout = TimeSpan.FromSeconds(5);
            options.ReconnectBackoff.Initial = TimeSpan.FromMilliseconds(20);
            options.ReconnectBackoff.Max = TimeSpan.FromMilliseconds(100);
            tweak?.Invoke(options);
        });

        return new ShioajiTestHost(collection.BuildServiceProvider());
    }

    // 監督器只實作 IAsyncDisposable,ServiceProvider.Dispose() 遇到只有非同步釋放的服務會擲出例外,所以這裡走 DisposeAsync。
    // The supervisor implements only IAsyncDisposable, and ServiceProvider.Dispose() throws for a service that can
    // only be disposed asynchronously, so this goes through DisposeAsync.
    public void Dispose() => _provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

internal static class TestJson
{
    public static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    /// <summary>
    /// 取出 JSON 中某個數字欄位的原始文字。
    /// Gets the raw text of a number field in some JSON.
    /// </summary>
    public static string RawNumber(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement;

        foreach (var name in path)
        {
            element = element.GetProperty(name);
        }

        return element.GetRawText();
    }
}

internal static class TestEvents
{
    /// <summary>
    /// 把 fixture 當成一個 SSE 事件讀成型別化事件。
    /// Reads a fixture as one SSE event into a typed event.
    /// </summary>
    public static ShioajiStreamEvent Parse(string eventName, string fixture)
    {
        var parsed = ShioajiStreamEventParser.Parse(eventName, Fixture.Read(fixture), DateTimeOffset.UnixEpoch);

        Assert.IsTrue(parsed.IsSuccess, parsed.IsFailure ? parsed.Error.ToString() : "ok");

        return parsed.GetValueOrThrow();
    }
}
