using FlyleafLib;
using FlyleafLib.MediaPlayer;
using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace Wpf_Samples
{
    /// <summary>
    /// Interaction logic for Sample2_Custom.xaml
    /// </summary>
    public partial class Sample2_Custom : Window
    {
        Sample2_ViewModel s2vm;
        Player player;
        
        public Sample2_Custom()
        {
            Master.RegisterFFmpeg(":2");
            InitializeComponent();
            
            player = new Player();
            s2vm = new Sample2_ViewModel(player);
            flview.DataContext = s2vm;
        }
    }
}
