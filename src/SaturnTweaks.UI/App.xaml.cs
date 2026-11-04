using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Engine;
using SaturnTweaks.Engine.Detection;
using SaturnTweaks.Engine.Tweaks;
using SaturnTweaks.UI.ViewModels;
using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace SaturnTweaks.UI;

public partial class App : Application
{
    private IServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();
        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        // MainWindow has constructor-injected dependencies. Do not use App.xaml
        // StartupUri for it: WPF's XAML loader would try to call a parameterless
        // constructor before the dependency-injection container can create it.
        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("WPF dispatcher exception", args.Exception);
            MessageBox.Show(
                "Saturn Tweaks encountered an unexpected UI error. Details were saved to the app log.\n\n" + args.Exception.Message,
                "Saturn Tweaks error", MessageBoxButton.OK, MessageBoxImage.Error);
            // A dispatcher exception can be safely recovered from only after the user is informed.
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                WriteCrashLog("Unrecoverable application exception", exception);
            else
                WriteCrashLog("Unrecoverable application exception", new Exception(args.ExceptionObject?.ToString() ?? "Unknown exception"));
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("Unobserved background task exception", args.Exception);
            args.SetObserved();
        };
    }

    private static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SaturnTweaks", "Logs");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "crash.log");
            File.AppendAllText(path,
                $"[{DateTimeOffset.Now:O}] {source}\r\n{exception}\r\n{new string('-', 80)}\r\n");
        }
        catch
        {
            // Crash reporting must never cause another crash.
        }
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        // Logging
        // This is a GUI (WinExe) application, so do not register the Console
        // logging provider: it requires Microsoft.Extensions.Logging.Console and
        // there is no console window for users to inspect. Debug logging remains
        // available to Visual Studio / diagnostic listeners.
        services.AddLogging(b => b
            .SetMinimumLevel(LogLevel.Debug)
            .AddDebug());

        // Core services
        services.AddSingleton<ISystemDetector, WindowsSystemDetector>();
        services.AddSingleton<IChangeHistoryRepository, ChangeHistoryRepository>();

        // MonitoringService: inject the WPF Dispatcher as the UI-thread marshal.
        // The Engine project must not reference WPF, so the marshal is wired here
        // in the UI project where System.Windows.Application is available.
        services.AddSingleton<MonitoringService>(sp =>
        {
            var logger  = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MonitoringService>>();
            var detector = sp.GetRequiredService<ISystemDetector>();
            Action<Action> marshal = action =>
                System.Windows.Application.Current?.Dispatcher.Invoke(action);
            return new MonitoringService(logger, detector, marshal);
        });

        services.AddSingleton<GameLibraryService>();

        // Phase 1 tweaks
        services.AddSingleton<ITweakExecutor, HagsEnableTweak>();
        services.AddSingleton<ITweakExecutor, GameModeEnableTweak>();
        services.AddSingleton<ITweakExecutor, GameDvrDisableTweak>();
        services.AddSingleton<ITweakExecutor, VisualEffectsPerformanceTweak>();
        services.AddSingleton<ITweakExecutor, DisableMouseAccelerationTweak>();
        services.AddSingleton<ITweakExecutor, MmcssGamingPriorityTweak>();
        services.AddSingleton<ITweakExecutor, NetworkThrottlingDisableTweak>();
        services.AddSingleton<ITweakExecutor, PowerPlanTweak>();

        // Phase 6 tweaks — Graphics
        services.AddSingleton<ITweakExecutor, DisableFullscreenOptimizationsTweak>();
        services.AddSingleton<ITweakExecutor, DisableVrrTearingTweak>();
        services.AddSingleton<ITweakExecutor, PreferDiscreteGpuForAppsTweak>();

        // Phase 6 tweaks — Input
        services.AddSingleton<ITweakExecutor, FlattenMouseCurvesTweak>();
        services.AddSingleton<ITweakExecutor, DisableUsbSelectiveSuspendTweak>();
        services.AddSingleton<ITweakExecutor, NeutralPointerSpeedTweak>();

        // Phase 6 tweaks — Memory
        services.AddSingleton<ITweakExecutor, DisableLargeSystemCacheTweak>();
        services.AddSingleton<ITweakExecutor, DisableClearPageFileTweak>();

        // Phase 6 tweaks — Advanced
        services.AddSingleton<ITweakExecutor, Irq8PriorityTweak>();
        services.AddSingleton<ITweakExecutor, Win32PrioritySeparationTweak>();
        services.AddSingleton<ITweakExecutor, DisableSysMainTweak>();

        // Orchestrator
        services.AddSingleton<TweakOrchestrator>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<NetworkViewModel>();
        services.AddSingleton<GraphicsViewModel>();
        services.AddSingleton<InputViewModel>();
        services.AddSingleton<MemoryStorageViewModel>();
        services.AddSingleton<AdvancedViewModel>();
        services.AddSingleton<GameProfilesViewModel>();
        services.AddSingleton<RestoreCenterViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Pages
        services.AddTransient<Views.OverviewPage>();
        services.AddTransient<Views.PerformancePage>();
        services.AddTransient<Views.GraphicsPage>();
        services.AddTransient<Views.NetworkPage>();
        services.AddTransient<Views.InputPage>();
        services.AddTransient<Views.MemoryStoragePage>();
        services.AddTransient<Views.GameProfilesPage>();
        services.AddTransient<Views.AdvancedPage>();
        services.AddTransient<Views.DiagnosticsPage>();
        services.AddTransient<Views.RestoreCenterPage>();
        services.AddTransient<Views.SettingsPage>();

        // Main window
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_services as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}
