using FlyleafLib.Custom;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Mathematics;

using static FlyleafLib.MediaFramework.MediaRenderer.Renderer;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public partial class BitmapRenderer : IVP, ICustomRenderer
{
    internal LogHandler Log;
    private double _curRatio = 1.0;
    private ZoomConstraint _zoomConstraint = new ZoomConstraint(1.0, 50.0);
    public VPConfig VideoConfig => ucfg;
    VPConfig ucfg;
    public Renderer Renderer { get; private set; }

    private readonly int UniqueId;

    public SwapChain SwapChain { get; private set; }
    public Viewport Viewport { get; private set; }
    public int ControlWidth { get; private set; }
    public int ControlHeight { get; private set; }
    public int SideXPixels => sideXPixels;
    public int SideYPixels => sideYPixels;

    public double InitialZoom
    {
        get => _zoomConstraint.InitialZoom;
        set => _zoomConstraint.InitialZoom = value;
    }
    public double MaximalZoom
    {
        get => _zoomConstraint.MaximalZoom;
        set => _zoomConstraint.MaximalZoom = value;
    }
    public double ValidateZoom(double zoom) => _zoomConstraint.ValidateZoom(zoom);
    

    int  sideXPixels, sideYPixels;

    Vortice.Direct3D11.ID3D11DeviceContext     _context;
    ID3D11Buffer            vsBuffer;
    VSBufferType            vsData    = new();

    VPRequestType   vpRequestsIn, vpRequests; // In: From User | ProcessRequests Copy
        
    public event Action CustomSetSize;

    public BitmapRenderer(Renderer renderer, VPConfig config, int uniqueId = -1)
    {
        ucfg = config;
        this.Renderer = renderer;

        UniqueId = uniqueId == -1 ? GetUniqueId() : uniqueId;
        Log = new(("[#" + UniqueId + "]").PadRight(8, ' ') + " [BitmapRenderer ] ");

        _context = Renderer.Device.CreateDeferredContext();
        vsBuffer = Renderer.Device.CreateBuffer(vsDesc);

        _context.IASetVertexBuffer(0, Renderer.vertexBuffer, sizeof(float) * 5);
        _context.IASetInputLayout(Renderer.inputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetConstantBuffer(0, vsBuffer);
        _context.VSSetShader(Renderer.vsMain);
        _context.PSSetSampler(0, Renderer.samplerLinear);
        _context.PSSetShader(Renderer.psShader["rgba"]);
    }
    void IVP.VPRequest(VPRequestType request)
        => VPRequest(request);

    internal void VPRequest(VPRequestType request)
    {
        vpRequestsIn |= request;
    }

    void IVP.MonitorChanged(GPUOutput monitor) { }

    private void SetViewport(int width, int height)
    {
        int x, y, newWidth, newHeight, xZoomPixels, yZoomPixels;

        var fillRatio = (double) ControlWidth/ControlHeight;

        var zoom = ValidateZoom(ucfg.zoom);
        
        if (_curRatio < fillRatio)
        {
            newHeight = (int)(height * zoom);
            newWidth =  (int)(newHeight * _curRatio);

            sideXPixels = ((int)(width - (height * _curRatio))) & ~1;
            sideYPixels = 0;

            y = (int)(height * ucfg.panYOffset);
            x = (int)(width * ucfg.panXOffset) + (sideXPixels / 2);

            yZoomPixels = newHeight - height;
            xZoomPixels = newWidth - (width - sideXPixels);
        }
        else
        {
            newWidth = (int)(width * zoom);
            newHeight = (int) (newWidth / _curRatio);

            sideYPixels = ((int)(height - (width / _curRatio))) & ~1;
            sideXPixels = 0;

            x = (int)(width * ucfg.panXOffset);
            y = (int)(height * ucfg.panYOffset) + (sideYPixels / 2);

            xZoomPixels = newWidth - width;
            yZoomPixels = newHeight - (height - sideYPixels);
        }

        Viewport = new((int)(x - xZoomPixels * (float)ucfg.zoomCenter.X), (int)(y - yZoomPixels * (float)ucfg.zoomCenter.Y), newWidth, newHeight);        
    }
    public void UpdateSize(int width, int height)
    {   // TBR
        ControlWidth = width;
        ControlHeight = height;
    }
}
