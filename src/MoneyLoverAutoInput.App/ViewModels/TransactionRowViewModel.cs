using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Drafts;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.App.ViewModels;

/// <summary>Môi trường chung mà mọi dòng trên lưới Xem trước cần (danh sách nhóm/ví, nhóm mặc định, lưu nháp).</summary>
public sealed record RowEditContext(
    Func<TransactionType, IReadOnlyList<string>> CategoryOptions,
    Func<TransactionType, string> DefaultCategory,
    IReadOnlyList<string> WalletOptions,
    Action<TransactionRowViewModel> DraftChanged);

/// <summary>Một dòng trên lưới Xem trước. Mọi chỉnh sửa ghi vào <see cref="Draft"/> và được lưu nháp ngay.</summary>
public sealed class TransactionRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static IReadOnlyList<ChoiceOption<TransactionType>> TypeOptions { get; } = new[]
    {
        new ChoiceOption<TransactionType>(TransactionType.Chi, "Chi"),
        new ChoiceOption<TransactionType>(TransactionType.Thu, "Thu"),
    };

    private readonly RowEditContext _ctx;
    private bool _isSelected;

    public TransactionRow Model { get; }
    public RowStatus Status { get; }
    public RowDraft Draft { get; }

    public TransactionRowViewModel(TransactionRow model, RowStatus status, RowDraft? draft, RowEditContext ctx)
    {
        Model = model;
        Status = status;
        Draft = draft ?? new RowDraft();
        _ctx = ctx;

        var selected = Draft.Selected ?? RowStatusResolver.IsSelectedByDefault(status);
        _isSelected = selected && IsSelectable;
    }

    // ---------- Chỉ đọc ----------

    public string Id => Model.Id;
    public int ExcelRow => Model.ExcelRow;
    public string Description => Model.Description;

    /// <summary>Người nhận (Chi) / người gửi (Thu) theo sao kê; "—" nếu ngân hàng không ghi.</summary>
    public string CounterpartText => string.IsNullOrWhiteSpace(Model.CounterpartName) ? "—" : Model.CounterpartName;

    public string CounterpartTooltip
    {
        get
        {
            var role = Model.Loai == TransactionType.Chi ? "Chuyển đến" : "Nhận từ";
            var name = string.IsNullOrWhiteSpace(Model.CounterpartName) ? "(ngân hàng không ghi tên)" : Model.CounterpartName;
            var account = string.IsNullOrWhiteSpace(Model.CounterpartAccount)
                ? ""
                : $"{Environment.NewLine}Tài khoản: {Core.Privacy.Masking.MaskAccount(Model.CounterpartAccount)}";
            return $"{role}: {name}{account}";
        }
    }
    public string MatchedRule => Model.Action == RuleAction.Skip ? $"Bỏ qua: {Model.MatchedRule}" : Model.MatchedRule;

    /// <summary>Tooltip của ô Nhóm: nhóm đầy đủ + luật đã khớp.</summary>
    public string MatchedRuleTooltip =>
        $"{Model.Nhom}{Environment.NewLine}" +
        (string.IsNullOrEmpty(Model.MatchedRule) ? "Không khớp luật nào (nhóm mặc định)" : "Luật khớp: " + MatchedRule);
    public string StatusText => IsDeleted
        ? "Đã xóa"
        : RowStatusResolver.ToDisplayText(Status) + (IsEdited ? " · đã sửa" : "");
    public bool IsSelectable => RowStatusResolver.IsSelectable(Status) && !IsDeleted;

    /// <summary>Dòng đã nhập, lỗi dữ liệu hoặc đã xóa: không cho sửa.</summary>
    public bool IsEditable => Status is not (RowStatus.Done or RowStatus.Invalid) && !IsDeleted;

    /// <summary>Người dùng đã xóa dòng khỏi danh sách nhập (lưu trong nháp, khôi phục được).</summary>
    public bool IsDeleted => Draft.IsDeleted;

    /// <summary>Dòng đã nhập vào Money Lover thì không xóa (chỉ ẩn được bằng bộ lọc).</summary>
    public bool CanDelete => Status != RowStatus.Done && !IsDeleted;
    public bool IsLocked => !IsEditable;

    public IReadOnlyList<string> CategoryOptions => _ctx.CategoryOptions(Model.Loai);
    public IReadOnlyList<string> WalletOptions => _ctx.WalletOptions;

    public string ErrorText => string.Join(" ", Model.Errors);
    public bool HasErrors => Model.Errors.Count > 0;

    /// <summary>Cảnh báo không chặn nhập (VD mã giao dịch trùng trong file).</summary>
    public string WarningText => string.Join(" ", Model.Warnings);
    public bool HasWarnings => Model.Warnings.Count > 0;

    /// <summary>Được chọn và đủ điều kiện → sẽ được nhập.</summary>
    public bool WillImport => IsSelected && !IsDeleted && Model.CanImport;

    public bool IsEdited => Draft.HasContentEdits;

    /// <summary>Giá trị gốc từ ngân hàng, hiện trong tooltip để đối chiếu.</summary>
    public string OriginalSummary =>
        $"Theo sao kê: {Model.OriginalNgay:dd/MM/yyyy} · {Model.OriginalLoai} · {Model.OriginalSoTien.ToString("#,##0", Vi)}" +
        $"{Environment.NewLine}Mô tả: {Model.Description}{Environment.NewLine}Mã GD: {Model.TxnCode} · dòng Excel {Model.ExcelRow}";

    // ---------- Chọn ----------

    /// <summary>Dòng đã nhập hoặc lỗi dữ liệu không thể được chọn (giá trị true bị bỏ qua).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !IsSelectable) value = false;
            if (!SetProperty(ref _isSelected, value)) return;
            Draft.Selected = value;
            OnPropertyChanged(nameof(WillImport));
            _ctx.DraftChanged(this);
        }
    }

    // ---------- Sửa được ----------

    public DateTime? NgayDate
    {
        get => Model.Ngay == default ? null : Model.Ngay.ToDateTime(TimeOnly.MinValue);
        set
        {
            if (!IsEditable || value is null) return;
            var d = DateOnly.FromDateTime(value.Value);
            if (d == Model.Ngay) return;
            Model.Ngay = d;
            Draft.Ngay = d == Model.OriginalNgay ? null : d;
            Edited(nameof(NgayDate));
        }
    }

    public TransactionType Loai
    {
        get => Model.Loai;
        set
        {
            if (!IsEditable || value == Model.Loai) return;
            Model.Loai = value;
            Draft.Loai = value == Model.OriginalLoai ? null : value;

            // Nhóm của loại cũ không dùng được cho loại mới → về nhóm mặc định của loại mới.
            if (!CategoryOptions.Contains(Model.Nhom))
            {
                Model.Nhom = _ctx.DefaultCategory(value);
                Draft.Nhom = Model.Nhom;
                OnPropertyChanged(nameof(Nhom));
            }

            OnPropertyChanged(nameof(IsExpense));
            OnPropertyChanged(nameof(CategoryOptions));
            OnPropertyChanged(nameof(CounterpartTooltip));
            Edited(nameof(Loai));
        }
    }

    /// <summary>Cho ComboBox Loại (bind SelectedItem vào đúng instance trong <see cref="TypeOptions"/>).</summary>
    public ChoiceOption<TransactionType> LoaiOption
    {
        get => TypeOptions.First(o => o.Value == Model.Loai);
        set
        {
            if (value is null) return;
            Loai = value.Value;
            OnPropertyChanged();
        }
    }

    public bool IsExpense => Model.Loai == TransactionType.Chi;
    public long SoTien => Model.SoTien;

    /// <summary>Số tiền dạng "45.000". Gõ "45000", "45.000" hay "45,000" đều được; gõ sai thì giữ giá trị cũ.</summary>
    public string SoTienText
    {
        get => Model.SoTien.ToString("#,##0", Vi);
        set
        {
            if (!IsEditable) return;
            var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
            var onlyAllowed = (value ?? "").All(c => char.IsDigit(c) || c is '.' or ',' || char.IsWhiteSpace(c));
            if (!onlyAllowed || !long.TryParse(digits.Length == 0 ? "0" : digits, out var amount))
            {
                OnPropertyChanged(); // trả ô về giá trị cũ
                return;
            }
            if (amount == Model.SoTien) { OnPropertyChanged(); return; }

            Model.SoTien = amount;
            Draft.SoTien = amount == Model.OriginalSoTien ? null : amount;
            OnPropertyChanged(nameof(SoTien));
            Edited(nameof(SoTienText));
        }
    }

    public string Nhom
    {
        get => Model.Nhom;
        set
        {
            var v = value?.Trim() ?? "";
            if (!IsEditable || v == Model.Nhom) return;
            Model.Nhom = v;
            Draft.Nhom = v;
            Edited(nameof(Nhom));
        }
    }

    public string Wallet
    {
        get => Model.Vi;
        set
        {
            var v = value?.Trim() ?? "";
            if (!IsEditable || v == Model.Vi) return;
            Model.Vi = v;
            Draft.Vi = v;
            Edited(nameof(Wallet));
        }
    }

    public string GhiChu
    {
        get => Model.GhiChu;
        set
        {
            var v = value ?? "";
            if (!IsEditable || v == Model.GhiChu) return;
            Model.GhiChu = v;
            Draft.GhiChu = v;
            Edited(nameof(GhiChu));
        }
    }

    /// <summary>Xóa (true) hoặc khôi phục (false) dòng. Trả về false nếu không làm được.</summary>
    public bool SetDeleted(bool deleted)
    {
        if (deleted == IsDeleted) return false;
        if (deleted && Status == RowStatus.Done) return false;

        Draft.Deleted = deleted ? true : null;
        foreach (var p in new[]
                 {
                     nameof(IsDeleted), nameof(CanDelete), nameof(IsSelectable), nameof(IsEditable), nameof(IsLocked),
                     nameof(WillImport), nameof(StatusText),
                 })
        {
            OnPropertyChanged(p);
        }
        _ctx.DraftChanged(this);
        return true;
    }

    private void Edited(string property)
    {
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(WillImport));
        OnPropertyChanged(nameof(IsEdited));
        OnPropertyChanged(nameof(StatusText));
        _ctx.DraftChanged(this);
    }
}
