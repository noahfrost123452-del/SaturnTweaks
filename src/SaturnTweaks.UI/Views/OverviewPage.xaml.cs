using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SaturnTweaks.UI.ViewModels;

namespace SaturnTweaks.UI.Views;

public partial class OverviewPage : Page
{
    public OverviewPage(OverviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
