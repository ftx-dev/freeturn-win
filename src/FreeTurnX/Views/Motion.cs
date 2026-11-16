using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace FreeTurnX.Views;

public static class Motion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Motion), new FrameworkPropertyMetadata(true));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    public static bool Allowed => SystemParameters.ClientAreaAnimation && !((App)Application.Current).Model.Settings.DisableAnimations;

    public static void Animate(Animatable target, DependencyProperty property, double value, int milliseconds = 140)
    {
        if (!Allowed) { target.BeginAnimation(property, null); target.SetValue(property, value); return; }
        var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(milliseconds))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
