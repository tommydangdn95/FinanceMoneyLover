# CLAUDE.md

Hướng dẫn cho Claude Code khi làm việc trong repo này. **Đặc tả đầy đủ nằm ở [SPEC.md](SPEC.md). Đọc SPEC trước khi code**, và cập nhật SPEC khi thay đổi hành vi.

## Dự án

`MoneyLoverAutoInput` là app desktop **WPF / .NET 8 cho Windows 11**. App đọc file sao kê ngân hàng (`.xlsx`), phân loại giao dịch theo luật, cho người dùng xem và sửa trên lưới, rồi nhập vào app Money Lover trên Android qua Appium (UiAutomator2). Money Lover không có import và cũng không còn web app, nên cách duy nhất là tự động hóa UI.

## Trạng thái

**Đã xong** (có test):
- **Core:** `Input/` (đọc sao kê, validate), `Categorization/`, `State/`, `Import/StatementLoader` và `RowStatus`, `Privacy/Masking`, `AppDataPaths`.
- **App:** khung WPF (FluentWindow, điều hướng, Host/DI, Serilog, chặn hai bản app chạy cùng lúc, bắt exception toàn cục).
  - **Xem trước** chạy được đầy đủ: mở hoặc kéo thả file, phân loại, sửa nhóm và ghi chú, lọc, tìm kiếm, tính tổng, tự phân loại lại khi file config đổi.
  - **Cài đặt** mới có các nút mở thư mục dữ liệu, mở file config, tải lại config.

**Chưa làm**, theo thứ tự nên làm:
1. `Core/Environment` và trang Môi trường (adb, Appium).
2. `Core/Automation` và `ImportRunner`, rồi trang Chạy. Phần này cần selector thật từ `uiautomator dump`; selector hiện vẫn toàn `TODO`.
3. Trang Luật phân loại, trang Lịch sử.
4. Trang Cài đặt dạng form, nhớ kích thước cửa sổ.

Các nút "Chạy thử" và "Bắt đầu nhập" đang bị disable có chủ đích. `PreviewViewModel.GetRowsToImport()` là đầu vào cho `ImportRunner`.

## Cấu trúc

```
src/MoneyLoverAutoInput.Core/   net8.0          logic: Input, Categorization, State, Environment, Import, Automation
src/MoneyLoverAutoInput.App/    net8.0-windows  WPF: Views, ViewModels, Services (không chứa logic nghiệp vụ)
tests/MoneyLoverAutoInput.Tests/                xUnit, chỉ tham chiếu Core
```

Thư viện chính:
- App: `WPF-UI` (FluentWindow, NavigationView), `CommunityToolkit.Mvvm`, `Microsoft.Extensions.Hosting`
- Core: `ClosedXML`, `Appium.WebDriver` 5.x, `Selenium.Support`, `Serilog`

## Lệnh

```powershell
dotnet build MoneyLoverAutoInput.slnx
dotnet test MoneyLoverAutoInput.slnx
dotnet run --project src/MoneyLoverAutoInput.App
dotnet run --project src/MoneyLoverAutoInput.App -- "samples\sao_ke_mau.xlsx"   # mở thẳng file
$env:MLAI_REAL_STATEMENT="<đường dẫn sao kê thật>"; dotnet test --filter "FullyQualifiedName~Real_statement"   # chỉ kiểm cấu trúc, không copy file
dotnet publish src/MoneyLoverAutoInput.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
appium                                   # Appium server :4723 (cài: npm i -g appium; appium driver install uiautomator2)
adb devices
adb shell uiautomator dump /sdcard/ui.xml; adb pull /sdcard/ui.xml   # soi selector
```

Shell chính là PowerShell 5.1, nên không dùng `&&`. Máy có .NET SDK 10 kèm runtime 8, nên solution là `.slnx` nhưng các project vẫn target `net8.0`. Nếu viết script `.ps1` có chữ tiếng Việt thì phải lưu UTF-8 **có BOM**, không thì PowerShell 5.1 đọc sai.

Dữ liệu runtime khi chạy thử nằm ở `%LOCALAPPDATA%\MoneyLoverAutoInput\`. Xóa `appsettings.json` ở đó để app tạo lại từ bản mặc định.

## Quy tắc kiến trúc

- **Core không được tham chiếu WPF** (không dùng `System.Windows`, `Dispatcher`). Core giao tiếp với UI qua `IProgress<T>`, `CancellationToken`, event, interface.
- **MVVM nghiêm ngặt:** code-behind của View chỉ chứa `InitializeComponent` và phần thuần hiển thị. Logic đặt trong ViewModel (`[ObservableProperty]`, `[RelayCommand]`). Dialog và chọn file đi qua service để test được.
- **DI** qua Generic Host trong `App.xaml.cs`. Config dùng `IOptionsMonitor<T>`, vì người dùng có thể bấm "Tải lại cấu hình" lúc app đang mở.
- **Config:**
  - Chỉ đọc file người dùng, **không merge** với `appsettings.default.json`, vì binder gộp mảng theo index (SPEC 5.1).
  - Giá trị mặc định đặt ở initializer của class `AppSettings`. Initializer của **List/Dictionary phải để rỗng**, vì binder sẽ append vào chứ không thay thế.
  - Thêm key mới thì sửa cả `AppSettings.cs` lẫn `appsettings.default.json`. `DefaultConfigTests` sẽ bắt lỗi bind.
- **Lỗi của một dòng giao dịch:**
  - `TransactionRow.DataErrors` là lỗi cố định (ngày, tiền, mã giao dịch).
  - "Chưa phân loại" được tính động trong `Errors` / `CanImport` và tự hết khi người dùng chọn nhóm.
- **Không chặn UI thread:**
  - Lệnh Appium và `adb` là đồng bộ hoặc chậm, nên luôn bọc trong `Task.Run`.
  - Không dùng `.Result` hoặc `.Wait()` trên UI thread.
  - Cập nhật `ObservableCollection` từ background thread phải marshal về UI thread, hoặc dùng `BindingOperations.EnableCollectionSynchronization`.
- **Dừng an toàn:** chỉ kiểm tra `CancellationToken` giữa các bước UI và giữa các dòng. Sau khi đã bấm Lưu thì **không** hủy nữa (SPEC 8.2).
- **Dữ liệu runtime** (config người dùng, `state.json`, logs, screenshots) nằm ở `%LOCALAPPDATA%\MoneyLoverAutoInput\`, **không** ghi cạnh file exe.
- App chỉ chạy một instance (Mutex), vì `state.json` không chịu được hai process cùng ghi.

## Định dạng sao kê (tóm tắt, chi tiết ở SPEC mục 6)

- Có 1 sheet. Metadata nằm ở trên (`Thời gian:`, `Tài khoản giao dịch:` ở cột A/B), tiêu đề bảng ở **dòng 9**, dữ liệu từ dòng 10, **mới nhất ở trên**.
- Cột: `Ngày thực hiện` | `Ngày hiệu lực` | `Mô tả giao dịch` | `Ghi nợ` | `Ghi có` | `Số dư` | `Tài khoản đối ứng` | `Tên tài khoản` | `Mã giao dịch`.
- Những chỗ dễ sai:
  - Ngày là **chuỗi text** (`dd-MM-yyyy HH:mm:ss` và `dd/MM/yyyy`), không phải kiểu Date.
  - Ô trống là chuỗi rỗng `''`, không phải null.
  - `UsedRange` rộng hơn dữ liệu thật.
- Tìm dòng tiêu đề và cột **theo tên**, không hard-code chỉ số.
- `Ghi nợ` > 0 nghĩa là Chi, `Ghi có` > 0 nghĩa là Thu. `Mã giao dịch` là khóa chống trùng trong `state.json`, dùng chung cho mọi file.

## Quy ước code

- **Không hard-code selector hay tên nhóm/ví trong code.** Selector nằm ở `Selectors`, luật phân loại ở `CategoryRules`, danh sách nhóm ở `KnownCategories`.
- Selector ưu tiên theo thứ tự `resource-id` > `content-desc` > `text`. Không dùng xpath theo index.
- Parse ngày và số dùng `CultureInfo.InvariantCulture` và `ParseExact`. Tiền dùng `long` (VND). Hiển thị tiền theo `#,###`.
- So khớp luật phân loại phải chuẩn hóa trước: bỏ dấu tiếng Việt, lowercase, gộp khoảng trắng.
- State ghi sau **mỗi** giao dịch, theo kiểu atomic (ghi file tạm rồi rename).
- Phân biệt các loại lỗi ở SPEC mục 11 (môi trường, kết nối, không tìm thấy phần tử, dữ liệu). Khi lỗi trong lúc nhập luôn lưu screenshot kèm `PageSource`.
- Chữ trên UI, thông báo và log viết **tiếng Việt**. Tên class/method/biến viết tiếng Anh, trừ các tên trường đã cố định trong SPEC (`Ngay`, `Loai`, `SoTien`, `Nhom`, `Vi`, `GhiChu`).
- Bật `<Nullable>enable</Nullable>` và `<TreatWarningsAsErrors>` cho Core.

## Dữ liệu cá nhân (bắt buộc)

- File sao kê thật (`Account Enquiry-*.xlsx`) chứa số tài khoản, số điện thoại, tên đối tác và số dư. **Không** copy vào `samples/` hay `tests/`, không đưa vào commit, không dán nguyên nội dung vào output.
- File mẫu cho test phải **tự tạo và ẩn danh** (số TK, SĐT, mã GD đều giả), giữ đúng bố cục và kiểu ô của file thật.
- Trên UI, log và state phải che số tài khoản (chỉ giữ 4 số cuối). **Không hiển thị và không log số dư.**
- `.gitignore` phải có: `Account Enquiry-*.xlsx`, `state.json`, `logs/`, `screenshots/`, `bin/`, `obj/`, `*.user`, `.vs/`.

## Lưu ý thiết bị

- Máy Windows cần có Node.js + Appium 2 + `uiautomator2`, Android Platform-Tools, JDK 17, cùng `ANDROID_HOME` và `JAVA_HOME`. App không đóng gói sẵn những thứ này mà chỉ kiểm tra rồi hướng dẫn cài.
- Money Lover phải để tiếng Việt. Khi chạy, màn hình phải mở khóa và bật "Stay awake".
- Máy Xiaomi/Redmi/POCO phải bật thêm "USB debugging (Security settings)".
- Chưa xác minh `AppPackage` `com.bookmark.money`. Kiểm tra bằng `adb shell dumpsys window | findstr mCurrentFocus`.
