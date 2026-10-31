# Ozakboy.TradeKit.Shioaji

.NET 的永豐金 Shioaji 官方本機 HTTP server 用戶端,不使用任何第三方套件。

[English](README.md)

Shioaji 是永豐金證券的下單與行情 API。官方把它做成一個本機行程:`shioaji server start` 會登入券商、載入憑證、
維持 token 與連線,並在 `127.0.0.1:8080` 開出 HTTP 與 SSE 介面。這個套件管的是這個行程**外面**的事:
把它啟動起來並看著它、跟它講 HTTP、讀它的 SSE 串流、把 JSON 變成有型別的 C# 物件、
並在請求打到券商之前用本地預算擋下過量的呼叫。

它建構在 [`Ozakboy.Http`](https://github.com/ozakboy/Ozakboy.Http) 的管線與
[`Ozakboy.Security`](https://github.com/ozakboy/Ozakboy.Security) 的遮罩器之上。
這個家族的政策是只用 .NET BCL、Microsoft 官方套件與 `Ozakboy.*`,判定看**遞移相依**而不是套件名稱;
這裡的每一個相依都符合,見最後的「相依政策」。

> **目前狀態:**已在永豐的**模擬環境**以有效憑證實測過登入、帳號、委託回報訂閱(證券與期貨)、商品列表與詳細資料、
> 1 分 K、整日逐筆與用量查詢(結果見「官方 server 的已知行為」)。行情串流的事件欄位、下單與委託回報的回應形狀、
> 以及其他歷史端點仍照文件推定,全部列在「限制與未實測事項」;**其中全市場日 OHLCV 的路徑與回應形狀尤其未驗證。**
> **上實盤之前請先讀那一段。**

## 這一版做了什麼

| 範圍 | 狀態 |
| --- | --- |
| 啟動並監督官方 server 行程(`ShioajiServerSupervisor`),憑證只走子行程環境變數 | 已完成 |
| 接手外部已啟動的 server(Attach),模式不符時拒絕 | 已完成 |
| 非預期退出的退避重啟,超過上限進入 Faulted | 已完成 |
| `/api/v1/info`、帳號、流量、串流狀態、`subscribe_trade` / `unsubscribe_trade` | 已完成 |
| 商品查詢:單一商品、詳細資訊(含當沖資格)、分頁列表、期貨列表 | 已完成 |
| `ListAllContractsAsync`:以每頁 500 筆走完某個類別的所有分頁,每一頁先過 `MarketQuery` 預算(0.2.0) | 已完成 |
| 歷史資料(`ShioajiHistoryClient`,0.2.0):1 分 K(本地檢查 30 天區間)、逐筆、快照(本地檢查 500 檔)、排行掃描、借券券源、資券餘額、處置股與注意股 | 已完成 —— 除了 K 線與逐筆,其餘回應形狀未驗證 |
| 全市場日 OHLCV(`GetDailyQuotesAsync`,0.2.0) | 已完成,但**路徑與回應形狀都未驗證,尚未對真 server 的 OpenAPI 確認**;路徑可用 `ShioajiOptions.DailyQuotesPath` 覆寫 |
| 欄位陣列回應(`datetime[]`、`Open[]` …):任一欄位長度不一致就整筆失敗(`shioaji.history.malformed`) | 已完成 |
| 流量計(`ShioajiTrafficMeter`,0.2.0):回應位元組按台北日分桶、08:00 重置,可與 server 自己回報的用量對照 | 已完成 |
| 行情訂閱 / 取消,本地先擋三種會靜默失敗的訂閱 | 已完成 |
| 聚合 SSE 串流:股票 / 期權的成交、五檔、合併報價、指數報價、委託回報 | 已完成 —— 欄位依文件,未對真 server 驗證 |
| 斷線與閒置偵測、退避重連、重連後的重放回呼、生命週期事件、有界佇列 | 已完成 |
| 下單(股票與期貨)、改價、改量、撤單、列出委託;下單類請求永不重試 | 已完成 |
| 本地限流預算:行情查詢、帳務、委託、訂閱上限 | 已完成 |
| 與 `TradeKit.Abstractions` 的中立介面對映(`IExchangeClient` 等) | 未做 —— 見「未來」 |
| 帳務(`portfolio`)、組合單、預收 | 未做 —— 見「未來」 |

所有價格與數量都是 `decimal`,所有時間都是 UTC `DateTimeOffset`。預期的失敗一律回傳 `Result` / `Result<T>`,
錯誤碼一律 `shioaji.` 開頭,集中在 `ShioajiErrorCodes`,每一個都附中英說明。

## 安裝

```
dotnet add package Ozakboy.TradeKit.Shioaji
```

目標框架 `net10.0`。還要有官方的 `shioaji` 命令列工具(`uv tool install shioaji`,Windows 另有官方的
`install.ps1`),這個套件不會幫你安裝它。

## 快速開始

```csharp
var services = new ServiceCollection();
services.AddLogging();

services.AddShioaji(options =>
{
    options.BaseUri = new Uri("http://127.0.0.1:8080");
});

// 重連後要重放的東西:委託回報訂閱(證券與期貨各一次)與行情訂閱。
// Shioaji 斷線後必須重新訂閱,串流讀取器會在每次重連成功之後呼叫這個回呼。
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
        options.BindAddress = "127.0.0.1:8080";
    },
    provider => () => new ShioajiCredentials
    {
        ApiKey = Environment.GetEnvironmentVariable("MY_SHIOAJI_API_KEY")!,
        SecretKey = Environment.GetEnvironmentVariable("MY_SHIOAJI_SECRET_KEY")!,
    });

await using var provider = services.BuildServiceProvider();
```

```csharp
// 1. 啟動(或接手)官方 server。
var supervisor = provider.GetRequiredService<ShioajiServerSupervisor>();
var started = await supervisor.StartAsync(cancellationToken);

if (started.IsFailure)
{
    Console.WriteLine($"{started.Error.Code}: {started.Error.Message}");
    return;
}

// 2. 帳號,以及委託回報訂閱(證券與期貨各訂一次,否則 order_event 只有心跳)。
var auth = provider.GetRequiredService<ShioajiAuthClient>();
var accounts = (await auth.GetAccountsAsync(cancellationToken)).GetValueOrThrow();

foreach (var account in accounts.Where(a => a.IsSigned && a.AccountType is ShioajiAccountType.Stock or ShioajiAccountType.Futures))
{
    await auth.SubscribeTradeAsync(account, cancellationToken);
}

// 3. 訂閱行情。連續月合約(TXFR1)要先用 GetContractAsync 查出 TargetCode 再帶進來。
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

// 4. 讀串流。同一時間只能有一個消費端。
var reader = provider.GetRequiredService<ShioajiStreamReader>();

await foreach (var item in reader.ReadAsync(cancellationToken))
{
    if (item.IsFailure)
    {
        continue; // 讀不懂的事件:記下來,串流不會因此結束。
    }

    switch (item.GetValueOrThrow())
    {
        case ShioajiTickStk tick:
            Console.WriteLine($"{tick.Code} {tick.Close} @ {tick.Time:O}");
            break;
        case ShioajiOrderEvent order:
            Console.WriteLine($"回報 {order.EventId} {order.EventType}"); // 以 EventId 去重
            break;
        case ShioajiStreamLifecycleEvent { Kind: ShioajiStreamLifecycleKind.Disconnected }:
            break; // 斷線到下一個 Connected 之間的行情沒收到 —— 視為資料缺口。
    }
}

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
    Console.WriteLine($"{placed.Error.Code}: {placed.Error.Message}");
}
```

這一段範例有一個測試逐行照抄(`ReadmeQuickStartTests`),對著假伺服器跑;範例壞了,測試就會紅。

### 回補 1 分 K 與逐筆(0.2.0)

```csharp
// 收盤後回補。1 分 K 單次最多 30 天(含頭尾),逐筆一次一天。
// 盤中不要拿它們當即時報價:即時行情請訂閱串流。
var history = provider.GetRequiredService<ShioajiHistoryClient>();
var contract = new ShioajiContractRef { SecurityType = ShioajiSecurityType.Stock, Exchange = "TSE", Code = "2330" };

var bars = await history.GetKbarsAsync(contract, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 30), cancellationToken);

if (bars.IsSuccess)
{
    var page = bars.GetValueOrThrow();

    if (page.PossiblyTrafficExhausted)
    {
        // 空回應有兩種可能:真的沒有資料,或當日流量用完(券商不報錯、只回空值)。
        // 先查剩餘流量,再決定要不要把這一段存成「那天沒有行情」。
        var usage = (await auth.GetUsageAsync(cancellationToken)).GetValueOrThrow();
        Console.WriteLine($"剩餘 {usage.RemainingBytes} bytes");
    }

    foreach (var bar in page.Items)
    {
        Console.WriteLine($"{bar.Time:O} O{bar.Open} H{bar.High} L{bar.Low} C{bar.Close} V{bar.Volume}");
    }
}

var ticks = await history.GetTicksAsync(
    contract,
    new DateOnly(2026, 10, 30),
    ShioajiTicksQuery.Range(new TimeOnly(9, 0), new TimeOnly(9, 30)),
    cancellationToken);

// 本程序今天大概用了多少流量(回應本文的大小;券商自己的帳以 GetUsageAsync 為準)。
var traffic = provider.GetRequiredService<ShioajiTrafficMeter>().Snapshot();
Console.WriteLine($"今日已用約 {traffic.Bytes} bytes,下次重置 {traffic.NextResetAt:O}");
```

`auth` 是主範例裡的 `ShioajiAuthClient`。這段範例有對應的測試逐行照抄,保證能編譯執行。
**注意 `bar.Time` 是該分鐘的結束時刻**(例如台股每個交易日是 09:01 到 13:30),要存成開盤時刻請自行減 1 分鐘。
全市場日 OHLCV 是 `history.GetDailyQuotesAsync(date)`;用它之前請先讀「限制與未實測事項」裡關於它的警告。

### 歷史資料重點(0.2.0)

- **本地檢查先做、而且不花任何額度。**1 分 K 區間超過 30 個日曆日(含頭尾)回 `shioaji.history.range_too_long`;起始日晚於結束日回
  `shioaji.validation.invalid_argument`;快照超過 500 檔回 `shioaji.history.too_many_contracts`;逐筆條件不合法、商品類別不明、
  代碼空白也會被拒絕。這些都不佔預算、不會送到 server。
- **欄位陣列回應。**`kbars` 與 `ticks` 回 `{datetime: […], Open: […], …}`。欄位長度不一致就整筆回 `shioaji.history.malformed`:
  某一筆的收盤價對到別筆的時間,比沒有資料更糟。`datetime` 是 ISO 字串(不帶時區視為台北牆上時間,減 8 小時)或依大小判斷單位的
  epoch(秒、毫秒、微秒、奈秒)。數字由原始文字讀成 `decimal`。
- **空頁不等於沒有資料。**結果是 `ShioajiHistoryPage<T>`,帶 `Items`、`ResponseBytes`、`IsEmpty` 與 `PossiblyTrafficExhausted`。
  今天還沒查過 server 的用量時,空頁一律標成「可能用完」;呼叫過 `GetUsageAsync` 之後,旗標依「server 回報的剩餘量減去之後又用掉的」判斷。
- **每次呼叫在本地檢查之後佔一個 `MarketQuery` 額度**(10 秒 50 次),POST 永不重試。
- **流量計。**`ShioajiTrafficMeter` 是單例,每個經過 `ShioajiApiClient` 的回應都記進來(`Content-Length`,分塊傳輸時用實際讀到的位元組;
  非 2xx 也算;不含 SSE 串流與請求本文)。按台北流量日分桶,每日 08:00(= UTC 00:00)重置,公開 `TodayBytes`、`Reset()`、`Snapshot()`。
  **它是估計值**:券商自己的數字以 `GetUsageAsync` 為準,每次查到都會寫進流量計的 `ServerReportedBytes`。

## 幾個值得知道的設計決定

### 為什麼走官方 server,而不是自己寫 sidecar 或用社群套件

登入、CA 啟用、token 快取、Solace 重連都在官方 server 裡,而且文件明列 C# 走這條路。自寫 Python sidecar 要自己維護一份
會跟著券商改版的東西;社群套件違反這個家族的相依政策。所以這個套件只管 server 的**外面**,
並把「server 的行為」當成一個需要被量測、被假裝(測試用的假伺服器)、被防禦的對象。

### 憑證只走子行程的環境變數

`ShioajiServerSupervisor` 的建構式收一個 `Func<ShioajiCredentials>`,**只在啟動子行程的那一刻呼叫**,
值填進 `ProcessStartInfo.Environment`(`SJ_API_KEY`、`SJ_SEC_KEY`、`SJ_CA_PATH`、`SJ_CA_PASSWD`)。

- **不寫任何檔案。**不產生 `.env`,不把值放進命令列參數(那會出現在行程清單裡)。有一個測試在啟動後掃描整個 `HomePath`,
  確認沒有任何檔案含有憑證字面值。
- **不進日誌。**四個值一取得就登記到 `SecretMasker`;子行程的 stdout / stderr 逐行遮罩之後才寫入日誌(stdout 用
  Information、stderr 用 Warning)。遮罩是字面替換,所以**短於 8 個字元的值無法登記**(會大量誤遮一般文字),
  請使用夠長的憑證與密碼。
- `ShioajiCredentials.ToString()` 只印出各欄位有沒有值;`AdditionalEnvironment` 不接受那四個鍵,
  憑證只能走憑證提供者。
- 要求 HTTP 認證時(`ShioajiServerOptions.RequireAuth`,對應 `--auth`),用 `ShioajiOptions.UseBearerAuth` 讓每個請求帶
  `Authorization: Bearer <金鑰>:<密鑰>`;金鑰、密鑰與 `金鑰:密鑰` 三個字面值會登記到用戶端的遮罩器,錯誤本文、
  錯誤訊息與日誌裡出現它們都會被換掉(測試讓假伺服器把標頭原樣回顯,驗證遮罩生效)。
  對非本機位址用明文 `http` 送 Bearer 會在設定驗證時被拒絕。

### 管線:不簽章、不限流、只有 GET 會重試,POST 永不重試

- **不簽章。**這個 server 不用簽章,認證靠 Bearer 標頭。
- **不限流。**管線內的限流是「排隊」,而這裡需要的是「先擋下並明確告知這一筆沒送出」,所以限流是
  `ShioajiRateBudget` 的事,見下一節。
- **重試只對 GET。**所有 POST —— 下單、改價、改量、撤單、行情訂閱、`subscribe_trade` —— 都被標成非冪等,
  並額外釘上 `RetryPolicy.NoRetry`。兩道防線:萬一有人把管線的預設策略換成「什麼都重試」,下單仍然只會送一次。
  有測試對每一個下單端點證明「不管怎麼失敗,恰好送出一次」,而且把這個保護拿掉時那些測試會變紅。
- **「結果未知」會被標出來。**下單類請求逾時或遇到 5xx,錯誤的 `Data` 帶 `outcomeUnknown = true`:
  請求可能已經被券商收下,呼叫端必須去查委託(`ListTradesAsync`),不可重送。連線被拒(請求確定沒到達)與 4xx
  (明確拒絕)不帶這個標記。2xx 但回應讀不懂,是專用的 `shioaji.order.response_unreadable`,同樣帶標記。

### 本地限流預算與紅線

券商對超量的處置是**停 IP 與 ID,而且反覆登入無法解除**,所以預算的方向是保守的:寧可在本地多等,也不要碰處置線。
`ShioajiRateBudget` 用 `TimeProvider` 做滑動視窗;需要等的時間超過 `MaxWait`(預設 10 秒)就**直接失敗**
(`shioaji.rate_budget.exhausted`,暫時性),不會先睡一陣子才失敗 —— 下單的呼叫端需要立刻知道「這一筆沒送出」。

| 類別 | 上限 | 目前計入的呼叫 |
| --- | --- | --- |
| `MarketQuery` | 10 秒 50 次 | `ShioajiHistoryClient` 的每一次呼叫;`ListAllContractsAsync` 的每一頁 |
| `Portfolio` | 5 秒 25 次 | `GetUsageAsync`;`ListTradesAsync(refresh: true)` |
| `Order` | 10 秒 250 次 | 下單、改價、改量、撤單;`ListTradesAsync(refresh: false)` |
| `Subscription` | 同時 200 個 | `SubscribeAsync` +1,`UnsubscribeAsync` −1;重複訂同一筆不重複計,請求失敗會把剛計入的還回去 |

單一商品查詢讀的是 server 本機的商品檔,不算券商的行情查詢,所以不計入 `MarketQuery`;只有一口氣連續翻很多頁的
`ListAllContractsAsync` 每一頁佔一個額度。預設值就是券商公告的上限,調大等於把處置線的風險交給你。另外有三條公告的紅線不在預算裡、
而是要你自己遵守:**盤中禁止用 `ticks` / `snapshots` / `kbars` 當即時報價**(0.2.0 起有這幾個端點了,而且
**本套件不替你判斷現在是不是盤中**:`ShioajiHistoryClient` 是給收盤後回補用的,即時行情請走串流)、
同一個身分最多 5 條連線(聚合串流只算一條 SSE 連線)、每日流量上限(`GetUsageAsync` 可以查剩多少,`ShioajiTrafficMeter` 看本程序用了多少)。

### 本地先擋三種會「靜默失敗」的訂閱

- 連續月合約(代碼以 `R1` / `R2` 結尾)**沒有 `TargetCode`**:server 回 200,卻只會送心跳。本地直接拒絕
  `shioaji.subscription.target_code_required`。
- **指數訂閱 `Tick` 或 `BidAsk`**:指數只支援 `Quote`,拒絕 `shioaji.subscription.index_requires_quote`。
- **訂閱數超過 200**:拒絕 `shioaji.subscription.limit_exceeded`。

這三種都在請求送出之前拒絕,不佔用任何額度。

### 串流與重連語意

- 連**聚合串流** `GET /api/v1/stream/data`:一條連線帶全部具名事件、共用心跳。串流用另一個不經管線的
  `HttpClient`(管線會把整個回應讀完才回傳,SSE 永遠讀不完)。
- **斷線偵測有兩種:**連線結束 / 讀取失敗,以及 `StreamIdleTimeout`(預設 45 秒)內沒收到任何一行 —— 心跳與 SSE
  註解行都算「收到」。
- **重連:**指數退避(初始 1 秒、上限 30 秒、±20% 抖動);一條連線健康地收過資料之後退避重置,連資料都沒收到
  就斷的連線不重置,免得 server 一接受就關時每秒被重連打爆。
- **Shioaji 斷線後必須重新訂閱。**重連成功後會呼叫你注入的回呼(`Func<CancellationToken, Task<Result>>`,
  在 DI 容器裡註冊,或直接傳給建構式),用它重放行情訂閱(`ShioajiQuoteClient.ReplaySubscriptionsAsync`)與
  `subscribe_trade`。回呼回傳失敗或丟出可預期的例外只寫日誌,串流照常繼續。
- **生命週期事件**(`ShioajiStreamLifecycleEvent`:`Connected` / `Disconnected` / `Reconnecting`,帶重連編號與原因)
  與行情從同一個 `IAsyncEnumerable` 送出。**從 `Disconnected` 到下一個 `Connected` 之間的行情沒有收到**,
  策略應視為資料缺口。
- **有界佇列**(預設 10,000):滿了**丟棄最舊的**並累計到 `DroppedEvents`,絕不阻塞讀取。佇列裡的生命週期事件也可能被丟,
  所以判斷缺口請同時看 `DroppedEvents`。
- **未知格式只記一次。**註解行、不認得的欄位名、沒有冒號的行,每個種類第一次出現時寫一行日誌,第一個心跳的原文也是 ——
  這是為了讓第一次連真 server 時能直接從日誌看出心跳到底長什麼樣。

### 事件分類

| 事件 | 型別 |
| --- | --- |
| `tick_stk` / `bidask_stk` / `quote_stk` | `ShioajiTickStk` / `ShioajiBidAskStk` / `ShioajiQuoteStk` |
| `tick_fop` / `bidask_fop` / `quote_fop` | `ShioajiTickFop` / `ShioajiBidAskFop` / `ShioajiQuoteFop` |
| `quote_idx` | `ShioajiQuoteIdx` |
| `order_event` | `ShioajiOrderEvent`(帶 `EventId` 供去重、`RawJson`) |
| `heartbeat` / `ping` / 事件名空白 / 事件名不認得且 `data` 不是 JSON 物件 | `ShioajiHeartbeat`(保留原文) |
| 事件名不認得但 `data` 是 JSON 物件(例如 `kbar`) | `ShioajiUnknownEvent`(原樣送出,不丟棄) |
| 已知的行情 / 委託事件名卻讀不懂 | 串流上的**失敗**(`shioaji.stream.malformed_event`),不當心跳吞掉 |
| 連線生命週期 | `ShioajiStreamLifecycleEvent` |

五檔是 `IReadOnlyList<decimal>`,**長度剛好 5**;長度不對的那一則事件失敗,不把殘缺的五檔送給策略。
必要欄位(代碼、時間、價格、五檔陣列、`event_id`)缺漏一律失敗,**絕不補 0** —— 0 會被讀成「成交價是零元」。

### 數字與時間

- **JSON 的數字一律由原始文字讀成 `decimal`**,中間沒有任何一步經過 `double`;字串型數字(`"29.8"`)也能讀。
  送出時用 `Precision.ToPlainString` 寫成沒有科學記號、沒有尾隨零的原始數字。測試用 `29.8` 與 25 位小數證明。
- **時間:**Shioaji 的 `date`、`time`、`datetime` 是**台北牆上時間**,以固定 UTC+08:00 換成 UTC(台灣沒有日光節約時間,
  所以不查時區資料庫,也不受執行機器的時區影響)。`datetime` 若自帶 `Z` 或 `±hh:mm` 以它為準。
  數字型的時間戳(`order_ts`、成交的 `ts`)視為 Unix 秒(含小數),大於 1e11 的視為毫秒。
  `ShioajiStreamEvent.ReceivedAt` 是本套件收到的時刻,`ShioajiMarketEvent.Time` 才是交易所的撮合時間。

### Supervisor 的行為

- **接手(Attach):**啟動時 `/api/v1/info` 已經有回應、且 `simulation` 與 `Production` 一致,視為外部已啟動,
  進入 `Attached`,**不管理那個行程**,停止時也不動它(也不會去取憑證)。
- **模式不符就拒絕:**有回應但 `simulation` 與 `Production` 不一致,回 `shioaji.server.mode_mismatch`。
  本套件**不會終止別人啟動的 server**,也不會在它上面下單。自己啟動的行程回報了不同的模式(「想連模擬卻連到正式」)
  同樣拒絕並終止它。
- **啟動:**`shioaji server start --no-open`(不帶 `--no-open` 會自動開瀏覽器,所以一律帶)加 `--production`、`--auth`、
  `--receive-window` 視設定而定。模式、位址、認證一律明確以環境變數設定,不繼承使用者機器上可能殘留的同名變數。
  逾時 → 終止並回 `shioaji.server.start_timeout`,錯誤的 `Data` 帶最後 20 行輸出(已遮罩);行程在就緒前退出 →
  `shioaji.server.exited_early`,帶結束碼與最後 20 行輸出。
- **重啟:**就緒之後行程非預期退出,指數退避重啟(`RestartBackoff`,上限 60 秒);連續超過 `MaxRestarts`(預設 5)
  進入 `Faulted` 並放棄 —— 需要人工介入。行程穩定跑過 `StableAfter` 之後再退出,連續計數歸零。
- **停止:**先 `shioaji server stop`(等 `StopTimeout`,預設 10 秒),沒停就終止整棵行程樹。
- **輸出剝色碼:**真的 `shioaji` 輸出帶 ANSI 色碼。stdout / stderr 逐行先剝掉 ANSI escape sequence,再遮罩,
  才寫日誌;「最後 20 行」的錯誤資料用的也是剝過的版本。
- **認證失敗是專用錯誤:**行程在就緒前退出,且輸出含 `Authentication failed`(不分大小寫),或 `login validation error`、
  `invalid secret_key`、`invalid api_key` → `shioaji.server.auth_failed`,`Error.Data["detail"]` 帶那一行(已剝色碼、已遮罩)。
  同一組憑證重試只會得到同一個答案,所以**直接進入 `Faulted`,不進重啟迴圈**;重啟過程中遇到認證失敗同樣直接 `Faulted`。
- **`secret_key` 必須是 base58:**`ShioajiCredentials.Validate()`(supervisor 在啟動行程之前會呼叫)檢查 `SecretKey` 只含 base58 字元,
  含 `0`、`O`、`I`、`l`、`_`、`-` 等字元就回 `shioaji.server.invalid_credentials`,**不啟動行程**,訊息只說「格式不符(base58)」,
  不回顯任何字元。`ApiKey` 的格式未知,不檢查。
- **日誌檔位置:**啟動時 stdout 的 `server log: <路徑>` 那一行會解析進 `ShioajiServerStatus.LogFilePath`,讓宿主把路徑顯示給使用者;
  解析不到就是 `null`。
- `ShioajiServerStatus` 快照:`State`、`ProcessId`、`StartedAt`、`Restarts`、`LastExitCode`、`LastError`、`LogFilePath`。

## 官方 server 的已知行為

以下是對真的 `shioaji server start --no-open` 觀察到、文件沒有寫明的行為。

**行程與輸出**

- **server 是前景行程,不會自行脫離。**登入失敗時行程直接退出,stdout 最後一行是 `Error: Authentication failed: …`。
  所以「行程以 0 結束但 server 仍在回應 → Attached」那條路徑實務上不會發生,程式裡保留只是防禦。
- **輸出帶 ANSI 色碼。**監督器寫進日誌前會先剝掉。
- **啟動時 stdout 第一行是 `server log: <SJ_HOME_PATH>\logs\server-<port>\server.<UTC 日期>.log`。**
- **`secret_key` 必須是 base58**;含 `_` 之類的字元,server 直接登入失敗退出。
- **`shioaji server status` 的輸出是 toon 格式**,server 沒在跑時是四行:
  ```
  running: false
  pid: null
  port: 8080
  healthy: false
  ```
  本套件**沒有**解析這個輸出(健康判斷走 `GET /api/v1/info`),這裡只是記下格式。

**歷史資料(模擬環境)**

- **1 分 K 的 `datetime` 是該分鐘的結束時刻**,不帶時區的台北時間。台股每個交易日是 09:01 到 13:30:
  09:01 那根含開盤集合競價,13:30 那根含收盤集合競價;沒有成交的分鐘沒有那一根。
- **單位:**股票的 `Volume` 是張、`Amount` 是元(價 × 股數);期貨的 `Volume` 是口。
- **指數與期貨連續月也有 1 分 K。**期貨含夜盤(15:00 至次日 05:00)。
- **流量參考:**一檔股票 30 天的 1 分 K 約 0.3–0.5 MB;一檔熱門股一天的整日逐筆約 0.4 MB。
- **`GET /api/v1/auth/usage` 的 `bytes` 更新有延遲**(連續請求後十幾秒才跟上)。判斷剩餘流量請以本機的
  `ShioajiTrafficMeter` 為主、server 回報的數字為輔。
- **商品詳細資料**的參考價、漲停、跌停在股票與期貨都有;指數只有參考價。期貨連續月的 `target_code` 是當月實際合約。

## 限制與未實測事項

下面每一項都是文件沒寫、或還沒在真 server 上確認過的行為。套件的處理方式是防禦性的 —— 但防禦性不等於正確,
第一次在你的環境使用時,請對照日誌逐項確認:

- **心跳的格式與頻率。**文件沒寫。套件把事件名 `heartbeat` / `ping`、空白事件名、非 JSON 物件的未知事件都當心跳並保留原文,
  SSE 註解行也算「活著」。第一個心跳的原文與第一個註解行會寫進日誌。
- **非 2xx 的錯誤本文結構。**文件沒寫。套件把本文前 2 KB(經遮罩)放進 `Error.Data["body"]`,並只在 JSON 物件裡找
  `detail` / `message` / `error` / `msg` 這幾個字串欄位湊一句說明。
- **`order_event` 的 JSON 形狀。**文件只有欄位清單,沒有範例。成交那部分是放在頂層還是巢狀在 `deal` 之下不明,
  套件兩種都收;`EventType` 由內容推斷(明確的 `event_type` 欄位優先)。**完整原文一律保留在 `RawJson`**,推斷錯了可以自己再讀。
- **`place_order` / `update_price` / `update_qty` / `cancel_order` 的回應。**`place_order` 依文件讀成 `Trade`,
  只有 `order.id` 是必要欄位;2xx 但讀不出來是 `shioaji.order.response_unreadable`(單可能已成立)。
  改價 / 改量 / 撤單的回應格式沒有說明,所以 2xx 一律視為成功,讀得出 `Trade` 就帶回去,讀不出就回 `null` ——
  不能因為回應長得不一樣,就把一筆已經生效的撤單報成失敗。
- **`subscribe_trade` 的回應。**文件沒寫,2xx 即成功,不讀本文。
- **`/api/v1/stream/status` 的 `timestamp` 格式。**假設是 Unix 秒或日期時間字串,讀不懂時為 `null`。
- **欄位名。**`tick_stk` 的 `closing_oddlot_*` / `fixed_trade_*`、`quote_stk` 的 `avail_borrowing`、各種 `*_fop` 的細節欄位、
  `quote_idx` 的欄位,文件只寫了萬用字元或根本沒寫;名稱取自 Python SDK,**未在 HTTP 上驗證**。對不上時那個欄位是 `null`,
  不會讓事件失敗(只有代碼、時間、價格與五檔是必要的)。
- **全市場日 OHLCV 的路徑與回應形狀(0.2.0)。**文件只提到命令列的 `shioaji data daily-quotes --date YYYY-MM-DD --exclude-warrant`,
  沒有寫 HTTP 路徑。`GetDailyQuotesAsync` 對 `ShioajiOptions.DailyQuotesPath`(預設 **`/api/v1/data/daily_quotes`,未驗證,尚未對真 server 的 OpenAPI 確認**)
  送 `POST`,body 是 `{date, exclude_warrant}`,並假設回應是物件陣列、每筆至少有 `code`、`open`、`high`、`low`、`close`、`volume`;
  多餘的欄位在 `ShioajiDailyQuote.Raw`。路徑不對時回 `shioaji.http.404`:到 server 的 `/docs` 找真正的路徑,再設定 `DailyQuotesPath`。
- **其他查詢端點的形狀(0.2.0)。**只有 `kbars` 與 `ticks` 的形狀有文件(帶欄位名的欄位陣列),也已在模擬環境確認。`snapshots`、`scanner`、`short_stock_sources`、
  `credit_enquire` 與兩個監管清單讀成「物件陣列」(或包著陣列的物件;監管清單另收欄位陣列物件,Python SDK 就是這樣回的);
  欄位名取自 Python SDK,**未在 HTTP 上驗證**,沒有對映成屬性的內容都留在每一筆的 `Raw`。`short_stock_sources` 與 `credit_enquire`
  的請求 body(`{contracts: [...]}`)是比照 `snapshots` 推定的。
- **流量用完時「空值」長什麼樣。**文件只說行情查詢回「空值」,沒說那是什麼。空本文、`null`、`[]`、`{}`、欄位全是 `null`、欄位全是空陣列
  一律讀成空頁;若 server 用別的方式回應(例如錯誤本文),就會以那個錯誤浮現。
- **epoch 型的 `datetime`(0.2.0)。**數字型的 `datetime` 視為真正的 Unix epoch(UTC)。已知 Python SDK 的奈秒 `ts` 是把台北牆上時間當成 UTC 寫的;
  若 HTTP server 原樣轉出,這種格子會晚 8 小時。ISO 字串(本套件預期的形式)不受影響。
- **1 分 K 的 30 天上限含不含頭尾。**券商寫「≤ 30 天」;本套件採較保守的讀法,含頭尾共 30 個日曆日。
- **Attached 防禦路徑。**server 實際上是前景行程(見上一節),但程式裡仍保留「行程以結束碼 0 結束但 server 仍在回應 → Attached,
  停止時呼叫 `shioaji server stop`」那條路徑,它只用腳本化的探測測過。
- **`server stop` 的行為、`--port` 參數、SSE 連線是否計入 5 條登入連線、HTTP / SSE 實際延遲。**都沒量過。
- **`SJ_AGENT_HARNESS`**(語意未知)只提供透傳,走 `ShioajiServerOptions.AdditionalEnvironment`。
- **Windows 以外的平台。**測試只在 Windows 跑過。`ShioajiServerSupervisor` 的行程管理與 `HttpListener` 假伺服器理論上跨平台,
  CI 設定檔也是 ubuntu,但**沒有實際跑過**。

## 未來

這一版刻意不做、但會在後續版本補上的:

- 實作 `TradeKit.Abstractions` 的中立介面:`IExchangeClient`、`IMarketDataFeed`、`IUserDataFeed`。
- 帳務(`portfolio`):部位、損益、交割、保證金、交易額度。
- 用程式碼強制「盤中禁止拿歷史端點當即時報價」(0.2.0 只在文件寫明這條規則、交給呼叫端決定;要強制需要交易日曆)。
- 組合單、預收款項 / 預收股票、`watchlist`。
- 加值串流(`calculated_index`、`index_components`、`kbar`、`scanner`):目前它們會以 `ShioajiUnknownEvent` 原樣送出。

## 測試

單元與合約測試全程不連外網,也不需要任何憑證:

- **`FakeShioajiHttpServer`**(`tests/.../Support`)用 BCL 的 `HttpListener` 在 `127.0.0.1:<隨機 port>` 起一個 server 替身,
  可以註冊路由、開 SSE 端點推事件、模擬斷線、記錄收到的請求。它的行為比照文件(`event:` + `data:` 兩行格式、
  未訂閱只給心跳、連續月沒帶 `target_code` 就只有心跳),每個行為在註解裡寫明依據的文件段落,而且是 `public` 類別,
  可以複製或引用到別的專案。
- **`FakeShioajiServerExe`**(`tests/FakeShioajiServerExe`)是一個最小的 console 專案,模擬 `shioaji` 命令列:
  啟動後開 HTTP 回 `/api/v1/info`、收到 `server stop` 就退出、立刻崩潰、永遠不就緒、第一次崩潰之後正常、
  每次都崩潰。監督器的啟動、逾時、重啟、Faulted、強制終止、遮罩與「憑證不落地」都是對它測的。
- **Fixtures 不是錄製的。**`tests/.../Fixtures/PROVENANCE.md` 逐檔寫明建構依據:沒有憑證,所以全部是依文件欄位清單手工建構。
  這代表測試證明的是「套件正確讀懂文件描述的形狀」,**不是**「真 server 真的長這樣」。

```
dotnet test -c Release
dotnet test -c Release -- --coverage --coverage-output-format cobertura
```

## 安全

- 套件不含任何預設憑證,也不會把憑證寫進檔案、日誌或 `ToString`。
- 登入券商的憑證只走子行程環境變數,見上面的「憑證只走子行程的環境變數」。
- Bearer 金鑰登記在用戶端的遮罩器上,`Error` 的訊息、每一筆 `Data` 與例外文字都不會帶著它們,
  即使 server 把標頭回顯在錯誤本文裡也一樣。
- 串流重連的原因文字、子行程輸出、日誌裡的未知格式內容,在寫出之前都會過遮罩器。
- 個資(`ShioajiAccount.PersonId`、`Username`)只在記憶體裡,套件的日誌不會寫它們;你自己的日誌請也不要寫。

## 相依政策

只允許 .NET BCL、Microsoft 官方套件與 `Ozakboy.*` 自研套件,判定看**遞移相依**。

| 套件 | 版本 | 用途 |
| --- | --- | --- |
| `Ozakboy.Http` | 0.3.4 | 請求管線(重試、逾時、脫敏日誌、遮罩) |
| `Ozakboy.Core.Abstractions` | 0.3.0 | `Result` / `Error` / `Precision` |
| `Ozakboy.Security` | 0.1.1 | `SecretMasker` |
| `Ozakboy.TradeKit.Abstractions` | 0.8.0 | 本版只引用、不實作;中立介面對映見「未來」 |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.14 | DI 註冊 |
| `Microsoft.Extensions.Http` | 10.0.14 | 具名 `HttpClient` |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.14 | 日誌 |

測試專案另用 MSTest 與 `Microsoft.Extensions.TimeProvider.Testing`,走 Microsoft.Testing.Platform 而不是舊的 VSTest
(VSTest 那條路徑會遞移帶進第三方的 Newtonsoft.Json)。

## 授權

MIT,見 [LICENSE](LICENSE)。
