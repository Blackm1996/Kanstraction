using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Kanstraction.Behaviors;

/// <summary>
/// Applies a white background to any editable <see cref="TextBox"/> while it has keyboard focus
/// so that edit mode is obvious across the application.
/// </summary>
public static class TextBoxEditHighlighter
{
    private enum StoredBackgroundKind
    {
        None,
        LocalValue,
        Binding
    }

    private static readonly DependencyProperty HighlightAppliedProperty = DependencyProperty.RegisterAttached(
        "HighlightApplied",
        typeof(bool),
        typeof(TextBoxEditHighlighter),
        new PropertyMetadata(false));

    private static readonly DependencyProperty StoredBackgroundKindProperty = DependencyProperty.RegisterAttached(
        "StoredBackgroundKind",
        typeof(StoredBackgroundKind),
        typeof(TextBoxEditHighlighter),
        new PropertyMetadata(StoredBackgroundKind.None));

    private static readonly DependencyProperty StoredBackgroundValueProperty = DependencyProperty.RegisterAttached(
        "StoredBackgroundValue",
        typeof(object),
        typeof(TextBoxEditHighlighter));

    private static bool _isRegistered;

    public static void Register()
    {
        if (_isRegistered)
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnTextBoxGotKeyboardFocus),
            true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnTextBoxLostKeyboardFocus),
            true);

        _isRegistered = true;
    }

    private static void OnTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (textBox.IsReadOnly || !textBox.IsEnabled)
        {
            return;
        }

        if ((bool)textBox.GetValue(HighlightAppliedProperty))
        {
            return;
        }

        var binding = CloneBackgroundBinding(textBox);

        if (binding != null)
        {
            textBox.SetValue(StoredBackgroundKindProperty, StoredBackgroundKind.Binding);
            textBox.SetValue(StoredBackgroundValueProperty, binding);
        }
        else
        {
            var localValue = textBox.ReadLocalValue(Control.BackgroundProperty);
            if (localValue != DependencyProperty.UnsetValue)
            {
                textBox.SetValue(StoredBackgroundKindProperty, StoredBackgroundKind.LocalValue);
                textBox.SetValue(StoredBackgroundValueProperty, localValue);
            }
            else
            {
                textBox.SetValue(StoredBackgroundKindProperty, StoredBackgroundKind.None);
                textBox.ClearValue(StoredBackgroundValueProperty);
            }
        }

        textBox.SetValue(HighlightAppliedProperty, true);
        textBox.SetValue(Control.BackgroundProperty, Brushes.White);
    }

    private static void OnTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (!(bool)textBox.GetValue(HighlightAppliedProperty))
        {
            return;
        }

        textBox.ClearValue(HighlightAppliedProperty);

        var kind = (StoredBackgroundKind)textBox.GetValue(StoredBackgroundKindProperty);
        var storedValue = textBox.GetValue(StoredBackgroundValueProperty);

        textBox.ClearValue(StoredBackgroundKindProperty);
        textBox.ClearValue(StoredBackgroundValueProperty);

        switch (kind)
        {
            case StoredBackgroundKind.Binding when storedValue is BindingBase binding:
                BindingOperations.SetBinding(textBox, Control.BackgroundProperty, binding);
                break;
            case StoredBackgroundKind.LocalValue:
                textBox.SetValue(Control.BackgroundProperty, storedValue);
                break;
            default:
                textBox.ClearValue(Control.BackgroundProperty);
                break;
        }
    }

    private static BindingBase? CloneBackgroundBinding(TextBox textBox)
    {
        if (BindingOperations.GetBinding(textBox, Control.BackgroundProperty) is Binding binding)
        {
            return binding.Clone();
        }

        if (BindingOperations.GetMultiBinding(textBox, Control.BackgroundProperty) is MultiBinding multiBinding)
        {
            return multiBinding.Clone();
        }

        if (BindingOperations.GetPriorityBinding(textBox, Control.BackgroundProperty) is PriorityBinding priorityBinding)
        {
            return priorityBinding.Clone();
        }

        return null;
    }
}
