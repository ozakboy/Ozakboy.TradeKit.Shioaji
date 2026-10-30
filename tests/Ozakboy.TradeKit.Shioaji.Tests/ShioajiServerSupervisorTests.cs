using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ozakboy.Security.Masking;
using Ozakboy.TradeKit.Shioaji.Auth;
using Ozakboy.TradeKit.Shioaji.Server;
using Ozakboy.TradeKit.Shioaji.Tests.Support;

namespace Ozakboy.TradeKit.Shioaji.Tests;

[TestClass]
public sealed class ShioajiServerSupervisorTests
{
    private const string FakeProcessName = "FakeShioajiServerExe";

    private const string ApiKey = "AKEY-1234567890";
    private const string SecretKey = "SecretKeyAbcdefghij"; // 必須是 base58:不含 0 O I l _ -
    private const string CaPath = "C:\\certs\\sinopac-ca-secret.pfx";
    private const string CaPassword = "CA-PASSWORD-XYZ";

    private static readonly string[] AllSecrets = [ApiKey, SecretKey, CaPath, CaPassword];

    [ClassCleanup]
    public static void KillLeftovers()
    {
        foreach (var process in Process.GetProcessesByName(FakeProcessName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // 已經結束。
                // Already gone.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// 假可執行檔的位置:與測試專案同層的 FakeShioajiServerExe 專案的輸出。
    /// Where the fake executable lives: the output of the FakeShioajiServerExe project next to the test project.
    /// </summary>
    private static string FakeExecutable()
    {
        var targetFramework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var configuration = targetFramework.Parent!;
        var testsRoot = configuration.Parent!.Parent!.Parent!;
        var fileName = OperatingSystem.IsWindows() ? FakeProcessName + ".exe" : FakeProcessName;
        var path = Path.Combine(testsRoot.FullName, FakeProcessName, "bin", configuration.Name, targetFramework.Name, fileName);

        Assert.IsTrue(File.Exists(path), $"the fake executable is missing: {path}");

        return path;
    }

    private static ShioajiCredentials Credentials() => new()
    {
        ApiKey = ApiKey,
        SecretKey = SecretKey,
        CaPath = CaPath,
        CaPassword = CaPassword,
    };

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _home;

        private Rig(
            int port,
            string home,
            ShioajiTestHost host,
            ShioajiServerSupervisor supervisor,
            CapturingLogger<ShioajiServerSupervisor> logger,
            SecretMasker masker,
            int credentialCalls)
        {
            Port = port;
            _home = home;
            Host = host;
            Supervisor = supervisor;
            Logger = logger;
            Masker = masker;
            CredentialCalls = credentialCalls;
        }

        public int Port { get; }

        public ShioajiTestHost Host { get; }

        public ShioajiServerSupervisor Supervisor { get; }

        public CapturingLogger<ShioajiServerSupervisor> Logger { get; }

        public SecretMasker Masker { get; }

        public int CredentialCalls { get; private set; }

        public string Home => _home;

        public Uri BaseUri => new($"http://127.0.0.1:{Port}/");

        public static Rig Create(string mode = "serve", Action<ShioajiServerOptions>? configure = null, Dictionary<string, string>? environment = null)
        {
            var port = FakeShioajiHttpServer.FindFreePort();
            var home = Path.Combine(Path.GetTempPath(), "shioaji-sup-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(home);

            var host = ShioajiTestHost.CreateAt(new Uri($"http://127.0.0.1:{port}/"));
            var logger = new CapturingLogger<ShioajiServerSupervisor>();
            var masker = new SecretMasker();

            var options = new ShioajiServerOptions
            {
                ExecutablePath = FakeExecutable(),
                BindAddress = $"127.0.0.1:{port}",
                HomePath = home,
                StartupTimeout = TimeSpan.FromSeconds(30),
                StartupPollInterval = TimeSpan.FromMilliseconds(100),
                RestartBackoff = TimeSpan.FromMilliseconds(50),
                StopTimeout = TimeSpan.FromSeconds(5),
            };

            options.AdditionalEnvironment["FAKE_SHIOAJI_MODE"] = mode;

            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    options.AdditionalEnvironment[key] = value;
                }
            }

            configure?.Invoke(options);

            Rig? rig = null;
            var supervisor = new ShioajiServerSupervisor(
                options,
                () =>
                {
                    if (rig is not null)
                    {
                        rig.CredentialCalls++;
                    }

                    return Credentials();
                },
                host.Auth,
                logger,
                masker);

            rig = new Rig(port, home, host, supervisor, logger, masker, 0);

            return rig;
        }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.DisposeAsync();
            Host.Dispose();

            try
            {
                Directory.Delete(_home, recursive: true);
            }
            catch (IOException)
            {
                // 行程剛結束、檔案還被占用:留在暫存目錄,不影響測試結果。
                // The process just ended and a file is still held: it stays in the temp directory without affecting the result.
            }
        }

        public async Task<JsonElement> ReadEnvironmentAsync()
        {
            using var http = new HttpClient();
            var json = await http.GetStringAsync(new Uri(BaseUri, "__env"));

            return JsonDocument.Parse(json).RootElement.Clone();
        }

        public async Task<bool> WaitUntilAsync(Func<ShioajiServerStatus, bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                if (condition(Supervisor.Status))
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return condition(Supervisor.Status);
        }
    }

    private static void AssertNoSecretLeaks(string text, string where)
    {
        foreach (var secret in AllSecrets)
        {
            Assert.DoesNotContain(secret, text, $"{where} leaked a credential");
        }
    }

    [TestMethod]
    public async Task Start_SpawnsTheServer_FeedsCredentialsThroughTheEnvironment_AndStopsCleanly()
    {
        await using var rig = Rig.Create(environment: new Dictionary<string, string> { ["SJ_AGENT_HARNESS"] = "production" });

        var started = await rig.Supervisor.StartAsync();

        Assert.IsTrue(started.IsSuccess, started.Error?.ToString());

        var status = rig.Supervisor.Status;

        Assert.AreEqual(ShioajiServerState.Running, status.State);
        Assert.IsNotNull(status.ProcessId);
        Assert.IsNotNull(status.StartedAt);
        Assert.AreEqual(0, status.Restarts);
        Assert.AreEqual(1, rig.CredentialCalls, "the credentials are fetched once, at the instant of spawning");

        // 憑證確實以環境變數到了子行程,模式、位址、HOME 與透傳的變數也是。
        // The credentials really reached the child as environment variables, and so did the mode, the address, the
        // home directory and the passthrough variable.
        var environment = await rig.ReadEnvironmentAsync();

        Assert.AreEqual(ApiKey, environment.GetProperty("SJ_API_KEY").GetString());
        Assert.AreEqual(SecretKey, environment.GetProperty("SJ_SEC_KEY").GetString());
        Assert.AreEqual(CaPath, environment.GetProperty("SJ_CA_PATH").GetString());
        Assert.AreEqual(CaPassword, environment.GetProperty("SJ_CA_PASSWD").GetString());
        Assert.AreEqual("false", environment.GetProperty("SJ_PRODUCTION").GetString());
        Assert.AreEqual($"127.0.0.1:{rig.Port}", environment.GetProperty("SJ_HTTP_ADDR").GetString());
        Assert.AreEqual(rig.Home, environment.GetProperty("SJ_HOME_PATH").GetString());
        Assert.AreEqual("production", environment.GetProperty("SJ_AGENT_HARNESS").GetString());

        // 啟動參數:一定有 --no-open(否則會開瀏覽器),模擬環境沒有 --production 與 --auth。
        // The arguments: --no-open is always there (or a browser opens), and simulation has neither --production nor --auth.
        var all = rig.Logger.AllText;

        Assert.Contains("ARGS: server start --no-open", all);
        Assert.DoesNotContain("--production", all);
        Assert.DoesNotContain("--auth", all);

        // 輸出是逐行遮罩之後才進日誌的:假的 exe 把憑證印了出來,日誌裡只能看到遮罩字串。
        // The output is masked line by line before it is logged: the fake exe prints the credentials, and the log may
        // only show the mask.
        AssertNoSecretLeaks(all, "the log");
        Assert.Contains(rig.Masker.MaskSegment, all);
        Assert.Contains("login with key", all);
        Assert.IsTrue(rig.Logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("ca password is", StringComparison.Ordinal)), "stderr goes out as a warning");

        // 憑證不寫任何檔案。
        // The credentials are written to no file.
        foreach (var file in Directory.GetFiles(rig.Home, "*", SearchOption.AllDirectories))
        {
            AssertNoSecretLeaks(await File.ReadAllTextAsync(file), file);
        }

        var stopped = await rig.Supervisor.StopAsync();

        Assert.IsTrue(stopped.IsSuccess);
        Assert.AreEqual(ShioajiServerState.Stopped, rig.Supervisor.Status.State);
        Assert.IsNull(rig.Supervisor.Status.ProcessId);
        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length, "no process may be left behind");
        Assert.IsTrue((await rig.Host.Auth.ProbeInfoAsync(TimeSpan.FromSeconds(1), CancellationToken.None)).IsFailure);
    }

    [TestMethod]
    public async Task Start_AppliesTheOptionsAsFlagsAndEnvironment()
    {
        await using var rig = Rig.Create(configure: options =>
        {
            options.Production = true;
            options.RequireAuth = true;
            options.ReceiveWindowMs = 7000;
        });

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);

        var environment = await rig.ReadEnvironmentAsync();

        Assert.AreEqual("true", environment.GetProperty("SJ_PRODUCTION").GetString());
        Assert.Contains("ARGS: server start --no-open --production --auth --receive-window 7000", rig.Logger.AllText);
        Assert.Contains("SJ_HTTP_AUTH=true", rig.Logger.AllText);
    }

    [TestMethod]
    public async Task Start_WhenAlreadyRunning_IsASuccessWithoutASecondProcess()
    {
        await using var rig = Rig.Create();

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);

        var pid = rig.Supervisor.Status.ProcessId;

        var both = await Task.WhenAll(rig.Supervisor.StartAsync(), rig.Supervisor.StartAsync());

        Assert.IsTrue(both.All(r => r.IsSuccess));
        Assert.AreEqual(pid, rig.Supervisor.Status.ProcessId);
        Assert.AreEqual(1, Process.GetProcessesByName(FakeProcessName).Length);
        Assert.AreEqual(1, rig.CredentialCalls);
    }

    [TestMethod]
    public async Task AServerThatNeverAnswers_TimesOutKillsTheProcess_AndReportsTheMaskedLastOutput()
    {
        await using var rig = Rig.Create("hang", options => options.StartupTimeout = TimeSpan.FromSeconds(2));

        var result = await rig.Supervisor.StartAsync();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.ServerStartTimeout, result.Error.Code);
        Assert.AreEqual("shioaji.server.start_timeout", result.Error.Code);

        var lastOutput = result.Error.Data![ShioajiErrorDataKeys.LastOutput];

        Assert.Contains("connecting to broker", lastOutput);
        AssertNoSecretLeaks(lastOutput, "the error data");
        AssertNoSecretLeaks(result.Error.Message, "the error message");
        Assert.AreEqual(ShioajiServerState.Stopped, rig.Supervisor.Status.State);
        Assert.IsNotNull(rig.Supervisor.Status.LastError);
        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length, "the hung process must be killed");
    }

    [TestMethod]
    public async Task AProcessThatCrashesAtStartup_IsExitedEarly_WithTheExitCodeAndMaskedOutput()
    {
        await using var rig = Rig.Create("crash");

        var result = await rig.Supervisor.StartAsync();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.ServerExitedEarly, result.Error.Code);
        Assert.AreEqual("3", result.Error.Data![ShioajiErrorDataKeys.ExitCode]);

        var lastOutput = result.Error.Data[ShioajiErrorDataKeys.LastOutput];

        Assert.Contains("fatal: login failed for key", lastOutput);
        AssertNoSecretLeaks(lastOutput, "the error data");
        AssertNoSecretLeaks(rig.Logger.AllText, "the log");
        Assert.AreEqual(ShioajiServerState.Stopped, rig.Supervisor.Status.State);
    }

    [TestMethod]
    public async Task AnExitThatIsNotACrashButTheServerNeverAnswers_IsStillExitedEarly()
    {
        await using var rig = Rig.Create("exit", environment: new Dictionary<string, string> { ["FAKE_SHIOAJI_EXIT_CODE"] = "0" });

        var result = await rig.Supervisor.StartAsync();

        Assert.AreEqual(ShioajiErrorCodes.ServerExitedEarly, result.Error!.Code);
        Assert.AreEqual("0", result.Error.Data![ShioajiErrorDataKeys.ExitCode]);
    }

    [TestMethod]
    public async Task AnUnexpectedExit_IsRestartedWithBackoff()
    {
        await using var rig = Rig.Create("crash-once");

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);

        var firstPid = rig.Supervisor.Status.ProcessId;

        var recovered = await rig.WaitUntilAsync(s => s is { Restarts: 1, State: ShioajiServerState.Running }, TimeSpan.FromSeconds(30));
        var status = rig.Supervisor.Status;

        Assert.IsTrue(recovered, $"state {status.State}, restarts {status.Restarts}, error {status.LastError}");
        Assert.AreEqual(7, status.LastExitCode);
        Assert.AreNotEqual(firstPid, status.ProcessId, "a new process took over");
        Assert.AreEqual(2, rig.CredentialCalls, "the credentials are fetched again for the restart");
        Assert.IsTrue((await rig.Host.Auth.GetInfoAsync()).IsSuccess, "the restarted server answers");

        var stopped = await rig.Supervisor.StopAsync();

        Assert.IsTrue(stopped.IsSuccess);
        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length);
    }

    [TestMethod]
    public async Task TooManyRestarts_PutTheSupervisorInFaultedAndItStopsTrying()
    {
        await using var rig = Rig.Create(
            "serve-then-crash",
            options => options.MaxRestarts = 2,
            new Dictionary<string, string> { ["FAKE_SHIOAJI_LIFETIME_MS"] = "2000" });

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);

        var faulted = await rig.WaitUntilAsync(s => s.State == ShioajiServerState.Faulted, TimeSpan.FromSeconds(60));
        var status = rig.Supervisor.Status;

        Assert.IsTrue(faulted, $"state {status.State}, restarts {status.Restarts}");
        Assert.AreEqual(2, status.Restarts, "exactly MaxRestarts restarts were attempted");
        Assert.AreEqual(9, status.LastExitCode);
        Assert.IsNotNull(status.LastError);
        Assert.IsNull(status.ProcessId);

        await Task.Delay(500);
        Assert.AreEqual(ShioajiServerState.Faulted, rig.Supervisor.Status.State, "Faulted is terminal until someone starts it again");
        Assert.AreEqual(3, rig.CredentialCalls, "one start plus two restarts, and no more");
        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length);

        Assert.IsTrue((await rig.Supervisor.StopAsync()).IsSuccess);
        Assert.AreEqual(ShioajiServerState.Stopped, rig.Supervisor.Status.State);
    }

    [TestMethod]
    public async Task AServerThatIgnoresTheStopCommand_IsKilledAfterTheStopTimeout()
    {
        await using var rig = Rig.Create(
            configure: options => options.StopTimeout = TimeSpan.FromSeconds(1),
            environment: new Dictionary<string, string> { ["FAKE_SHIOAJI_STOP_MODE"] = "ignore" });

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);
        Assert.AreEqual(1, Process.GetProcessesByName(FakeProcessName).Length);

        var stopped = await rig.Supervisor.StopAsync();

        Assert.IsTrue(stopped.IsSuccess);
        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length, "the process tree must be killed");
        Assert.IsTrue(rig.Logger.Entries.Any(e => e.Message.Contains("沒有在時限內停止", StringComparison.Ordinal)), rig.Logger.AllText);
    }

    [TestMethod]
    public async Task ARunningServerWithTheRightMode_IsAttachedToAndNeverTouched()
    {
        using var external = new FakeShioajiHttpServer();
        var logger = new CapturingLogger<ShioajiServerSupervisor>();
        using var host = ShioajiTestHost.Create(external);
        var credentialCalls = 0;

        var supervisor = new ShioajiServerSupervisor(
            new ShioajiServerOptions { ExecutablePath = "Z:\\does\\not\\exist\\shioaji.exe", BindAddress = external.BindAddress },
            () =>
            {
                credentialCalls++;
                return Credentials();
            },
            host.Auth,
            logger);

        await using (supervisor)
        {
            var started = await supervisor.StartAsync();

            Assert.IsTrue(started.IsSuccess, started.Error?.ToString());
            Assert.AreEqual(ShioajiServerState.Attached, supervisor.Status.State);
            Assert.IsNull(supervisor.Status.ProcessId);
            Assert.AreEqual(0, credentialCalls, "attaching must not even fetch the credentials");
            Assert.Contains("attaching", logger.AllText);

            Assert.IsTrue((await supervisor.StartAsync()).IsSuccess, "starting again while attached is a no-op");

            var stopped = await supervisor.StopAsync();

            Assert.IsTrue(stopped.IsSuccess);
            Assert.AreEqual(ShioajiServerState.Stopped, supervisor.Status.State);
        }

        Assert.IsTrue((await host.Auth.GetInfoAsync()).IsSuccess, "the external server is still there");
    }

    [TestMethod]
    public async Task ARunningServerWithTheWrongMode_IsRefused_AndLeftAlone()
    {
        using var external = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(external);

        var supervisor = new ShioajiServerSupervisor(
            new ShioajiServerOptions { ExecutablePath = "Z:\\nope\\shioaji.exe", BindAddress = external.BindAddress, Production = true },
            Credentials,
            host.Auth);

        await using (supervisor)
        {
            var result = await supervisor.StartAsync();

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(ShioajiErrorCodes.ServerModeMismatch, result.Error.Code);
            Assert.Contains("simulation=True", result.Error.Message);
            Assert.AreEqual(ShioajiServerState.Stopped, supervisor.Status.State);
            Assert.IsNotNull(supervisor.Status.LastError);
        }

        Assert.IsTrue((await host.Auth.GetInfoAsync()).IsSuccess, "a server somebody else started is never killed");
    }

    [TestMethod]
    public async Task ASpawnedServerWhoseModeDisagrees_IsKilledAndRefused()
    {
        // Production=false,但這個假 server 偏要回報 simulation=false —— 等於「想連模擬卻連到正式」,是最危險的一種不一致。
        // Production=false, yet this fake insists on reporting simulation=false: wanting simulation and reaching
        // production, the most dangerous kind of disagreement.
        await using var rig = Rig.Create(environment: new Dictionary<string, string> { ["FAKE_SHIOAJI_SIMULATION_OVERRIDE"] = "false" });

        var result = await rig.Supervisor.StartAsync();

        Assert.AreEqual(ShioajiErrorCodes.ServerModeMismatch, result.Error!.Code);
        Assert.AreEqual(ShioajiServerState.Stopped, rig.Supervisor.Status.State);
        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length, "the process it started must be killed");
    }

    [TestMethod]
    public async Task AMissingExecutable_IsReportedWithoutSpawningAnything()
    {
        await using var rig = Rig.Create(configure: options => options.ExecutablePath = Path.Combine(Path.GetTempPath(), "no-such-shioaji.exe"));

        var result = await rig.Supervisor.StartAsync();

        Assert.AreEqual(ShioajiErrorCodes.ServerExecutableNotFound, result.Error!.Code);
        Assert.AreEqual(0, rig.CredentialCalls, "no credentials are fetched when there is nothing to start");
        Assert.AreEqual(ShioajiServerState.Stopped, rig.Supervisor.Status.State);
    }

    [TestMethod]
    public async Task AServerThatDetachesItself_BecomesAttached_AndIsStoppedWithTheStopCommand()
    {
        // 假設 `server start` 啟動後自行脫離:行程以 0 結束,server 卻還在回應。這個情境用腳本化的探測模擬。
        // Suppose `server start` detaches itself: the process exits 0 while the server keeps answering. A scripted
        // probe simulates that.
        var home = Path.Combine(Path.GetTempPath(), "shioaji-sup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(home);
        var probes = 0;
        var logger = new CapturingLogger<ShioajiServerSupervisor>();

        var options = new ShioajiServerOptions
        {
            ExecutablePath = FakeExecutable(),
            HomePath = home,
            StartupPollInterval = TimeSpan.FromMilliseconds(100),
            StopTimeout = TimeSpan.FromSeconds(5),
        };

        options.AdditionalEnvironment["FAKE_SHIOAJI_MODE"] = "exit";

        var supervisor = new ShioajiServerSupervisor(
            options,
            Credentials,
            _ =>
            {
                // 第一次探測(啟動前)回報「沒在跑」;之後只有在假 exe 的行程已經結束時,server 才「出現」。
                // The first probe, before the start, reports "not running"; afterwards the server "appears" only once
                // the fake exe's process has ended.
                var running = Interlocked.Increment(ref probes) > 1 && Process.GetProcessesByName(FakeProcessName).Length == 0;

                return Task.FromResult(running
                    ? Result.Success(new ShioajiServerInfo { Name = "shioaji", Version = "x", Simulation = true, Protocols = [] })
                    : Result.Failure<ShioajiServerInfo>(new Error("test.down", "not running", ErrorCategory.Network)));
            },
            logger,
            null,
            null);

        try
        {
            await using (supervisor)
            {
                var started = await supervisor.StartAsync();

                Assert.IsTrue(started.IsSuccess, started.Error?.ToString());
                Assert.AreEqual(ShioajiServerState.Attached, supervisor.Status.State);
                Assert.IsNull(supervisor.Status.ProcessId);

                await supervisor.StopAsync();

                Assert.AreEqual(ShioajiServerState.Stopped, supervisor.Status.State);
                Assert.Contains("stop requested", logger.AllText, "a server we started is stopped even when it detached");
            }
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [TestMethod]
    public async Task ACredentialsProviderThatThrowsOrReturnsNull_IsAStartFailure_NotACrash()
    {
        static Task<Result<ShioajiServerInfo>> Down(CancellationToken _) =>
            Task.FromResult(Result.Failure<ShioajiServerInfo>(new Error("test.down", "not running", ErrorCategory.Network)));

        var options = new ShioajiServerOptions { ExecutablePath = FakeExecutable() };

        var throwing = new ShioajiServerSupervisor(options, () => throw new InvalidOperationException("vault is locked"), Down, null, null, null);
        var returningNull = new ShioajiServerSupervisor(options, () => null!, Down, null, null, null);

        await using (throwing)
        await using (returningNull)
        {
            var first = await throwing.StartAsync();
            var second = await returningNull.StartAsync();

            Assert.AreEqual(ShioajiErrorCodes.ServerStartFailed, first.Error!.Code);
            Assert.Contains("vault is locked", first.Error.Message);
            Assert.Contains("InvalidOperationException", first.Error.Message);
            Assert.AreEqual(ShioajiErrorCodes.ServerStartFailed, second.Error!.Code);
            Assert.AreEqual(ShioajiServerState.Stopped, throwing.Status.State);
            Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length);
        }
    }

    // ───────────────────────── 0.1.1:對真 shioaji 的實測事實 / 0.1.1: facts measured on the real shioaji ─────────────────────────

    [TestMethod]
    public async Task ColourCodesInTheOutput_AreStrippedBeforeTheLogAndTheLastLines()
    {
        await using var rig = Rig.Create();

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);

        var all = rig.Logger.AllText;

        Assert.DoesNotContain("\u001b", all, "no ANSI escape may reach the log");
        Assert.Contains("INFO colored startup line", all);
        Assert.Contains("2026-10-30T08:33:02.060484Z", all);
    }

    [TestMethod]
    public void StripAnsi_RemovesEverySequenceAndNothingElse()
    {
        const string coloured = "\u001b[2m2026-10-30T08:33:02Z\u001b[0m \u001b[32m INFO\u001b[0m \u001b[1;31mdone\u001b[0m [not-escape]";

        Assert.AreEqual("2026-10-30T08:33:02Z  INFO done [not-escape]", ShioajiServerSupervisor.StripAnsi(coloured));
        Assert.AreEqual("plain", ShioajiServerSupervisor.StripAnsi("plain"));
    }

    [TestMethod]
    public async Task AFailedLogin_IsAuthFailed_StrippedMasked_AndGoesStraightToFaulted()
    {
        await using var rig = Rig.Create("auth-fail");

        var result = await rig.Supervisor.StartAsync();

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual(ShioajiErrorCodes.ServerAuthFailed, result.Error.Code);
        Assert.AreEqual("shioaji.server.auth_failed", result.Error.Code);
        Assert.AreEqual(ErrorCategory.Unauthorized, result.Error.Category);
        Assert.IsFalse(result.Error.IsTransient, "retrying cannot help");

        var detail = result.Error.Data![ShioajiErrorDataKeys.Detail];

        Assert.Contains("Authentication failed", detail);
        Assert.Contains("invalid secret_key", detail);
        Assert.DoesNotContain("\u001b", detail, "the detail line is stripped of colour codes");
        Assert.AreEqual("1", result.Error.Data[ShioajiErrorDataKeys.ExitCode]);

        Assert.AreEqual(ShioajiServerState.Faulted, rig.Supervisor.Status.State);
        Assert.AreEqual(0, rig.Supervisor.Status.Restarts, "an authentication failure never enters the restart loop");
        Assert.AreEqual(1, rig.CredentialCalls);
        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length);
    }

    [TestMethod]
    public async Task AnAuthFailureDuringARestart_StopsTheLoopInFaulted()
    {
        await using var rig = Rig.Create("crash-once", environment: new Dictionary<string, string> { ["FAKE_SHIOAJI_AFTER"] = "auth-fail" });

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);

        var faulted = await rig.WaitUntilAsync(s => s.State == ShioajiServerState.Faulted, TimeSpan.FromSeconds(30));
        var status = rig.Supervisor.Status;

        Assert.IsTrue(faulted, $"state {status.State}, error {status.LastError}");
        Assert.AreEqual(1, status.Restarts, "one restart was tried; the authentication failure ended the loop");
        Assert.Contains("Authentication failed", status.LastError!);

        await Task.Delay(600);
        Assert.AreEqual(2, rig.CredentialCalls, "no third attempt");
    }

    [TestMethod]
    public async Task TheServerLogPath_IsParsedFromTheFirstStdoutLine()
    {
        await using var rig = Rig.Create();

        Assert.IsNull(rig.Supervisor.Status.LogFilePath, "nothing was started yet");
        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);

        var path = rig.Supervisor.Status.LogFilePath;

        Assert.IsNotNull(path);
        Assert.AreEqual(Path.Combine(rig.Home, "logs", $"server-{rig.Port}", "server.2026-10-30.log"), path);
        Assert.DoesNotContain("\u001b", path);
    }

    [TestMethod]
    public async Task TheServerLogPath_IsNullWhenTheLineNeverAppears_AndForAnAttachedServer()
    {
        using var external = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(external);

        var attached = new ShioajiServerSupervisor(
            new ShioajiServerOptions { BindAddress = external.BindAddress },
            Credentials,
            host.Auth);

        await using (attached)
        {
            Assert.IsTrue((await attached.StartAsync()).IsSuccess);
            Assert.IsNull(attached.Status.LogFilePath);
        }

        // 輸出裡沒有 `server log:` 那一行 → null。
        // No `server log:` line in the output → null.
        await using var rig = Rig.Create(environment: new Dictionary<string, string> { ["FAKE_SHIOAJI_NO_LOG_LINE"] = "1" });

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);
        Assert.IsNull(rig.Supervisor.Status.LogFilePath);
    }

    [TestMethod]
    [DataRow("bad_secret_key")]
    [DataRow("has-dash")]
    [DataRow("zeroO0")]
    [DataRow("capitalI")]
    [DataRow("lowerl")]
    [DataRow("with space")]
    [DataRow("")]
    public async Task ANonBase58SecretKey_IsRefusedLocally_WithoutStartingAProcess_OrEchoingAnything(string secret)
    {
        var started = 0;
        var supervisor = new ShioajiServerSupervisor(
            new ShioajiServerOptions { ExecutablePath = FakeExecutable() },
            () =>
            {
                started++;
                return new ShioajiCredentials { ApiKey = "api-key-123456", SecretKey = secret };
            },
            _ => Task.FromResult(Result.Failure<ShioajiServerInfo>(new Error("test.down", "not running", ErrorCategory.Network))),
            null,
            null,
            null);

        await using (supervisor)
        {
            var result = await supervisor.StartAsync();

            Assert.IsTrue(result.IsFailure);
            Assert.AreEqual(ShioajiErrorCodes.ServerInvalidCredentials, result.Error.Code);
            Assert.AreEqual("shioaji.server.invalid_credentials", result.Error.Code);
            Assert.Contains("base58", result.Error.Message);

            if (secret.Length > 0)
            {
                Assert.DoesNotContain(secret, result.Error.Message, "the message must not echo the secret");
            }

            Assert.AreEqual(1, started, "the provider was asked once");
            Assert.AreEqual(ShioajiServerState.Stopped, supervisor.Status.State);
            Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length, "no process may be started");
        }
    }

    [TestMethod]
    public void Credentials_Validate_AcceptsBase58AndChecksOnlyTheSecret()
    {
        const string base58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        Assert.IsTrue(new ShioajiCredentials { ApiKey = "anything goes here_-!", SecretKey = base58 }.Validate().IsSuccess);
        Assert.IsTrue(new ShioajiCredentials { ApiKey = "x", SecretKey = base58 }.Validate().IsSuccess);
        Assert.IsTrue(new ShioajiCredentials { ApiKey = "x", SecretKey = "0" }.Validate().IsFailure);
    }

    [TestMethod]
    public async Task Cancelling_DuringStartup_KillsTheProcessAndReturnsCancelled()
    {
        await using var rig = Rig.Create("hang");
        using var cancellation = new CancellationTokenSource();

        var starting = rig.Supervisor.StartAsync(cancellation.Token);

        await Task.Delay(1500);
        await cancellation.CancelAsync();

        var result = await starting;

        Assert.AreEqual(ShioajiErrorCodes.RequestCancelled, result.Error!.Code);
        Assert.AreEqual(ShioajiServerState.Stopped, rig.Supervisor.Status.State);
        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length);

        using var already = new CancellationTokenSource();
        await already.CancelAsync();

        Assert.AreEqual(ShioajiErrorCodes.RequestCancelled, (await rig.Supervisor.StartAsync(already.Token)).Error!.Code);
        Assert.AreEqual(ShioajiErrorCodes.RequestCancelled, (await rig.Supervisor.StopAsync(already.Token)).Error!.Code);
    }

    [TestMethod]
    public async Task DisposeAsync_StopsARunningServer_AndStartAfterwardsThrows()
    {
        var rig = Rig.Create();

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);

        await rig.DisposeAsync();

        Assert.AreEqual(0, Process.GetProcessesByName(FakeProcessName).Length);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => rig.Supervisor.StartAsync());
        await rig.Supervisor.DisposeAsync();
    }

    [TestMethod]
    public async Task StartAgainAfterAStop_SpawnsAFreshProcess()
    {
        await using var rig = Rig.Create();

        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);
        Assert.IsTrue((await rig.Supervisor.StopAsync()).IsSuccess);
        Assert.IsTrue((await rig.Supervisor.StartAsync()).IsSuccess);
        Assert.AreEqual(ShioajiServerState.Running, rig.Supervisor.Status.State);
        Assert.AreEqual(2, rig.CredentialCalls);
    }

    [TestMethod]
    public void ResolveExecutable_UsesTheConfiguredPathOnlyWhenItExists()
    {
        Assert.IsNull(ShioajiServerSupervisor.ResolveExecutable(Path.Combine(Path.GetTempPath(), "definitely-missing-shioaji.exe")));
        Assert.AreEqual(FakeExecutable(), ShioajiServerSupervisor.ResolveExecutable(FakeExecutable()));
    }

    [TestMethod]
    public void Constructor_ValidatesItsArguments()
    {
        using var external = new FakeShioajiHttpServer();
        using var host = ShioajiTestHost.Create(external);

        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiServerSupervisor(null!, Credentials, host.Auth));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiServerSupervisor(new ShioajiServerOptions(), null!, host.Auth));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ShioajiServerSupervisor(new ShioajiServerOptions(), Credentials, null!));
        Assert.ThrowsExactly<ArgumentException>(() => new ShioajiServerSupervisor(new ShioajiServerOptions { BindAddress = "x" }, Credentials, host.Auth));
    }
}
