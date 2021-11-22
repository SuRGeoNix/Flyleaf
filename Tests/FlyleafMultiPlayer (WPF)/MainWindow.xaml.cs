using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafMultiPlayer__WPF_
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public Player PlayerView1 { get => _PlayerView1; set { _PlayerView1 = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerView1))); } }
        Player _PlayerView1;
        public Player PlayerView2 { get => _PlayerView2; set { _PlayerView2 = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerView2))); } }
        Player _PlayerView2;
        public Player PlayerView3 { get => _PlayerView3; set { _PlayerView3 = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerView3))); } }
        Player _PlayerView3;
        public Player PlayerView4 { get => _PlayerView4; set { _PlayerView4 = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerView4))); } }
        Player _PlayerView4;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand RotatePlayers { get; set; }

        List<Player> Players = new List<Player>();

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public MainWindow()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            Master.RegisterFFmpeg(":2");

            // Creates 4 Players and adds them in the PlayerViews
            for (int i=0; i<4; i++)
                Players.Add(new Player());

            PlayerView1 = Players[0];
            PlayerView2 = Players[1];
            PlayerView3 = Players[2];
            PlayerView4 = Players[3];

            RotatePlayers = new RelayCommand(RotatePlayersAction);

            DataContext = this;

            InitializeComponent();
        }

        private void RotatePlayersAction(object obj)
        {
            // User should unsubscribe from all Player (and Player.Control) events before swaping
            flyleafControl1.UnsubscribePlayer();
            flyleafControl2.UnsubscribePlayer();
            flyleafControl3.UnsubscribePlayer();
            flyleafControl4.UnsubscribePlayer();

            PlayerView1 = PrevPlayer(PlayerView1);
            PlayerView2 = PrevPlayer(PlayerView2);
            PlayerView3 = PrevPlayer(PlayerView3);
            PlayerView4 = PrevPlayer(PlayerView4);

            // User should subscribe to all Player (and Player.Control) events after swaping and Raise(null) (Flyleaf WPF Control will handle the re-subscribe to the new player automatically)
        }

        private Player PrevPlayer(Player player)
        {
            return player.PlayerId == 1 ? Players[Players.Count - 1] : Players[player.PlayerId - 2];
        }
    }
}
