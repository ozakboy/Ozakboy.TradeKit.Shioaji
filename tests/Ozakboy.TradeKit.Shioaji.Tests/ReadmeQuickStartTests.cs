using Microsoft.Extensions.DependencyInjection;
using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.History;
using Ozakboy.TradeKit.Shioaji.MarketData;
using Ozakboy.TradeKit.Shioaji.Orders;
using Ozakboy.TradeKit.Shioaji.Portfolio;
using Ozakboy.TradeKit.Shioaji.Server;
using Ozakboy.TradeKit.Shioaji.Streaming;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

/// <summary>
/// README「快速開始」那一段的程式碼,逐行照抄,對著假伺服器跑一次 —— 文件裡的範例編譯不過或跑不起來,測試就會紅。
/// The code of the README quick start, copied line by line and run once against the fake server: if the example in
/// the documentation stops compiling or running, this test goes red.
/// </summary>
[TestClass]
public sealed class ReadmeQuickStartTests
{
    [TestMethod]
    public async Task TheQuickStart_StartsSubscribesReadsAndPlacesAnOrder()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/order/place_order", 200, Fixture.Read("trade_response.json"));

        // ───────────────────────── README 開始 / README begins ─────────────────────────
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddShioaji(options =>
        {
            options.BaseUri = server.BaseUri; // README: new Uri("http://127.0.0.1:8080")
        });

        // 重連後要重放的東西:委託回報訂閱(證券與期貨各一次)與行情訂閱。
        services.AddSingleton<Func<CancellationToken, Task<Result>>>(provider => async cancellationToken =>
        {
            var auth = provider.GetRequiredService<ShioajiAuthClient>();
            var quotes = provider.GetRequiredService<ShioajiQuoteClient>();

            var accounts = await auth.GetAccountsAsync(cancellationToken);

            if (accounts.IsFailure)
            {
                return accounts.ToResult();
            }

            foreach (var account in accounts.GetValueOrThrow().Where(a => a.IsSigned && a.AccountType is ShioajiAccountType.Stock or ShioajiAccountType.Futures))
            {
                var subscribed = await auth.SubscribeTradeAsync(account, cancellationToken);

                if (subscribed.IsFailure)
                {
                    return subscribed;
                }
            }

            return await quotes.ReplaySubscriptionsAsync(cancellationToken);
        });

        // 憑證由你從環境變數或安全儲存讀,只在啟動子行程的那一刻被呼叫。
        services.AddShioajiServerSupervisor(
            options =>
            {
                options.Production = false; // 預設就是模擬
                options.BindAddress = server.BindAddress; // README: "127.0.0.1:8080"
            },
            provider => () => new ShioajiCredentials
            {
                ApiKey = Environment.GetEnvironmentVariable("MY_SHIOAJI_API_KEY") ?? "api-key-placeholder",
                SecretKey = Environment.GetEnvironmentVariable("MY_SHIOAJI_SECRET_KEY") ?? "secret-key-placeholder",
            });

        await using var provider = services.BuildServiceProvider();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cancellation.Token;

        // 1. 啟動(或接手)官方 server。
        var supervisor = provider.GetRequiredService<ShioajiServerSupervisor>();
        var started = await supervisor.StartAsync(cancellationToken);

        if (started.IsFailure)
        {
            Assert.Fail(started.Error.ToString()); // README: Console.WriteLine($"{started.Error.Code}: {started.Error.Message}"); return;
        }

        // 2. 帳號,以及委託回報訂閱(證券與期貨各訂一次,否則 order_event 只有心跳)。
        var auth = provider.GetRequiredService<ShioajiAuthClient>();
        var accounts = (await auth.GetAccountsAsync(cancellationToken)).GetValueOrThrow();

        foreach (var account in accounts.Where(a => a.IsSigned && a.AccountType is ShioajiAccountType.Stock or ShioajiAccountType.Futures))
        {
            await auth.SubscribeTradeAsync(account, cancellationToken);
        }

        // 3. 訂閱行情。
        var quotes = provider.GetRequiredService<ShioajiQuoteClient>();

        await quotes.SubscribeAsync(
            new ShioajiSubscription
            {
                SecurityType = ShioajiSecurityType.Stock,
                Exchange = "TSE",
                Code = "2330",
                QuoteType = ShioajiQuoteType.Tick,
            },
            cancellationToken);

        // 4. 讀串流。
        var reader = provider.GetRequiredService<ShioajiStreamReader>();
        var ticks = new List<ShioajiTickStk>();

        var publisher = Task.Run(
            async () =>
            {
                await server.WaitForConnectionsAsync(1, TimeSpan.FromSeconds(10));
                await server.PublishAsync("tick_stk", Fixture.Read("tick_stk.json"), "2330");
            },
            CancellationToken.None);

        await foreach (var item in reader.ReadAsync(cancellationToken))
        {
            if (item.IsFailure)
            {
                continue; // 讀不懂的事件:記下來,串流不會因此結束。
            }

            switch (item.GetValueOrThrow())
            {
                case ShioajiTickStk tick:
                    ticks.Add(tick);
                    break;
                case ShioajiStreamLifecycleEvent { Kind: ShioajiStreamLifecycleKind.Disconnected }:
                    break; // 斷線到下一個 Connected 之間的行情沒收到 —— 視為資料缺口。
            }

            if (ticks.Count == 1)
            {
                break;
            }
        }

        await publisher;

        // 5. 下單(模擬環境)。
        var orders = provider.GetRequiredService<ShioajiOrderClient>();
        var placed = await orders.PlaceStockOrderAsync(
            new ShioajiStockOrderRequest
            {
                Exchange = "TSE",
                Code = "2330",
                Action = ShioajiAction.Buy,
                Price = 600.5m,
                Quantity = 1m,
                PriceType = ShioajiPriceType.Limit,
                OrderType = ShioajiOrderType.Rod,
                OrderLot = ShioajiOrderLot.Common,
                OrderCondition = ShioajiOrderCondition.Cash,
                CustomField = "sig001",
            },
            cancellationToken);

        if (placed.IsFailure)
        {
            // 逾時與 5xx 會帶 outcomeUnknown=true:單可能已經成立,先查委託,不要重送。
            Assert.Fail(placed.Error.ToString());
        }

        // ───────────────────────── README 結束 / README ends ─────────────────────────
        Assert.AreEqual(ShioajiServerState.Attached, supervisor.Status.State, "the fake server answers, so the supervisor attaches");
        Assert.HasCount(1, ticks);
        Assert.AreEqual(1002m, ticks[0].Close);
        Assert.AreEqual("a1b2c3d4", placed.GetValueOrThrow().Order.Id);
        Assert.AreEqual(2, server.Count("POST", "/api/v1/auth/subscribe_trade"), "securities and futures are subscribed once each");
    }

    /// <summary>
    /// README「回補 1 分 K 與逐筆」那一段,逐行照抄。
    /// The README section "Backfilling 1-minute bars and ticks", copied line by line.
    /// </summary>
    [TestMethod]
    public async Task TheBackfillExample_ReadsBarsAndTicks_AndShowsTheTraffic()
    {
        using var server = new FakeShioajiHttpServer();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShioaji(options => options.BaseUri = server.BaseUri);
        await using var provider = services.BuildServiceProvider();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cancellation.Token;
        var output = new List<string>();
        var auth = provider.GetRequiredService<ShioajiAuthClient>();

        // ───────────────────────── README 開始 / README begins ─────────────────────────
        var history = provider.GetRequiredService<ShioajiHistoryClient>();
        var contract = new ShioajiContractRef { SecurityType = ShioajiSecurityType.Stock, Exchange = "TSE", Code = "2330" };

        var bars = await history.GetKbarsAsync(contract, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30), cancellationToken);

        if (bars.IsSuccess)
        {
            var page = bars.GetValueOrThrow();

            if (page.PossiblyTrafficExhausted)
            {
                var usage = (await auth.GetUsageAsync(cancellationToken)).GetValueOrThrow();
                output.Add($"remaining {usage.RemainingBytes} bytes"); // README: Console.WriteLine
            }

            foreach (var bar in page.Items)
            {
                output.Add($"{bar.Time:O} O{bar.Open} H{bar.High} L{bar.Low} C{bar.Close} V{bar.Volume}"); // README: Console.WriteLine
            }
        }

        var ticks = await history.GetTicksAsync(
            contract,
            new DateOnly(2026, 10, 30),
            ShioajiTicksQuery.Range(new TimeOnly(9, 0), new TimeOnly(9, 30)),
            cancellationToken);

        var traffic = provider.GetRequiredService<ShioajiTrafficMeter>().Snapshot();
        output.Add($"used {traffic.Bytes} bytes, resets {traffic.NextResetAt:O}"); // README: Console.WriteLine
        // ───────────────────────── README 結束 / README ends ─────────────────────────

        Assert.IsTrue(bars.IsSuccess, bars.Error?.ToString());
        Assert.IsTrue(ticks.IsSuccess, ticks.Error?.ToString());
        Assert.HasCount(2, bars.GetValueOrThrow().Items);
        Assert.HasCount(2, ticks.GetValueOrThrow().Items);
        Assert.Contains("O1000 H1002 L999.5 C1001.5 V120", output[0]);
        Assert.IsTrue(traffic.Bytes > 0, "both responses were counted");
        Assert.HasCount(3, output);
    }

    /// <summary>
    /// README「帳務查詢」那一段,逐行照抄。
    /// The README section "Portfolio queries", copied line by line.
    /// </summary>
    [TestMethod]
    public async Task ThePortfolioExample_ReadsPositionsProfitAndLossSettlementsAndLimits()
    {
        using var server = new FakeShioajiHttpServer();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShioaji(options => options.BaseUri = server.BaseUri);
        await using var provider = services.BuildServiceProvider();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = cancellation.Token;
        var output = new List<string>();

        // ───────────────────────── README 開始 / README begins ─────────────────────────
        var portfolio = provider.GetRequiredService<ShioajiPortfolioClient>();

        var positions = await portfolio.ListStockPositionsAsync(cancellationToken: cancellationToken);

        if (positions.IsSuccess)
        {
            foreach (var position in positions.GetValueOrThrow())
            {
                output.Add($"{position.Code} {position.Direction} x{position.Quantity} cost {position.Price} pnl {position.Pnl}"); // README: Console.WriteLine
            }
        }

        var realized = await portfolio.ListStockProfitLossAsync(
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31),
            cancellationToken: cancellationToken);

        if (realized.IsSuccess && realized.GetValueOrThrow() is [{ Id: { } id }, ..])
        {
            var details = await portfolio.ListStockProfitLossDetailsAsync(id, cancellationToken: cancellationToken);

            foreach (var detail in details.GetValueOrThrow())
            {
                output.Add($"{detail.Code} {detail.TradeType} fee {detail.Fee} tax {detail.Tax}"); // README: Console.WriteLine
            }
        }

        var settlements = await portfolio.ListSettlementsAsync(cancellationToken: cancellationToken);

        foreach (var settlement in settlements.GetValueOrThrow())
        {
            output.Add($"T+{settlement.TPlus} {settlement.Date:yyyy-MM-dd} {settlement.Amount}"); // README: Console.WriteLine
        }

        var limits = await portfolio.GetTradingLimitsAsync(cancellationToken: cancellationToken);
        output.Add($"available {limits.GetValueOrThrow().TradingAvailable}"); // README: Console.WriteLine
        // ───────────────────────── README 結束 / README ends ─────────────────────────

        Assert.Contains("2330 Buy x2 cost 600.5 pnl 18999", output[0]);
        Assert.Contains("DayTrade", output[1]);
        Assert.Contains("T+0 2026-05-21 100000", output[2]);
        Assert.Contains("available 3800000", output[^1]);
        Assert.HasCount(5, output);
    }
}
