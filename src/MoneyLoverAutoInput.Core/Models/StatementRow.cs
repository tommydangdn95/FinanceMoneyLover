namespace MoneyLoverAutoInput.Core.Models;

/// <summary>Một dòng dữ liệu thô trong sao kê, chưa phân loại.</summary>
public sealed class StatementRow
{
    /// <summary>Số dòng trong Excel (1-based), để báo lỗi.</summary>
    public int ExcelRow { get; init; }

    public DateTime? ExecutedAt { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public string Description { get; init; } = "";
    public long? Debit { get; init; }
    public long? Credit { get; init; }
    public string CounterpartAccount { get; init; } = "";
    public string CounterpartName { get; init; } = "";
    public string TxnCode { get; init; } = "";

    /// <summary>Ô Ghi nợ hoặc Ghi có có giá trị nhưng không parse được.</summary>
    public bool AmountParseFailed { get; init; }

    /// <summary>Lỗi parse ở mức ô (ngày sai, số tiền sai...).</summary>
    public List<string> ParseErrors { get; } = new();
}
