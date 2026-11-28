using System.Numerics;
using FlyleafLib.MediaFramework.MediaDecoder;
using FlyleafLib.MediaFramework.MediaFrame;
using FlyleafLib.Plugins;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using RectangleF = System.Drawing.RectangleF;

namespace FlyleafLib.MediaFramework.MediaRenderer;

public unsafe partial class Renderer
{
    const int SubtitleColorSlot = 4;
    const string HdrSubtitleShader = "subtitle-scRGB";

    readonly object lockSubtitleOverlay = new();
    ID3D11BlendState hdrSubtitleBlend;
    ID3D11Buffer hdrSubtitleBuffer;
    float hdrSubtitleWhiteScale;
    SubtitlesFrame bitmapSubtitle;
    int bitmapWidth, bitmapHeight;
    int scaledBitmapWidth, scaledBitmapHeight;
    SwsFlags bitmapScaleQuality;
    RectangleF subtitleObstruction;
    float subtitleObstructionGap;
    IReadOnlyList<SubtitlesOverlayRect> pluginOverlay;
    readonly List<SubtitleTexture> bitmapTextures = [], pluginTextures = [];
    readonly ID3D11ShaderResourceView[] subtitleResource = new ID3D11ShaderResourceView[1];

    sealed record SubtitleTexture(ID3D11ShaderResourceView View, int X, int Y, int Width, int Height, RectangleF Ink);

    /// <summary>The SDR reference white used for subtitles on the active HDR output, in nits.</summary>
    public float? SubtitleWhiteNits => SwapChain.ActiveColorSpace == ColorSpaceType.RgbFullG10NoneP709 && hdrSubtitleBlend != null
        ? (float.IsFinite(autoSDRPeakNits) && autoSDRPeakNits > 0 ? autoSDRPeakNits : 203.0f) : null;

    void SetupHdrSubtitles()
    {
        using var shader = ShaderCompiler.Compile(ShaderCompiler.PS_SUBTITLES_SCRGB);
        if (shader == null)
            return;

        psShader[HdrSubtitleShader] = device.CreatePixelShader(shader);
        hdrSubtitleBuffer = device.CreateBuffer(new BufferDescription
        {
            BindFlags = BindFlags.ConstantBuffer, Usage = ResourceUsage.Default, ByteWidth = (uint)sizeof(Vector4)
        });
        hdrSubtitleWhiteScale = 0;

        // Subtitle RGB is straight alpha and the composition target is premultiplied.
        var blend = blendDesc;
        blend.RenderTarget[0].SourceBlendAlpha = Blend.One;
        blend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
        hdrSubtitleBlend = device.CreateBlendState(blend);
    }

    internal void SubsConfig(int width, int height)
    {
        lock (lockSubtitleOverlay)
        {
            SubsDispose();
            bitmapWidth = width;
            bitmapHeight = height;
        }
    }

    internal void SubsFillRects(SubtitlesFrame frame)
    {
        lock (lockSubtitleOverlay)
        {
            ClearBitmapSubtitle();
            bitmapSubtitle = frame;
        }
    }

    /// <summary>Sets the area that bitmap subtitle overlays should avoid. Pass an empty rectangle to clear it.</summary>
    /// <param name="bounds">Obstruction bounds relative to the full render surface.</param>
    /// <param name="gap">Space above the obstruction as a fraction of the surface height.</param>
    public void SetBitmapSubtitleObstruction(RectangleF bounds, float gap = 0)
    {
        bool redraw;
        lock (lockSubtitleOverlay)
        {
            if (subtitleObstruction == bounds && subtitleObstructionGap == gap)
                return;

            subtitleObstruction = bounds;
            subtitleObstructionGap = gap;
            redraw = bitmapSubtitle != null || pluginTextures.Count != 0;
        }

        if (redraw)
            RenderRequest();
    }

    void FLSubsRender(long timestamp)
    {
        if (SubsRender(timestamp))
            context.PSSetShader(psShader[psId]);
    }

    void D3SubsRender(long timestamp) => SubsRender(timestamp);

    bool SubsRender(long timestamp)
    {
        lock (lockSubtitleOverlay)
        {
            Viewport videoView = Viewport;
            Viewport pluginView = videoView;
            IReadOnlyList<SubtitlesOverlayRect> overlay = null;
            if (player?.SubtitlesDecoder is { HasOverlayRenderer: true } decoder)
            {
                var style = player.Subtitles.RenderStyle;
                if (decoder.SubtitlesStream?.CodecID == AVCodecID.Subrip)
                    pluginView = SubRipViewport(videoView, ControlWidth, ControlHeight, style.WithinVideo);

                if (pluginView.Width >= 1 && pluginView.Height >= 1)
                {
                    var request = new SubtitlesRenderRequest(0, (int)pluginView.Width, (int)pluginView.Height,
                        (int)(scfg?.Width ?? 0), (int)(scfg?.Height ?? 0), style) { SurfaceWidth = ControlWidth };
                    overlay = decoder.RenderSubtitles(timestamp, request);
                }
            }

            UpdatePluginSubtitles(overlay);

            if (bitmapSubtitle != null &&
                (scaledBitmapWidth != (int)videoView.Width || scaledBitmapHeight != (int)videoView.Height ||
                 bitmapScaleQuality != ucfg.BitmapSubsScaleQuality))
                ScaleBitmapSubtitles((int)videoView.Width, (int)videoView.Height);

            if (bitmapTextures.Count == 0 && pluginTextures.Count == 0)
                return false;

            context.OMSetRenderTargets(SwapChain.BackBufferRtv);
            context.PSSetSampler(0, samplerPoint);
            context.VSSetShader(vsSimple);
            BindSubtitleOutput();

            DrawSubtitles(bitmapTextures, videoView, SubtitleOverlayLift(bitmapTextures, videoView));
            DrawSubtitles(pluginTextures, pluginView, SubtitleOverlayLift(pluginTextures, pluginView));

            subtitleResource[0] = null;
            context.PSSetShaderResources(0, subtitleResource);
            context.OMSetBlendState(null);
            context.RSSetViewport(videoView);
            context.VSSetShader(vsMain);
            context.PSSetSampler(0, samplerLinear);
            return true;
        }
    }

    void BindSubtitleOutput()
    {
        if (SubtitleWhiteNits is not float whiteNits)
        {
            context.OMSetBlendState(blendStateAlpha);
            context.PSSetShader(psShader["rgba"]);
            return;
        }

        float scale = whiteNits / 80.0f;
        if (scale != hdrSubtitleWhiteScale)
        {
            context.UpdateSubresource(new Vector4(scale, 0, 0, 0), hdrSubtitleBuffer);
            hdrSubtitleWhiteScale = scale;
        }

        context.PSSetConstantBuffer(SubtitleColorSlot, hdrSubtitleBuffer);
        context.OMSetBlendState(hdrSubtitleBlend);
        context.PSSetShader(psShader[HdrSubtitleShader]);
    }

    void UpdatePluginSubtitles(IReadOnlyList<SubtitlesOverlayRect> overlay)
    {
        if (ReferenceEquals(pluginOverlay, overlay))
            return;

        ReleaseSubtitleTextures(pluginTextures);
        pluginOverlay = null;
        if (overlay != null)
            foreach (var rect in overlay)
            {
                if (rect.Width <= 0 || rect.Height <= 0 || rect.Width > 16384 || rect.Height > 16384 ||
                    rect.Pixels.Length < (long)rect.Width * rect.Height * 4)
                    continue;

                using var pixels = rect.Pixels.Pin();
                UploadSubtitle(pluginTextures, rect.X, rect.Y, rect.Width, rect.Height, (byte*)pixels.Pointer, rect.Width * 4);
            }
        pluginOverlay = overlay;
    }

    static Viewport SubRipViewport(Viewport video, int width, int height, bool withinVideo)
    {
        if (!withinVideo)
            return new Viewport(0, 0, width, height);

        float left = Math.Max(0, video.X), top = Math.Max(0, video.Y);
        float right = Math.Min(width, video.X + video.Width), bottom = Math.Min(height, video.Y + video.Height);
        return new Viewport(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    float SubtitleOverlayLift(List<SubtitleTexture> textures, Viewport viewport)
    {
        if (textures.Count == 0 || subtitleObstruction.Width <= 0 || subtitleObstruction.Height <= 0)
            return 0;

        var surface = new RectangleF(0, 0, ControlWidth, ControlHeight);
        var visibleVideo = RectangleF.Intersect(surface, new RectangleF(viewport.X, viewport.Y, viewport.Width, viewport.Height));
        var obstruction = new RectangleF(subtitleObstruction.X * surface.Width, subtitleObstruction.Y * surface.Height,
            subtitleObstruction.Width * surface.Width, subtitleObstruction.Height * surface.Height);

        bool overlaps = false;
        float lift = 0, available = visibleVideo.Height;
        foreach (var rect in textures)
        {
            var ink = rect.Ink;
            ink.Offset(viewport.X, viewport.Y);
            var visible = RectangleF.Intersect(visibleVideo, ink);
            if (visible.Width <= 0 || visible.Height <= 0)
                continue;

            // Move all regions together and keep the topmost one inside the picture.
            available = Math.Min(available, visible.Top - visibleVideo.Top);
            if (visible.Right > obstruction.Left && visible.Left < obstruction.Right && visible.Bottom > obstruction.Top)
            {
                overlaps |= visible.Top < obstruction.Bottom;
                lift = Math.Max(lift, visible.Bottom - obstruction.Top + subtitleObstructionGap * surface.Height);
            }
        }

        return overlaps ? Math.Min(lift, available) : 0;
    }

    void DrawSubtitles(List<SubtitleTexture> textures, Viewport viewport, float lift)
    {
        foreach (var rect in textures)
        {
            context.RSSetViewport(new Viewport(viewport.X + rect.X, viewport.Y + rect.Y - lift, rect.Width, rect.Height));
            subtitleResource[0] = rect.View;
            context.PSSetShaderResources(0, subtitleResource);
            context.Draw(6, 0);
        }
    }

    void UploadSubtitle(List<SubtitleTexture> textures, int x, int y, int width, int height, byte* pixels, int stride)
    {
        var ink = SubtitleInk(pixels, stride, width, height);
        if (ink.IsEmpty)
            return;

        ink.Offset(x, y);
        var desc = new Texture2DDescription
        {
            Usage = ResourceUsage.Default, Width = (uint)width, Height = (uint)height,
            Format = Format.B8G8R8A8_UNorm, ArraySize = 1, MipLevels = 1,
            BindFlags = BindFlags.ShaderResource, SampleDescription = new(1, 0)
        };
        using var texture = device.CreateTexture2D(desc, [new SubresourceData { DataPointer = (nint)pixels, RowPitch = (uint)stride }]);
        textures.Add(new(device.CreateShaderResourceView(texture), x, y, width, height, ink));
    }

    void ScaleBitmapSubtitles(int width, int height)
    {
        ReleaseSubtitleTextures(bitmapTextures);
        scaledBitmapWidth = width;
        scaledBitmapHeight = height;
        bitmapScaleQuality = ucfg.BitmapSubsScaleQuality;

        int sourceWidth = bitmapSubtitle.BitmapWidth > 0 ? bitmapSubtitle.BitmapWidth : bitmapWidth;
        int sourceHeight = bitmapSubtitle.BitmapHeight > 0 ? bitmapSubtitle.BitmapHeight : bitmapHeight;
        if (width < 1 || height < 1 || sourceWidth < 1 || sourceHeight < 1)
            return;

        double scaleX = (double)width / sourceWidth, scaleY = (double)height / sourceHeight;
        for (int i = 0; i < bitmapSubtitle.sub.num_rects; i++)
        {
            var rect = bitmapSubtitle.sub.rects[i];
            if (rect->type != AVSubtitleType.Bitmap)
                continue;
            int x = (int)Math.Round(rect->x * scaleX), y = (int)Math.Round(rect->y * scaleY);
            int rectWidth = (int)Math.Round((rect->x + (double)rect->w) * scaleX) - x;
            int rectHeight = (int)Math.Round((rect->y + (double)rect->h) * scaleY) - y;
            if (rect->w <= 0 || rect->h <= 0 || rectWidth <= 0 || rectHeight <= 0 || rectWidth > 16384 || rectHeight > 16384)
                continue;

            var frame = av_frame_alloc();
            if (frame == null)
                continue;

            SwsContext* sws = null;
            try
            {
                frame->format = (int)AVPixelFormat.BGRA;
                frame->width = rectWidth;
                frame->height = rectHeight;
                if (av_frame_get_buffer(frame, 0) < 0)
                    continue;

                sws = sws_getContext(rect->w, rect->h, AVPixelFormat.Pal8, rectWidth, rectHeight,
                    AVPixelFormat.BGRA, bitmapScaleQuality, null, null, null);
                if (sws == null || sws_scale(sws, (byte**)&rect->data, (int*)&rect->linesize, 0, rect->h,
                    (byte**)&frame->data, (int*)&frame->linesize) != rectHeight)
                    continue;

                UploadSubtitle(bitmapTextures, x, y, rectWidth, rectHeight, (byte*)frame->data[0], frame->linesize[0]);
            }
            finally
            {
                if (sws != null) sws_freeContext(sws);
                av_frame_free(&frame);
            }
        }
    }

    static RectangleF SubtitleInk(byte* pixels, int stride, int width, int height)
    {
        int left = width, top = height, right = 0, bottom = 0;
        for (int y = 0; y < height; y++)
        {
            byte* row = pixels + y * stride;
            int first = 0;
            while (first < width && row[first * 4 + 3] == 0) first++;
            if (first == width) continue;

            int last = width - 1;
            while (last > first && row[last * 4 + 3] == 0) last--;
            left = Math.Min(left, first);
            right = Math.Max(right, last + 1);
            top = Math.Min(top, y);
            bottom = y + 1;
        }
        return bottom > top ? RectangleF.FromLTRB(left, top, right, bottom) : default;
    }

    static void ReleaseSubtitleTextures(List<SubtitleTexture> textures)
    {
        foreach (var rect in textures)
            rect.View.Dispose();
        textures.Clear();
    }

    void ClearBitmapSubtitle()
    {
        if (bitmapSubtitle != null)
            SubtitlesDecoder.DisposeFrame(bitmapSubtitle);
        bitmapSubtitle = null;
        scaledBitmapWidth = scaledBitmapHeight = 0;
        ReleaseSubtitleTextures(bitmapTextures);
    }

    internal void SubsClearBitmap()
    {
        lock (lockSubtitleOverlay)
            ClearBitmapSubtitle();
    }

    internal void SubsDispose()
    {
        lock (lockSubtitleOverlay)
        {
            ClearBitmapSubtitle();
            pluginOverlay = null;
            ReleaseSubtitleTextures(pluginTextures);
        }
    }
}
