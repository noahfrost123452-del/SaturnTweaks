using System.Windows.Controls;
using SaturnTweaks.UI.ViewModels;

namespace SaturnTweaks.UI.Views;

public partial class MemoryStoragePage : Page
{
    public MemoryStoragePage(MemoryStorageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
