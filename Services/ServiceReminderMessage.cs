using System.Globalization;
using System.Text;

namespace LineReminder.Services;

/// <summary>把服事表的一列組成 LINE 訊息內容。</summary>
public static class ServiceReminderMessage
{
    public static string Build(ServiceScheduleRow row)
    {
        (string Label, string? Name)[] duties =
        [
            ("鳥瞰", row.Overview),
            ("詩歌", row.Hymn),
        ];
        var assigned = duties.Where(d => IsAssigned(d.Name)).ToList();

        if (assigned.Count == 0)
        {
            return """
                弟兄們平安喜樂,
                本周因特別聚會或集中主日, 未排服事人位
                請大家安心的度過一周, 下周再見 ✌️
                """;
        }

        var sb = new StringBuilder();
        sb.AppendLine("弟兄們平安喜樂,");
        sb.AppendLine($"本周({row.Date.ToString("MM/dd", CultureInfo.InvariantCulture)}) 主日的服事人位如下");
        sb.AppendLine();
        foreach (var (label, name) in assigned)
            sb.AppendLine($"{label} : {name}");
        sb.AppendLine();
        sb.Append("如有任何服事問題請找大管家回報, 感謝大家的配搭");
        return sb.ToString();
    }

    /// <summary>空白或填「X」都視為未排服事。</summary>
    private static bool IsAssigned(string? name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Trim().Equals("X", StringComparison.OrdinalIgnoreCase);
}
