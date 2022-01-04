using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using FlyleafLib.MediaPlayer;

namespace FlyleafLib.Controls.WPF
{
    /// <summary>
    /// Interaction logic for PlayerDebug.xaml
    /// </summary>
    public partial class PlayerDebug : UserControl
    {
        public Player Player
        {
            get { return (Player)GetValue(PlayerProperty); }
            set { SetValue(PlayerProperty, value); }
        }

        public static readonly DependencyProperty PlayerProperty =
            DependencyProperty.Register("Player", typeof(Player), typeof(PlayerDebug), new PropertyMetadata(null));

        public Brush BoxColor
        {
            get { return (Brush)GetValue(BoxColorProperty); }
            set { SetValue(BoxColorProperty, value); }
        }

        public static readonly DependencyProperty BoxColorProperty =
            DependencyProperty.Register("BoxColor", typeof(Brush), typeof(PlayerDebug), new PropertyMetadata(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0000000"))));

        public Brush HeaderColor
        {
            get { return (Brush)GetValue(HeaderColorProperty); }
            set { SetValue(HeaderColorProperty, value); }
        }

        public static readonly DependencyProperty HeaderColorProperty =
            DependencyProperty.Register("HeaderColor", typeof(Brush), typeof(PlayerDebug), new PropertyMetadata(new SolidColorBrush(Colors.LightSalmon)));

        public Brush InfoColor
        {
            get { return (Brush)GetValue(InfoColorProperty); }
            set { SetValue(InfoColorProperty, value); }
        }

        public static readonly DependencyProperty InfoColorProperty =
            DependencyProperty.Register("InfoColor", typeof(Brush), typeof(PlayerDebug), new PropertyMetadata(new SolidColorBrush(Colors.LightSteelBlue)));

        public Brush ValueColor
        {
            get { return (Brush)GetValue(ValueColorProperty); }
            set { SetValue(ValueColorProperty, value); }
        }

        public static readonly DependencyProperty ValueColorProperty =
            DependencyProperty.Register("ValueColor", typeof(Brush), typeof(PlayerDebug), new PropertyMetadata(new SolidColorBrush(Colors.White)));
        public PlayerDebug()
        {
            InitializeComponent();
        }
    }
}
