using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FreeTurnX.Views;

/// <summary>Кирпичики форм в духе настроек Windows 11: карточка, строка «подпись - элемент», сноска.</summary>
static class Ui
{
    public static bool Compact => ((App)Application.Current).Model.Settings.CompactMode;
    public static void FadeIn(UIElement element)
    {
        if (!Motion.Allowed) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(130)) { FillBehavior = FillBehavior.Stop });
    }

    public static Button ActionIcon(string label, string path, Action action, Brush? color = null)
    {
        var ink = color ?? new SolidColorBrush(Color.FromRgb(70, 145, 220));
        var geometry = Geometry.Parse(path).Clone();
        var bounds = geometry.Bounds;
        geometry.Transform = new TranslateTransform(12 - bounds.X - bounds.Width / 2, 12 - bounds.Y - bounds.Height / 2);
        geometry.Freeze();
        var drawing = new Canvas { Width = 24, Height = 24 };
        drawing.Children.Add(new System.Windows.Shapes.Path { Data = geometry, Stroke = ink, StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round });
        var icon = new Viewbox { Width = 20, Height = 20, Child = drawing };
        var button = new Button { Content = icon, ToolTip = label, Width = 36, Height = 36, Padding = new Thickness(6), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), BorderBrush = ink, BorderThickness = new Thickness(1) };
        button.SetResourceReference(Control.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        button.Foreground = ink;
        button.Style = (Style)Application.Current.Resources["ActionIconButton"];
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    public static TextBlock Header(string text) => new() { Text = text, Style = (Style)Application.Current.Resources["SectionHeader"] };

    public static TextBlock Hint(string text, Brush? color = null)
    {
        var t = new TextBlock { Text = text, Style = (Style)Application.Current.Resources["Hint"] };
        if (color is not null) { t.Foreground = color; t.Opacity = 1; }
        return t;
    }

    public static TextBlock Glyph(string glyph, double size = 16) =>
        new() { Text = glyph, Style = (Style)Application.Current.Resources["Glyph"], FontSize = size };

    public static Border Card(params UIElement[] rows)
    {
        var panel = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) panel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Opacity = 0.4 });
            panel.Children.Add(rows[i]);
        }
        return new Border { Style = (Style)Application.Current.Resources["Card"], Child = panel };
    }

    /// <summary>Строка: заголовок и (по желанию) серое описание слева, элемент справа.</summary>
    public static Grid Row(string title, UIElement control, string? description = null, Brush? descriptionColor = null)
    {
        var grid = new Grid { MinHeight = 32 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Editable text must not measure at its full content width: a long URL
        // otherwise consumes the label column and wraps its title vertically.
        if (control is TextBox or PasswordBox)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions[1].Width = new GridLength(2, GridUnitType.Star);
            ((FrameworkElement)control).MinWidth = 0;
        }
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(description))
        {
            // Описание - серая вторая строка под заголовком, без разделителя (как в настройках Windows 11).
            var d = new TextBlock { Text = description, FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            if (descriptionColor is not null) { d.Foreground = descriptionColor; d.Opacity = 1; }
            left.Children.Add(d);
        }
        grid.Children.Add(left);
        if (control is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        if (Compact && control is not CheckBox)
        {
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(control, 0);
            Grid.SetRow(control, 1);
            left.Margin = new Thickness(0, 0, 0, 8);
        }
        return grid;
    }

    public static CheckBox Switch(bool value, Action<bool> changed, bool enabled = true)
    {
        var box = new CheckBox { IsChecked = value, IsEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
        return box;
    }

    public static ComboBox Choice<T>(T value, (T value, string title)[] options, Action<T> changed, double width = 220)
    {
        var combo = new ComboBox { MinWidth = Compact ? Math.Min(width, 260) : width };
        foreach (var (v, title) in options) combo.Items.Add(new ComboBoxItem { Content = title, Tag = v });
        combo.SelectedIndex = Array.FindIndex(options, o => Equals(o.value, value));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: T v }) changed(v); };
        return combo;
    }

    public static TextBox Field(string value, Action<string> changed, double width = 320, string? placeholder = null)
    {
        var box = new TextBox { Text = value, MinWidth = Compact ? Math.Min(width, 240) : width, MaxWidth = 480 };
        if (placeholder is not null) box.ToolTip = placeholder;
        box.TextChanged += (_, _) => changed(box.Text);
        return box;
    }

    public static Button Button(string title, Action click, bool accent = false, bool danger = false)
    {
        var b = new Button { Content = title, Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        if (accent && Application.Current.TryFindResource("SmoothAccentButton") is Style style) b.Style = style;
        if (danger) { b.Foreground = Red; b.BorderBrush = Red; }
        b.Click += (_, _) => click();
        return b;
    }

    public static ScrollViewer Page(params UIElement[] children)
    {
        var panel = new StackPanel { Margin = new Thickness(Compact ? 12 : 24, 8, Compact ? 12 : 24, 24), MaxWidth = 860 };
        foreach (var c in children) panel.Children.Add(c);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public static readonly Brush Red = new SolidColorBrush(Color.FromRgb(220, 60, 55));
    public static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(225, 135, 20));
    public static readonly Brush Green = new SolidColorBrush(Color.FromRgb(30, 170, 100));
}
