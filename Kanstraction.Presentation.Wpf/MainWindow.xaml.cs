using Kanstraction.Presentation.Wpf.ViewModels;
using System.Windows;

namespace Kanstraction.Presentation.Wpf;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
