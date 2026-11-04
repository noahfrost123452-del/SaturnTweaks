using System.Windows.Controls;
using System.Windows.Input;
using SaturnTweaks.UI.ViewModels;

namespace SaturnTweaks.UI.Views;

public partial class PerformancePage : Page
{
    private readonly PerformanceViewModel _viewModel;

    public PerformancePage(PerformanceViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void TweakRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe
            && fe.DataContext is TweakRowItem item)
        {
            if (_viewModel.SelectedTweak is not null)
                _viewModel.SelectedTweak.IsPreviewed = false;
            _viewModel.SelectedTweak = item;
            item.IsPreviewed = true;
        }
    }
}
