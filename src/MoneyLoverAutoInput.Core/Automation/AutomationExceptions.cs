namespace MoneyLoverAutoInput.Core.Automation;

/// <summary>Phân loại lỗi khi nhập (SPEC 11).</summary>
public abstract class AutomationException : Exception
{
    protected AutomationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Mất kết nối Appium/thiết bị → DỪNG TOÀN BỘ, giữ state.</summary>
public sealed class ConnectionLostException : AutomationException
{
    public ConnectionLostException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Không thấy phần tử / quá thời gian / màn hình không như mong đợi → THỬ LẠI dòng đó.</summary>
public sealed class UiStepException : AutomationException
{
    public UiStepException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Dữ liệu không tồn tại trong app (nhóm/ví) → FAILED NGAY, không thử lại.</summary>
public sealed class DataNotFoundException : AutomationException
{
    public DataNotFoundException(string message) : base(message) { }
}
