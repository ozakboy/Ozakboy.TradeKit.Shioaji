using Microsoft.Extensions.DependencyInjection;
using Ozakboy.Http;
using Ozakboy.Security.Masking;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiApiClientTests
{
    private const string Key = "test-api-key-123456";
    private const string Secret = "test-secret-key-7890";

    private sealed class Sample
    {
        public decimal Price { get; init; }

        public decimal Quantity { get; init; }

        public string? Name { get; init; }

        public long? LargeNumber { get; init; }
    }

    [TestMethod]
    public async Task Get_DeserializesSnakeCaseAndReadsDecimalsFromNumbersAndStrings()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/sample", 200, """{"price":29.8,"quantity":"3.5","name":"x","large_number":"12345678901"}""");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Api.GetAsync<Sample>("/api/v1/sample");

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
        Assert.AreEqual(29.8m, result.GetValueOrThrow().Price);
        Assert.AreEqual(3.5m, result.GetValueOrThrow().Quantity);
        Assert.AreEqual("x", result.GetValueOrThrow().Name);
        Assert.AreEqual(12_345_678_901L, result.GetValueOrThrow().LargeNumber);
    }

    [TestMethod]
    public async Task Post_SerializesSnakeCaseSkipsNullsAndKeepsDecimalText()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/sample", 200, """{"price":1,"quantity":2}""");
        using var host = ShioajiTestHost.Create(server);

        var typed = await host.Api.PostAsync<Sample>("/api/v1/sample", new { TradeId = "abc", Price = 29.8m, Skipped = (string?)null });
        var plain = await host.Api.PostAsync("/api/v1/sample", new { Price = 0.1m });

        Assert.IsTrue(typed.IsSuccess);
        Assert.IsTrue(plain.IsSuccess);
        Assert.AreEqual("""{"trade_id":"abc","price":29.8}""", server.Requests[0].Body);
        Assert.AreEqual("""{"price":0.1}""", server.Requests[1].Body);
        Assert.AreEqual("application/json", server.Requests[0].Headers["Content-Type"].Split(';')[0]);
    }

    [TestMethod]
    public async Task Post_WithoutABodySendsNone()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/empty", 200, "{}");
        using var host = ShioajiTestHost.Create(server);

        Assert.IsTrue((await host.Api.PostAsync("/api/v1/empty", null)).IsSuccess);
        Assert.AreEqual(string.Empty, server.Requests[0].Body);
    }

    [TestMethod]
    [DataRow(400, ErrorCategory.Validation, false)]
    [DataRow(401, ErrorCategory.Unauthorized, false)]
    [DataRow(403, ErrorCategory.Forbidden, false)]
    [DataRow(404, ErrorCategory.NotFound, false)]
    [DataRow(409, ErrorCategory.Conflict, false)]
    [DataRow(422, ErrorCategory.Validation, false)]
    [DataRow(429, ErrorCategory.RateLimited, true)]
    [DataRow(500, ErrorCategory.Unavailable, true)]
    [DataRow(503, ErrorCategory.Unavailable, true)]
    public async Task NonSuccessStatus_BecomesAShioajiHttpErrorWithTheStatusAndBody(int status, ErrorCategory category, bool transient)
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/fail", status, """{"detail":"something specific went wrong"}""");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Api.PostAsync("/api/v1/fail", new { A = 1 });

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual($"shioaji.http.{status}", result.Error.Code);
        Assert.AreEqual(ShioajiErrorCodes.ForHttpStatus(status), result.Error.Code);
        Assert.AreEqual(category, result.Error.Category);
        Assert.AreEqual(transient, result.Error.IsTransient);
        Assert.AreEqual(status.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Error.Data![ShioajiErrorDataKeys.StatusCode]);
        Assert.AreEqual("""{"detail":"something specific went wrong"}""", result.Error.Data[ShioajiErrorDataKeys.Body]);
        Assert.Contains("something specific went wrong", result.Error.Message);
        Assert.AreEqual("/api/v1/fail", result.Error.Data[ShioajiErrorDataKeys.Path]);
    }

    [TestMethod]
    public async Task ErrorBody_IsTruncatedTo2KilobytesAndANonJsonBodyStillWorks()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/big", 400, new string('x', 10_000));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Api.PostAsync("/api/v1/big", null);

        Assert.AreEqual(2048, result.Error!.Data![ShioajiErrorDataKeys.Body].Length);
        Assert.DoesNotContain("detail", result.Error.Message);
    }

    [TestMethod]
    public async Task EmptyErrorBody_LeavesNoBodyKey()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/none", 400, string.Empty);
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Api.PostAsync("/api/v1/none", null);

        Assert.IsFalse(result.Error!.Data!.ContainsKey(ShioajiErrorDataKeys.Body));
    }

    [TestMethod]
    public async Task ServerDown_IsServerUnreachable_AndTransient()
    {
        using var server = new FakeShioajiHttpServer();

        // Windows 上連到沒人聽的 loopback port 要將近 2 秒才會被拒絕,所以逾時要放寬,也用 POST 免得被重試三次。
        // On Windows a connection to a loopback port nobody listens on takes nearly 2 seconds to be refused, so the
        // timeout is widened, and POST is used so it is not retried three times.
        using var host = ShioajiTestHost.Create(server, options =>
        {
            options.BaseUri = new Uri($"http://127.0.0.1:{FakeShioajiHttpServer.FindFreePort()}/");
            options.RequestTimeout = TimeSpan.FromSeconds(8);
        });

        var result = await host.Api.PostAsync("/api/v1/info", null);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.ServerUnreachable, result.Error.Code);
        Assert.IsTrue(result.Error.IsTransient);
        Assert.AreEqual("http.network", result.Error.Data![ShioajiErrorDataKeys.PipelineCode]);
    }

    [TestMethod]
    public async Task SlowServer_IsRequestTimeout_AndTransient()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/slow", _ =>
        {
            Thread.Sleep(1500);
            return new FakeResponse(200, "{}");
        });
        using var host = ShioajiTestHost.Create(server, options => options.RequestTimeout = TimeSpan.FromMilliseconds(300));

        var result = await host.Api.PostAsync("/api/v1/slow", null);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.RequestTimeout, result.Error.Code);
        Assert.IsTrue(result.Error.IsTransient);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/slow"), "a timed out POST must not be repeated");
    }

    [TestMethod]
    public async Task Post_IsNeverRetried_EvenOnA503()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/order", 503, "{}");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Api.PostAsync("/api/v1/order", new { A = 1 });

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(1, server.Count("POST", "/api/v1/order"));
    }

    [TestMethod]
    public async Task Get_IsRetriedOnA503()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/flaky", 503, "{}");
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Api.GetAsync<Sample>("/api/v1/flaky");

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.ForHttpStatus(503), result.Error.Code);
        Assert.AreEqual(3, server.Count("GET", "/api/v1/flaky"), "the pipeline's default policy retries an idempotent GET");
    }

    [TestMethod]
    public async Task Get_RecoversWhenALaterAttemptSucceeds()
    {
        using var server = new FakeShioajiHttpServer();
        var calls = 0;
        server.Map("GET", "/api/v1/recover", _ => Interlocked.Increment(ref calls) < 2
            ? new FakeResponse(503, "{}")
            : new FakeResponse(200, """{"price":1,"quantity":2}"""));
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Api.GetAsync<Sample>("/api/v1/recover");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2, server.Count("GET", "/api/v1/recover"));
    }

    [TestMethod]
    public async Task BearerHeader_IsSentWhenEnabled_AndAbsentWhenNot()
    {
        using var server = new FakeShioajiHttpServer();
        server.RequireBearer($"{Key}:{Secret}");

        using (var with = ShioajiTestHost.Create(server, o =>
        {
            o.UseBearerAuth = true;
            o.ApiKey = Key;
            o.SecretKey = Secret;
        }))
        {
            var ok = await with.Auth.GetInfoAsync();

            Assert.IsTrue(ok.IsSuccess, ok.Error?.ToString());
            Assert.AreEqual($"Bearer {Key}:{Secret}", server.Requests[^1].Headers["Authorization"]);
        }

        using var without = ShioajiTestHost.Create(server);
        var denied = await without.Auth.GetInfoAsync();

        Assert.IsTrue(denied.IsFailure);
        Assert.AreEqual("shioaji.http.401", denied.Error.Code);
        Assert.AreEqual(ErrorCategory.Unauthorized, denied.Error.Category);
        Assert.IsFalse(server.Requests[^1].Headers.ContainsKey("Authorization"));
    }

    [TestMethod]
    public async Task TheBearerSecrets_AreMaskedEvenWhenTheServerEchoesThemBack()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("POST", "/api/v1/echo", request => new FakeResponse(
            500,
            $$"""{"detail":"rejected {{request.Headers["Authorization"]}} key={{Key}} secret={{Secret}}"}"""));
        using var host = ShioajiTestHost.Create(server, o =>
        {
            o.UseBearerAuth = true;
            o.ApiKey = Key;
            o.SecretKey = Secret;
        });

        var result = await host.Api.PostAsync("/api/v1/echo", new { A = 1 });

        Assert.IsTrue(result.IsFailure);

        var everything = new List<string> { result.Error.Message, result.Error.ToString() };
        everything.AddRange(result.Error.Data!.Values);

        foreach (var text in everything)
        {
            Assert.DoesNotContain(Key, text);
            Assert.DoesNotContain(Secret, text);
        }

        var mask = host.Services.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName).MaskSegment;
        Assert.Contains(mask, result.Error.Data[ShioajiErrorDataKeys.Body], "the body must show where the secret was");
        Assert.IsGreaterThanOrEqualTo(3, host.Services.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName).KnownSecretCount);
    }

    [TestMethod]
    public async Task BlankPath_IsRefusedLocally()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var result = await host.Api.GetAsync<Sample>(" ");

        Assert.AreEqual(ShioajiErrorCodes.InvalidArgument, result.Error!.Code);
        Assert.IsEmpty(server.Requests);
    }

    [TestMethod]
    public async Task UnreadableSuccessBodies_AreMalformedResponses()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/notjson", 200, "<html>");
        server.Map("GET", "/api/v1/empty", 200, string.Empty);
        server.Map("GET", "/api/v1/null", 200, "null");
        using var host = ShioajiTestHost.Create(server);

        foreach (var path in new[] { "/api/v1/notjson", "/api/v1/empty", "/api/v1/null" })
        {
            var result = await host.Api.GetAsync<Sample>(path);

            Assert.AreEqual(ShioajiErrorCodes.MalformedResponse, result.Error!.Code, path);
            Assert.IsFalse(result.Error.IsTransient, "re-fetching an unreadable answer returns the same answer");
        }
    }

    [TestMethod]
    public async Task CancellationByTheCaller_IsRequestCancelled()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/api/v1/slow", _ =>
        {
            Thread.Sleep(1000);
            return new FakeResponse(200, "{}");
        });
        using var host = ShioajiTestHost.Create(server);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var result = await host.Api.GetAsync<Sample>("/api/v1/slow", cancellation.Token);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.RequestCancelled, result.Error.Code);
        Assert.AreEqual(ErrorCategory.Cancelled, result.Error.Category);
    }

    [TestMethod]
    public void Constructor_RejectsNullPipeline()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiApiClient(null!, SecretMasker.Default));
    }

    [TestMethod]
    public async Task APathPrefixOnTheBaseAddress_Survives()
    {
        using var server = new FakeShioajiHttpServer();
        server.Map("GET", "/proxy/api/v1/sample", 200, """{"price":1,"quantity":1}""");
        using var host = ShioajiTestHost.Create(server, o => o.BaseUri = new Uri(server.BaseUri, "proxy"));

        var result = await host.Api.GetAsync<Sample>("/api/v1/sample");

        Assert.IsTrue(result.IsSuccess, result.Error?.ToString());
    }
}
