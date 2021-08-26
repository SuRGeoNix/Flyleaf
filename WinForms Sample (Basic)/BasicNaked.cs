
using System;
using System.Windows.Forms;

using FlyleafLib.MediaPlayer;

namespace WinForms_Sample__Basic_
{
    public partial class BasicNaked : Form
    {
        Player player = new Player();
        public BasicNaked()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FlyleafLib.Master.RegisterFFmpeg(":2");
            player.Control = flyleaf1;
            player.OpenAsync(@"../../../../Sample.mp4");
            player.OpenCompleted += (o, x) => { if (x.Success && x.Type == FlyleafLib.MediaType.Video) player.Play(); };
        }
    }
}
