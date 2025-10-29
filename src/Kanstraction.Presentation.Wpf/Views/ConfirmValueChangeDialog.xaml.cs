using System.Windows;

namespace Kanstraction.Views;

public partial class ConfirmValueChangeDialog : Window
{
    public ConfirmValueChangeDialog(string title, string message, string confirmText, string revertText)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        BtnSave.Content = confirmText;
        BtnReturnToDefault.Content = revertText;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void ReturnToDefault_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
