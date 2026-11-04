using System.Windows.Controls;
using System.Windows.Input;
using SaturnTweaks.Engine;
using SaturnTweaks.UI.ViewModels;

namespace SaturnTweaks.UI.Views;

public partial class GameProfilesPage : Page
{
    private readonly GameProfilesViewModel _viewModel;

    public GameProfilesPage(GameProfilesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void Game_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.DataContext is DetectedGame game)
            await _viewModel.SelectGameCommand.ExecuteAsync(game);
    }
}
