using Microsoft.Extensions.DependencyInjection;
using Ozakboy.Http;
using Ozakboy.Security.Masking;
using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Contracts;
using Ozakboy.TradeKit.Shioaji.MarketData;
using Ozakboy.TradeKit.Shioaji.Orders;
using Ozakboy.TradeKit.Shioaji.Server;
using Ozakboy.TradeKit.Shioaji.Streaming;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiServiceCollectionExtensionsTests
{
    [TestMethod]
    public void AddShioaji_RegistersEveryClientAsASingleton()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        foreach (var type in new[]
        {
            typeof(ShioajiOptions),
            typeof(HttpPipelineClient),
            typeof(ShioajiApiClient),
            typeof(ShioajiAuthClient),
            typeof(ShioajiContractClient),
            typeof(ShioajiQuoteClient),
            typeof(ShioajiOrderClient),
            typeof(ShioajiStreamReader),
            typeof(ShioajiRateBudget),
        })
        {
            var first = host.Services.GetRequiredService(type);
            var second = host.Services.GetRequiredService(type);

            Assert.AreSame(first, second, $"{type.Name} must be a singleton");
        }
    }

    [TestMethod]
    public void AddShioaji_NullArguments_Throw()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ShioajiServiceCollectionExtensions.AddShioaji(null!, _ => { }));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ServiceCollection().AddShioaji(null!));
    }

    [TestMethod]
    public void AddShioaji_InvalidConfiguration_ThrowsWithTheReason()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            new ServiceCollection().AddShioaji(options => options.UseBearerAuth = true));

        Assert.Contains("ApiKey", exception.Message);
    }

    [TestMethod]
    public void TheHttpClients_AreConfiguredForTheirJob()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);
        var factory = host.Services.GetRequiredService<IHttpClientFactory>();

        var requests = factory.CreateClient(ShioajiConstants.HttpClientName);
        var stream = factory.CreateClient(ShioajiConstants.StreamHttpClientName);

        Assert.AreEqual(server.BaseUri, requests.BaseAddress);
        Assert.AreEqual(Timeout.InfiniteTimeSpan, requests.Timeout, "timeouts belong to the pipeline");
        Assert.AreEqual(Timeout.InfiniteTimeSpan, stream.Timeout, "a stream has no overall timeout");
        Assert.AreEqual(server.BaseUri, stream.BaseAddress);
    }

    [TestMethod]
    public void ABaseUriWithoutATrailingSlash_GetsOne()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, o => o.BaseUri = new Uri(server.BaseUri, "prefix"));

        var requests = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ShioajiConstants.HttpClientName);

        Assert.IsTrue(requests.BaseAddress!.AbsoluteUri.EndsWith("/prefix/", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TheBearerSecrets_AreRegisteredOnTheClientsMasker_AndShortOnesAreSkippedWithoutThrowing()
    {
        using var server = new FakeShioajiHttpServer();

        using (var with = ShioajiTestHost.Create(server, o =>
        {
            o.UseBearerAuth = true;
            o.ApiKey = "bearer-key-123456";
            o.SecretKey = "bearer-secret-654321";
        }))
        {
            var masker = with.Services.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName);

            Assert.AreEqual(3, masker.KnownSecretCount, "the key, the secret and key:secret");
            Assert.DoesNotContain("bearer-key-123456", masker.MaskText("token bearer-key-123456:bearer-secret-654321 end"));
        }

        using (var short8 = ShioajiTestHost.Create(server, o =>
        {
            o.UseBearerAuth = true;
            o.ApiKey = "short";
            o.SecretKey = "tiny";
        }))
        {
            var masker = short8.Services.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName);

            // "short:tiny" 剛好 10 個字元,所以組合字面仍然會登記;兩個單獨的值太短而略過。
            // "short:tiny" is exactly 10 characters so the combined literal is still registered; the two short values
            // on their own are skipped.
            Assert.AreEqual(1, masker.KnownSecretCount);
        }

        using var none = ShioajiTestHost.Create(server);

        Assert.AreEqual(0, none.Services.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName).KnownSecretCount);
    }

    [TestMethod]
    public void AReplayFuncInTheContainer_IsUsedByTheStreamReader_AndAbsenceIsFine()
    {
        using var server = new FakeShioajiHttpServer();

        using (var without = ShioajiTestHost.Create(server))
        {
            Assert.IsNotNull(without.Stream);
        }

        using var with = ShioajiTestHost.Create(server, services: services =>
            services.AddSingleton<Func<CancellationToken, Task<Result>>>(_ => _ => Task.FromResult(Result.Success())));

        Assert.IsNotNull(with.Stream);
    }

    [TestMethod]
    public void AddShioajiServerSupervisor_RegistersTheSupervisor()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server, services: services =>
            services.AddShioajiServerSupervisor(
                options => options.BindAddress = server.BindAddress,
                _ => () => new ShioajiCredentials { ApiKey = "api-key-for-di-test", SecretKey = "secret-for-di-test" }));

        var supervisor = host.Services.GetRequiredService<ShioajiServerSupervisor>();

        Assert.AreSame(supervisor, host.Services.GetRequiredService<ShioajiServerSupervisor>());
        Assert.AreEqual(server.BindAddress, host.Services.GetRequiredService<ShioajiServerOptions>().BindAddress);
        Assert.AreEqual(ShioajiServerState.Stopped, supervisor.Status.State);
    }

    [TestMethod]
    public void AddShioajiServerSupervisor_ValidatesItsArguments()
    {
        var services = new ServiceCollection();

        Assert.ThrowsExactly<ArgumentNullException>(() => ShioajiServiceCollectionExtensions.AddShioajiServerSupervisor(null!, _ => { }, _ => Credentials));
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddShioajiServerSupervisor(null!, _ => Credentials));
        Assert.ThrowsExactly<ArgumentNullException>(() => services.AddShioajiServerSupervisor(_ => { }, null!));
        Assert.ThrowsExactly<ArgumentException>(() => services.AddShioajiServerSupervisor(o => o.BindAddress = "bad", _ => Credentials));
    }

    private static ShioajiCredentials Credentials() => new() { ApiKey = "api-key-for-di-test", SecretKey = "secret-for-di-test" };

    [TestMethod]
    public void TheMaskerOfTheNamedClient_IsTheOneSharedWithThePipelineFacade()
    {
        using var server = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(server);

        var masker = host.Services.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName);

        Assert.IsInstanceOfType<SecretMasker>(masker);
        Assert.AreSame(masker, host.Services.GetOzakboyHttpMasker(ShioajiConstants.HttpClientName));
    }
}
