# 測試 fixture 來源說明 / Fixture provenance

合約測試以這些 JSON 重播,**不連網**。每一份都要能回答「這份資料是哪裡來的、有沒有被改過」。

Contract tests replay these JSON files and never touch the network. Each one has to answer where it came from and
whether it was edited.

## 重要前提:沒有任何一份是從真 server 錄製的 / Important: none of these was recorded from a real server

撰寫本套件 0.1.0 時**沒有 Shioaji 憑證**,無法連線到真的 `shioaji server`;官方文件
(`https://sinotrade.github.io/llms-full.txt`,2026-10-30 整理於
`docs/external/shioaji-http-api.md`)給的是**欄位清單**,沒有逐字的範例回應。所以下面所有檔案都是
**依文件的欄位清單手工建構**,不是實錄。

While 0.1.0 was written there were **no Shioaji credentials**, so a real `shioaji server` could never be reached,
and the official documentation gives **field lists** rather than verbatim sample responses. Every file below is
therefore **hand-built from the documented field lists**, not recorded.

這代表測試證明的是「本套件正確讀懂文件描述的形狀」,**不是**「真 server 真的長這樣」。對真 server 錄到回應後,
請用實際錄到的回應取代這裡的檔案,並更新本表。

So the tests prove that the package reads the shape the documentation describes correctly, **not** that the real
server looks like this. Once responses are recorded from a real server, replace these files with them and update
this table.

## 檔案 / Files

| 檔案 / File | 對應 / Models | 建構依據 / Built from | 刻意放進去的陷阱 / Deliberate traps |
| --- | --- | --- | --- |
| `tick_stk.json` | `tick_stk` | 文件 §4 Tick(STK) 欄位清單;`closing_oddlot_*`、`fixed_trade_*` 的欄位名取自 Python SDK 的 `TickSTKv1`,**未在 HTTP 上驗證** / §4 Tick(STK) field list; the `closing_oddlot_*` and `fixed_trade_*` names come from the Python SDK's `TickSTKv1` and are **unverified over HTTP** | `total_amount` 是字串型數字;`close` 是整數 / `total_amount` is a string-typed number and `close` an integer |
| `bidask_stk.json` | `bidask_stk` | 文件 §4 BidAsk(STK) 欄位清單 / §4 BidAsk(STK) field list | 五檔陣列長度 5;`diff_*` 有負數 / five-level arrays of length 5; `diff_*` has negatives |
| `quote_stk.json` | `quote_stk` | Tick 與 BidAsk 欄位的聯集;`avail_borrowing` 取自 Python SDK / the union of the Tick and BidAsk fields; `avail_borrowing` from the Python SDK | `close` 為 `29.8`,用來證明沒有經過 `double`;`simtrade` 為 true / `close` is `29.8` to prove nothing went through `double`; `simtrade` is true |
| `tick_fop.json` | `tick_fop` | 文件 §4 只寫「FOP 另有 …」;其餘欄位取自 Python SDK 的 `TickFOPv1`,**未驗證** / §4 only says "FOP also has …"; the rest comes from the Python SDK's `TickFOPv1` and is **unverified** | |
| `bidask_fop.json` | `bidask_fop` | 文件 §4:`bid_total_vol`、`ask_total_vol`、`first_derived_*`、`underlying_price` / §4: those named fields | |
| `quote_fop.json` | `quote_fop` | Tick(FOP) 與 BidAsk(FOP) 的聯集,**未驗證** / the union of Tick(FOP) and BidAsk(FOP), **unverified** | |
| `quote_idx.json` | `quote_idx` | 文件只列了事件名 `quote_idx` 與「指數只支援 Quote」;欄位是依指數報價的常識推測,**未驗證** / the documentation lists only the event name `quote_idx` and that an index supports Quote only; the fields are inferred, **unverified** | |
| `order_event_order.json` | `order_event` 委託回報 / an order report | 文件 §6 回報事件欄位清單 `{event_id, operation, order, status, contract}`;`exchange_ts` 用 Unix 秒(**型別未驗證**) / §6 field list; `exchange_ts` as Unix seconds (**type unverified**) | |
| `order_event_order_measured.json` | `order_event` 委託回報(**實測外形**) / an order report (**measured shape**) | 2026-11-02 盤中、shioaji 1.7.10 模擬環境、`GET /api/v1/stream/data` 收到的原文,只把 `account_id` 換成假值。外面包著 `{"state":"StockOrder","data":{"StockOrder":{…}}}`,`event_id` 有值 / the raw text received on `GET /api/v1/stream/data` from the shioaji 1.7.10 simulation during the session on 2026-11-02, with only `account_id` replaced by a fake value. It is wrapped in `{"state":"StockOrder","data":{"StockOrder":{…}}}` and `event_id` has a value | 實測 / measured |
| `order_event_deal.json` | `order_event` 成交回報 / a deal report | 文件 §6 成交欄位清單 `{trade_id, seqno, ordno, exchange_seq, …, ts}`;文件沒寫它是頂層還是巢狀在 `deal` 之下,這裡用頂層(本套件兩種都收) / the §6 deal field list; the documentation does not say whether it sits at the top level or under `deal`, and this file uses the top level (the package accepts both) | |
| `trade_response.json` | `place_order` 回應 / the place_order response | 文件 §6 `{contract, order:{id, seqno, ordno, action, …}, status:{id, status, status_code, order_ts, deals:[]}}` | `status` 為 `PartFilled` 且帶一筆成交 / `status` is `PartFilled` with one fill |
| `contract_stk.json`、`contract_futr1.json` | `GET /data/contracts/{code}` | 文件 §3 `{security_type, region, exchange, code, target_code}`;`TXFR1` 的 `target_code` 範例值依文件寫 `TXFF6`,這裡換成較新的月份 `TXFK6` / §3; the documentation's example `target_code` for `TXFR1` is `TXFF6`, and a later month `TXFK6` is used here | 股票的 `target_code` 是空字串(本套件讀成 `null`) / a stock's `target_code` is an empty string, which the package reads as `null` |
| `contract_info_stk.json` | `GET /data/contracts/{code}/info` | 文件 §3 提到 `day_trade: Yes\|OnlyBuy\|No` 在這裡;其餘欄位(`name`、`reference`、`limit_up` …)取自 Python SDK 的 `Stock` 合約 / §3 says `day_trade` lives here; the other fields come from the Python SDK's `Stock` contract | `limit_up` 是字串型數字;`update_date` 用 `2026/10/30` 的斜線寫法;中文名稱 / `limit_up` is a string-typed number; `update_date` uses slashes; a Chinese name |

### 0.2.0 新增:歷史資料與查詢 / Added in 0.2.0: historical data and queries

同樣**沒有任何一份是從真 server 錄製的**,全部依文件 §5 手工建構。`kbars`、`ticks` 的欄位清單文件有寫,其餘端點文件只寫了名字與請求
參數、沒寫回應,所以形狀是依 Python SDK 的回傳型別推定的。
Likewise **none of these was recorded from a real server**; all are hand-built from §5. The column lists of `kbars` and
`ticks` are documented; for the other endpoints the documentation gives only the name and the request parameters and not
the response, so the shapes are inferred from the Python SDK's return types.

| 檔案 / File | 對應 / Models | 建構依據 / Built from | 刻意放進去的陷阱 / Deliberate traps |
| --- | --- | --- | --- |
| `kbars.json` | `POST /data/kbars` | 文件 §5 欄位陣列 `{datetime[], Open[], High[], Low[], Close[], Volume[], Amount[]}`;**文件沒寫 `datetime` 的格式**,這裡用 ISO 無時區字串(視為台北牆上時間) / the §5 column arrays; the documentation does **not** give the `datetime` format, and ISO strings with no offset (Taipei wall-clock) are used here | 第三列的 `Open`、`Volume` 是字串型數字;`Open` 有整數與小數混用 / the third row's `Open` and `Volume` are string-typed numbers; integers and decimals are mixed |
| `ticks.json` | `POST /data/ticks` | 文件 §5 欄位 `datetime, close, volume, bid_price, bid_volume, ask_price, ask_volume, tick_type`(小寫);**`datetime` 與 `tick_type` 的值格式文件沒寫** / the §5 columns in lower case; the value formats of `datetime` and `tick_type` are not documented | 第一列 `datetime` 帶 9 位小數秒(超過 .NET 刻度);第三列 `ask_price`、`ask_volume` 是 `null` / the first `datetime` has 9 fractional digits, finer than a .NET tick; `ask_price` and `ask_volume` are `null` in the third row |
| `snapshots.json` | `POST /data/snapshots` | 文件只寫了請求 `{contracts: [...]}`(≤ 500 檔);回應欄位取自 Python SDK 的 `Snapshot`,**未在 HTTP 上驗證**;`ts` 的格式是推定 / the documentation gives only the request; the response fields come from the Python SDK's `Snapshot` and are **unverified over HTTP**, and the `ts` format is inferred | 第二筆只有 `code`、`exchange`、`close`(其餘必須是 `null`);第一筆有一個沒對映的欄位 `unlisted_extra`(應留在 `Raw`);`total_amount` 是字串型數字 / the second entry has only `code`, `exchange` and `close`; the first carries an unmapped `unlisted_extra`, which belongs in `Raw`; `total_amount` is a string-typed number |
| `daily_quotes.json` | `POST` `ShioajiOptions.DailyQuotesPath` | **路徑與回應都是 0.2.0 的推定,文件沒有**(只有命令列 `shioaji data daily-quotes`):物件陣列,每筆有 `code`、`open`、`high`、`low`、`close`、`volume` 加幾個多餘欄位 / **both the path and the response are 0.2.0 inferences with no documentation behind them** (only the command line `shioaji data daily-quotes` exists): an array of objects with `code`, `open`, `high`, `low`, `close`, `volume` and a few extra fields | 第二筆的價格是字串型數字;`name`、`date`、`amount` 是多餘欄位(應留在 `Raw`) / the second entry's prices are string-typed numbers; `name`, `date` and `amount` are extra fields that belong in `Raw` |
| `scanner.json` | `POST /data/scanner` | 文件只寫了請求 `{scanner_type, date, ascending, count}`;回應欄位取自 Python SDK 的 `ScannerItem`,**未驗證** / the documentation gives only the request; the response fields come from the Python SDK's `ScannerItem`, **unverified** | 第二筆只有 `code`、`name`、`close`;`bid_orders` 是沒對映的陣列(應留在 `Raw`) / the second entry has only `code`, `name` and `close`; `bid_orders` is an unmapped array that belongs in `Raw` |
| `short_stock_sources.json` | `POST /data/short_stock_sources` | 文件只寫「券源,僅 STK」;欄位取自 Python SDK 的 `ShortStockSource`,請求 body 形狀是推定,**未驗證** / the documentation says only "sources, STK only"; the fields come from the Python SDK's `ShortStockSource` and the request body is inferred, **unverified** | 第二筆的 `short_stock_source` 是字串 `"0"`(真正的零);沒有 `update_time` / the second entry's `short_stock_source` is the string `"0"`, a real zero; no `update_time` |
| `credit_enquire.json` | `POST /data/credit_enquire` | 文件只寫「資券餘額」;欄位取自 Python SDK 的 `CreditEnquire`(代碼欄位叫 `stock_id`),**未驗證** / the documentation says only "margin and short balances"; the fields come from the Python SDK's `CreditEnquire`, whose code field is `stock_id`, **unverified** | 第二筆只有 `stock_id` / the second entry has only `stock_id` |
| `regulatory_punish.json` | `GET /data/regulatory_punish` | 文件只寫端點名稱;**回應形狀完全沒寫**,這裡用物件陣列 / the documentation names the endpoint only; **its response shape is entirely unwritten**, and an array of objects is used here | 第二筆只有 `code`、`name` / the second entry has only `code` and `name` |
| `regulatory_notice.json` | `GET /data/regulatory_notice` | 同上;這份故意用**欄位陣列物件**(Python SDK 的 `Notice` 就是這樣回的),證明兩種形狀都收 / as above; this one deliberately uses the **column-array object** (which is how the Python SDK's `Notice` comes back) to prove that both shapes are accepted | 欄位長度相同;中文名稱 / equal column lengths; Chinese names |

`Support/FakeShioajiHttpServer.cs` 的 `MapHistoryDefaults` 還有每個歷史端點的最小預設回應(寫在程式裡,不是檔案),依據同上。
`MapHistoryDefaults` in `Support/FakeShioajiHttpServer.cs` also holds a minimal default answer for each historical endpoint
(in code, not in files), from the same sources.

### 0.3.0 新增:帳務查詢 / Added in 0.3.0: portfolio queries

同樣**沒有任何一份是從真 server 錄製的**。文件的帳務章節給了每個端點的欄位表,但只有 `settlements` 與 `account_balance` 有範例 JSON,
所以下面的**回應外形(裸陣列、包著陣列的物件、欄位陣列)都是推定的**,三種形狀各用一份檔證明都收。
Likewise **none of these was recorded from a real server**. The portfolio chapter of the documentation gives a field table for
every endpoint but example JSON only for `settlements` and `account_balance`, so the **response shapes below (a bare array, an
object wrapping an array, column arrays) are inferences**, with one file each to prove all three are accepted.

| 檔案 / File | 對應 / Models | 建構依據 / Built from | 刻意放進去的陷阱 / Deliberate traps |
| --- | --- | --- | --- |
| `portfolio_positions_stock.json` | `POST /portfolio/position_unit`(證券 / stock) | 文件 `StockPosition` 欄位表;裸物件陣列(推定) / the documentation's `StockPosition` table; a bare array of objects (inferred) | 第一筆 `direction`、`cond` 帶型別前綴(`Action.Sell`、`StockOrderCond.MarginTrading`);`quantity`、`price`、`margin_purchase_amount` 是字串型數字;`price` 為 `29.8`(證明不經 `double`);第二筆沒有 `id`、價格與損益(必須是 `null`,不是 0),`cond` 是 `Netting`,多一個沒對映的欄位 / the first entry's `direction` and `cond` carry a type prefix; `quantity`, `price` and `margin_purchase_amount` are string-typed numbers; the second has no `id`, price or pnl, which must read as `null` and not zero |
| `portfolio_positions_stock_columnar.json` | 同上 / same | 同上,但是欄位陣列物件(Python SDK 風格,推定) / same, as a column-array object (Python SDK style, inferred) | `quantity`、`price` 混用數字與字串;`last_price` 第二格是 `null` / numbers and strings mixed; a `null` cell |
| `portfolio_profit_loss_stock_wrapped.json` | `POST /portfolio/profit_loss`(證券 / stock) | 文件 `StockProfitLoss` 欄位表;包在 `items` 裡(推定) / the `StockProfitLoss` table; wrapped in `items` (inferred) | 第二筆 `date` 是 `20261029`、`pnl` 是字串型負數、`cond` 是 `Emerging` / the second entry's `date` is `20261029`, `pnl` a string-typed negative, `cond` `Emerging` |
| `portfolio_profitloss_sum_stock.json` | `POST /portfolio/profitloss_sum`(證券 / stock) | 文件的 `profitloss_sum`(清單)與 `total`(`ProfitLossTotal`) / the documented `profitloss_sum` list and `total` | 第二筆只有 `code`、`quantity`、`pnl`(其餘必須是 `null`);`total.buy_cost` 是字串型數字 / the second entry has only `code`, `quantity` and `pnl`; `total.buy_cost` is a string-typed number |

其餘帳務端點(`position_detail`、`profit_loss_detail`、期貨各端點、`settlements`、`trading_limits`、`margin`、`account_balance`)沒有 fixture 檔,
它們的預設回應寫在 `Support/FakeShioajiHttpServer.cs` 的 `MapPortfolioDefaults`(依據寫在該方法的註解裡),情境測試的 JSON 直接寫在測試裡。
The other portfolio endpoints have no fixture file: their default answers are in `MapPortfolioDefaults` of
`Support/FakeShioajiHttpServer.cs` (with the basis in that method's comment), and scenario JSON is inline in the tests.

## 沒有 fixture 檔的回應 / Responses without a fixture file

`/api/v1/info`、`/auth/accounts`、`/auth/usage`、`/stream/status` 的 JSON 直接寫在
`Support/FakeShioajiHttpServer.cs` 的預設路由裡,依據是文件 §2 的欄位清單;
`stream/status` 的 `timestamp` 格式文件沒寫,假設為 Unix 秒。

The JSON for `/api/v1/info`, `/auth/accounts`, `/auth/usage` and `/stream/status` is written inline in the default
routes of `Support/FakeShioajiHttpServer.cs`, from the §2 field lists; the format of the `timestamp` in
`stream/status` is undocumented and is assumed to be Unix seconds.
