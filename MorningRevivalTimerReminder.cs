using LineReminder.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace LineReminder;

public class MorningRevivalTimerReminder(
    ServiceScheduleReader scheduleReader,
    LineMessagingClient line,
    ILogger<MorningRevivalTimerReminder> logger)
{
    // 每週一 09:00 台灣時間 = 01:00 UTC（台灣固定 UTC+8，沒有日光節約時間）
    private static readonly TimeSpan TaiwanOffset = TimeSpan.FromHours(8);

    [Function("MorningRevivalTimerReminder")]
    public async Task Run([TimerTrigger("0 0 1 * * 1")] TimerInfo myTimer, CancellationToken cancellationToken)
    {
        var today = GetTaiwanToday();
        var rows = await scheduleReader.GetRowsAsync(cancellationToken);

        // 找出今天起 7 天內的主日（週一執行時就是本週日；週日執行時就是當天）
        var row = rows
            .Where(r => r.Date >= today && r.Date <= today.AddDays(6))
            .OrderBy(r => r.Date)
            .FirstOrDefault();

        if (row is null)
        {
            logger.LogWarning("服事表中找不到 {From:yyyy/MM/dd} ~ {To:yyyy/MM/dd} 的資料，本次不發送", today, today.AddDays(6));
            return;
        }

        var message = ServiceReminderMessage.Build(row);
        logger.LogInformation("發送 {Date:yyyy/MM/dd} 的服事提醒：\n{Message}", row.Date, message);

        var groupId = Environment.GetEnvironmentVariable("LINE_GROUP_ID")
            ?? throw new InvalidOperationException("缺少設定值 LINE_GROUP_ID");
        await line.PushTextAsync(groupId, message, cancellationToken);
    }

    private static DateOnly GetTaiwanToday()
    {
        // 本機測試用：在 local.settings.json 設定 REMINDER_DATE_OVERRIDE=2026-11-08 可模擬指定日期
        if (DateOnly.TryParse(Environment.GetEnvironmentVariable("REMINDER_DATE_OVERRIDE"), out var overrideDate))
            return overrideDate;

        return DateOnly.FromDateTime(DateTime.UtcNow + TaiwanOffset);
    }
}
