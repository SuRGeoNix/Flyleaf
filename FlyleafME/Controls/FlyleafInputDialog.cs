using System.Windows;
using System.Windows.Controls;

namespace Flyleaf.Controls.WPF;

/// <summary>Themed default input prompt; applications can supply their own opening UI.</summary>
internal sealed class FlyleafInputDialog : FlyleafModalDialog
{
    internal TextBox Input { get; } = new() { MinWidth = 290, Margin = new Thickness(0, 0, 0, 16) };
    internal FlyleafInputDialog(string title, string label, string acceptLabel) : base(title)
    {
        Input.SetResourceReference(StyleProperty, "FlyleafME.InputStyle");

        var accept = FlyleafPanelUI.Button(acceptLabel); accept.IsDefault = true;
        var cancel = FlyleafPanelUI.Button("Cancel"); cancel.IsCancel = true; cancel.Margin = new Thickness(8, 0, 0, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(accept); actions.Children.Add(cancel);
        var body = new StackPanel { Margin = new Thickness(18) };
        var caption = FlyleafPanelUI.Text(label); caption.Margin = new Thickness(0, 0, 0, 8);
        body.Children.Add(caption); body.Children.Add(Input); body.Children.Add(actions);
        Body.Content = body;
        accept.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(Input.Text)) DialogResult = true; };
        Loaded += (_, _) => Input.Focus();
    }
}
