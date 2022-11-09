using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls.WPF
{
    public class FlyleafBar : UserControl, INotifyPropertyChanged
    {
        public Player Player
        {
            get { return (Player)GetValue(PlayerProperty); }
            set { SetValue(PlayerProperty, value); }
        }
        public static readonly DependencyProperty PlayerProperty =
            DependencyProperty.Register(nameof(Player), typeof(Player), typeof(FlyleafBar), new PropertyMetadata(null, new PropertyChangedCallback(OnPlayerChanged)));

        private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { }

        ContextMenu popUpMenuSubtitles, popUpMenuVideo;
        bool initialized = false;

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (initialized)
                return;

            initialized = true;

            popUpMenuSubtitles  = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner_Subtitles", this))?.ContextMenu;
            popUpMenuVideo      = ((FrameworkElement)Template.FindName("PART_ContextMenuOwner_Video", this))?.ContextMenu;

            if (popUpMenuSubtitles != null)
            {
                popUpMenuSubtitles.PlacementTarget = this;
                popUpMenuSubtitles.Opened += (o, e) => { if (Player != null) Player.Activity.IsEnabled = false; };
                popUpMenuSubtitles.Closed += (o, e) => { if (Player != null) Player.Activity.IsEnabled = true; };
            }

            if (popUpMenuVideo != null)
            {
                popUpMenuVideo.PlacementTarget = this;
                popUpMenuVideo.Opened += (o, e) => { if (Player != null) Player.Activity.IsEnabled = false; };
                popUpMenuVideo.Closed += (o, e) => { if (Player != null) Player.Activity.IsEnabled = true; };
            }

        }

        static FlyleafBar()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(FlyleafBar), new FrameworkPropertyMetadata(typeof(FlyleafBar)));
        }

        public ICommand OpenSettingsCmd { get; set; }
        public void OpenSettingsAction(object obj)
        {
            RaiseEvent(new OpenSettingsEventArgs(OpenSettingsEvent, Player));
        }

        public ICommand OpenContextMenu { get; set; }
        public void OpenContextMenuAction(object obj)
        {
            FrameworkElement element = (FrameworkElement)obj;
            if (element == null || element.ContextMenu == null)
                return;

            element.ContextMenu.PlacementTarget = element;
            element.ContextMenu.IsOpen = true;
        }

        public FlyleafBar()
        {
            OpenContextMenu = new RelayCommand(OpenContextMenuAction);
            OpenSettingsCmd = new RelayCommand(OpenSettingsAction);
        }

        #region Property Change
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected void Set<T>(ref T field, T value, bool check = true, [CallerMemberName] string propertyName = "")
        {
            if (!check || (field == null && value != null) || (field != null && !field.Equals(value)))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion

        public static readonly RoutedEvent OpenSettingsEvent = EventManager.RegisterRoutedEvent(nameof(OpenSettings), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FlyleafBar));
        public event RoutedEventHandler OpenSettings
        {
            add { AddHandler (OpenSettingsEvent, value); }
            remove { RemoveHandler(OpenSettingsEvent, value); }
        }
    }

    public class OpenSettingsEventArgs : RoutedEventArgs
    {
        public OpenSettingsEventArgs(RoutedEvent routedEvent, object source) : base(routedEvent, source) {}
    }
}
