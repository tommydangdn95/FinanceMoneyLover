using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;
using MoneyLoverAutoInput.Core.State;

namespace MoneyLoverAutoInput.Core.Import;

/// <summary>Trạng thái một dòng trên màn hình Xem trước (SPEC 9, 10.3).</summary>
public enum RowStatus
{
    /// <summary>Chưa từng nhập.</summary>
    New,
    /// <summary>Đã nhập thành công — khóa, không nhập lại.</summary>
    Done,
    /// <summary>Lần trước lỗi — chọn sẵn để nhập lại.</summary>
    PreviouslyFailed,
    /// <summary>Lần trước mất kết nối lúc Lưu — có thể đã lưu, cần kiểm tra tay.</summary>
    NeedsManualCheck,
    /// <summary>Luật hiện tại là Skip.</summary>
    Skipped,
    /// <summary>Lỗi dữ liệu cố định — không nhập được.</summary>
    Invalid,
}

public static class RowStatusResolver
{
    public static RowStatus Resolve(TransactionRow row, StateEntry? state)
    {
        if (state?.Status == ImportStatus.Done) return RowStatus.Done;
        if (row.HasDataErrors) return RowStatus.Invalid;
        if (state?.Status == ImportStatus.Failed)
            return state.NeedsManualCheck ? RowStatus.NeedsManualCheck : RowStatus.PreviouslyFailed;

        // State "Skipped" cũ không còn ý nghĩa nếu luật đã đổi (SPEC 9): chỉ dựa vào luật hiện tại.
        return row.Action == RuleAction.Skip ? RowStatus.Skipped : RowStatus.New;
    }

    /// <summary>Người dùng có được tick chọn dòng này không.</summary>
    public static bool IsSelectable(RowStatus status) =>
        status is not (RowStatus.Done or RowStatus.Invalid);

    /// <summary>Có được chọn sẵn khi mở file không.</summary>
    public static bool IsSelectedByDefault(RowStatus status) =>
        status is RowStatus.New or RowStatus.PreviouslyFailed;

    public static string ToDisplayText(RowStatus status) => status switch
    {
        RowStatus.New => "Mới",
        RowStatus.Done => "Đã nhập",
        RowStatus.PreviouslyFailed => "Lỗi lần trước",
        RowStatus.NeedsManualCheck => "Cần kiểm tra tay",
        RowStatus.Skipped => "Bỏ qua",
        RowStatus.Invalid => "Dữ liệu lỗi",
        _ => status.ToString(),
    };
}
