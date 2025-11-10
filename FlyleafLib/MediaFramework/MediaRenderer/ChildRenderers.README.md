# Child Renderers Support

This feature allows displaying the same video stream in multiple host controls simultaneously, useful for scenarios like:
- Displaying multiple video streams as thumbnails with a selected thumbnail shown in a larger view
- Picture-in-picture implementations
- Multi-monitor setups with synchronized playback

## Architecture

The implementation uses **child swap chains** that share the same D3D11 device with the main renderer. This approach:
- Avoids copying frames between devices (efficient)
- Requires all surfaces to be on the same GPU adapter
- Supports independent pan, zoom, and rotation on each child renderer
- Child renderers are not full players (no separate audio output)

## Key Components

### 1. ChildRenderer Class
Located in `FlyleafLib/MediaFramework/MediaRenderer/ChildRenderer.cs`

A child renderer manages its own swap chain and can render frames from the parent renderer with independent viewing settings:
- Pan (X/Y offsets)
- Zoom (scale factor and center point)
- Rotation (0°, 90°, 180°, 270°)
- Horizontal/Vertical flip

### 2. Renderer.ChildRenderers.cs
Extension to the main Renderer class that provides:
- `RegisterChildRenderer()` - Registers a child renderer
- `UnregisterChildRenderer()` - Unregisters a child renderer
- `GetChildRenderers()` - Gets all registered child renderers
- Internal methods for rendering and presenting to child renderers

### 3. Player.ChildRenderers.cs
Player-level API for easier child renderer management:
- `CreateChildRenderer()` - Creates and registers a new child renderer
- `RemoveChildRenderer()` - Removes and disposes a child renderer
- `GetChildRenderers()` - Gets all child renderers

## Usage Example (WPF)

### Basic Setup

```csharp
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using FlyleafLib.MediaFramework.MediaRenderer;

public partial class MainWindow : Window
{
    private Player player;
    private ChildRenderer thumbnailRenderer;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize player
        Engine.Start(new EngineConfig());
        player = new Player(new Config());
        
        // Attach player to main view
        mainVideoHost.Player = player;
        
        // Create child renderer for thumbnail view
        // thumbnailHost is another FlyleafHost control
        var thumbnailHandle = new WindowInteropHelper(thumbnailHost.Surface).Handle;
        thumbnailRenderer = player.CreateChildRenderer(thumbnailHandle);
        
        if (thumbnailRenderer != null)
        {
            // Configure thumbnail with zoom out for overview
            thumbnailRenderer.Zoom = 0.5;
        }
    }
    
    private void OnThumbnailClick(object sender, EventArgs e)
    {
        // Can adjust child renderer view independently
        thumbnailRenderer.Zoom = 1.0;
        thumbnailRenderer.SetPanAll(0, 0, 0, 1.0, new Point(0.5, 0.5));
    }
    
    protected override void OnClosed(EventArgs e)
    {
        thumbnailRenderer?.Dispose();
        player?.Dispose();
        base.OnClosed(e);
    }
}
```

### Multiple Thumbnails

```csharp
private List<ChildRenderer> thumbnailRenderers = new();

private void CreateThumbnailGrid()
{
    foreach (var thumbnailHost in thumbnailHostsGrid.Children.OfType<FlyleafHost>())
    {
        var handle = new WindowInteropHelper(thumbnailHost.Surface).Handle;
        var childRenderer = player.CreateChildRenderer(handle);
        
        if (childRenderer != null)
        {
            // Configure each thumbnail independently
            childRenderer.Zoom = 0.3;
            thumbnailRenderers.Add(childRenderer);
        }
    }
}

private void CleanupThumbnails()
{
    foreach (var renderer in thumbnailRenderers)
    {
        player.RemoveChildRenderer(renderer);
    }
    thumbnailRenderers.Clear();
}
```

### Dynamic Pan/Zoom on Child Renderer

```csharp
// Pan the child renderer view
thumbnailRenderer.PanXOffset = 100;  // Pan right by 100 pixels
thumbnailRenderer.PanYOffset = -50;  // Pan up by 50 pixels

// Zoom with center point
thumbnailRenderer.SetZoomAndCenter(2.0, new Point(0.3, 0.3));

// Zoom to specific point maintaining that point's position
Point clickPoint = new Point(200, 150);
thumbnailRenderer.ZoomWithCenterPoint(clickPoint, 1.5);

// Rotate
thumbnailRenderer.Rotation = 90;  // 0, 90, 180, or 270

// Flip
thumbnailRenderer.HFlip = true;
thumbnailRenderer.VFlip = false;

// Reset all transformations
thumbnailRenderer.SetPanAll(0, 0, 0, 1.0, new Point(0.5, 0.5));
```

## Performance Considerations

1. **Shared Device**: All child renderers share the same D3D11 device as the main renderer
   - Must be on the same GPU adapter
   - Cross-adapter scenarios will require additional copying (not currently implemented)

2. **Video Processor Path**: 
   - For D3D11 Video Processor: Child renderers copy from parent's backBuffer
   - For Flyleaf Shaders: Child renderers use frame's SRVs directly (more efficient)

3. **Synchronization**: Child renderers render and present synchronously with the main renderer
   - All rendering happens on the same thread
   - Present operations are sequential

## Limitations

1. Child renderers are **not independent players**:
   - No separate audio output
   - No separate playback controls
   - No separate subtitle rendering (currently)

2. GPU adapter constraints:
   - All surfaces must be on the same GPU adapter
   - Moving windows to different monitors on different GPUs is not supported

3. No separate configuration:
   - VSync and buffer settings are inherited from parent renderer

## Future Enhancements

Possible future improvements:
- Support for subtitle rendering on child renderers
- Support for overlay content on child renderers
- Cross-adapter rendering with automatic texture copying
- Asynchronous rendering option for better performance
- Independent VSync settings per child renderer

## Thread Safety

All child renderer operations are thread-safe:
- Registration/unregistration is protected by locks
- Rendering operations are synchronized with the main renderer
- Disposal is safe to call from any thread

## Best Practices

1. **Initialize after player is ready**: Create child renderers after the player has opened a video stream
2. **Dispose properly**: Always dispose child renderers before disposing the player
3. **Handle window resize**: Call `childRenderer.Resize(width, height)` when the window size changes
4. **Check IsActive**: Verify `childRenderer.IsActive` before performing operations
5. **Handle failures**: Check for null returns from `CreateChildRenderer()` and handle errors gracefully
