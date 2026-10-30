using Ozakboy.TradeKit.Shioaji.Server;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiOptionsTests
{
    [TestMethod]
    public void Defaults_MatchTheSpecification()
    {
        var options = new ShioajiOptions();

        Assert.AreEqual(new Uri("http://127.0.0.1:8080"), options.BaseUri);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.RequestTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(45), options.StreamIdleTimeout);
        Assert.AreEqual(TimeSpan.FromSeconds(1), options.ReconnectBackoff.Initial);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.ReconnectBackoff.Max);
        Assert.AreEqual(10_000, options.StreamChannelCapacity);
        Assert.IsFalse(options.UseBearerAuth);
        Assert.IsTrue(options.Validate().IsSuccess);
    }

    [TestMethod]
    public void Validate_RejectsRelativeAndNonHttpAddresses()
    {
        Assert.AreEqual(
            ShioajiErrorCodes.InvalidOptions,
            new ShioajiOptions { BaseUri = new Uri("/relative", UriKind.Relative) }.Validate().Error!.Code);
        Assert.AreEqual(
            ShioajiErrorCodes.InvalidOptions,
            new ShioajiOptions { BaseUri = new Uri("ftp://127.0.0.1/") }.Validate().Error!.Code);
    }

    [TestMethod]
    public void Validate_BearerNeedsBothKeys()
    {
        var onlyKey = new ShioajiOptions { UseBearerAuth = true, ApiKey = "key-only-value" };

        Assert.AreEqual(ShioajiErrorCodes.InvalidOptions, onlyKey.Validate().Error!.Code);
        Assert.IsTrue(new ShioajiOptions { UseBearerAuth = true, ApiKey = "key-value-1", SecretKey = "secret-value-1" }.Validate().IsSuccess);
    }

    [TestMethod]
    public void Validate_RefusesBearerOverPlainHttpToANonLoopbackHost()
    {
        var remote = new ShioajiOptions
        {
            BaseUri = new Uri("http://shioaji.example.test:8080"),
            UseBearerAuth = true,
            ApiKey = "key-value-1",
            SecretKey = "secret-value-1",
        };

        Assert.AreEqual(ShioajiErrorCodes.InvalidOptions, remote.Validate().Error!.Code);

        remote.BaseUri = new Uri("https://shioaji.example.test:8080");
        Assert.IsTrue(remote.Validate().IsSuccess);

        remote.BaseUri = new Uri("http://[::1]:8080");
        Assert.IsTrue(remote.Validate().IsSuccess);
    }

    [TestMethod]
    public void Validate_RejectsNonPositiveTimeoutsAndCapacity()
    {
        Assert.IsTrue(new ShioajiOptions { RequestTimeout = TimeSpan.Zero }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiOptions { StreamIdleTimeout = TimeSpan.FromSeconds(-1) }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiOptions { StreamChannelCapacity = 0 }.Validate().IsFailure);
    }

    [TestMethod]
    public void Validate_ReportsNestedOptionProblems()
    {
        var badBackoff = new ShioajiOptions();
        badBackoff.ReconnectBackoff.Max = TimeSpan.FromMilliseconds(1);

        Assert.IsTrue(badBackoff.Validate().IsFailure);

        var badBudget = new ShioajiOptions();
        badBudget.RateBudget.OrderLimit = 0;

        Assert.IsTrue(badBudget.Validate().IsFailure);
    }

    [TestMethod]
    public void ToString_NeverContainsTheCredentials()
    {
        var options = new ShioajiOptions { UseBearerAuth = true, ApiKey = "super-secret-key-1", SecretKey = "super-secret-value-2" };

        var text = options.ToString();

        Assert.DoesNotContain("super-secret", text);
    }

    [TestMethod]
    public void ServerOptions_RejectCredentialKeysInAdditionalEnvironment()
    {
        var options = new ShioajiServerOptions();
        options.AdditionalEnvironment["sj_api_key"] = "x";

        var validation = options.Validate();

        Assert.IsTrue(validation.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.InvalidOptions, validation.Error.Code);
    }

    [TestMethod]
    public void ServerOptions_ValidateTheirOwnFields()
    {
        Assert.IsTrue(new ShioajiServerOptions().Validate().IsSuccess);
        Assert.IsTrue(new ShioajiServerOptions { BindAddress = "no-port" }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiServerOptions { BindAddress = "127.0.0.1:99999" }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiServerOptions { StartupTimeout = TimeSpan.Zero }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiServerOptions { MaxRestarts = -1 }.Validate().IsFailure);
        Assert.IsTrue(new ShioajiServerOptions { ReceiveWindowMs = 0 }.Validate().IsFailure);

        var blankKey = new ShioajiServerOptions();
        blankKey.AdditionalEnvironment[" "] = "x";

        Assert.IsTrue(blankKey.Validate().IsFailure);
    }

    [TestMethod]
    public void Credentials_ToStringRevealsNothing()
    {
        var credentials = new ShioajiCredentials
        {
            ApiKey = "api-key-should-not-print",
            SecretKey = "secret-should-not-print",
            CaPath = "C:\\secret\\ca.pfx",
            CaPassword = "password-should-not-print",
        };

        var text = credentials.ToString();

        Assert.DoesNotContain("should-not-print", text);
        Assert.DoesNotContain("ca.pfx", text);
        Assert.Contains("(set)", text);
        Assert.Contains("(unset)", new ShioajiCredentials { ApiKey = "a", SecretKey = "b" }.ToString());
    }
}
