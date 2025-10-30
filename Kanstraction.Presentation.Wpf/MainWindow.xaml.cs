using Kanstraction.Presentation.Wpf.ViewModels;
using System.Windows;

namespace Kanstraction;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
