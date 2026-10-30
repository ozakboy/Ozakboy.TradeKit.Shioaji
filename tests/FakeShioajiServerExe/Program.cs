// 假的 shioaji 可執行檔,只給 ShioajiServerSupervisor 的測試用。
// 行為由環境變數 FAKE_SHIOAJI_MODE 決定:
//   serve             啟動後開 HTTP,回 /api/v1/info,收到 /__stop 就結束(預設)
//   crash             印出一行錯誤後立刻以結束碼 3 結束
//   hang              印出一行後永遠不開 HTTP(用來測啟動逾時)
//   crash-once        第一次執行服務 600 ms 後以結束碼 7 結束,之後的執行正常服務(用來測重啟)
//   serve-then-crash  每次執行都服務 FAKE_SHIOAJI_LIFETIME_MS(預設 400)毫秒後以結束碼 9 結束(用來測 Faulted)
// `server stop` 會對 SJ_HTTP_ADDR 送 /__stop;FAKE_SHIOAJI_STOP_MODE=ignore 時什麼都不做(用來測強制終止)。
//
// A fake shioaji executable used only by the ShioajiServerSupervisor tests.
// FAKE_SHIOAJI_MODE decides the behaviour: serve, crash, hang, crash-once, serve-then-crash (see the Chinese above).

using System.Net;
using System.Text;
using System.Text.Json;

var arguments = args;
var mode = Environment.GetEnvironmentVariable("FAKE_SHIOAJI_MODE") ?? "serve";
var address = Environment.GetEnvironmentVariable("SJ_HTTP_ADDR") ?? "127.0.0.1:8080";
var home = Environment.GetEnvironmentVariable("SJ_HOME_PATH");
var production = string.Equals(Environment.GetEnvironmentVariable("SJ_PRODUCTION"), "true", StringComparison.OrdinalIgnoreCase);

if (arguments.Length >= 2 && arguments[0] == "server" && arguments[1] == "stop")
{
    Console.WriteLine("stop requested");

    if (Environment.GetEnvironmentVariable("FAKE_SHIOAJI_STOP_MODE") == "ignore")
    {
        return 0;
    }

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

    try
    {
        await client.PostAsync($"http://{address}/__stop", null);
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
        Console.Error.WriteLine("stop: server not reachable");
    }

    return 0;
}

if (arguments.Length < 2 || arguments[0] != "server" || arguments[1] != "start")
{
    Console.Error.WriteLine("usage: server start|stop");
    return 2;
}

var apiKey = Environment.GetEnvironmentVariable("SJ_API_KEY") ?? string.Empty;
var caPassword = Environment.GetEnvironmentVariable("SJ_CA_PASSWD") ?? string.Empty;

Console.WriteLine("ARGS: " + string.Join(' ', arguments));
Console.WriteLine($"ENV: SJ_PRODUCTION={Environment.GetEnvironmentVariable("SJ_PRODUCTION")} SJ_HTTP_AUTH={Environment.GetEnvironmentVariable("SJ_HTTP_AUTH")}");

// 故意把憑證印出來,讓測試能驗證輸出是逐行遮罩之後才進日誌的。
// The credentials are printed on purpose, so the tests can verify output is masked line by line before it is logged.
Console.WriteLine($"login with key {apiKey}");
Console.Error.WriteLine($"ca password is {caPassword}");

if (mode == "crash")
{
    Console.Error.WriteLine($"fatal: login failed for key {apiKey}");
    return 3;
}

if (mode == "exit")
{
    // 立刻以 FAKE_SHIOAJI_EXIT_CODE(預設 0)結束,不開 HTTP。用來模擬 server 啟動後自行脫離,或啟動失敗。
    // Exits at once with FAKE_SHIOAJI_EXIT_CODE (0 by default) without serving, simulating a server that detaches
    // itself after the start, or one that fails to start.
    return int.TryParse(Environment.GetEnvironmentVariable("FAKE_SHIOAJI_EXIT_CODE"), out var immediate) ? immediate : 0;
}

if (mode == "hang")
{
    Console.WriteLine("connecting to broker...");
    await Task.Delay(Timeout.Infinite);
    return 0;
}

TimeSpan? lifetime = null;
var exitCode = 0;

if (mode == "crash-once")
{
    var runs = 1;

    if (home is not null)
    {
        Directory.CreateDirectory(home);
        var counter = Path.Combine(home, "runs.txt");

        if (File.Exists(counter))
        {
            runs = int.Parse(File.ReadAllText(counter).Trim()) + 1;
        }

        File.WriteAllText(counter, runs.ToString());
    }

    if (runs == 1)
    {
        lifetime = TimeSpan.FromMilliseconds(600);
        exitCode = 7;
    }
}
else if (mode == "serve-then-crash")
{
    var milliseconds = int.TryParse(Environment.GetEnvironmentVariable("FAKE_SHIOAJI_LIFETIME_MS"), out var parsed) ? parsed : 400;
    lifetime = TimeSpan.FromMilliseconds(milliseconds);
    exitCode = 9;
}

var simulation = !production;

if (bool.TryParse(Environment.GetEnvironmentVariable("FAKE_SHIOAJI_SIMULATION_OVERRIDE"), out var forced))
{
    simulation = forced;
}

using var listener = new HttpListener();
listener.Prefixes.Add($"http://{address}/");
listener.Start();
Console.WriteLine("server ready");

using var stop = new CancellationTokenSource();
var stoppedByRequest = false;

if (lifetime is { } life)
{
    stop.CancelAfter(life);
}

while (!stop.IsCancellationRequested)
{
    HttpListenerContext context;

    try
    {
        context = await listener.GetContextAsync().WaitAsync(stop.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }

    var path = context.Request.Url!.AbsolutePath;
    string[] protocols = ["http", "sse"];
    string body;

    if (path == "/api/v1/info")
    {
        body = JsonSerializer.Serialize(new
        {
            name = "shioaji",
            version = "fake",
            description = "fake shioaji server",
            protocols,
            scheme = "http",
            uds_plaintext = false,
            simulation,
        });
    }
    else if (path == "/__env")
    {
        body = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["SJ_API_KEY"] = Environment.GetEnvironmentVariable("SJ_API_KEY"),
            ["SJ_SEC_KEY"] = Environment.GetEnvironmentVariable("SJ_SEC_KEY"),
            ["SJ_CA_PATH"] = Environment.GetEnvironmentVariable("SJ_CA_PATH"),
            ["SJ_CA_PASSWD"] = Environment.GetEnvironmentVariable("SJ_CA_PASSWD"),
            ["SJ_PRODUCTION"] = Environment.GetEnvironmentVariable("SJ_PRODUCTION"),
            ["SJ_HTTP_ADDR"] = Environment.GetEnvironmentVariable("SJ_HTTP_ADDR"),
            ["SJ_HOME_PATH"] = Environment.GetEnvironmentVariable("SJ_HOME_PATH"),
            ["SJ_AGENT_HARNESS"] = Environment.GetEnvironmentVariable("SJ_AGENT_HARNESS"),
        });
    }
    else if (path == "/__stop")
    {
        body = "{}";
        stoppedByRequest = true;
        stop.Cancel();
    }
    else
    {
        context.Response.StatusCode = 404;
        body = "{}";
    }

    var bytes = Encoding.UTF8.GetBytes(body);
    context.Response.ContentType = "application/json";
    context.Response.ContentLength64 = bytes.Length;
    await context.Response.OutputStream.WriteAsync(bytes);
    context.Response.Close();
}

listener.Stop();
Console.WriteLine("server stopped");

return lifetime is null || stoppedByRequest ? 0 : exitCode;
