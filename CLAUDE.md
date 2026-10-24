# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

An Azure Functions app (.NET 8, isolated worker, Functions v4) that reads a church Sunday service-duty roster from a Google Sheet and pushes a weekly reminder to a LINE group via the LINE Messaging API. Code comments, log messages, and the LINE message text are in Traditional Chinese — keep new ones consistent.

## Commands

```bash
dotnet build                 # build
func start --port 7006       # run locally (port matches Properties/launchSettings.json)

# Fire the timer function manually while running locally
# (POST to the Functions admin endpoint; body must be JSON)
#   POST http://localhost:7006/admin/functions/MorningRevivalTimerReminder   body: {}
```

There is no test project.

## Architecture

- [Program.cs](Program.cs) — host setup. Registers `ServiceScheduleReader` as a singleton and `LineMessagingClient` as a typed `HttpClient`. OpenTelemetry → Azure Monitor is only enabled when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set.
- [MorningRevivalTimerReminder.cs](MorningRevivalTimerReminder.cs) — timer trigger `0 0 1 * * 1` (Monday 01:00 UTC = 09:00 Taiwan, UTC+8, no DST). Computes "this week's Sunday" in Taiwan time (week runs Mon–Sun), finds the matching sheet row, builds the message, and pushes it to `LINE_GROUP_ID`. If no row matches, it logs a warning and sends nothing.
- [LineWebhook.cs](LineWebhook.cs) — anonymous HTTP trigger at `POST /api/line/webhook`. Exists only to discover the group ID: typing `groupid` in a LINE group makes the bot reply with that group's ID. Validates `X-Line-Signature` (HMAC-SHA256 with `LINE_CHANNEL_SECRET`) before processing.
- [Services/ServiceScheduleReader.cs](Services/ServiceScheduleReader.cs) — Google Sheets read-only access via a service account. Reads `A2:C` (row 1 is the header): A = date, B = 鳥瞰 (overview), C = 詩歌 (hymn). Dates may be serial numbers or text (`M/d/yyyy`, `yyyy/M/d`, `yyyy-M-d`). Uses `GOOGLE_SHEET_NAME` if set, otherwise the first worksheet.
- [Services/ServiceReminderMessage.cs](Services/ServiceReminderMessage.cs) — formats the LINE text. A duty cell that is blank or `X` counts as unassigned; if all duties are unassigned, a "no service this week" message is sent instead. Adding a new duty column means extending `ServiceScheduleRow`, the sheet range/parsing in the reader, and the `duties` array here.
- [Services/LineMessagingClient.cs](Services/LineMessagingClient.cs) — push (with `X-Line-Retry-Key` for idempotent retries) and reply (free, doesn't count against the monthly push quota). Throws on non-2xx with the response body.

## Configuration

All settings are read via `Environment.GetEnvironmentVariable` (from `local.settings.json` locally, App Settings in Azure); missing required ones throw `InvalidOperationException("缺少設定值 …")`.

| Setting | Purpose |
|---|---|
| `GOOGLE_SERVICE_ACCOUNT_JSON` | Service account key JSON (the sheet must be shared with that account) |
| `GOOGLE_SHEET_ID` | Spreadsheet ID |
| `GOOGLE_SHEET_NAME` | Optional worksheet name |
| `LINE_CHANNEL_ACCESS_TOKEN` | Messaging API token |
| `LINE_CHANNEL_SECRET` | Webhook signature verification |
| `LINE_GROUP_ID` | Push target |
| `REMINDER_DATE_OVERRIDE` | Local testing only: pretend "today" is this date (e.g. `2026-11-08`) |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Optional telemetry |

## Secrets

`local.settings.json` and other secret files are blocked from reading by `.claude/settings.json` — don't try to work around that. Secrets come only from environment/app settings; never hardcode them or include them in logs, exception messages, or commits.


## 機密處理

### 絕對禁止
- 在程式碼中寫死 API 金鑰、token 或密碼
- 在 commit 訊息中包含機密
- 將機密記錄到 console 或檔案
- 在錯誤訊息中包含機密

### 永遠
- 使用環境變數處理機密
- 透過安全的配置管理參照機密
- 定期輪換憑證
- 每個環境使用不同的憑證
