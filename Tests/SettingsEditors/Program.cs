using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Flyleaf.Controls.WPF;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;
using FlyleafLib.MediaFramework.MediaRenderer;
using Flyleaf;
using PlayerKeyBinding = FlyleafLib.MediaPlayer.KeyBinding;
using HostTargets = FlyleafLib.Controls.WPF.AvailableWindows;

internal static class Program
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length is 2 or 3 && args[0] == "--queue-images") { QueueImageChecks.Run(args[1], args.Length == 3 ? args[2] : null); return; }
        Check(FlyleafSettingsValues.TryOptions([new(" user_agent ", "  Agent / 1.0  "), new("empty", ""), new("", "")], out var options, out _), "Valid options rejected");
        Check(options.Count == 2 && options["user_agent"] == "  Agent / 1.0  " && options["empty"] == "", "Values changed or blank draft saved");
        Check(!FlyleafSettingsValues.TryOptions([new("a", "1"), new(" a ", "2")], out _, out _), "Duplicate names accepted");
        Check(!FlyleafSettingsValues.TryOptions([new(" ", "1")], out _, out _), "Missing name accepted");
        Check(!FlyleafSettingsValues.TryOptions([new("token", "a\0b")], out _, out _), "Native null accepted");
        Check(FlyleafSettingsValues.TryOptions([new("A", "1"), new("a", "2")], out _, out _), "Case-sensitive option names merged");
        Check(FlyleafSettingsValues.TryAudioFilters([("volume", "a", "  volume=0.5  "), ("volume", "b", "volume=1")], out var filters, out _), "Repeated filter with unique IDs rejected");
        Check(filters[0].Args == "  volume=0.5  " && filters[1].Id == "b", "Filter order/arguments changed");
        Check(!FlyleafSettingsValues.TryAudioFilters([("volume", "a", ""), ("volume", "a", "")], out _, out _), "Duplicate filter ID accepted");
        Check(!FlyleafSettingsValues.TryAudioFilters([("", "", "volume=1")], out _, out _), "Missing filter name accepted");
        Check(FlyleafSettingsValues.TryAudioFilters([("", "", ""), ("volume", "", "1")], out filters, out _) && filters.Count == 1, "Blank filter draft saved");
        Check(!FlyleafSettingsValues.TryAudioFilters([("volume;bad", "", "")], out _, out _), "Invalid filter name accepted");
        Check(FlyleafPluginOptionDescriptions.Describe("Example", "Enabled", "true", null).Kind == FlyleafPluginOptionKind.Boolean, "Boolean default not recognized");
        Check(FlyleafPluginOptionDescriptions.Describe("Example", "Count", "1", typeof(int)).Kind == FlyleafPluginOptionKind.Integer, "Declared integer not recognized");
        Check(FlyleafPluginOptionDescriptions.Describe("Example", "Code", "001", typeof(string)).Kind == FlyleafPluginOptionKind.Text, "String incorrectly inferred as number");
        Check(FlyleafPluginOptionDescriptions.Describe("Example", "Mode", "Dark", typeof(FlyleafTheme)).Choices!.Contains("System"), "Declared enum choices missing");
        var batch = FlyleafPluginOptionDescriptions.Describe("YTDLP", "PlaylistBatchSize", "20", null);
        Check(batch.Minimum == 1 && batch.Maximum == 500 && batch.Unit == "items", "YTDLP range/units lost");
        Check(FlyleafPluginOptionDescriptions.Describe("YTDLP", "BrowserCookiesSource", "Auto", null).Choices!.SequenceEqual(["Auto", "Chrome", "Brave"]), "Browser choices lost");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check(FlyleafSettingsValues.TryInteger("120", 1, 200, out var number) && number == 120, "Invariant plugin integer failed");
            Check(!FlyleafSettingsValues.TryInteger("1,5", 0, 200, out _), "Fractional plugin integer accepted");
            Check(!FlyleafSettingsValues.TryInteger("201", 0, 200, out _), "Out-of-range plugin integer accepted");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }

        var binding = new PlayerKeyBinding { Action = KeyBindingAction.Custom, ActionName = "app.open", Key = Key.O };
        Action callback = () => { };
        binding.SetAction(callback, true);
        var gesture = new FlyleafShortcutGesture(Key.P, Ctrl: true, IsKeyUp: true);
        gesture.Apply(binding);
        Check(binding.ActionName == "app.open" && binding.IsKeyUp && binding.Ctrl && binding.Key == Key.P, "Gesture edit changed action identity");
        Check(ReferenceEquals(typeof(PlayerKeyBinding).GetField("ActionInternal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(binding), callback), "Gesture edit discarded application delegate");
        Check(gesture.Conflict([binding]) == binding && gesture.Conflict([binding], binding) == null, "Conflict/self-conflict wrong");
        Check(new FlyleafShortcutGesture(Key.P, Ctrl: true, IsKeyUp: false).Conflict([binding]) == binding, "Router's key-down/up collision ignored");
        Check(new FlyleafShortcutGesture(Key.P, Shift: true).Conflict([binding]) == null, "Different modifiers reported as conflict");
        Check(new FlyleafShortcutGesture(Key.None).Conflict([new() { Key = Key.None }]) == null, "Unassigned entries conflict");
        Check(FlyleafTrackMenus.TrackLabel("2", Language.Get("eng"), "Commentary", "subrip") == "2 · English · Commentary · subrip",
            "Known subtitle languages are missing from track labels");
        Check(FlyleafTrackMenus.TrackLabel("Track 1", Language.Unknown, "Movie.srt", null) == "Track 1 · Unknown · Movie.srt",
            "External subtitle labels must start with the track number");
        Check(FlyleafTrackMenus.TrackLabel("3", Language.Get(""), null, "ass") == "3 · Unknown · ass",
            "Blank subtitle language metadata did not fall back to Unknown");
        Check(FlyleafTrackMenus.TrackLabel("1", Language.Get("ell"), null, "aac") == "1 · Greek · aac",
            "Audio labels are missing recognized languages");
        Check(FlyleafTrackMenus.VideoTrackLabel("3", 1080, "h264", title: "Main") == "3 · 1080p · h264 · Main",
            "Embedded video labels must start with the one-based track number");
        Check(FlyleafTrackMenus.VideoTrackLabel("Track 2", 720, "h264", protocol: "hls") == "Track 2 · 720p · hls · h264",
            "External video labels must keep the number, resolution and protocol in order");
        CheckControlShortcuts();
        CheckManualDelayEntry();
        Check(FlyleafMediaInformation.Time(TimeSpan.FromMilliseconds(3723456).Ticks) == "01:02:03.456",
            "Information timestamps are missing millisecond precision");
        CheckDockRatioSizing();
        CheckRatioResizeViewport();
        CheckOriginalSizeViewport();
        CheckSharedPanelLayout();
        CheckSingleInstanceLaunch();
        CheckFolderImport();
        CheckFileAssociations();
        CheckQueueSort();

        Check(FlyleafLanguageSuggestions.Find("fre", []).Any(c => c.Value == FlyleafLib.Language.Get("fr")), "Library ISO alias missing from autocomplete");
        Check(FlyleafLanguageSuggestions.Find("Ελληνικά", []).Any(c => c.Value == FlyleafLib.Language.Get("el")), "Native language name missing from autocomplete");
        Check(FlyleafLanguageSuggestions.Find("pob", []).Any(c => c.Value.CultureName == "pt-BR"), "Library Brazilian Portuguese alias missing");
        Check(FlyleafLanguageSuggestions.Find("und", []).First().Value == FlyleafLib.Language.Unknown, "Unknown-language choice missing");
        Check(!FlyleafLanguageSuggestions.Find("eng", [FlyleafLib.Language.English]).Any(c => c.Value == FlyleafLib.Language.English), "Already selected language suggested again");
        Check(FlyleafLanguageSuggestions.Find("en", [], 4).Count <= 4, "Autocomplete result limit ignored");

        var windowDefaults = new Flyleaf.AppConfig();
        Check(windowDefaults.Window.Width == 900 && windowDefaults.Window.Height == 560, "Initial window dimensions did not use compact defaults");
        Check(windowDefaults.Window.KeepWindowRatioOnResize, "New windows did not default to ratio-preserving resize");
        windowDefaults.Window.Width = 780; windowDefaults.Window.Height = 500;
        var windowRoundTrip = Flyleaf.AppConfig.Deserialize(windowDefaults.Serialize());
        Check(windowRoundTrip.Window.Width == 780 && windowRoundTrip.Window.Height == 500, "Explicitly saved window dimensions did not round-trip");
        var ui = new FlyleafUIConfig(); ui.Mouse.ZoomOnCtrlWheel = HostTargets.Both; ui.Mouse.RotateOnShiftWheel = HostTargets.None;
        var copyUI = ui.Clone(); copyUI.Mouse.ZoomOnCtrlWheel = HostTargets.Overlay;
        Check(ui.Mouse.ZoomOnCtrlWheel == HostTargets.Both, "Mouse UI clone shares mutable preferences");
        ui.SelectedPlugin = "ExamplePlugin";
        Check(ui.Clone().SelectedPlugin == ui.SelectedPlugin, "Cloning UI defaults lost the selected plugin");
        var restoredUI = FlyleafUIConfigSerializer.Deserialize(FlyleafUIConfigSerializer.Serialize(ui));
        Check(restoredUI.SelectedPlugin == ui.SelectedPlugin, "Saving UI defaults lost the selected plugin");
        Check(restoredUI.Mouse.ZoomOnCtrlWheel == HostTargets.Both && restoredUI.Mouse.RotateOnShiftWheel == HostTargets.None, "Mouse choices lost on save/load");
        var olderUI = FlyleafUIConfigSerializer.Deserialize("{\"SchemaVersion\":1,\"UIConfig\":{}}");
        Check(olderUI.SelectedPlugin == null, "Older UI documents should leave plugin selection unset");
        ui.Subtitles.FontFamily = "Arial"; ui.Subtitles.Color = "#FFFFE8C0"; ui.Subtitles.OutlineThickness = 0; ui.Subtitles.BottomOffset = 72;
        ui.OSD.DurationSeconds = 2; ui.OSD.Position = FlyleafOSDPosition.BottomLeft;
        var textRoundTrip = FlyleafUIConfigSerializer.Deserialize(FlyleafUIConfigSerializer.Serialize(ui));
        Check(textRoundTrip.Subtitles.FontFamily == "Arial" && textRoundTrip.Subtitles.Color == "#FFFFE8C0" && textRoundTrip.Subtitles.OutlineThickness == 0 && textRoundTrip.Subtitles.BottomOffset == 72 && textRoundTrip.OSD.DurationSeconds == 2 && textRoundTrip.OSD.Position == FlyleafOSDPosition.BottomLeft, "Text/OSD preferences did not survive serialization");
        var textClone = ui.Clone(); textClone.Subtitles.FontSize = 80; textClone.OSD.Enabled = false;
        Check(ui.Subtitles.FontSize == 48 && ui.OSD.Enabled, "Text/OSD clone shares mutable settings");
        Check(olderUI.Subtitles.FontSize == 48 && olderUI.Subtitles.OutlineThickness == 1 && olderUI.Subtitles.WithinVideo && olderUI.OSD.Enabled && olderUI.OSD.FontSize == 16 && olderUI.OSD.DurationSeconds == 3.5, "Older UI document lost text/OSD defaults");
        foreach (Action invalid in new Action[] { () => ui.Subtitles.FontSize = double.NaN, () => ui.Subtitles.OutlineThickness = -1, () => ui.Subtitles.Color = "not a color", () => ui.OSD.DurationSeconds = double.PositiveInfinity, () => ui.OSD.Position = (FlyleafOSDPosition)99 })
        {
            bool rejected = false; try { invalid(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid overlay setting accepted");
        }
        int textNotifications = 0; ui.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(FlyleafUIConfig.Subtitles) or nameof(FlyleafUIConfig.OSD)) textNotifications++; };
        ui.Subtitles.Bold = false; ui.OSD.FontSize = 20;
        Check(textNotifications == 2, "Nested text preferences do not notify their UI owner");
        Check(olderUI.Mouse.PanOnCtrlDrag == HostTargets.Surface && olderUI.Mouse.FullscreenOnDoubleClick == HostTargets.Both, "Older UI file changed gesture defaults");
        Check(olderUI.Appearance.Theme == FlyleafTheme.Dark && olderUI.Appearance.Accent == FlyleafAccent.Firebrick,
            "Omitted appearance values did not inherit Dark and Firebrick");
        foreach (var mode in new[] { FlyleafTheme.Dark, FlyleafTheme.Light })
            Check(FlyleafThemeManager.GetAccentColor(mode, FlyleafAccent.Firebrick) == Color.FromRgb(178, 34, 34), "Firebrick changed from the requested #B22222");
        foreach (var accent in Enum.GetValues<FlyleafAccent>())
        {
            ui.Appearance.Accent = accent;
            Check(FlyleafUIConfigSerializer.Deserialize(FlyleafUIConfigSerializer.Serialize(ui)).Appearance.Accent == accent, "Accent does not survive saving UI defaults: " + accent);
        }
        Check(new FlyleafSettingsPanel().Pages.Any(p => p.Id == "plugins" && p.IsAvailable != null), "Plugins category lacks its availability condition");
        var orderedSettings = new FlyleafSettingsPanel();
        orderedSettings.Pages.Insert(0, new("application", "Application", _ => new StackPanel()));
        Check(orderedSettings.Pages.First().Id == "application" && orderedSettings.Pages.Last().Id == "keybindings" && orderedSettings.Pages.Last().Title == "Bindings", "Application/Bindings navigation order is wrong");
        Check(!string.IsNullOrWhiteSpace(FlyleafSettingsBranding.LibraryVersion) && FlyleafSettingsBranding.Copyright == typeof(Player).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright, "Settings branding is missing the core library metadata");

        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository != null && !File.Exists(Path.Combine(repository.FullName, "Flyleaf", "Flyleaf.csproj"))) repository = repository.Parent;
        if (repository == null) throw new Exception("Cannot locate Flyleaf plugin output");
        foreach (var plugin in new[] { "YTDLP", "BitSwarm" })
        {
            var directory = Path.Combine(repository.FullName, "Flyleaf", "bin", "InformationCheck", "Plugins", plugin);
            string dll = Path.Combine(directory, "FlyleafLib.Plugins." + plugin + ".dll");
            Check(File.Exists(dll), plugin + " package DLL missing from Flyleaf output");
            Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
            {
                var candidate = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
            }
            AssemblyLoadContext.Default.Resolving += Resolve;
            try
            {
                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
                var type = assembly.GetTypes().Single(t => !t.IsAbstract && typeof(FlyleafLib.Plugins.PluginBase).IsAssignableFrom(t));
                using var instance = (FlyleafLib.Plugins.PluginBase)Activator.CreateInstance(type)!;
                var pluginDefaults = instance.GetDefaultOptions();
                Check(pluginDefaults.Count > 0, plugin + " cannot expose options against current core");
                if (plugin == "YTDLP")
                    Check(pluginDefaults.ContainsKey("BrowserCookiesSource") && FlyleafPluginOptionDescriptions.Describe(type.Name, "BrowserCookiesSource", pluginDefaults["BrowserCookiesSource"], null).Kind == FlyleafPluginOptionKind.Choice, "Installed YTDLP is missing its browser choice editor");
                else
                {
                    Check(pluginDefaults.ContainsKey("FolderComplete") && FlyleafPluginOptionDescriptions.Describe(type.Name, "FolderComplete", pluginDefaults["FolderComplete"], typeof(string)).Kind == FlyleafPluginOptionKind.Folder, "Installed BitSwarm storage path is a raw string editor");
                    Check(FlyleafPluginOptionDescriptions.Describe(type.Name, "MetadataParallelReq", "14", typeof(int)).MultipleOf == 2, "BitSwarm metadata request constraint missing");
                }
            }
            finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        CheckLogOutputRollback();
        CheckPlayerProfiles(application.Dispatcher);
        var engineConfig = typeof(Engine).GetProperty(nameof(Engine.Config))!;
        var previousConfig = engineConfig.GetValue(null);
        engineConfig.SetValue(null, new EngineConfig());
        try { CheckComposedPlaybackBar(); CheckComposedSwapDrops(); }
        finally { engineConfig.SetValue(null, previousConfig); }
        CheckDockSettingsTransitions();
        CheckMessageLifetime();
        CheckPlaybackDisplay();
        var autoRange = DisplayLuminanceRange.Resolve(-1, -1, 0.2f, 200, false);
        Check(autoRange == new DisplayLuminanceRange(0.2f, 200), "Automatic luminance was not resolved");
        Check(DisplayLuminanceRange.Resolve(-1, 400, 0.2f, 200, false).Minimum == 0.4f, "Automatic black level did not scale with peak override");
        Check(DisplayLuminanceRange.Resolve(500, 100, 0, 1000, true).Minimum < 100, "Black level was not clamped below peak");
        Check(DisplayLuminanceRange.Resolve(float.NaN, float.PositiveInfinity, float.NaN, float.NaN, true) == new DisplayLuminanceRange(0, 1000), "Nonfinite luminance did not resolve safely");
        Check(!FlyleafSettingsValues.ValidLuminanceRange(250, -1, 200) && FlyleafSettingsValues.ValidLuminanceRange(100, -1, 200), "Luminance validation ignores resolved automatic peak");
        Check(!FlyleafSettingsValues.ValidLuminanceRange(200, 200, 1000), "Equal black and peak accepted");
        double minimum = 0.5, maximum = 3; int writes = 0;
        var rangeEditor = new FlyleafDurationRangeEditor(() => (minimum, maximum), (min, max) => { minimum = min; maximum = max; writes++; }, action => action());
        rangeEditor.RefreshValues(); Check(writes == 0, "Range refresh writes configuration");
        Check(rangeEditor.TryCommit(0.1234567, 3.7654321) && minimum == 0.1234567 && maximum == 3.7654321, "Exact fractional range lost");
        Check(!rangeEditor.TryCommit(4, 3) && !rangeEditor.TryCommit(double.NaN, 3) && !rangeEditor.TryCommit(0, 86401) && writes == 1, "Invalid duration range changed configuration");
        var latencyEditor = new FlyleafDurationRangeEditor(() => (minimum, maximum), (min, max) => { minimum = min; maximum = max; }, action => action(), maximumOff: true);
        Check(latencyEditor.TryCommit(0.5, 0) && maximum == 0 && minimum == 0.5, "Disabled latency maximum rejected");
        Check(!rangeEditor.TryCommit(0.5, 0), "Buffer minimum greater than zero maximum accepted");
        minimum = 0.5; maximum = 3; rangeEditor.RefreshValues(true);
        rangeEditor.Measure(new Size(300, 200)); rangeEditor.Arrange(new Rect(0, 0, 300, 200)); rangeEditor.UpdateLayout();
        var rangeBar = rangeEditor.Children.OfType<FlyleafRangeBar>().Single();
        var rangeThumbs = rangeBar.Children.OfType<System.Windows.Controls.Primitives.Thumb>().ToArray();
        int beforeDrag = writes;
        rangeThumbs[0].RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(20, 0));
        Check(double.Parse(rangeEditor.MinimumInput.Text, CultureInfo.CurrentCulture) == rangeBar.Lower && rangeBar.Lower > minimum && writes == beforeDrag, "Dragging does not preview the minimum without committing");
        rangeThumbs[0].RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(20, 0, false));
        Check(minimum == rangeBar.Lower && writes == beforeDrag + 1, "Drag release did not commit preview");
        rangeThumbs[1].RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(20, 0));
        Check(double.Parse(rangeEditor.MaximumInput.Text, CultureInfo.CurrentCulture) == rangeBar.Upper && rangeBar.Upper > maximum, "Dragging does not preview the maximum");
        rangeThumbs[1].RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(20, 0, true));
        Check(double.Parse(rangeEditor.MaximumInput.Text, CultureInfo.CurrentCulture) == maximum && rangeBar.Upper == maximum && writes == beforeDrag + 1, "Canceled drag did not restore saved values");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            application.Resources.MergedDictionaries.Clear();
            application.Resources.MergedDictionaries.Add(new() { Source = new Uri($"pack://application:,,,/FlyleafME;component/Themes/{theme}.xaml") });
            application.Resources.MergedDictionaries.Add(new() { Source = new Uri("pack://application:,,,/FlyleafME;component/Themes/Controls.xaml") });
            var settingsBrand = new FlyleafSettingsPanel();
            foreach (var size in new[] { new Size(720, 620), new Size(360, 620), new Size(720, 240) })
            {
                settingsBrand.Measure(size); settingsBrand.Arrange(new Rect(new Point(0, 0), size)); settingsBrand.UpdateLayout();
                var visibleBranding = Find<FlyleafSettingsBranding>(settingsBrand).Where(b => b.Visibility == Visibility.Visible).ToArray();
                if (size.Height == 240) Check(visibleBranding.Length == 0, "Branding displaces navigation in a short panel");
                else
                {
                    Check(visibleBranding.Length == 1, "Settings branding is missing or duplicated");
                    var footer = visibleBranding.Single();
                    var position = footer.TransformToAncestor(settingsBrand).Transform(new Point(0, 0));
                    Check(position.Y > size.Height - 180 && (size.Width == 360 || footer.ActualWidth <= 145), "Settings branding is not anchored below navigation");
                    Check(Find<TextBlock>(footer).Any(t => t.Text.Contains(FlyleafSettingsBranding.LibraryVersion)), "Visible footer omits the library version");
                }
            }
            var demo = Demo();
            var panel = new FlyleafPanel("review", "Panel controls · " + theme, demo);
            // Mirror the real control's owned palette. Application resource changes
            // only broadcast to attached windows; this visual deliberately has no HWND.
            FlyleafThemeManager.Apply(panel.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
            panel.Measure(new Size(700, 640)); panel.Arrange(new Rect(0, 0, 700, 640)); panel.UpdateLayout();
            Check(Find<CheckBox>(panel).Any(c => c.Template?.FindName("Box", c) != null), theme + " checkbox template failed");
            Check(Find<TextBox>(panel).Any(t => t.Template?.FindName("PART_ContentHost", t) != null), theme + " text template failed");
            var shortcutItem = new MenuItem { Header = "Close window", InputGestureText = "Ctrl+W" };
            shortcutItem.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.ThemeMenuItemStyle");
            shortcutItem.Measure(new Size(220, 40));
            var shortcutText = (TextBlock)shortcutItem.Template.FindName("Gesture", shortcutItem);
            Check(shortcutText.Text == "Ctrl+W" && shortcutText.Visibility == Visibility.Visible, "Themed menus hide the assigned shortcut");
            shortcutItem.InputGestureText = "";
            Check(shortcutText.Visibility == Visibility.Collapsed, "Themed menus reserve space for an unassigned shortcut");
            var combo = Find<ComboBox>(panel).Single();
            Check(combo.Template?.FindName("PART_Popup", combo) != null, theme + " combo template failed");
            Check(Find<TreeViewItem>(panel).Any(t => t.Template?.FindName("Selection", t) != null), theme + " tree template failed");
            var queue = new FlyleafQueuePanel();
            queue.Measure(new Size(360, 800)); queue.Arrange(new Rect(0, 0, 360, 800)); queue.UpdateLayout();
            Check(queue.ActualWidth == 360, theme + " queue constructor/layout failed");
            string directory = Path.Combine(AppContext.BaseDirectory, "Review"); Directory.CreateDirectory(directory);
            CheckSettingsScrolling(directory, theme);
            CheckInformationLayout(directory, theme);
            CheckTrackPanels(directory, theme);
            CheckPlaybackPanelLayout(directory, theme);
            var queueOwner = new FlyleafME();
            FlyleafPlaylistAction? requestedAction = null;
            EventHandler<FlyleafPlaylistActionEventArgs> recordAction = (_, e) => requestedAction = e.Action;
            queueOwner.PlaylistActionRequested += recordAction;
            queueOwner.PlaylistActionsAvailable = true;
            FlyleafThemeManager.Apply(queueOwner.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
            var queueContext = new FlyleafPanelContext(queueOwner, "queue");
            queue.Context = queueContext;
            queueOwner.Queue.Add(new FlyleafQueueEntry { Input = "https://example.com/channel", Title = "News channel" });
            var embedded = queueOwner.Queue.Add(new FlyleafQueueEntry { Input = "https://example.com/album", Title = "Embedded playlist" });
            embedded.Update(Enumerable.Range(1, 3).Select(i => { var item = new FlyleafLib.MediaFramework.MediaPlaylist.PlaylistItem { DirectUrl = $"https://example.com/{i}" }; typeof(FlyleafLib.MediaFramework.MediaPlaylist.PlaylistItem).GetField("_Title", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(item, $"Embedded item {i}"); return item; }).ToArray(), null);
            for (int i = 1; i <= 30; i++) queueOwner.Queue.Add(new FlyleafQueueEntry { Input = $"C:/Media/{i}.mp4", Title = $"Video {i}" });
            typeof(FlyleafQueue).GetMethod("SetCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(queueOwner.Queue, [embedded]);
            typeof(FlyleafQueue).GetField("navigationChild", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(queueOwner.Queue, embedded.Children[1]);
            embedded.Children[1].Mark(true);
            typeof(FlyleafQueue).GetMethod("NotifyNavigation", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(queueOwner.Queue, null);
            typeof(FlyleafQueuePanel).GetMethod("Attach", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(queue, null);
            var queuePanel = new FlyleafPanel(new FlyleafPanelDefinition("queue", "Queue", _ => queue) { HandlesScrolling = true }, queue);
            FlyleafThemeManager.Apply(queuePanel.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
            foreach (var width in new[] { 420, 300, 360 })
            {
                RenderReview(queuePanel, width, 580, Path.Combine(directory, $"Queue-{theme}-{width}.png"));
                var list = Find<ListBox>(queue).Single();
                Check(list.SelectionMode == SelectionMode.Extended && list.Items.Count == 35, "Queue did not expose multiple selection and embedded rows");
                Check(list.ActualHeight > (width == 300 ? 366 : 400) && list.ActualHeight < 550, "Queue list did not fill its available panel height");
                Check(queueOwner.Queue.TotalItems == 34, "Queue item count double-counted embedded parents");
                var playingRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(embedded.Children[1]);
                Check(((Border)playingRow.Template.FindName("PlayingAccent", playingRow)).Visibility == Visibility.Visible &&
                    playingRow.FontWeight == FontWeights.SemiBold, "Playing child did not receive its accent treatment");
                Check(!embedded.IsCurrent && embedded.Children[1].IsCurrent, "Embedded parent was highlighted as an additional playing item");
                Check(Find<TextBlock>(list).Any(text => text.Text == "2–4") && !Find<TextBlock>(list).Any(text => text.Text == "▶"),
                    "Queue row numbers were missing or playing icons remained");
                var toolbar = (WrapPanel)queue.FindName("ToolbarHost");
                var queueTools = (StackPanel)queue.FindName("QueueTools");
                var playbackTools = (StackPanel)queue.FindName("PlaybackTools");
                var divider = (Border)queue.FindName("ToolbarDivider");
                var first = queueTools.TranslatePoint(default, toolbar);
                var second = playbackTools.TranslatePoint(default, toolbar);
                Check(width == 300 ? second.Y > first.Y && second.X == first.X : second.Y == first.Y && second.X > first.X,
                    $"Queue toolbar groups did not lay out as expected at width {width}: {first}, {second}");
                Check(divider.Visibility == (width == 300 ? Visibility.Hidden : Visibility.Visible),
                    "Queue toolbar divider was not limited to the one-row layout");
                Check(second.X + playbackTools.ActualWidth <= toolbar.ActualWidth + 0.5
                    && Find<Button>(toolbar).All(button => button.Visibility == Visibility.Visible),
                    "Queue toolbar hid or clipped an action");
            }

            static MenuItem MenuAction(ItemsControl menu, string label) =>
                menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, label));
            void CheckAction(ItemsControl menu, string label, FlyleafPlaylistAction expected)
            {
                requestedAction = null;
                var item = MenuAction(menu, label);
                Check(item.IsEnabled && menu.Style != null && item.Style != null, $"{label} was unavailable or lost its themed style");
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(requestedAction == expected, $"{label} did not request {expected}");
            }

            Check(queue.CreateAddMenu().Items.Count == 2, "Queue Add menu offered folder import without application support");
            queueOwner.SupportsFolderImport = true;
            var addMenu = queue.CreateAddMenu();
            CheckAction(addMenu, "Add files or playlists…", FlyleafPlaylistAction.Add);
            CheckAction(addMenu, "Add folder…", FlyleafPlaylistAction.AddFolder);
            CheckAction(addMenu, "Add URL…", FlyleafPlaylistAction.AddUrl);
            RenderReview(addMenu, 210, 100, Path.Combine(directory, $"QueueAddMenu-{theme}.png"));
            var queueList = Find<ListBox>(queue).Single();
            var contextMenu = queueList.ContextMenu;
            queue.PopulateQueueMenu();
            CheckAction(contextMenu, "Add files or playlists…", FlyleafPlaylistAction.Add);
            CheckAction(contextMenu, "Add folder…", FlyleafPlaylistAction.AddFolder);
            CheckAction(contextMenu, "Add URL…", FlyleafPlaylistAction.AddUrl);
            Check(MenuAction(MenuAction(contextMenu, "Sort queue"), "Name numeric (A–Z)").IsEnabled,
                "The Queue context menu lost its numeric sorting options");
            var contextPlaylist = MenuAction(contextMenu, "Playlist");
            CheckAction(contextPlaylist, "New queue", FlyleafPlaylistAction.New);
            CheckAction(contextPlaylist, "Save playlist", FlyleafPlaylistAction.Save);
            CheckAction(contextPlaylist, "Save playlist as…", FlyleafPlaylistAction.SaveAs);
            CheckAction(contextPlaylist, "Export playlist…", FlyleafPlaylistAction.Export);

            var playlistMenu = queue.CreatePlaylistMenu();
            CheckAction(playlistMenu, "New queue", FlyleafPlaylistAction.New);
            CheckAction(playlistMenu, "Save playlist", FlyleafPlaylistAction.Save);
            CheckAction(playlistMenu, "Save playlist as…", FlyleafPlaylistAction.SaveAs);
            CheckAction(playlistMenu, "Export playlist…", FlyleafPlaylistAction.Export);
            RenderReview(playlistMenu, 210, 145, Path.Combine(directory, $"QueuePlaylistMenu-{theme}.png"));
            queueOwner.PlaylistActionsAvailable = false;
            Check(queue.CreateAddMenu().Items.OfType<MenuItem>().Concat(queue.CreatePlaylistMenu().Items.OfType<MenuItem>())
                .All(item => !item.IsEnabled), "Queue menus allowed playlist operations while an operation was running");
            queueOwner.PlaylistActionsAvailable = true;

            var sortButton = (Button)queue.FindName("SortQueueButton");
            Check(sortButton.IsEnabled && sortButton.Content is Viewbox && MenuAction(queue.CreateSortMenu(), "Name numeric (A–Z)").IsEnabled,
                "The queue toolbar lost the numeric sort choice");
            var autoAdvance = (ToggleButton)queue.FindName("AutoAdvanceToggle");
            var repeat = (ToggleButton)queue.FindName("RepeatToggle");
            Check(!queueOwner.Queue.AutoAdvance && autoAdvance.IsChecked == false, "Auto-play next item did not default to off");
            autoAdvance.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Check(queueOwner.Queue.AutoAdvance && !queueOwner.Queue.Repeat, "Auto-play next item changed Repeat or failed to update");
            repeat.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Check(queueOwner.Queue.AutoAdvance && queueOwner.Queue.Repeat, "Repeat changed auto-play or failed to update");
            autoAdvance.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            queue.PopulateQueueMenu(embedded.Children[1]);
            Check(MenuAction(contextMenu, "Play").InputGestureText == "Enter"
                && !MenuAction(contextMenu, "Remove selected entries").IsEnabled,
                "The Queue context menu did not respect embedded item actions");
            var contextAutoNext = MenuAction(contextMenu, "Auto-next");
            var contextRepeat = MenuAction(contextMenu, "Repeat queue");
            Check(contextAutoNext.IsCheckable && !contextAutoNext.IsChecked && contextRepeat.IsCheckable && contextRepeat.IsChecked,
                "The Queue context menu did not show current playback modes");
            contextAutoNext.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            contextRepeat.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(queueOwner.Queue.AutoAdvance && !queueOwner.Queue.Repeat && autoAdvance.IsChecked == true && repeat.IsChecked == false,
                "Queue context toggles did not update the model and toolbar");
            queueList.UpdateLayout();
            var clickedRow = (ListBoxItem)queueList.ItemContainerGenerator.ContainerFromIndex(1);
            var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(doubleClick, 2);
            doubleClick.Source = clickedRow;
            queueList.RaiseEvent(doubleClick);
            Check(doubleClick.Handled && ReferenceEquals(queueList.SelectedItem, embedded), "Queue double-click was swallowed by drag selection handling");
            Check(!(bool)typeof(FlyleafQueuePanel).GetField("deferredSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(queue)!, "Double-click left a deferred drag selection");
            var other = queueOwner.Queue.Sources.Last();
            queueList.SelectedItems.Add(other);
            queue.PopulateQueueMenu(other);
            RenderReview(contextMenu, 265, 360, Path.Combine(directory, $"QueueContextMenu-{theme}.png"));
            var remove = MenuAction(contextMenu, "Remove selected entries");
            Check(remove.IsEnabled && remove.InputGestureText == "Delete", "The Queue context menu omitted removal for selected sources");
            remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!queueOwner.Queue.Sources.Contains(embedded) && !queueOwner.Queue.Sources.Contains(other),
                "Queue context removal did not keep the multiple selection");
            var previousOrder = queueOwner.Queue.Sources.ToArray();
            queue.PopulateQueueMenu();
            MenuAction(contextMenu, "Shuffle queue").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(!queueOwner.Queue.Sources.SequenceEqual(previousOrder), "Queue context shuffle did not reorder the queue");
            queueOwner.PlaylistActionRequested -= recordAction;
            var standalonePlaylist = queue.CreatePlaylistMenu();
            Check(standalonePlaylist.Items.Count == 1, "Standalone Queue offered app-managed playlist commands");
            MenuAction(standalonePlaylist, "New queue").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(queueOwner.Queue.Sources.Count == 0, "New queue could not clear a standalone Queue");
            var prompt = new FlyleafInputDialog("Add URL", "Media or playlist URL", "Add");
            FlyleafThemeManager.Apply(prompt.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
            Check(prompt.WindowStyle == WindowStyle.None && prompt.ResizeMode == ResizeMode.NoResize, "URL prompt restored a native title bar");
            RenderReview((FrameworkElement)prompt.Content, 380, 198, Path.Combine(directory, $"QueueUrl-{theme}.png"));
            Check(Find<Button>((FrameworkElement)prompt.Content).Any(button => Equals(button.Content, "Add")), "Add URL prompt lost its Add action");
            var folderPrompt = new FolderImportDialog(prompt.Resources, @"C:\Media\Episodes");
            var folderChrome = (FrameworkElement)folderPrompt.Content;
            RenderReview(folderChrome, 382, 380, Path.Combine(directory, $"QueueFolderDialog-{theme}.png"));
            Check(folderPrompt.WindowStyle == WindowStyle.None && folderPrompt.ResizeMode == ResizeMode.NoResize
                && Find<TextBlock>(folderChrome).Any(text => text.Text == "Add folder to queue")
                && Find<Button>(folderChrome).Any(button => Equals(button.ToolTip, "Close")),
                "Folder import lost its Flyleaf dialog header or close button");
            var folderSort = Find<ComboBox>(folderChrome).Single();
            Check(Equals(folderSort.SelectedItem, "File Explorer") && folderPrompt.Options.Sort == FolderSort.Explorer
                && Find<TextBlock>(folderChrome).Any(text => text.Visibility == Visibility.Visible && text.Text.Contains("visible order in open Explorer")),
                "Folder import did not default to File Explorer or show its help");
            Check(folderSort.Items.Contains("Name numeric (A–Z)") && folderSort.Items.Contains("Name numeric (Z–A)"),
                "The folder dialog did not expose both Name numeric sorts");
            var folderToggles = Find<CheckBox>(folderChrome).ToDictionary(box => (string)box.Content);
            var allMedia = folderToggles["All media"];
            var addFolder = Find<Button>(folderChrome).Single(button => Equals(button.Content, "Add to queue"));
            Check(allMedia.IsChecked == true && folderPrompt.Options.MediaType == FolderMediaType.All,
                "The folder dialog did not default to All media");
            allMedia.IsChecked = false;
            Check(folderPrompt.Options.MediaType == FolderMediaType.None && !addFolder.IsEnabled,
                "The folder dialog allowed importing an empty selection");
            folderToggles["Audio"].IsChecked = true;
            folderToggles["Animations"].IsChecked = true;
            Check(allMedia.IsChecked == null && addFolder.IsEnabled
                && folderPrompt.Options.MediaType == (FolderMediaType.Audio | FolderMediaType.Animations),
                "The folder dialog could not select several media types");
            RenderReview(folderChrome, 382, 380, Path.Combine(directory, $"QueueFolderSelection-{theme}.png"));
            allMedia.IsChecked = true;
            Check(folderPrompt.Options.MediaType == FolderMediaType.All && addFolder.IsEnabled,
                "The folder dialog could not restore All media from a partial selection");
            typeof(FlyleafQueuePanel).GetMethod("Detach", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(queue, null);
            queueOwner.Queue.Dispose();
            RenderDockSplitterPreview(directory, theme);
            RenderPlaybackBarReview(directory, theme);
            RenderBindingsReview(directory, theme);
            var subtitleEditor = new FlyleafSubtitleAppearanceEditor { Options = new FlyleafSubtitleOptions() };
            RenderReview(new FlyleafPanel("subtitle-style", "Subtitle text appearance", subtitleEditor), 340, 720, Path.Combine(directory, $"SubtitleEditor-{theme}.png"));
            var fontChoice = Find<ComboBox>(subtitleEditor).Single();
            Check(fontChoice.Template.FindName("PART_EditableTextBox", fontChoice) is TextBox { Visibility: Visibility.Visible }, "Editable font selector is missing its text input");
            fontChoice.SelectedItem = "Arial";
            Check(subtitleEditor.Options.FontFamily == "Arial", "Font selection does not update subtitle options");
            Check(Find<Button>(subtitleEditor).Where(b => b.Content is Border).All(b => ((Border)b.Content).ActualWidth >= 14), "Color swatch was squeezed away by button padding");
            var sizeInput = Find<TextBox>(subtitleEditor).First(t => t.Width == 76); sizeInput.Text = "invalid";
            Find<Button>(subtitleEditor).Single(b => Equals(b.ToolTip, "Reset appearance")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(sizeInput.Text == "48" && subtitleEditor.Options.FontFamily == "Segoe UI", "Appearance reset retained an invalid draft or custom font");
            CheckSubtitleSettingsAppearance(directory, theme);
            RenderReview(new FlyleafPanel("osd-style", "On-screen display", new FlyleafOSDOptionsEditor { Options = new() }), 340, 740, Path.Combine(directory, $"OSDEditor-{theme}.png"));
            RenderSubtitlePreview(directory, theme);
            var statusDemo = new FlyleafPlaybackStatusOverlay { Options = new(), Margin = new Thickness(12) };
            statusDemo.Update(new(Status.Playing, true, ActivityMode.Active, TimeSpan.FromSeconds(754).Ticks, TimeSpan.FromMinutes(105).Ticks, false, true, false));
            var statusScene = new Grid { Background = new SolidColorBrush(Color.FromRgb(40, 55, 72)) }; statusScene.Children.Add(statusDemo);
            RenderReview(statusScene, 640, 360, Path.Combine(directory, $"PlaybackTime-{theme}.png"));
            statusDemo.Error("Unable to open media. The server returned an error. Open Error details to inspect and copy the full message.");
            RenderReview(statusScene, 640, 360, Path.Combine(directory, $"PlaybackError-{theme}.png")); statusDemo.Reset();
            foreach (int width in new[] { 300, 360, 640 })
            {
                foreach (string tab in new[] { "Picture", "Framing", "360°" })
                {
                    var video = new FlyleafVideoPanel();
                    video.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    // Navigation is outside the disabled renderer-dependent editors.
                    video.Measure(new Size(width, 900)); video.Arrange(new Rect(0, 0, width, 900)); video.UpdateLayout();
                    Find<Button>(video).Single(b => Equals(b.Content, tab)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var review = new FlyleafPanel("video-review", "Video adjustments · " + tab, video);
                    RenderReview(review, width, 700, Path.Combine(directory, $"Video-{theme}-{tab.Replace("°", "")}-{width}.png"));
                    Check(review.ActualWidth == width, "Video adjustments narrow layout failed");
                    if (tab == "Framing") CheckVideoFramingLayout(video);
                    if (tab != "360°" && width == 300)
                    {
                        string disclosure = tab == "Picture" ? "Display luminance" : "Crop · source pixels";
                        Find<Expander>(video).Single(e => e.Header is TextBlock label && label.Text == disclosure).IsExpanded = true;
                        RenderReview(review, width, 900, Path.Combine(directory, $"Video-{theme}-{tab}-Expanded.png"));
                    }
                    video.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                }
            }
            rangeEditor.RefreshValues(true);
            var filterDemo = new StackPanel();
            foreach (var name in new[] { FLFilters.Brightness, FLFilters.Contrast }) filterDemo.Children.Add(new FlyleafVideoFilterEditor { Filter = new FLFilter { Filter = name, Minimum = -100, Maximum = 100, Default = 0 } });
            filterDemo.Children.Add(rangeEditor);
            RenderReview(new FlyleafPanel("ranges", "Filters and duration range", filterDemo), 300, 400, Path.Combine(directory, $"Ranges-{theme}.png"));
            filterDemo.Children.Remove(rangeEditor);
            var bitmap = new RenderTargetBitmap(700, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(directory, theme + ".png"); using (var file = File.Create(path)) encoder.Save(file);
            Console.WriteLine("Rendered " + path);
            var probe = new SettingsLayoutProbe();
            var settingsPanel = new FlyleafPanel("settings-layout", "Settings rows · " + theme, probe);
            foreach (int width in new[] { 700, 360 })
            {
                settingsPanel.Measure(new Size(width, 700)); settingsPanel.Arrange(new Rect(0, 0, width, 700)); settingsPanel.UpdateLayout();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => { }));
                settingsPanel.UpdateLayout();
                var dictionary = probe.Children.OfType<StackPanel>().Single().Children.OfType<Grid>().Single(g => g.ColumnDefinitions.Count == 3);
                var textboxes = dictionary.Children.OfType<TextBox>().ToArray();
                var key1 = textboxes[0]; var value1 = textboxes[1]; var key2 = textboxes[2]; var value2 = textboxes[3];
                Point Position(FrameworkElement element) => element.TransformToAncestor(settingsPanel).Transform(new Point(0, 0));
                Check(Math.Abs(Position(key1).Y - Position(value1).Y) < 0.1 && Math.Abs(key1.ActualHeight - value1.ActualHeight) < 0.1, "Dictionary key/value do not align vertically");
                Check(Math.Abs(Position(value1).X - Position(value2).X) < 0.1 && Math.Abs(Position(key1).X - Position(key2).X) < 0.1, "Dictionary columns differ between rows");
                var numeric = probe.Children.OfType<StackPanel>().Single().Children.OfType<Grid>().First(g => g.ColumnDefinitions.Count == 2);
                var label = numeric.Children.OfType<TextBlock>().Single();
                var number = Find<TextBox>(numeric).Single();
                Check(width == 700 ? Math.Abs(Position(label).Y + label.ActualHeight / 2 - Position(number).Y - number.ActualHeight / 2) < 1 : Position(number).Y > Position(label).Y,
                    "Setting rows did not switch between inline and narrow layouts");
                var header = probe.Children.OfType<StackPanel>().Single().Children.OfType<Border>().First();
                var title = ((StackPanel)header.Child).Children.OfType<TextBlock>().Single(); var undo = Find<Button>(header).Single();
                Check(Position(undo).X - Position(title).X - title.ActualWidth <= 12, "Reset action is detached from its section heading");
                var layoutBitmap = new RenderTargetBitmap(width, 700, 96, 96, PixelFormats.Pbgra32); layoutBitmap.Render(settingsPanel);
                var layoutEncoder = new PngBitmapEncoder(); layoutEncoder.Frames.Add(BitmapFrame.Create(layoutBitmap));
                using var layoutFile = File.Create(Path.Combine(Path.GetDirectoryName(path)!, $"Settings-{theme}-{width}.png")); layoutEncoder.Save(layoutFile);
            }
            foreach (var accent in Enum.GetValues<FlyleafAccent>())
            {
                FlyleafThemeManager.Apply(panel.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light, accent);
                panel.UpdateLayout();
                var selectedCheck = Find<CheckBox>(panel).First();
                var box = (Border)selectedCheck.Template.FindName("Box", selectedCheck);
                Check(((SolidColorBrush)box.Background).Color == ((SolidColorBrush)panel.FindResource("FlyleafME.AccentBrush")).Color,
                    theme + " " + accent + " dynamic accent failed without recreating the checkbox");
            }
            var logo = new FlyleafLogo { Width = 28, Height = 28 };
            foreach (var accent in Enum.GetValues<FlyleafAccent>())
            {
                FlyleafThemeManager.Apply(logo.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light, accent);
                logo.Measure(new Size(28, 28)); logo.Arrange(new Rect(0, 0, 28, 28)); logo.UpdateLayout();
                Check(ReferenceEquals(logo.MarkBrush, logo.FindResource("FlyleafME.LogoMarkBrush")), theme + " logo accent does not update");
                var icon = FlyleafLogo.CreateImageSource(logo.MarkBrush, logo.FoldBrush, logo.PlayBrush, logo.OutlineBrush);
                Check(icon.Width == 128 && icon.Height == 128 && ((DrawingGroup)icon.Drawing).Children.All(c => c is GeometryDrawing), "Logo ImageSource is not a complete vector drawing");
            }
        }
        RenderLogoPreview(repository.FullName);
        RenderAccentPalette();
        Console.WriteLine($"{checks} settings checks passed. No media engine was started and no windows were shown.");
    }

    static void RenderAccentPalette()
    {
        var accents = Enum.GetValues<FlyleafAccent>();
        const int width = 1080, height = 350;
        var preview = new Grid { Width = width, Height = height };
        preview.ColumnDefinitions.Add(new() { Width = new GridLength(72) });
        foreach (var _ in accents) preview.ColumnDefinitions.Add(new());
        preview.RowDefinitions.Add(new()); preview.RowDefinitions.Add(new());
        foreach (var (mode, row) in new[] { (FlyleafTheme.Dark, 0), (FlyleafTheme.Light, 1) })
        {
            var themeLabel = new TextBlock { Text = mode.ToString(), FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var themeCell = new Border { Child = themeLabel };
            FlyleafThemeManager.Apply(themeCell.Resources, mode);
            themeCell.SetResourceReference(Border.BackgroundProperty, "FlyleafME.SurfaceBrush");
            themeLabel.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush");
            Grid.SetRow(themeCell, row); preview.Children.Add(themeCell);
            for (int i = 0; i < accents.Length; i++)
            {
                var color = FlyleafThemeManager.GetAccentColor(mode, accents[i]);
                var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var card = new Border { Child = body, Padding = new Thickness(6) };
                FlyleafThemeManager.Apply(card.Resources, mode, accents[i]);
                card.SetResourceReference(Border.BackgroundProperty, "FlyleafME.SurfaceBrush");
                Grid.SetRow(card, row); Grid.SetColumn(card, i + 1); preview.Children.Add(card);
                var label = new TextBlock { Text = accents[i].ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
                label.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush"); body.Children.Add(label);
                body.Children.Add(new FlyleafLogo { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Center });
                var hex = new TextBlock { Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
                hex.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush"); body.Children.Add(hex);
                var button = new Button { Content = "Play", Width = 70, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(8, 4, 8, 4) };
                button.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.PrimaryButtonStyle"); body.Children.Add(button);
            }
        }
        preview.Measure(new Size(width, height)); preview.Arrange(new Rect(0, 0, width, height)); preview.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(preview);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(AppContext.BaseDirectory, "Review", "AccentPalette.png")); encoder.Save(file);
    }

    static void CheckControlShortcuts()
    {
        var keys = new KeysConfig(); keys.LoadDefault();
        var before = keys.Keys.ToArray(); string? executed = null;
        FlyleafControlKeybindings.Attach(keys, id => executed = id, addDefaultGestures: true);
        Check(keys.Keys.Count == before.Length + FlyleafME.ControlKeybindingDefaults.Count - 2 && before.All(b => keys.Keys.Contains(b)), "Control defaults removed existing bindings instead of transferring navigation");
        Check(FlyleafME.ControlKeybindingDefaults["control.subtitles.up"] == new FlyleafShortcutGesture(Key.Up, Alt: true) && FlyleafME.ControlKeybindingDefaults["control.subtitles.down"].Key == Key.Down && FlyleafME.ControlKeybindingDefaults["control.subtitles.larger"].Key == Key.Right && FlyleafME.ControlKeybindingDefaults["control.subtitles.smaller"].Key == Key.Left, "Control subtitle default directions differ from V1");
        var up = keys.Get("control.subtitles.up");
        ((Action)typeof(PlayerKeyBinding).GetField("ActionInternal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(up)!)();
        Check(executed == "control.subtitles.up" && up.Alt && !up.IsKeyUp, "Control action did not dispatch as a repeatable Alt gesture");
        new FlyleafShortcutGesture(Key.U, Ctrl: true, IsKeyUp: true).Apply(up);
        keys.Get("control.subtitles.down").Key = Key.None;
        int previousCount = keys.Keys.Count;
        FlyleafControlKeybindings.Attach(keys, id => executed = "new:" + id, addDefaultGestures: true);
        ((Action)typeof(PlayerKeyBinding).GetField("ActionInternal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(up)!)();
        Check(keys.Keys.Count == previousCount && up.Key == Key.U && up.Ctrl && up.IsKeyUp && keys.Get("control.subtitles.down").Key == Key.None && executed == "new:control.subtitles.up", "Reattachment lost remapping/unassignment or retained the old callback");
        var occupied = new KeysConfig(); occupied.LoadDefault(); occupied.Add(Key.Up, KeyBindingAction.VolumeUp, alt: true);
        var existing = occupied.Keys.Single(b => b.Key == Key.Up && b.Alt);
        FlyleafControlKeybindings.Attach(occupied, _ => { }, addDefaultGestures: true);
        Check(existing.Action == KeyBindingAction.VolumeUp && occupied.Get("control.subtitles.up").Key == Key.None && occupied.Get("control.subtitles.down").Key == Key.Down, "Control defaults overwrote an occupied application/library gesture");
        var loaded = new KeysConfig(); loaded.LoadDefault();
        FlyleafControlKeybindings.Attach(loaded, _ => { }, addDefaultGestures: false);
        Check(loaded.Keys.Where(b => FlyleafME.ControlKeybindingDefaults.ContainsKey(b.ActionName ?? "")).Count(b => b.Key == Key.None) == FlyleafME.ControlKeybindingDefaults.Count - 3, "Loaded configurations received unsolicited active default gestures");
        Check(keys.Get("control.navigation.previous").Ctrl && keys.Get("control.navigation.previous").Key == Key.Up && keys.Get("control.navigation.next").Key == Key.Down && !keys.Keys.Any(b => b.Action is KeyBindingAction.OpenPrevItem or KeyBindingAction.OpenNextItem), "Legacy navigation gestures did not transfer to control ownership");
        Check(keys.Get("control.navigation.previousPage").Key == Key.PageUp && keys.Get("control.navigation.nextPage").Ctrl && keys.Get("control.navigation.nextPage").IsKeyUp, "Large-step navigation defaults are wrong");
        var f5 = keys.Get("control.queue.autoAdvance");
        Check(f5.Key == Key.F5 && f5.IsKeyUp && !f5.Alt && !f5.Ctrl && !f5.Shift, "Queue auto-play does not use unmodified F5 on release");
        ((Action)typeof(PlayerKeyBinding).GetField("ActionInternal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f5)!)();
        Check(executed == "new:control.queue.autoAdvance", "F5 did not dispatch the control's queue action");
        Check(loaded.Get("control.queue.autoAdvance").Key == Key.F5, "Existing configs missing the new action did not receive F5");
        var f5Occupied = new KeysConfig(); f5Occupied.LoadDefault(); f5Occupied.Add(Key.F5, KeyBindingAction.VolumeUp);
        FlyleafControlKeybindings.Attach(f5Occupied, _ => { }, addDefaultGestures: false);
        Check(f5Occupied.Get("control.queue.autoAdvance").Key == Key.None && f5Occupied.Keys.Any(b => b.Key == Key.F5 && b.Action == KeyBindingAction.VolumeUp), "F5 default overwrote an existing user gesture");
        f5.Key = Key.None;
        FlyleafControlKeybindings.Attach(keys, _ => { }, addDefaultGestures: true);
        Check(keys.Get("control.queue.autoAdvance").Key == Key.None, "Reattachment restored deliberately unassigned F5");
        Check(FlyleafQueue.NavigationIndex(5, 20, 10, false) == 15 && FlyleafQueue.NavigationIndex(15, 20, 10, false) == 19 && FlyleafQueue.NavigationIndex(5, 20, -10, false) == 0, "Large navigation steps do not clamp at the ends");
        Check(FlyleafQueue.NavigationIndex(15, 20, 10, true) == 5 && FlyleafQueue.NavigationIndex(0, 20, -10, true) == 10 && FlyleafQueue.NavigationIndex(-1, 0, 10, true) == -1, "Large-step repeat/empty navigation is invalid");
    }

    static void CheckSingleInstanceLaunch()
    {
        string workingDirectory = Path.GetTempPath();
        string media = Path.Combine(workingDirectory, "test clip.mkv");
        var request = Flyleaf.LaunchRequest.Parse(
            ["/StartMinimized", "test clip.mkv", "https://example.com/playlist.m3u", new Uri(media).AbsoluteUri],
            workingDirectory, newWindow: true);
        Check(request.StartMinimized && request.NewWindow && request.Inputs.SequenceEqual([media, "https://example.com/playlist.m3u", media]),
            "Launch inputs lost a switch, URL, or path relative to the launching process");

        string name = "Flyleaf.LaunchChecks." + Guid.NewGuid().ToString("N");
        using var primary = new Flyleaf.SingleInstance(name);
        Check(primary.TryAcquire(), "The first launch could not acquire the instance mutex");
        var arrived = new TaskCompletionSource<Flyleaf.LaunchRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.Listen(payload => { arrived.TrySetResult(payload); return true; });
        bool delivered = Task.Run(async () =>
        {
            using var secondary = new Flyleaf.SingleInstance(name);
            return !secondary.TryAcquire() && await secondary.ForwardAsync(request);
        }).GetAwaiter().GetResult();
        Check(delivered && arrived.Task.IsCompletedSuccessfully &&
            arrived.Task.Result.Inputs.SequenceEqual(request.Inputs) && arrived.Task.Result.NewWindow && arrived.Task.Result.StartMinimized,
            "A second launch failed to forward its inputs to the running instance");
    }

    static void CheckQueueSort()
    {
        using var queue = new FlyleafQueue();
        var ten = queue.Add(@"C:\Media\10.mp4");
        var prefixed = queue.Add(@"C:\Media\ycl0.avi");
        var one = queue.Add(@"C:\Media\1.mp4");
        var two = queue.Add(new FlyleafQueueEntry { Input = "https://example.com/series", Title = "2.mp4" });
        var zero = queue.Add(@"C:\Media\0.mp4");
        var first = new FlyleafLib.MediaFramework.MediaPlaylist.PlaylistItem { DirectUrl = "https://example.com/part1" };
        var second = new FlyleafLib.MediaFramework.MediaPlaylist.PlaylistItem { DirectUrl = "https://example.com/part2" };
        two.Update([first, second], first);
        var children = two.Children.ToArray();
        typeof(FlyleafQueue).GetMethod("SetCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(queue, [two]);
        long revision = queue.Revision;
        queue.SortByName(numeric: true);
        Check(queue.Sources.SequenceEqual([zero, one, two, ten, prefixed]) && queue.Revision == revision + 1,
            "Name numeric failed to order numeric filenames together or ignored the visible title");
        Check(ReferenceEquals(queue.CurrentSource, two) && two.Children.SequenceEqual(children)
            && queue.CurrentItemNumber == 3 && queue.CaptureEntries()[2].Input == two.Input,
            "Sorting changed the playing source, embedded sequence, numbering or saved order");
        queue.SortByName(numeric: true);
        Check(queue.Revision == revision + 1, "Sorting an already sorted queue marked it dirty");
        queue.SortByName(descending: true, numeric: true);
        Check(queue.Sources.SequenceEqual([prefixed, ten, two, one, zero]), "Name numeric (Z–A) was not in reverse natural order");
        queue.SortByName();
        Check(queue.Sources.SequenceEqual([zero, one, ten, two, prefixed]), "Name (A–Z) did not compare filenames as text");
        queue.SortByName(descending: true);
        Check(queue.Sources.SequenceEqual([prefixed, two, ten, one, zero]), "Name (Z–A) did not reverse the text order");
        var appended = queue.Add(@"C:\Media\Append.mp4");
        Check(ReferenceEquals(queue.Sources.Last(), appended), "Sorting made subsequent adds reorder automatically");
    }

    static void CheckFileAssociations()
    {
        var extensions = FileAssociations.Groups.SelectMany(group => group.Extensions).ToArray();
        Check(extensions.Length == extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count() &&
            extensions.Contains(".fpls") && extensions.Contains(".webp"), "File association groups have duplicate or missing extensions");

        var images = FileAssociations.Groups.Single(group => group.Label == "Images & animations").Extensions;
        Check(new[] { ".exr", ".dpx", ".ras", ".jp2" }.All(images.Contains) &&
            FileAssociations.Groups.Single(group => group.Label == "Audio").Extensions.Contains(".ogg"),
            "File associations assigned a shared extension to the wrong category");
        Check(MediaExtensions.IsImagePath("Frame.DPX") && !MediaExtensions.IsImagePath("Frame.dpx.txt"),
            "Image recognition failed to match the final extension case-insensitively");

        const string app = @"C:\Test Folder\Flyleaf.exe";
        const string moved = @"D:\Moved Folder\Flyleaf.exe";
        const string appKey = @"Software\RegisteredApplications";
        const string types = @"Software\SuRGeoNix\Flyleaf\Capabilities\FileAssociations";
        const string command = @"Software\Classes\Flyleaf.Media\shell\open\command";
        const string openWith = @"Software\Classes\.mp4\OpenWithProgids";
        const string otherDefault = @"Software\Classes\.mp4";
        const string customOpenWith = @"Software\Classes\.m2p\OpenWithProgids";
        Check(FileAssociations.TryNormalizeExtension(" M2P ", out var custom) && custom == ".m2p"
            && !FileAssociations.TryNormalizeExtension("*.m2p", out _)
            && !FileAssociations.TryNormalizeExtension(@"m2p\shell", out _)
            && !FileAssociations.TryNormalizeExtension("movie.m2p", out _), "Custom extension validation accepted an invalid extension");
        string scratch = @"Software\Flyleaf-AssociationCheck-" + Guid.NewGuid().ToString("N");
        var currentUser = Microsoft.Win32.Registry.CurrentUser;
        try
        {
            using var user = currentUser.CreateSubKey(scratch);
            using (var media = user.CreateSubKey(otherDefault)) media.SetValue("", "Another.Player");
            string? Read(string key, string name)
            {
                using var item = user.OpenSubKey(key);
                return item?.GetValue(name) as string;
            }

            FileAssociations.ApplyRegistration(user, app, [".mp4", ".fpls", "M2P"]);
            Check(Read(appKey, "Flyleaf") == @"Software\SuRGeoNix\Flyleaf\Capabilities" &&
                Read(types, ".mp4") == FileAssociations.Handler && Read(types, ".fpls") == FileAssociations.Handler,
                "Selected extensions did not register with Windows");
            using (var media = user.OpenSubKey(openWith))
                Check(media?.GetValueNames().Contains(FileAssociations.Handler) == true, "Flyleaf was missing from Open with");
            Check(Read(command, "") == $"\"{app}\" \"%1\"", "Shell command did not quote paths");
            using (var media = user.OpenSubKey(customOpenWith))
                Check(Read(types, ".m2p") == FileAssociations.Handler && media?.GetValueNames().Contains(FileAssociations.Handler) == true,
                    "The custom extension was not registered");
            FileAssociations.ApplyRegistration(user, moved, [".fpls"]);
            using (var media = user.OpenSubKey(customOpenWith))
                Check(Read(types, ".m2p") == null && media?.GetValueNames().Contains(FileAssociations.Handler) == false,
                    "Removing a custom extension left its registration");
            using (var media = user.OpenSubKey(openWith))
                Check(media?.GetValueNames().Contains(FileAssociations.Handler) == false, "Deselection left a Flyleaf Open with entry");
            Check(Read(types, ".mp4") == null && Read(command, "") == $"\"{moved}\" \"%1\"" &&
                Read(otherDefault, "") == "Another.Player", "Reapplying associations disrupted other defaults or failed to update the executable");
            FileAssociations.ApplyRegistration(user, moved, []);
            Check(Read(appKey, "Flyleaf") == null && Read(types, ".fpls") == null && Read(command, "") != null,
                "Clearing registration broke the stable handler or left stale registrations");
        }
        finally { currentUser.DeleteSubKeyTree(scratch, throwOnMissingSubKey: false); }
    }

    static void CheckFolderImport()
    {
        var root = Directory.CreateTempSubdirectory("Flyleaf-folder-check-");
        var child2 = root.CreateSubdirectory("Disc 2");
        var child10 = root.CreateSubdirectory("Disc 10");
        var numeric = root.CreateSubdirectory("Numeric");
        var fixtures = new List<string>();
        string Write(DirectoryInfo folder, string name, byte[] data)
        {
            var path = Path.Combine(folder.FullName, name);
            File.WriteAllBytes(path, data);
            fixtures.Add(path);
            return path;
        }

        try
        {
            var earlier = Write(root, "Episode 2.MP4", [1]);
            var later = Write(root, "Episode 10.mp4", [1, 2, 3]);
            File.SetLastWriteTimeUtc(earlier, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(later, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var track = Write(root, "Track.ogg", []);
            var image = Write(root, "Image.png", []);
            Write(root, "Captions.srt", []);
            Write(root, "Playlist.m3u", []);
            var nested2 = Write(child2, "Chapter 10.mkv", []);
            var nested10 = Write(child10, "Chapter 2.mkv", []);

            var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAUEBAAAACwAAAAAAQABAAACAkQBADs=");
            var stillGif = Write(root, "Still.gif", gif);
            int imageBlock = Array.IndexOf(gif, (byte)0x2c);
            var animatedGif = Write(root, "Animated.gif", [.. gif[..^1], .. gif[imageBlock..]]);
            byte[] webp = [.. "RIFF"u8, 22, 0, 0, 0, .. "WEBPVP8X"u8, 10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
            var stillWebp = Write(root, "Still.webp", webp);
            webp[20] = 2;
            var animatedWebp = Write(root, "Animated.webp", webp);

            FolderImportResult Scan(FolderMediaType filter, FolderSort sort = FolderSort.NameAscending, bool recursive = false,
                IReadOnlyDictionary<string, Dictionary<string, int>>? views = null) =>
                FolderQueueImport.Scan(root.FullName, new(filter, recursive, sort), views, default);

            Check(Scan(FolderMediaType.All).Paths.Length == 8 && Scan(FolderMediaType.Audio).Paths.SequenceEqual([track]),
                "All media included non-media files or the audio filter lost Ogg");
            Check(Scan(FolderMediaType.Video).Paths.SequenceEqual([later, earlier])
                && Scan(FolderMediaType.Video, recursive: true).Paths.SequenceEqual([later, earlier, nested2, nested10]),
                "Folder import failed to sort names as text or visit nested folders in order");
            foreach (var sort in new[] { FolderSort.NumberDescending, FolderSort.ModifiedNewest, FolderSort.SizeLargest })
                Check(Scan(FolderMediaType.Video, sort).Paths.SequenceEqual([later, earlier]), $"Folder sort {sort} ignored its ordering");
            foreach (var sort in new[] { FolderSort.NameDescending, FolderSort.NumberAscending, FolderSort.ModifiedOldest, FolderSort.SizeSmallest })
                Check(Scan(FolderMediaType.Video, sort).Paths.SequenceEqual([earlier, later]), $"Folder sort {sort} ignored its ordering");

            var images = Scan(FolderMediaType.Images).Paths;
            var animations = FolderQueueImport.ScanAsync(root.FullName, new(FolderMediaType.Animations, false, FolderSort.NameAscending), default)
                .GetAwaiter().GetResult().Paths;
            Check(images.ToHashSet().SetEquals([image, stillGif, stillWebp]) &&
                animations.ToHashSet().SetEquals([animatedGif, animatedWebp]),
                "Folder import did not distinguish animated GIF/WebP from still images");
            Check(Scan(FolderMediaType.Audio | FolderMediaType.Animations).Paths.SequenceEqual([animatedGif, animatedWebp, track]),
                "Mixed folder filters did not preserve sorting or exclude unselected types");
            Check(Scan(FolderMediaType.Images | FolderMediaType.Animations).Paths.ToHashSet().SetEquals(images.Concat(animations))
                && Scan(FolderMediaType.None).Paths.Length == 0, "Combined visual filters or empty media filtering failed");

            var views = new Dictionary<string, Dictionary<string, int>>
            {
                [root.FullName] = new()
                {
                    [later] = 0, [earlier] = 1, [child10.FullName] = 2, [child2.FullName] = 3, [numeric.FullName] = 4
                },
                [child10.FullName] = new() { [nested10] = 0 },
                [child2.FullName] = new() { [nested2] = 0 }
            };
            var explorer = Scan(FolderMediaType.Video, FolderSort.Explorer, recursive: true, views);
            Check(explorer.Paths.SequenceEqual([later, earlier, nested10, nested2]) && !explorer.UsedNameFallback,
                "Folder import ignored Explorer item order or subfolder order");
            var fallback = FolderQueueImport.ScanAsync(root.FullName, new(FolderMediaType.Video, false, FolderSort.Explorer), default)
                .GetAwaiter().GetResult();
            string[] policyOrder = ExplorerFolderOrder.UsesLiteralNameOrder() ? [later, earlier] : [earlier, later];
            Check(fallback.Paths.SequenceEqual(policyOrder) && fallback.UsedNameFallback,
                "A folder without an Explorer view did not use the Windows name-sort policy");

            var zero = Write(numeric, "0.mp4", []);
            var one = Write(numeric, "1.mp4", []);
            var two = Write(numeric, "2.mp4", []);
            var ten = Write(numeric, "10.mp4", []);
            var prefixed = Write(numeric, "ycl0.avi", []);
            FolderImportResult ScanNames(FolderSort sort) =>
                FolderQueueImport.Scan(numeric.FullName, new(FolderMediaType.Video, false, sort), null, default);
            Check(ScanNames(FolderSort.NameAscending).Paths.SequenceEqual([zero, one, ten, two, prefixed])
                && ScanNames(FolderSort.NameDescending).Paths.SequenceEqual([prefixed, two, ten, one, zero]),
                "Folder Name sort did not compare filenames as text");
            Check(ScanNames(FolderSort.NumberAscending).Paths.SequenceEqual([zero, one, two, ten, prefixed])
                && ScanNames(FolderSort.NumberDescending).Paths.SequenceEqual([prefixed, ten, two, one, zero]),
                "Folder Name numeric sort moved a prefixed filename into the digit-only names");

            var partialView = new Dictionary<string, Dictionary<string, int>>
            {
                [numeric.FullName] = new() { [prefixed] = 0, [zero] = 1 }
            };
            var explorerOptions = new FolderImportOptions(FolderMediaType.Video, false, FolderSort.Explorer);
            var naturalExplorer = FolderQueueImport.Scan(numeric.FullName, explorerOptions, partialView, default, explorerUsesLiteralNames: false);
            var literalExplorer = FolderQueueImport.Scan(numeric.FullName, explorerOptions, partialView, default, explorerUsesLiteralNames: true);
            Check(naturalExplorer.UsedNameFallback && naturalExplorer.Paths.SequenceEqual([prefixed, zero, one, two, ten]),
                "Explorer view order did not take priority over numeric fallback");
            Check(literalExplorer.UsedNameFallback && literalExplorer.Paths.SequenceEqual([prefixed, zero, one, ten, two]),
                "Explorer view order did not take priority over literal fallback");

            string[] imagePaths = [Write(numeric, "Frame.exr", []), Write(numeric, "Frame.DPX", []),
                Write(numeric, "Frame.ras", []), Write(numeric, "Frame.jp2", [])];
            var additionalImages = FolderQueueImport.Scan(numeric.FullName,
                new(FolderMediaType.Images, false, FolderSort.NameAscending), null, default);
            Check(additionalImages.Paths.ToHashSet().SetEquals(imagePaths),
                "Folder import did not include all shared image extensions");

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                FolderQueueImport.ScanAsync(root.FullName, new(FolderMediaType.All, true, FolderSort.NameAscending), canceled.Token)
                    .GetAwaiter().GetResult();
                Check(false, "Folder import ignored cancellation");
            }
            catch (OperationCanceledException) { checks++; }
        }
        finally
        {
            foreach (var fixture in fixtures) File.Delete(fixture);
            child2.Delete();
            child10.Delete();
            numeric.Delete();
            root.Delete();
        }
    }

    static void CheckSharedPanelLayout()
    {
        var settings = new Flyleaf.WindowOptions();
        var settingsOverlay = new FlyleafPanelPlacement("settings", FlyleafPanelMode.Overlay, FlyleafPanelSide.Right, 760, false);
        var settingsFloating = settingsOverlay with
        {
            Mode = FlyleafPanelMode.Floating,
            FloatingBounds = new(100, 150, 800, 650)
        };
        var queue = new FlyleafPanelPlacement("queue", FlyleafPanelMode.Docked, FlyleafPanelSide.Right, 360, true);
        settings.PanelLayout = [settingsOverlay, queue];

        settings.MergePanelLayout([settingsOverlay, queue], [settingsFloating, queue]);
        settings.MergePanelLayout([settingsOverlay, queue], [settingsOverlay, queue]);
        Check(settings.PanelLayout.Single(panel => panel.Id == "settings") == settingsFloating,
            "Closing an unchanged older window overwrote the newer floating placement");

        settings.MergePanelLayout([settingsOverlay, queue], [settingsOverlay with { IsOpen = true }, queue with { Width = 410 }]);
        Check(settings.PanelLayout.Single(panel => panel.Id == "settings") == settingsFloating
            && settings.PanelLayout.Single(panel => panel.Id == "queue").Width == 410,
            "Activation capture saved a temporary open state or overwrote a newer floating position");

        var temporary = settingsFloating with { IsOpen = true, FloatingBounds = new(200, 250, 800, 650) };
        settings.MergePanelLayout([settingsFloating, queue], [temporary, queue], saveOpenState: true);
        Check(settings.PanelLayout.Single(panel => panel.Id == "settings") == temporary, "Closing a window lost its floating placement");
        settings.MergePanelLayout([settingsFloating, queue], [settingsFloating, queue], saveOpenState: true);
        Check(settings.PanelLayout.Single(panel => panel.Id == "settings") == temporary with { IsOpen = false },
            "A floating Settings panel from an earlier window reopened even though it was closed in the last window");

        var video = queue with { Id = "video", IsOpen = false };
        settings.MergePanelLayout([queue, video], [queue with { IsOpen = false }, video with { IsOpen = true }], saveOpenState: true);
        Check(!settings.PanelLayout.Single(panel => panel.Id == "queue").IsOpen &&
            settings.PanelLayout.Single(panel => panel.Id == "video").IsOpen,
            "A merged layout kept competing panels open in one dock");

        var legacy = Flyleaf.AppConfig.Deserialize(
            """{"SchemaVersion":1,"AppConfig":{"Window":{"RestorePanelLayout":true}}}""");
        Check(legacy.Window.StartupPanelLayout == Flyleaf.StartupPanelLayoutMode.LastUsed,
            "A previous opt-in to panel restoration did not migrate to Last used layout");
        var explicitDefault = Flyleaf.AppConfig.Deserialize(
            """{"SchemaVersion":1,"AppConfig":{"Window":{"StartupPanelLayout":"Default","RestorePanelLayout":true}}}""");
        Check(explicitDefault.Window.GetStartupPanelLayout() == null,
            "A legacy restore preference overrode the new startup layout");
        var initial = new Flyleaf.AppConfig();
        Check(initial.Window.StartupPanelLayout == Flyleaf.StartupPanelLayoutMode.Default &&
            initial.Window.GetStartupPanelLayout() == null, "A fresh window did not start with the default layout");
        initial.Window.PanelLayout = [queue];
        initial.Window.SaveCurrentPanelLayout([queue with { Width = 425 }, temporary]);
        var saved = Flyleaf.AppConfig.Deserialize(initial.Serialize()).Window;
        Check(saved.StartupPanelLayout == Flyleaf.StartupPanelLayoutMode.Saved
            && saved.GetStartupPanelLayout()!.SequenceEqual([queue with { Width = 425 }, temporary with { IsOpen = false }]),
            "Saving the layout did not select and round-trip the explicit snapshot");
        saved.MergePanelLayout([queue], [queue with { Width = 380 }], saveOpenState: true);
        Check(saved.GetStartupPanelLayout()!.First() == queue with { Width = 425 },
            "Updating last-used layout changed the explicit snapshot");
        saved.StartupPanelLayout = Flyleaf.StartupPanelLayoutMode.LastUsed;
        Check(saved.GetStartupPanelLayout()!.Single() == queue with { Width = 380 },
            "Switching to Last used layout did not select the independent snapshot");
        saved.StartupPanelLayout = Flyleaf.StartupPanelLayoutMode.Default;
        Check(saved.GetStartupPanelLayout() == null && saved.SavedPanelLayout!.Length == 2,
            "Default layout restored saved panels or discarded the manually saved layout");
    }

    static void CheckDockRatioSizing()
    {
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        foreach (int leftDock in new[] { 0, 365 })
        foreach (int rightDock in new[] { 0, 365 })
        {
            int left = (int)Math.Round((2 + leftDock) * dpi), right = (int)Math.Round((2 + rightDock) * dpi);
            int top = (int)Math.Round(2 * dpi), bottom = (int)Math.Round(2 * dpi) + 1;
            var expanded = Flyleaf.WindowRatioSizing.AroundVideo(800, 400, 960, 540, left, top, right, bottom);
            Check(expanded.Left + left == 800 && expanded.Top + top == 400 && expanded.Width - left - right == 960 && expanded.Height - top - bottom == 540,
                "Outward dock expansion moved or resized the video");
            Check(expanded.Height == 540 + top + bottom, "Opening a dock adds a title-bar row to the window height");
            var closed = Flyleaf.WindowRatioSizing.AroundVideo(expanded.Left + left, expanded.Top + top, expanded.Width - left - right, expanded.Height - top - bottom,
                (int)Math.Round(2 * dpi), (int)Math.Round(2 * dpi), (int)Math.Round(2 * dpi), bottom);
            Check(closed.Left + (int)Math.Round(2 * dpi) == 800 && closed.Top + (int)Math.Round(2 * dpi) == 400 && closed.Width == 960 + (int)Math.Round(2 * dpi) * 2,
                "Closing docks did not reverse the expansion");
            var saved = Flyleaf.WindowRatioSizing.WithoutDocks(expanded.Left, expanded.Width,
                left - Math.Round(2 * dpi), right - Math.Round(2 * dpi));
            Check(saved.Left == closed.Left && saved.Width == closed.Width, "Saving a window reserved space for its docks");
        }
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        foreach (double ratio in new[] { 16.0 / 9, 9.0 / 16, 1.0 })
        foreach (int docks in new[] { 0, 365, 690 })
        foreach (int edge in Enumerable.Range(1, 8))
        {
            int frameWidth = (int)Math.Round((docks + 4) * dpi), frameHeight = (int)Math.Round(4 * dpi);
            var size = Flyleaf.WindowRatioSizing.Constrain((int)(950 * dpi) - frameWidth, (int)(680 * dpi) - frameHeight, edge, ratio,
                Math.Max((int)Math.Ceiling(400 * dpi), (int)Math.Ceiling(404 * dpi) - frameWidth),
                Math.Max(1, (int)Math.Ceiling(360 * dpi) - frameHeight), (int)(3000 * dpi) - frameWidth, (int)(2000 * dpi) - frameHeight);
            double contentWidth = (size.Width + frameWidth) / dpi - 4;
            var fitted = FlyleafDockWidths.Fit(contentWidth - 400 - (docks == 0 ? 0 : docks == 365 ? 5 : 10), docks == 690 ? 320 : 0, docks == 690 ? 240 : 0,
                docks == 0 ? 0 : 360, docks == 0 ? 0 : 240);
            double actualVideoWidth = contentWidth - fitted.Left - fitted.Right - (docks == 0 ? 0 : docks == 365 ? 5 : 10);
            Check(actualVideoWidth * dpi >= 400 * dpi - 1 && Math.Abs(actualVideoWidth * dpi - size.Height * ratio) <= 1.1,
                "Actual dock layout lost video ratio/minimum across dock, edge or DPI changes");
        }
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        foreach (double ratio in new[] { 16.0 / 9, 9.0 / 16, 1.0 })
        {
            var minima = new List<(int Width, int Height)>();
            foreach (int docks in new[] { 0, 365 })
            {
                int frameWidth = (int)Math.Round((docks + 4) * dpi), frameHeight = (int)Math.Round(4 * dpi);
                minima.Add(Flyleaf.WindowRatioSizing.Constrain(1, 1, 2, ratio,
                    Math.Max((int)Math.Ceiling(400 * dpi), (int)Math.Ceiling(404 * dpi) - frameWidth),
                    Math.Max(1, (int)Math.Ceiling(360 * dpi) - frameHeight),
                    (int)(3000 * dpi) - frameWidth, (int)(2000 * dpi) - frameHeight));
            }
            Check(minima[0] == minima[1], "Closing a dock increased the minimum video size/portrait window height");
        }
        var narrow = FlyleafDockWidths.Fit(590, 320, 240, 360, 240);
        Check(Math.Abs(narrow.Left + narrow.Right - 590) < 0.001 && narrow.Left >= 240 && narrow.Right >= 240, "Two docks exceed the available width above minimum sizes");
        var tiny = FlyleafDockWidths.Fit(190, 320, 240, 360, 240);
        Check(Math.Abs(tiny.Left + tiny.Right - 190) < 0.001, "Tiny dock layout overflows the host budget");
    }

    static void CheckRatioResizeViewport()
    {
        const BindingFlags rendererMembers = BindingFlags.Instance | BindingFlags.NonPublic;
        var rendererType = typeof(Renderer);
        var renderer = (Renderer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(rendererType);
        var setViewport = rendererType.GetMethod("SetViewport", rendererMembers, [typeof(int), typeof(int)])!;
        var curRatio = rendererType.GetField("curRatio", rendererMembers)!;
        var fillRatio = rendererType.GetField("fillRatio", rendererMembers)!;
        rendererType.GetField("zoom", rendererMembers)!.SetValue(renderer, 1.0);

        void SetViewport(int width, int height, double ratio)
        {
            curRatio.SetValue(renderer, ratio);
            fillRatio.SetValue(renderer, width / (double)height);
            setViewport.Invoke(renderer, [width, height]);
        }

        foreach (double ratio in new[] { 16.0 / 9, 9.0 / 16, 1.0, 7.0 / 2, 2.0 / 7 })
        foreach (int edge in Enumerable.Range(1, 8))
        foreach (int offset in Enumerable.Range(0, 7))
        {
            var (width, height) = Flyleaf.WindowRatioSizing.Constrain(
                901 + offset, 549 + offset, edge, ratio, 400, 356, 1920, 1600);
            SetViewport(width, height, ratio);
            var viewport = renderer.Viewport;
            Check(viewport.X == 0 && viewport.Y == 0 && viewport.Width == width && viewport.Height == height,
                $"Ratio resize left uncovered pixels at {width} × {height} (ratio {ratio}, edge {edge})");
        }

        foreach (double dpi in new[] { 1.25, 1.5, 1.75 })
        {
            var view = new FlyleafME { UseLayoutRounding = true };
            VisualTreeHelper.SetRootDpi(view, new DpiScale(dpi, dpi));
            const BindingFlags viewMembers = BindingFlags.Instance | BindingFlags.NonPublic;
            var viewType = typeof(FlyleafME);
            var grid = (Grid)viewType.GetField("dockLayout", viewMembers)!.GetValue(view)!;
            foreach (var (field, column, dockWidth) in new[] { ("leftDock", 0, 320), ("rightDock", 4, 360) })
            {
                var dock = (ContentControl)viewType.GetField(field, viewMembers)!.GetValue(view)!;
                dock.Content = new FlyleafPanel(field, field, new Grid()) { PreferredWidth = dockWidth };
                grid.ColumnDefinitions[column].Width = new GridLength(dockWidth);
            }
            viewType.GetMethod("UpdateDockLayout", viewMembers)!.Invoke(view, null);
            var (left, right) = view.GetRequestedDockPixelInsets(dpi);
            foreach (double ratio in new[] { 16.0 / 9, 9.0 / 16 })
            foreach (int videoWidth in new[] { (int)Math.Ceiling(400 * dpi), 797 })
            {
                var (width, height) = Flyleaf.WindowRatioSizing.Constrain(
                    videoWidth, 549, 2, ratio, (int)Math.Ceiling(400 * dpi), 356, 1920, 1600);
                var size = new Size((width + left + right) / dpi, height / dpi);
                view.Measure(size);
                view.Arrange(new Rect(size));
                int actualWidth = (int)Math.Round(view.Host.ActualWidth * dpi);
                int actualHeight = (int)Math.Round(view.Host.ActualHeight * dpi);
                Check(actualWidth == width && actualHeight == height,
                    $"Dock layouts changed the requested surface at {dpi:P0} DPI to {actualWidth} × {actualHeight} from {width} × {height}");
                SetViewport(actualWidth, actualHeight, ratio);
                var viewport = renderer.Viewport;
                Check(viewport.X == 0 && viewport.Y == 0 && viewport.Width == actualWidth && viewport.Height == actualHeight,
                    $"Ratio resize left uncovered pixels with docks at {dpi:P0} DPI");
            }
        }

        SetViewport(640, 480, 16.0 / 9);
        Check(renderer.Viewport.Y == 60 && renderer.Viewport.Height == 360, "Ratio resize rounding removed letterboxing");
        SetViewport(800, 400, 3.0 / 4);
        Check(renderer.Viewport.X == 250 && renderer.Viewport.Width == 300, "Ratio resize rounding removed pillarboxing");
    }

    static void CheckOriginalSizeViewport()
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(Renderer);
        var renderer = (Renderer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var setViewport = type.GetMethod("SetViewport", members, [typeof(int), typeof(int)])!;
        void Field(string name, object value) => type.GetField(name, members)!.SetValue(renderer, value);
        void Dimensions(uint visibleHeight, double displayRatio, uint rotation = 0)
        {
            type.GetProperty(nameof(Renderer.VisibleHeight))!.SetValue(renderer, visibleHeight);
            Field("keepRatio", displayRatio);
            Field("rotation", rotation);
        }

        void CheckViewport(int width, int height, int x, int y, int viewWidth, int viewHeight, string scenario)
        {
            Field("fillRatio", width / (double)height);
            setViewport.Invoke(renderer, [width, height]);
            var viewport = renderer.Viewport;
            Check(viewport.X == x && viewport.Y == y && viewport.Width == viewWidth && viewport.Height == viewHeight,
                $"Unexpected viewport for {scenario}: {viewport.X}, {viewport.Y}, {viewport.Width} × {viewport.Height}");
        }

        Field("originalSize", true);
        Field("curRatio", 1.0);
        Field("zoom", 1.0);
        Field("zoomCenter", new Point(0.5, 0.5));
        Dimensions(301, 401.0 / 301);
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            int width = (int)(800 * dpi), height = (int)(600 * dpi);
            CheckViewport(width, height, (width - 401) / 2, (height - 301) / 2, 401, 301, $"original size at {dpi:P0} DPI");
        }

        Dimensions(1080, 16.0 / 9);
        CheckViewport(1200, 900, -360, -90, 1920, 1080, "clipped 1080p at 150% DPI");
        Field("panXOffset", 0.1);
        CheckViewport(1200, 900, -240, -90, 1920, 1080, "panned original size");
        Field("panXOffset", 0.0);
        Field("zoom", 0.5);
        CheckViewport(1200, 900, 120, 180, 960, 540, "zoom relative to original size");
        Field("zoom", 1.0);
        Dimensions(576, 16.0 / 9, 90); // 720 × 576 with a 64:45 sample aspect ratio.
        CheckViewport(1200, 900, 312, -62, 576, 1024, "rotated anamorphic video");
        Field("originalSize", false);
        CheckViewport(1200, 900, 150, 0, 900, 900, "return to fit and the selected aspect ratio");
    }

    sealed class ShortcutCaptureSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = new DrawingVisual();
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    static void RenderBindingsReview(string directory, string theme)
    {
        var keys = new KeysConfig(); keys.LoadDefault(); keys.Keys.Clear();
        keys.Add(Key.Space, KeyBindingAction.TogglePlayPause);
        keys.Add(Key.Left, KeyBindingAction.SeekBackward);
        keys.Add(Key.Right, KeyBindingAction.SeekForward);
        keys.AddCustom(Key.O, true, () => { }, "app.open", ctrl: true);
        FlyleafControlKeybindings.Attach(keys, _ => { }, addDefaultGestures: true);
        var editor = new FlyleafKeybindingsSettings();
        typeof(FlyleafSettingsEditor).GetField("config", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(editor, new Config.PlaybackConfig());
        int before = keys.Keys.Count;
        editor.BuildBindings(keys, new FlyleafMouseOptions());
        Check(keys.Keys.Count == before, "Building Bindings added or changed shortcuts");
        var panel = new FlyleafPanel("bindings", "Bindings", editor);
        FlyleafThemeManager.Apply(panel.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
        foreach (int width in new[] { 340, 760 })
        {
            RenderReview(panel, width, 1500, Path.Combine(directory, $"Bindings-{theme}-{width}.png"));
            var labels = Find<TextBlock>(editor).Where(text => text.Parent is WrapPanel && text.ToolTip is string)
                .Select(text => text.Text).ToArray();
            int nextItem = Array.IndexOf(labels, "Next item");
            Check(Array.IndexOf(labels, "Auto-next") < nextItem && nextItem > 0 &&
                labels.Skip(nextItem - 1).Take(3).SequenceEqual(["Auto-next", "Next item", "Next items · large step"]),
                "Auto-next and item navigation are not in display-label order");
            var mouse = Find<TextBlock>(editor).Single(text => text.Text == "Mouse bindings");
            var add = Find<TextBlock>(editor).Single(text => text.Text == "Additional shortcut");
            Check(mouse.TransformToAncestor(editor).Transform(new Point()).Y > add.TransformToAncestor(editor).Transform(new Point()).Y,
                "Mouse bindings were not placed after keyboard actions and Add");
            Check(Find<Button>(editor).Where(button => (button.ToolTip as string)?.StartsWith("Unassign ") == true)
                .All(button => Equals(button.Content, "") && button.Width == 28), "Unassign buttons did not use the compact trash icon");
            Check(Find<Button>(editor).Where(button => (button.ToolTip as string)?.StartsWith("Reset ") == true)
                .All(button => Equals(button.Content, "") && button.Width == 28), "Reset shortcuts did not use the shared undo icon");
        }
        var actionSelector = Find<ComboBox>(editor).Single(combo => Equals(combo.ToolTip, "Library action"));
        Check(actionSelector.SelectedItem?.ToString() == "Toggle Play Pause", "Action selector displayed its data object instead of the action label");
        var commit = Find<Button>(editor).Single(button => Equals(button.ToolTip, "Add shortcut"));
        Check(!commit.IsEnabled, "Add shortcut was enabled before capture");
        var capture = Find<TextBox>(editor).Last(text => Equals(text.Tag, "FlyleafKeyCapture"));
        capture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new ShortcutCaptureSource(), 0, Key.F24) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Check(commit.IsEnabled && keys.Keys.Count == before && capture.Text.Contains("F24"), "Capturing an additional shortcut changed bindings before Add");
    }
    static void CheckManualDelayEntry()
    {
        const long step = 50 * TimeSpan.TicksPerMillisecond;
        long delay = 0;
        var (card, _) = FlyleafPanelUI.DelaySection("audio", () => delay, value => delay = value,
            () => delay -= step, () => delay += step);
        var body = (StackPanel)card.Child;
        var row = (Grid)body.Children[0];
        var value = row.Children.OfType<TextBox>().Single();
        var error = (TextBlock)body.Children[1];
        void Enter(string text)
        {
            value.Text = text;
            value.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new ShortcutCaptureSource(), 0, Key.Enter)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
        }

        Enter((-123.5m).ToString(CultureInfo.CurrentCulture));
        Check(delay == -1235000 && error.Visibility == Visibility.Collapsed, "Enter failed to apply a fractional negative delay");
        Enter("86400001");
        Check(delay == -1235000 && error.Visibility == Visibility.Visible, "An out-of-range delay changed playback");
        value.Text = "250";
        value.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, value, null)
        {
            RoutedEvent = Keyboard.LostKeyboardFocusEvent
        });
        Check(delay == 2500000 && error.Visibility == Visibility.Collapsed, "Leaving the field failed to apply a valid delay");

        var plus = row.Children.OfType<Button>().Single(button => Equals(button.ToolTip, "Increase audio delay by one step"));
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(delay == 3000000 && value.Text == "300", "The step button did not use the manually entered delay");
    }

    static void CheckTrackPanels(string directory, string theme)
    {
        var audio = new FlyleafAudioPanel();
        var subtitles = new FlyleafSubtitlesPanel();
        var audioPanel = new FlyleafPanel("audio", "Audio", audio);
        var subtitlePanel = new FlyleafPanel("subtitles", "Subtitles", subtitles);
        var scene = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        scene.Children.Add(audioPanel);
        scene.Children.Add(subtitlePanel);
        FlyleafThemeManager.Apply(scene.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
        RenderReview(scene, 560, 470, Path.Combine(directory, $"Audio-Subtitles-{theme}.png"));

        foreach (var (panel, stream) in new[] { (audioPanel, "audio"), (subtitlePanel, "subtitle") })
        {
            var decrease = Find<Button>(panel).Single(button => Equals(button.ToolTip, $"Decrease {stream} delay by one step"));
            var increase = Find<Button>(panel).Single(button => Equals(button.ToolTip, $"Increase {stream} delay by one step"));
            var reset = Find<Button>(panel).Single(button => Equals(button.ToolTip, $"Reset {stream} delay"));
            var value = Find<TextBox>(panel).Single(input => Equals(input.ToolTip, $"Enter {stream} delay in milliseconds and press Enter"));
            double center = decrease.TranslatePoint(new Point(0, decrease.ActualHeight / 2), panel).Y;
            Check(!value.IsReadOnly && value.Text == "0" &&
                center == increase.TranslatePoint(new Point(0, increase.ActualHeight / 2), panel).Y &&
                center == reset.TranslatePoint(new Point(0, reset.ActualHeight / 2), panel).Y &&
                Math.Abs(value.TranslatePoint(new Point(0, value.ActualHeight / 2), panel).Y - center) <= 1,
                $"{stream} delay controls and editable value are not on one line");
        }

        Check(Find<TextBlock>(audio.Children[^1]).Any(text => text.Text == "Volume"), "Volume is not the last Audio section");
    }

    static void CheckLogOutputRollback()
    {
        var engineConfig = typeof(Engine).GetProperty(nameof(Engine.Config))!;
        var engineLoaded = typeof(Engine).GetProperty(nameof(Engine.IsLoaded))!;
        var stream = typeof(Logger).GetField("fileStream", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousConfig = engineConfig.GetValue(null);
        var previousLoaded = engineLoaded.GetValue(null);
        string directory = Path.Combine(Path.GetTempPath(), "Flyleaf-log-check-" + Guid.NewGuid().ToString("N"));
        string original = Path.Combine(directory, "original.log");
        string locked = Path.Combine(directory, "locked.log");
        string replacement = Path.Combine(directory, "replacement.log");
        var config = new EngineConfig { LogLevel = LogLevel.Info };
        engineConfig.SetValue(null, config);
        engineLoaded.SetValue(null, true);
        try
        {
            config.LogOutput = original;
            var log = new LogHandler();
            log.Info("before change");
            using (var blocked = new FileStream(locked, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                bool rejected = false;
                try { config.LogOutput = locked; }
                catch (IOException) { rejected = true; }
                Check(rejected && config.LogOutput == original, "An unavailable path replaced the active log destination");
            }
            log.Info("after rejected change");
            config.LogOutput = replacement;
            var contents = File.ReadAllText(original);
            Check(contents.Contains("before change") && contents.Contains("after rejected change") && File.Exists(replacement),
                "The old log stopped writing after a rejected path or failed to flush on switching files");
        }
        finally
        {
            config.LogOutput = "";
            (stream.GetValue(null) as FileStream)?.Dispose();
            stream.SetValue(null, null);
            engineLoaded.SetValue(null, previousLoaded);
            engineConfig.SetValue(null, previousConfig);
            foreach (var file in new[] { original, locked, replacement })
                if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    static void CheckPlayerProfiles(System.Windows.Threading.Dispatcher dispatcher)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Flyleaf-profile-checks-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "Flyleaf.Config.json");
        Config Defaults()
        {
            var config = new Config();
            config.Playback.KeyBindings = FlyleafME.CreateDefaultKeybindings();
            return config;
        }
        // Config normally gets these services from Engine.Start. Use an empty plugin
        // registry and the test dispatcher so the checks do not start native playback.
        var enginePlugins = typeof(Engine).GetProperty(nameof(Engine.Plugins))!;
        var coreDispatcher = typeof(Utils).GetField("uiDispatcher", BindingFlags.Static | BindingFlags.NonPublic)!;
        var plugins = (PluginsEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PluginsEngine));
        typeof(PluginsEngine).GetProperty(nameof(PluginsEngine.Types))!.SetValue(plugins, new Dictionary<string, PluginType>());
        var previousPlugins = enginePlugins.GetValue(null);
        var previousDispatcher = coreDispatcher.GetValue(null);
        enginePlugins.SetValue(null, plugins);
        coreDispatcher.SetValue(null, dispatcher);
        try
        {
            Flyleaf.ConfigStore Load()
            {
                var store = new Flyleaf.ConfigStore(directory);
                store.LoadBeforeEngine(() => new EngineConfig());
                store.LoadProfiles(Defaults());
                return store;
            }

            var store = Load();
            var profiles = store.Profiles;
            var first = profiles.Open();
            var older = profiles.Open();
            var initialId = first.Id;
            Check(first.Id == older.Id && profiles.Current.Profiles.Single().Name == "Default"
                && !ReferenceEquals(first.Player, older.Player) && ReferenceEquals(first.Presentation, older.Presentation),
                "Windows using the same profile must have independent players and shared presentation");
            Check(first.Presentation.Appearance.Theme == FlyleafTheme.Dark &&
                first.Presentation.Appearance.Accent == FlyleafAccent.Firebrick,
                "The initial profile did not inherit the factory appearance");
            profiles.Observe(first);
            profiles.Observe(older);
            first.Player.Playback.SeekOffset = 202;
            int presentationChanges = 0;
            first.Presentation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FlyleafUIConfig.Subtitles)) presentationChanges++;
            };
            older.Presentation.Subtitles.FontSize = 37;
            Check(first.Presentation.Subtitles.FontSize == 37 && presentationChanges == 1,
                "A subtitle edit in the second window did not reach the first window immediately");
            first.Player.Video.DecoderThreads = 6;
            first.Player.Video.FLFilters[FLFilters.Brightness] = new()
                { Filter = FLFilters.Brightness, Minimum = -100, Maximum = 100, Value = 13 };
            first.Player.Playback.KeyBindings.Keys.Single(k => k.Action == KeyBindingAction.OpenFromFileDialog).Key = Key.F9;
            profiles.Capture(first);
            Check(older.Player.Playback.SeekOffset != 202, "A player edit silently changed another open player");

            older.Player.Demuxer.FormatOpt["other-window"] = "merged";
            older.Player.Video.FLFilters[FLFilters.Brightness] = new()
                { Filter = FLFilters.Brightness, Minimum = -100, Maximum = 100, Value = 0 };
            profiles.Capture(older);
            var fresh = profiles.Open();
            Check(fresh.Player.Playback.SeekOffset == 202 && fresh.Presentation.Subtitles.FontSize == 37
                && fresh.Player.Demuxer.FormatOpt["other-window"] == "merged"
                && fresh.Player.Video.FLFilters[FLFilters.Brightness].Value == 13,
                "An older window's snapshot overwrote newer edits to the same profile");
            Check(!File.Exists(path), "Capturing a window wrote configuration before exit");

            profiles.Rename(first, "Everyday");
            profiles.Copy(first, "Cinema");
            var cinemaId = first.Id;
            var cinemaPeer = profiles.Open(cinemaId);
            first.Presentation.Subtitles.FontSize = 50;
            first.Player.Playback.SeekOffset = 303;
            profiles.Capture(first);
            profiles.SetResetSource(initialId);
            profiles.Reset(first, player: false);
            Check(first.Presentation.Subtitles.FontSize == 37 && cinemaPeer.Presentation.Subtitles.FontSize == 37
                && first.Player.Playback.SeekOffset == 303,
                "Resetting Media Control did not reach both windows or changed Player settings");
            older.Presentation.Subtitles.FontSize = 22;
            Check(first.Presentation.Subtitles.FontSize == 37,
                "A presentation change leaked into a copied profile");
            profiles.SetResetSource(null);
            profiles.Reset(first, player: true);
            Check(first.Player.Playback.SeekOffset == Defaults().Playback.SeekOffset
                && first.Player.Video.DecoderThreads == 6 && first.PlayerSettingsPending,
                "Player reset did not distinguish updates for the open window from full config");

            first.Player.Demuxer.FormatOpt["after-reset"] = "edited";
            profiles.Capture(first);
            profiles.SetResetSource(initialId);
            store.Application.Window.Left = -130;
            store.Application.Window.Top = 80;
            Check(!store.Application.Startup.AllowTransparency, "Transparency was enabled by default");
            store.Application.Startup.KeepInTray = false;
            store.Application.Startup.SingleInstance = false;
            store.Application.Startup.AllowTransparency = true;
            store.Application.Window.StartupPanelLayout = Flyleaf.StartupPanelLayoutMode.LastUsed;
            var placement = new FlyleafPanelPlacement("test-panel", FlyleafPanelMode.Floating, FlyleafPanelSide.Left, 320, true,
                new FlyleafFloatingPanelBounds(-150, 60, 320, 400));
            store.Application.Window.PanelLayout = [placement];
            store.Engine.LogLevel = LogLevel.Info;
            store.Application.CustomFileExtensions = [".m2p"];
            store.Engine.LogOutput = Path.Combine(directory, "Flyleaf.log");
            Check(store.Save() && File.Exists(path) && !store.Save(), "Saving did not skip a second unchanged exit snapshot");

            var reloaded = Load();
            Check(reloaded.Application.CustomFileExtensions.SequenceEqual([".m2p"]), "Custom extensions did not survive app config save");
            var cinema = reloaded.Profiles.Open();
            var everyday = reloaded.Profiles.Open(initialId);
            Check(reloaded.LoadError == null && cinema.Id == cinemaId && reloaded.Profiles.Current.ResetSource == initialId
                && reloaded.Application.Window.Left == -130 && reloaded.Application.Window.Top == 80 && reloaded.Engine.LogLevel == LogLevel.Info,
                "Consolidated config lost the selection, reset source, engine settings, or window position");
            Check(reloaded.Engine.LogOutput == Path.Combine(directory, "Flyleaf.log"), "The configured log path did not survive saving");
            Check(!reloaded.Application.Startup.KeepInTray && !reloaded.Application.Startup.SingleInstance &&
                reloaded.Application.Startup.AllowTransparency, "Startup preferences did not persist");
            Check(reloaded.Application.Window.StartupPanelLayout == Flyleaf.StartupPanelLayoutMode.LastUsed
                && reloaded.Application.Window.GetStartupPanelLayout()!.Single() == placement,
                "Panel restore preference or floating placement did not survive saving");
            Check(cinema.Player.Video.DecoderThreads == Defaults().Video.DecoderThreads && cinema.Presentation.Subtitles.FontSize == 37
                && cinema.Player.Demuxer.FormatOpt["after-reset"] == "edited" && !cinema.Player.Demuxer.FormatOpt.ContainsKey("other-window"),
                "Editing a window after reset overwrote pending player defaults");
            Check(everyday.Presentation.Subtitles.FontSize == 22 && everyday.Player.Video.FLFilters[FLFilters.Brightness].Value == 13
                && everyday.Player.Playback.KeyBindings.Keys.Single(key => key.Action == KeyBindingAction.OpenFromFileDialog).Key == Key.F9,
                "Serialization lost a live subtitle edit, video filter, or shortcut");
            Check(!reloaded.Save(), "Loading the saved configuration caused an unnecessary write");

            var reloadedProfiles = reloaded.Profiles;
            Check(reloadedProfiles.GetResetSource(everyday) == null && reloadedProfiles.GetResetSource(cinema) == initialId,
                "The preferred reset source must fall back to factory defaults only in windows using that profile");
            reloadedProfiles.Reset(everyday, player: false);
            Check(everyday.Presentation.Subtitles.FontSize == new FlyleafUIConfig().Subtitles.FontSize
                && everyday.Player.Playback.SeekOffset == 202 && cinema.Presentation.Subtitles.FontSize == 37,
                "Resetting the preferred reset profile's Media Control did not apply only its factory defaults");
            reloadedProfiles.Reset(everyday, player: true);
            Check(everyday.Player.Playback.SeekOffset == Defaults().Playback.SeekOffset && everyday.PlayerSettingsPending
                && reloadedProfiles.Current.ResetSource == initialId && reloadedProfiles.GetResetSource(cinema) == initialId,
                "Resetting the preferred reset profile's Player did not apply factory defaults or preserve the shared reset preference");

            reloadedProfiles.Observe(cinema);
            cinema.Player.Playback.SeekOffset = 414;
            cinema.Player.Video.DecoderThreads = 6;
            reloadedProfiles.Create(cinema, "Fresh");
            var created = reloadedProfiles.Open();
            Check(cinema.Id == created.Id && created.Id != cinemaId
                && reloadedProfiles.Current.Profiles.Single(profile => profile.Id == created.Id).Name == "Fresh"
                && cinema.Player.Playback.SeekOffset == Defaults().Playback.SeekOffset && cinema.PlayerSettingsPending
                && ReferenceEquals(cinema.Presentation, created.Presentation)
                && created.Presentation.Subtitles.FontSize == new FlyleafUIConfig().Subtitles.FontSize,
                "Creating a profile did not activate factory defaults for Player and Media Control");
            reloadedProfiles.Capture(cinema);
            var original = reloadedProfiles.Open(cinemaId);
            Check(original.Player.Playback.SeekOffset == 414 && original.Player.Video.DecoderThreads == 6
                && original.Presentation.Subtitles.FontSize == 37
                && reloadedProfiles.Open().Player.Video.DecoderThreads == Defaults().Video.DecoderThreads,
                "Creating a profile lost edits to the active profile or carried its pending player settings into the new profile");
            cinema.Presentation.Subtitles.FontSize = 51;
            reloadedProfiles.Create(cinema, "Fresh again");
            Check(cinema.Presentation.Subtitles.FontSize == new FlyleafUIConfig().Subtitles.FontSize
                && created.Presentation.Subtitles.FontSize == 51,
                "New profiles share mutable presentation settings with Factory defaults");

            File.WriteAllText(path, "{invalid configuration JSON");
            var damaged = Load();
            Check(damaged.LoadError != null && !damaged.Save() && File.ReadAllText(path) == "{invalid configuration JSON",
                "An invalid configuration was overwritten");
            File.Delete(path);

            var legacyApp = new Flyleaf.AppConfig();
            legacyApp.Window.Width = 1234;
            File.WriteAllText(Path.Combine(directory, "Flyleaf.AppConfig.json"), legacyApp.Serialize());
            var legacyUI = new FlyleafUIConfig();
            legacyUI.Subtitles.FontSize = 43;
            File.WriteAllText(Path.Combine(directory, "Flyleaf.UIConfig.json"), FlyleafUIConfigSerializer.Serialize(legacyUI));
            var legacyPlayer = Defaults();
            legacyPlayer.Playback.SeekOffset = 777;
            var legacy = new Flyleaf.PlayerProfiles.LegacyDocument(1, cinemaId,
                [new(cinemaId, "Default", System.Text.Json.Nodes.JsonNode.Parse(legacyPlayer.Serialize())!.AsObject())]);
            File.WriteAllText(Path.Combine(directory, "Flyleaf.PlayerProfiles.json"), System.Text.Json.JsonSerializer.Serialize(legacy));
            var imported = Load();
            var importedSession = imported.Profiles.Open();
            Check(imported.LoadError == null && importedSession.Id == cinemaId && importedSession.Player.Playback.SeekOffset == 777
                && importedSession.Presentation.Subtitles.FontSize == 43 && imported.Application.Window.Width == 1234
                && !File.Exists(path) && imported.Save(), "Legacy files failed to migrate into the consolidated config");
        }
        finally
        {
            enginePlugins.SetValue(null, previousPlugins);
            coreDispatcher.SetValue(null, previousDispatcher);
            foreach (var name in new[] { "Config", "AppConfig", "UIConfig", "PlayerProfiles" })
            {
                var file = Path.Combine(directory, $"Flyleaf.{name}.json");
                if (File.Exists(file)) File.Delete(file);
            }
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    static void CheckComposedSwapDrops()
    {
        using var view = new FlyleafME();
        using var legacy = new FlyleafLib.Controls.WPF.FlyleafHost();
        var host = typeof(FlyleafLib.Controls.WPF.FlyleafHost);
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var canStart = host.GetMethod("CanStartPlayerSwap", instance)!;
        Check(view.Host.SwapOnDrop == HostTargets.None
            && (bool)canStart.Invoke(legacy, [HostTargets.Surface])!
            && !(bool)canStart.Invoke(legacy, [HostTargets.Overlay])!, "Player swap start defaults changed");
        view.Host.SwapOnDrop = HostTargets.Both;
        Check(!(bool)canStart.Invoke(view.Host, [HostTargets.Surface])!
            && !(bool)canStart.Invoke(view.Host, [HostTargets.Overlay])!,
            "Raw host configuration re-enabled player swap initiation in FlyleafME");

        var payloadType = host.GetNestedType("FlyleafHostDropWrap", BindingFlags.NonPublic)!;
        var payload = Activator.CreateInstance(payloadType, nonPublic: true)!;
        payloadType.GetField("FlyleafHost")!.SetValue(payload, legacy);
        var swap = new DataObject(payloadType, payload);
        var dragArgs = typeof(DragEventArgs).GetConstructor(instance, null,
            [typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point)], null)!;
        DragEventArgs Invoke(string handler, RoutedEvent routedEvent, FlyleafLib.Controls.WPF.FlyleafHost target, IDataObject data)
        {
            var args = (DragEventArgs)dragArgs.Invoke([data, DragDropKeyStates.ShiftKey, DragDropEffects.Move, target, new Point()]);
            args.RoutedEvent = routedEvent;
            host.GetMethod(handler, instance)!.Invoke(target, [target, args]);
            return args;
        }

        foreach (var (handler, routedEvent) in new[]
        {
            ("Host_DragEnter", DragDrop.DragEnterEvent), ("Host_PreviewDragOver", DragDrop.PreviewDragOverEvent),
            ("Surface_Drop", DragDrop.DropEvent), ("Overlay_Drop", DragDrop.DropEvent)
        })
        {
            var args = Invoke(handler, routedEvent, view.Host, swap);
            Check(args.Handled && args.Effects == DragDropEffects.None, $"{handler} allowed a legacy player swap into FlyleafME");
        }

        Check(!Invoke("Host_PreviewDragOver", DragDrop.PreviewDragOverEvent, legacy, swap).Handled,
            "The legacy host rejected a player swap");
        foreach (var media in new[] { new DataObject(DataFormats.FileDrop, new[] { @"C:\Media\video.mp4" }),
            new DataObject(DataFormats.Text, "https://example.invalid/video") })
        {
            Check(!Invoke("Host_DragEnter", DragDrop.DragEnterEvent, view.Host, media).Handled
                && !Invoke("Host_PreviewDragOver", DragDrop.PreviewDragOverEvent, view.Host, media).Handled,
                "The FlyleafME swap guard consumed a regular media drag");
        }
    }

    static void CheckComposedPlaybackBar()
    {
        using var first = new FlyleafME();
        using var second = new FlyleafME();
        first.RegisterPanel(new FlyleafPanelDefinition("video", "Custom video", _ => new Grid()));
        first.RegisterDefaultPanels();
        first.RegisterDefaultPanels();
        second.RegisterDefaultPanels();
        Check(first.RegisteredPanels.Count == 7 && first.RegisteredPanels.Single(p => p.Id == "video").Title == "Custom video",
            "Built-in registration overwrote an application panel or duplicated definitions");

        var bar = new FlyleafPlaybackBar { MediaElement = first };
        Check(ReferenceEquals(bar.QueueSource, first.Queue) && ReferenceEquals(bar.InformationSource, first.Information)
            && ReferenceEquals(bar.Options, first.UIConfig.PlaybackBar) && bar.CanOpenSettings,
            "Composed playback bar did not connect its owner");
        var explicitOptions = new FlyleafPlaybackBarOptions();
        bar.Options = explicitOptions;
        bar.MediaElement = second;
        Check(ReferenceEquals(bar.QueueSource, second.Queue) && ReferenceEquals(bar.Options, explicitOptions),
            "Switching owners broke a connection or overwrote an explicit option");
        bar.MediaElement = null;
        Check(bar.QueueSource == null && bar.InformationSource == null && ReferenceEquals(bar.Options, explicitOptions),
            "Disconnecting a composed playback bar kept owner bindings or cleared a caller's setting");

        FlyleafPlaylistAction? requested = null;
        first.PlaylistActionRequested += (_, e) => requested = e.Action;
        first.PlaylistActionsAvailable = true;
        var general = (ContextMenu)typeof(FlyleafME).GetMethod("BuildContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(first, null)!;
        MenuItem Menu(ItemsControl parent, string title) => parent.Items.OfType<MenuItem>().Single(item => Equals(item.Header, title));
        IEnumerable<string> Headers(ItemsControl parent) => parent.Items.OfType<MenuItem>().Select(item => (string)item.Header);
        var input = Menu(general, "Input");
        var playback = Menu(general, "Playback");
        Check(Menu(input, "Open media…").IsEnabled && !Menu(playback, "Speed").IsEnabled,
            "General menu lost its Open action or enabled speeds without playable media");
        Check(Headers(general).SequenceEqual(["Input", "Queue", "Playback", "Video", "Audio", "Subtitles", "Media information", "Fullscreen", "Settings…"]),
            "General menu groups are out of order");
        Check(Headers(playback).SequenceEqual(["Speed", "Chapters", "Frame stepping", "Open playback panel…"]),
            "Playback menu still contains transport shortcuts or has misplaced actions");
        var queue = Menu(general, "Queue");
        Check(!Headers(queue).Contains("Add folder…"), "An application without folder import exposed the action");
        first.SupportsFolderImport = true;
        var folderMenu = (ContextMenu)typeof(FlyleafME).GetMethod("BuildContextMenu", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(first, null)!;
        Menu(Menu(folderMenu, "Queue"), "Add folder…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(requested == FlyleafPlaylistAction.AddFolder, "The folder action did not reach the application");

        bar.MediaElement = first;
        var buildMore = typeof(FlyleafPlaybackBar).GetMethod("BuildMoreMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var moreWithFolder = (ContextMenu)buildMore.Invoke(bar, [new Button()])!;
        requested = null;
        Menu(moreWithFolder, "Add folder…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(requested == FlyleafPlaylistAction.AddFolder, "The playback bar's More menu did not offer folder import");
        var document = Menu(queue, "Playlist document");
        Check(Headers(document).SequenceEqual(["New queue", "Save playlist", "Save playlist as…", "Export playlist…"]),
            "Queue document actions are split up");
        Menu(document, "Save playlist").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Check(requested == FlyleafPlaylistAction.Save, "Moving the save command lost its playlist action");

        bar.MediaElement = second;
        var more = (ContextMenu)buildMore.Invoke(bar, [new Button()])!;
        Check(more.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Panels")).Items.Count == 6
            && more.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "General menu…")),
            "More menu did not group the registered panels or expose the general menu");
    }

    static void CheckPlaybackPanelLayout(string directory, string theme)
    {
        var editor = new FlyleafPlaybackPanel();
        var panel = new FlyleafPanel("playback", "Playback", editor);
        FlyleafThemeManager.Apply(panel.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
        RenderReview(panel, 300, 680, Path.Combine(directory, $"PlaybackPanel-{theme}.png"));

        StackPanel SectionBody(string title) => editor.Children.OfType<Border>()
            .Select(card => (StackPanel)card.Child)
            .Single(body => body.Children[0] is TextBlock text && text.Text == title);
        var speed = SectionBody("Speed");
        Check(speed.Children.Count == 3 && speed.Children[1] is TextBlock { Text: "Multiplier (0.125–16×)" }
            && speed.Children[2] is Grid { Children: [TextBox { Text: "1" }, Button { Content: "Set" }, ComboBox presets] }
            && presets.Items.Count == 7 && presets.SelectedItem is ComboBoxItem { Content: "Presets" },
            "Speed does not show an inline multiplier field, Set button, and preset dropdown");
        Check(SectionBody("Playback mode").Children.Count == 3 && SectionBody("Frame stepping").Children.Count == 2,
            "Playback mode or Frame stepping still contains explanatory text");
    }

    static void CheckSettingsScrolling(string directory, string theme)
    {
        var settings = new FlyleafSettingsPanel
        {
            Context = new FlyleafPanelContext(new FlyleafME(), "scroll-check")
        };
        settings.Pages.Clear();
        foreach (var category in new[] { "Application", "Profiles", "Playback", "Audio", "Video", "Subtitles", "Demuxer", "Plugins", "Bindings" })
            settings.Pages.Add(new(category, category, _ => new SettingsLayoutProbe { MinHeight = 640 }));
        settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var panel = new FlyleafPanel(new FlyleafPanelDefinition("scroll-check", "Settings", _ => settings) { HandlesScrolling = true }, settings);
        var size = new Size(700, 248);
        panel.Measure(size);
        panel.Arrange(new Rect(size));
        panel.UpdateLayout();

        var navigation = Find<ScrollViewer>(settings).Single(scroll => scroll.Content is StackPanel);
        var page = Find<ScrollViewer>(settings).Single(scroll => scroll.Content is ContentControl);
        var firstCategory = Find<Button>(navigation).First();
        var navigationBar = (ScrollBar)navigation.Template.FindName("PART_VerticalScrollBar", navigation);
        var pageBar = (ScrollBar)page.Template.FindName("PART_VerticalScrollBar", page);
        Check(navigation.ComputedVerticalScrollBarVisibility == Visibility.Visible && page.ComputedVerticalScrollBarVisibility == Visibility.Visible
            && navigationBar.ActualWidth == 10 && pageBar.ActualWidth == 10,
            $"{theme} Settings scrollbar widths were {navigationBar.ActualWidth} and {pageBar.ActualWidth} DIPs");
        Point Position(FrameworkElement element) => element.TranslatePoint(new Point(0, 0), settings);
        Check(Math.Abs(Position(navigationBar).X - Position(firstCategory).X - firstCategory.ActualWidth) < 0.1
            && Position(page).X - Position(navigationBar).X - navigationBar.ActualWidth >= 12,
            theme + " category scrollbar is detached from the rail or crowds the page");
        double pageRightGap = panel.ActualWidth - pageBar.TranslatePoint(new Point(pageBar.ActualWidth, 0), panel).X;
        Check(Math.Abs(pageRightGap - 5) < 0.1, $"{theme} Settings page scrollbar is {pageRightGap} DIPs from the panel edge");
        var thumb = (Thumb)pageBar.Template.FindName("ScrollThumb", pageBar);
        var thumbCenter = thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), panel);
        var hit = VisualTreeHelper.HitTest(panel, thumbCenter)?.VisualHit;
        Check(hit != null && (ReferenceEquals(hit, thumb) || thumb.IsAncestorOf(hit)),
            theme + " Settings page scrollbar cannot be hit in the panel padding");
        double expectedViewport = settings.ActualWidth - Position(page).X - pageBar.ActualWidth - 8;
        Check(Math.Abs(page.ViewportWidth - expectedViewport) < 0.1, theme + " moving the page scrollbar changed the text viewport width");

        page.ScrollToVerticalOffset(120);
        navigation.ScrollToVerticalOffset(72);
        panel.UpdateLayout();
        var categoryOffset = navigation.VerticalOffset;
        Check(page.VerticalOffset == 120 && categoryOffset > 0, theme + " Settings scroll test could not scroll both regions");
        var video = Find<Button>(navigation).Single(button => Equals(button.Content, "Video"));
        var oldContent = ((ContentControl)page.Content).Content;
        video.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        panel.UpdateLayout();
        Check(!ReferenceEquals(oldContent, ((ContentControl)page.Content).Content) && page.VerticalOffset == 0,
            theme + " new Settings category inherited the previous page's vertical offset");
        Check(navigation.VerticalOffset == categoryOffset, theme + " changing categories reset the navigation scrollbar");

        RenderReview(panel, (int)size.Width, (int)size.Height, Path.Combine(directory, $"Settings-Scroll-{theme}.png"));
        settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        var horizontal = new ScrollViewer
        {
            Content = new Border { Width = 400 },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        horizontal.Measure(new Size(200, 80));
        horizontal.Arrange(new Rect(0, 0, 200, 80));
        horizontal.UpdateLayout();
        var horizontalBar = (ScrollBar)horizontal.Template.FindName("PART_HorizontalScrollBar", horizontal);
        Check(horizontal.ComputedHorizontalScrollBarVisibility == Visibility.Visible && horizontalBar.ActualHeight == 10,
            $"{theme} horizontal scrollbar height was {horizontalBar.ActualHeight} DIPs");
    }

    static void CheckInformationLayout(string directory, string theme)
    {
        var owner = new FlyleafME();
        using var information = owner.Information;
        information.SourceSection.Update(
        [
            new("Title", "Example media"),
            new("Location", @"C:\Media\A longer directory name\An example file with a long title.mkv"),
            new("Container", "Matroska")
        ]);
        information.VideoSection.Update([new("Codec", "HEVC"), new("Size", "3840 × 2160"), new("Frame rate", "23.976 fps")]);
        information.AudioSection.Update([new("Codec", "AAC"), new("Sample rate", "48,000 Hz")]);
        information.StatisticsSection.Update([new("Total bitrate", "1250 kb/s"), new("Presented rate", "23.976 fps")]);

        var editor = new FlyleafInformationPanel { Context = new FlyleafPanelContext(owner, "information") };
        var panel = new FlyleafPanel("information", "Media information", editor);
        FlyleafThemeManager.Apply(panel.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
        editor.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        RenderReview(panel, 330, 480, Path.Combine(directory, $"Information-{theme}.png"));
        var scroll = Find<ScrollViewer>(panel).Single();
        Check(scroll.ScrollableHeight > 0 && scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible,
            "Media Information overflows without a vertical scrollbar");
        var body = editor.Children.OfType<StackPanel>().Single();
        Check(body.Children[0] is CheckBox { Content: "Show statistics overlay" },
            "The statistics overlay toggle is not at the top of Media Information");
        var sections = body.Children.OfType<Border>().Select(card => card.Child).OfType<Expander>().ToArray();
        Check(sections.Select(section => (section.Header as TextBlock)?.Text).SequenceEqual(
            ["Source", "Playback", "Processing", "Video", "Audio", "Subtitles", "Data", "Statistics", "Diagnostics"]),
            "Media Information section order is incorrect");
        var expanders = Find<Expander>(panel).ToArray();
        Check(expanders.Length == 12 && expanders.All(section => section.IsExpanded),
            "Media Information sections must start expanded");
        var fields = Find<TextBox>(panel).ToArray();
        var text = fields.Select(t => t.Text).ToArray();
        Check(text.Contains("Location") && text.Contains("HEVC") && text.Contains("23.976 fps"),
            "Media Information rows are missing their labels or values");
        Check(fields.All(field => field.IsReadOnly) &&
            fields.Where(field => field.Visibility != Visibility.Collapsed)
                .All(field => field.Template.FindName("PART_ContentHost", field) is Border),
            "Media Information fields do not use the selectable read-only template");
        var codec = fields.Single(field => field.Text == "HEVC");
        codec.SelectAll();
        information.VideoSection.Update([new("Codec", "HEVC"), new("Size", "1920 × 1080"), new("Frame rate", "23.976 fps")]);
        panel.UpdateLayout();
        Check(Find<TextBox>(panel).Contains(codec) && codec.SelectedText == "HEVC",
            "Refreshing another field interrupted text selection");
        var diagnostics = Find<Expander>(panel).Single(expander => expander.Header is TextBlock { Text: "Diagnostics" });
        var copy = Find<Button>(panel).Single(button => Equals(button.Content, "Copy information"));
        Check(copy.TransformToAncestor(editor).Transform(new Point()).Y > diagnostics.TransformToAncestor(editor).Transform(new Point()).Y,
            "Copy information is above Diagnostics");
        editor.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    static void RenderPlaybackBarReview(string directory, string theme)
    {
        using var queue = new FlyleafQueue();
        var bar = new FlyleafPlaybackBar { QueueSource = queue, VerticalAlignment = VerticalAlignment.Bottom };
        int settingsOpens = 0;
        bar.SettingsRequested += (_, _) => settingsOpens++;
        var scene = new Grid { Background = new SolidColorBrush(Color.FromRgb(18, 23, 29)) };
        FlyleafThemeManager.Apply(scene.Resources, theme == "Dark" ? FlyleafTheme.Dark : FlyleafTheme.Light);
        scene.Children.Add(bar);
        scene.Measure(new Size(1100, 120)); scene.Arrange(new Rect(0, 0, 1100, 120)); scene.UpdateLayout();
        T Part<T>(string name) where T : DependencyObject => (T)bar.Template.FindName(name, bar);
        bar.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        bar.Options.Visibility = FlyleafVisibilityPolicy.Never;
        Check(bar.Visibility == Visibility.Collapsed, "Playback bar ignored the Never visibility option");
        bar.Visibility = Visibility.Visible;
        Check(bar.Visibility == Visibility.Visible, "A host could not supply its own playback bar visibility");
        bar.ClearValue(UIElement.VisibilityProperty);
        bar.Options.Visibility = FlyleafVisibilityPolicy.Always;
        Check(bar.Visibility == Visibility.Visible, "Playback bar ignored the Always visibility option");
        bar.Options.Visibility = FlyleafVisibilityPolicy.Auto;
        Check(bar.Visibility == Visibility.Visible, "The unconnected playback bar was hidden in Auto mode");

        var autoNext = Part<System.Windows.Controls.Primitives.ToggleButton>("PART_AutoNext");
        var speed = Part<Button>("PART_Speed");
        Check(speed.ToolTip is string tip && tip.Contains("1×") && bar.SpeedNeedleAngle == 0,
            "Playback speed does not report a neutral gauge at 1×");
        var previous = Part<Button>("PART_Previous");
        var next = Part<Button>("PART_Next");
        bool NavigationVisible() => autoNext.Visibility == Visibility.Visible &&
            previous.Visibility == Visibility.Visible && next.Visibility == Visibility.Visible;
        bool NavigationHidden() => autoNext.Visibility == Visibility.Collapsed &&
            previous.Visibility == Visibility.Collapsed && next.Visibility == Visibility.Collapsed;
        Check(NavigationHidden(), "An empty queue showed Auto-next or item navigation");
        queue.Add("C:/Media/One.mp4");
        Check(NavigationHidden(), "A single-item queue showed Auto-next or item navigation");
        queue.Add("C:/Media/Two.mp4");
        Check(NavigationVisible() && autoNext.IsChecked == false, "Playback bar did not show the queue's Auto-next state");
        autoNext.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
        Check(queue.AutoAdvance, "Playback bar toggle did not update the queue");
        queue.AutoAdvance = false;
        Check(autoNext.IsChecked == false, "Playback bar toggle missed an Auto-next change");

        // Populate template visuals with representative metadata without starting an engine.
        Part<TextBlock>("PART_VideoDetail").SetCurrentValue(TextBlock.TextProperty, "4K · HEVC · HDR10");
        Part<TextBlock>("PART_AudioDetail").SetCurrentValue(TextBlock.TextProperty, "ENG · 5.1");
        Part<TextBlock>("PART_SubtitleDetail").SetCurrentValue(TextBlock.TextProperty, "ENG");
        Part<TextBlock>("PART_CurrentTime").SetCurrentValue(TextBlock.TextProperty, "00:12:34");
        Part<TextBlock>("PART_Duration").SetCurrentValue(TextBlock.TextProperty, "01:48:20");
        Part<Slider>("PART_HorizontalVolume").SetCurrentValue(Slider.MaximumProperty, 100.0);
        Part<Slider>("PART_HorizontalVolume").SetCurrentValue(Slider.ValueProperty, 65.0);
        foreach (var button in Find<Button>(bar)) button.SetCurrentValue(UIElement.IsEnabledProperty, true);
        foreach (int width in new[] { 400, 480, 560, 720, 1100, 400 })
        {
            RenderReview(scene, width, 120, Path.Combine(directory, $"PlaybackBar-{theme}-{width}.png"));
            Check(bar.ActualHeight <= 82 && bar.ActualHeight >= 72, "Playback bar lost the compact two-row height");
            var buttons = Find<Button>(bar).Where(button => button.Visibility == Visibility.Visible)
                .Select(button => (Button: button, Bounds: new Rect(button.TransformToAncestor(bar).Transform(new Point()), button.RenderSize)))
                .OrderBy(item => item.Bounds.Left).ToArray();
            foreach (var item in buttons)
                Check(item.Bounds.Left >= 8 - 0.5 && item.Bounds.Right <= width - 8 + 0.5, $"Playback control extends outside the bar at {width}");
            for (int index = 1; index < buttons.Length; index++)
                Check(buttons[index - 1].Bounds.Right <= buttons[index].Bounds.Left + 0.5, $"Playback groups overlap at {width}");
            Check(Part<Button>("PART_Play").Visibility == Visibility.Visible && Part<Button>("PART_Previous").Visibility == Visibility.Visible &&
                Part<Button>("PART_Next").Visibility == Visibility.Visible && Part<Button>("PART_Fullscreen").Visibility == Visibility.Visible,
                "Narrow playback layout removed primary transport/fullscreen actions");
            var volume = Part<Button>("PART_Volume");
            Check(autoNext.Visibility == Visibility.Visible &&
                autoNext.TranslatePoint(new Point(autoNext.ActualWidth, 0), bar).X <= volume.TranslatePoint(new Point(), bar).X,
                "Auto-next is missing or does not sit before volume");
            var popup = Part<System.Windows.Controls.Primitives.Popup>("PART_VolumePopup");
            Check(((FrameworkElement)popup.Child).DataContext == bar, "Vertical volume popup lost the playback bar binding context");
            if (width == 400 && Part<Button>("PART_Settings").Visibility == Visibility.Collapsed)
            {
                var menu = new ContextMenu();
                typeof(FlyleafPlaybackBar).GetMethod("AddOverflowActions", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bar, [menu]);
                var settings = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Settings…"));
                int before = settingsOpens;
                settings.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(settingsOpens == before + 1, "Overflowed Settings action became inaccessible");
            }
            if (width == 400) Check(bar.IsVolumeCompact, "Narrow playback bar kept the horizontal volume slider despite insufficient room");
            if (width >= 560) Check(!bar.IsVolumeCompact, "Volume collapsed while enough room remained for a horizontal slider");
            if (width == 1100) Check(speed.Visibility == Visibility.Visible && Part<TextBlock>("PART_VideoDetail").Visibility == Visibility.Visible &&
                Part<TextBlock>("PART_AudioDetail").Visibility == Visibility.Visible, "Wide layout failed to restore stream details");
        }
        queue.Remove(queue.Sources[1]);
        Check(NavigationHidden(), "Removing the second queue item left playback navigation visible");
        bar.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }
    static void RenderDockSplitterPreview(string directory, string theme)
    {
        var preview = new Grid();
        foreach (int width in new[] { 180, 5, 330, 5, 180 }) preview.ColumnDefinitions.Add(new() { Width = new GridLength(width) });
        foreach (var (column, title) in new[] { (0, "Audio"), (2, "Playback area"), (4, "Queue") })
        {
            var label = new TextBlock { Text = title, Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Top };
            label.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
            var surface = new Border { Child = label }; surface.SetResourceReference(Border.BackgroundProperty, column == 2 ? "FlyleafME.RaisedSurfaceBrush" : "FlyleafME.SurfaceBrush");
            Grid.SetColumn(surface, column); preview.Children.Add(surface);
        }
        foreach (int column in new[] { 1, 3 })
        {
            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
            splitter.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.DockSplitterStyle");
            Grid.SetColumn(splitter, column); preview.Children.Add(splitter);
        }
        RenderReview(preview, 700, 240, Path.Combine(directory, $"DockSplitters-{theme}.png"));
    }

    static void RenderReview(FrameworkElement visual, int width, int height, string path)
    {
        visual.Measure(new Size(width, height)); visual.Arrange(new Rect(0, 0, width, height)); visual.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }

    static void CheckDockSettingsTransitions()
    {
        // Host disposal logs through Engine.Config; provide only logging settings,
        // without starting FFmpeg, graphics, audio or the engine thread.
        var original = Engine.Config;
        var configProperty = typeof(Engine).GetProperty(nameof(Engine.Config))!;
        configProperty.SetValue(null, new EngineConfig { LogLevel = LogLevel.Quiet });
        try { CheckDockSettingsLayout(); CheckPanelLayoutRestore(); }
        finally { configProperty.SetValue(null, original); }
        Check(!Engine.IsLoaded, "Dock review unexpectedly started the media engine");
    }

    static void CheckPanelLayoutRestore()
    {
        using var view = new FlyleafME();
        int factories = 0;
        foreach (var id in new[] { "left", "right", "closed" })
            view.RegisterPanel(new FlyleafPanelDefinition(id, id, _ => { factories++; return new StackPanel(); }));

        // Mark just this control loaded so the test can call the real registry
        // without showing a window or starting the native host.
        var loaded = typeof(FrameworkElement).GetProperty("IsLoadedCache", BindingFlags.NonPublic | BindingFlags.Instance)!;
        loaded.SetValue(view, true);
        try
        {
            view.Measure(new Size(900, 600)); view.Arrange(new Rect(0, 0, 900, 600));
            var closed = new FlyleafPanelPlacement("closed", FlyleafPanelMode.Overlay, FlyleafPanelSide.Left, 350, false);
            view.RestorePanelLayout([
                new("left", FlyleafPanelMode.Docked, FlyleafPanelSide.Left, 460, true),
                new("right", FlyleafPanelMode.Docked, FlyleafPanelSide.Right, 365, true),
                closed,
                new("unknown", FlyleafPanelMode.Overlay, FlyleafPanelSide.Right, 320, true)]);
            Check(factories == 2 && view.GetRequestedDockInsets() == (465, 370),
                "Restore lost a requested dock width or created a closed or unknown panel");
            var layout = view.CapturePanelLayout();
            Check(layout.Length == 3 && layout.Contains(closed) && layout.Count(panel => panel.IsOpen) == 2,
                "Capturing the layout lost closed placement or open docks");
            var opened = view.ShowPanel("closed");
            Check(opened is { IsOpen: true, Mode: FlyleafPanelMode.Overlay, Side: FlyleafPanelSide.Left, PreferredWidth: 350 },
                "Opening a previously closed panel ignored its restored placement");

            var unopened = new FlyleafPanelDefinition("unopened", "Unopened", _ => new StackPanel()) { PreferredWidth = 310 };
            view.RegisterPanel(unopened);
            view.RestorePanelLayout([new("unopened", FlyleafPanelMode.Docked, FlyleafPanelSide.Left, 420, false)]);
            view.ResetPanelLayout();
            var reset = view.CapturePanelLayout();
            Check(view.GetRequestedDockInsets() == (0, 0) && reset.Length == 3 && reset.All(panel => !panel.IsOpen),
                "Resetting the panel layout kept a panel open or kept an unopened override");
            var reopened = view.ShowPanel("left");
            Check(reopened is { IsOpen: true, Mode: FlyleafPanelMode.Overlay, Side: FlyleafPanelSide.Right, PreferredWidth: 300 },
                "Resetting the panel layout kept a custom placement on an existing panel");
            var unopenedReset = view.ShowPanel("unopened");
            Check(unopenedReset is { IsOpen: true, Mode: FlyleafPanelMode.Overlay, Side: FlyleafPanelSide.Right, PreferredWidth: 310 },
                "Resetting the panel layout kept a deferred custom placement");
        }
        finally { loaded.SetValue(view, false); }
    }

    static void CheckMessageLifetime()
    {
        var options = new FlyleafOSDOptions { DurationSeconds = 0.5 };
        var osd = new FlyleafOSDOverlay { Options = options };
        Check(!osd.IsHitTestVisible && !osd.Focusable, "OSD intercepts playback input");
        osd.ShowMessage("First"); PumpDispatcher(300); osd.ShowMessage("Second"); PumpDispatcher(300);
        Check(osd.Message == "Second" && osd.Visibility == Visibility.Visible, "Older message timeout cleared its replacement");
        PumpDispatcher(350);
        Check(osd.Message == "" && osd.Visibility == Visibility.Collapsed, "OSD did not expire");
        osd.ShowMessage("Visible"); options.Enabled = false;
        Check(osd.Message == "" && osd.Visibility == Visibility.Collapsed, "Disabling OSD did not clear it");
        osd.ShowMessage("Disabled"); Check(osd.Message == "", "Disabled OSD displayed a message");
        options.Enabled = true; osd.ShowMessage("Unload"); osd.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Check(osd.Message == "", "Unloaded OSD retained a message/timer");
        osd.ShowMessage("Operation failed", error: true); osd.ShowMessage("Volume 60%");
        Check(osd.Message == "Operation failed", "Routine feedback replaced an unexpired error");
        options.ShowErrors = false; Check(osd.Message == "", "Error setting failed to clear an action error");
        options.ShowErrors = true; options.Enabled = false; osd.ShowMessage("Still an error", error: true);
        Check(osd.Message == "Still an error", "Disabling action messages disabled independently enabled errors"); osd.Clear();
        osd.ShowMessage("Paused", status: true); Check(osd.Message == "Paused", "Disabling action messages disabled independently enabled status");
        options.ShowStatus = false; Check(osd.Message == "", "Status setting failed to clear a brief status message");
        options.ShowStatus = true; options.Enabled = true; osd.ShowMessage("Paused", status: true); osd.ClearStatusMessage();
        Check(osd.Message == "", "Resuming playback left a Paused status message");
        osd.ShowMessage("Volume 50%"); osd.ClearStatusMessage(); Check(osd.Message == "Volume 50%", "Resume cleared unrelated action feedback");
        osd.ShowMessage("Error", error: true); osd.ClearStatusMessage(); Check(osd.Message == "Error", "Resume cleared an operation error"); osd.Clear();
    }
    static void CheckPlaybackDisplay()
    {
        var options = new FlyleafOSDOptions();
        var overlay = new FlyleafPlaybackStatusOverlay { Options = options };
        Check(!overlay.StatusVisible && !overlay.TimeVisible, "Empty player displays an opening/time indicator");
        var playing = new FlyleafPlaybackDisplay(Status.Playing, true, ActivityMode.Active, TimeSpan.FromSeconds(754).Ticks, TimeSpan.FromMinutes(105).Ticks, false, false, false);
        overlay.Update(playing);
        Check(overlay.TimeVisible && overlay.TimeText == "00:12:34 / 01:45:00", "Keyboard activity lacks compact time");
        overlay.Update(playing with { Activity = ActivityMode.FullActive, BarVisible = true });
        Check(!overlay.TimeVisible, "Mouse playback bar duplicates compact time");
        overlay.Update(playing with { Activity = ActivityMode.Idle }); Check(!overlay.TimeVisible, "Auto time remains in idle");
        options.TimeDisplay = FlyleafVisibilityPolicy.Always; Check(overlay.TimeVisible, "Always time disappears in idle");
        options.TimeDisplay = FlyleafVisibilityPolicy.Never; overlay.Seek(true, 900000); Check(!overlay.TimeVisible, "Seeking overrides Never time preference");
        options.TimeDisplay = FlyleafVisibilityPolicy.Auto;
        Check(overlay.TimeVisible && overlay.TimeText.StartsWith("00:15:00"), "Seek target is not revealed in compact time");
        overlay.Update(playing with { Time = 0, Activity = ActivityMode.Idle });
        Check(overlay.TimeText.StartsWith("00:15:00"), "Old playback position overwrote an in-progress seek target");
        overlay.Seek(false, 900000); overlay.CancelOperations();
        overlay.SetBuffering(true); Check(!overlay.StatusVisible, "Buffering flashes without delay");
        overlay.SetBuffering(false); PumpDispatcher(350); Check(!overlay.StatusVisible, "Canceled buffering indicator appeared late");
        overlay.SetBuffering(true); PumpDispatcher(350);
        Check(overlay.StatusVisible && overlay.StatusText == "Buffering…", "Long buffering has no persistent indicator");
        overlay.SetBuffering(false); Check(!overlay.StatusVisible, "Completed buffering indicator remained");
        overlay.Error("Fatal error"); Check(overlay.StatusVisible && overlay.StatusText == "Fatal error", "Blocking error did not persist");
        options.ShowErrors = false; Check(!overlay.StatusVisible, "Error visibility setting was ignored");
        options.ShowErrors = true; overlay.NewInput(); Check(!overlay.StatusVisible, "New input retained a blocking error");
        overlay.Update(playing with { Status = Status.Failed, CanPlay = false });
        overlay.Measure(new Size(640, 360)); overlay.Arrange(new Rect(0, 0, 640, 360)); overlay.UpdateLayout();
        Find<Button>(overlay).Single(b => Equals(b.Content, "Dismiss")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!overlay.StatusVisible, "Dismiss left the generic Failed status visible");
        Check(FlyleafPlaybackStatusOverlay.FormatTime(0, 0, true) == "LIVE" && FlyleafPlaybackStatusOverlay.FormatTime(TimeSpan.FromSeconds(30).Ticks, TimeSpan.FromMinutes(2).Ticks, true) == "LIVE · 00:00:30 / 00:02:00", "Live/DVR time is misleading");
        Check(FlyleafPlaybackStatusOverlay.FormatTime(TimeSpan.FromHours(25).Ticks, TimeSpan.FromHours(26).Ticks, false).Contains("1.01:00:00"), "Long duration wraps after 24 hours");
        options.ShowStatus = false; options.ShowErrors = false; options.TimeDisplay = FlyleafVisibilityPolicy.Always;
        var saved = FlyleafUIConfigSerializer.Deserialize(FlyleafUIConfigSerializer.Serialize(new FlyleafUIConfig { OSD = options }));
        Check(!saved.OSD.ShowStatus && !saved.OSD.ShowErrors && saved.OSD.TimeDisplay == FlyleafVisibilityPolicy.Always, "OSD status/error/time preferences lost on save");
        overlay.Reset();
    }
    static void PumpDispatcher(int milliseconds)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    static void CheckVideoFramingLayout(FlyleafVideoPanel video)
    {
        Button Action(string label) => Find<Button>(video).Single(b => Equals(b.ToolTip, label));
        Point Position(FrameworkElement element) => element.TranslatePoint(new Point(), video);
        var zoomOut = Action("Zoom out");
        var zoomIn = Action("Zoom in");
        var rotateLeft = Action("Rotate left 90°");
        var rotateRight = Action("Rotate right 90°");
        Check(Math.Abs(Position(zoomOut).X - Position(rotateLeft).X) < 0.1
            && Math.Abs(Position(zoomIn).X - Position(rotateRight).X) < 0.1
            && zoomOut.ActualWidth == rotateLeft.ActualWidth && zoomOut.ActualHeight == rotateLeft.ActualHeight
            && Position(rotateRight).X + rotateRight.ActualWidth <= video.ActualWidth + 0.1,
            "Framing zoom and rotation steps do not share the same columns and size");

        var horizontal = Find<CheckBox>(video).Single(c => Equals(c.Content, "Horizontal"));
        var vertical = Find<CheckBox>(video).Single(c => Equals(c.Content, "Vertical"));
        Check(Math.Abs(Position(horizontal).Y - Position(vertical).Y) < 0.1, "Mirror checkboxes do not share a baseline");
        var reset = Find<Button>(video).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Reset framing to defaults");
        Check(reset.Parent is StackPanel heading && heading.Children.OfType<TextBlock>().Single().Text == "Framing",
            "The framing reset action is detached from its section heading");
        Check(Find<Button>(video).Any(b => Equals(b.Content, "Fit to window  ▾")), "Framing is missing its Size selector");
    }

    static void CheckSubtitleSettingsAppearance(string directory, string theme)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var settings = new FlyleafSubtitlesSettings
        {
            Context = new FlyleafPanelContext(new FlyleafME(), "subtitle-settings")
        };
        typeof(FlyleafSettingsEditor).GetField("config", members)!.SetValue(settings, new Config.SubtitlesConfig());
        typeof(FlyleafSubtitlesSettings).GetMethod("Build", members)!.Invoke(settings, null);
        var panel = new FlyleafPanel("subtitle-settings", "Subtitles settings", settings);
        RenderReview(panel, 680, 730, Path.Combine(directory, $"SubtitleSettings-{theme}.png"));

        var editor = Find<FlyleafSubtitleAppearanceEditor>(settings).Single();
        var group = (StackPanel)editor.Parent;
        var header = (StackPanel)((Border)group.Children[0]).Child;
        Check(header.Children.OfType<Button>().Single().ToolTip as string == "Reset text appearance to defaults"
            && !Find<Button>(editor).Any(button => Equals(button.ToolTip, "Reset appearance")),
            theme + " subtitle appearance reset is missing from the header or duplicated in the editor");
        var font = Find<ComboBox>(editor).Single();
        var size = Find<TextBox>(editor).First(input => input.Width == 76);
        foreach (int width in new[] { 680, 340 })
        {
            panel.Measure(new Size(width, 730));
            panel.Arrange(new Rect(0, 0, width, 730));
            panel.UpdateLayout();
            double fontX = font.TranslatePoint(new Point(), editor).X;
            double sizeX = size.TranslatePoint(new Point(), editor).X;
            Check(Math.Abs(fontX - sizeX) < 0.1 && font.ActualWidth <= 260 && fontX + font.ActualWidth <= editor.ActualWidth + 0.1,
                $"{theme} font picker failed to align with the size field at {width} DIPs");
        }
    }

    static void RenderSubtitlePreview(string directory, string theme)
    {
        var style = new FlyleafSubtitleOptions { Color = "#FFFFE8C0" };
        var text = new FlyleafSubtitleText { Options = style, Text = "Flyleaf subtitle text\nΕλληνικά · العربية · 日本語", VideoViewport = new Rect(0, 0.12, 1, 0.76) };
        var scene = new Grid { Background = Brushes.Black };
        scene.Children.Add(new Border { Margin = new Thickness(0, 72, 0, 72), Background = new LinearGradientBrush(System.Windows.Media.Color.FromRgb(40, 75, 110), System.Windows.Media.Color.FromRgb(160, 100, 45), 20) });
        scene.Children.Add(text);
        var osd = new FlyleafOSDOverlay { Options = new(), Margin = new Thickness(14) }; osd.ShowMessage("Volume 65%"); scene.Children.Add(osd);
        RenderReview(scene, 960, 600, Path.Combine(directory, $"TextOverlays-{theme}.png")); osd.Clear();
        byte[] Draw()
        {
            text.Measure(new Size(500, 240)); text.Arrange(new Rect(0, 0, 500, 240)); text.UpdateLayout();
            var bitmap = new RenderTargetBitmap(500, 240, 96, 96, PixelFormats.Pbgra32); bitmap.Render(text);
            byte[] pixels = new byte[500 * 240 * 4]; bitmap.CopyPixels(pixels, 500 * 4, 0); return pixels;
        }
        var initial = Draw(); style.Color = "#FF00FF00";
        Check(!initial.SequenceEqual(Draw()), "Subtitle color change did not invalidate rendering");
        style.OutlineThickness = 0; var noOutline = Draw(); style.OutlineThickness = 6;
        Check(!noOutline.SequenceEqual(Draw()), "Subtitle outline change did not invalidate rendering");
        text.Text = ""; Check(Draw().All(b => b == 0), "Empty subtitles left visible text behind");
        text.Text = new string('W', 10000); text.Measure(new Size(0, 0)); text.Arrange(new Rect(0, 0, 0, 0)); text.UpdateLayout();
        Check(!text.IsHitTestVisible && !text.Focusable, "Subtitles intercept playback input");
    }

    static void CheckDockSettingsLayout()
    {
        // Exercise the real dock layout without loading the native video host.
        using var view = new FlyleafME();
        int requestedOffset = 0; view.ItemNavigationRequested += (_, e) => { requestedOffset = e.Offset; e.Handled = true; };
        view.ToggleQueueAutoAdvance();
        Check(view.Queue.AutoAdvance, "Control slideshow action failed to start queue auto-play");
        view.ToggleQueueAutoAdvance();
        Check(!view.Queue.AutoAdvance, "Control slideshow action failed to stop queue auto-play");
        view.PreviousItem(); Check(requestedOffset == -1, "Control previous navigation did not reach the host override");
        view.NextItems(); Check(requestedOffset == 10, "Default large navigation step differs from V1");
        view.ItemNavigationLargeStep = 5; view.PreviousItems(); Check(requestedOffset == -5, "Configured large step was ignored");
        Check(view.ActivityTimeout == 2800 && view.Host.ActivityTimeout == 2800, "Default auto-hide delay is not forwarded to the host");
        view.UIConfig.PlaybackBar.AutoHideDelayMilliseconds = 4250;
        Check(view.ActivityTimeout == 4250 && view.Host.ActivityTimeout == 4250, "Editing auto-hide delay did not update the live host");
        var idleSaved = FlyleafUIConfigSerializer.Deserialize(FlyleafUIConfigSerializer.Serialize(view.UIConfig));
        Check(idleSaved.PlaybackBar.AutoHideDelayMilliseconds == 4250 && view.UIConfig.Clone().PlaybackBar.AutoHideDelayMilliseconds == 4250, "Auto-hide delay did not survive save/clone");
        view.UIConfig = idleSaved; view.UIConfig.PlaybackBar.AutoHideDelayMilliseconds = 0;
        Check(view.ActivityTimeout == 0 && view.Host.ActivityTimeout == 0, "Zero auto-hide delay was not forwarded after UI config replacement");
        Check(FlyleafUIConfigSerializer.Deserialize("{\"SchemaVersion\":1,\"UIConfig\":{}}").PlaybackBar.AutoHideDelayMilliseconds == 2800, "Old UI defaults changed auto-hide behavior");
        view.MoveSubtitlesUp(); view.IncreaseSubtitleTextSize();
        Check(view.UIConfig.Subtitles.BottomOffset == 50 && view.UIConfig.Subtitles.FontSize == 50, "Control subtitle actions do not use V1's two-unit steps");
        view.MoveSubtitlesDown(); view.DecreaseSubtitleTextSize();
        Check(view.UIConfig.Subtitles.BottomOffset == 48 && view.UIConfig.Subtitles.FontSize == 48, "Opposite control subtitle actions are inconsistent");
        view.UIConfig.Subtitles.BottomOffset = 0; view.MoveSubtitlesDown();
        view.UIConfig.Subtitles.FontSize = 8; view.DecreaseSubtitleTextSize();
        Check(view.UIConfig.Subtitles.BottomOffset == 0 && view.UIConfig.Subtitles.FontSize == 8, "Subtitle actions crossed lower editor bounds");
        view.UIConfig.Subtitles.BottomOffset = 500; view.MoveSubtitlesUp();
        view.UIConfig.Subtitles.FontSize = 160; view.IncreaseSubtitleTextSize();
        Check(view.UIConfig.Subtitles.BottomOffset == 500 && view.UIConfig.Subtitles.FontSize == 160, "Subtitle actions crossed upper editor bounds");
        T Field<T>(string name) => (T)typeof(FlyleafME).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        using (var headersQueue = new FlyleafQueue())
        {
            headersQueue.ApplyEntries([new() { Input = "https://example.com/live.m3u8", UserAgent = "Chrome", Referrer = "https://example.com/player" }, new() { Input = "https://example.com/next.m3u8" }]);
            var item = new FlyleafLib.MediaFramework.MediaPlaylist.PlaylistItem { HTTPHeaders = new() { ["X-Test"] = "keep" } };
            headersQueue.Sources[0].ApplyHttpOptions(item);
            Check(item.HTTPHeaders["User-Agent"] == "Chrome" && item.HTTPHeaders["X-Test"] == "keep" && item.Referrer == "https://example.com/player", "Queue did not apply per-entry HTTP options to the library item");
            var captured = headersQueue.CaptureEntries();
            Check(captured[0].UserAgent == "Chrome" && captured[0].Referrer == "https://example.com/player" && captured[1].UserAgent == null, "Queue capture lost or leaked channel options");
        }
        using (var jumpQueue = new FlyleafQueue())
        {
            jumpQueue.ApplyEntries(Enumerable.Range(0, 15).Select(i => new FlyleafQueueEntry { Input = $"https://example.com/{i}.mp4" }));
            typeof(FlyleafQueue).GetField("currentSource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(jumpQueue, jumpQueue.Sources[0]);
            Check(jumpQueue.NavigationTarget(10)?.Source == jumpQueue.Sources[10], "Large queue jump selected an intermediate source");
            var children = Enumerable.Range(0, 15).Select(i => new FlyleafLib.MediaFramework.MediaPlaylist.PlaylistItem { DirectUrl = $"https://example.com/child/{i}" }).ToArray();
            jumpQueue.Sources[0].Update(children, children[0]);
            Check(jumpQueue.NavigationTarget(10)?.Child?.Position == 10, "Large jump ignored a known child sequence");
            Check(jumpQueue.NavigationTarget(20)?.Source == jumpQueue.Sources[6], "Large jump did not cross a known child sequence correctly");
        }
        using (var positionQueue = new FlyleafQueue())
        {
            var first = positionQueue.Add("first.mp4");
            var group = positionQueue.Add("group");
            var last = positionQueue.Add("last.mp4");
            group.Update(Enumerable.Range(0, 3).Select(i => new FlyleafLib.MediaFramework.MediaPlaylist.PlaylistItem { DirectUrl = $"https://example.com/{i}" }).ToArray(), null);
            typeof(FlyleafQueue).GetMethod("NotifyNavigation", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(positionQueue, null);
            Check(first.ItemNumber == 1 && group.IndexText == "2–4" && group.Children[1].ItemNumber == 3 && last.ItemNumber == 5,
                "Row indexes do not match the flattened footer order");
            Check(positionQueue.ItemCountText == "5 items", "Queue without an active item reported a misleading current position");
            typeof(FlyleafQueue).GetField("currentSource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(positionQueue, group);
            typeof(FlyleafQueue).GetField("navigationChild", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(positionQueue, group.Children[1]);
            Check(positionQueue.CurrentItemNumber == 3 && positionQueue.ItemCountText == "3 / 5 items", "Queue position double-counted an embedded parent");
            positionQueue.MoveBefore([last], first);
            Check(positionQueue.ItemCountText == "4 / 5 items", "Queue position did not follow visible reorder");
            Check(last.ItemNumber == 1 && group.Children[1].ItemNumber == 4, "Queue row indexes did not update after reordering");
            positionQueue.Clear();
            Check(positionQueue.CurrentItemNumber == 0 && positionQueue.ItemCountText == "0 items", "Clearing queue retained a current-item number");
        }
        var slide = new FlyleafQueueProgression();
        bool Step(double ms, Status status = Status.Playing, long duration = 0, long clock = 0,
            bool image = true, bool live = true, bool enabled = true, bool blocked = false) =>
            slide.Update(ms, 3000, status, duration, clock, image, live, enabled, blocked);
        Check(!Step(0, Status.Ended) && !Step(2999, Status.Ended) && Step(1, Status.Ended), "Zero-duration JPEG did not honor the slideshow interval");
        Check(!Step(3000, Status.Ended), "Slideshow advanced the same item more than once");
        slide.Reset();
        Check(!Step(2999) && Step(1), "Zero-time live-flagged PNG did not advance without Ended");
        slide.Reset();
        Check(!Step(1000) && !Step(9000, Status.Paused) && !Step(1999) && Step(1), "Paused slideshow time leaked into the countdown");
        slide.Reset();
        Check(!Step(1000) && !Step(9000, enabled: false) && !Step(1999) && Step(1), "Disabled auto-advance consumed the slideshow interval");
        slide.Reset();
        Check(!Step(1000) && !Step(10, clock: 1) && !Step(10000), "Unknown-duration animated PNG was advanced by the image timer");
        Check(Step(0, Status.Ended), "Unknown-duration animation did not advance after its observed clock and Ended");
        slide.Reset();
        Check(!Step(6000, duration: 1, live: false) && Step(0, Status.Ended, duration: 1, live: false), "Timed video did not wait for Ended");
        slide.Reset();
        Check(!Step(9000, image: false) && !Step(9000, Status.Ended, clock: 1, image: false), "Known live source was treated as a slide");
        slide.Reset();
        Check(!Step(9000, blocked: true) && !Step(2999) && Step(1), "Playback errors/loop/reverse failed to block slideshow advancement");
        slide.Reset();
        Check(!Step(2999), "Opening a new item retained the previous countdown");
        Check(FlyleafQueueProgression.IsImageFormat("png_pipe") && FlyleafQueueProgression.IsImageFormat("jpeg_pipe") &&
            FlyleafQueueProgression.IsImageFormat("apng") && !FlyleafQueueProgression.IsImageFormat("hls") &&
            !FlyleafQueueProgression.IsImageFormat("dshow"), "Image candidate format detection includes live sources or excludes image inputs");
        slide.Reset();
        Check(!slide.Update(0, 3000, Status.Ended, 400000, 0, true, false, true, false, true) &&
            !slide.Update(2999, 3000, Status.Ended, 400000, 0, true, false, true, false, true) &&
            slide.Update(1, 3000, Status.Ended, 400000, 0, true, false, true, false, true),
            "image2 single-frame JPEG synthetic duration bypassed the slideshow interval");
        slide.Reset();
        Check(!slide.Update(1000, 3000, Status.Playing, 400000, 1, true, false, true, false, true) &&
            slide.Update(0, 3000, Status.Ended, 400000, 1, true, false, true, false, true),
            "Observed animation clock did not override an estimated single-frame count");
        slide.Reset();
        bool webpImage = FlyleafQueueProgression.IsImageFormat("webp_anim");
        Check(webpImage && !slide.Update(4000, 3000, Status.Playing, 0, 33400000, webpImage, true, true, false) &&
            slide.Update(0, 3000, Status.Ended, 0, 33400000, webpImage, true, true, false),
            "Animated WebP was stopped by its zero-duration live flag or advanced before Ended");
        var slideUI = new FlyleafUIConfig();
        Check(slideUI.Queue.SlideShowIntervalMilliseconds == 3000, "Slideshow lost V1's three-second default");
        slideUI.Queue.SlideShowIntervalMilliseconds = 4500;
        var slideCopy = slideUI.Clone(); slideCopy.Queue.SlideShowIntervalMilliseconds = 6000;
        Check(slideUI.Queue.SlideShowIntervalMilliseconds == 4500, "Slideshow options were shared by cloned UI profiles");
        int queueNotifications = 0;
        slideUI.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FlyleafUIConfig.Queue)) queueNotifications++; };
        slideUI.Mouse = new FlyleafMouseOptions();
        slideUI.Queue.SlideShowIntervalMilliseconds = 5000;
        Check(queueNotifications == 1, "Replacing mouse options duplicated slideshow subscriptions");
        slideUI.Queue.SlideShowIntervalMilliseconds = 4500;
        Check(FlyleafUIConfigSerializer.Deserialize(FlyleafUIConfigSerializer.Serialize(slideUI)).Queue.SlideShowIntervalMilliseconds == 4500, "Slideshow interval did not persist");
        Check(FlyleafUIConfigSerializer.Deserialize("""{"SchemaVersion":1,"UIConfig":{}}""").Queue.SlideShowIntervalMilliseconds == 3000, "Older UI documents lost slideshow defaults");
        using (var shuffledQueue = new FlyleafQueue())
        {
            shuffledQueue.ApplyEntries(Enumerable.Range(0, 12).Select(i => new FlyleafQueueEntry { Input = $"https://example.com/{i}" }));
            var original = shuffledQueue.Sources.ToArray();
            typeof(FlyleafQueue).GetMethod("SetCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(shuffledQueue, [original[4]]);
            shuffledQueue.ShuffleItems();
            Check(!shuffledQueue.Sources.SequenceEqual(original) && shuffledQueue.Sources.ToHashSet().SetEquals(original), "Shuffle did not visibly reorder the same queue entries");
            Check(ReferenceEquals(shuffledQueue.CurrentSource, original[4]), "Shuffle changed the playing source");
            var position = shuffledQueue.Sources.IndexOf(original[4]);
            shuffledQueue.Repeat = true;
            Check(ReferenceEquals(shuffledQueue.NavigationTarget(1)?.Source, shuffledQueue.Sources[(position + 1) % original.Length]), "Shuffle playback order differs from the visible queue");
            var added = shuffledQueue.Add("https://example.com/added");
            Check(ReferenceEquals(shuffledQueue.Sources.Last(), added), "Add silently randomized the queue after a one-shot shuffle");
        }
        using (var titleQueue = new FlyleafQueue())
        {
            var title = new TextBlock();
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(FlyleafQueue.CurrentTitle)) { Source = titleQueue }));
            var fallback = new DataTrigger { Binding = new System.Windows.Data.Binding(nameof(FlyleafQueue.CurrentTitle)) { Source = titleQueue }, Value = null };
            fallback.Setters.Add(new Setter(TextBlock.TextProperty, new System.Windows.Data.Binding { Source = "Core media title" }));
            style.Triggers.Add(fallback); title.Style = style;
            Check(title.Text == "Core media title", "Title bar lost its core title fallback");
            var channel = titleQueue.Add(new FlyleafQueueEntry { Input = "https://example.com/channel", Title = "Channel name" });
            var select = typeof(FlyleafQueue).GetMethod("SetCurrent", BindingFlags.NonPublic | BindingFlags.Instance)!;
            select.Invoke(titleQueue, [channel]);
            Check(title.Text == "Channel name", "Imported channel title did not take priority over the core title");
            select.Invoke(titleQueue, [null]);
            Check(title.Text == "Core media title", "Title bar retained a queue title after clearing the active source");
        }
        using (var failureQueue = new FlyleafQueue())
        {
            failureQueue.ApplyEntries(Enumerable.Range(0, 4).Select(i => new FlyleafQueueEntry { Input = $"https://example.com/{i}.mp4" }));
            // A failed open has no CurrentSource; navigation must retain the attempted position.
            typeof(FlyleafQueue).GetField("navigationSource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(failureQueue, failureQueue.Sources[2]);
            Check(failureQueue.CurrentSource == null && failureQueue.NavigationTarget(1)?.Source == failureQueue.Sources[3], "Next after failure restarted the queue");
            Check(failureQueue.NavigationTarget(-1)?.Source == failureQueue.Sources[1], "Previous after failure lost the attempted position");
            var original = failureQueue.Sources.ToArray();
            failureQueue.MoveBefore([original[1], original[2]], original[0]);
            Check(failureQueue.Sources.SequenceEqual(new[] { original[1], original[2], original[0], original[3] }), "Dragging multiple entries lost their relative order");
            failureQueue.Remove([original[1], original[3]]);
            Check(failureQueue.Sources.SequenceEqual(new[] { original[2], original[0] }), "Multiple removal removed unselected entries");
            failureQueue.Clear();
            Check(failureQueue.TotalItems == 0 && failureQueue.NavigationTarget(1) == null, "Clear retained navigation state");
            failureQueue.Add("https://example.com/single");
            failureQueue.Repeat = true;
            typeof(FlyleafQueue).GetField("navigationSource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(failureQueue, failureQueue.Sources[0]);
            Check(failureQueue.NavigationTarget(1)?.Source == failureQueue.Sources[0], "Single-item repeat did not wrap");
        }
        view.HeaderContent = new Border { Height = 32 };
        var root = Field<Grid>("overlayRoot"); root.Measure(new Size(1280, 700)); root.Arrange(new Rect(0, 0, 1280, 700)); root.UpdateLayout();
        Check(Field<FlyleafOSDOverlay>("osd").Margin.Top == Field<ContentPresenter>("overlayHeader").ActualHeight + 12 && Field<FlyleafPlaybackStatusOverlay>("playbackDisplay").Margin.Top == Field<ContentPresenter>("overlayHeader").ActualHeight + 12, $"Left/right OSD did not retain top spacing below the header: {Field<FlyleafOSDOverlay>("osd").Margin.Top}, {Field<FlyleafPlaybackStatusOverlay>("playbackDisplay").Margin.Top}, header={Field<ContentPresenter>("overlayHeader").ActualHeight}");
        var messages = Field<FlyleafOSDOverlay>("osd");
        view.UIConfig.OSD.Position = FlyleafOSDPosition.TopLeft;
        messages.ShowMessage("An OSD message");
        root.UpdateLayout();
        Check(messages.Visibility == Visibility.Visible, "OSD message was not shown");
        messages.Clear();
        void Layout(int width)
        {
            typeof(FlyleafME).GetMethod("UpdateDockLayout", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, null);
            view.Measure(new Size(width, 700)); view.Arrange(new Rect(0, 0, width, 700)); view.UpdateLayout();
        }
        var grid = Field<Grid>("dockLayout");
        var left = Field<ContentControl>("leftDock"); var right = Field<ContentControl>("rightDock");
        var registered = Field<Dictionary<string, FlyleafPanel>>("panels");
        FlyleafPanel Dock(string id, ContentControl host, int column, double width)
        {
            var panel = new FlyleafPanel(id, id, new StackPanel()) { Mode = FlyleafPanelMode.Docked, IsOpen = true, PreferredWidth = width };
            panel.AttachContext(view, new FlyleafPanelContext(view, id)); registered.Add(id, panel);
            host.Content = panel; grid.ColumnDefinitions[column].Width = new GridLength(width);
            return panel;
        }
        var leftPanel = Dock("left-review", left, 0, 320);
        var rightPanel = Dock("queue", right, 4, 360);
        var settings = new FlyleafPanel(new FlyleafPanelDefinition("settings", "Settings", _ => new StackPanel()) { FillOverlayArea = true }, new StackPanel());
        settings.AttachContext(view, new FlyleafPanelContext(view, "settings")); registered.Add("settings", settings);
        view.HeaderContent = new Border { Height = 32 };
        Layout(1280);
        Check(left.ActualWidth == 320 && right.ActualWidth == 360, "Initial dock widths are incorrect");
        Check(view.HeaderHeight == 0 && ReferenceEquals(Field<ContentPresenter>("overlayHeader").Content, view.HeaderContent) && Math.Abs(view.Host.ActualHeight - view.ActualHeight) < 0.01,
            "Docking moved the title bar into an extra layout row");
        var rightResize = Field<System.Windows.Controls.GridSplitter>("rightSplitter");
        rightResize.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
        rightResize.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-40, 0));
        Layout(1280);
        Check(right.ActualWidth == 400, "Right splitter cannot grow the dock");
        rightResize.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(-40, 0, false));
        rightResize.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
        rightResize.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(40, 0));
        Layout(1280);
        Check(right.ActualWidth == 360, "Right splitter cannot shrink the dock");
        rightResize.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(40, 0, false));
        var leftResize = Field<System.Windows.Controls.GridSplitter>("leftSplitter");
        leftResize.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
        leftResize.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(40, 0)); Layout(1280);
        Check(left.ActualWidth == 360, "Left splitter cannot grow the dock");
        leftResize.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(40, 0, false));
        leftResize.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
        leftResize.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(-40, 0)); Layout(1280);
        Check(left.ActualWidth == 320, "Left splitter cannot shrink the dock");
        leftResize.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(-40, 0, false));
        Layout(1000);
        Check(view.ActualWidth <= 1000.01 && view.DockWidth <= 600.01 && view.Host.ActualWidth >= 399.99 && view.Host.ActualWidth + view.DockWidth <= 1000.01,
            "Visible docks overflow a narrow window and invalidate ratio geometry");
        Check(view.RequestedDockWidth == 690, "Narrow layout lost the requested widths needed for explicit ratio resizing");
        Layout(1280);
        for (int i = 0; i < 4; i++)
        {
            settings.IsOpen = true;
            typeof(FlyleafME).GetField("overlayPanel", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, settings);
            Field<Grid>("overlayPanels").Children.Add(settings);
            Layout(i % 2 == 0 ? 1280 : 700);
            Check(left.Visibility == Visibility.Collapsed && right.Visibility == Visibility.Collapsed && view.DockWidth == 0, "Settings did not hide dock columns");
            Check(view.RequestedDockWidth == 0, "Ratio sizing reserved hidden dock space while Settings is open");
            Check(view.GetRequestedDockInsets() == (325, 365) && view.GetDockInsets(1280) == (325, 365),
                "A temporary overlay hid the dock space from saved window geometry");
            Check(ReferenceEquals(left.Content, leftPanel) && ReferenceEquals(right.Content, rightPanel) && grid.ColumnDefinitions[4].Width.Value == 360, "Settings lost dock editors or requested widths");
            view.TogglePanel("settings"); Layout(1280);
            Check(!settings.IsOpen && ReferenceEquals(registered["settings"], settings) && left.ActualWidth == 320 && right.ActualWidth == 360, "Settings toggle did not restore the same docks and widths");
        }
        settings.IsOpen = true;
        typeof(FlyleafME).GetField("overlayPanel", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, settings);
        Field<Grid>("overlayPanels").Children.Add(settings); Layout(1280);
        view.TogglePanel("queue");
        Check(!rightPanel.IsOpen && right.Content == null && rightPanel.PreferredWidth == 360 && settings.IsOpen, "Closing a hidden queue saved its collapsed width or closed Settings");
        view.TogglePanel("settings"); Layout(1280);
        Check(left.ActualWidth == 320 && right.ActualWidth == 0, "Settings restored a deliberately closed dock");
    }

    static void RenderLogoPreview(string repository)
    {
        var preview = new Grid { Width = 880, Height = 280 };
        for (int i = 0; i < 4; i++) preview.ColumnDefinitions.Add(new());
        foreach (var (title, index) in new[] { ("Original bitmap", 0), ("Vector · brand red", 1), ("Vector · dark theme", 2), ("Vector · light theme", 3) })
        {
            var body = new StackPanel();
            var card = new Border { Child = body, Padding = new Thickness(16) };
            FlyleafThemeManager.Apply(card.Resources, index == 3 ? FlyleafTheme.Light : FlyleafTheme.Dark);
            card.SetResourceReference(Border.BackgroundProperty, "FlyleafME.SurfaceBrush");
            Grid.SetColumn(card, index); preview.Children.Add(card);
            var label = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush"); body.Children.Add(label);
            FrameworkElement Mark(int size)
            {
                if (index == 0) return new Image { Width = size, Height = size, Source = new BitmapImage(new Uri(Path.Combine(repository, "Images", "Flyleaf_TitleBar_128.png"))) };
                var logo = new FlyleafLogo { Width = size, Height = size };
                if (index == 1)
                {
                    logo.MarkBrush = new SolidColorBrush(Color.FromRgb(255, 48, 40)); logo.FoldBrush = new SolidColorBrush(Color.FromRgb(205, 19, 37));
                    logo.PlayBrush = Brushes.White; logo.OutlineBrush = new SolidColorBrush(Color.FromRgb(32, 43, 57));
                }
                return logo;
            }
            body.Children.Add(Mark(128));
            var sizes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
            foreach (int size in new[] { 16, 24, 28, 40 }) { var mark = Mark(size); mark.Margin = new Thickness(0, 0, 8, 0); mark.VerticalAlignment = VerticalAlignment.Center; sizes.Children.Add(mark); }
            body.Children.Add(sizes);
        }
        preview.Measure(new Size(880, 280)); preview.Arrange(new Rect(0, 0, 880, 280)); preview.UpdateLayout();
        var image = new RenderTargetBitmap(880, 280, 96, 96, PixelFormats.Pbgra32); image.Render(preview);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        var directory = Path.Combine(AppContext.BaseDirectory, "Review"); Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, "LogoComparison.png")); encoder.Save(file);
    }

    sealed class SettingsLayoutProbe : FlyleafSettingsEditor
    {
        internal SettingsLayoutProbe() { config = new FlyleafLib.Config.PlaybackConfig(); Build(); }
        protected override FlyleafLib.NotifyPropertyChanged SelectConfig(Player target) => target.Config.Playback;
        protected override void Build()
        {
            var group = Group("Decoder and format options", () => { });
            Number(group, "Frame queue", "frames", () => 5, _ => { }, 1, 64);
            TextEntry(group, "Decoder override", () => "Automatic", _ => { });
            Options(group, () => new() { ["user_agent"] = "Example player / 1.0", ["reconnect_at_eof"] = "1" }, _ => { }, "Input options");
            Refresh();
        }
    }

    static StackPanel Demo()
    {
        var body = new StackPanel();
        TextBlock Label(string text) { var label = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 6) }; label.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush"); return label; }
        body.Children.Add(Label("Playback preferences"));
        var check = new CheckBox { Content = "Play automatically", IsChecked = true }; check.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.CheckBoxStyle"); body.Children.Add(check);
        var check2 = new CheckBox { Content = "Accurate seeking", Margin = new Thickness(0, 8, 0, 0) }; check2.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.CheckBoxStyle"); body.Children.Add(check2);
        body.Children.Add(Label("Seek step · seconds"));
        var number = new TextBox { Text = "5", Width = 76, HorizontalAlignment = HorizontalAlignment.Left }; number.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.InputStyle"); body.Children.Add(number);
        body.Children.Add(Label("Video processor"));
        var combo = new ComboBox { ItemsSource = new[] { "Automatic", "Flyleaf", "D3D11" }, SelectedIndex = 0, Width = 200, HorizontalAlignment = HorizontalAlignment.Left }; combo.SetResourceReference(FrameworkElement.StyleProperty, "FlyleafME.ComboBoxStyle"); body.Children.Add(combo);
        body.Children.Add(new Expander { Header = "Advanced", IsExpanded = true, Content = Label("Format options are applied when the next input opens."), Margin = new Thickness(0, 12, 0, 8) });
        var tree = new TreeView { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Height = 115 };
        var source = new TreeViewItem { Header = "Example playlist", IsExpanded = true }; source.Items.Add(new TreeViewItem { Header = "Episode 1", IsSelected = true }); source.Items.Add(new TreeViewItem { Header = "Episode 2" }); tree.Items.Add(source); body.Children.Add(tree);
        var longContent = new StackPanel(); for (int i = 0; i < 20; i++) longContent.Children.Add(Label("Additional option " + (i + 1)));
        body.Children.Add(new ScrollViewer { Height = 125, Content = longContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return body;
    }

    static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
}
