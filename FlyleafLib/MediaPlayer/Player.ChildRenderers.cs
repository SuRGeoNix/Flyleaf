using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib.MediaPlayer;

public unsafe partial class Player
{
    #region Child Renderers
    /// <summary>
    /// Creates a child renderer that shares the D3D11 device with the main renderer
    /// The child renderer will display the same video content with independent pan/zoom/rotation settings
    /// </summary>
    /// <param name="handle">Window handle for the child renderer's output surface</param>
    /// <param name="uniqueId">Optional unique identifier for the child renderer</param>
    /// <returns>The created ChildRenderer instance, or null if no renderer is available</returns>
    public ChildRenderer CreateChildRenderer(nint handle, int uniqueId = -1)
    {
        if (renderer == null)
        {
            Log?.Error("Cannot create child renderer: Main renderer is not initialized");
            return null;
        }

        if (handle == nint.Zero)
        {
            Log?.Error("Cannot create child renderer: Invalid window handle");
            return null;
        }

        try
        {
            var childRenderer = new ChildRenderer(renderer, handle, uniqueId);
            childRenderer.Initialize();
            renderer.RegisterChildRenderer(childRenderer);
            
            Log?.Info($"Created child renderer #{childRenderer.UniqueId}");
            return childRenderer;
        }
        catch (Exception e)
        {
            Log?.Error($"Failed to create child renderer: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Removes and disposes a child renderer
    /// </summary>
    /// <param name="childRenderer">The child renderer to remove</param>
    public void RemoveChildRenderer(ChildRenderer childRenderer)
    {
        if (childRenderer == null || renderer == null)
            return;

        try
        {
            renderer.UnregisterChildRenderer(childRenderer);
            childRenderer.Dispose();
            Log?.Info($"Removed child renderer #{childRenderer.UniqueId}");
        }
        catch (Exception e)
        {
            Log?.Error($"Failed to remove child renderer: {e.Message}");
        }
    }

    /// <summary>
    /// Gets all registered child renderers
    /// </summary>
    /// <returns>Read-only list of child renderers</returns>
    public IReadOnlyList<ChildRenderer> GetChildRenderers()
    {
        return renderer?.GetChildRenderers() ?? new List<ChildRenderer>();
    }
    #endregion
}
