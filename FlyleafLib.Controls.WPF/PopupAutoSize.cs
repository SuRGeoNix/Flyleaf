using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlyleafLib.Controls.WPF
{
    /// <summary>
    /// Forces a reused submenu Popup to remeasure its content when it is opened.
    /// This handles menus whose ItemsSource becomes smaller between openings.
    /// </summary>
    public static class PopupAutoSize
    {
        public static readonly DependencyProperty RefreshOnOpenProperty =
            DependencyProperty.RegisterAttached(
                "RefreshOnOpen", typeof(bool), typeof(PopupAutoSize),
                new PropertyMetadata(false, OnRefreshOnOpenChanged));

        public static bool GetRefreshOnOpen(DependencyObject obj) =>
            (bool)obj.GetValue(RefreshOnOpenProperty);

        public static void SetRefreshOnOpen(DependencyObject obj, bool value) =>
            obj.SetValue(RefreshOnOpenProperty, value);

        private static void OnRefreshOnOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Popup popup)
                return;

            popup.Opened -= Popup_Opened;
            if ((bool)e.NewValue)
                popup.Opened += Popup_Opened;
        }

        private static void Popup_Opened(object sender, EventArgs e)
        {
            var popup = (Popup)sender;

            // ItemsSource and generated containers must be updated before remeasuring.
            popup.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
            {
                if (!popup.IsOpen || popup.Child is not UIElement content)
                    return;

                // Invalidate the content (not only the Popup, whose MeasureOverride
                // reports zero size). The invalidation propagates to the PopupRoot.
                FindDescendant<ItemsPresenter>(content)?.InvalidateMeasure();
                FindDescendant<ScrollViewer>(content)?.InvalidateMeasure();
                content.InvalidateMeasure();
                content.UpdateLayout();
            }));
        }

        private static T FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    return match;

                T found = FindDescendant<T>(child);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
