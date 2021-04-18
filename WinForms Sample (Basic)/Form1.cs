
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
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            FlyleafLib.Utils.FFmpeg.RegisterFFmpeg(":2");
            flyleaf1.Player.Start();
            flyleaf1.Player.Open(@"c:\root\down\samples\0.mp4");
            flyleaf1.Player.OpenCompleted += (o, x) => { if (x.success && x.type == FlyleafLib.MediaType.Video) flyleaf1.Player.Play(); };
        }
    }
}
