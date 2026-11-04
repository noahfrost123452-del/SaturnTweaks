using System.Windows.Controls;
using SaturnTweaks.UI.ViewModels;

namespace SaturnTweaks.UI.Views;

public partial class NetworkPage : Page
{
    public NetworkPage(NetworkViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
