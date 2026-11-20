using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FlyleafLib;
using Flyleaf.Controls.WPF;

namespace Flyleaf;

// Application content: no dependency on the reusable panel's visual tree or placement.
internal static class ReviewNotesPanel
{
    sealed class NotesModel : NotifyPropertyChanged
    {
        string notes = "Review the opening sequence.";
        public string Notes { get => notes; set => Set(ref notes, value); }
    }

    internal static FrameworkElement Create(FlyleafPanelContext context)
    {
        var content = new StackPanel { DataContext = new NotesModel() };
        var label = new TextBlock { Text = "Application-owned notes", Margin = new Thickness(0, 0, 0, 12) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush");
        content.Children.Add(label);
        var source = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        source.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.TextBrush");
        source.SetBinding(TextBlock.TextProperty, new Binding("Player.Playlist.Selected.Title")
        { Source = context, TargetNullValue = "No media open", StringFormat = "Source: {0}" });
        content.Children.Add(source);
        var notes = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140 };
        notes.SetBinding(TextBox.TextProperty, new Binding(nameof(NotesModel.Notes))
        { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        content.Children.Add(notes);
        var hint = new TextBlock
        {
            Text = "This application panel permits left docking and floating only. Notes stay in memory when moved or closed.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.SecondaryTextBrush");
        content.Children.Add(hint);
        var close = new Button { Content = "Close through panel context", HorizontalAlignment = HorizontalAlignment.Left };
        close.Click += (_, _) => context.Close();
        content.Children.Add(close);
        return content;
    }
}
