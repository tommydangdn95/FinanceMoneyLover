using System.Text.Json;
using System.Text.Json.Serialization;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Drafts;

/// <summary>
/// Chỉnh sửa tay của người dùng trên một dòng (SPEC 10.3). Trường null = không sửa, dùng giá trị từ sao kê/luật.
/// </summary>
public sealed class RowDraft
{
    public DateOnly? Ngay { get; set; }
    public TransactionType? Loai { get; set; }
    public long? SoTien { get; set; }
    public string? Nhom { get; set; }
    public string? Vi { get; set; }
    public string? GhiChu { get; set; }

    /// <summary>Người dùng đã tick/bỏ tick. null = dùng mặc định theo trạng thái.</summary>
    public bool? Selected { get; set; }

    /// <summary>true = người dùng đã xóa dòng khỏi danh sách nhập (ẩn, không nhập, khôi phục được).</summary>
    public bool? Deleted { get; set; }

    [JsonIgnore]
    public bool IsDeleted => Deleted == true;

    /// <summary>Có sửa nội dung giao dịch (không tính việc chọn/bỏ chọn).</summary>
    [JsonIgnore]
    public bool HasContentEdits =>
        Ngay is not null || Loai is not null || SoTien is not null || Nhom is not null || Vi is not null || GhiChu is not null;

    [JsonIgnore]
    public bool IsEmpty => !HasContentEdits && Selected is null && !IsDeleted;

    /// <summary>Ghi đè các trường đã sửa lên giao dịch.</summary>
    public void ApplyTo(TransactionRow row)
    {
        if (Ngay is { } ngay) row.Ngay = ngay;
        if (Loai is { } loai) row.Loai = loai;
        if (SoTien is { } soTien) row.SoTien = soTien;
        if (Nhom is not null) row.Nhom = Nhom;
        if (Vi is not null) row.Vi = Vi;
        if (GhiChu is not null) row.GhiChu = GhiChu;
    }
}

/// <summary>
/// drafts.json: bản nháp chỉnh sửa trên Xem trước, khóa = Mã giao dịch, dùng chung cho mọi file sao kê.
/// Ghi atomic sau mỗi thay đổi để đóng app (hoặc app crash) vẫn không mất chỉnh sửa. Thread-safe.
/// </summary>
public sealed class DraftStore
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
    private Dictionary<string, RowDraft> _rows = new(StringComparer.Ordinal);

    public string FilePath { get; }

    private DraftStore(string filePath) => FilePath = filePath;

    public static DraftStore Load(string filePath)
    {
        var store = new DraftStore(filePath);
        if (!File.Exists(filePath)) return store;

        using var stream = File.OpenRead(filePath);
        var doc = JsonSerializer.Deserialize<DraftDocument>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"File nháp không hợp lệ: {filePath}");
        if (doc.Version > CurrentVersion)
            throw new InvalidDataException($"File nháp được tạo bởi phiên bản mới hơn (version {doc.Version}).");

        store._rows = new Dictionary<string, RowDraft>(doc.Rows, StringComparer.Ordinal);
        return store;
    }

    /// <summary>Bản sao của nháp (sửa bản sao không ảnh hưởng store cho tới khi gọi <see cref="Set"/>).</summary>
    public RowDraft? Get(string id)
    {
        lock (_lock) return _rows.TryGetValue(id, out var d) ? Copy(d) : null;
    }

    public int Count
    {
        get { lock (_lock) return _rows.Count; }
    }

    /// <summary>Lưu nháp của một dòng; nháp rỗng thì xóa.</summary>
    public void Set(string id, RowDraft draft)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        lock (_lock)
        {
            if (draft.IsEmpty) _rows.Remove(id);
            else _rows[id] = Copy(draft);
            SaveLocked();
        }
    }

    public void Remove(string id)
    {
        lock (_lock)
        {
            if (_rows.Remove(id)) SaveLocked();
        }
    }

    public void RemoveMany(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            var changed = false;
            foreach (var id in ids) changed |= _rows.Remove(id);
            if (changed) SaveLocked();
        }
    }

    /// <summary>Áp nháp lên các dòng; trả về số dòng có chỉnh sửa nội dung.</summary>
    public int ApplyTo(IEnumerable<TransactionRow> rows)
    {
        var count = 0;
        lock (_lock)
        {
            foreach (var row in rows)
            {
                if (!_rows.TryGetValue(row.Id, out var d)) continue;
                d.ApplyTo(row);
                if (d.HasContentEdits) count++;
            }
        }
        return count;
    }

    private static RowDraft Copy(RowDraft d) => new()
    {
        Ngay = d.Ngay, Loai = d.Loai, SoTien = d.SoTien, Nhom = d.Nhom, Vi = d.Vi, GhiChu = d.GhiChu,
        Selected = d.Selected, Deleted = d.Deleted,
    };

    private void SaveLocked()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = FilePath + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new DraftDocument { Version = CurrentVersion, Rows = _rows }, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, FilePath, overwrite: true);
    }

    private sealed class DraftDocument
    {
        public int Version { get; set; } = CurrentVersion;
        public Dictionary<string, RowDraft> Rows { get; set; } = new();
    }
}
