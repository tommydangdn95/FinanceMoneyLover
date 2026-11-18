# CLAUDE.md

Hướng dẫn cho Claude Code khi làm việc trong repo này. **Đặc tả đầy đủ nằm ở [SPEC.md](SPEC.md). Đọc SPEC trước khi code**, và cập nhật SPEC khi thay đổi hành vi.

## Dự án

`MoneyLoverAutoInput` là app desktop **WPF / .NET 8 cho Windows 11**. App đọc file sao kê ngân hàng (`.xlsx`), phân loại giao dịch theo luật, cho người dùng xem và sửa trên lưới, rồi nhập vào app Money Lover trên Android qua Appium (UiAutomator2). Money Lover không có import và cũng không còn web app, nên cách duy nhất là tự động hóa UI.

## Trạng thái

**Đã xong** (có test):
- **Core:** `Input/` (đọc sao kê, validate), `Categorization/`, `State/`, `Import/StatementLoader` và `RowStatus`, `Privacy/Masking`, `AppDataPaths`.
- **App:** khung WPF (FluentWindow, điều hướng, Host/DI, Serilog, chặn hai bản app chạy cùng lúc, bắt exception toàn cục).
  - **Xem trước** chạy được đầy đủ: mở hoặc kéo thả file, phân loại, **sửa mọi cột** (ngày, loại, số tiền, nhóm, ví, ghi chú), **xóa / khôi phục một hoặc nhiều dòng**, **tự lưu nháp** (`drafts.json`), hoàn tác từng dòng hoặc tất cả, lọc, tìm kiếm, tính tổng, tự phân loại lại khi file config đổi.
  - **Thiết lập** (SPEC 10.5): ví, danh sách nhóm, nhóm mặc định, bảng luật (thêm, xóa, đổi thứ tự), thử luật, lưu có kiểm tra hợp lệ (`Core/Configuration/SettingsFile`). Lần đầu mở app (`SetupCompleted = false`) sẽ vào thẳng trang này.
  - **Cài đặt** mới có các nút mở thư mục dữ liệu, mở file config, tải lại config.

  - **Môi trường** (`Core/Tools`: `AdbClient`, `AppiumServerManager`, `EnvironmentChecker`). Namespace là `Core.Tools`, **không** đặt `Core.Environment` vì sẽ che mất `System.Environment`.
  - **Chạy** (`Core/Automation/MoneyLoverUi` theo SPEC 8.2, `Core/Import/ImportRunner`). Nút "Chạy thử" và "Bắt đầu nhập" đã bật.
    - Logic thuần đã có test: `CategoryListParser`/`CategorySearch` (đọc màn chọn nhóm, "SHOW MORE…"), parse số tiền và ngày, và runner (dùng `IMoneyLoverUi` giả).
    - **CHƯA chạy trên điện thoại thật.** Lần chạy thử đầu do người dùng tự bấm. `DiscardConfirm` (`android:id/button1`) chưa được xác minh.

**Chưa làm:**
1. Trang Lịch sử.
2. Trang Cài đặt dạng form (Tools/Appium/Timing/Wallets), nhớ kích thước cửa sổ.

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
- **DataGrid ở theme tối:** WPF mặc định tô nền dòng bằng `SystemColors.Window` (trắng), và WPF-UI không ghi đè màu này. `App.xaml` có style toàn cục cho DataGrid/Row/Cell dùng màu theo theme.
  - Style kế thừa phải dùng `BasedOn="{StaticResource DefaultDataGridStyle}"` (tương tự `DefaultDataGridRowStyle`, `DefaultDataGridCellStyle`). **Không dùng** `{x:Type DataGrid}`, vì nó rơi về style của Windows và làm mất tiêu đề cột tối.
  - Control WPF-UI đặt trong ô (ComboBox, DatePicker) cần `MinWidth="0" Padding="8,4"`, không thì chữ bị che trong cột hẹp.
- **Ô chọn có danh sách** (nhóm, ví): dùng `Controls/SearchComboBox` (`ItemsSource` + `Value`). Đây là TextBox kèm Popup, tìm theo "chứa", không phân biệt dấu (`TextNormalizer.ContainsNormalized`).
  - **Không dùng ComboBox editable** cho mục đích này. TextSearch mặc định chỉ khớp tiền tố, nên gõ "petrol" không ra "Transportation > Petrol". Còn nếu lọc danh sách của chính ComboBox thì nó tự đặt lại Text theo mục đang chọn, làm chữ đang gõ bị xóa (người dùng báo là không gõ được).
- **Kiểm tra giao diện bằng ảnh chụp thật** — CHỈ chụp cửa sổ app (`PrintWindow` theo handle), **không bao giờ chụp toàn màn hình** (`CopyFromScreen`), vì sẽ dính các cửa sổ riêng tư khác của người dùng. Không giành focus hay điều khiển màn hình của người dùng khi chưa được đồng ý. (Win32 `PrintWindow` với cờ `PW_RENDERFULLCONTENT`, rồi xem ảnh PNG). UI Automation chỉ đọc cây control nên không phát hiện được lỗi màu hay bố cục: lưới trắng xóa trước đây vẫn "đọc" ra đủ dữ liệu.
- **Không dùng `ui:InfoBar`** của WPF-UI 4.3: khi bind `IsOpen` nó không hiển thị. Dùng `Views/Banner` và bật/tắt bằng `Visibility`.
- **Ghi config từ UI:** luôn sửa trên `SettingsFile.Clone(...)`, không bao giờ sửa trực tiếp `IOptionsMonitor.CurrentValue`. Lưu bằng `SettingsFile.Save` (atomic) rồi gọi `IConfigurationRoot.Reload()`.
- **Binding lệnh từ trong cell template của `DataGrid`:** dùng `RelativeSource AncestorType=UserControl`, không dùng `ElementName`.
- **ComboBox trong cell của `DataGrid`:** bind `SelectedItem` (Mode=TwoWay) vào đúng instance trong danh sách tĩnh (xem `LoaiOption`, `TypeOption`). **Không dùng `SelectedValue` + `SelectedValuePath`**: đã kiểm tra, giá trị không được đẩy về ViewModel và cũng không báo lỗi gì.
- **Culture vi-VN** đặt trong **constructor** của `App`. Đặt trong `OnStartup` async thì sau `await` culture mất hiệu lực, và DatePicker hiện ngày kiểu Mỹ.
- Lỗi binding WPF được ghi vào log app (`BindingErrorListener`).
- **Chỉnh sửa trên Xem trước:** mọi thay đổi đi qua `RowDraft` → `DraftStore` (drafts.json). Không tự giữ chỉnh sửa trong ViewModel; muốn dựng lại lưới thì gọi `StatementLoader.Process(read, settings, drafts)`.
- **MVVM nghiêm ngặt:** code-behind của View chỉ chứa `InitializeComponent` và phần thuần hiển thị. Logic đặt trong ViewModel (`[ObservableProperty]`, `[RelayCommand]`). Dialog và chọn file đi qua service để test được.
- **DI** qua Generic Host trong `App.xaml.cs`. Config dùng `IOptionsMonitor<T>`, vì người dùng có thể bấm "Tải lại cấu hình" lúc app đang mở.
- **Config:**
  - Chỉ đọc file người dùng, **không merge** với `appsettings.default.json`, vì binder gộp mảng theo index (SPEC 5.1).
  - Giá trị mặc định đặt ở initializer của class `AppSettings`. Initializer của **List/Dictionary phải để rỗng**, vì binder sẽ append vào chứ không thay thế.
  - Thêm key mới thì sửa cả `AppSettings.cs` lẫn `appsettings.default.json`. `DefaultConfigTests` sẽ bắt lỗi bind.
- **Nhóm là cây 2 cấp** (`KnownCategories: { "Chi": [ { Name, Children } ] }`).
  - Mọi nơi khác tham chiếu nhóm bằng **đường dẫn** `"Cha > Con"` (`CategoryTree.PathOf/Split/Flatten`). Không so khớp nhóm bằng tên lá, vì có tên trùng giữa nhóm cha và nhóm con.
  - Cây nhóm thật của người dùng chỉ nằm trong config ở `%LOCALAPPDATA%`, vì có tên người thân. **Không** copy cây đó vào `appsettings.default.json` hay vào test.
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
- Mã trùng trong file chỉ **cảnh báo** (`TransactionRow.Warnings`); lần xuất hiện thứ n nhận khóa `MÃ#n`. Luôn dùng `Id` làm khóa cho state/nháp, và `TxnCode` khi cần hiển thị mã gốc.

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

- Máy Windows cần có Node.js + Appium (2.x trở lên; máy dev dùng 4.0) + `uiautomator2`, Android Platform-Tools, JDK 17 trở lên, cùng `ANDROID_HOME` và `JAVA_HOME`. App không đóng gói sẵn những thứ này mà chỉ kiểm tra rồi hướng dẫn cài.
- Money Lover của người dùng để **giao diện tiếng Anh**: selector theo text ("Save", "Select category", "Write note", "Expense"/"Income"), tên nhóm ("Water Bill", "Entertainment", "Incoming transfer"...) và tên ví đều tiếng Anh. Ví dùng cho sao kê là **"TP Bank"**; có một ví khác tên "TPBank" (liên kết, không dùng), nên phải khớp tên ví **chính xác**.
- Màn Add transaction dùng **bàn phím số riêng của app** và chưa thấy ô ngày (chi tiết ở SPEC 8.2). Selector hiện mới lấy từ ảnh chụp; còn thiếu `resource-id`.
- Người dùng đã **nhập tay** một phần giao dịch của kỳ sao kê, nên có nguy cơ nhập trùng (SPEC 14).
- Khi chạy, màn hình phải mở khóa và bật "Stay awake".
- Máy Xiaomi/Redmi/POCO phải bật thêm "USB debugging (Security settings)".
- **Môi trường đã cài và kiểm tra (15/11/2026)** trên máy dev:
  - Android SDK có sẵn ở `%LOCALAPPDATA%\Android\Sdk`. `ANDROID_HOME`, `ANDROID_SDK_ROOT` và `platform-tools` trong PATH được đặt ở mức **user**.
  - JDK 26 (`JAVA_HOME`, mức machine).
  - Appium **4.0.0** (`npm i -g appium`) và driver **uiautomator2 9.1.0**. `appium driver doctor uiautomator2` báo 0 lỗi bắt buộc.
  - Terminal hoặc VS Code mở **trước** lúc cài phải mở lại thì mới nhận PATH mới.
- **Điện thoại:** Samsung, Android 10, Money Lover **8.93.0**. `AppPackage` = `com.bookmark.money` đã xác minh bằng `adb shell pm list packages`. Android 10 **không có Wireless debugging** (cần Android 11+), nên chỉ dùng được USB.
