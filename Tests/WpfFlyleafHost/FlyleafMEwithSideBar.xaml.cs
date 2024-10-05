using FlyleafLib.MediaPlayer;
using System.Windows;

namespace WpfFlyleafHost;

public partial class FlyleafMEwithSideBar : Window
{
    public Player Player { get; set; } = new Player();

    public FlyleafMEwithSideBar()
    {
        InitializeComponent();
        DataContext = this;
    }
}
