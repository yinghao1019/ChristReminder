# ChristReminder

每週一早上自動讀取 Google 試算表中的主日服事表，找出**本週日**的服事人位，透過 LINE Bot 推播到群組提醒。

以 Azure Functions（.NET 8 隔離式背景工作角色）實作，包含兩個函式：

| 函式 | 觸發方式 | 用途 |
|---|---|---|
| `MorningRevivalTimerReminder` | Timer，每週一 09:00（台灣時間） | 讀取服事表、組訊息、推播到 LINE 群組 |
| `LineWebhook` | HTTP `POST /api/line/webhook` | 接收 LINE webhook 事件；在群組輸入 `groupid` 會回覆群組 ID |

```
Timer（週一 09:00）
  → Google Sheets API 讀取服事表
  → 找出本週日那一列
  → 組成提醒訊息
  → LINE Messaging API 推播到群組
```

---

## Requirements

### 本機工具

| 工具 | 版本 | 安裝 |
|---|---|---|
| .NET SDK | 8.0 以上 | https://dotnet.microsoft.com/download |
| Azure Functions Core Tools | v4 | `winget install Microsoft.Azure.FunctionsCoreTools` |
| Azure CLI | 最新版 | `winget install Microsoft.AzureCLI` |
| Node.js | 18 以上 | 安裝 Azurite 用 |
| Azurite | 最新版 | `npm install -g azurite` |

### 外部服務

- **Azure 訂用帳戶**（隨用隨付即可，Function 用量在免費額度內）
- **Google Cloud 專案**，啟用 Google Sheets API，並建立服務帳戶
- **LINE Official Account** 與 Messaging API Channel

### 服事表格式

> ⚠️ 必須是 **Google 試算表**。上傳到 Drive 的 `.xlsx` 檔無法用 Sheets API 讀取（錯誤：`The document must not be an Office file`），請先用「檔案 → 另存為 Google 試算表」轉換。

第 1 列為標題，從第 2 列開始讀取：

| A：日期 | B：鳥瞰 | C：詩歌 |
|---|---|---|
| 2026/10/25 | 王弟兄 | 李弟兄 |
| 2026/11/01 | 陳弟兄 | X |

- 日期可以是日期格式的儲存格，或文字 `M/d/yyyy`、`yyyy/M/d`、`yyyy-M-d`
- 服事欄位空白或填 `X` 視為未排；全部未排時會發送「本周未排服事人位」的訊息

---

## Dependencies

| 套件 | 版本 | 用途 |
|---|---|---|
| `Microsoft.Azure.Functions.Worker` | 2.52.0 | Functions 隔離式背景工作角色 |
| `Microsoft.Azure.Functions.Worker.Sdk` | 2.0.7 | 建置與程式碼產生 |
| `Microsoft.Azure.Functions.Worker.Extensions.Timer` | 4.3.1 | Timer Trigger |
| `Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore` | 2.1.1 | HTTP Trigger（LINE webhook） |
| `Microsoft.Azure.Functions.Worker.OpenTelemetry` | 1.2.0 | 遙測 |
| `OpenTelemetry.Extensions.Hosting` | 1.15.3 | 遙測 |
| `Azure.Monitor.OpenTelemetry.Exporter` | 1.7.0 | 將 log 送到 Application Insights |
| `Google.Apis.Sheets.v4` | 1.77.0.4290 | 讀取 Google 試算表 |

---

## Project Structure

```
ChristReminder/
├── ChristReminder.csproj             # 專案檔與 NuGet 套件
├── Program.cs                        # 主機設定、DI 註冊、Application Insights
├── host.json                         # Functions 主機設定
├── MorningRevivalTimerReminder.cs    # Timer 函式：每週一推播服事提醒
├── LineWebhook.cs                    # HTTP 函式：LINE webhook，查詢群組 ID
├── Services/
│   ├── ServiceScheduleReader.cs      # 從 Google 試算表讀取服事表
│   ├── ServiceReminderMessage.cs     # 將一列資料組成 LINE 訊息文字
│   └── LineMessagingClient.cs        # 呼叫 LINE Messaging API（push / reply）
├── Properties/
│   └── launchSettings.json
└── local.settings.json               # 本機設定值（不進版控，需自行建立）
```

---

## Configuration

| 名稱 | 必填 | 說明 |
|---|---|---|
| `GOOGLE_SERVICE_ACCOUNT_JSON` | ✅ | Google 服務帳戶金鑰 JSON 的完整內容 |
| `GOOGLE_SHEET_ID` | ✅ | 試算表 ID（網址 `/spreadsheets/d/<ID>/edit` 中間那段） |
| `GOOGLE_SHEET_NAME` | | 工作表名稱；未設定時讀取第一個工作表 |
| `LINE_CHANNEL_ACCESS_TOKEN` | ✅ | LINE Channel access token（long-lived） |
| `LINE_CHANNEL_SECRET` | ✅* | LINE Channel secret，webhook 驗證簽章用（*只有 `LineWebhook` 需要） |
| `LINE_GROUP_ID` | ✅ | 推播目標群組 ID（`C` 開頭）；測試時可改成自己的 User ID（`U` 開頭） |
| `REMINDER_DATE_OVERRIDE` | | 模擬「今天」的日期，例如 `2026-11-02`，**僅供測試，正式環境不要設定** |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | | 設定後 log 會送到 Application Insights；Azure 開啟 Application Insights 時會自動加上 |

> **`GOOGLE_SERVICE_ACCOUNT_JSON` 的格式依位置而不同：**
> - `local.settings.json`：值放在 JSON 字串裡，雙引號**要跳脫**：`"{\"type\":\"service_account\", ...}"`
> - Azure Portal 環境變數：**直接貼上原始 JSON，不要跳脫**：`{"type":"service_account", ...}`
>
> 金鑰中 `private_key` 的 `\n` 是原本就有的，兩種情況都要保留。

### 外部服務設定

**Google**
1. 在 [Google Cloud Console](https://console.cloud.google.com) 建立專案並啟用 **Google Sheets API**
2. 「IAM 與管理」→「服務帳戶」→ 建立服務帳戶 → 新增 JSON 金鑰並下載
3. 在試算表「共用」中加入服務帳戶 email（`xxx@xxx.iam.gserviceaccount.com`），權限設為**檢視者**

**LINE**
1. LINE Official Account Manager →「設定」→ 開啟**允許加入群組或多人聊天室**；「回應設定」→ 關閉**自動回應訊息**、開啟 **Webhook**
2. LINE Developers Console →「Basic settings」取得 **Channel secret**；「Messaging API」發行 **Channel access token (long-lived)**
3. 將 Bot 邀請進群組
4. 取得群組 ID：部署後設定 webhook（見 [Deploy](#deploy)），在群組輸入 `groupid`

> 免費方案每月 200 則推播，推播到群組會**按群組人數計算**。reply 不計入額度。

---

## How to Build

```bash
dotnet build
```

---

## How to Run Locally

**1. 建立 `local.settings.json`**

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "GOOGLE_SERVICE_ACCOUNT_JSON": "{\"type\":\"service_account\", ...}",
    "GOOGLE_SHEET_ID": "<試算表 ID>",
    "LINE_CHANNEL_ACCESS_TOKEN": "<token>",
    "LINE_CHANNEL_SECRET": "<channel secret>",
    "LINE_GROUP_ID": "<群組 ID>"
  }
}
```

可用 PowerShell 將金鑰檔轉成跳脫後的字串並複製到剪貼簿：

```powershell
(Get-Content "<金鑰檔路徑>.json" -Raw | ConvertFrom-Json | ConvertTo-Json -Compress -Depth 5) | ConvertTo-Json | Set-Clipboard
```

**2. 啟動 Azurite**（另開一個終端機，保持開啟）

```bash
azurite --silent --location %TEMP%\azurite
```

**3. 啟動 Functions**

```bash
func start
```

看到以下輸出即啟動成功：

```
Functions:
        LineWebhook: [POST] http://localhost:7071/api/line/webhook
        MorningRevivalTimerReminder: timerTrigger
```

---

## Deploy

### 1. 建立 Azure 資源

**Azure Portal**：建立資源 →「函數應用程式」→ 主控方案選 **彈性使用量 (Flex Consumption)**

| 欄位 | 設定 |
|---|---|
| 執行階段堆疊 | .NET |
| 版本 | 8 (LTS)，隔離式背景工作角色 |
| 區域 | East Asia |
| 儲存體 | 新建（Timer Trigger 必要） |
| Application Insights | 選用（建議開啟以便查看 log，每月 5 GB 免費） |

建立後到「設定」→「縮放與並行」，確認**永遠就緒執行個體為 0**，避免產生固定費用。

**或用 Azure CLI：**

```bash
az group create -n <resource-group> -l eastasia
az storage account create -n <storage-name> -g <resource-group> -l eastasia --sku Standard_LRS
az functionapp create -n <function-app-name> -g <resource-group> --storage-account <storage-name> --flexconsumption-location eastasia --runtime dotnet-isolated --runtime-version 8.0
```

### 2. 設定環境變數

Function App →「設定」→「環境變數」，加入 [Configuration](#configuration) 中的必填項目，按「套用」。

### 3. 發佈

```bash
az login
func azure functionapp publish <function-app-name>
```

> 若 `az login` 出現 `AADSTS50076`（需要 MFA），改用 `az login --tenant <tenant-id>`。

### 4. 設定 LINE Webhook

LINE Developers Console →「Messaging API」→ Webhook URL 設為：

```
https://<function-app 預設網域>/api/line/webhook
```

按 **Verify** 確認成功，並開啟 **Use webhook**。接著在群組輸入 `groupid` 取得群組 ID，填入 `LINE_GROUP_ID`。

---

## Test

> ⚠️ 手動觸發 Timer 函式會**真的推播到 LINE 群組**。測試時可將 `LINE_GROUP_ID` 改成自己的 User ID（LINE Developers Console「Basic settings」→「Your user ID」）。

### 手動觸發 Timer 函式

**本機**（不需金鑰）：

```bash
curl -X POST http://localhost:7071/admin/functions/MorningRevivalTimerReminder -H "Content-Type: application/json" -d "{\"input\":\"\"}"
```

**Azure**：
- Portal：函式 `MorningRevivalTimerReminder` →「程式碼 + 測試」→「測試/執行」→「執行」
- 或呼叫管理 API（需要 `_master` 金鑰，在 Function App →「函式」→「應用程式金鑰」取得，**請勿外流**）：

```bash
curl -X POST "https://<function-app 預設網域>/admin/functions/MorningRevivalTimerReminder" -H "x-functions-key: <master key>" -H "Content-Type: application/json" -d "{\"input\":\"\"}"
```

回應 `202 Accepted` 表示已觸發，函式在背景執行。

### 模擬其他日期

設定 `REMINDER_DATE_OVERRIDE=2026-11-02`，程式會將該日視為今天，並讀取該週日（11/08）的資料。測試完務必移除。

### 測試 LINE Webhook

在 Bot 所在的群組輸入 `groupid`，Bot 應回覆 `群組 ID：Cxxxx…`。

### 查看 log

- 本機：直接顯示在 `func start` 的終端機
- Azure：需開啟 Application Insights，於「即時計量」或「記錄」查詢：

```kusto
traces
| where message has "LINE 事件" or message has "服事提醒"
| order by timestamp desc
```

---

## Schedule

排程設定在 `MorningRevivalTimerReminder.cs`：

```
0 0 1 * * 1
│ │ │ │ │ └─ 星期（0 = 週日，1 = 週一）
│ │ │ │ └─── 月
│ │ │ └───── 日
│ │ └─────── 時（UTC）
│ └───────── 分
└─────────── 秒
```

Azure Functions 排程使用 **UTC**，台灣為 UTC+8，所以 UTC 週一 01:00 = 台灣週一 09:00。Flex Consumption（Linux）不支援 `WEBSITE_TIME_ZONE`，需自行換算。

程式會讀取執行當週（週一至週日）的**週日**那一列；當天是週日則讀取當天。

---

## Troubleshooting

| 錯誤 | 原因與解法 |
|---|---|
| `The document must not be an Office file` | 試算表是上傳的 `.xlsx`，請轉存為 Google 試算表並更新 `GOOGLE_SHEET_ID` |
| `Error deserializing JSON credential data` | `GOOGLE_SERVICE_ACCOUNT_JSON` 格式錯誤；Azure 上不要跳脫雙引號 |
| `The caller does not have permission` | 試算表沒有共用給服務帳戶 email |
| `缺少設定值 XXX` | 未設定該環境變數，或設定後沒有按「套用」 |
| Webhook Verify 顯示 401 | `LINE_CHANNEL_SECRET` 錯誤 |
| Webhook Verify 顯示 500 | 未設定 `LINE_CHANNEL_SECRET` |
| `'func' 不是內部或外部命令` | Core Tools 不在 PATH；將 `C:\Program Files\Microsoft\Azure Functions Core Tools` 加入使用者 PATH 後重開終端機 |
| 執行成功但沒收到訊息 | 服事表中沒有本週日的資料（log 會顯示「找不到本週日」），或 Bot 不在群組中 |
