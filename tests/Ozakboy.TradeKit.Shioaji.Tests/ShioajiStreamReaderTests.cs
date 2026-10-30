using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.MarketData;
using Ozakboy.TradeKit.Shioaji.Streaming;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiStreamReaderTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private static readonly ShioajiSubscription Tsmc = new()
    {
        SecurityType = ShioajiSecurityType.Stock,
        Exchange = "TSE",
        Code = "2330",
        QuoteType = ShioajiQuoteType.Tick,
    };

    private static readonly ShioajiAccount StockAccount = new()
    {
        AccountType = ShioajiAccountType.Stock,
        PersonId = "A123456789",
        BrokerId = "9A95",
        AccountId = "1234567",
        IsSigned = true,
        Username = "TEST",
    };

    /// <summary>
    /// 一直讀到條件成立、逾時或讀取器結束為止。
    /// Reads until the condition holds, the time runs out, or the reader ends.
    /// </summary>
    private static async Task<List<Result<ShioajiStreamEvent>>> CollectAsync(
        ShioajiStreamReader reader,
        Func<List<Result<ShioajiStreamEvent>>, bool> until,
        Func<Task>? whileConnected = null)
    {
        using var cancellation = new CancellationTokenSource(Patience);
        var items = new List<Result<ShioajiStreamEvent>>();
        Task? background = null;

        await foreach (var item in reader.ReadAsync(cancellation.Token))
        {
            items.Add(item);

            // 第一個事件(Connected)到了才開始做會觸發事件的動作。
            // Whatever triggers events starts only once the first event, Connected, has arrived.
            if (background is null && whileConnected is not null)
            {
                background = Task.Run(whileConnected, CancellationToken.None);
            }

            if (until(items))
            {
                break;
            }
        }

        if (background is not null)
        {
            await background;
        }

        return items;
    }

    private static IEnumerable<ShioajiStreamLifecycleEvent> Lifecycle(IEnumerable<Result<ShioajiStreamEvent>> items) =>
        items.Where(i => i.IsSuccess).Select(i => i.GetValueOrThrow()).OfType<ShioajiStreamLifecycleEvent>();

    [TestMethod]
    public async Task ASubscribedTick_ArrivesTypedAfterTheConnectedEvent_WithBothTimes()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var before = DateTimeOffset.UtcNow;

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiTickStk),
            async () =>
            {
                await host.Quotes.SubscribeAsync(Tsmc);
                await server.PublishAsync("tick_stk", Fixture.Read("tick_stk.json"), "2330");
            });

        var connected = (ShioajiStreamLifecycleEvent)items[0].GetValueOrThrow();

        Assert.AreEqual(ShioajiStreamLifecycleKind.Connected, connected.Kind);
        Assert.AreEqual(0, connected.Attempt);
        Assert.IsNull(connected.Reason);

        var tick = items.Select(i => i.GetValueOrThrow()).OfType<ShioajiTickStk>().Single();

        Assert.AreEqual("2330", tick.Code);
        Assert.AreEqual(1002m, tick.Close);
        Assert.IsGreaterThanOrEqualTo(before, tick.ReceivedAt, "ReceivedAt is the local arrival time");
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 1, 0, 1, TimeSpan.Zero).AddTicks(2_345_670), tick.Time, "Time is the exchange's, converted from Taipei");
    }

    [TestMethod]
    public async Task AnUnsubscribedTick_IsReplacedByAHeartbeat_LikeTheRealServer()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat),
            () => server.PublishAsync("tick_stk", Fixture.Read("tick_stk.json"), "2330"));

        var events = items.Select(i => i.GetValueOrThrow()).ToList();

        Assert.IsFalse(events.OfType<ShioajiTickStk>().Any(), "no subscription means no data");

        var heartbeat = events.OfType<ShioajiHeartbeat>().Single();

        Assert.AreEqual("heartbeat", heartbeat.EventName);
    }

    [TestMethod]
    public async Task AContinuousMonthSubscriptionWithoutATargetCode_YieldsOnlyHeartbeats()
    {
        // 這條證明替身重現了文件 §4 說的陷阱:連續月不帶 target_code 時 server 回 200,卻什麼資料都不給。
        // This reproduces the trap §4 describes: a continuous-month subscription without target_code earns a 200 and
        // then no data at all.
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat),
            async () =>
            {
                // 繞過本地驗證,直接用低階 client 送出會踩到陷阱的訂閱。
                // Bypasses the local validation to send the trap-triggering subscription through the low-level client.
                await host.Api.PostAsync(
                    "/api/v1/stream/subscribe",
                    new { SecurityType = "FUT", Exchange = "TAIFEX", Code = "TXFR1", QuoteType = "Tick" });
                await server.PublishAsync("tick_fop", Fixture.Read("tick_fop.json"), "TXFR1");
            });

        Assert.IsFalse(items.Select(i => i.GetValueOrThrow()).OfType<ShioajiTickFop>().Any());
    }

    [TestMethod]
    public async Task OrderEvents_NeedTheTradeSubscription()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiOrderEvent),
            async () =>
            {
                await server.PublishAsync("order_event", Fixture.Read("order_event_order.json"));
                await host.Auth.SubscribeTradeAsync(StockAccount);
                await server.PublishAsync("order_event", Fixture.Read("order_event_order.json"));
            });

        var events = items.Select(i => i.GetValueOrThrow()).ToList();

        Assert.IsTrue(events.OfType<ShioajiHeartbeat>().Any(), "before subscribe_trade the order stream carries only a heartbeat");
        Assert.AreEqual("evt-0001", events.OfType<ShioajiOrderEvent>().Single().EventId);
    }

    [TestMethod]
    public async Task AUnknownEventName_IsPassedThrough()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiUnknownEvent),
            () => server.PublishAsync("kbar", """{"code":"2330"}"""));

        var unknown = items.Select(i => i.GetValueOrThrow()).OfType<ShioajiUnknownEvent>().Single();

        Assert.AreEqual("kbar", unknown.EventName);
        Assert.AreEqual("""{"code":"2330"}""", unknown.RawData);
    }

    [TestMethod]
    public async Task AMalformedEvent_IsAFailureOnTheStream_AndTheStreamCarriesOn()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat),
            async () =>
            {
                var connection = server.OpenConnections[0];
                await connection.SendEventAsync("tick_stk", "oops");
                await connection.SendHeartbeatAsync();
            });

        var failure = items.Single(i => i.IsFailure);

        Assert.AreEqual(ShioajiErrorCodes.StreamMalformedEvent, failure.Error!.Code);
        Assert.IsTrue(items.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat));
    }

    [TestMethod]
    public async Task ADroppedConnection_ReconnectsReplaysTheSubscriptions_AndEmitsTheLifecycle()
    {
        using var server = new FakeShioajiHttpServer();
        var replayed = 0;
        using var host = ShioajiTestHost.Create(server, services: services =>
            services.AddSingleton<Func<CancellationToken, Task<Result>>>(provider => async cancellationToken =>
            {
                Interlocked.Increment(ref replayed);

                var quotes = provider.GetRequiredService<ShioajiQuoteClient>();
                var auth = provider.GetRequiredService<ShioajiAuthClient>();

                await auth.SubscribeTradeAsync(StockAccount, cancellationToken);

                return await quotes.ReplaySubscriptionsAsync(cancellationToken);
            }));

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiTickStk),
            async () =>
            {
                await host.Quotes.SubscribeAsync(Tsmc);

                // 模擬 server 重啟:連線斷了,而且訂閱全部消失。
                // Simulates a server restart: the connection drops and every subscription is lost.
                server.ForgetSubscriptions();
                server.DropConnections();

                Assert.IsTrue(await server.WaitForConnectionsAsync(2, Patience), "the reader must reconnect");

                // 重放之後 server 又認得訂閱了,所以這一筆能送到。
                // After the replay the server knows the subscription again, so this one gets through.
                await Task.Delay(300);
                await server.PublishAsync("tick_stk", Fixture.Read("tick_stk.json"), "2330");
            });

        var lifecycle = Lifecycle(items).ToList();

        Assert.AreEqual(
            "Connected,Disconnected,Reconnecting,Connected",
            string.Join(',', lifecycle.Select(e => e.Kind)));
        Assert.AreEqual("0,0,1,1", string.Join(',', lifecycle.Select(e => e.Attempt)));
        Assert.IsNotNull(lifecycle[1].Reason);
        Assert.IsNotNull(lifecycle[2].Reason);
        Assert.IsNull(lifecycle[3].Reason);
        Assert.AreEqual(1, Volatile.Read(ref replayed), "the replay callback runs once per reconnection");
        Assert.AreEqual(1, host.Stream.Status.Reconnects);
        Assert.IsTrue(items.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiTickStk), "data flows again after the replay");
    }

    [TestMethod]
    public async Task AFailingReplayCallback_IsLoggedAndTheStreamKeepsGoing()
    {
        using var server = new FakeShioajiHttpServer();
        var logger = new CapturingLogger<ShioajiStreamReader>();
        using var host = ShioajiTestHost.Create(server, services: services =>
        {
            services.AddSingleton<ILogger<ShioajiStreamReader>>(logger);
            services.AddSingleton<Func<CancellationToken, Task<Result>>>(_ =>
                _ => Task.FromResult<Result>(new Error("test.replay", "replay refused", ErrorCategory.Unavailable)));
        });

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat) && Lifecycle(list).Count(e => e.Kind == ShioajiStreamLifecycleKind.Connected) >= 2,
            async () =>
            {
                server.DropConnections();
                await server.WaitForConnectionsAsync(2, Patience);
                await Task.Delay(200);
                await server.PublishHeartbeatAsync();
            });

        Assert.IsTrue(Lifecycle(items).Count(e => e.Kind == ShioajiStreamLifecycleKind.Connected) >= 2);
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("replay refused", StringComparison.Ordinal)), logger.AllText);
    }

    [TestMethod]
    public async Task AReplayCallbackThatThrows_IsLoggedAndTheStreamKeepsGoing()
    {
        using var server = new FakeShioajiHttpServer();
        var logger = new CapturingLogger<ShioajiStreamReader>();
        using var host = ShioajiTestHost.Create(server, services: services =>
        {
            services.AddSingleton<ILogger<ShioajiStreamReader>>(logger);
            services.AddSingleton<Func<CancellationToken, Task<Result>>>(_ =>
                _ => throw new InvalidOperationException("replay exploded"));
        });

        var items = await CollectAsync(
            host.Stream,
            list => Lifecycle(list).Count(e => e.Kind == ShioajiStreamLifecycleKind.Connected) >= 2,
            () =>
            {
                server.DropConnections();
                return Task.CompletedTask;
            });

        Assert.IsTrue(Lifecycle(items).Count(e => e.Kind == ShioajiStreamLifecycleKind.Connected) >= 2);
        Assert.IsTrue(logger.Entries.Any(e => e.Message.Contains("replay exploded", StringComparison.Ordinal)), logger.AllText);
    }

    [TestMethod]
    public async Task ASilentConnection_IsDeclaredDeadAfterTheIdleTimeout_AndReconnected()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o => o.StreamIdleTimeout = TimeSpan.FromMilliseconds(400));

        var items = await CollectAsync(
            host.Stream,
            list => Lifecycle(list).Count(e => e.Kind == ShioajiStreamLifecycleKind.Connected) >= 2);

        var lifecycle = Lifecycle(items).ToList();
        var disconnected = lifecycle.First(e => e.Kind == ShioajiStreamLifecycleKind.Disconnected);

        Assert.Contains("idle", disconnected.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.IsGreaterThanOrEqualTo(2, server.SseConnectionTotal);
    }

    [TestMethod]
    public async Task ASteadyHeartbeat_KeepsAConnectionAlivePastTheIdleTimeout()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o => o.StreamIdleTimeout = TimeSpan.FromMilliseconds(500));

        var items = await CollectAsync(
            host.Stream,
            list => list.Count(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat) >= 6,
            async () =>
            {
                for (var i = 0; i < 6; i++)
                {
                    await Task.Delay(200);
                    await server.PublishHeartbeatAsync();
                }
            });

        Assert.AreEqual(1, server.SseConnectionTotal, "heartbeats every 200 ms must keep a 500 ms idle timeout from firing");
        Assert.IsFalse(Lifecycle(items).Any(e => e.Kind == ShioajiStreamLifecycleKind.Disconnected));
    }

    [TestMethod]
    public async Task CommentStyleHeartbeats_AlsoKeepTheConnectionAlive()
    {
        using var server = new FakeShioajiHttpServer { HeartbeatStyle = FakeHeartbeatStyle.Comment };
        var logger = new CapturingLogger<ShioajiStreamReader>();
        using var host = ShioajiTestHost.Create(server, o => o.StreamIdleTimeout = TimeSpan.FromMilliseconds(500), services =>
            services.AddSingleton<ILogger<ShioajiStreamReader>>(logger));

        var items = await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiUnknownEvent),
            async () =>
            {
                for (var i = 0; i < 6; i++)
                {
                    await Task.Delay(200);
                    await server.PublishHeartbeatAsync();
                }

                await server.PublishAsync("kbar", "{}");
            });

        Assert.AreEqual(1, server.SseConnectionTotal, "comment heartbeats every 200 ms must keep a 500 ms idle timeout from firing");
        Assert.IsFalse(Lifecycle(items).Any(e => e.Kind == ShioajiStreamLifecycleKind.Disconnected));
        Assert.IsFalse(items.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat), "an SSE comment is not an event");
    }

    [TestMethod]
    public async Task EachUnknownFormatKind_IsLoggedOnce()
    {
        using var server = new FakeShioajiHttpServer { HeartbeatStyle = FakeHeartbeatStyle.Comment };
        var logger = new CapturingLogger<ShioajiStreamReader>();
        using var host = ShioajiTestHost.Create(server, services: services => services.AddSingleton<ILogger<ShioajiStreamReader>>(logger));

        var items = await CollectAsync(
            host.Stream,
            list => list.Count(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat) >= 2,
            async () =>
            {
                var connection = server.OpenConnections[0];

                await server.PublishHeartbeatAsync();
                await server.PublishHeartbeatAsync();
                await connection.WriteRawAsync("payload:{}\n\n");
                await connection.WriteRawAsync("payload:{}\n\n");
                await connection.WriteRawAsync("event:heartbeat\ndata:one\n\n");
                await connection.WriteRawAsync("event:heartbeat\ndata:two\n\n");
            });

        // 註解心跳不產生事件;之後兩個具名心跳才產生。
        // Comment heartbeats yield no event; the two named heartbeats afterwards do.
        Assert.IsTrue(items.Count(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat) >= 2);

        var text = logger.Entries.Select(e => e.Message).ToList();

        Assert.AreEqual(1, text.Count(m => m.Contains("kind", StringComparison.Ordinal) && m.Contains("comment", StringComparison.Ordinal)), logger.AllText);
        Assert.AreEqual(1, text.Count(m => m.Contains("unknown-field:payload", StringComparison.Ordinal)), logger.AllText);
        Assert.AreEqual(1, text.Count(m => m.Contains("data=one", StringComparison.Ordinal)), "the first heartbeat's original text is logged to help measure the format");
        Assert.AreEqual(0, text.Count(m => m.Contains("data=two", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ASlowConsumer_LosesTheOldestEvents_AndTheDropsAreCounted()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o => o.StreamChannelCapacity = 5);
        using var cancellation = new CancellationTokenSource(Patience);

        await host.Quotes.SubscribeAsync(Tsmc);

        await using var enumerator = host.Stream.ReadAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Assert.IsTrue(await enumerator.MoveNextAsync(), "the Connected event");

        for (var i = 1; i <= 60; i++)
        {
            var json = Fixture.Read("tick_stk.json").Replace("\"close\":1002", $"\"close\":{1000 + i}", StringComparison.Ordinal);
            await server.PublishAsync("tick_stk", json, "2330");
        }

        var deadline = DateTime.UtcNow + Patience;

        while (host.Stream.DroppedEvents < 55 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.IsGreaterThanOrEqualTo(55, host.Stream.DroppedEvents);
        Assert.AreEqual(host.Stream.DroppedEvents, host.Stream.Status.DroppedEvents);

        var kept = new List<ShioajiTickStk>();

        while (kept.Count < 5 && await enumerator.MoveNextAsync())
        {
            if (enumerator.Current.IsSuccess && enumerator.Current.GetValueOrThrow() is ShioajiTickStk tick)
            {
                kept.Add(tick);
            }
        }

        Assert.AreEqual(5, kept.Count);
        Assert.AreEqual(1060m, kept[^1].Close, "the newest event survives; the oldest are the ones dropped");
    }

    [TestMethod]
    public async Task ASecondConcurrentReader_GetsAlreadyReading_ThenEnds_AndTheFirstIsUnaffected()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        using var cancellation = new CancellationTokenSource(Patience);

        await using var first = host.Stream.ReadAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Assert.IsTrue(await first.MoveNextAsync());

        var second = new List<Result<ShioajiStreamEvent>>();

        await foreach (var item in host.Stream.ReadAsync(cancellation.Token))
        {
            second.Add(item);
        }

        Assert.HasCount(1, second);
        Assert.AreEqual(ShioajiErrorCodes.StreamAlreadyReading, second[0].Error!.Code);
        Assert.AreEqual(1, server.SseConnectionTotal, "a second consumer must not open a second connection");

        await server.PublishHeartbeatAsync();
        Assert.IsTrue(await first.MoveNextAsync());
    }

    [TestMethod]
    public async Task Cancelling_EndsTheEnumerationQuietly_AndTheReaderCanBeUsedAgain()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        using (var first = new CancellationTokenSource())
        {
            var count = 0;

            await foreach (var item in host.Stream.ReadAsync(first.Token))
            {
                count++;
                Assert.IsTrue(item.IsSuccess);

                await first.CancelAsync();
            }

            Assert.AreEqual(1, count);
        }

        Assert.AreEqual(ShioajiStreamReaderState.Stopped, host.Stream.Status.State);

        var again = await CollectAsync(host.Stream, list => list.Count >= 1);

        Assert.HasCount(1, again);
        Assert.AreEqual(2, server.SseConnectionTotal);
    }

    [TestMethod]
    public async Task Status_ReflectsTheConnectionAndTheLastEvent()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(ShioajiStreamReaderState.Stopped, host.Stream.Status.State);
        Assert.IsNull(host.Stream.Status.ConnectedAt);
        Assert.IsNull(host.Stream.Status.LastEventAt);

        await CollectAsync(
            host.Stream,
            list => list.Any(i => i.IsSuccess && i.GetValueOrThrow() is ShioajiHeartbeat),
            async () =>
            {
                await server.PublishHeartbeatAsync();
                await Task.Delay(100);
            });

        var during = host.Stream.Status;

        Assert.AreEqual(ShioajiStreamReaderState.Stopped, during.State, "after the enumeration ends the reader is stopped");
        Assert.IsNotNull(during.LastEventAt);
        Assert.AreEqual(0, during.Reconnects);
    }

    [TestMethod]
    public async Task TheStatusSnapshot_IsConnectedWhileReading()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        using var cancellation = new CancellationTokenSource(Patience);

        await using var enumerator = host.Stream.ReadAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Assert.IsTrue(await enumerator.MoveNextAsync());

        var status = host.Stream.Status;

        Assert.AreEqual(ShioajiStreamReaderState.Connected, status.State);
        Assert.IsNotNull(status.ConnectedAt);
    }

    [TestMethod]
    public async Task TheBearerHeader_IsSentOnTheStreamToo_AndAnAuthFailureBecomesReconnecting()
    {
        using var server = new FakeShioajiHttpServer();
        server.RequireBearer("stream-key-123:stream-secret-456");

        using (var allowed = ShioajiTestHost.Create(server, o =>
        {
            o.UseBearerAuth = true;
            o.ApiKey = "stream-key-123";
            o.SecretKey = "stream-secret-456";
        }))
        {
            var items = await CollectAsync(allowed.Stream, list => list.Count >= 1);

            Assert.AreEqual(ShioajiStreamLifecycleKind.Connected, ((ShioajiStreamLifecycleEvent)items[0].GetValueOrThrow()).Kind);
            Assert.AreEqual("Bearer stream-key-123:stream-secret-456", server.Requests[^1].Headers["Authorization"]);
        }

        using var denied = ShioajiTestHost.Create(server);
        var refused = await CollectAsync(denied.Stream, list => list.Count >= 1);

        var lifecycle = (ShioajiStreamLifecycleEvent)refused[0].GetValueOrThrow();

        Assert.AreEqual(ShioajiStreamLifecycleKind.Reconnecting, lifecycle.Kind);
        Assert.AreEqual("HTTP 401", lifecycle.Reason);
    }

    [TestMethod]
    public async Task ANeverReachableServer_KeepsRetryingWithGrowingAttemptNumbers()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.BaseUri = new Uri($"http://127.0.0.1:{FakeShioajiHttpServer.FindFreePort()}/");
            o.RequestTimeout = TimeSpan.FromMilliseconds(400);
        });

        var items = await CollectAsync(host.Stream, list => Lifecycle(list).Count(e => e.Kind == ShioajiStreamLifecycleKind.Reconnecting) >= 3);

        var reconnecting = Lifecycle(items).Where(e => e.Kind == ShioajiStreamLifecycleKind.Reconnecting).ToList();

        Assert.AreEqual("1,2,3", string.Join(',', reconnecting.Select(e => e.Attempt).Take(3)));
        Assert.IsFalse(Lifecycle(items).Any(e => e.Kind == ShioajiStreamLifecycleKind.Connected));
        Assert.AreEqual(0, host.Stream.Status.Reconnects);
    }

    [TestMethod]
    public void Constructor_ValidatesItsArguments()
    {
        using var http = new HttpClient();

        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiStreamReader(null!, new ShioajiOptions()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiStreamReader(http, null!));
        Assert.ThrowsExactly<ArgumentException>(() => new ShioajiStreamReader(http, new ShioajiOptions { StreamChannelCapacity = 0 }));
    }
}
