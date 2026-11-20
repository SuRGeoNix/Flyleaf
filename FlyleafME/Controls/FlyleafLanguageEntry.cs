using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using static Flyleaf.Controls.WPF.FlyleafPanelUI;

namespace Flyleaf.Controls.WPF;

internal sealed class FlyleafLanguageEntry : Grid
{
    internal TextBox Input { get; } = new() { MinWidth = 170, MaxWidth = 300, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Search by language name, native name or ISO code" };
    readonly ListBox matches = new() { DisplayMemberPath = nameof(FlyleafLanguageChoice.DisplayName), BorderThickness = new Thickness(0), MaxHeight = 250, IsTabStop = false };
    readonly Popup popup;
    readonly Func<IEnumerable<Language>> selected;
    bool choosing;
    internal bool IsSuggestionOpen => popup.IsOpen;
    internal event EventHandler? Confirmed;

    internal FlyleafLanguageEntry(Func<IEnumerable<Language>> selected)
    {
        this.selected = selected;
        Input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle"); Input.Tag = this;
        matches.SetResourceReference(Control.BackgroundProperty, "FlyleafME.SurfaceBrush");
        matches.SetResourceReference(Control.ForegroundProperty, "FlyleafME.TextBrush");
        matches.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "FlyleafME.SuggestionItemStyle");
        var border = new Border { Child = matches, BorderThickness = new Thickness(1), Padding = new Thickness(4), CornerRadius = new CornerRadius(5), MinWidth = 300 };
        border.SetResourceReference(Border.BackgroundProperty, "FlyleafME.SurfaceBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "FlyleafME.BorderBrush");
        popup = new Popup { Child = border, PlacementTarget = Input, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        Children.Add(Input); Children.Add(popup);
        Input.TextChanged += (_, _) => RefreshSuggestions();
        Input.GotKeyboardFocus += (_, _) => RefreshSuggestions();
        Input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && popup.IsOpen) { popup.IsOpen = false; e.Handled = true; return; }
            if (e.Key is Key.Down or Key.Up)
            {
                if (!popup.IsOpen) RefreshSuggestions();
                if (matches.Items.Count == 0) return;
                matches.SelectedIndex = Math.Clamp(matches.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, matches.Items.Count - 1);
                matches.ScrollIntoView(matches.SelectedItem); e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (popup.IsOpen && (matches.SelectedItem ?? matches.Items.Cast<object>().FirstOrDefault()) is FlyleafLanguageChoice choice) Choose(choice);
                else { popup.IsOpen = false; Confirmed?.Invoke(this, EventArgs.Empty); }
                e.Handled = true;
            }
            else if (e.Key == Key.Tab) popup.IsOpen = false;
        };
        matches.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var item = ItemsControl.ContainerFromElement(matches, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item?.Content is not FlyleafLanguageChoice choice) return;
            Choose(choice); e.Handled = true;
        };
        Input.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(new Action(() =>
        { if (!Input.IsKeyboardFocusWithin && !matches.IsKeyboardFocusWithin) popup.IsOpen = false; }));
        matches.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && matches.SelectedItem is FlyleafLanguageChoice choice) { Choose(choice); e.Handled = true; }
            else if (e.Key == Key.Escape) { popup.IsOpen = false; Input.Focus(); e.Handled = true; }
        };
        Unloaded += (_, _) => popup.IsOpen = false;
    }

    void RefreshSuggestions()
    {
        if (choosing) return;
        matches.ItemsSource = FlyleafLanguageSuggestions.Find(Input.Text, selected());
        matches.SelectedIndex = -1;
        popup.IsOpen = Input.IsKeyboardFocusWithin && matches.Items.Count > 0;
    }

    void Choose(FlyleafLanguageChoice choice)
    {
        choosing = true;
        try { Input.Text = choice.Value.CultureName ?? "und"; popup.IsOpen = false; Confirmed?.Invoke(this, EventArgs.Empty); }
        finally { choosing = false; }
    }
}
