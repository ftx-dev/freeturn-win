using System;
using System.Windows;
using System.Windows.Controls;
using static FreeTurnX.Strings;

namespace FreeTurnX.Views;

public sealed class AppDialog : Window
{
    public bool Remember { get; private set; }
    public bool HasChoice { get; private set; }
    public AppDialog(string title, string message, string accept, string cancel, bool remember = false)
    {
        Title = title;
        Width = Math.Min(480, SystemParameters.WorkArea.Width - 32);
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Application.Current.MainWindow is { IsVisible: true } owner) Owner = owner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (message.Length > 0) panel.Children.Add(new ScrollViewer { MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 16) } });
        var check = new CheckBox { Content = L("Запомнить мой выбор"), Margin = new Thickness(0, 20, 0, 16), Visibility = remember ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(check);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        if (cancel.Length > 0) buttons.Children.Add(Ui.Button(cancel, () => { HasChoice = true; Remember = check.IsChecked == true; DialogResult = false; }));
        buttons.Children.Add(Ui.Button(accept, () => { HasChoice = true; Remember = check.IsChecked == true; DialogResult = true; }, accent: true));
        panel.Children.Add(buttons);
        var frame = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Child = panel };
        frame.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        frame.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        Content = frame;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }
}
