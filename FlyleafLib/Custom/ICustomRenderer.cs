namespace FlyleafLib.Custom;

public interface ICustomRenderer
{
    double InitialZoom { get; }
    double MaximalZoom { get; }
    double ValidateZoom(double zoom);
}
