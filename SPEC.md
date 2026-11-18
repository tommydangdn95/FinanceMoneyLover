# SPEC — MoneyLoverAutoInput

Ứng dụng desktop **Windows 11 (WPF / .NET 8)**. App đọc **file sao kê tài khoản ngân hàng (Excel "Lịch sử giao dịch tài khoản")**, tự phân loại từng giao dịch theo luật, cho người dùng xem và sửa trước, rồi tự động nhập vào app **Money Lover** trên điện thoại Android bằng cách điều khiển giao diện app qua **Appium (UiAutomator2)**.

---

## 1. Bối cảnh & lý do

- Money Lover **không hỗ trợ import CSV/Excel** (chỉ export). File backup `.mlx` dùng để restore, không phải để nhập giao dịch mới.
- **Web app đã ngừng từ 22/09/2023**, nên không còn trang web nào để soi request hoặc tự động hóa qua trình duyệt.
- Nguồn dữ liệu là file sao kê xuất từ internet banking. File này **không có cột nhóm (category)**, nên app phải tự gán nhóm dựa trên mô tả giao dịch, và người dùng có thể sửa lại trên giao diện.
- App giả lập thao tác người dùng trên app Android: bấm "+", nhập số tiền, chọn nhóm, ví, ngày, ghi chú, rồi bấm Lưu.

## 2. Mục tiêu

| # | Mục tiêu |
|---|---|
| G1 | Đọc file sao kê ngân hàng `.xlsx` (định dạng ở mục 6), chọn file bằng hộp thoại hoặc kéo thả |
| G2 | Tự gán `Loai` (Chi/Thu) từ cột Ghi nợ/Ghi có, và gán `Nhom` theo luật từ khóa (mục 7.2) |
| G3 | **Màn hình xem trước:** người dùng sửa nhóm, bỏ chọn dòng, xem dòng lỗi hoặc dòng đã nhập trước khi chạy |
| G4 | Nhập từng giao dịch vào Money Lover: loại, số tiền, nhóm, ví, ngày, ghi chú. Có tiến trình, log trực tiếp, Dừng/Tiếp tục |
| G5 | Không nhập trùng khi chạy lại hoặc khi các file sao kê có khoảng thời gian chồng nhau, dựa trên `Mã giao dịch` |
| G6 | Ghi log và chụp màn hình khi lỗi, rồi chạy tiếp dòng kế tiếp |
| G7 | **Kiểm tra môi trường** (adb, Appium, thiết bị, app Money Lover) và **tự khởi động Appium server** |
| G8 | Selector và luật phân loại nằm trong file cấu hình. Luật phân loại sửa được ngay trong app |

**Ngoài phạm vi (v1):** iOS, macOS/Linux, định dạng sao kê của ngân hàng khác (chỉ cần thiết kế để dễ thêm sau), tự đọc thông báo ngân hàng, chạy không cần PC, sửa hoặc xóa giao dịch đã có, nhập dạng "chuyển khoản giữa các ví" của Money Lover, trình soạn selector trong app (sửa bằng JSON).

## 3. Môi trường & công nghệ

### 3.1 Ứng dụng

| Thành phần | Lựa chọn |
|---|---|
| Ngôn ngữ / runtime | C# / .NET 8 (`net8.0-windows` cho App, `net8.0` cho Core) |
| Giao diện | **WPF**, mô hình **MVVM** |
| MVVM | `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`) |
| Giao diện Windows 11 | `WPF-UI` (lepoco/wpfui): Fluent, Mica, sáng/tối theo hệ thống |
| Host / DI / config | `Microsoft.Extensions.Hosting` (DI, `IOptions`, config JSON) |
| Log | `Serilog` (+ `Sinks.File`, và sink riêng để đẩy log lên UI) |
| Excel | `ClosedXML` |
| Tự động hóa | `Appium.WebDriver` 5.x, `Selenium.Support`; Appium 2.x trở lên (máy dev: 4.0.0) + driver `uiautomator2` (9.1.0) |
| Test | xUnit (Core: reader, categorizer, state; ViewModel nếu cần) |
| Đóng gói | `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`. Sau v1: **Velopack** (installer + tự cập nhật) |
| IDE | Visual Studio 2022 (workload ".NET desktop development"), hoặc VS Code + C# Dev Kit |

### 3.2 Phụ thuộc bên ngoài trên máy Windows

App **không** đóng gói sẵn các thành phần này. Màn hình Kiểm tra môi trường (mục 10.2) phát hiện thiếu gì và hướng dẫn cài:

| Thành phần | Ghi chú |
|---|---|
| Node.js LTS + Appium (2.x trở lên) | `npm i -g appium` rồi `appium driver install uiautomator2` |
| Android Platform-Tools (`adb`) | Lấy theo đường dẫn trong config, rồi `ANDROID_HOME\platform-tools`, rồi `PATH` |
| JDK 17 trở lên | `JAVA_HOME` |
| Biến môi trường | `ANDROID_HOME`, `JAVA_HOME`. Khi tự khởi động Appium, app truyền các biến này vào process con |

**Không cần** project Android hoặc Android Studio. Lần chạy đầu, Appium tự cài app hỗ trợ (Appium Settings, UiAutomator2 Server) lên điện thoại.

### 3.3 Ghi chú thiết bị
- Money Lover hiện để **giao diện tiếng Anh** ("Save", "Select category", "Write note"...). Selector theo text, tên nhóm và tên ví trong config đều theo ngôn ngữ này. **Đổi ngôn ngữ app thì phải đổi config theo.**
- Xiaomi/Redmi/POCO: phải bật thêm **"USB debugging (Security settings)"**, nếu không thì click/gõ sẽ không có tác dụng.
- Khi chạy: mở khóa màn hình, tắt tự khóa hoặc bật "Stay awake".
- Wi-Fi debugging (Android 11+): `adb pair IP:pairPort` (một lần), sau đó `adb connect IP:port`. Cổng thay đổi mỗi lần bật lại.

## 4. Kiến trúc

```
┌──────────────────────────── Windows 11 PC ─────────────────────────────┐
│ MoneyLoverAutoInput.App (WPF)                                          │
│  Views (XAML) ⇄ ViewModels (CommunityToolkit.Mvvm)                     │
│        │ gọi qua interface, DI                                         │
│ MoneyLoverAutoInput.Core (class library, không phụ thuộc WPF)          │
│  ├─ BankStatementReader → StatementRow                                 │
│  ├─ Categorizer         → TransactionRow                               │
│  ├─ RowValidator                                                       │
│  ├─ StateStore          → state.json                                   │     ┌──── Android ────┐
│  ├─ ImportRunner        → vòng lặp nhập, IProgress, CancellationToken  │     │                 │
│  ├─ Environment/        → AdbClient, AppiumServerManager, EnvChecker   │     │                 │
│  └─ Automation/         → DriverFactory, UiHelper, Pages ──────────────┼──►  │ UiAutomator2    │
│                                    HTTP :4723                          │ ADB │ ──► Money Lover │
│ Appium Server (process con do app khởi động, hoặc chạy sẵn bên ngoài)  │     │                 │
└────────────────────────────────────────────────────────────────────────┘     └─────────────────┘
```

**Nguyên tắc:**
- **Core không tham chiếu WPF.** Toàn bộ logic nằm ở Core. App chỉ có View, ViewModel và phần nối DI.
- **Không chặn UI thread khi nhập.** `ImportRunner.RunAsync(rows, IProgress<ImportProgress>, CancellationToken)` chạy trên background thread (`Task.Run`). Tiến trình được báo về qua `IProgress<T>` (tự marshal về UI thread). Các lệnh Appium là đồng bộ, nên luôn bọc chúng trong `Task.Run`.
- **Dừng an toàn.** Nút Dừng hủy `CancellationToken`. Runner chỉ kiểm tra token **giữa các bước UI và giữa các dòng**, không bao giờ cắt ngang lúc đang bấm Lưu. Dòng đang nhập dở bị hủy thì quay về màn hình chính và giữ nguyên trạng thái chưa nhập.
- Luồng dữ liệu: `xlsx → StatementRow → Categorizer → TransactionRow (+ chỉnh tay của người dùng) → ImportRunner → UI Android`.

## 5. Cấu trúc project

```
MoneyLoverAutoInput/
├─ SPEC.md
├─ CLAUDE.md
├─ .gitignore
├─ MoneyLoverAutoInput.slnx              # định dạng solution mới (SDK 10)
├─ src/
│  ├─ MoneyLoverAutoInput.Core/            # net8.0, class library
│  │  ├─ Models/
│  │  │  ├─ StatementRow.cs                # 1 dòng sao kê thô
│  │  │  ├─ StatementInfo.cs               # metadata: số TK, khoảng thời gian
│  │  │  ├─ TransactionRow.cs              # giao dịch sẽ nhập vào app
│  │  │  ├─ ImportProgress.cs
│  │  │  └─ RunResult.cs
│  │  ├─ Configuration/
│  │  │  └─ AppSettings.cs                 # các class Options map với appsettings.json
│  │  ├─ Input/
│  │  │  ├─ IStatementReader.cs
│  │  │  ├─ BankStatementReader.cs
│  │  │  └─ RowValidator.cs
│  │  ├─ Categorization/
│  │  │  ├─ CategoryRule.cs
│  │  │  ├─ Categorizer.cs
│  │  │  └─ TextNormalizer.cs              # bỏ dấu, lowercase
│  │  ├─ State/
│  │  │  └─ StateStore.cs
│  │  ├─ Environment/
│  │  │  ├─ AdbClient.cs                   # devices, connect, pair, kiểm tra package
│  │  │  ├─ AppiumServerManager.cs         # tìm/khởi động/dừng appium, chờ /status
│  │  │  └─ EnvironmentChecker.cs          # tổng hợp kết quả cho màn hình kiểm tra
│  │  ├─ Privacy/
│  │  │  └─ Masking.cs                     # che số tài khoản
│  │  ├─ Import/
│  │  │  ├─ StatementLoader.cs             # đọc → phân loại → validate → sắp xếp
│  │  │  ├─ RowStatus.cs                   # trạng thái dòng trên Xem trước (Mới/Đã nhập/Lỗi/...)
│  │  │  └─ ImportRunner.cs                # vòng lặp mục 8, retry, phân loại lỗi
│  │  └─ Automation/
│  │     ├─ DriverFactory.cs
│  │     ├─ UiHelper.cs
│  │     ├─ Selector.cs
│  │     └─ Pages/
│  │        ├─ HomePage.cs
│  │        ├─ AddTransactionPage.cs
│  │        ├─ AmountKeypad.cs
│  │        ├─ CategoryPicker.cs
│  │        ├─ WalletPicker.cs
│  │        └─ DatePicker.cs
│  └─ MoneyLoverAutoInput.App/             # net8.0-windows, WPF
│     ├─ App.xaml(.cs)                     # Generic Host + DI, Serilog, xử lý exception toàn cục
│     ├─ appsettings.default.json          # cấu hình mặc định (EmbeddedResource), copy ra thư mục dữ liệu ở lần chạy đầu
│     ├─ Views/
│     │  ├─ MainWindow.xaml                # FluentWindow + NavigationView
│     │  ├─ EnvironmentPage.xaml
│     │  ├─ PreviewPage.xaml
│     │  ├─ RunPage.xaml
│     │  ├─ SetupView.xaml                 # Thiết lập: ví, nhóm, luật (10.5)
│     │  ├─ HistoryPage.xaml
│     │  └─ SettingsPage.xaml
│     ├─ ViewModels/                       # 1 ViewModel cho mỗi Page
│     ├─ Services/                         # DialogService, FilePickerService, UiLogSink
│     └─ Converters/
├─ tests/
│  └─ MoneyLoverAutoInput.Tests/           # xUnit, chỉ tham chiếu Core
└─ samples/
   └─ sao_ke_mau.xlsx                      # file mẫu ĐÃ ẨN DANH (số TK, SĐT, tên giả)
```

### 5.1 Thư mục dữ liệu người dùng

App phát hành dạng single-file, nên **không** ghi gì vào thư mục cài đặt. Mọi dữ liệu nằm ở `%LOCALAPPDATA%\MoneyLoverAutoInput\`:

```
%LOCALAPPDATA%\MoneyLoverAutoInput\
├─ appsettings.json      # cấu hình người dùng (tạo từ bản mặc định ở lần chạy đầu)
├─ state.json
├─ logs\run-yyyyMMdd.log
└─ screenshots\
```

Cài đặt có nút "Mở thư mục dữ liệu".

**Config:** lần chạy đầu, bản mặc định (`appsettings.default.json`, nhúng trong exe) được copy ra thư mục dữ liệu. Từ đó app **chỉ đọc file người dùng**, không merge với bản mặc định. Lý do là configuration binder gộp mảng theo index, nên merge sẽ làm các luật người dùng đã xóa "sống lại". Key bị thiếu (ví dụ key mới ở phiên bản sau) lấy giá trị mặc định khai báo trong class `AppSettings`.

**Mở file từ dòng lệnh:** `MoneyLoverAutoInput.exe "<sao_ke>.xlsx"` mở thẳng file đó ở trang Xem trước. Kéo file thả lên exe hoặc dùng "Open with" cũng chạy theo cách này.

> File sao kê thật (ví dụ `Account Enquiry-*.xlsx`) chứa số tài khoản và thông tin cá nhân, **không** đưa vào `samples/`, test hay git.

## 6. Dữ liệu đầu vào — file sao kê ngân hàng

### 6.1 Bố cục file (theo file mẫu `Account Enquiry-23_10_2026-16_11_2026-<số TK>.xlsx`)

Một sheet (tên `VN`). Phần đầu là metadata, sau đó là bảng giao dịch:

| Ô | Nội dung | Ví dụ |
|---|---|---|
| A3 (merge A3:I3) | Tiêu đề | `LỊCH SỬ GIAO DỊCH TÀI KHOẢN` |
| A5 / B5 | `Thời gian:` / khoảng ngày | `23/10/2026 - 16/11/2026` |
| A6 / B6 | `Tài khoản giao dịch:` / số tài khoản | `0353xxxxxxx` |
| Dòng 9 | Tiêu đề cột | xem 6.2 |
| Dòng 10 trở đi | Giao dịch, **mới nhất ở trên** | |

**Không hard-code số dòng.** Reader tìm dòng tiêu đề bằng cách quét 30 dòng đầu để tìm dòng có đủ các tên cột bắt buộc. Metadata được tìm theo nhãn ở cột A (`Thời gian:`, `Tài khoản giao dịch:`). Bảng kết thúc ở dòng đầu tiên có cả `Ngày thực hiện` lẫn `Mã giao dịch` đều trống. Sheet có thể khai báo vùng dữ liệu rộng hơn thực tế (mẫu báo `A3:I80` nhưng chỉ có 2 dòng dữ liệu).

### 6.2 Các cột

| Cột Excel | Bắt buộc | Kiểu trong file | Ví dụ | Dùng cho |
|---|---|---|---|---|
| `Ngày thực hiện` | ✔ | **text** `dd-MM-yyyy HH:mm:ss` | `23-10-2026 10:08:08` | `Ngay` (chỉ lấy phần ngày) + sắp xếp |
| `Ngày hiệu lực` | ✖ | text `dd/MM/yyyy` | `23/10/2026` | Dự phòng khi `Ngày thực hiện` lỗi |
| `Mô tả giao dịch` | ✔ | text | `TT QR - HY00000000000001` | Phân loại + `GhiChu` |
| `Ghi nợ` | ✔* | số nguyên, hoặc `''` | `85000` | Tiền ra → `Loai = Chi` |
| `Ghi có` | ✔* | số nguyên, hoặc `''` | | Tiền vào → `Loai = Thu` |
| `Số dư` | ✖ | số nguyên | `1504000` | Không dùng (không hiển thị, không log) |
| `Tài khoản đối ứng` | ✖ | text, thường `''` | | Phân loại |
| `Tên tài khoản` | ✖ | text, thường `''` | `CTY CP DICH VU DI DONG TRUC TUYEN` | Phân loại + `GhiChu` |
| `Mã giao dịch` | ✔ | text | `999CTMB000000005` | **Khóa chống trùng** (`Id`) |

\* Mỗi dòng phải có **đúng một** trong hai cột `Ghi nợ` / `Ghi có` lớn hơn 0.

**Lưu ý khi parse:**
- Ô trống có thể là chuỗi rỗng `''` chứ không phải null, nên phải coi `''` và khoảng trắng là trống.
- Ngày là **chuỗi text**, không phải kiểu Date của Excel. Parse bằng `ParseExact` với `InvariantCulture`. Vẫn chấp nhận ô kiểu Date nếu ngân hàng đổi định dạng.
- Số tiền là số (format `#,###`). Nếu gặp dạng text thì làm sạch `.`, `,`, khoảng trắng rồi parse thành `long` (VND, không có phần lẻ).
- Mô tả có thể chứa số điện thoại hoặc mã tham chiếu, đều là thông tin cá nhân (xem 11).

### 6.3 Chuyển đổi `StatementRow` → `TransactionRow`

| Trường | Quy tắc |
|---|---|
| `Id` | `Mã giao dịch` (trim) |
| `Ngay` | Phần ngày của `Ngày thực hiện`, nếu lỗi thì lấy `Ngày hiệu lực` |
| `Loai` | `Ghi nợ > 0` → `Chi`; `Ghi có > 0` → `Thu` |
| `SoTien` | Giá trị của cột tương ứng ở trên |
| `Nhom` | Theo luật phân loại (7.2), không khớp luật nào thì dùng nhóm mặc định theo `Loai`. **Người dùng sửa được trên màn hình Xem trước** |
| `Vi` | `Wallets[<số TK>]` nếu có, ngược lại `Defaults.Wallet` (cả file dùng chung 1 ví) |
| `GhiChu` | Theo `NoteTemplate`, mặc định `{description}`. Sửa được trên màn hình Xem trước |
| `MatchedRule` | Tên/keyword của luật đã khớp (để hiển thị) |

Thứ tự nhập: **tăng dần theo `Ngày thực hiện`** (cũ trước, mới sau), ngược với thứ tự trong file.

### 6.4 Validation

Chạy ngay sau khi đọc file, trước khi đụng tới điện thoại.

**`Mã giao dịch` trùng trong file chỉ là CẢNH BÁO**, không chặn nhập:
- Dòng vẫn được chọn sẵn và nhập bình thường. Cột Trạng thái hiện "⚠ Mã GD trùng", banner phía trên báo số dòng, và bộ lọc "Có lỗi / cần xem" bắt được các dòng này.
- Để `state.json` và `drafts.json` không lẫn giữa các dòng, mỗi lần xuất hiện sau lần đầu (theo thứ tự dòng trong file) nhận **khóa riêng** `MÃ#2`, `MÃ#3`... (`TransactionRow.Id`). Mã gốc nằm ở `TransactionRow.TxnCode`.
- **Rủi ro:** nếu đó thật là cùng một giao dịch bị lặp trong sao kê thì sẽ nhập 2 lần. Người dùng tự bỏ chọn hoặc xóa dòng.

Lỗi (chặn nhập) gồm: thiếu cột bắt buộc (lỗi cả file), `Mã giao dịch` trống, ngày không parse được, cả Ghi nợ lẫn Ghi có đều trống/0 hoặc đều > 0, số tiền không parse được, không xác định được nhóm (không khớp luật và không có nhóm mặc định).

Dòng lỗi hiển thị màu đỏ trên màn hình Xem trước, kèm số dòng Excel và lý do, và **không được chọn để nhập**. Nút "Bắt đầu nhập" chỉ tính các dòng hợp lệ. Lỗi "Chưa phân loại" sẽ hết khi người dùng chọn nhóm cho dòng đó.

### 6.5 Sao kê thẻ tín dụng ("Lịch sử giao dịch thẻ tín dụng")

`StatementReader` tự nhận dạng loại file: có dòng tiêu đề đủ `Ngày giao dịch`, `Mô tả giao dịch`, `Ghi nợ`, `Ghi có` thì là sao kê thẻ (`CreditCardStatementReader`), còn lại là sao kê tài khoản (6.1–6.4).

- Metadata: `Thời gian liệt kê giao dịch` và `Số thẻ:` (ngân hàng đã che sẵn dạng `411111xxxxxx9999`). Nhãn ở một ô, giá trị là ô có dữ liệu đầu tiên bên phải.
- Cột: `Ngày giao dịch` | `Ngày cập nhật` | `Số tiền giao dịch` | `Ghi nợ` | `Ghi có` | `Mô tả giao dịch`. Không có giờ, Mã giao dịch, Tài khoản đối ứng hay Số dư. Các dòng **không** theo thứ tự ngày.
- Ngày lấy theo `Ngày giao dịch` (trống thì `Ngày cập nhật`). `Ghi nợ` > 0 là Chi (tiêu bằng thẻ), `Ghi có` > 0 là Thu (trả nợ thẻ, hoàn phí). `Số tiền giao dịch` chỉ để tham khảo.
- **Khóa chống trùng tự tạo:** `TTD-yyyyMMdd-N|C<số tiền>-<8 hex SHA-256 của mô tả đã chuẩn hóa>`. Ổn định giữa các kỳ sao kê chồng lấn; không chứa nguyên văn mô tả. Hai dòng giống hệt nhau trong file → cảnh báo, dòng sau nhận `#2` (như 6.4).
- **Ví:** `Wallets[số thẻ]` nếu có, rồi `Defaults.CreditCardWallet` (màn Thiết lập: "Ví cho sao kê thẻ tín dụng"), rồi `Defaults.Wallet`.

### 6.6 Bảng giao dịch BIDV

`StatementReader` nhận dạng khi dòng tiêu đề có đủ `Ngày giao dịch`, `Nội dung giao dịch`, `Số tiền`, `Mã giao dịch` (`BidvStatementReader`). Thứ tự thử: thẻ tín dụng (6.5) → BIDV → sao kê tài khoản.

- Tiêu đề thường ở dòng 1, **không có metadata** (không có số tài khoản, không có kỳ). Mới nhất ở trên.
- Cột: `Ngày giao dịch` | `Nội dung giao dịch` | `Số tiền` | `Số dư` | `Tiền tệ` | `Mã giao dịch`. **`Số dư` không bao giờ được đọc.**
- `Số tiền` **có dấu**: âm = Chi, dương = Thu, bằng 0 = lỗi. Chấp nhận ô số hoặc text (`-571,276`); số lẻ thập phân là lỗi.
- `Tiền tệ` khác `VND` (nếu có) là lỗi dòng.
- `Mã giao dịch` có thể **trùng** giữa các dòng khác số tiền (giao dịch thẻ). Xử lý như 6.4: cảnh báo, dòng sau nhận `MÃ#2`.
- **Ví:** `Defaults.BidvWallet` (màn Thiết lập: "Ví cho bảng giao dịch BIDV"), trống thì `Defaults.Wallet`.

## 7. Cấu hình — `appsettings.json`

### 7.1 Tổng thể

```jsonc
{
  "SetupCompleted": false,              // false = lần đầu mở app vào trang Thiết lập (10.5)
  "Tools": {
    "AdbPath": "",                      // trống = ANDROID_HOME\platform-tools\adb.exe, rồi PATH
    "AppiumCommand": "appium",          // lệnh hoặc đường dẫn appium.cmd
    "AutoStartAppium": true             // app tự chạy Appium nếu ServerUrl chưa phản hồi
  },
  "Appium": {
    "ServerUrl": "http://127.0.0.1:4723",
    "Udid": "",                         // trống = thiết bị duy nhất; Wi-Fi: "192.168.1.20:41xxx"
    "AppPackage": "com.bookmark.money", // xác minh: adb shell dumpsys window | findstr mCurrentFocus
    "NewCommandTimeoutSec": 300
  },
  "Timing": {
    "DefaultWaitSec": 10,
    "DelayBetweenRowsMs": 800,
    "RetryPerRow": 2
  },
  "Defaults": {
    "Wallet": "TP Bank",                // ví trong Money Lover tương ứng với tài khoản sao kê (khớp chính xác)
    "CreditCardWallet": "",             // ví cho sao kê thẻ tín dụng (SPEC 6.5); trống = dùng Wallet
    "BidvWallet": "",                   // ví cho bảng giao dịch BIDV (SPEC 6.6); trống = dùng Wallet
    "ManualWallet": "Cash",             // ví chọn sẵn trên màn Nhập tay (SPEC 10.9); trống = dùng Wallet
    "ExpenseCategory": "Other Expense", // trống = dòng không khớp luật là lỗi "Chưa phân loại"
    "IncomeCategory": "Other Income",
    "NoteTemplate": "{description}"     // placeholder: {description}, {counterpartName}, {txnCode}
  },
  "Wallets": {
    // số tài khoản → tên ví trong app (tùy chọn, ưu tiên hơn Defaults.Wallet)
    // "0999xxxxxxx": "TP Bank"
  },
  "KnownCategories": {
    // cây nhóm như trong app: nhóm cha + Children (Money Lover chỉ có 2 cấp)
    "Chi": [
      { "Name": "Food & Beverage", "Children": ["Café", "Restaurants"] },
      { "Name": "Health & Fitness", "Children": ["Fitness"] },
      { "Name": "Fitness" },
      { "Name": "Other Expense" }
    ],
    "Thu": [ { "Name": "Salary" }, { "Name": "Incoming transfer" }, { "Name": "Other Income" } ]
  },
  "CategoryRules": [ /* xem 7.2 */ ],
  "Selectors": {
    // text lấy từ ảnh chụp app (giao diện tiếng Anh) — tạm thời; TODO cần resource-id từ uiautomator dump
    "AddButton":          { "by": "id",   "value": "TODO" },            // nút "+" giữa thanh dưới
    "TypeTabExpense":     { "by": "text", "value": "Expense" },         // tab đầu màn Add transaction
    "TypeTabIncome":      { "by": "text", "value": "Income" },
    "WalletField":        { "by": "id",   "value": "TODO" },            // đọc text để biết ví đang chọn
    "WalletItem":         { "by": "text", "value": "{name}" },          // khớp CHÍNH XÁC
    "AmountField":        { "by": "id",   "value": "TODO" },
    "KeypadDigit":        { "by": "id",   "value": "TODO" },            // {d} = chữ số
    "KeypadTripleZero":   { "by": "text", "value": "000" },
    "KeypadClear":        { "by": "text", "value": "C" },
    "KeypadNext":         { "by": "id",   "value": "TODO" },            // nút ">" xanh dương
    "CategoryField":      { "by": "text", "value": "Select category" },
    "CategoryItem":       { "by": "text", "value": "{name}" },
    "NoteField":          { "by": "text", "value": "Write note" },
    "DateField":          { "by": "id",   "value": "TODO" },            // chưa thấy trên màn hình
    "SaveButton":         { "by": "text", "value": "Save" }
  }
}
```

- **Định danh nhóm bằng đường dẫn:** nhóm cha là `"Tên"`, nhóm con là `"Cha > Con"` (`CategoryTree`). Dạng này được dùng ở mọi nơi: `Defaults.*Category`, `CategoryRules[].category`, cột Nhóm trên Xem trước và `state.json`.
  - **Lý do:** Money Lover cho phép một nhóm con trùng tên với nhóm cha khác. App của người dùng có "Fitness", "Home Maintenance", "Makeup", "Streaming Service" vừa là nhóm cha vừa là nhóm con. Riêng "Beauty > Cắt Tóc" có **hai** nhóm con trùng hẳn tên, nên được báo cảnh báo trùng; người dùng nên đổi tên một nhóm trong app.
  - **Tương thích ngược:** config cũ dạng danh sách phẳng (`"Chi": ["A", "B"]`) vẫn bind được, mỗi phần tử thành một nhóm cha.
  - **Khi tự động chọn nhóm (8.2 bước 6):** tách đường dẫn ra. Nhóm con thì cuộn tới nhóm cha trước, rồi chọn phần tử có tên khớp **ngay sau** nhóm cha đó, để không bấm nhầm nhóm cha cùng tên.
- File config có comment (`//`). `Microsoft.Extensions.Configuration.Json` đọc được comment. Khi app **ghi lại** config (lưu luật/cài đặt) thì ghi bằng `System.Text.Json` và comment sẽ mất. Chấp nhận được, vì bản mặc định có comment vẫn nằm trong app.
- Mọi `TODO` được điền sau khi soi UI (xem mục 12). Comment chỉ đặt **ngoài** chuỗi `value`.
- `Selectors` chỉ sửa bằng file JSON (nút "Mở file cấu hình" trong Cài đặt). Sửa xong bấm "Tải lại cấu hình", không cần khởi động lại app.

### 7.2 Luật phân loại — `CategoryRules`

```jsonc
"CategoryRules": [
  { "match": ["TIEN NUOC", "CAP NUOC"],        "type": "Chi", "category": "Water Bill" },
  { "match": ["GRAB", "BE GROUP", "XANH SM"], "type": "Chi", "category": "Transportation" },
  { "match": ["SHOPEE", "LAZADA"],            "type": "Chi", "category": "Shopping" },
  { "match": ["LUONG", "SALARY"],             "type": "Thu", "category": "Salary" },
  { "match": ["CK DEN", "NHAN TIEN"],         "type": "Thu", "category": "Incoming transfer" },
  { "match": ["CHUYEN VI NOI BO"],            "action": "Skip" }   // ví dụ dùng Skip
]
```

> "TT QR" và "NAP TIEN VI MOMO" chỉ là **kênh thanh toán**, không cho biết mua gì. Cùng một kênh có thể là bất kỳ nhóm nào: hóa đơn, giải trí, ăn uống... Vì vậy không nên có luật cố định cho hai từ khóa này; để chúng rơi về nhóm mặc định rồi sửa trên Xem trước.

- **Vùng so khớp:** `Mô tả giao dịch` + `Tên tài khoản` + `Tài khoản đối ứng`, đã chuẩn hóa (bỏ dấu tiếng Việt, chữ thường, gộp khoảng trắng). Keyword cũng được chuẩn hóa như vậy.
- **Cách khớp:** "chứa chuỗi" (contains), khớp bất kỳ keyword nào trong `match` là khớp. Có thể thêm `"regex": "..."` cho trường hợp đặc biệt.
- `type`: `Chi` | `Thu` | bỏ trống = cả hai. Luật chỉ áp khi `Loai` khớp.
- **Luật đầu tiên khớp sẽ thắng.** Trên màn hình Luật có thể kéo thả hoặc dùng nút lên/xuống để đổi thứ tự.
- `action`: `Import` (mặc định) hoặc `Skip`. Dòng `Skip` mặc định bị bỏ chọn trên màn hình Xem trước. Người dùng có thể chọn lại để nhập.
- Không khớp luật nào thì dùng `Defaults.ExpenseCategory` / `IncomeCategory`. Nếu nhóm mặc định trống thì dòng đó lỗi "Chưa phân loại".
- Tên `category` phải **trùng tên nhóm hiển thị trong app**.
- **Tạo luật nhanh:** trên màn hình Xem trước, khi đổi nhóm của một dòng, app hỏi "Tạo luật cho các giao dịch tương tự?" và điền sẵn keyword từ mô tả (bỏ các đoạn số/mã).

## 8. Luồng xử lý

### 8.1 Tổng thể
1. **Mở app:** nạp config, chạy Kiểm tra môi trường ở chế độ nền (không chặn UI).
2. **Chọn file sao kê:** Core đọc, phân loại và validate, rồi đối chiếu với `state.json`. Kết quả hiện lên màn hình Xem trước (mục 10.3).
3. **Người dùng rà soát:** sửa nhóm hoặc ghi chú, chọn hay bỏ chọn dòng, rồi bấm "Bắt đầu nhập". Trước khi chạy, app hiện hộp xác nhận tóm tắt: số dòng, tổng Chi, tổng Thu, ví.
4. **Chuẩn bị:** bảo đảm Appium chạy được (tự khởi động nếu `AutoStartAppium`), có thiết bị, tạo session Appium (`noReset = true`) và `ActivateApp(AppPackage)`. Thất bại thì báo lỗi kèm hướng dẫn, không nhập gì.
5. **Nhập:** với mỗi dòng được chọn, chạy **8.2**. Thành công thì ghi `Done` vào state. Lỗi thì retry theo `RetryPerRow`, vẫn lỗi thì ghi `Failed` kèm lý do và ảnh chụp màn hình, rồi quay về màn hình chính và chạy dòng tiếp theo. Mỗi bước được báo về UI qua `IProgress`.
6. **Kết thúc / Dừng:** hiện tổng kết (Done / Failed / Skipped / Đã có từ trước) và đóng session. Nếu app đã tự khởi động Appium thì để Appium chạy tiếp cho lần sau, chỉ tắt khi thoát app.

**Chế độ chạy thử (thay cho `--dry-run`):** nút "Chạy thử" làm bước 4 và đi qua 8.2 cho **1 dòng đầu** nhưng **không bấm Lưu**. Thay vào đó app bấm Back hoặc hủy, để kiểm tra selector mà không tạo giao dịch.

### 8.2 Nhập một giao dịch

Luồng dưới đây dựa trên **uiautomator dump thật** (Money Lover 8.93.0, giao diện tiếng Anh, Samsung Android 10, màn 1080×2094). Selector ở `appsettings.default.json` (mục 12).

| Activity | Vai trò |
|---|---|
| `com.zoostudio.moneylover.main.MainActivity` | Màn chính. Có nút "+" `btAddTransaction` |
| `...ui.editTransaction.ActivityEditTransaction` | Add transaction |
| `...ui.categoryPicker.CategoryPickerActivity` | Chọn nhóm |
| `...ui.switcherWallet.ActivityWalletSwitcher` | Chọn ví |
| `...hashtagTransaction.activities.ActivityAddNote` | Ghi chú |

1. **Về màn chính:** nếu `AddButton` không hiện thì bấm `Back`, tối đa 3 lần, rồi `ActivateApp`.
2. Bấm `AddButton`.
3. **Loại:** bấm `TypeTabExpense` / `TypeTabIncome`. Phải làm **trước** khi chọn nhóm, vì danh sách nhóm phụ thuộc loại.
4. **Ví:** đọc `WalletText`. Chỉ khi khác `Vi` thì bấm `WalletField`, rồi bấm `WalletItem` (tên khớp **chính xác**; có "TP Bank" và "TPBank"). **Không** bấm nút menu ⋮ (`@menu`) trên dòng ví.
5. **Số tiền:** màn mở sẵn **bàn phím số của app** với id cố định:
   - Chữ số 1–9 là `KeypadDigit` (`button{d}`). Số **0 là `KeypadZero` (`button14`)**, khác quy luật. `000` là `button21`.
   - Bấm `KeypadClear` trước, rồi gõ từng chữ số (gặp cụm "000" thì dùng `KeypadTripleZero`), rồi bấm `KeypadNext` (`>`) để đóng bàn phím.
   - Đọc lại `AmountField`, bỏ mọi ký tự không phải số, rồi so với số tiền cần nhập. Lệch thì làm lại bước này.
6. **Nhóm:** bấm `CategoryField`, mở `CategoryPickerActivity`.
   - Bấm tab `CategoryTabExpense` / `CategoryTabIncome` cho chắc.
   - Danh sách là `CategoryList`. **Nhóm cha có hơn 2 nhóm con bị thu gọn sau dòng "SHOW MORE…"** (`CategoryShowMore`).
   - **Nhóm cha:** cuộn tới `CategoryItem` có tên đúng bằng tên cha rồi bấm.
   - **Nhóm con ("Cha > Con"):** cuộn tới dòng **nhóm cha**. Trong các dòng ngay sau nó (dòng con có `@child_indicator`), tìm tên con. Không thấy thì bấm "SHOW MORE…" thuộc nhóm cha đó rồi tìm lại. Không tìm theo tên con trên toàn danh sách, vì có tên trùng giữa nhóm cha và nhóm con (VD "Fitness").
   - Sau khi chọn, đọc `CategoryText` để xác nhận.
7. **Ngày:** đọc `DateText` (dạng `"Sunday, 15/11/2026"`, lấy phần `dd/MM/yyyy`). Bấm `DatePrev` / `DateNext` (lùi/tiến 1 ngày) cho tới khi khớp ngày cần nhập. Mỗi bước đọc lại để xác nhận; giới hạn số lần bấm, VD 400.
8. **Ghi chú** (nếu có): bấm `NoteField`, mở `ActivityAddNote`. Gõ vào `NoteEdit`, rồi bấm `NoteSave` để quay về.
9. **Lưu:** `SaveButton` bị khóa (`enabled=false`) cho tới khi đủ dữ liệu, nên dùng nó để **kiểm tra lần cuối**. Bấm, rồi chờ `AddButton` hiện lại (đã về màn chính).

Giữa các bước 2 đến 8 runner kiểm tra `CancellationToken`. Từ bước 9 trở đi thì không hủy nữa. Nếu bị hủy giữa chừng thì bấm Back để thoát màn Add transaction; nếu app hỏi có bỏ giao dịch đang nhập không thì chọn bỏ.

### 8.3 Chọn ngày
Không cần DatePicker: màn Add transaction có sẵn nút lùi/tiến 1 ngày (bước 7). DatePicker (`llSelectDate`) chỉ cần nếu sau này phải nhập giao dịch cách xa nhiều tháng.

## 9. State & chống trùng — `state.json`

Có **một** file state dùng chung cho mọi file sao kê, nằm trong thư mục dữ liệu (5.1). Khóa là `Mã giao dịch`, nên khi hai kỳ sao kê chồng ngày nhau thì các giao dịch đã nhập vẫn không bị nhập lại.

```json
{
  "version": 1,
  "rows": {
    "999CTMB000000005": { "status": "Done",    "at": "2026-11-15T16:30:12+07:00", "sourceFile": "Account Enquiry-01_11_2026-15_11_2026-***1234.xlsx", "amount": 85000, "category": "Ăn uống" },
    "999TTMB000000002": { "status": "Skipped", "at": "...", "reason": "Rule: NAP TIEN VI MOMO" },
    "999CTMB000000003": { "status": "Failed",  "at": "...", "error": "CategoryItem 'Cafe' not found", "screenshot": "screenshots/999CTMB000000003_163045.png" }
  }
}
```
- Ghi sau **mỗi** dòng (ghi ra file tạm rồi rename để tránh hỏng file) để bị ngắt giữa chừng vẫn resume được.
- Trên màn hình Xem trước:
  - Dòng `Done` hiện trạng thái "Đã nhập", bị khóa và không chọn được.
  - Dòng `Failed` được chọn sẵn để nhập lại.
  - Dòng `Skipped` hiện "Bỏ qua" và bị bỏ chọn; người dùng có thể chọn lại.
- Chỉ ghi `Skipped` vào state khi dòng bị bỏ qua lúc **chạy**, theo luật `Skip`. Việc người dùng bỏ chọn tay thì không lưu.
- Màn hình Lịch sử xem được các dòng trong state. Có nút "Xóa trạng thái" cho một dòng hoặc cho toàn bộ (kèm hộp xác nhận), ví dụ khi đã xóa giao dịch trong app và muốn nhập lại.

> Rủi ro trùng: nếu app đã lưu nhưng tool chưa kịp ghi state (mất kết nối đúng lúc bấm Lưu) thì lần chạy sau có thể nhập trùng 1 dòng. Dòng đó được đánh `Failed` với lý do "Không xác nhận được đã lưu, kiểm tra tay trước khi nhập lại" và **không** được chọn sẵn.

## 10. Giao diện (WPF)

Cửa sổ chính là `FluentWindow` (WPF-UI) có Mica, theme sáng/tối theo Windows, và `NavigationView` bên trái gồm các trang sau. Toàn bộ chữ trên giao diện là tiếng Việt.

### 10.1 Thanh trạng thái (luôn hiện)
Hiện trạng thái thiết bị (●), Appium (●), và file sao kê đang mở. Bấm vào thì chuyển sang trang Môi trường.

### 10.2 Môi trường
- **Danh sách kiểm tra** (✔ / ✖ / đang kiểm tra), mỗi mục có hướng dẫn hoặc nút sửa: `adb` tìm thấy, `JAVA_HOME`, `ANDROID_HOME`, Node.js, Appium và driver `uiautomator2`, Appium server phản hồi `/status`, có đúng 1 thiết bị (hoặc trùng `Udid`), thiết bị đã mở khóa, Money Lover đã cài (`pm list packages`).
- **Nút:** "Kiểm tra lại", "Khởi động/Dừng Appium".
- **Kết nối Wi-Fi:** ô IP:port, nút "Pair" (nhập mã ghép đôi) và nút "Connect". Kết nối xong có tùy chọn lưu vào `Udid`.

### 10.3 Xem trước (trang chính)
- **Chọn file:** vùng kéo thả file `.xlsx` cùng nút "Chọn file". Sau khi đọc, hiện số tài khoản (đã che) và khoảng thời gian của sao kê.
- **`DataGrid` các cột, đều sửa được trước khi chạy:**
  - ☐ Chọn
  - Ngày (DatePicker, `dd/MM/yyyy`)
  - Loại (Chi/Thu)
  - Số tiền (gõ `45000`, `45.000` hay `45,000` đều được; Chi màu đỏ, Thu màu xanh)
  - Nhóm (chọn từ cây nhóm của loại tương ứng, cho gõ tự do)
  - Ví (chọn từ `KnownWallets`, cho gõ tự do)
  - Ghi chú
  - **Người nhận / gửi** (chỉ xem): cột "Tên tài khoản" của sao kê, là người nhận nếu Chi, người gửi nếu Thu, "—" nếu ngân hàng không ghi. Tooltip hiện "Chuyển đến / Nhận từ" kèm số tài khoản đối ứng **đã che** (4 số cuối). Ô tìm kiếm cũng tìm theo tên này.
  - Luật khớp (trong tooltip của ô Nhóm), Trạng thái (Mới / Đã nhập / Lỗi trước / Bỏ qua / Dữ liệu lỗi, kèm "· đã sửa"), nút hoàn tác dòng.
- **Đổi Loại:** nếu nhóm hiện tại không thuộc loại mới thì nhóm tự chuyển về nhóm mặc định của loại mới.
- **Giá trị gốc từ ngân hàng** (ngày, loại, số tiền, mô tả, mã GD, dòng Excel) được giữ trong `TransactionRow.Original*` và hiện ở tooltip của các ô để đối chiếu.
- **Dòng bị khóa:** dòng "Đã nhập" và dòng "Dữ liệu lỗi" không sửa được.
- **Lỗi động** (chặn nhập, hết lỗi khi sửa xong): số tiền ≤ 0, chưa có nhóm, chưa có ví.
- **Tự lưu nháp (`drafts.json`, `Core/Drafts/DraftStore`):**
  - Mỗi chỉnh sửa, kể cả chọn/bỏ chọn, được ghi atomic ngay theo Mã giao dịch. Đóng app hay app bị crash rồi mở lại cùng file (hoặc file sao kê khác chứa cùng giao dịch) vẫn thấy đúng như cũ.
  - Nháp chỉ lưu các trường đã đổi. Trường nào sửa về đúng giá trị gốc thì bị xóa khỏi nháp.
  - Khi luật thay đổi, app phân loại lại rồi áp nháp lên trên, nên **chỉnh sửa tay luôn thắng luật**.
  - "Hoàn tác" trên dòng xóa nháp của dòng đó. "Hoàn tác tất cả" xóa nháp của mọi dòng trong file, có hỏi xác nhận.
  - Khi một dòng nhập xong (`Done`), `ImportRunner` sẽ xóa nháp của dòng đó.
- **Xóa dòng** (loại khỏi danh sách nhập; dữ liệu sao kê không bị đụng tới):
  - **Một dòng:** nút 🗑 trên dòng, không hỏi xác nhận.
  - **Nhiều dòng:** bôi đen nhiều dòng trên lưới (Ctrl/Shift + click, chế độ `SelectionMode=Extended`), rồi bấm "Xóa dòng đang chọn" hoặc phím Delete. Có hỏi xác nhận khi xóa từ 2 dòng trở lên.
  - **Lưu trong nháp:** `RowDraft.Deleted = true`, vẫn còn khi mở lại app.
  - **Dòng đã xóa:** bị ẩn, không được nhập, không tính vào tổng. Chỉ hiện ở bộ lọc **"Đã xóa"**, kèm nút **khôi phục**. "Hoàn tác tất cả" cũng khôi phục các dòng đã xóa.
  - Dòng đã nhập vào Money Lover (`Done`) không xóa được.
- **Bộ lọc:** Tất cả / Mới / Có lỗi / Chưa phân loại / Đã sửa / Đã nhập / Đã xóa. Ô tìm kiếm theo mô tả, ghi chú, nhóm, mã GD.
- **Chân trang:** số giao dịch sẽ nhập, tổng Chi, tổng Thu (tính theo giá trị đã sửa), số dòng đã sửa. Các nút "Chạy thử", "Bắt đầu nhập".

### 10.4 Chạy
- **Tiến trình:** thanh tiến trình (n/N), dòng đang nhập và bước hiện tại (VD "Chọn nhóm: Ăn uống").
- **Nút:** "Dừng" (dừng sau bước an toàn, xem 4) và "Tiếp tục" (chạy tiếp các dòng còn lại).
- **Danh sách kết quả** theo dòng (✔ / ✖ / ⏭). Bấm vào dòng lỗi thì xem lý do, ảnh chụp màn hình, và nút "Mở page source".
- **Log trực tiếp** (sink Serilog → `ObservableCollection`, giới hạn 2000 dòng gần nhất).
- **Khi đang chạy:** khóa các trang Xem trước / Thiết lập / Cài đặt. Đóng cửa sổ lúc này thì app hỏi xác nhận rồi dừng an toàn.

### 10.5 Thiết lập (ví, nhóm, luật phân loại)

**Lần đầu mở app** (`SetupCompleted = false`), app vào thẳng trang này và hiện banner chào mừng. Bấm "Lưu và bắt đầu" thì app ghi `SetupCompleted = true` rồi chuyển sang Xem trước. Những lần sau trang vẫn nằm trên thanh điều hướng, nút lúc này là "Lưu thiết lập".

Trang gồm 4 phần:
1. **Ví:** `Defaults.Wallet`, tên ví trong Money Lover ứng với tài khoản sao kê. Bắt buộc.
2. **Nhóm:** hai ô nhập nhiều dòng cho cây `KnownCategories.Chi` / `Thu`. Mỗi dòng một nhóm; **nhóm con thụt đầu dòng hoặc bắt đầu bằng "-"** ngay dưới nhóm cha của nó. Dòng trống được bỏ qua. Kèm hai ô chọn nhóm mặc định `Defaults.ExpenseCategory` / `IncomeCategory`, cho gõ tự do.
3. **Luật phân loại:** bảng `CategoryRules` theo thứ tự ưu tiên, mỗi luật có các cột:
   - **#**: thứ tự ưu tiên.
   - **Từ khóa**: cách nhau bởi dấu phẩy.
   - **Loại**: Chi / Thu / Cả hai.
   - **Hành động**: Nhập / Bỏ qua.
   - **Nhóm**: chọn từ danh sách nhóm của loại tương ứng; bị khóa khi hành động là Bỏ qua.
   - **Nút trên từng dòng**: Lên / Xuống / Xóa.

   "Thêm luật" chèn luật mới lên đầu bảng. Trường `Regex` không sửa được trên UI nhưng vẫn được giữ khi lưu.
4. **Thử luật:** dán một mô tả và chọn Chi/Thu, app cho biết luật nào khớp và kết quả là nhóm gì, bị Bỏ qua, hay rơi về nhóm mặc định. App dùng thiết lập đang sửa, chưa cần lưu.

**Lưu** (`SettingsFile.Validate` rồi `SettingsFile.Save`):
- **Lỗi chặn lưu:** thiếu ví; luật không có từ khóa; luật "Nhập" chưa chọn nhóm; regex sai.
- **Cảnh báo, vẫn cho lưu:** danh sách nhóm trống; nhóm mặc định hoặc nhóm của luật không có trong danh sách nhóm.
- **Cách ghi:** app sửa trên bản sao của cấu hình hiện tại, nên các mục không có trên UI (Selectors, Appium...) được giữ nguyên. File được ghi atomic, comment trong file sẽ mất. Sau đó app nạp lại config ngay, trang Xem trước phân loại lại và giữ các chỉnh sửa tay.
- **Trước khi lưu:** nhãn "Chưa lưu" báo có thay đổi chưa ghi. "Hoàn tác" đọc lại từ file.

### 10.6 Lịch sử
Bảng các dòng trong `state.json`, có lọc theo trạng thái, nút xóa trạng thái (mục 9) và nút mở ảnh lỗi.

### 10.7 Cài đặt
Form cho `Tools`, `Appium`, `Timing`, `Wallets` (ánh xạ số tài khoản → ví), `Defaults.NoteTemplate`. Ví, nhóm và luật nằm ở trang Thiết lập (10.5). Có các nút "Mở file cấu hình", "Tải lại cấu hình", "Mở thư mục dữ liệu".

### 10.9 Nhập tay (giao dịch không có trong sao kê, VD tiền mặt)

Trang "Nhập tay" (`ManualEntryViewModel`, `Core/Manual`):
- **Form thêm nhanh:** Ngày (mặc định hôm nay), Loại, Số tiền, Nhóm (`SearchComboBox`), Ví (mặc định `Defaults.ManualWallet`, thường là "Cash"; trống thì `Defaults.Wallet`), Ghi chú. Enter ở ô Số tiền/Ghi chú = Thêm. Thêm xong giữ ngày/loại/nhóm/ví, xóa số tiền/ghi chú, con trỏ về ô Số tiền.
- Số tiền gõ được `45000`, `45.000`, `45,000`, `45k`, `1.5tr` (`AmountParser`). Không cho ngày tương lai, số tiền ≤ 0, nhóm không có trong danh sách, thiếu ví.
- **Lưu:** `manual.json` ở thư mục dữ liệu, ghi atomic sau mỗi thay đổi. Mỗi giao dịch có `Id` = `TAY-yyyyMMddHHmmss-<12 hex>`, không đổi; trạng thái đã nhập/lỗi nằm ở `state.json` theo Id, như sao kê.
- **Lưới:** sửa được mọi cột khi chưa nhập; dòng đã nhập bị khóa. Xóa từng dòng (dòng chưa nhập thì hỏi). "Dọn dòng đã nhập" bỏ các dòng đã nhập khỏi danh sách (không ảnh hưởng Money Lover/state).
- **Chạy thử / Bắt đầu nhập:** chuyển `TransactionRow` (`ManualEntry.ToTransactionRow`) sang trang Chạy, dùng chung `ImportRunner`; `SourceFile` = "Nhập tay". Chạy xong trang tự đọc lại trạng thái.

### 10.8 Yêu cầu chung
- **Không chặn UI thread:** mọi việc đọc file, gọi `adb`, gọi Appium đều chạy `async`.
- **Exception toàn cục:** bắt `DispatcherUnhandledException` và `TaskScheduler.UnobservedTaskException`, ghi log và hiện hộp thoại lỗi thay vì để app crash.
- **Chỉ một instance:** dùng Mutex, vì hai instance cùng ghi `state.json` có thể làm hỏng file.
- **Kích thước:** cửa sổ tối thiểu 1100×700, nhớ lại kích thước và vị trí cửa sổ.

## 11. Logging, lỗi & dữ liệu cá nhân

- Serilog ghi ra `logs/run-yyyyMMdd.log` (trong thư mục dữ liệu) và ra UI. Mỗi dòng log có `RowId`.
- **Che số tài khoản** khi hiển thị, ghi log và ghi state (chỉ giữ 4 số cuối: `***1234`). Không hiển thị, không log số dư.
- Khi lỗi: chụp `driver.GetScreenshot()` vào `screenshots/{RowId}_{HHmmss}.png`, đồng thời lưu `PageSource` (XML) cùng tên để debug selector.
- Phân loại lỗi:
  - **Môi trường** (thiếu adb/Appium/thiết bị): chặn ngay từ bước 8.1.4 và chỉ sang trang Môi trường.
  - **Kết nối** (Appium hoặc thiết bị mất kết nối giữa chừng): dừng toàn bộ, state giữ nguyên. Hiện hộp thoại hướng dẫn kết nối lại, kèm nút "Tiếp tục".
  - **Không tìm thấy phần tử** / **timeout**: retry dòng đó, hết lượt thì đánh `Failed` và chạy tiếp.
  - **Dữ liệu** (nhóm/ví không tồn tại trong app): đánh `Failed` ngay, không retry.
- Thư mục dữ liệu chứa thông tin tài chính cá nhân. Trong repo, `.gitignore` phải chặn file sao kê thật, `state.json`, `logs/`, `screenshots/`.

## 12. Thu thập selector (việc cần làm trước khi code Pages)

Với từng màn hình bên dưới, mở trên điện thoại rồi chạy `adb shell uiautomator dump /sdcard/ui.xml; adb pull /sdcard/ui.xml` (hoặc dùng Appium Inspector). Ghi `resource-id`, `text`, `content-desc` vào `Selectors`:

**Đã xong (16/11/2026)**, bằng dump thật lưu ở `dumps/` (git bỏ qua vì chứa số dư/giao dịch):
- [x] Màn chính: `btAddTransaction`
- [x] Add transaction: tab loại, ví, ô số tiền, bàn phím số (id từng phím), nhóm, ghi chú, ngày (nút lùi/tiến 1 ngày), Save
- [x] Add transaction sau khi bấm `>`: bàn phím đóng, ô ngày vẫn ở chỗ cũ, Save bị khóa khi chưa chọn nhóm
- [x] Màn chọn nhóm: tab, `recycler_view`, `@name`, `@child_indicator`, "SHOW MORE…"
- [x] Màn chọn ví: `@name` trong dòng, nút menu ⋮ phải tránh
- [x] Màn ghi chú: `edtNote`, `actionSave`
- [—] DatePicker: không cần (xem 8.3)

**Tên nhóm** cũng được đọc thẳng từ màn chọn nhóm: tự cuộn và bấm "SHOW MORE…", không chép từ ảnh. Nhờ vậy phát hiện ra các chỗ chép sai từ ảnh: "Cắt Tóc" và "Cẳt Tóc" là 2 nhóm khác nhau, "Mỳ, Bún, Cơm", và nhóm bị sót "Family > Pets".

Ưu tiên `resource-id`, sau đó `content-desc`, cuối cùng mới đến `text`. Tránh xpath theo index vì dễ gãy khi app cập nhật.

Đồng thời liệt kê **tên chính xác các nhóm và ví** trong app để điền `KnownCategories`, `CategoryRules` và `Defaults`.

## 13. Tiêu chí hoàn thành (v1)

**Core (unit test, không cần điện thoại):**
- [ ] Đọc được file sao kê mẫu (đã ẩn danh) với đúng số dòng, ngày, loại, số tiền, mã GD; bỏ qua được metadata và vùng trống phía dưới.
- [ ] Luật phân loại đúng thứ tự ưu tiên, không phân biệt dấu/hoa thường, `Skip` hoạt động, có fallback nhóm mặc định.
- [ ] StateStore ghi atomic; đọc lại được sau khi bị ngắt giữa chừng.

**App:**
- [ ] Trang Môi trường phát hiện đúng khi thiếu adb, thiếu thiết bị, hoặc Appium chưa chạy; tự khởi động được Appium.
- [ ] Mở file sao kê: Xem trước hiện đúng phân loại; sửa nhóm và bỏ chọn được; dòng lỗi và dòng đã nhập bị khóa.
- [ ] "Chạy thử" đi hết các bước với 1 dòng mà không tạo giao dịch nào.
- [ ] Nhập 5 giao dịch (gồm cả Chi và Thu, 1 dòng ngày cũ, 1 dòng bị `Skip`): các dòng nhập hiện đúng trong app, dòng `Skip` không xuất hiện. UI vẫn phản hồi trong suốt lúc chạy.
- [ ] Bấm Dừng ở giao dịch thứ 3 rồi Tiếp tục (hoặc đóng app và mở lại): chỉ nhập giao dịch 3–5, không trùng 1–2.
- [ ] Mở file sao kê kỳ sau có ngày chồng với kỳ trước: các giao dịch đã `Done` hiện "Đã nhập" và không bị nhập lại.
- [ ] Dòng có nhóm không tồn tại trong app: đánh `Failed`, có screenshot và page source xem được trên trang Chạy, các dòng sau vẫn được nhập.
- [ ] Chạy được qua cả USB và Wi-Fi debugging.
- [ ] Bản publish single-file chạy được trên một máy Windows 11 khác mà không cần cài .NET.

## 14. Rủi ro & giới hạn

| Rủi ro | Giảm thiểu |
|---|---|
| Ngân hàng đổi định dạng sao kê | Tìm cột theo tên tiêu đề, không theo vị trí; reader nằm sau `IStatementReader`; báo rõ cột thiếu |
| Mô tả giao dịch mơ hồ (VD `TT QR - <mã>` không có tên cửa hàng), phân loại sai | Màn hình Xem trước cho sửa tay; tạo luật nhanh; nhóm mặc định |
| Nạp ví điện tử / chuyển khoản nội bộ bị tính là chi tiêu | Luật `action: Skip` |
| App Money Lover cập nhật giao diện làm selector gãy | Selector nằm trong config, tải lại không cần restart; lỗi có kèm page source; dùng "Chạy thử" để kiểm tra |
| Người dùng chưa cài Node/Appium/JDK | Trang Môi trường kiểm tra và hướng dẫn từng bước |
| UI bị treo do lệnh Appium đồng bộ | Mọi lệnh chạy trong `Task.Run`, báo tiến trình qua `IProgress` |
| Dừng giữa chừng gây giao dịch dở dang | Chỉ kiểm tra hủy giữa các bước, không hủy sau khi bấm Lưu |
| Bàn phím số riêng không nhận `SendKeys` | Fallback `AmountKeypad` |
| Danh sách nhóm dài, phải cuộn | `UiScrollable.scrollIntoView` |
| Màn hình khóa / Wi-Fi rớt | Kiểm tra trạng thái màn hình trước mỗi dòng; lỗi kết nối thì dừng, giữ state, cho phép Tiếp tục |
| Nhập trùng khi mất kết nối đúng lúc Lưu | Đánh dấu "cần kiểm tra tay", không tự chọn lại |
| Nhập trùng với giao dịch người dùng **đã nhập tay** trước đó (state.json không biết), ví dụ những ngày đầu kỳ sao kê đã được nhập tay | Người dùng bỏ chọn trên Xem trước. Sau v1: "Chỉ nhập từ ngày…", và cảnh báo khi số tiền + ngày trùng giao dịch đã có trong app |
| Hai ví tên gần giống ("TP Bank" và "TPBank") | Chọn ví bằng khớp chính xác text, không dùng `textContains` |
| Lộ dữ liệu tài chính cá nhân | Che số TK, không hiển thị số dư, dữ liệu nằm trong `%LOCALAPPDATA%`, `.gitignore`, file mẫu ẩn danh |
| Tự động hóa UI có thể trái điều khoản app | Chỉ dùng cho tài khoản của chính mình, tốc độ giống người dùng (có delay) |

## 15. Hướng mở rộng (sau v1)

- Installer và tự cập nhật bằng Velopack.
- Reader cho định dạng sao kê của ngân hàng khác (tự nhận diện qua tiêu đề).
- Nhập "Nạp ví MoMo" thành giao dịch chuyển tiền giữa các ví trong Money Lover thay vì `Skip`.
- Trình soi selector trong app: dump UI từ thiết bị, bấm chọn phần tử để gán vào key selector.
- Đóng gói sẵn `adb.exe` trong app.
- Lớp Core dùng lại cho các app tài chính khác.
