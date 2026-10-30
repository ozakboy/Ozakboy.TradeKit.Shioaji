using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiTrafficMeterTests
{
    private static FakeTimeProvider ClockAt(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));

    private sealed class SettableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ShioajiUsage Usage(long bytes, long limit, long remaining) =>
        new() { Connections = 1, Bytes = bytes, LimitBytes = limit, RemainingBytes = remaining };

    [TestMethod]
    public void Record_AccumulatesBytesAndRequests()
    {
        var meter = new ShioajiTrafficMeter(ClockAt(2026, 10, 30, 3, 0));

        Assert.AreEqual(0L, meter.TodayBytes);
        Assert.AreEqual(0L, meter.TodayRequests);

        meter.Record(100);
        meter.Record(250);

        Assert.AreEqual(350L, meter.TodayBytes);
        Assert.AreEqual(2L, meter.TodayRequests);
    }

    [TestMethod]
    public void Record_ZeroAndNegativeBytes_CountAsARequestButAddNoTraffic()
    {
        var meter = new ShioajiTrafficMeter(ClockAt(2026, 10, 30, 3, 0));

        meter.Record(0);
        meter.Record(-5);
        meter.Record(10);

        Assert.AreEqual(10L, meter.TodayBytes);
        Assert.AreEqual(3L, meter.TodayRequests);
    }

    [TestMethod]
    public void TheDayResetsAtEightInTheMorningTaipei_WhichIsMidnightUtc()
    {
        // 台北 2026-10-30 07:59:30 = UTC 2026-10-29 23:59:30:還在 10-29 的流量日。
        // Taipei 07:59:30 on 2026-10-30 is UTC 23:59:30 on 10-29: still the 10-29 traffic day.
        var clock = ClockAt(2026, 10, 29, 23, 59, 30);
        var meter = new ShioajiTrafficMeter(clock);

        meter.Record(100);
        Assert.AreEqual(new DateOnly(2026, 10, 29), meter.Snapshot().BucketDate);

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.AreEqual(100L, meter.TodayBytes, "one second before 08:00 Taipei the bucket is the same");

        clock.Advance(TimeSpan.FromSeconds(1));

        // 台北 08:00:00 整:新的一桶。
        // 08:00:00 sharp in Taipei: a new bucket.
        Assert.AreEqual(0L, meter.TodayBytes);
        Assert.AreEqual(0L, meter.TodayRequests);

        meter.Record(50);
        var snapshot = meter.Snapshot();

        Assert.AreEqual(new DateOnly(2026, 10, 30), snapshot.BucketDate);
        Assert.AreEqual(50L, snapshot.Bytes);
        Assert.AreEqual(1L, snapshot.Requests);
        Assert.HasCount(1, snapshot.History);
        Assert.AreEqual(new DateOnly(2026, 10, 29), snapshot.History[0].Date);
        Assert.AreEqual(100L, snapshot.History[0].Bytes);
        Assert.AreEqual(1L, snapshot.History[0].Requests);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), snapshot.NextResetAt, "08:00 Taipei on the 31st");
    }

    [TestMethod]
    public void AWholeTrafficDay_IsOneBucket()
    {
        var clock = ClockAt(2026, 10, 30, 0, 0);
        var meter = new ShioajiTrafficMeter(clock);

        meter.Record(1);
        clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59));
        meter.Record(2);

        Assert.AreEqual(3L, meter.TodayBytes, "00:00:00 to 23:59:59 UTC is 08:00 to 07:59:59 Taipei, one traffic day");
        Assert.IsEmpty(meter.Snapshot().History);
    }

    [TestMethod]
    public void Reset_ClearsTodayAndTheServerReport_ButKeepsTheHistory()
    {
        var clock = ClockAt(2026, 10, 29, 12, 0);
        var meter = new ShioajiTrafficMeter(clock);

        meter.Record(70);
        clock.Advance(TimeSpan.FromDays(1));
        meter.Record(30);
        meter.ReportServerUsage(Usage(500, 1000, 500));

        meter.Reset();

        Assert.AreEqual(0L, meter.TodayBytes);
        Assert.AreEqual(0L, meter.TodayRequests);
        Assert.IsNull(meter.ServerReportedBytes);
        Assert.HasCount(1, meter.Snapshot().History, "yesterday stays");
    }

    [TestMethod]
    public void ServerReportedBytes_ComesFromTheReport_AndDoesNotSurviveTheReset()
    {
        var clock = ClockAt(2026, 10, 29, 12, 0);
        var meter = new ShioajiTrafficMeter(clock);

        Assert.IsNull(meter.ServerReportedBytes);

        meter.ReportServerUsage(Usage(2048, 10_000, 7952));

        Assert.AreEqual(2048L, meter.ServerReportedBytes);

        var snapshot = meter.Snapshot();
        Assert.AreEqual(2048L, snapshot.ServerReportedBytes);
        Assert.AreEqual(10_000L, snapshot.ServerReportedLimitBytes);
        Assert.AreEqual(7952L, snapshot.ServerReportedRemainingBytes);
        Assert.AreEqual(clock.GetUtcNow(), snapshot.ServerReportedAt);

        clock.Advance(TimeSpan.FromDays(1));

        Assert.IsNull(meter.ServerReportedBytes, "yesterday's report is not today's");
        Assert.IsNull(meter.Snapshot().EstimatedRemainingBytes);
    }

    [TestMethod]
    public void EstimatedRemaining_IsTheReportedRemainderMinusWhatWasUsedSince()
    {
        var meter = new ShioajiTrafficMeter(ClockAt(2026, 10, 29, 12, 0));

        Assert.IsNull(meter.Snapshot().EstimatedRemainingBytes, "no report yet");

        meter.Record(40);
        meter.ReportServerUsage(Usage(500, 1000, 500));
        Assert.AreEqual(500L, meter.Snapshot().EstimatedRemainingBytes, "nothing used since the report");

        meter.Record(120);
        meter.Record(30);
        Assert.AreEqual(350L, meter.Snapshot().EstimatedRemainingBytes);

        meter.Record(1000);
        Assert.AreEqual(-650L, meter.Snapshot().EstimatedRemainingBytes, "the estimate can go negative");
    }

    [TestMethod]
    public void IsPossiblyExhausted_CannotBeRuledOutWithoutAReport_AndFollowsTheEstimateWithOne()
    {
        var meter = new ShioajiTrafficMeter(ClockAt(2026, 10, 29, 12, 0));

        Assert.IsTrue(meter.IsPossiblyExhausted(), "no report: unknown counts as possibly exhausted");

        meter.ReportServerUsage(Usage(0, 1000, 1000));
        Assert.IsFalse(meter.IsPossiblyExhausted());

        meter.Record(999);
        Assert.IsFalse(meter.IsPossiblyExhausted(), "one byte left");

        meter.Record(1);
        Assert.IsTrue(meter.IsPossiblyExhausted(), "nothing left");

        meter.ReportServerUsage(Usage(10, 1000, 990));
        Assert.IsFalse(meter.IsPossiblyExhausted(), "a fresh report replaces the estimate");
    }

    [TestMethod]
    public void History_KeepsAtMostSevenDays_NewestFirst_AndSkipsDaysWithNothing()
    {
        var clock = ClockAt(2026, 10, 1, 12, 0);
        var meter = new ShioajiTrafficMeter(clock);

        for (var day = 1; day <= 9; day++)
        {
            meter.Record(day);
            clock.Advance(TimeSpan.FromDays(1));
        }

        // 第 10 天什麼都沒記,跳到第 12 天。
        // Nothing is recorded on day 10, and the clock jumps to day 12.
        clock.Advance(TimeSpan.FromDays(2));
        meter.Record(99);

        var history = meter.Snapshot().History;

        Assert.HasCount(7, history);
        Assert.AreEqual(new DateOnly(2026, 10, 9), history[0].Date, "the newest recorded day comes first; the empty days leave no bucket");
        Assert.AreEqual(9L, history[0].Bytes);
        Assert.AreEqual(new DateOnly(2026, 10, 3), history[6].Date);
        Assert.AreEqual(99L, meter.TodayBytes);
    }

    [TestMethod]
    public void AClockThatStepsBackwards_DoesNotRollTheBucketBack()
    {
        // FakeTimeProvider 不允許倒退,所以用一個可以任意設定的時鐘。
        // FakeTimeProvider refuses to go backwards, so a clock that can be set freely is used.
        var clock = new SettableClock(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        var meter = new ShioajiTrafficMeter(clock);

        meter.Record(10);
        clock.Now -= TimeSpan.FromDays(2);
        meter.Record(5);

        Assert.AreEqual(15L, meter.TodayBytes);
        Assert.AreEqual(new DateOnly(2026, 10, 30), meter.Snapshot().BucketDate);
        Assert.IsEmpty(meter.Snapshot().History);
    }

    [TestMethod]
    public void TheDefaultTimeProvider_IsTheSystemClock()
    {
        var meter = new ShioajiTrafficMeter();

        meter.Record(1);

        Assert.AreEqual(1L, meter.TodayBytes);
        Assert.IsTrue(meter.Snapshot().NextResetAt > DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public async Task Record_IsThreadSafe()
    {
        var meter = new ShioajiTrafficMeter(ClockAt(2026, 10, 30, 3, 0));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                meter.Record(3);
            }
        })));

        Assert.AreEqual(8L * 2000 * 3, meter.TodayBytes);
        Assert.AreEqual(8L * 2000, meter.TodayRequests);
    }

    // ───────────────────────────── 接上 HTTP 管線 ─────────────────────────────

    [TestMethod]
    public async Task EveryResponse_IsRecorded_ByContentLength_NonSuccessIncluded()
    {
        using var server = new FakeShioajiHttpServer();
        var body = """{"price":29.8,"name":"台積電"}""";
        server.Map("GET", "/api/v1/sample", 200, body);
        server.Map("GET", "/api/v1/broken", 404, """{"detail":"boom"}""");
        using var host = ShioajiTestHost.Create(server);

        Assert.AreEqual(0L, host.Traffic.TodayBytes);

        Assert.IsTrue((await host.Api.GetStringAsync("/api/v1/sample", CancellationToken.None)).IsSuccess);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(body), host.Traffic.TodayBytes, "Chinese text is counted in bytes, not characters");
        Assert.AreEqual(1L, host.Traffic.TodayRequests);

        Assert.IsTrue((await host.Api.GetStringAsync("/api/v1/broken", CancellationToken.None)).IsFailure);
        Assert.AreEqual(Encoding.UTF8.GetByteCount(body) + Encoding.UTF8.GetByteCount("""{"detail":"boom"}"""), host.Traffic.TodayBytes, "a 404 answer is traffic too");
        Assert.AreEqual(2L, host.Traffic.TodayRequests);
    }

    [TestMethod]
    public async Task AChunkedResponse_WithNoContentLength_IsCountedByTheBytesRead()
    {
        using var server = new FakeShioajiHttpServer();
        var body = """{"name":"鴻海精密工業股份有限公司"}""";
        server.Map("GET", "/api/v1/chunked", _ => new FakeResponse(200, body, Chunked: true));
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.Api.GetStringAsync("/api/v1/chunked", CancellationToken.None)).IsSuccess);

        Assert.AreEqual(Encoding.UTF8.GetByteCount(body), host.Traffic.TodayBytes);
    }

    [TestMethod]
    public async Task APostResponse_IsRecordedToo_AndARefusedCallRecordsNothing()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/post", 200, "{}");
        using var host = ShioajiTestHost.Create(server);

        await host.Api.PostAsync("/api/v1/post", new { a = 1 });

        Assert.AreEqual(2L, host.Traffic.TodayBytes);
        Assert.AreEqual(1L, host.Traffic.TodayRequests);

        await host.Api.GetStringAsync(" ", CancellationToken.None);
        Assert.AreEqual(1L, host.Traffic.TodayRequests, "a blank path never leaves the process");
    }

    [TestMethod]
    public async Task GetUsage_WritesTheServersBytesIntoTheMeter()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        Assert.IsNull(host.Traffic.ServerReportedBytes);

        var usage = (await host.Auth.GetUsageAsync()).GetValueOrThrow();

        Assert.AreEqual(usage.Bytes, host.Traffic.ServerReportedBytes);
        Assert.AreEqual(1024L, host.Traffic.ServerReportedBytes);
        Assert.IsTrue(host.Traffic.TodayBytes > 0, "the usage response itself is traffic");
        Assert.AreEqual(usage.RemainingBytes, host.Traffic.Snapshot().ServerReportedRemainingBytes);
    }

    [TestMethod]
    public async Task GetUsage_AnUnreadableAnswer_WritesNothing()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/auth/usage", 200, """{"connections":1,"bytes":2}""");
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.Auth.GetUsageAsync()).IsFailure);
        Assert.IsNull(host.Traffic.ServerReportedBytes);

        server.Map("GET", "/api/v1/auth/usage", 500, "{}");
        Assert.IsTrue((await host.Auth.GetUsageAsync()).IsFailure);
        Assert.IsNull(host.Traffic.ServerReportedBytes);
    }

    [TestMethod]
    public async Task AnApiClientBuiltWithoutAMeter_MeasuresNothing_AndAnAuthClientWithoutOneStillWorks()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var api = new ShioajiApiClient(host.Services.GetRequiredService<Ozakboy.Http.HttpPipelineClient>());
        var auth = new ShioajiAuthClient(api, host.Budget);

        var usage = await auth.GetUsageAsync();

        Assert.IsTrue(usage.IsSuccess, usage.Error?.ToString());
        Assert.AreEqual(0L, host.Traffic.TodayBytes, "the meter of the other registration saw none of it");
    }
}
