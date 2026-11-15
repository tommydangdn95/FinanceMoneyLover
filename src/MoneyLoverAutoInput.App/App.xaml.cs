using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MoneyLoverAutoInput.App.Services;
using MoneyLoverAutoInput.App.ViewModels;
using MoneyLoverAutoInput.App.Views;
using MoneyLoverAutoInput.Core;
using MoneyLoverAutoInput.Core.Configuration;
using MoneyLoverAutoInput.Core.Input;
using MoneyLoverAutoInput.Core.State;
using Serilog;

namespace MoneyLoverAutoInput.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\MoneyLoverAutoInput.SingleInstance";

    private Mutex? _singleInstance;
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Chỉ một instance: hai process cùng ghi state.json có thể làm hỏng file (SPEC 10.8).
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Money Lover Auto Input đang chạy rồi.", "Money Lover Auto Input",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var paths = AppDataPaths.Default();

        try
        {
            paths.EnsureCreated();
            UserConfig.EnsureExists(paths);
            ConfigureLogging(paths);
            RegisterGlobalExceptionHandlers();

            _host = BuildHost(paths);
            await _host.StartAsync();

            // Resolve sớm để lỗi state.json/config hiện ngay lúc mở app.
            _host.Services.GetRequiredService<StateStore>();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            Log.Information("Khởi động app. Thư mục dữ liệu: {Root}", paths.Root);

            // Mở file truyền qua dòng lệnh (kéo file thả lên exe, "Open with").
            var fileArg = e.Args.FirstOrDefault(a => a.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            if (fileArg is not null)
            {
                await _host.Services.GetRequiredService<PreviewViewModel>().LoadFileAsync(System.IO.Path.GetFullPath(fileArg));
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Không khởi động được app");
            MessageBox.Show(
                $"Không khởi động được app:\n\n{ex.Message}\n\nThư mục dữ liệu: {paths.Root}",
                "Money Lover Auto Input", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static IHost BuildHost(AppDataPaths paths)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = paths.Root,
        });

        // Chỉ đọc config người dùng (xem UserConfig).
        builder.Configuration.AddJsonFile(paths.ConfigFile, optional: false, reloadOnChange: true);

        var services = builder.Services;
        services.AddSerilog();
        services.Configure<AppSettings>(builder.Configuration);

        services.AddSingleton(paths);
        services.AddSingleton(_ => StateStore.Load(paths.StateFile));
        services.AddSingleton<IStatementReader, BankStatementReader>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IShellService, ShellService>();

        services.AddSingleton<PreviewViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private static void ConfigureLogging(AppDataPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                System.IO.Path.Combine(paths.LogsDir, "run-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 60,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {RowId} {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Lỗi trong task nền không được xử lý");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Lỗi nghiêm trọng, app sẽ đóng");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Lỗi không được xử lý trên UI thread");
        MessageBox.Show($"Đã xảy ra lỗi:\n\n{e.Exception.Message}\n\nChi tiết đã được ghi vào log.",
            "Money Lover Auto Input", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Đồng bộ: OnExit async void sẽ để process thoát trước khi host dừng xong.
            _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        Log.CloseAndFlush();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
