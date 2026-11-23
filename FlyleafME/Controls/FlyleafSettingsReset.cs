using System.Runtime.CompilerServices;
using System.Windows;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

/// <summary>Optional application-defined reset values for Player settings.</summary>
public static class FlyleafSettingsReset
{
    public static readonly DependencyProperty DefaultsProperty = DependencyProperty.RegisterAttached(
        "Defaults", typeof(Config), typeof(FlyleafSettingsReset),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetDefaults(DependencyObject element, Config? defaults)
        => element.SetValue(DefaultsProperty, defaults);

    static readonly ConditionalWeakTable<FrameworkElement, Config> libraryDefaults = new();

    /// <summary>Reads the already-resolved reset configuration; falls back to FlyleafLib defaults.</summary>
    public static Config GetDefaults(FrameworkElement editor)
    {
        Config Resolve() => (Config?)editor.GetValue(DefaultsProperty)
            ?? libraryDefaults.GetValue(editor, _ => new Config());
        return editor.Dispatcher.CheckAccess() ? Resolve() : editor.Dispatcher.Invoke(Resolve);
    }
}
