using System.Windows.Controls;
using System.Windows.Input;
using SaturnTweaks.UI.ViewModels;

namespace SaturnTweaks.UI.Views;

public partial class RestoreCenterPage : Page
{
    private readonly RestoreCenterViewModel _viewModel;

    public RestoreCenterPage(RestoreCenterViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void Session_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement fe && fe.DataContext is SessionGroup g)
            _viewModel.SelectedSession = g;
    }
}
