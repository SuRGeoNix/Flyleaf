using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;
/// <summary>Installed plugin options with optional application-supplied presentation metadata.</summary>
public sealed class FlyleafPluginSettings : FlyleafSettingsEditor
{
    /// <summary>Override a plugin option's presentation; return null to use built-in/type-derived metadata.</summary>
    public Func<string, string, FlyleafPluginOptionDescription?>? DescribeOption { get; set; }
    string? selectedPlugin;
    readonly Button pluginSelector = Button("");
    readonly Dictionary<string, StackPanel> pluginBodies = new(StringComparer.Ordinal);
    ContextMenu? pluginMenu;
    public FlyleafPluginSettings()
    {
        pluginSelector.HorizontalAlignment = HorizontalAlignment.Left;
        pluginSelector.Margin = new Thickness(0, 12, 0, 6);
        pluginSelector.Click += (_, _) =>
        {
            if (pluginMenu != null) pluginMenu.IsOpen = false;
            var menu = new ContextMenu { PlacementTarget = pluginSelector, Placement = PlacementMode.Bottom };
            menu.SetResourceReference(StyleProperty, "FlyleafME.ThemeContextMenuStyle");
            foreach (var name in pluginBodies.Keys)
            {
                var item = new MenuItem { Header = PluginTitle(name), IsCheckable = true, IsChecked = name == selectedPlugin };
                item.Click += (_, _) =>
                {
                    selectedPlugin = name;
                    if (Context?.UIConfig is { } uiConfig) uiConfig.SelectedPlugin = name;
                    ShowSelectedPlugin();
                };
                menu.Items.Add(item);
            }
            pluginMenu = menu; menu.IsOpen = true;
        };
        Unloaded += (_, _) => { if (pluginMenu != null) pluginMenu.IsOpen = false; pluginMenu = null; };
    }
    static string PluginTitle(string name) => name == "BitSwarmPlugin" ? "BitSwarm" : name;
    void ShowSelectedPlugin()
    {
        pluginSelector.Content = PluginTitle(selectedPlugin ?? "Plugin") + " ▾";
        foreach (var (name, body) in pluginBodies) body.Visibility = name == selectedPlugin ? Visibility.Visible : Visibility.Collapsed;
    }
    protected override NotifyPropertyChanged SelectConfig(Player target) => target.Config;
    protected override void Build()
    {
        BeginPage("Plugins");
        if (pluginMenu != null) pluginMenu.IsOpen = false;
        pluginMenu = null; pluginBodies.Clear();
        if (player == null) return;
        var configured = player.Config.Plugins;
        var installed = Engine.Plugins.Types.Values.Where(t => configured.TryGetValue(t.Name, out var options) && options.Count > 0).OrderBy(t => t.Name).ToArray();
        if (installed.Length == 0)
        {
            Children.Add(Text("No configurable external plugins were loaded. Plugins are discovered at application startup; restart after installing or updating a plugin.", true));
            return;
        }
        Children.Add(Text("Changes are available to subsequent plugin operations. Some plugins also apply changes immediately. Editing does not restart an extraction or reopen media.", true));
        var remembered = Context?.UIConfig is { } uiConfig ? uiConfig.SelectedPlugin : selectedPlugin;
        selectedPlugin = installed.FirstOrDefault(t => t.Name == remembered)?.Name ?? installed[0].Name;
        Children.Add(pluginSelector);
        foreach (var pluginType in installed)
        {
            var pluginBody = new StackPanel(); pluginBodies.Add(pluginType.Name, pluginBody); Children.Add(pluginBody);
            var values = configured[pluginType.Name];
            Observe(values);
            Dictionary<string, string> defaults;
            try
            {
                using var instance = PluginHandler.CreatePluginInstance(pluginType);
                defaults = new(instance.GetDefaultOptions(), StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                var fallback = Group(pluginType.Name, null, pluginBody);
                fallback.Children.Add(Text("Option descriptions are unavailable: " + ex.Message, true));
                Options(fallback, () => new(values), v => UpdateUnknown(values, v, []), "Plugin options · name / value");
                continue;
            }
            var pluginTitle = PluginTitle(pluginType.Name);
            pluginBody.Children.Add(Text($"Version {pluginType.Version}", true));
            var declared = pluginType.Type.GetNestedTypes(BindingFlags.Public)
                .Where(t => t.Name.EndsWith("Options", StringComparison.Ordinal))
                .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First().PropertyType, StringComparer.Ordinal);
            var descriptions = defaults.Select(pair => (pair.Key, Default: pair.Value,
                Info: DescribeOption?.Invoke(pluginType.Name, pair.Key) ?? FlyleafPluginOptionDescriptions.Describe(pluginType.Name, pair.Key, pair.Value, declared.GetValueOrDefault(pair.Key)))).ToArray();
            var advanced = new StackPanel();
            foreach (var group in descriptions.GroupBy(d => d.Info.Group))
            {
                var entries = group.ToArray();
                var section = Group(group.Key, () =>
                {
                    var reset = FlyleafSettingsReset.GetDefaults(this).Plugins;
                    var source = reset.TryGetValue(pluginType.Name, out var options) ? options : null;
                    foreach (var option in entries) values[option.Key] = source?.GetValueOrDefault(option.Key) ?? option.Default;
                }, group.Key == "Advanced" ? advanced : pluginBody);
                foreach (var option in entries)
                {
                    string Read() => values.GetValueOrDefault(option.Key) ?? option.Default;
                    void Write(string value) => values[option.Key] = value;
                    var info = option.Info;
                    string? invalid = null;
                    if (info.Kind == FlyleafPluginOptionKind.Boolean && !bool.TryParse(Read(), out _)) invalid = "Stored value is not true/false.";
                    bool ValidInteger(string text) => FlyleafSettingsValues.TryInteger(text, info.Minimum, info.Maximum, out var v) && (info.MultipleOf <= 1 || v % info.MultipleOf == 0);
                    if (info.Kind == FlyleafPluginOptionKind.Integer && !ValidInteger(Read())) invalid = "Stored value is outside the supported numeric range or increment.";
                    if (invalid != null)
                    {
                        TextEntry(section, info.Label, Read, Write, v => info.Kind == FlyleafPluginOptionKind.Boolean
                            ? bool.TryParse(v, out _) ? null : "Enter true or false."
                            : ValidInteger(v) ? null : $"Enter an integer from {info.Minimum} to {info.Maximum}" + (info.MultipleOf > 1 ? $", in multiples of {info.MultipleOf}." : "."));
                        section.Children.Add(Text(invalid + " Correct it or reset this group.", true));
                    }
                    else switch (info.Kind)
                    {
                        case FlyleafPluginOptionKind.Boolean:
                            Toggle(section, info.Label, info.Help, () => bool.TryParse(Read(), out var v) && v, v => Write(v ? "true" : "false")); break;
                        case FlyleafPluginOptionKind.Integer:
                            Number(section, info.Label, info.Unit, () => long.TryParse(Read(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0,
                                v => Write(((long)v).ToString(CultureInfo.InvariantCulture)), info.Minimum, info.Maximum,
                                v => v != Math.Truncate(v) ? "Enter a whole number." : info.MultipleOf > 1 && v % info.MultipleOf != 0 ? $"Use a multiple of {info.MultipleOf}." : null); break;
                        case FlyleafPluginOptionKind.Choice when info.Choices is { Length: > 0 } choices:
                            Choice(section, info.Label, Read, Write, choices.Select(c => (c, c)).ToArray()); break;
                        case FlyleafPluginOptionKind.Folder:
                            Folder(section, info.Label, Read, Write, allowRelative: true); break;
                        case FlyleafPluginOptionKind.File:
                            FilePath(section, info.Label, Read, Write); break;
                        default:
                            TextEntry(section, info.Label, Read, Write, trim: false); break;
                    }
                    if (info.Kind != FlyleafPluginOptionKind.Boolean && info.Help.Length > 0) section.Children.Add(Text(info.Help, true));
                }
            }
            var extra = Group("Additional options", null, advanced);
            Options(extra, () => values.Where(p => !defaults.ContainsKey(p.Key)).ToDictionary(p => p.Key, p => p.Value),
                v => UpdateUnknown(values, v, defaults.Keys.ToHashSet(StringComparer.Ordinal)), "Unrecognized options · name / value");
            var expander = new Expander { Header = Text(pluginTitle + " · Advanced"), Content = advanced, Margin = new Thickness(0, 18, 0, 0) };
            expander.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush"); pluginBody.Children.Add(expander);
        }
        ShowSelectedPlugin();
        Refresh();
    }
    static void UpdateUnknown(Utils.ObservableDictionary<string, string> values, Dictionary<string, string> replacement, HashSet<string> known)
    {
        if (replacement.Keys.Any(known.Contains)) throw new ArgumentException("Use the named editor above for a known plugin option.");
        foreach (var key in values.Keys.Where(k => !known.Contains(k) && !replacement.ContainsKey(k)).ToArray()) values.Remove(key);
        foreach (var pair in replacement) values[pair.Key] = pair.Value;
    }
}
