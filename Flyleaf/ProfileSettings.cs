using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using FlyleafLib;
using Flyleaf.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace Flyleaf;

internal sealed class ProfileSettings : FlyleafSettingsEditor
{
    readonly MainWindow owner;

    public ProfileSettings(MainWindow owner)
    {
        this.owner = owner;
        Loaded += (_, _) => App.Instance.Profiles.MetadataChanged += ProfilesChanged;
        Unloaded += (_, _) => App.Instance.Profiles.MetadataChanged -= ProfilesChanged;
    }

    void ProfilesChanged(object? sender, EventArgs e) => Refresh();

    protected override NotifyPropertyChanged SelectConfig(Player target) => target.Config;

    protected override void Build()
    {
        BeginPage("Profiles", "Changes are saved when Flyleaf exits.");
        var profiles = App.Instance.Profiles;
        var session = owner.Profile;
        var group = Group("Active profile", null);
        var selector = Selector("Active profile");
        group.Children.Add(selector);
        var create = MakeButton("Create…");
        create.ToolTip = "Create a profile from Factory defaults";
        var copy = MakeButton("Copy…");
        copy.ToolTip = "Copy the active profile";
        var rename = MakeButton("Rename…");
        group.Children.Add(Actions(create, copy, rename));

        var resetGroup = Group("Reset profile settings", null);
        var resetSource = Selector("Reset source");
        resetGroup.Children.Add(resetSource);
        var resetControl = MakeButton("Reset media control");
        var resetPlayer = MakeButton("Reset player");
        resetGroup.Children.Add(Actions(resetControl, resetPlayer));

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        group.Children.Add(status);

        bool refreshing = false;
        string? error = null;
        void Display()
        {
            refreshing = true;
            try
            {
                var choices = profiles.Current.Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(p => (Id: (Guid?)p.Id, p.Name)).ToArray();
                DisplayChoices(selector, choices, session.Id);
                DisplayChoices(resetSource, [(null, "Factory defaults"), .. choices.Where(choice => choice.Id != session.Id)],
                    profiles.GetResetSource(session));
                status.Text = error ?? (session.PlayerSettingsPending ? "Open a new window or restart Flyleaf to apply the remaining player settings." : "");
                status.Visibility = status.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            finally { refreshing = false; }
        }

        void Change(Action action)
        {
            error = null;
            try
            {
                App.Instance.ActivatePlayerWindow(owner);
                action();
                owner.ApplyProfilePresentation();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Text.Json.JsonException or NotSupportedException)
            {
                error = ex.Message;
            }
            Display();
        }

        selector.SelectionChanged += (_, _) =>
        {
            if (!refreshing && selector.SelectedItem is ComboBoxItem { Tag: Guid id })
                Change(() => profiles.Select(session, id));
        };
        resetSource.SelectionChanged += (_, _) =>
        {
            if (!refreshing && resetSource.SelectedItem is ComboBoxItem item)
                Change(() => profiles.SetResetSource((Guid?)item.Tag));
        };
        create.Click += (_, _) =>
        {
            if (Context?.MediaElement?.PromptForInput("Create profile from Factory defaults", "Profile name", "Create") is { } name)
                Change(() => profiles.Create(session, name));
        };
        copy.Click += (_, _) =>
        {
            if (Context?.MediaElement?.PromptForInput("Copy profile", "Profile name", "Create") is { } name)
                Change(() => profiles.Copy(session, name));
        };
        rename.Click += (_, _) =>
        {
            if (Context?.MediaElement?.PromptForInput("Rename profile", "Profile name", "Rename") is { } name)
                Change(() => profiles.Rename(session, name));
        };
        resetControl.Click += (_, _) => ConfirmReset(false, "media control");
        resetPlayer.Click += (_, _) => ConfirmReset(true, "player");

        void ConfirmReset(bool player, string label)
        {
            if (MessageBox.Show(Window.GetWindow(this), $"Reset the {label} settings in this profile?",
                "Reset profile settings", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                Change(() => profiles.Reset(session, player));
        }

        RefreshWith(Display);
        Refresh();
    }

    static void DisplayChoices(ComboBox control, (Guid? Id, string Name)[] choices, Guid? selected)
    {
        var current = control.Items.OfType<ComboBoxItem>().Select(i => ((Guid?)i.Tag, (string)i.Content));
        if (!current.SequenceEqual(choices))
        {
            control.Items.Clear();
            foreach (var (id, name) in choices) control.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        }
        control.SelectedItem = control.Items.OfType<ComboBoxItem>().Single(i => (Guid?)i.Tag == selected);
    }

    static ComboBox Selector(string name)
    {
        var selector = new ComboBox { Margin = new Thickness(0, 6, 0, 0) };
        selector.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
        AutomationProperties.SetName(selector, name);
        return selector;
    }

    static WrapPanel Actions(params Button[] buttons)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in buttons) panel.Children.Add(button);
        return panel;
    }

    static Button MakeButton(string content)
    {
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 4) };
        button.SetResourceReference(StyleProperty, "FlyleafME.ButtonStyle");
        return button;
    }
}
