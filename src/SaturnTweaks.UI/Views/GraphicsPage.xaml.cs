using System.Windows.Controls;
using System.Windows.Input;
using SaturnTweaks.UI.ViewModels;

namespace SaturnTweaks.UI.Views;

public partial class GraphicsPage : Page
{
    private readonly GraphicsViewModel _viewModel;

    public GraphicsPage(GraphicsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void TweakRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.FrameworkElement fe || fe.DataContext is not TweakRowItem item)
            return;

        if (_viewModel.SelectedTweak is not null)
            _viewModel.SelectedTweak.IsPreviewed = false;

        _viewModel.SelectedTweak = item;
        item.IsPreviewed = true;
    }
}
