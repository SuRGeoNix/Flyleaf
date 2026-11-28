using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using Flyleaf.Controls.WPF;

namespace Flyleaf;

internal sealed class FolderImportDialog : FlyleafModalDialog
{
    readonly (FolderMediaType Type, CheckBox Box)[] media =
    [
        (FolderMediaType.Audio, MediaCheck("Audio")),
        (FolderMediaType.Video, MediaCheck("Video")),
        (FolderMediaType.Images, MediaCheck("Images")),
        (FolderMediaType.Animations, MediaCheck("Animations"))
    ];
    readonly CheckBox allMedia = MediaCheck("All media");
    readonly ComboBox sort;
    readonly CheckBox subfolders = new() { Content = "Include subfolders", IsChecked = true, Margin = new Thickness(0, 12, 0, 12) };
    readonly Button confirm = new() { IsDefault = true };
    bool settingAllMedia;

    FolderMediaType SelectedMedia
    {
        get
        {
            var selected = FolderMediaType.None;
            foreach (var (type, box) in media)
                if (box.IsChecked == true) selected |= type;
            return selected;
        }
    }

    internal FolderImportOptions Options => new(SelectedMedia, subfolders.IsChecked == true, (FolderSort)sort.SelectedIndex);

    internal FolderImportDialog(ResourceDictionary theme, IReadOnlyList<string> folders, bool append)
        : base(append ? (folders.Count == 1 ? "Add folder to queue" : "Add folders to queue")
            : (folders.Count == 1 ? "Open folder" : "Open folders"))
    {
        Resources.MergedDictionaries.Add(theme);

        var body = new StackPanel { Width = 345, Margin = new Thickness(18) };
        var selection = folders.Count == 1 ? folders[0] : $"{folders.Count:N0} folders selected";
        body.Children.Add(new TextBlock { Text = selection, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        allMedia.FontWeight = FontWeights.SemiBold;
        allMedia.Checked += (_, _) => SetAllMedia(true);
        allMedia.Unchecked += (_, _) => SetAllMedia(false);
        body.Children.Add(allMedia);
        var types = new UniformGrid { Columns = 2, Margin = new Thickness(24, 4, 0, 14) };
        foreach (var (_, box) in media)
        {
            box.Checked += MediaTypeChanged;
            box.Unchecked += MediaTypeChanged;
            types.Children.Add(box);
        }
        body.Children.Add(types);
        sort = Selector(body, "Sort by", ["File Explorer", "Name (A–Z)", "Name (Z–A)", "Name numeric (A–Z)", "Name numeric (Z–A)",
            "Date modified (newest)", "Date modified (oldest)", "Size (largest)", "Size (smallest)"]);
        var sortHelp = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed
        };
        sortHelp.SetResourceReference(ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        sort.SelectionChanged += (_, _) =>
        {
            sortHelp.Text = (FolderSort)sort.SelectedIndex switch
            {
                FolderSort.NumberAscending or FolderSort.NumberDescending => "Compares the whole name and reads digit groups as numbers (2 before 10).",
                FolderSort.Explorer => "Uses the visible order in open Explorer folders and Windows name sorting for other items.",
                _ => ""
            };
            sortHelp.Visibility = sortHelp.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        sort.SelectedIndex = (int)FolderSort.Explorer;
        body.Children.Add(sortHelp);
        subfolders.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        body.Children.Add(subfolders);

        confirm.Content = append ? "Add to queue" : "Open";
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { confirm, cancel })
        {
            button.SetResourceReference(StyleProperty, "FlyleafME.ButtonStyle");
            actions.Children.Add(button);
        }
        confirm.Click += (_, _) => DialogResult = true;
        body.Children.Add(actions);
        Body.Content = body;
    }

    static CheckBox MediaCheck(string label)
    {
        var box = new CheckBox { Content = label, IsChecked = true, MinHeight = 22 };
        box.SetResourceReference(StyleProperty, "FlyleafME.CheckBoxStyle");
        return box;
    }

    void SetAllMedia(bool isChecked)
    {
        settingAllMedia = true;
        foreach (var (_, box) in media) box.IsChecked = isChecked;
        settingAllMedia = false;
        confirm.IsEnabled = isChecked;
    }

    void MediaTypeChanged(object sender, RoutedEventArgs e)
    {
        if (settingAllMedia) return;
        var selected = SelectedMedia;
        allMedia.IsChecked = selected switch
        {
            FolderMediaType.All => true,
            FolderMediaType.None => false,
            _ => null
        };
        confirm.IsEnabled = selected != FolderMediaType.None;
    }

    static ComboBox Selector(Panel body, string label, string[] options)
    {
        body.Children.Add(new TextBlock { Text = label });
        var box = new ComboBox { ItemsSource = options, Margin = new Thickness(0, 4, 0, 10) };
        box.SetResourceReference(StyleProperty, "FlyleafME.ComboBoxStyle");
        AutomationProperties.SetName(box, label);
        body.Children.Add(box);
        return box;
    }
}
