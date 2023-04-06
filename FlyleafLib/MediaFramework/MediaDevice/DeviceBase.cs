using System.Collections.Generic;

namespace FlyleafLib.MediaFramework.MediaDevice;

public class DeviceBase
{
    public string   FriendlyName            { get; }
    public string   SymbolicLink            { get; }
    public IEnumerable<DeviceStreamBase> 
                    Streams                 { get; protected set; }
    public string   Url                     { get; protected set; } // default Url

    public DeviceBase(string friendlyName, string symbolicLink)
    {
        FriendlyName = friendlyName;
        SymbolicLink = symbolicLink;

        Engine.Log.Debug($"[{(this is AudioDevice ? "Audio" : "Video")}Device] {friendlyName}");
    }

    public override string ToString() => FriendlyName;
}
