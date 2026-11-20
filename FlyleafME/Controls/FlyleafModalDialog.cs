using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Flyleaf.Controls.WPF;

/// <summary>Provides Flyleaf's themed chrome for modal dialogs.</summary>
public abstract class FlyleafModalDialog : Window
{
    protected ContentPresenter Body { get; } = new();

    protected FlyleafModalDialog(string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "FlyleafME.SurfaceBrush");
        SetResourceReference(ForegroundProperty, "FlyleafME.TextBrush");

        var header = new DockPanel { Background = Brushes.Transparent, Margin = new Thickness(12, 6, 6, 6) };
        var close = FlyleafPanelUI.Button("×");
        close.ToolTip = "Close";
        close.Padding = new Thickness(8, 1, 8, 1);
        close.MinHeight = 28;
        close.Click += (_, _) => DialogResult = false;
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.MouseLeftButtonDown += (_, e) =>
        {
            for (DependencyObject? element = e.OriginalSource as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
                if (element is ButtonBase) return;
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        };

        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.SetResourceReference(Border.BorderBrushProperty, "FlyleafME.BorderBrush");
        var layout = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        layout.Children.Add(headerBorder);
        layout.Children.Add(Body);
        var frame = new Border { Child = layout, BorderThickness = new Thickness(1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "FlyleafME.BorderBrush");
        frame.SetResourceReference(Border.BackgroundProperty, "FlyleafME.SurfaceBrush");
        Content = frame;
    }
}
