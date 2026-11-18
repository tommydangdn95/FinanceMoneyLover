using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Models;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using DriverOptions = OpenQA.Selenium.Appium.AppiumOptions;

namespace MoneyLoverAutoInput.Core.Automation;

/// <summary>
/// Điều khiển Money Lover qua Appium UiAutomator2 theo SPEC 8.2. Mọi selector lấy từ cấu hình.
/// Lệnh Appium là đồng bộ → mỗi thao tác công khai chạy trong Task.Run.
/// </summary>
public sealed class MoneyLoverUi : IMoneyLoverUi
{
    private readonly AndroidDriver _driver;
    private readonly AppSettings _settings;
    private readonly SelectorResolver _sel;
    private readonly ILogger _logger;
    private readonly string _dataRoot;
    private readonly TimeSpan _wait;

    private MoneyLoverUi(AndroidDriver driver, AppSettings settings, string dataRoot, ILogger logger)
    {
        _driver = driver;
        _settings = settings;
        _sel = new SelectorResolver(settings.Selectors);
        _dataRoot = dataRoot;
        _logger = logger;
        _wait = TimeSpan.FromSeconds(Math.Max(3, settings.Timing.DefaultWaitSec));
    }

    /// <summary>Tạo session Appium tới thiết bị. Lần đầu Appium cài UiAutomator2 server lên điện thoại (chậm).</summary>
    public static async Task<MoneyLoverUi> CreateAsync(AppSettings settings, string deviceSerial, string dataRoot,
        ILogger logger, CancellationToken ct)
    {
        var options = new DriverOptions
        {
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            DeviceName = deviceSerial,
        };
        options.AddAdditionalAppiumOption("udid", deviceSerial);
        options.AddAdditionalAppiumOption("noReset", true);
        options.AddAdditionalAppiumOption("newCommandTimeout", settings.Appium.NewCommandTimeoutSec);
        options.AddAdditionalAppiumOption("ignoreHiddenApiPolicyError", true);

        var driver = await Task.Run(() =>
        {
            try
            {
                return new AndroidDriver(new Uri(settings.Appium.ServerUrl), options, TimeSpan.FromSeconds(180));
            }
            catch (WebDriverException ex)
            {
                throw new ConnectionLostException("Không tạo được phiên Appium: " + ex.Message, ex);
            }
        }, ct).ConfigureAwait(false);

        logger.LogInformation("Đã kết nối Appium tới thiết bị {Serial}", deviceSerial);
        return new MoneyLoverUi(driver, settings, dataRoot, logger);
    }

    // ================= IMoneyLoverUi =================

    public Task EnsureHomeAsync(CancellationToken ct) => Run(() =>
    {
        if (Exists("AddButton", TimeSpan.FromSeconds(2))) return;

        _driver.ActivateApp(_settings.Appium.AppPackage);
        if (Exists("AddButton", TimeSpan.FromSeconds(3))) return;

        for (var i = 0; i < 3; i++)
        {
            ct.ThrowIfCancellationRequested();
            Back();
            if (Exists("AddButton", TimeSpan.FromSeconds(2))) return;
        }

        _driver.ActivateApp(_settings.Appium.AppPackage);
        if (!Exists("AddButton", _wait))
            throw new UiStepException("Không về được màn chính của Money Lover (không thấy nút \"+\").");
    });

    public Task FillTransactionAsync(TransactionRow row, IProgress<string> step, CancellationToken ct) => Run(() =>
    {
        step.Report("Mở màn thêm giao dịch");
        Tap("AddButton");
        Wait("AmountField");
        ct.ThrowIfCancellationRequested();

        step.Report(row.Loai == TransactionType.Chi ? "Chọn loại: Chi" : "Chọn loại: Thu");
        Tap(row.Loai == TransactionType.Chi ? "TypeTabExpense" : "TypeTabIncome");
        ct.ThrowIfCancellationRequested();

        step.Report($"Nhập số tiền: {row.SoTien.ToString("#,##0", CultureInfo.GetCultureInfo("vi-VN"))}");
        EnterAmount(row.SoTien);
        ct.ThrowIfCancellationRequested();

        step.Report($"Chọn ví: {row.Vi}");
        SelectWallet(row.Vi);
        ct.ThrowIfCancellationRequested();

        step.Report($"Chọn nhóm: {row.Nhom}");
        SelectCategory(row.Nhom, row.Loai);
        ct.ThrowIfCancellationRequested();

        step.Report($"Chọn ngày: {row.Ngay:dd/MM/yyyy}");
        SetDate(row.Ngay);
        ct.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(row.GhiChu))
        {
            step.Report("Ghi chú");
            SetNote(row.GhiChu);
        }
    });

    public Task SaveAsync() => Run(() =>
    {
        var save = Wait("SaveButton");
        if (!save.Enabled)
            throw new UiStepException("Nút Save đang khóa — giao dịch chưa đủ thông tin.");
        save.Click();
        if (!Exists("AddButton", _wait + _wait))
            throw new UiStepException("Đã bấm Save nhưng không thấy quay về màn chính.");
    });

    public Task DiscardEntryAsync() => Run(() =>
    {
        for (var i = 0; i < 4; i++)
        {
            if (Exists("AddButton", TimeSpan.FromSeconds(1))) return;
            Back();
            ConfirmDiscardIfAsked();
        }
    });

    public Task<string?> CaptureFailureAsync(string rowId) => Task.Run<string?>(() =>
    {
        try
        {
            var dir = Path.Combine(_dataRoot, "screenshots");
            Directory.CreateDirectory(dir);
            var safeId = Regex.Replace(rowId, @"[^\w#.-]", "_");
            var name = $"{safeId}_{DateTime.Now:yyyyMMdd_HHmmss}";
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), _driver.GetScreenshot().AsByteArray);
            File.WriteAllText(Path.Combine(dir, name + ".xml"), _driver.PageSource);
            return Path.Combine("screenshots", name + ".png");
        }
        catch (Exception ex) when (ex is WebDriverException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Không chụp được màn hình lỗi");
            return null;
        }
    });

    public ValueTask DisposeAsync()
    {
        try { _driver.Quit(); }
        catch (WebDriverException ex) { _logger.LogDebug(ex, "Quit Appium session"); }
        _driver.Dispose();
        return ValueTask.CompletedTask;
    }

    // ================= Các bước =================

    /// <summary>Bàn phím số riêng của app: C, từng chữ số (0 = KeypadZero, "000" = KeypadTripleZero), rồi ">".</summary>
    private void EnterAmount(long amount)
    {
        if (amount <= 0) throw new UiStepException("Số tiền phải lớn hơn 0.");

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            if (!Exists("KeypadClear", TimeSpan.FromSeconds(2)))
            {
                Tap("AmountField");
                Wait("KeypadClear");
            }

            Tap("KeypadClear");
            var digits = amount.ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < digits.Length;)
            {
                if (i > 0 && string.CompareOrdinal(digits, i, "000", 0, 3) == 0)
                {
                    Tap("KeypadTripleZero");
                    i += 3;
                    continue;
                }
                var d = digits[i];
                if (d == '0') Tap("KeypadZero");
                else Tap(_sel.By("KeypadDigit", "d", d.ToString()), $"phím {d}");
                i++;
            }
            Tap("KeypadNext");

            var shown = ParseAmountText(Wait("AmountField").Text);
            if (shown == amount) return;
            _logger.LogWarning("Số tiền hiển thị {Shown} khác {Expected}, nhập lại (lần {Attempt})", shown, amount, attempt);
        }
        throw new UiStepException($"Không nhập được đúng số tiền {amount}.");
    }

    /// <summary>"45,000" / "45.000" / "45,000.00" → 45000 (bỏ phần thập phân nếu có).</summary>
    internal static long? ParseAmountText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = Regex.Replace(text, @"[.,]\d{1,2}$", ""); // phần lẻ ".00"/",0"
        var digits = new string(t.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var v) ? v : null;
    }

    private void SelectWallet(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet)) throw new DataNotFoundException("Chưa có ví.");
        if (Text("WalletText") == wallet) return;

        Tap("WalletField");
        var by = _sel.By("WalletItem", "name", wallet);
        if (!Exists(by, _wait))
        {
            Back();
            throw new DataNotFoundException($"Không có ví '{wallet}' trong Money Lover.");
        }
        _driver.FindElement(by).Click();

        if (!WaitUntil(() => TryText("WalletText") == wallet, _wait))
            throw new UiStepException($"Đã chọn ví '{wallet}' nhưng màn hình không cập nhật.");
    }

    private void SelectCategory(string path, TransactionType type)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new DataNotFoundException("Chưa có nhóm.");
        var (parent, child) = CategoryTree.Split(path);
        var leaf = child ?? parent;

        // Money Lover nhớ và CHỌN SẴN nhóm dùng lần trước. Nếu đúng nhóm cần nhập thì không mở màn chọn nhóm.
        // Màn chỉ hiện tên lá, nên chỉ bỏ qua khi tên lá không trùng với nhóm khác cùng loại (VD "Fitness" trùng).
        if (TryText("CategoryText") == leaf && IsUniqueLeaf(leaf, type))
        {
            _logger.LogInformation("Nhóm '{Path}' đã được chọn sẵn — bỏ qua bước chọn nhóm", path);
            return;
        }

        Tap("CategoryField");
        var list = Wait("CategoryList");
        var tab = type == TransactionType.Chi ? "CategoryTabExpense" : "CategoryTabIncome";
        if (Exists(tab, TimeSpan.FromSeconds(2))) Tap(tab);
        list = Wait("CategoryList");

        ScrollListToTop(list);

        var nameId = _sel.Value("CategoryRowName");
        var childId = _sel.Value("CategoryChildIndicator");
        var insideParent = false;
        var expansions = 0;
        var atEnd = false; // scrollGesture trả false = đã cuộn và giờ đang ở cuối → vẫn phải đọc trang cuối này

        for (var page = 0; page < 80; page++)
        {
            var rows = CategoryListParser.Parse(_driver.PageSource, nameId, childId);
            switch (CategorySearch.Next(rows, parent, child, insideParent))
            {
                case CategorySearchStep.Tap t:
                    TapAt(t.Row.Bounds.CenterX, t.Row.Bounds.CenterY);
                    if (!WaitUntil(() => TryText("CategoryText") == leaf, _wait))
                        throw new UiStepException($"Đã bấm nhóm '{path}' nhưng màn thêm giao dịch không hiện nhóm đó.");
                    return;

                case CategorySearchStep.ExpandShowMore m:
                    if (++expansions > 30) throw new UiStepException("Bấm \"SHOW MORE\" quá nhiều lần.");
                    TapAt(m.Row.Bounds.CenterX, m.Row.Bounds.CenterY);
                    Thread.Sleep(600);
                    page--; // đọc lại cùng vị trí
                    continue;

                case CategorySearchStep.NotFound nf:
                    SaveDiagnostic("category_notfound");
                    Back();
                    throw new DataNotFoundException(nf.Reason);

                case CategorySearchStep.ScrollDown s:
                    insideParent = s.InsideParent;
                    if (atEnd)
                    {
                        SaveDiagnostic("category_notfound");
                        Back();
                        throw new DataNotFoundException($"Không có nhóm '{path}' trong Money Lover.");
                    }
                    atEnd = !ScrollList(Wait("CategoryList"), "down", 0.6);
                    Thread.Sleep(400);
                    continue;
            }
        }
        Back();
        throw new UiStepException($"Cuộn quá lâu mà không thấy nhóm '{path}'.");
    }

    private bool IsUniqueLeaf(string leaf, TransactionType type) =>
        _settings.GetKnownCategories(type).Count(p => CategoryTree.LeafName(p) == leaf) <= 1;

    /// <summary>Lưu ảnh + page source của màn ĐANG hiện (trước khi Back) để chẩn đoán selector.</summary>
    private void SaveDiagnostic(string tag)
    {
        try
        {
            var dir = Path.Combine(_dataRoot, "screenshots");
            Directory.CreateDirectory(dir);
            var name = $"{tag}_{DateTime.Now:yyyyMMdd_HHmmss}";
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), _driver.GetScreenshot().AsByteArray);
            File.WriteAllText(Path.Combine(dir, name + ".xml"), _driver.PageSource);
            _logger.LogWarning("Đã lưu màn hình chẩn đoán: screenshots\\{Name}.xml", name);
        }
        catch (Exception ex) when (ex is WebDriverException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Không lưu được màn hình chẩn đoán");
        }
    }

    /// <summary>Ngày hiển thị "Sunday, 15/11/2026" → bấm lùi/tiến 1 ngày tới đúng ngày.</summary>
    private void SetDate(DateOnly target)
    {
        for (var guard = 0; guard < 800; guard++)
        {
            var current = ParseDateText(Text("DateText"))
                          ?? throw new UiStepException($"Không đọc được ngày: '{TryText("DateText")}'.");
            var diff = target.DayNumber - current.DayNumber;
            if (diff == 0) return;
            if (Math.Abs(diff) > 400) throw new UiStepException($"Ngày {target:dd/MM/yyyy} cách hôm nay quá xa.");

            // Bấm nhiều lần liên tiếp rồi mới đọc lại cho nhanh; gần đích thì bấm từng lần.
            var steps = Math.Min(Math.Abs(diff), Math.Abs(diff) > 5 ? 5 : 1);
            var key = diff < 0 ? "DatePrev" : "DateNext";
            for (var i = 0; i < steps; i++) Tap(key);
        }
        throw new UiStepException("Không chỉnh được ngày.");
    }

    internal static DateOnly? ParseDateText(string? text)
    {
        var m = Regex.Match(text ?? "", @"(\d{1,2})/(\d{1,2})/(\d{4})");
        return m.Success && DateOnly.TryParseExact($"{int.Parse(m.Groups[1].Value):00}/{int.Parse(m.Groups[2].Value):00}/{m.Groups[3].Value}",
            "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private void SetNote(string note)
    {
        Tap("NoteField");
        var edit = Wait("NoteEdit");
        edit.Clear();
        edit.SendKeys(note);
        Tap("NoteSave");
        Wait("AmountField");
    }

    // ================= Tiện ích =================

    /// <summary>
    /// Chạy lệnh Appium (đồng bộ) trên thread pool. Exception được BẮT TRONG Task.Run rồi ném lại ở phía async:
    /// nếu để exception thoát khỏi delegate của Task.Run, Visual Studio (Just My Code) coi là "user-unhandled"
    /// và dừng app dù runner vẫn bắt được bình thường.
    /// </summary>
    private async Task Run(Action action)
    {
        var error = await Task.Run(() =>
        {
            try
            {
                Guard(action);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }).ConfigureAwait(false);

        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Đổi lỗi WebDriver thành loại lỗi của SPEC 11.</summary>
    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (AutomationException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (WebDriverException ex) when (IsConnectionError(ex))
        {
            throw new ConnectionLostException("Mất kết nối tới Appium/điện thoại: " + ex.Message, ex);
        }
        catch (WebDriverException ex)
        {
            throw new UiStepException(ex.Message, ex);
        }
    }

    internal static bool IsConnectionError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is HttpRequestException or System.Net.Sockets.SocketException) return true;
            var m = e.Message;
            if (m.Contains("session", StringComparison.OrdinalIgnoreCase) &&
                (m.Contains("terminated", StringComparison.OrdinalIgnoreCase) ||
                 m.Contains("not started", StringComparison.OrdinalIgnoreCase) ||
                 m.Contains("no such session", StringComparison.OrdinalIgnoreCase))) return true;
            if (m.Contains("instrumentation process is not running", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("device offline", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("device not found", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("socket hang up", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("ECONNREFUSED", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("Could not proxy", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private IWebElement Wait(string key) => Wait(_sel.By(key), key);

    private IWebElement Wait(By by, string label)
    {
        IWebElement? found = null;
        if (WaitUntil(() => (found = _driver.FindElements(by).FirstOrDefault()) is not null, _wait)) return found!;
        throw new UiStepException($"Không thấy '{label}' trên màn hình.");
    }

    private bool Exists(string key, TimeSpan timeout) => Exists(_sel.By(key), timeout);

    private bool Exists(By by, TimeSpan timeout) => WaitUntil(() => _driver.FindElements(by).Count > 0, timeout);

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (condition()) return true;
            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(250);
        }
    }

    private void Tap(string key) => Wait(key).Click();

    private void Tap(By by, string label) => Wait(by, label).Click();

    private string Text(string key) => Wait(key).Text ?? "";

    private string? TryText(string key) => _driver.FindElements(_sel.By(key)).FirstOrDefault()?.Text;

    private void TapAt(int x, int y) =>
        _driver.ExecuteScript("mobile: clickGesture", new Dictionary<string, object> { ["x"] = x, ["y"] = y });

    private void Back() => _driver.Navigate().Back();

    /// <summary>Nếu Back làm hiện hộp thoại "bỏ giao dịch?" thì xác nhận bỏ.</summary>
    private void ConfirmDiscardIfAsked()
    {
        if (!_settings.Selectors.ContainsKey("DiscardConfirm")) return;
        var by = _sel.By("DiscardConfirm");
        if (Exists(by, TimeSpan.FromMilliseconds(800))) _driver.FindElement(by).Click();
    }

    /// <summary>Cuộn danh sách; trả về false nếu đã hết (không cuộn thêm được).</summary>
    private bool ScrollList(IWebElement list, string direction, double percent)
    {
        var loc = list.Location;
        var size = list.Size;
        var result = _driver.ExecuteScript("mobile: scrollGesture", new Dictionary<string, object>
        {
            ["left"] = loc.X, ["top"] = loc.Y, ["width"] = size.Width, ["height"] = size.Height,
            ["direction"] = direction, ["percent"] = percent,
        });
        return result is bool b && b;
    }

    private void ScrollListToTop(IWebElement list)
    {
        for (var i = 0; i < 40 && ScrollList(list, "up", 1.0); i++) { }
    }
}
