using System;
using System.Windows;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace DisposePlayer
{
    /// <summary>
    /// Interaction logic for FlyleafWPFControl.xaml
    /// </summary>
    public partial class FlyleafWPFControl : Window
    {
        public Player Player { get ; set; }
        public FlyleafWPFControl()
        {
            // Prepares Player's Configuration
            Config config = new Config();

            // Initializes the Player
            Player = new Player(config);

            // Allowing VideoView to access our Player
            DataContext = this;

            InitializeComponent();
        }
    }
}
