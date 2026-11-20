using System.Windows;
using Vortice.Mathematics;
using WPoint = System.Windows.Point;

namespace FlyleafLib.MediaFramework.MediaRenderer;

/// <summary>Runtime presentation adjustments for this renderer.</summary>
public unsafe partial class Renderer
{
    /// <summary>
    /// Whether to use Dolby Vision reconstruction or the compatible base layer
    /// </summary>
    public bool DoviPreferBaseLayer;

    /// <summary>
    /// Whether to use Dolby L1 metadata or Flyleaf's histogram for tone mapping
    /// </summary>
    public bool DoviUseHDRDetect;

    /// <summary>
    /// Preferred processing backend. Auto selects a suitable processor; VideoProcessor reports
    /// the effective backend after capability fallback or panorama requirements are applied.
    /// </summary>
    public VideoProcessors PreferredVideoProcessor
    {
        get => preferredVideoProcessor;
        set
        {
            if (Set(ref preferredVideoProcessor, value)) VPSwitchCheck();
        }
    }
    VideoProcessors preferredVideoProcessor = VideoProcessors.Auto;

    /// <summary>Position of alpha encoded in a split video frame.</summary>
    public SplitFrameAlphaPosition SplitFrameAlphaPosition
    {
        get => splitFrameAlphaPosition;
        set
        {
            if (Set(ref splitFrameAlphaPosition, value))
                VPRequest(VPRequestType.ReConfigVP);
        }
    }
    SplitFrameAlphaPosition splitFrameAlphaPosition;

    /// <summary>Requested deinterlacing mode (D3D11 processing only); FieldType reports the effective mode.</summary>
    public DeInterlace DeInterlace
    {
        get => deInterlace;
        set
        {
            if (!Set(ref deInterlace, value)) return;
            var request = VPRequestType.Deinterlace;
            if (scfg != null && VPSelection() != VideoProcessor)
                request |= VPRequestType.ReConfigVP;
            VPRequest(request);
        }
    }
    DeInterlace deInterlace = DeInterlace.Auto;

    /// <summary>Runtime controls for 360 panoramic projection.</summary>
    public Pano360Config Pano360 { get; }

    public CornerRadius     CornerRadius            { get => cornerRadius; internal set { if (Set(ref cornerRadius, value)) SwapChain.SetClip(); } }
    internal CornerRadius cornerRadius;

    /// <summary>
    /// Video aspect ratio
    /// </summary>
    public AspectRatio      AspectRatio             { get => aspectRatio;          set { if (Set(ref aspectRatio, value)) VPRequest(VPRequestType.AspectRatio); } }
    AspectRatio aspectRatio = AspectRatio.Keep;
    public void ToggleKeepRatio()
    {
        if (AspectRatio == AspectRatio.Keep)
            AspectRatio = AspectRatio.Fill;
        else if (AspectRatio == AspectRatio.Fill)
            AspectRatio = AspectRatio.Keep;
    }

    /// <summary>
    /// Custom aspect ratio (AspectRatio must be set to Custom to have an effect)
    /// </summary>
    public AspectRatio      AspectRatioCustom       { get => aspectRatioCustom;    set { if (Set(ref aspectRatioCustom, value) && AspectRatio == AspectRatio.Custom) { aspectRatio = AspectRatio.Fill; AspectRatio = AspectRatio.Custom; } } }
    AspectRatio aspectRatioCustom = new(16, 9);

    /// <summary>
    /// Cropping rectangle for the output frame (based on frame size)
    /// </summary>
    public CropRect         Crop                    { get => userCrop;                  set { if (!Set(ref userCrop,        value)) return; HasUserCrop = userCrop != CropRect.Empty; VPRequest(VPRequestType.Crop); } }
    internal CropRect userCrop = CropRect.Empty;
    internal bool HasUserCrop = false;

    // TODO: if we clamp to -0.5 / +0.5 for example it will not work properly with zoom point

    /// <summary>
    /// Pan X Offset to change the X location
    /// </summary>
    public double           PanXOffset              { get => panXOffset;            set { if (Set(ref panXOffset,   Math.Clamp(value, -10, 10))) VPRequest(VPRequestType.Viewport); } }
    internal double panXOffset;

    /// <summary>
    /// Pan Y Offset to change the Y location
    /// </summary>
    public double           PanYOffset              { get => panYOffset;            set { if (Set(ref panYOffset,   Math.Clamp(value, -10, 10))) VPRequest(VPRequestType.Viewport); } }
    internal double panYOffset;

    /// <summary>
    /// Rotation angle (for D3D11 VP allowed values are 0, 90, 180, 270 only)
    /// </summary>
    public uint             Rotation                { get => userRotation;              set { if (Set(ref userRotation,     value)) VPRequest(VPRequestType.RotationFlip); } }
    internal uint userRotation;

    public bool             HFlip                   { get => userHFlip;                 set { if (Set(ref userHFlip,        value)) VPRequest(VPRequestType.RotationFlip); } }
    internal bool userHFlip;

    public bool             VFlip                   { get => userVFlip;                 set { if (Set(ref userVFlip,        value)) VPRequest(VPRequestType.RotationFlip); } }
    internal bool userVFlip;

    public double           Zoom                    { get => SnapToInt(zoom * 100); set { if (Set(ref zoom, SnapToInt(value / 100))) VPRequest(VPRequestType.Viewport); } }
    internal double zoom = 1;

    public WPoint
                            ZoomCenter              { get => zoomCenter;            set { if (Set(ref zoomCenter,   value)) VPRequest(VPRequestType.Viewport); } }
    internal WPoint zoomCenter = new(0.5, 0.5);

    /// <summary>Whether zoom is relative to the source's display size in physical pixels.</summary>
    public bool OriginalSize => originalSize;
    bool originalSize;

    /// <summary>Selects original size or fit to window and centers the picture at 100% zoom.</summary>
    public void SetOriginalSize(bool enabled)
    {
        Set(ref originalSize, enabled, propertyName: nameof(OriginalSize));
        Set(ref zoom, 1.0, propertyName: nameof(Zoom));
        Set(ref zoomCenter, new(0.5, 0.5), propertyName: nameof(ZoomCenter));
        Set(ref panXOffset, 0.0, propertyName: nameof(PanXOffset));
        Set(ref panYOffset, 0.0, propertyName: nameof(PanYOffset));
        VPRequest(VPRequestType.Viewport);
    }

    public void ResetViewport()
    {
        Set(ref originalSize, false, propertyName: nameof(OriginalSize));
        SetViewport(0, 0, 0, 100, new(0.5, 0.5), CropRect.Empty, AspectRatio.Keep, false, false);
    }

    public void SetViewport(int panX, int panY, uint rotation, double zoom, WPoint p, CropRect crop, AspectRatio ratio, bool hflip, bool vflip)
    {
        AspectRatio = ratio;
        Zoom        = zoom;
        ZoomCenter  = p;
        PanXOffset  = panX;
        PanYOffset  = panY;
        Crop        = crop;
        Rotation    = rotation;
        HFlip       = hflip;
        VFlip       = vflip;

        VPRequest(VPRequestType.Crop | VPRequestType.RotationFlip);
    }

    public void RotateRight()   => Rotation = (userRotation + 90) % 360;
    public void RotateLeft()    => Rotation = userRotation < 90 ? 360 + userRotation - 90 : userRotation - 90;

    public void ZoomIn()        => Zoom += Config.Video.ZoomOffset;
    public void ZoomOut()       => Zoom = Math.Max(Zoom - Config.Video.ZoomOffset, 0);
    public void ZoomIn (WPoint p) { SetZoomWithCenterPoint(p, zoom + Config.Video.ZoomOffset / 100.0); }
    public void ZoomOut(WPoint p) { double zoom = this.zoom - Config.Video.ZoomOffset / 100.0; if (zoom < 0.001) return; SetZoomWithCenterPoint(p, zoom); }
    public void SetZoomAndCenter(double zoom, WPoint p)
    {
        Zoom        = zoom;
        ZoomCenter  = p;
        VPRequest(VPRequestType.Viewport);
    }
    internal void SetZoomWithCenterPoint(Point p, double zoom)
    {
        /* Notes
         * Zooms in a way that the specified point before zoom will be at the same position after zoom
         * 
         * Zoomed Point (ZP)    // the current point in a -possible- zoomed viewport
         * Zoom (Z)
         * Unzoomed Point (UP)  // the actual pixel of the current point
         * Viewport Point (VP)
         * Center Point (CP)
         *
         * UP = (VP + ZP) / Z =>
         * ZP = (UP * Z) - VP
         * CP = VP / (ZP - 1) (when UP = ZP)
         */

        zoom = SnapToInt(zoom);
        Viewport view = Viewport;

        if (!(p.X >= view.X && p.X < view.X + view.Width && p.Y >= view.Y && p.Y < view.Y + view.Height)) // Point out of view
        {
            Zoom = zoom * 100;
            return;
        }

        Point viewport = new(view.X, view.Y);
        RemoveViewportOffsets(ref viewport);
        RemoveViewportOffsets(ref p);

        // Finds the required center point so that p will have the same pixel after zoom
        Point zoomCenter = new(
            GetCenterPoint(zoom, ((p.X - viewport.X) / (this.zoom / zoom)) - p.X) / (view.Width  / this.zoom),
            GetCenterPoint(zoom, ((p.Y - viewport.Y) / (this.zoom / zoom)) - p.Y) / (view.Height / this.zoom));

        // TBR: bug
        //if (zoomCenter.X > 10 || zoomCenter.X < -10)

        SetZoomAndCenter(zoom * 100, zoomCenter);

        void RemoveViewportOffsets(ref Point p)
        {
            p.X -= (SideXPixels / 2 + (panXOffset * ControlWidth));
            p.Y -= (SideYPixels / 2 + (panYOffset * ControlHeight));
        }
    
        double GetCenterPoint(double zoom, double offset)
            => zoom == 1 ? offset : offset / SnapToInt(zoom - 1); // possible bug when zoom = 1 (noticed in out of bounds zoom out)
    }
}

/// <summary>
/// Runtime controls for 360 panoramic (equirectangular) video projection
/// </summary>
public class Pano360Config : NotifyPropertyChanged
{
    readonly Renderer renderer;

    internal Pano360Config(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        this.renderer = renderer;
    }

    // Hosts cancel gestures/inertia when this view is reset or its mode changes.
    internal event Action NavigationReset;

    /// <summary>Enables equirectangular projection through the shader rendering path.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value)) return;
            NavigationReset?.Invoke();
            renderer.VPRequest(VPRequestType.ReConfigVP);
        }
    }
    internal bool _enabled;

    /// <summary>Horizontal viewing angle in degrees, wrapped to [-180, 180).</summary>
    public double Yaw
    {
        get => _yaw;
        set
        {
            if (!double.IsFinite(value)) return;
            value = ((value % 360) + 540) % 360 - 180;
            if (Set(ref _yaw, value)) renderer.VPRequest(VPRequestType.UpdatePano);
        }
    }
    internal double _yaw;

    /// <summary>Vertical viewing angle in degrees [-88.2, 88.2]; positive looks down.</summary>
    public double Pitch
    {
        get => _pitch;
        set
        {
            if (!double.IsFinite(value)) return;
            if (Set(ref _pitch, Math.Clamp(value, -88.2, 88.2))) renderer.VPRequest(VPRequestType.UpdatePano);
        }
    }
    internal double _pitch;

    /// <summary>Projection zoom [0.1, 2.0], default 0.5. Separate from renderer framing zoom.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            if (!double.IsFinite(value)) return;
            if (Set(ref _zoom, Math.Clamp(value, 0.1, 2.0))) renderer.VPRequest(VPRequestType.UpdatePano);
        }
    }
    internal double _zoom = 0.5;

    /// <summary>Horizontal field of view [30, 150] degrees, before projection Zoom is applied.</summary>
    public double Fov
    {
        get => _fov;
        set
        {
            if (!double.IsFinite(value)) return;
            if (Set(ref _fov, Math.Clamp(value, 30.0, 150.0))) renderer.VPRequest(VPRequestType.UpdatePano);
        }
    }
    internal double _fov = 90;

    /// <summary>Resets orientation and projection without disabling panorama mode.</summary>
    public void Reset()
    {
        NavigationReset?.Invoke();
        Yaw = Pitch = 0;
        Zoom = 0.5;
        Fov = 90;
    }
}
