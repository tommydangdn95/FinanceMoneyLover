using ClosedXML.Excel;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Input;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Tests;

internal static class TestHelpers
{
    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Samples", "sao_ke_mau.xlsx");

    public static readonly string[] Headers =
    {
        "Ngày thực hiện", "Ngày hiệu lực", "Mô tả giao dịch", "Ghi nợ", "Ghi có",
        "Số dư", "Tài khoản đối ứng", "Tên tài khoản", "Mã giao dịch",
    };

    /// <summary>Dựng một sao kê trong bộ nhớ. Mỗi dòng là 9 giá trị theo thứ tự <see cref="Headers"/>.</summary>
    public static StatementReadResult ReadBuilt(IEnumerable<object?[]> rows, int headerRow = 9,
        string[]? headers = null, string account = "09990001234")
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("VN");
        ws.Cell(3, 1).Value = "LỊCH SỬ GIAO DỊCH TÀI KHOẢN";
        ws.Cell(headerRow - 4, 1).Value = "Thời gian:";
        ws.Cell(headerRow - 4, 2).Value = "01/11/2026 - 15/11/2026";
        ws.Cell(headerRow - 3, 1).Value = "Tài khoản giao dịch:";
        ws.Cell(headerRow - 3, 2).Value = account;

        var h = headers ?? Headers;
        for (var i = 0; i < h.Length; i++) ws.Cell(headerRow, i + 1).Value = h[i];

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case string s: cell.Value = s; break;
                    case long l: cell.Value = l; break;
                    case int n: cell.Value = n; break;
                    case DateTime d: cell.Value = d; break;
                    default: cell.Value = row[c]!.ToString(); break;
                }
            }
            r++;
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return new BankStatementReader().Read(ms, $"Account Enquiry-test-{account}.xlsx");
    }

    public static object?[] Row(string executed, string desc, object debit, object credit, string code,
        string counterpartName = "") =>
        new object?[] { executed, executed[..10].Replace('-', '/'), desc, debit, credit, 1000000L, "", counterpartName, code };

    public static AppSettings Settings(params CategoryRule[] rules) => new()
    {
        Defaults = new DefaultsOptions
        {
            Wallet = "Tài khoản ngân hàng",
            ExpenseCategory = "Khác",
            IncomeCategory = "Thu nhập khác",
            NoteTemplate = "{description}",
        },
        CategoryRules = rules.ToList(),
    };

    public static CategoryRule Rule(string category, TransactionType? type, params string[] match) =>
        new() { Match = match.ToList(), Type = type, Category = category };

    public static CategoryRule SkipRule(params string[] match) =>
        new() { Match = match.ToList(), Action = RuleAction.Skip };

    public static StatementInfo Info(string account = "09990001234") =>
        new() { SourceFile = "test.xlsx", AccountNumber = account };
}
