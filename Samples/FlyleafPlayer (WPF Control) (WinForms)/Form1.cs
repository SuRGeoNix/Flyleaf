using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;

using FlyleafLib;
using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaPlayer;

namespace FlyleafPlayer__WPF_Control_
{
    /// <summary>
    /// FlyleafPlayer (WPF Control) Sample
    /// </summary>
    public partial class Form1 : Form
    {
        /// <summary>
        /// Element's host Child to host VideoView
        /// </summary>
        public ContentControl   ContentControl  { get; set; }

        /// <summary>
        /// Flyleaf WPF Control hosted at VideoView's content
        /// </summary>
        public Flyleaf          Flyleaf         { get; set; }

        /// <summary>
        /// VideoView hosted at Content's Control content and hosts Flyleaf WinForms Control throught WindowsFormsHost.Child
        /// </summary>
        public VideoView        VideoView       { get; set; }

        /// <summary>
        /// Flyleaf Player "binded" to VideoView
        /// </summary>
        public Player           Player          { get; set; }

        public Form1()
        {
            // Ensures that we have enough worker threads to avoid the UI from freezing or not updating on time
            ThreadPool.GetMinThreads(out int workers, out int ports);
            ThreadPool.SetMinThreads(workers + 6, ports + 6);

            // Registers FFmpeg Libraries
            Master.RegisterFFmpeg(":2");

            // Prepares Player's Configuration
            Config config = new Config();

            // Initializes the Player
            Player = new Player(config);

            InitializeComponent();

            // Required Controls for WPF Control
            VideoView = new VideoView();
            Flyleaf = new Flyleaf();

            // Hosts the WPF VideoView
            ContentControl      = new ContentControl();
            elementHost1.Child  = ContentControl;
            elementHost1.Dock   = DockStyle.Fill;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            VideoView.UpdateDefaultStyle();
            VideoView.ApplyTemplate();

            #region Required code as WindowFront doesn't know WindowsBack here
            WindowInteropHelper helper = new WindowInteropHelper(VideoView.WindowFront);
            helper.Owner = Handle;
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(VideoView.WindowFront);
            VideoView.WindowFront.Show();
            
            LocationChanged += (o, l) =>
            {
                var location = VideoView.WinFormsHost.PointToScreen(new System.Windows.Point(0,0));
                VideoView.WindowFront.Top = location.Y; VideoView.WindowFront.Left = location.X;
            };

            VideoView.WinFormsHost.SizeChanged += (o, l) =>
            {
                var location = VideoView.WinFormsHost.PointToScreen(new System.Windows.Point(0,0));
                VideoView.WindowFront.Top = location.Y; VideoView.WindowFront.Left = location.X;

                VideoView.WindowFront.Height = VideoView.WinFormsHost.ActualHeight;
                VideoView.WindowFront.Width = VideoView.WinFormsHost.ActualWidth;
            };
            #endregion

            VideoView.Content   = Flyleaf;
            VideoView.BeginInit();
            Player.Control      = VideoView.FlyleafWF;

            VideoView.Player    = Player;
            
            Flyleaf.Player      = Player;

            ContentControl.Content = VideoView;

        }
    }
}
