using System.Windows;

namespace Kanstraction.Views;

public partial class StartupLoadingWindow : Window
{
    public StartupLoadingWindow()
    {
        InitializeComponent();
    }

    public void UpdateStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateStatus(message));
            return;
        }

        StatusTextBlock.Text = message;
    }
}
