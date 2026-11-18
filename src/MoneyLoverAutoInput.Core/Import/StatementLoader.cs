using MoneyLoverAutoInput.Core.Categorization;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Drafts;
using MoneyLoverAutoInput.Core.Input;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Import;

public sealed class LoadedStatement
{
    public required StatementInfo Info { get; init; }

    /// <summary>Đã sắp xếp tăng dần theo thời gian thực hiện (cũ trước).</summary>
    public IReadOnlyList<TransactionRow> Rows { get; init; } = Array.Empty<TransactionRow>();

    public IReadOnlyList<string> FileErrors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RuleErrors { get; init; } = Array.Empty<string>();

    public bool HasFileErrors => FileErrors.Count > 0;
}

/// <summary>Đọc → phân loại → validate → sắp xếp (SPEC 8.1 bước 2).</summary>
public sealed class StatementLoader
{
    private readonly IStatementReader _reader;

    public StatementLoader(IStatementReader reader) => _reader = reader;

    public LoadedStatement Load(string path, AppSettings settings)
    {
        var read = _reader.Read(path);
        return Process(read, settings);
    }

    public static LoadedStatement Process(StatementReadResult read, AppSettings settings)
    {
        var categorizer = new Categorizer(settings);

        if (read.FileErrors.Count > 0)
        {
            return new LoadedStatement
            {
                Info = read.Info,
                FileErrors = read.FileErrors,
                RuleErrors = categorizer.RuleErrors,
            };
        }

        var rows = read.Rows
            .Select(r => categorizer.Categorize(r, read.Info))
            .OrderBy(r => r.SortKey)
            .ThenByDescending(r => r.ExcelRow) // cùng thời điểm: file mới-ở-trên, nên dòng dưới xảy ra trước
            .ToList();

        RowValidator.Validate(rows);

        return new LoadedStatement { Info = read.Info, Rows = rows, RuleErrors = categorizer.RuleErrors };
    }

    /// <summary>
    /// Đọc → phân loại theo luật hiện tại → áp chỉnh sửa tay đã lưu nháp (SPEC 10.3).
    /// Dùng cả khi mở file lẫn khi luật thay đổi: chỉnh sửa tay luôn thắng luật.
    /// Dòng lỗi dữ liệu không áp nháp (bị khóa trên UI).
    /// </summary>
    public static LoadedStatement Process(StatementReadResult read, AppSettings settings, DraftStore? drafts)
    {
        var result = Process(read, settings);
        drafts?.ApplyTo(result.Rows.Where(r => !r.HasDataErrors));
        return result;
    }
}
