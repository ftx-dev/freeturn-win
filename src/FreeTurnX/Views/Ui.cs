using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeTurnX.Views;

/// <summary>Кирпичики форм в духе настроек Windows 11: карточка, строка «подпись - элемент», сноска.</summary>
static class Ui
{
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
        var combo = new ComboBox { MinWidth = width };
        foreach (var (v, title) in options) combo.Items.Add(new ComboBoxItem { Content = title, Tag = v });
        combo.SelectedIndex = Array.FindIndex(options, o => Equals(o.value, value));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: T v }) changed(v); };
        return combo;
    }

    public static TextBox Field(string value, Action<string> changed, double width = 320, string? placeholder = null)
    {
        var box = new TextBox { Text = value, MinWidth = width, MaxWidth = 480 };
        if (placeholder is not null) box.ToolTip = placeholder;
        box.TextChanged += (_, _) => changed(box.Text);
        return box;
    }

    public static Button Button(string title, Action click, bool accent = false)
    {
        var b = new Button { Content = title, Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        if (accent && Application.Current.TryFindResource("AccentButtonStyle") is Style style) b.Style = style;
        b.Click += (_, _) => click();
        return b;
    }

    public static ScrollViewer Page(params UIElement[] children)
    {
        var panel = new StackPanel { Margin = new Thickness(24, 8, 24, 24), MaxWidth = 860 };
        foreach (var c in children) panel.Children.Add(c);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public static readonly Brush Red = new SolidColorBrush(Color.FromRgb(220, 60, 55));
    public static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(225, 135, 20));
    public static readonly Brush Green = new SolidColorBrush(Color.FromRgb(30, 170, 100));
}
