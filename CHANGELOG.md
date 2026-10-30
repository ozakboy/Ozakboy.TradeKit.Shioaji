# Changelog

本檔記錄本套件所有值得注意的變更。
All notable changes to this package are documented here.

格式依循 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.1.0/),版號依循
[語意化版本](https://semver.org/lang/zh-TW/)。
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versioning follows
[Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-10-30

**加入歷史資料與查詢(K 線、逐筆、快照、全市場日 OHLCV、排行掃描、借券券源、資券餘額、處置股與注意股)、流量計與全商品列表。**
**Adds historical data and queries (bars, ticks, snapshots, the all-market daily OHLCV, the ranking scanner, short-stock
sources, margin and short balances, the disposition and attention lists), a traffic meter, and a list-everything contract
call.**

`GetKbarsAsync`、`GetTicksAsync`、`GetUsageAsync`、商品列表與商品詳細資料已於 2026-10-31 對真的 `shioaji server`(模擬環境)實測:
1 分 K 的 `Time` 是該分鐘的**結束**時刻(2330 每日 09:01 至 13:30),`Volume` 是張、`Amount` 是元;逐筆一天約 7,400 筆、0.4 MB。
其餘端點(快照、全市場日 OHLCV、排行、券源、資券、處置股)仍只對著本機假伺服器與依文件手工建構的 fixture(見
`Fixtures/PROVENANCE.md`)測過。**全市場日 OHLCV 的 HTTP 路徑與回應形狀文件都沒寫,屬於未驗證的推定,待實機 OpenAPI 確認。**
`GetKbarsAsync`, `GetTicksAsync`, `GetUsageAsync`, the contract list and the contract info were run against a real
`shioaji server` (simulation) on 2026-10-31: a bar's `Time` is the **end** of its minute (2330: 09:01 to 13:30 each day),
`Volume` is in lots and `Amount` in TWD; one day of ticks is about 7,400 rows and 0.4 MB. The other endpoints (snapshots,
the all-market daily OHLCV, the scanner, short-stock sources, margin balances, the regulatory lists) are still tested only
against the local fake server and fixtures built by hand from the documentation (see `Fixtures/PROVENANCE.md`). **Neither
the HTTP path nor the response shape of the all-market daily OHLCV is documented; both are unverified inferences awaiting a
real server's OpenAPI.**

### 新增功能 / Added

- **`ShioajiHistoryClient`**:`GetKbarsAsync`(1 分 K,`POST /api/v1/data/kbars`)、`GetTicksAsync`(`AllDay` / `RangeTime` /
  `LastCount`,`POST /api/v1/data/ticks`)、`GetSnapshotsAsync`(`POST /api/v1/data/snapshots`)、`GetDailyQuotesAsync`、
  `GetScannerAsync`、`GetShortStockSourcesAsync`、`GetCreditEnquireAsync`、`GetRegulatoryPunishAsync`、
  `GetRegulatoryNoticeAsync`。全部在本地檢查之後才過 `MarketQuery` 預算(10 秒 50 次),POST 永不重試。商品用新的
  `ShioajiContractRef { SecurityType, Exchange, Code }` 指稱,可由 `ShioajiContract.ToRef()` 或 `ShioajiContractRef.From` 轉出。
  Every call goes through the `MarketQuery` budget after its local checks, and POST is never retried. Contracts are named with
  the new `ShioajiContractRef`, convertible from a `ShioajiContract`.
- **本地先擋的請求**:1 分 K 區間超過 30 個日曆日(含頭尾)→ `shioaji.history.range_too_long`;起始日晚於結束日、逐筆條件不合法
  (`RangeTime` 缺時間或起訖顛倒、`LastCount` 小於 1)、商品類別不明或代碼空白、`count` 超出 0 到 200、借券券源 / 資券餘額帶了非股票 →
  `shioaji.validation.invalid_argument`;快照超過 500 檔 → `shioaji.history.too_many_contracts`。全部在請求送出之前拒絕,
  不佔預算。
  Refused locally, before the request and without taking a budget unit: a bar range over 30 calendar days (both ends counted), a start
  after the end, bad tick conditions, an unknown security type or blank code, a scanner `count` outside 0 to 200, a non-stock in the
  stock-only queries, and more than 500 snapshot contracts.
- **`ShioajiHistoryPage<T>`**(`Items`、`ResponseBytes`、`IsEmpty`、`PossiblyTrafficExhausted`)。券商每日流量超量時行情查詢**不報錯、只回空值**,
  所以空頁不等於沒有資料:今天還沒向 server 查過用量時空頁一律標成「可能用完」,查過之後依「server 回報的剩餘量減去之後本地又累計的位元組」判斷。
  Over the daily traffic allowance market queries **answer with empty values instead of an error**, so an empty page is not proof of
  no data: with no usage fetched from the server today an empty page is always flagged as possibly exhausted, and afterwards the flag
  follows the server's remainder minus what was accumulated locally since.
- **欄位陣列解析器 `ShioajiColumnarReader`**(內部):`kbars` 與 `ticks` 的 `{datetime: […], Open: […], …}`。任一欄位長度不一致、必要欄位不是陣列
  → 整筆 `shioaji.history.malformed`;`datetime` 是 ISO 字串(不帶時區視為台北牆上時間,固定減 8 小時)或整數 epoch(依大小判斷秒 / 毫秒 / 微秒 / 奈秒);
  數字由原始文字讀成 `decimal`,不經 `double`;格子讀不懂回 `shioaji.response.missing_field`,錯誤資料帶欄位名與列號(`row`),絕不補 0;
  空本文、`null`、`[]`、`{}`、欄位全是 `null` 或空陣列都是成功的空頁。
  Columns of unequal length, or a required column that is not an array, fail the whole response with `shioaji.history.malformed`; a
  `datetime` is an ISO string (Taipei wall-clock time when it has no offset, fixed UTC+8) or an integer epoch whose unit is judged by
  magnitude; numbers are read from raw text into `decimal`, never `double`; an unreadable cell is `shioaji.response.missing_field` with
  the column and the row, never zero; an empty body, `null`, `[]`, `{}` and all-null or all-empty columns are a successful empty page.
- **`ShioajiTrafficMeter`**(單例,`TimeProvider` 注入):每個經過 `ShioajiApiClient` 的回應(含非 2xx)累計位元組(`Content-Length`,分塊傳輸時用實際讀到的
  UTF-8 位元組數),按台北流量日分桶、每日 08:00(= UTC 00:00)重置;公開 `TodayBytes`、`TodayRequests`、`ServerReportedBytes`、`Record`、`Reset()`、
  `Snapshot()`(含歷史桶最多 7 天、`NextResetAt`、估計剩餘量)。`ShioajiAuthClient.GetUsageAsync` 查到的用量會寫進 `ServerReportedBytes`。
  **這是估計值,券商的帳以 `GetUsageAsync` 為準。**
  A singleton on a `TimeProvider` that accumulates the bytes of every response through `ShioajiApiClient`, non-2xx included, in Taipei
  traffic-day buckets reset at 08:00; it exposes `TodayBytes`, `Reset()`, `Snapshot()` and more, and receives the usage
  `GetUsageAsync` fetches as `ServerReportedBytes`. It is an estimate; the broker's own count is `GetUsageAsync`.
- **`ShioajiContractClient.ListAllContractsAsync(securityType)`**:每頁 500 筆走完所有分頁,每一頁過 `MarketQuery` 預算。遇到空頁、少於 500 筆、累計達到 `total`、
  或單頁多於 500 筆(server 忽略分頁)就停;分頁沒前進(滿頁的第一筆與上一頁相同)或翻了 200 頁還沒結束 → `shioaji.response.malformed`。
  Walks every page at 500 per page through the `MarketQuery` budget; stops on an empty page, a short page, the running count reaching
  `total`, or a page over 500 (the server ignored paging); paging that does not advance, or 200 pages without an end, is
  `shioaji.response.malformed`.
- **`ShioajiOptions.DailyQuotesPath`** 與 **`ShioajiApiPaths`**(各端點路徑常數,`DailyQuotes` 預設 `/api/v1/data/daily_quotes`,**未驗證**)。
  `Validate()` 要求它是以 `/` 開頭的路徑。
  Overrides the path of the all-market daily OHLCV; `ShioajiApiPaths` holds the path constants.
- **`AddShioaji`** 一併註冊 `ShioajiHistoryClient` 與 `ShioajiTrafficMeter`(單例)。
  Registers `ShioajiHistoryClient` and `ShioajiTrafficMeter` as singletons.
- 新的錯誤碼:`shioaji.history.range_too_long`、`shioaji.history.too_many_contracts`、`shioaji.history.malformed`;新的 `Error.Data` 鍵 `row`
  (`ShioajiErrorDataKeys.Row`);新的常數 `ShioajiConstants.MaxKbarRangeDays`、`MaxSnapshotContracts`、`MaxScannerCount`、`TrafficResetHour`。
  New error codes, one new `Error.Data` key and four new constants.

### 功能優化 / Changed

- `ShioajiApiClient`、`ShioajiAuthClient`、`ShioajiContractClient` 的建構式多了**選填的結尾參數**(流量計;預算),既有程式碼不需要改。
  The constructors of `ShioajiApiClient`, `ShioajiAuthClient` and `ShioajiContractClient` gain optional trailing parameters, so existing
  code needs no change.
- 商品查詢的說明修正:單一商品查詢仍然不計入 `MarketQuery`,只有 `ListAllContractsAsync` 的每一頁計入。
  Single-contract lookups still do not count against `MarketQuery`; only each page of `ListAllContractsAsync` does.

### 技術改進 / Technical

- `FakeShioajiHttpServer` 加了每個歷史端點的預設路由(`kbars`、`ticks` 是欄位陣列)與 `FakeResponse.Chunked`(分塊傳輸、不帶 `Content-Length`);
  新的 fixtures 依文件手工建構,逐檔來源見 `Fixtures/PROVENANCE.md`。
  The fake server gains default routes for every historical endpoint and a `Chunked` response option; new fixtures are hand-built, with
  their provenance in `Fixtures/PROVENANCE.md`.
- README 的「回補 1 分 K 與逐筆」範例有一個測試逐行照抄。
  A test copies the README's backfill example line by line.

### 已知限制 / Known limitations

- **全市場日 OHLCV 的路徑與回應形狀未驗證**(文件只有命令列 `shioaji data daily-quotes`)。路徑不對回 `shioaji.http.404`,用 `DailyQuotesPath` 改。
  The path and the response shape of the all-market daily OHLCV are unverified.
- 除了 `kbars` 與 `ticks`,其餘歷史端點的回應形狀文件沒寫,是依 Python SDK 的回傳型別推定的;`short_stock_sources` 與 `credit_enquire` 的請求 body 也是推定。
  Apart from `kbars` and `ticks`, the response shapes are inferred from the Python SDK, and so are the request bodies of
  `short_stock_sources` and `credit_enquire`.
- 數字型的 `datetime` 視為真正的 Unix epoch;Python SDK 的奈秒 `ts` 是把台北牆上時間當 UTC 寫的,HTTP server 若原樣轉出會晚 8 小時(ISO 字串不受影響)。
  A numeric `datetime` is read as a true Unix epoch; the Python SDK's nanosecond `ts` carries Taipei wall-clock time as if it were UTC,
  which would be 8 hours late if the HTTP server passes it through.
- 1 分 K 的「≤ 30 天」取較保守的讀法:含頭尾共 30 個日曆日。
  The "≤ 30 days" bar limit is read conservatively: 30 calendar days with both ends counted.
- 本套件**不判斷現在是不是盤中**;「盤中禁止拿 `ticks` / `snapshots` / `kbars` 當即時報價」是呼叫端的責任。
  This package does not decide whether the market is open; never using `ticks` / `snapshots` / `kbars` as live quotes is the caller's duty.
- 仍從未用有效憑證對真的 `shioaji server` 跑過。
  Still never run against a real `shioaji server` with valid credentials.

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

[0.2.0]: https://github.com/ozakboy/Ozakboy.TradeKit.Shioaji/releases/tag/v0.2.0
[0.1.1]: https://github.com/ozakboy/Ozakboy.TradeKit.Shioaji/releases/tag/v0.1.1
[0.1.0]: https://github.com/ozakboy/Ozakboy.TradeKit.Shioaji/releases/tag/v0.1.0
