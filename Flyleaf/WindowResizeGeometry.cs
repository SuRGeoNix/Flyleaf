namespace Flyleaf;

// Screen coordinates in physical pixels. Child HWND boundaries are deliberately
// irrelevant: only the outer application window can supply a resize edge.
internal static class WindowResizeGeometry
{
    internal static int HitTest(int x, int y, int left, int top, int right, int bottom, int borderX, int borderY)
    {
        if (x < left || y < top || x >= right || y >= bottom) return 0;
        bool nearLeft = x - left < borderX;
        bool nearRight = right - x <= borderX;
        bool nearTop = y - top < borderY;
        bool nearBottom = bottom - y <= borderY;

        if (nearTop) return nearLeft ? 13 : nearRight ? 14 : 12;
        if (nearBottom) return nearLeft ? 16 : nearRight ? 17 : 15;
        return nearLeft ? 10 : nearRight ? 11 : 0;
    }
}
