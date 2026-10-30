# Changelog

本檔記錄本套件所有值得注意的變更。
All notable changes to this package are documented here.

格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/),版號依循
[語意化版本](https://semver.org/lang/zh-TW/)。
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versioning follows
[Semantic Versioning](https://semver.org/).

## [0.1.1] - 2026-10-30

**依對真的 `shioaji server start --no-open` 的第一次實測(假憑證)調整:剝 ANSI 色碼、認證失敗專用錯誤碼、日誌檔路徑、本地檢查 `secret_key` 的 base58 格式。**
**Adjusted to the first measurement of the real `shioaji server start --no-open` (fake credentials): stripping ANSI colour
codes, a dedicated authentication-failure error, the log file path, and a local base58 check of `secret_key`.**

實測方式:用假憑證對真的 `shioaji.exe` 跑一次(`SJ_HTTP_ADDR=127.0.0.1:18080`、`SJ_HOME_PATH` 指到暫存目錄)。沒有成功登入過,
所以只有啟動與登入失敗的行為是實測,其餘仍是文件推定。
Method: one run of the real `shioaji.exe` with fake credentials. No login succeeded, so only start-up and login-failure
behaviour is measured; the rest is still inferred from the documentation.

### 新增功能 / Added

- **`shioaji.server.auth_failed`**(`ShioajiErrorCodes.ServerAuthFailed`):行程在就緒前退出,且最後 20 行輸出含 `Authentication failed`
  (不分大小寫),或 `login validation error` / `invalid secret_key` / `invalid api_key` 時回傳。`Error.Data["detail"]`
  (`ShioajiErrorDataKeys.Detail`)帶那一行,已剝色碼、已遮罩,另有 `exitCode`。分類 `Unauthorized`,非暫時性。
  Returned when the process exits before readiness and its last 20 output lines contain `Authentication failed`
  (case-insensitive), `login validation error`, `invalid secret_key` or `invalid api_key`. `Error.Data["detail"]` carries that
  line, stripped and masked, plus `exitCode`. Category `Unauthorized`, not transient.
- **`shioaji.server.invalid_credentials`**(`ShioajiErrorCodes.ServerInvalidCredentials`)與 **`ShioajiCredentials.Validate()`**:
  `SecretKey` 含 base58 以外的字元(`0`、`O`、`I`、`l`、`_`、`-` 等,或空白)時,supervisor 在啟動行程之前就回傳這個錯誤,
  訊息只說「格式不符(base58)」,不回顯任何字元。`ApiKey` 的格式未知,不檢查。
  A `SecretKey` with a character outside base58 (`0`, `O`, `I`, `l`, `_`, `-` and the like, or blank) is refused by the
  supervisor before any process starts; the message only says the format does not match (base58) and echoes no character.
  The format of `ApiKey` is unknown and it is not checked.
- **`ShioajiServerStatus.LogFilePath`**(`string?`):解析子行程 stdout 的 `server log: <路徑>` 那一行;解析不到、接手別人啟動的 server、
  或還沒啟動過行程時為 `null`。
  Parsed from the child's `server log: <path>` stdout line; `null` when it cannot be parsed, for an attached server, or
  before any process was started.

### 功能優化 / Changed

- **supervisor 逐行剝除 ANSI escape sequence**(`ESC [ … 字母`)之後才遮罩並寫入日誌;錯誤資料裡的「最後 20 行」也是剝過的版本。
  The supervisor strips ANSI escape sequences from every output line before masking and logging it; the "last 20 lines" in error
  data are the stripped text too.
- **認證失敗直接進入 `Faulted`,不進退避重啟迴圈**;重啟過程中遇到認證失敗或憑證格式不符同樣直接 `Faulted` 並停止重啟。
  An authentication failure goes straight to `Faulted` without entering the backoff restart loop; one met during a restart, or a
  malformed credential, also ends the loop in `Faulted`.
- **行為變更:**格式不符的 `SecretKey` 以前會啟動一個行程、再因登入失敗退出,現在在本地就回 `shioaji.server.invalid_credentials`;
  `ShioajiServerStatus` 多了必填屬性 `LogFilePath`(以物件初始化器建構它的程式碼需要補上)。
  Behaviour change: a malformed `SecretKey` used to start a process that then failed to log in and is now refused locally;
  `ShioajiServerStatus` gains the required property `LogFilePath` (code that builds it with an object initializer must set it).

### 實測事實 / Measured facts

- `shioaji server start --no-open` 是**前景行程,不脫離**;登入失敗時直接退出,stdout 最後一行是
  `Error: Authentication failed: login validation error: invalid secret_key (base58 decode failed): …`。因此「行程以 0 結束但 server 仍在回應 → Attached」
  那條路徑實務上不會發生,保留只是防禦。
  `shioaji server start --no-open` is a **foreground process that does not detach**; on a failed login it exits at once with
  `Error: Authentication failed: …` as the last stdout line. The "exits 0 while still answering → Attached" path therefore does
  not happen in practice and stays only as a defence.
- 輸出帶 ANSI 色碼;啟動時 stdout 第一行是 `server log: <SJ_HOME_PATH>\logs\server-<port>\server.<UTC 日期>.log`;`secret_key` 必須是 base58。
  The output carries ANSI colour codes; the first stdout line at start is the `server log:` line; `secret_key` must be base58.
- `shioaji server status` 的輸出是 toon 格式(`running: false` / `pid: null` / `port: 18080` / `healthy: false`);本套件不解析它,只記錄。
  The output of `shioaji server status` is in toon format; this package does not parse it and only records the format.

### 技術改進 / Technical

- `FakeShioajiServerExe` 新增 `auth-fail` 行為(印實測的錯誤行到 stderr 後以 1 結束)、`FAKE_SHIOAJI_AFTER=auth-fail`(重啟後登入被拒)、
  `FAKE_SHIOAJI_NO_LOG_LINE`,並且所有行為都先印 `server log:` 與帶色碼的一行,和真 server 一致。
  `FakeShioajiServerExe` gains an `auth-fail` behaviour, `FAKE_SHIOAJI_AFTER=auth-fail` and `FAKE_SHIOAJI_NO_LOG_LINE`, and
  every behaviour now prints a `server log:` line and a coloured line first, as the real server does.

## [0.1.0] - 2026-10-30

**第一版(M0):讓桌面系統能啟動永豐金 Shioaji 的官方本機 server、看到行情、下一筆模擬單並收到回報。**
**The first release (M0): let a desktop application start SinoPac Shioaji's official local server, see market data,
place one simulated order and receive its report.**

這一版**從未對真的 `shioaji server` 跑過**(撰寫時沒有憑證);所有測試都對著本機假伺服器與假的可執行檔。
文件沒寫、沒量過的行為全部列在 README 的「限制與未實測事項」,套件對它們一律採防禦性處理並把第一次看到的原文寫進日誌。
This release has **never been run against a real `shioaji server`** (there were no credentials while it was
written); every test runs against a local fake server and a fake executable. Behaviour the documentation does not
describe and nobody measured is listed in the README's "Limits and unmeasured behaviour"; the package handles each
defensively and logs the first original text it sees.

### 新增功能 / Added

- **`ShioajiServerSupervisor`**:啟動並監督官方 server 行程(`shioaji server start --no-open`)。憑證由建構時注入的
  `Func<ShioajiCredentials>` 提供,**只在啟動子行程的那一刻呼叫**,值只走子行程的環境變數(`SJ_API_KEY`、`SJ_SEC_KEY`、
  `SJ_CA_PATH`、`SJ_CA_PASSWD`):不寫任何檔案、不進日誌;四個值一取得就登記進 `SecretMasker`,子行程的 stdout / stderr
  逐行遮罩之後才寫入日誌。啟動時 `/api/v1/info` 已有回應且 `simulation` 與 `Production` 一致 → `Attached`,不管理行程;
  模式不符 → 拒絕,絕不終止別人啟動的 server;啟動逾時 → 終止並回 `shioaji.server.start_timeout`(附最後 20 行遮罩輸出);
  就緒後非預期退出 → 指數退避重啟,連續超過 `MaxRestarts` 進入 `Faulted`;停止先 `shioaji server stop`,逾時終止整棵行程樹。
  `Status` 快照含 `State`、`ProcessId`、`StartedAt`、`Restarts`、`LastExitCode`、`LastError`。
  Starts and supervises the official server process. Credentials come from a `Func<ShioajiCredentials>` called only at
  the instant the child starts and travel only through the child's environment variables — no file, no log; all four
  values are registered on the `SecretMasker` as soon as they are obtained and the child's output is masked line by
  line before it is logged. An already-answering server whose `simulation` agrees with `Production` is attached to
  without managing its process; a mode mismatch is refused and a server someone else started is never killed; a
  start timeout kills the process and returns `shioaji.server.start_timeout` with the last 20 masked output lines;
  an unexpected exit after readiness is restarted with exponential backoff, and more than `MaxRestarts` consecutive
  restarts put it in `Faulted`; a stop calls `shioaji server stop` first and kills the whole process tree after the
  timeout.
- **`ShioajiApiClient`**(低階)與四個區塊 client:`ShioajiAuthClient`(`/api/v1/info`、帳號、流量、串流狀態、
  `subscribe_trade` / `unsubscribe_trade`)、`ShioajiContractClient`(單一商品、詳細資訊含 `day_trade`、分頁列表、期貨列表)、
  `ShioajiQuoteClient`(行情訂閱 / 取消與重放)、`ShioajiOrderClient`(股票與期貨下單、改價、改量、撤單、列出委託)。
  管線**不簽章、不限流、重試只對 GET、POST 永不重試**;非 2xx → `shioaji.http.<狀態碼>`,回應本文先遮罩再截斷到 2 KB 放進
  `Error.Data`;連線失敗 → `shioaji.server_unreachable`(暫時性);逾時 → `shioaji.request_timeout`。
  The low-level client and four area clients. The pipeline neither signs nor rate-limits, retries GET only and never
  retries a POST; a non-2xx answer becomes `shioaji.http.<status>` with the body masked and then truncated to 2 KB
  in `Error.Data`; a failed connection is `shioaji.server_unreachable` (transient) and a timeout is
  `shioaji.request_timeout`.
- **下單類請求的「結果未知」標記**:逾時與 5xx 的失敗在 `Error.Data` 帶 `outcomeUnknown = true`,提醒呼叫端單可能已被券商收下,
  應查委託而不是重送;連線被拒與 4xx 不帶。2xx 但回應讀不懂是專用的 `shioaji.order.response_unreadable`。
  The unknown-outcome flag for order-type requests: a timeout or 5xx failure carries `outcomeUnknown = true` in
  `Error.Data`, telling the caller the order may have been accepted and should be looked up rather than resent; a
  refused connection and a 4xx do not carry it. A 2xx with an unreadable body is the dedicated
  `shioaji.order.response_unreadable`.
- **本地先擋的請求**:連續月合約(`R1` / `R2`)沒帶 `TargetCode` → `shioaji.subscription.target_code_required`(server 會回 200
  卻只送心跳);指數訂閱 `Tick` / `BidAsk` → `shioaji.subscription.index_requires_quote`;訂閱數超過 200 →
  `shioaji.subscription.limit_exceeded`;委託的 `custom_field` 超過 6 個英數字元 → `shioaji.order.invalid_custom_field`;
  數量、價格、列舉值不合法 → `shioaji.order.invalid_request`。全部在請求送出之前拒絕。
  Refused locally: a continuous-month contract without a `TargetCode`, an index subscribed for `Tick` / `BidAsk`,
  more than 200 subscriptions, an order `custom_field` longer than 6 alphanumerics, and an invalid quantity, price or
  enum value — all before the request leaves.
- **`ShioajiStreamReader`**:讀聚合 SSE 串流 `GET /api/v1/stream/data`,`IAsyncEnumerable<Result<ShioajiStreamEvent>>`,
  同一時間一個消費端。事件型別:`ShioajiTickStk`、`ShioajiBidAskStk`、`ShioajiQuoteStk`、`ShioajiTickFop`、`ShioajiBidAskFop`、
  `ShioajiQuoteFop`、`ShioajiQuoteIdx`、`ShioajiOrderEvent`(含 `EventId` 供去重與 `RawJson`)、`ShioajiHeartbeat`、
  `ShioajiUnknownEvent`、`ShioajiStreamLifecycleEvent`。連線結束、讀取失敗或 `StreamIdleTimeout` 內沒收到任何一行 → 退避重連;
  重連成功後呼叫建構時注入的 `Func<CancellationToken, Task<Result>>` 回呼(重放訂閱與 `subscribe_trade`),失敗只寫日誌;
  連線 / 斷線 / 重連以 `ShioajiStreamLifecycleEvent` 從同一個序列送出;佇列有界(預設 10,000),滿了**丟棄最舊的**並累計到
  `DroppedEvents`;`Status` 快照含 `State`、`ConnectedAt`、`LastEventAt`、`Reconnects`、`DroppedEvents`。
  Reads the aggregate SSE stream as an `IAsyncEnumerable` for one consumer at a time. A dropped connection, a failed
  read, or no line at all within `StreamIdleTimeout` reconnects with backoff; the injected callback runs after each
  successful reconnection to replay subscriptions and `subscribe_trade`, and a failure is only logged; lifecycle
  events come through the same sequence; the queue is bounded and drops the oldest, counting in `DroppedEvents`.
- **事件解析**:價格與數量一律由 JSON 原始文字讀成 `decimal`(絕不經過 `double`),字串型數字也能讀;台北時間的 `date` +
  `time`(缺漏時用 `datetime`)以固定 UTC+08:00 轉成 UTC;五檔是長度剛好 5 的 `IReadOnlyList<decimal>`;必要欄位缺漏或
  數值無法解析一律失敗,絕不補 0;心跳格式未知,凡事件名為 `heartbeat` / `ping`、事件名空白、或事件名不認得且 `data` 不是
  JSON 物件的都歸為 `ShioajiHeartbeat` 並保留原文;事件名不認得但 `data` 是 JSON 物件的以 `ShioajiUnknownEvent` 原樣送出;
  已知事件名卻讀不懂的是串流上的失敗,不當心跳吞掉。
  Prices and quantities are always parsed from the raw JSON text into `decimal`, never through `double`, and
  string-typed numbers read too; the Taipei `date` + `time` (or `datetime`) convert to UTC at a fixed UTC+08:00; a book
  is exactly 5 levels; a missing required field or an unreadable number is a failure and never zero; a heartbeat of
  unknown format keeps its original text; an unrecognised name with a JSON-object `data` passes through as
  `ShioajiUnknownEvent`; a known name that cannot be read is a failure on the stream, never swallowed as a heartbeat.
- **`ShioajiRateBudget`**:本地滑動視窗預算(以 `TimeProvider` 計時),所有會打到券商的 client 呼叫先過它:行情查詢 10 秒 50
  次、帳務 5 秒 25 次、委託 10 秒 250 次、訂閱同時 200 個;需要等的時間超過 `MaxWait`(預設 10 秒)就直接失敗
  `shioaji.rate_budget.exhausted`,不會先睡再失敗。
  A local sliding-window budget over a `TimeProvider`, which every client call that reaches the broker passes first:
  50 market queries per 10 s, 25 account queries per 5 s, 250 order operations per 10 s, and 200 concurrent
  subscriptions; a wait beyond `MaxWait` (10 s by default) fails at once with `shioaji.rate_budget.exhausted`.
- **`AddShioaji` / `AddShioajiServerSupervisor`**:註冊選項、經 `Ozakboy.Http` 管線的具名 `HttpClient`、不經管線的串流用
  `HttpClient`、全部 client、串流讀取器與預算,皆為單例。`UseBearerAuth` 開啟時,金鑰、密鑰與 `金鑰:密鑰` 三個字面值登記到
  用戶端的遮罩器;對非本機位址用明文 `http` 送 Bearer 會在設定驗證時被拒絕。
  Register the options, a named `HttpClient` through the `Ozakboy.Http` pipeline, a stream `HttpClient` that bypasses
  it, every client, the stream reader and the budget, all as singletons. With `UseBearerAuth` the key, the secret and
  the literal `key:secret` are registered on the client's masker, and sending the bearer credential over plain `http`
  to a non-loopback address is refused at validation.
- **`ShioajiErrorCodes` / `ShioajiErrorDataKeys`**:全部錯誤碼(`shioaji.` 開頭)與 `Error.Data` 鍵名集中成公開常數,
  每個附中英說明。
  Every error code (prefix `shioaji.`) and every `Error.Data` key as a public constant, each documented in Chinese
  and English.

### 技術改進 / Technical

- **測試:**MSTest on Microsoft.Testing.Platform。`FakeShioajiHttpServer`(`HttpListener` 實作,行為與依據的文件段落寫在註解)、
  `FakeShioajiServerExe`(假的可執行檔,測試監督器的啟動、逾時、重啟、Faulted、強制終止、遮罩與憑證不落地),以及一個逐行照抄
  README 快速開始的測試。Fixtures **不是錄製的**,逐檔來源見 `Fixtures/PROVENANCE.md`。
  Tests: MSTest on Microsoft.Testing.Platform, a `FakeShioajiHttpServer` built on `HttpListener` whose behaviours cite
  the documentation sections they rest on, a `FakeShioajiServerExe` fake executable, and a test that copies the
  README quick start line by line. The fixtures are **not recordings**; see `Fixtures/PROVENANCE.md` for each one's
  source.
- **相依:**`Ozakboy.Http` 0.3.4、`Ozakboy.Core.Abstractions` 0.3.0、`Ozakboy.Security` 0.1.1、
  `Ozakboy.TradeKit.Abstractions` 0.8.0(本版只引用、不實作其介面)與 `Microsoft.Extensions.*` 10.0.14;
  遞移相依樹中沒有任何第三方社群套件。
  Dependencies: `Ozakboy.Http` 0.3.4, `Ozakboy.Core.Abstractions` 0.3.0, `Ozakboy.Security` 0.1.1,
  `Ozakboy.TradeKit.Abstractions` 0.8.0 (referenced, its interfaces not implemented in this release) and
  `Microsoft.Extensions.*` 10.0.14; the transitive graph contains no third-party community package.

### 已知限制 / Known limitations

- 從未對真的 `shioaji server` 跑過;心跳格式、非 2xx 錯誤本文、`order_event` 的 JSON 形狀、改價 / 改量 / 撤單與
  `subscribe_trade` 的回應、`stream/status` 的時間戳格式、數個欄位名、`server start` 是前景還是自行脫離,都沒量過。
  Never run against a real `shioaji server`: the heartbeat format, the non-2xx error body, the JSON shape of
  `order_event`, the answers to reprice / resize / cancel and `subscribe_trade`, the `stream/status` timestamp format,
  several field names, and whether `server start` stays in the foreground or detaches are all unmeasured.
- 測試只在 Windows 跑過。
  The tests have run on Windows only.
- 不在這一版:與 `TradeKit.Abstractions` 的中立介面對映(M1)、帳務、歷史資料、組合單、預收、`watchlist`、處置股。
  Not in this release: the mapping to the neutral `TradeKit.Abstractions` interfaces (M1), account data, historical
  data, combo orders, earmarking, `watchlist`, and regulatory lists.

[0.1.1]: https://github.com/ozakboy/Ozakboy.TradeKit.Shioaji/releases/tag/v0.1.1
[0.1.0]: https://github.com/ozakboy/Ozakboy.TradeKit.Shioaji/releases/tag/v0.1.0
