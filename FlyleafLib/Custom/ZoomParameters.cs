using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlyleafLib.Custom;

public struct ZoomParameters
{
    public ZoomParameters(double initZoom, double maxZoom)
    {
        InitialZoom = initZoom;
        MaximalZoom = maxZoom;
    }
    public double InitialZoom { get; set; } = 1.0;
    public double MaximalZoom { get; set; } = 50.0;
    public double ValidateZoom(double zoom)
    {
        if (zoom < InitialZoom && InitialZoom >= 0)
            zoom = InitialZoom;
        if (zoom > MaximalZoom && MaximalZoom >= 0)
            zoom = MaximalZoom;
        return zoom;
    }
}
