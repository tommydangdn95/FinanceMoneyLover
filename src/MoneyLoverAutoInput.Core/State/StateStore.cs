using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoneyLoverAutoInput.Core.State;

public enum ImportStatus
{
    Done,
    Failed,
    Skipped,
}

public sealed class StateEntry
{
    public ImportStatus Status { get; set; }
    public DateTimeOffset At { get; set; }

    /// <summary>Tên file sao kê đã che số tài khoản.</summary>
    public string? SourceFile { get; set; }
    public long? Amount { get; set; }
    public string? Category { get; set; }
    public string? Error { get; set; }
    public string? Reason { get; set; }
    public string? Screenshot { get; set; }

    /// <summary>Không xác nhận được app đã lưu hay chưa — cần kiểm tra tay trước khi nhập lại (SPEC 9).</summary>
    public bool NeedsManualCheck { get; set; }
}

/// <summary>
/// state.json dùng chung cho mọi file sao kê, khóa = Mã giao dịch (SPEC 9).
/// Ghi atomic sau mỗi thay đổi. Thread-safe.
/// </summary>
public sealed class StateStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _lock = new();
    private Dictionary<string, StateEntry> _rows = new(StringComparer.Ordinal);

    public string FilePath { get; }

    private StateStore(string filePath) => FilePath = filePath;

    /// <summary>Đọc state từ file; file chưa có thì bắt đầu rỗng.</summary>
    public static StateStore Load(string filePath)
    {
        var store = new StateStore(filePath);
        if (!File.Exists(filePath)) return store;

        using var stream = File.OpenRead(filePath);
        var doc = JsonSerializer.Deserialize<StateDocument>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"File state không hợp lệ: {filePath}");

        if (doc.Version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"File state được tạo bởi phiên bản mới hơn (version {doc.Version}). Hãy cập nhật app.");
        }

        store._rows = new Dictionary<string, StateEntry>(doc.Rows, StringComparer.Ordinal);
        return store;
    }

    public StateEntry? Get(string id)
    {
        lock (_lock) return _rows.TryGetValue(id, out var e) ? e : null;
    }

    public IReadOnlyDictionary<string, StateEntry> Snapshot()
    {
        lock (_lock) return new Dictionary<string, StateEntry>(_rows, StringComparer.Ordinal);
    }

    public void MarkDone(string id, string? sourceFile, long amount, string category) =>
        Set(id, new StateEntry
        {
            Status = ImportStatus.Done,
            At = DateTimeOffset.Now,
            SourceFile = sourceFile,
            Amount = amount,
            Category = category,
        });

    public void MarkFailed(string id, string? sourceFile, string error, string? screenshot = null,
        bool needsManualCheck = false) =>
        Set(id, new StateEntry
        {
            Status = ImportStatus.Failed,
            At = DateTimeOffset.Now,
            SourceFile = sourceFile,
            Error = error,
            Screenshot = screenshot,
            NeedsManualCheck = needsManualCheck,
        });

    public void MarkSkipped(string id, string? sourceFile, string reason) =>
        Set(id, new StateEntry
        {
            Status = ImportStatus.Skipped,
            At = DateTimeOffset.Now,
            SourceFile = sourceFile,
            Reason = reason,
        });

    public bool Remove(string id)
    {
        lock (_lock)
        {
            if (!_rows.Remove(id)) return false;
            SaveLocked();
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _rows.Clear();
            SaveLocked();
        }
    }

    private void Set(string id, StateEntry entry)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id trống.", nameof(id));
        lock (_lock)
        {
            _rows[id] = entry;
            SaveLocked();
        }
    }

    /// <summary>Ghi ra file tạm rồi rename để không bao giờ để lại file hỏng.</summary>
    private void SaveLocked()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = FilePath + ".tmp";
        var doc = new StateDocument { Version = CurrentVersion, Rows = _rows };

        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, doc, JsonOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(tmp, FilePath, overwrite: true);
    }

    private sealed class StateDocument
    {
        public int Version { get; set; } = CurrentVersion;
        public Dictionary<string, StateEntry> Rows { get; set; } = new();
    }
}
