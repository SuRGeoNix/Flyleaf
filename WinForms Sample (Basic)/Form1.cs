
using FlyleafLib.MediaPlayer;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

//using FlyleafLib.Controls;

namespace WinForms_Sample__Basic_
{
    public partial class Form1 : Form
    {
        Player player = new Player();
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FlyleafLib.Master.RegisterFFmpeg(":2");
            player.Control = flyleaf1;
            player.Open(@"../../../Sample.mp4");
            player.OpenCompleted += (o, x) => { if (x.success && x.type == FlyleafLib.MediaType.Video) player.Play(); };
        }
    }
}
