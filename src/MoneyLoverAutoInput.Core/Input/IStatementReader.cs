using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Input;

public sealed class StatementReadResult
{
    public required StatementInfo Info { get; init; }
    public IReadOnlyList<StatementRow> Rows { get; init; } = Array.Empty<StatementRow>();

    /// <summary>Lỗi mức file (không tìm thấy bảng, thiếu cột bắt buộc...). Có lỗi thì Rows rỗng.</summary>
    public IReadOnlyList<string> FileErrors { get; init; } = Array.Empty<string>();
}

/// <summary>Đọc một định dạng sao kê. Thêm ngân hàng khác bằng cách thêm implementation.</summary>
public interface IStatementReader
{
    StatementReadResult Read(string path);
    StatementReadResult Read(Stream stream, string sourceFile);
}
