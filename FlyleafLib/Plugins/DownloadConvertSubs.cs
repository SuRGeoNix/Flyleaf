using System;
using System.IO;
using System.Text;

using FlyleafLib.MediaFramework.MediaInput;
using static FlyleafLib.Utils;

namespace FlyleafLib.Plugins
{
    public class DownloadConvertSubs : PluginBase
    {
        public override OpenResults OnOpenSubtitles(SubtitlesInput input)
        {
            if (!input.Downloaded && !Handler.DownloadSubtitles(input)) return new OpenResults("Failed to download subtitles");

            input.Downloaded = true;

            if (!input.Converted)
            {
                Encoding subsEnc = SubtitleConverter.Detect(input.Url);

                if (subsEnc != Encoding.UTF8)
                {
                    Log($"SubtitlesInput converting from {subsEnc} | Folder: {Handler.VideoInput.InputData.Folder}"); 

                    FileInfo fi = new FileInfo(input.Url);
                    var newUrl = Path.Combine(Handler.VideoInput.InputData.Folder, "Subs", fi.Name.Remove(fi.Name.Length - fi.Extension.Length) + ".utf8.srt");
                    Directory.CreateDirectory(Path.Combine(Handler.VideoInput.InputData.Folder, "Subs"));
                    SubtitleConverter.Convert(input.Url, newUrl, subsEnc, new UTF8Encoding(false));
                    input.Url = newUrl;
                }

                input.Converted = true;
            }

            return null;
        }
    }
}
