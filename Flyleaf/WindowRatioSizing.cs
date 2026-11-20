namespace Flyleaf;

// Physical-pixel geometry for WM_SIZING. Edges use the dragged dimension;
// corners project the proposed size onto the aspect-ratio line.
internal static class WindowRatioSizing
{
    internal static (int Left, int Top, int Width, int Height) AroundVideo(int left, int top, int width, int height,
        int leftInset, int topInset, int rightInset, int bottomInset)
        => (left - leftInset, top - topInset, width + leftInset + rightInset, height + topInset + bottomInset);

    internal static (double Left, double Width) WithoutDocks(double left, double width, double leftDock, double rightDock)
        => (left + leftDock, width - leftDock - rightDock);

    internal static (int Width, int Height) Constrain(int width, int height, int edge,
        double ratio, int minWidth, int minHeight, int maxWidth, int maxHeight)
    {
        // Round the long side from an integer short side so the renderer's fit
        // stays within half a pixel of the surface in either orientation.
        bool landscape = ratio >= 1;
        double scale = landscape ? ratio : 1 / ratio;
        double minimum = landscape ? Math.Max(minHeight, Math.Ceiling(minWidth / scale))
            : Math.Max(minWidth, Math.Ceiling(minHeight / scale));
        double maximum = landscape ? Math.Min(maxHeight, Math.Floor(maxWidth / scale))
            : Math.Min(maxWidth, Math.Floor(maxHeight / scale));
        // Minimum size wins if a small monitor cannot accommodate both constraints.
        maximum = Math.Max(minimum, maximum);
        double wantedHeight = edge switch
        {
            1 or 2 => width / ratio,
            3 or 6 => height,
            _ => (ratio * width + height) / (ratio * ratio + 1)
        };
        double wantedShortSide = landscape ? wantedHeight : wantedHeight * ratio;
        int shortSide = (int)Math.Clamp(Math.Round(wantedShortSide), minimum, maximum);
        int longSide = (int)Math.Round(shortSide * scale);
        return landscape ? (longSide, shortSide) : (shortSide, longSide);
    }

    internal static (int Left, int Top) Place(int left, int top, int right, int bottom,
        int width, int height, int workLeft, int workTop, int workRight, int workBottom)
    {
        int x = (double)left + right > (double)workLeft + workRight ? right - width : left;
        int y = (double)top + bottom > (double)workTop + workBottom ? bottom - height : top;
        return (Math.Clamp(x, workLeft, Math.Max(workLeft, workRight - width)),
            Math.Clamp(y, workTop, Math.Max(workTop, workBottom - height)));
    }
}
