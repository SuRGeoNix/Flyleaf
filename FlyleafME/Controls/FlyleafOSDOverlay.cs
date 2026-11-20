using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Flyleaf.Controls.WPF;

/// <summary>One replaceable, timed message. Never takes focus or keeps playback controls awake.</summary>
public sealed class FlyleafOSDOverlay : Border
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(nameof(Options), typeof(FlyleafOSDOptions), typeof(FlyleafOSDOverlay), new PropertyMetadata(null, OptionsChanged));
    public FlyleafOSDOptions? Options { get => (FlyleafOSDOptions?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    readonly DispatcherTimer timer;
    static readonly FlyleafOSDOptions defaults = new();
    bool errorMessage, statusMessage;
    public string Message => message.Text;
    public FlyleafOSDOverlay()
    {
        IsHitTestVisible = false; Focusable = false; Visibility = Visibility.Collapsed;
        Padding = new Thickness(12, 7, 12, 7); CornerRadius = new CornerRadius(4); MaxWidth = 480;
        SetResourceReference(BackgroundProperty, "FlyleafME.OverlayBrush");
        message.SetResourceReference(TextBlock.ForegroundProperty, "FlyleafME.OverlayTextBrush"); Child = message;
        timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher); timer.Tick += (_, _) => Clear();
        Unloaded += (_, _) => Clear(); ApplyOptions();
    }
    static void OptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var osd = (FlyleafOSDOverlay)d;
        if (e.OldValue is FlyleafOSDOptions old) PropertyChangedEventManager.RemoveHandler(old, osd.Changed, "");
        if (e.NewValue is FlyleafOSDOptions current) PropertyChangedEventManager.AddHandler(current, osd.Changed, "");
        osd.ApplyOptions();
    }
    void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) ApplyOptions(); else Dispatcher.BeginInvoke(new Action(ApplyOptions));
    }
    void ApplyOptions()
    {
        var o = Options ?? defaults;
        message.FontSize = o.FontSize;
        HorizontalAlignment = o.Position is FlyleafOSDPosition.TopLeft or FlyleafOSDPosition.BottomLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        VerticalAlignment = o.Position is FlyleafOSDPosition.TopLeft or FlyleafOSDPosition.TopRight ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        if (errorMessage ? !o.ShowErrors : statusMessage ? !o.ShowStatus : !o.Enabled) Clear();
        else if (timer.IsEnabled) { timer.Stop(); timer.Interval = TimeSpan.FromSeconds(errorMessage ? 7 : o.DurationSeconds); timer.Start(); }
    }
    public void ShowMessage(string text, bool error = false, bool status = false)
    {
        Dispatcher.VerifyAccess();
        if (string.IsNullOrWhiteSpace(text)) { Clear(); return; }
        var o = Options ?? defaults;
        if (error ? !o.ShowErrors : status ? !o.ShowStatus : !o.Enabled) return;
        if (errorMessage && timer.IsEnabled && !error) return;
        timer.Stop();
        errorMessage = error;
        statusMessage = status;
        message.SetResourceReference(TextBlock.ForegroundProperty, error ? "FlyleafME.ErrorBrush" : "FlyleafME.OverlayTextBrush");
        message.Text = text.Length > 2048 ? text[..2048] : text;
        Visibility = Visibility.Visible;
        timer.Interval = TimeSpan.FromSeconds(error ? 7 : o.DurationSeconds); timer.Start();
    }
    public void Clear() { Dispatcher.VerifyAccess(); timer.Stop(); errorMessage = statusMessage = false; message.Text = ""; Visibility = Visibility.Collapsed; }
    internal void ClearStatusMessage() { Dispatcher.VerifyAccess(); if (statusMessage && !errorMessage) Clear(); }
}
