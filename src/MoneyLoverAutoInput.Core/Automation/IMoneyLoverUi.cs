using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Automation;

/// <summary>
/// Các thao tác trên app Money Lover cần cho việc nhập (SPEC 8.2). Có interface để <c>ImportRunner</c>
/// test được bằng bản giả, không cần điện thoại.
/// </summary>
public interface IMoneyLoverUi : IAsyncDisposable
{
    /// <summary>Về màn chính của Money Lover (Back tối đa 3 lần, rồi mở lại app).</summary>
    Task EnsureHomeAsync(CancellationToken ct);

    /// <summary>Mở màn Add transaction và điền đủ mọi trường (bước 2–8), CHƯA lưu.</summary>
    Task FillTransactionAsync(TransactionRow row, IProgress<string> step, CancellationToken ct);

    /// <summary>Bấm Save và chờ về màn chính (bước 9). Không hủy được giữa chừng.</summary>
    Task SaveAsync();

    /// <summary>Thoát màn đang nhập dở mà không lưu (dùng cho Chạy thử, khi lỗi hoặc khi Dừng).</summary>
    Task DiscardEntryAsync();

    /// <summary>Chụp ảnh màn hình + page source khi lỗi. Trả về đường dẫn ảnh (tương đối thư mục dữ liệu) hoặc null.</summary>
    Task<string?> CaptureFailureAsync(string rowId);
}
