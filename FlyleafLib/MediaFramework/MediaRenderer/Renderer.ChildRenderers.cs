using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.Utils;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    #region Child Renderers Management
    private readonly List<ChildRenderer> childRenderers = new();
    private readonly object lockChildRenderers = new();

    /// <summary>
    /// Registers a child renderer that will receive frames from this main renderer
    /// The child renderer will share the same D3D11 device
    /// </summary>
    /// <param name="childRenderer">The child renderer to register</param>
    public void RegisterChildRenderer(ChildRenderer childRenderer)
    {
        if (childRenderer == null)
            throw new ArgumentNullException(nameof(childRenderer));

        if (childRenderer.ParentRenderer != this)
            throw new InvalidOperationException("Child renderer must have this renderer as its parent");

        lock (lockChildRenderers)
        {
            if (!childRenderers.Contains(childRenderer))
            {
                childRenderers.Add(childRenderer);
                if (Logger.CanInfo) Log.Info($"Registered child renderer #{childRenderer.UniqueId}");
            }
        }
    }

    /// <summary>
    /// Unregisters a child renderer
    /// </summary>
    /// <param name="childRenderer">The child renderer to unregister</param>
    public void UnregisterChildRenderer(ChildRenderer childRenderer)
    {
        if (childRenderer == null)
            return;

        lock (lockChildRenderers)
        {
            if (childRenderers.Remove(childRenderer))
            {
                if (Logger.CanInfo) Log.Info($"Unregistered child renderer #{childRenderer.UniqueId}");
            }
        }
    }

    /// <summary>
    /// Gets all registered child renderers
    /// </summary>
    public IReadOnlyList<ChildRenderer> GetChildRenderers()
    {
        lock (lockChildRenderers)
        {
            return childRenderers.ToList();
        }
    }

    /// <summary>
    /// Renders the current frame to all active child renderers
    /// Should be called during the main render loop after rendering to the main swap chain
    /// </summary>
    private void RenderToChildRenderers(VideoFrame frame, bool secondField)
    {
        lock (lockChildRenderers)
        {
            if (childRenderers.Count == 0)
                return;

            foreach (var child in childRenderers)
            {
                if (child.IsActive && !child.Disposed)
                {
                    try
                    {
                        child.RenderFrame(frame, secondField);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Failed to render to child renderer #{child.UniqueId}: {e.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Presents all child renderers
    /// Should be called after the main renderer presents
    /// </summary>
    private void PresentChildRenderers()
    {
        lock (lockChildRenderers)
        {
            if (childRenderers.Count == 0)
                return;

            foreach (var child in childRenderers)
            {
                if (child.IsActive && !child.Disposed)
                {
                    try
                    {
                        child.Present();
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Failed to present child renderer #{child.UniqueId}: {e.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Disposes all child renderers
    /// </summary>
    private void DisposeChildRenderers()
    {
        lock (lockChildRenderers)
        {
            foreach (var child in childRenderers)
            {
                try
                {
                    child.Dispose();
                }
                catch (Exception e)
                {
                    Log.Error($"Failed to dispose child renderer #{child.UniqueId}: {e.Message}");
                }
            }
            childRenderers.Clear();
        }
    }
    #endregion
}
