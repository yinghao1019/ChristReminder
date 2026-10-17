using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;

namespace LineReminder.Services;

/// <summary>Excel 中的一列：A 日期、B 鳥瞰、C 詩歌。</summary>
public record ServiceScheduleRow(DateOnly Date, string? Overview, string? Hymn);

/// <summary>從 Google Sheet 讀取主日服事表。</summary>
public class ServiceScheduleReader
{
    private readonly SheetsService _sheets;
    private readonly string _spreadsheetId;
    private readonly string? _sheetName;

    public ServiceScheduleReader()
    {
        var json = RequiredSetting("GOOGLE_SERVICE_ACCOUNT_JSON");
        var credential = CredentialFactory.FromJson<ServiceAccountCredential>(json)
            .ToGoogleCredential()
            .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);

        _sheets = new SheetsService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ChristReminder",
        });
        _spreadsheetId = RequiredSetting("GOOGLE_SHEET_ID");
        // 工作表名稱（例如「2026下半年服事表」），沒設定就讀第一個工作表
        _sheetName = Environment.GetEnvironmentVariable("GOOGLE_SHEET_NAME");
    }

    public async Task<IReadOnlyList<ServiceScheduleRow>> GetRowsAsync(CancellationToken cancellationToken = default)
    {
        var sheetName = string.IsNullOrWhiteSpace(_sheetName)
            ? await GetFirstSheetNameAsync(cancellationToken)
            : _sheetName;

        // 從第 2 列開始讀（第 1 列是標題）
        var request = _sheets.Spreadsheets.Values.Get(_spreadsheetId, $"'{sheetName}'!A2:C");
        request.ValueRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.ValueRenderOptionEnum.UNFORMATTEDVALUE;
        request.DateTimeRenderOption = SpreadsheetsResource.ValuesResource.GetRequest.DateTimeRenderOptionEnum.SERIALNUMBER;

        var response = await request.ExecuteAsync(cancellationToken);
        var rows = new List<ServiceScheduleRow>();
        foreach (var row in response.Values ?? [])
        {
            if (row.Count == 0 || !TryParseDate(row[0], out var date))
                continue;

            rows.Add(new ServiceScheduleRow(date, CellText(row, 1), CellText(row, 2)));
        }
        return rows;
    }

    private async Task<string> GetFirstSheetNameAsync(CancellationToken cancellationToken)
    {
        var spreadsheet = await _sheets.Spreadsheets.Get(_spreadsheetId).ExecuteAsync(cancellationToken);
        return spreadsheet.Sheets[0].Properties.Title;
    }

    private static string? CellText(IList<object> row, int index) =>
        index < row.Count ? row[index]?.ToString()?.Trim() : null;

    /// <summary>日期儲存格可能是序列值（46334）或文字（11/8/2026、2026/11/08）。</summary>
    private static bool TryParseDate(object? value, out DateOnly date)
    {
        switch (value)
        {
            case double or long or int or decimal:
                date = DateOnly.FromDateTime(DateTime.FromOADate(Convert.ToDouble(value, CultureInfo.InvariantCulture)));
                return true;
            case string text:
                string[] formats = ["M/d/yyyy", "yyyy/M/d", "yyyy-M-d"];
                return DateOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            default:
                date = default;
                return false;
        }
    }

    private static string RequiredSetting(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"缺少設定值 {name}");
}
