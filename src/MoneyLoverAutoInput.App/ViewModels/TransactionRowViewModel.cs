using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Import;
using MoneyLoverAutoInput.Core.Models;

namespace MoneyLoverAutoInput.App.ViewModels;

/// <summary>Một dòng trên lưới Xem trước.</summary>
public sealed partial class TransactionRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public TransactionRow Model { get; }
    public RowStatus Status { get; }
    public IReadOnlyList<string> CategoryOptions { get; }

    /// <summary>Người dùng đã sửa tay — giữ lại khi phân loại lại.</summary>
    public bool IsNhomEdited { get; private set; }
    public bool IsGhiChuEdited { get; private set; }

    public TransactionRowViewModel(TransactionRow model, RowStatus status, IReadOnlyList<string> categoryOptions)
    {
        Model = model;
        Status = status;
        CategoryOptions = categoryOptions;
        _isSelected = RowStatusResolver.IsSelectedByDefault(status);
    }

    public string Id => Model.Id;
    public int ExcelRow => Model.ExcelRow;
    public string NgayText => Model.Ngay == default ? "" : Model.Ngay.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public TransactionType Loai => Model.Loai;
    public bool IsExpense => Model.Loai == TransactionType.Chi;
    public long SoTien => Model.SoTien;
    public string SoTienText => Model.SoTien.ToString("#,##0", Vi);
    public string Wallet => Model.Vi;
    public string Description => Model.Description;
    public string MatchedRule => Model.Action == RuleAction.Skip ? $"Bỏ qua: {Model.MatchedRule}" : Model.MatchedRule;

    public string StatusText => RowStatusResolver.ToDisplayText(Status);
    public bool IsSelectable => RowStatusResolver.IsSelectable(Status);
    public bool IsLocked => Status == RowStatus.Done;

    public string ErrorText => string.Join(" ", Model.Errors);
    public bool HasErrors => Model.Errors.Count > 0;

    /// <summary>Được chọn và đủ điều kiện → sẽ được nhập.</summary>
    public bool WillImport => IsSelected && Model.CanImport;

    private bool _isSelected;

    /// <summary>Dòng đã nhập hoặc lỗi dữ liệu không thể được chọn (giá trị true bị bỏ qua).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !IsSelectable) value = false;
            if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(WillImport));
        }
    }

    public string Nhom
    {
        get => Model.Nhom;
        set
        {
            var v = value?.Trim() ?? "";
            if (v == Model.Nhom) return;
            Model.Nhom = v;
            IsNhomEdited = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ErrorText));
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(WillImport));
        }
    }

    public string GhiChu
    {
        get => Model.GhiChu;
        set
        {
            var v = value ?? "";
            if (v == Model.GhiChu) return;
            Model.GhiChu = v;
            IsGhiChuEdited = true;
            OnPropertyChanged();
        }
    }
}
