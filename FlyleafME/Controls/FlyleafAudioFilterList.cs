using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FlyleafLib.MediaFramework.MediaDecoder;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

/// <summary>Edits an ordered saved filter chain; deliberately does not reload the native graph.</summary>
internal sealed class FlyleafAudioFilterList : StackPanel
{
    readonly Func<List<Filter>?> read;
    readonly Action<List<Filter>> write;
    readonly Action<Action> apply;
    readonly StackPanel rows = new();
    readonly TextBlock error = Text("", true);
    readonly List<(TextBox Name, TextBox Id, TextBox Args)> entries = [];
    List<Filter>? shown;
    bool dirty;

    internal FlyleafAudioFilterList(Func<List<Filter>?> read, Action<List<Filter>> write, Action<Action> apply)
    {
        this.read = read; this.write = write; this.apply = apply;
        Children.Add(rows);
        var add = Button("Add filter"); add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 8, 0, 0);
        add.Click += (_, _) => { AddRow("", "", ""); dirty = true; entries[^1].Name.Focus(); };
        Children.Add(add); Children.Add(error);
        error.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.ErrorBrush");
        RefreshValues(true);
    }

    internal void RefreshValues(bool discardDraft = false)
    {
        var current = read();
        if (!discardDraft && (dirty || ReferenceEquals(current, shown))) return;
        dirty = false; shown = current; error.Text = ""; rows.Children.Clear(); entries.Clear();
        if (current == null) return;
        foreach (var filter in current) AddRow(filter.Name ?? "", filter.Id ?? "", filter.Args ?? "");
    }

    bool Commit()
    {
        if (!dirty) return true;
        if (!FlyleafSettingsValues.TryAudioFilters(entries.Select(e => (e.Name.Text, e.Id.Text, e.Args.Text)), out var values, out var message))
        { error.Text = message; return false; }
        var result = values.Select(e => new Filter { Name = e.Name, Id = e.Id.Length == 0 ? null : e.Id, Args = e.Args }).ToList();
        dirty = false; error.Text = ""; apply(() => write(result)); shown = read(); return true;
    }

    void AddRow(string name, string id, string args)
    {
        TextBox Input(string value, string help)
        {
            var box = new TextBox { Text = value, ToolTip = help };
            box.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");
            box.TextChanged += (_, _) => dirty = true;
            box.LostKeyboardFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            return box;
        }
        var filterName = Input(name, "FFmpeg audio filter name");
        var filterId = Input(id, "Optional unique identifier for filter commands");
        var filterArgs = Input(args, "Filter arguments, passed verbatim");
        var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var fields = new Grid(); fields.ColumnDefinitions.Add(new()); fields.ColumnDefinitions.Add(new());
        filterName.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(filterId, 1);
        fields.Children.Add(filterName); fields.Children.Add(filterId);
        row.Children.Add(Text("Filter name / optional identifier")); row.Children.Add(fields);
        row.Children.Add(Text("Arguments")); row.Children.Add(filterArgs);
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var up = Button("↑"); up.ToolTip = "Move filter earlier";
        var down = Button("↓"); down.ToolTip = "Move filter later";
        var remove = Button("Remove");
        foreach (var button in new[] { up, down, remove }) { button.Margin = new Thickness(0, 0, 6, 0); actions.Children.Add(button); }
        row.Children.Add(actions); rows.Children.Add(row); entries.Add((filterName, filterId, filterArgs));
        void Move(int offset)
        {
            if (!Commit()) return;
            int index = entries.Where(e => e.Name.Text.Trim().Length != 0 || e.Id.Text.Trim().Length != 0 || e.Args.Text.Length != 0).ToList().FindIndex(e => e.Name == filterName);
            var copy = read()?.ToList() ?? [];
            if (index < 0 || index >= copy.Count || index + offset < 0 || index + offset >= copy.Count) return;
            (copy[index], copy[index + offset]) = (copy[index + offset], copy[index]);
            apply(() => write(copy)); RefreshValues(true);
        }
        up.Click += (_, _) => Move(-1); down.Click += (_, _) => Move(1);
        remove.Click += (_, _) => { entries.Remove((filterName, filterId, filterArgs)); rows.Children.Remove(row); dirty = true; Commit(); };
    }
}
