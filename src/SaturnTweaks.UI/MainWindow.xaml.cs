using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SaturnTweaks.UI.ViewModels;
using SaturnTweaks.UI.Views;

namespace SaturnTweaks.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _services;

    public MainWindow(MainViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _services = services;
        DataContext = viewModel;

        viewModel.NavigationRequested += OnNavigationRequested;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            NavigateTo("Overview");
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Saturn Tweaks could not finish starting.\n\n{ex.Message}\n\nA diagnostic log may be available in %LOCALAPPDATA%\SaturnTweaks\Logs.",
                "Saturn Tweaks startup error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnNavigationRequested(object? sender, string pageName)
    {
        NavigateTo(pageName);
    }

    private void NavigateTo(string pageName)
    {
        try
        {
            Page page = pageName switch
        {
            "Overview" => _services.GetRequiredService<OverviewPage>(),
            "Performance" => _services.GetRequiredService<PerformancePage>(),
            "Graphics" => _services.GetRequiredService<GraphicsPage>(),
            "Network" => _services.GetRequiredService<NetworkPage>(),
            "Input" => _services.GetRequiredService<InputPage>(),
            "Memory" => _services.GetRequiredService<MemoryStoragePage>(),
            "GameProfiles" => _services.GetRequiredService<GameProfilesPage>(),
            "Advanced" => _services.GetRequiredService<AdvancedPage>(),
            "Diagnostics" => _services.GetRequiredService<DiagnosticsPage>(),
            "Restore" => _services.GetRequiredService<RestoreCenterPage>(),
            "Settings" => _services.GetRequiredService<SettingsPage>(),
            _ => _services.GetRequiredService<OverviewPage>()
        };

            ContentFrame.Navigate(page);
            _viewModel.CurrentPage = pageName;
        }
        catch (Exception ex)
        {
            var error = new TextBlock
            {
                Text = $"This page could not be opened.\n\n{ex.Message}\n\nTry another page or use Settings to inspect the application data folder.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(32),
                FontSize = 14
            };
            ContentFrame.Content = error;
        }
    }

    // ─── Window chrome handlers ───────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
