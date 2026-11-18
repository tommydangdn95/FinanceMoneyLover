using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.Core.Manual;

/// <summary>Một giao dịch người dùng tự gõ trên màn "Nhập tay" (SPEC 10.7) — VD chi tiêu tiền mặt.</summary>
public sealed class ManualEntry
{
    /// <summary>Khóa trong state.json: "TAY-yyyyMMddHHmmss-" + 12 hex ngẫu nhiên. Không bao giờ đổi sau khi tạo.</summary>
    public string Id { get; set; } = "";
    public DateOnly Ngay { get; set; }
    public TransactionType Loai { get; set; } = TransactionType.Chi;
    public long SoTien { get; set; }
    public string Nhom { get; set; } = "";
    public string Vi { get; set; } = "";
    public string GhiChu { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public const string IdPrefix = "TAY-";

    public static ManualEntry Create(DateOnly ngay, TransactionType loai, long soTien, string nhom, string vi, string ghiChu,
        DateTimeOffset? now = null)
    {
        var at = now ?? DateTimeOffset.Now;
        return new ManualEntry
        {
            Id = $"{IdPrefix}{at:yyyyMMddHHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()}",
            Ngay = ngay, Loai = loai, SoTien = soTien, Nhom = nhom.Trim(), Vi = vi.Trim(), GhiChu = ghiChu.Trim(),
            CreatedAt = at,
        };
    }

    public ManualEntry Copy() => new()
    {
        Id = Id, Ngay = Ngay, Loai = Loai, SoTien = SoTien, Nhom = Nhom, Vi = Vi, GhiChu = GhiChu, CreatedAt = CreatedAt,
    };

    /// <summary>Chuyển sang dòng giao dịch để dùng chung <c>ImportRunner</c> với sao kê.</summary>
    public TransactionRow ToTransactionRow() => new()
    {
        Id = Id,
        TxnCode = Id,
        SortKey = Ngay.ToDateTime(TimeOnly.MinValue),
        OriginalNgay = Ngay,
        OriginalLoai = Loai,
        OriginalSoTien = SoTien,
        Ngay = Ngay,
        Loai = Loai,
        SoTien = SoTien,
        Nhom = Nhom,
        Vi = Vi,
        GhiChu = GhiChu,
        Description = GhiChu,
        Action = RuleAction.Import,
    };
}

/// <summary>
/// manual.json: danh sách giao dịch nhập tay. Ghi atomic sau mỗi thay đổi (đóng app không mất dữ liệu). Thread-safe.
/// Trạng thái đã nhập/lỗi nằm ở state.json theo <see cref="ManualEntry.Id"/>, như giao dịch từ sao kê.
/// </summary>
public sealed class ManualEntryStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _lock = new();
    private List<ManualEntry> _entries = new();

    public string FilePath { get; }

    private ManualEntryStore(string filePath) => FilePath = filePath;

    public static ManualEntryStore Load(string filePath)
    {
        var store = new ManualEntryStore(filePath);
        if (!File.Exists(filePath)) return store;

        using var stream = File.OpenRead(filePath);
        var doc = JsonSerializer.Deserialize<Document>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"File nhập tay không hợp lệ: {filePath}");
        if (doc.Version > CurrentVersion)
            throw new InvalidDataException($"File nhập tay được tạo bởi phiên bản mới hơn (version {doc.Version}).");

        store._entries = doc.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Id)).ToList();
        return store;
    }

    /// <summary>Bản sao, sắp theo ngày rồi theo thời điểm tạo.</summary>
    public IReadOnlyList<ManualEntry> All()
    {
        lock (_lock)
            return _entries.OrderBy(e => e.Ngay).ThenBy(e => e.CreatedAt).Select(e => e.Copy()).ToList();
    }

    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    public void Add(ManualEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id)) throw new ArgumentException("Thiếu Id.", nameof(entry));
        lock (_lock)
        {
            if (_entries.Any(e => e.Id == entry.Id)) throw new InvalidOperationException($"Trùng Id {entry.Id}.");
            _entries.Add(entry.Copy());
            SaveLocked();
        }
    }

    /// <summary>Ghi đè theo Id. Trả về false nếu không còn dòng đó.</summary>
    public bool Update(ManualEntry entry)
    {
        lock (_lock)
        {
            var i = _entries.FindIndex(e => e.Id == entry.Id);
            if (i < 0) return false;
            _entries[i] = entry.Copy();
            SaveLocked();
            return true;
        }
    }

    public int RemoveMany(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        lock (_lock)
        {
            var removed = _entries.RemoveAll(e => set.Contains(e.Id));
            if (removed > 0) SaveLocked();
            return removed;
        }
    }

    private void SaveLocked()
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(FilePath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = FilePath + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, new Document { Version = CurrentVersion, Entries = _entries }, JsonOptions);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, FilePath, overwrite: true);
    }

    private sealed class Document
    {
        public int Version { get; set; } = CurrentVersion;
        public List<ManualEntry> Entries { get; set; } = new();
    }
}
