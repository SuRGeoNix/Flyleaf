using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaRenderer;
using FlyleafLib.Controls.WPF;

namespace FlyleafPlayer.ChildRendererSample
{
    /// <summary>
    /// Sample application demonstrating Child Renderer feature
    /// Shows the same video in a main view and multiple thumbnail views
    /// </summary>
    public partial class MainWindow : Window
    {
        private Player player;
        private ChildRenderer thumbnailRenderer1;
        private ChildRenderer thumbnailRenderer2;
        
        public MainWindow()
        {
            InitializeComponent();
            InitializePlayer();
            SetupEventHandlers();
        }

        private void InitializePlayer()
        {
            // Initialize Flyleaf Engine
            Engine.Start(new EngineConfig()
            {
                FFmpegPath = ":FFmpeg",
                FFmpegDevices = false,
                PluginsPath = ":Plugins",
                UIRefresh = false,
                UIRefreshInterval = 250
            });

            // Create player configuration
            var config = new Config
            {
                Player = 
                { 
                    AutoPlay = true,
                    SeekAccurate = true
                },
                Video = 
                {
                    AspectRatio = AspectRatio.Keep,
                    ClearScreen = true
                },
                Audio = 
                {
                    Enabled = true
                },
                Subtitles = 
                {
                    Enabled = true
                }
            };

            // Create player
            player = new Player(config);
            
            // Attach to main host
            MainVideoHost.Player = player;
            
            // Subscribe to player events
            player.OpenCompleted += Player_OpenCompleted;
        }

        private void Player_OpenCompleted(object sender, OpenCompletedArgs e)
        {
            if (e.Success)
            {
                // Create child renderers after video is opened
                Dispatcher.Invoke(() => 
                {
                    CreateChildRenderers();
                });
            }
        }

        private void CreateChildRenderers()
        {
            // Create first thumbnail renderer
            if (Thumbnail1Host.Surface != null)
            {
                var handle1 = new WindowInteropHelper(Thumbnail1Host.Surface).Handle;
                thumbnailRenderer1 = player.CreateChildRenderer(handle1, 1);
                
                if (thumbnailRenderer1 != null)
                {
                    // Configure as small thumbnail with slight zoom out
                    thumbnailRenderer1.Zoom = 0.8;
                    UpdateStatus($"Created thumbnail renderer 1 (ID: {thumbnailRenderer1.UniqueId})");
                }
            }

            // Create second thumbnail renderer
            if (Thumbnail2Host.Surface != null)
            {
                var handle2 = new WindowInteropHelper(Thumbnail2Host.Surface).Handle;
                thumbnailRenderer2 = player.CreateChildRenderer(handle2, 2);
                
                if (thumbnailRenderer2 != null)
                {
                    // Configure with different view
                    thumbnailRenderer2.Zoom = 1.2;
                    thumbnailRenderer2.PanXOffset = -50;
                    UpdateStatus($"Created thumbnail renderer 2 (ID: {thumbnailRenderer2.UniqueId})");
                }
            }
        }

        private void SetupEventHandlers()
        {
            // File menu
            btnOpen.Click += BtnOpen_Click;
            btnExit.Click += (s, e) => Close();
            
            // Thumbnail interactions
            Thumbnail1Border.MouseLeftButtonDown += (s, e) => OnThumbnailClicked(thumbnailRenderer1, 1);
            Thumbnail2Border.MouseLeftButtonDown += (s, e) => OnThumbnailClicked(thumbnailRenderer2, 2);
            
            // Zoom controls
            btnZoomIn1.Click += (s, e) => ZoomThumbnail(thumbnailRenderer1, 1.2);
            btnZoomOut1.Click += (s, e) => ZoomThumbnail(thumbnailRenderer1, 0.8);
            btnReset1.Click += (s, e) => ResetThumbnail(thumbnailRenderer1);
            
            btnZoomIn2.Click += (s, e) => ZoomThumbnail(thumbnailRenderer2, 1.2);
            btnZoomOut2.Click += (s, e) => ZoomThumbnail(thumbnailRenderer2, 0.8);
            btnReset2.Click += (s, e) => ResetThumbnail(thumbnailRenderer2);
            
            // Rotation controls
            btnRotate1.Click += (s, e) => RotateThumbnail(thumbnailRenderer1);
            btnRotate2.Click += (s, e) => RotateThumbnail(thumbnailRenderer2);
            
            // Flip controls
            btnFlipH1.Click += (s, e) => FlipThumbnail(thumbnailRenderer1, true, false);
            btnFlipV1.Click += (s, e) => FlipThumbnail(thumbnailRenderer1, false, true);
            btnFlipH2.Click += (s, e) => FlipThumbnail(thumbnailRenderer2, true, false);
            btnFlipV2.Click += (s, e) => FlipThumbnail(thumbnailRenderer2, false, true);
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm|All Files|*.*",
                Title = "Select a video file"
            };

            if (dialog.ShowDialog() == true)
            {
                player.Open(dialog.FileName);
            }
        }

        private void OnThumbnailClicked(ChildRenderer renderer, int index)
        {
            if (renderer == null)
                return;

            UpdateStatus($"Thumbnail {index} clicked - Toggling zoom");
            
            // Toggle between normal and zoomed view
            if (renderer.Zoom > 1.0)
                renderer.Zoom = 0.8;
            else
                renderer.Zoom = 1.5;
        }

        private void ZoomThumbnail(ChildRenderer renderer, double factor)
        {
            if (renderer == null)
                return;

            renderer.Zoom *= factor;
            renderer.Zoom = Math.Max(0.1, Math.Min(renderer.Zoom, 5.0)); // Clamp between 0.1 and 5.0
            UpdateStatus($"Zoom: {renderer.Zoom:F2}x");
        }

        private void ResetThumbnail(ChildRenderer renderer)
        {
            if (renderer == null)
                return;

            renderer.SetPanAll(0, 0, 0, 1.0, new Point(0.5, 0.5));
            renderer.HFlip = false;
            renderer.VFlip = false;
            UpdateStatus("Reset to default view");
        }

        private void RotateThumbnail(ChildRenderer renderer)
        {
            if (renderer == null)
                return;

            renderer.Rotation = (renderer.Rotation + 90) % 360;
            UpdateStatus($"Rotation: {renderer.Rotation}°");
        }

        private void FlipThumbnail(ChildRenderer renderer, bool horizontal, bool vertical)
        {
            if (renderer == null)
                return;

            if (horizontal)
            {
                renderer.HFlip = !renderer.HFlip;
                UpdateStatus($"Horizontal Flip: {renderer.HFlip}");
            }
            
            if (vertical)
            {
                renderer.VFlip = !renderer.VFlip;
                UpdateStatus($"Vertical Flip: {renderer.VFlip}");
            }
        }

        private void UpdateStatus(string message)
        {
            StatusText.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up child renderers
            if (thumbnailRenderer1 != null)
            {
                player?.RemoveChildRenderer(thumbnailRenderer1);
                thumbnailRenderer1 = null;
            }
            
            if (thumbnailRenderer2 != null)
            {
                player?.RemoveChildRenderer(thumbnailRenderer2);
                thumbnailRenderer2 = null;
            }

            // Dispose player
            player?.Dispose();
            
            // Shutdown engine
            Engine.Dispose();
            
            base.OnClosed(e);
        }
    }
}
