using System;
using System.Windows;

using FlyleafLib;
using FlyleafLib.MediaPlayer;

namespace Wpf_Samples
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class Sample1 : Window
    {
        public Player       Player      { get ; set; } = new Player();

        public Sample1()
        {
            Master.RegisterFFmpeg(":2");
            InitializeComponent();
            DataContext = this;
        }
    }
}