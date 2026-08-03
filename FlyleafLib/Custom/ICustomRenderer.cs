namespace FlyleafLib.Custom;

public interface ICustomRenderer
{   
    event Action CustomSetSize;    
    double InitialZoom { get; }
    double MaximalZoom { get; }
    double ValidateZoom(double zoom);
}
