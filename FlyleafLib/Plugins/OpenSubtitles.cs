using System.Collections.Generic;
using System.IO;

using FlyleafLib.MediaFramework.MediaInput;

namespace FlyleafLib.Plugins
{
    public class OpenSubtitles : PluginBase, IOpenSubtitles, IProvideSubtitles
    {
        public new int Priority { get; set; } = 3000;

        public List<SubtitlesInput> SubtitlesInputs { get; set; } = new List<SubtitlesInput>();

        public override void OnInitialized()
        {
            SubtitlesInputs.Clear();
        }

        public override void OnInitializedSwitch()
        {
            if (Handler.OpenedPlugin != null && Handler.OpenedPlugin.IsPlaylist)
                SubtitlesInputs.Clear();
        }

        public OpenResults Open(string url)
        {
            foreach(var input in SubtitlesInputs)
                if (input.Tag != null && input.Tag.ToString().ToLower() == url.ToLower()) return new OpenResults();

            SubtitlesInputs.Add(new SubtitlesInput() { Url = url, Downloaded = true, InputData = new InputData() { Title = url }, Tag = url });
            return new OpenResults();
        }

        public OpenResults Open(Stream iostream)
        {
            return null;
        }
    }
}
