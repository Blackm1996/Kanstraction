using System;
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
            return CloneBinding(binding);
        }

        if (BindingOperations.GetMultiBinding(textBox, Control.BackgroundProperty) is MultiBinding multiBinding)
        {
            return CloneMultiBinding(multiBinding);
        }

        if (BindingOperations.GetPriorityBinding(textBox, Control.BackgroundProperty) is PriorityBinding priorityBinding)
        {
            return ClonePriorityBinding(priorityBinding);
        }

        return null;
    }

    private static BindingBase CloneBindingBase(BindingBase bindingBase)
    {
        return bindingBase switch
        {
            Binding binding => CloneBinding(binding),
            MultiBinding multiBinding => CloneMultiBinding(multiBinding),
            PriorityBinding priorityBinding => ClonePriorityBinding(priorityBinding),
            _ => throw new NotSupportedException($"Unsupported binding type '{bindingBase.GetType()}' for background property cloning.")
        };
    }

    private static Binding CloneBinding(Binding binding)
    {
        var clone = new Binding
        {
            BindsDirectlyToSource = binding.BindsDirectlyToSource,
            Mode = binding.Mode,
            UpdateSourceTrigger = binding.UpdateSourceTrigger,
            IsAsync = binding.IsAsync,
            AsyncState = binding.AsyncState,
            NotifyOnSourceUpdated = binding.NotifyOnSourceUpdated,
            NotifyOnTargetUpdated = binding.NotifyOnTargetUpdated,
            NotifyOnValidationError = binding.NotifyOnValidationError,
            ValidatesOnDataErrors = binding.ValidatesOnDataErrors,
            ValidatesOnExceptions = binding.ValidatesOnExceptions,
            ValidatesOnNotifyDataErrors = binding.ValidatesOnNotifyDataErrors,
            StringFormat = binding.StringFormat,
            TargetNullValue = binding.TargetNullValue,
            FallbackValue = binding.FallbackValue,
            BindingGroupName = binding.BindingGroupName,
            UpdateSourceExceptionFilter = binding.UpdateSourceExceptionFilter,
            Delay = binding.Delay
        };

        if (binding.Path is not null)
        {
            clone.Path = binding.Path;
        }

        if (!string.IsNullOrEmpty(binding.XPath))
        {
            clone.XPath = binding.XPath;
        }

        if (binding.Converter is not null)
        {
            clone.Converter = binding.Converter;
        }

        if (binding.ConverterParameter is not null)
        {
            clone.ConverterParameter = binding.ConverterParameter;
        }

        if (binding.ConverterCulture is not null)
        {
            clone.ConverterCulture = binding.ConverterCulture;
        }

        if (binding.Source is not null)
        {
            clone.Source = binding.Source;
        }
        else if (binding.RelativeSource is not null)
        {
            clone.RelativeSource = binding.RelativeSource;
        }
        else if (!string.IsNullOrEmpty(binding.ElementName))
        {
            clone.ElementName = binding.ElementName;
        }

        foreach (var rule in binding.ValidationRules)
        {
            clone.ValidationRules.Add(rule);
        }

        return clone;
    }

    private static MultiBinding CloneMultiBinding(MultiBinding multiBinding)
    {
        var clone = new MultiBinding
        {
            Mode = multiBinding.Mode,
            UpdateSourceTrigger = multiBinding.UpdateSourceTrigger,
            Converter = multiBinding.Converter,
            ConverterParameter = multiBinding.ConverterParameter,
            ConverterCulture = multiBinding.ConverterCulture,
            StringFormat = multiBinding.StringFormat,
            TargetNullValue = multiBinding.TargetNullValue,
            FallbackValue = multiBinding.FallbackValue,
            BindingGroupName = multiBinding.BindingGroupName,
            NotifyOnSourceUpdated = multiBinding.NotifyOnSourceUpdated,
            NotifyOnTargetUpdated = multiBinding.NotifyOnTargetUpdated,
            NotifyOnValidationError = multiBinding.NotifyOnValidationError,
            ValidatesOnDataErrors = multiBinding.ValidatesOnDataErrors,
            ValidatesOnExceptions = multiBinding.ValidatesOnExceptions,
            ValidatesOnNotifyDataErrors = multiBinding.ValidatesOnNotifyDataErrors
        };

        foreach (var childBinding in multiBinding.Bindings)
        {
            clone.Bindings.Add(CloneBindingBase(childBinding));
        }

        foreach (var rule in multiBinding.ValidationRules)
        {
            clone.ValidationRules.Add(rule);
        }

        return clone;
    }

    private static PriorityBinding ClonePriorityBinding(PriorityBinding priorityBinding)
    {
        var clone = new PriorityBinding
        {
            StringFormat = priorityBinding.StringFormat,
            TargetNullValue = priorityBinding.TargetNullValue,
            FallbackValue = priorityBinding.FallbackValue,
            BindingGroupName = priorityBinding.BindingGroupName
        };

        foreach (var childBinding in priorityBinding.Bindings)
        {
            clone.Bindings.Add(CloneBindingBase(childBinding));
        }

        return clone;
    }
}
