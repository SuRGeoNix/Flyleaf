using System.ComponentModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Flyleaf;

internal sealed class FileAssociationSettings : StackPanel
{
    readonly Dictionary<string, (CheckBox Choice, TextBlock Default)> rows = new(StringComparer.OrdinalIgnoreCase);
    readonly AppConfig config;
    readonly StackPanel customRows = new();
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly Button apply = MakeButton("Apply registration");
    readonly Button windows = MakeButton("Open Windows Default Apps");

    internal FileAssociationSettings(AppConfig config)
    {
        this.config = config;
        var title = new TextBlock { Text = "File associations", FontWeight = FontWeights.SemiBold, FontSize = 20 };
        title.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush");
        Children.Add(title);
        var help = new TextBlock
        {
            Text = "Choose which extensions offer Flyleaf in Open with. Set double-click defaults in Windows.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8)
        };
        help.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        Children.Add(help);

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        actions.Children.Add(apply);
        actions.Children.Add(windows);
        Children.Add(actions);
        status.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        Children.Add(status);

        foreach (var (label, extensions) in FileAssociations.Groups)
            AddGroup(label, extensions);
        AddCustomGroup();

        apply.Click += (_, _) => Run(() =>
        {
            var selected = rows.Where(row => row.Value.Choice.IsChecked == true).Select(row => row.Key).ToArray();
            FileAssociations.Apply(selected);
            RefreshDefaults();
            status.Text = selected.Length == 0 ? "Flyleaf’s Open with registration was cleared."
                : $"Flyleaf is registered for {selected.Length} extensions. Windows manages the defaults.";
        });
        windows.Click += (_, _) => Run(FileAssociations.OpenWindowsDefaults);
        Loaded += (_, _) =>
        {
            Run(() =>
            {
                var registered = FileAssociations.ReadRegistration();
                foreach (var value in (config.CustomFileExtensions ?? []).Concat(registered))
                    if (FileAssociations.TryNormalizeExtension(value, out var extension) && !rows.ContainsKey(extension))
                        AddCustomRow(extension);
                foreach (var (extension, row) in rows) row.Choice.IsChecked = registered.Contains(extension);
                RefreshDefaults();
            });
            if (Application.Current != null) Application.Current.Activated += ApplicationActivated;
        };
        Unloaded += (_, _) =>
        {
            if (Application.Current != null) Application.Current.Activated -= ApplicationActivated;
        };
    }

    void AddGroup(string label, string[] extensions)
    {
        var body = new StackPanel();
        var selectAll = MakeCheckBox("Select all");
        AutomationProperties.SetName(selectAll, $"Select all {label} extensions");
        selectAll.Margin = new Thickness(0, 0, 0, 6);
        body.Children.Add(selectAll);
        var choices = new WrapPanel();
        body.Children.Add(choices);
        var group = new Expander { Header = label, Content = body, Margin = new Thickness(0, 8, 0, 0) };
        group.SetResourceReference(StyleProperty, "FlyleafME.ExpanderStyle");
        Children.Add(group);

        bool settingGroup = false;
        var members = new List<CheckBox>();
        foreach (string extension in extensions)
        {
            var item = CreateExtensionRow(extension);
            item.Width = 152;
            item.Margin = new Thickness(0, 0, 8, 3);
            choices.Children.Add(item);
            var choice = rows[extension].Choice;
            members.Add(choice);
            choice.Checked += SelectionChanged;
            choice.Unchecked += SelectionChanged;
        }

        selectAll.Checked += (_, _) => SetGroup(true);
        selectAll.Unchecked += (_, _) => SetGroup(false);

        void SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (settingGroup) return;
            int count = members.Count(choice => choice.IsChecked == true);
            selectAll.IsChecked = count == 0 ? false : count == members.Count ? true : null;
        }

        void SetGroup(bool value)
        {
            settingGroup = true;
            foreach (var member in members) member.IsChecked = value;
            settingGroup = false;
        }
    }

    void AddCustomGroup()
    {
        var body = new StackPanel();
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var add = MakeButton("Add");
        add.MinWidth = 56;
        add.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(add, Dock.Right);
        line.Children.Add(add);
        var input = new TextBox { ToolTip = "File extension, e.g. .m2p", MaxLength = 65 };
        AutomationProperties.SetName(input, "Custom file extension");
        input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
        line.Children.Add(input);
        body.Children.Add(line);
        body.Children.Add(customRows);

        var group = new Expander { Header = "Custom extensions", Content = body, Margin = new Thickness(0, 8, 0, 0) };
        group.SetResourceReference(StyleProperty, "FlyleafME.ExpanderStyle");
        Children.Add(group);
        add.Click += (_, _) => Run(AddExtension);
        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Run(AddExtension);
        };

        void AddExtension()
        {
            if (!FileAssociations.TryNormalizeExtension(input.Text, out var extension))
            {
                status.Text = "Enter one extension using letters, numbers, hyphens or underscores.";
                return;
            }

            if (!rows.ContainsKey(extension)) AddCustomRow(extension);
            if (!FileAssociations.Groups.Any(group => group.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)))
                config.CustomFileExtensions = (config.CustomFileExtensions ?? []).Append(extension)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            rows[extension].Choice.IsChecked = true;
            input.Clear();
            RefreshDefaults();
            status.Text = $"{extension} is selected. Apply registration to update Open with.";
        }
    }

    void AddCustomRow(string extension)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var remove = MakeButton("Remove");
        remove.Margin = new Thickness(8, 0, 0, 0);
        AutomationProperties.SetName(remove, $"Remove {extension}");
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);
        row.Children.Add(CreateExtensionRow(extension));
        customRows.Children.Add(row);
        remove.Click += (_, _) =>
        {
            rows.Remove(extension);
            customRows.Children.Remove(row);
            config.CustomFileExtensions = (config.CustomFileExtensions ?? []).Where(value =>
                !FileAssociations.TryNormalizeExtension(value, out var normalized) ||
                !string.Equals(normalized, extension, StringComparison.OrdinalIgnoreCase)).ToArray();
            status.Text = $"{extension} was removed from the list. Apply registration to update Open with.";
        };
    }

    StackPanel CreateExtensionRow(string extension)
    {
        var choice = MakeCheckBox(extension);
        var badge = new TextBlock
        {
            Text = "Windows default", FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed
        };
        badge.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(choice);
        row.Children.Add(badge);
        rows.Add(extension, (choice, badge));
        return row;
    }

    void ApplicationActivated(object? sender, EventArgs e) => Run(RefreshDefaults);

    void RefreshDefaults()
    {
        foreach (var (extension, row) in rows)
        {
            bool isDefault = FileAssociations.IsDefault(extension);
            row.Default.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(row.Choice, isDefault ? "Flyleaf is the Windows default." : "");
        }
    }

    void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or Win32Exception or InvalidOperationException)
        {
            status.Text = ex.Message;
        }
    }

    static CheckBox MakeCheckBox(string text)
    {
        var check = new CheckBox { Content = text, MinHeight = 22 };
        check.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        return check;
    }

    static Button MakeButton(string text)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 4) };
        button.SetResourceReference(StyleProperty, "FlyleafME.ButtonStyle");
        return button;
    }
}
