using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Kanstraction.Views;

/// <summary>
/// Interaction logic for PromptTextDialog.xaml
/// </summary>
public partial class PromptTextDialog : Window
{
    public string? Value { get; private set; }

    public PromptTextDialog(string title, string? initial = null)
    {
        InitializeComponent();
        TitleText.Text = title;
        if (!string.IsNullOrWhiteSpace(initial))
            InputBox.Text = initial;
        Loaded += (_, __) => InputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var v = InputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(v))
        {
            MessageBox.Show("Please enter a value.", "Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Value = v;
        DialogResult = true;
    }

    private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Return)
            Ok_Click(sender!, e);
    }
}