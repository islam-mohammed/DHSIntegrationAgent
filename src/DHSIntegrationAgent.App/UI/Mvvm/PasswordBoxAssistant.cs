using System.Windows;
using System.Windows.Controls;

namespace DHSIntegrationAgent.App.UI.Mvvm;

/// <summary>
/// Enables binding PasswordBox.Password to a ViewModel string.
/// WPF intentionally does not allow direct binding to Password for security reasons.
/// This helper keeps the approach contained and predictable.
/// </summary>
public static class PasswordBoxAssistant
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxAssistant),
            new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

    public static string GetBoundPassword(DependencyObject obj)
        => (string)obj.GetValue(BoundPasswordProperty);

    public static void SetBoundPassword(DependencyObject obj, string value)
        => obj.SetValue(BoundPasswordProperty, value);

    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxAssistant),
            new PropertyMetadata(false, OnBindPasswordChanged));

    public static bool GetBindPassword(DependencyObject obj)
        => (bool)obj.GetValue(BindPasswordProperty);

    public static void SetBindPassword(DependencyObject obj, bool value)
        => obj.SetValue(BindPasswordProperty, value);

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxAssistant),
            new PropertyMetadata(false));

    private static bool GetIsUpdating(DependencyObject obj)
        => (bool)obj.GetValue(IsUpdatingProperty);

    private static void SetIsUpdating(DependencyObject obj, bool value)
        => obj.SetValue(IsUpdatingProperty, value);

    private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb) return;

        if ((bool)e.OldValue)
            pb.PasswordChanged -= HandlePasswordChanged;

        if ((bool)e.NewValue)
            pb.PasswordChanged += HandlePasswordChanged;
    }

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox pb) return;
        if (!GetBindPassword(pb)) return;

        pb.PasswordChanged -= HandlePasswordChanged;

        var newValue = e.NewValue as string ?? "";
        if (!GetIsUpdating(pb) && pb.Password != newValue)
            pb.Password = newValue;

        pb.PasswordChanged += HandlePasswordChanged;
    }

    private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
    {
        var pb = (PasswordBox)sender;

        SetIsUpdating(pb, true);
        SetBoundPassword(pb, pb.Password);
        SetIsUpdating(pb, false);
    }
}
