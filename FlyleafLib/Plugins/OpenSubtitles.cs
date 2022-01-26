using System;
using System.Collections.Generic;
using System.IO;

using FlyleafLib.MediaFramework.MediaInput;

namespace FlyleafLib.Plugins
{
    public class OpenSubtitles : PluginBase, IOpenSubtitles, IProvideSubtitles, ISearchSubtitles, ISuggestSubtitlesInput
    {
        public new int Priority { get; set; } = 3000;

        public List<SubtitlesInput> SubtitlesInputs => Handler.UserInput.SubtitlesInputs;

        public override void OnInitialized()
        {
            //SubtitlesInputs.Clear();
        }

        public override void OnInitializedSwitch()
        {
            //if (Handler.OpenedPlugin != null && Handler.OpenedPlugin.IsPlaylist)
                //SubtitlesInputs.Clear();
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

        public void Search(Language lang)
        {
            // TBR: Until we define input urls as local/web/torrent etc. (perform also FileInfo only once on pluginhandler)
            if (!Config.Subtitles.UseLocalSearch)
                return;

            string[] files = null;

            try
            {
                FileInfo fi = new FileInfo(Handler.UserInputUrl);
                string prefix = fi.Name.Substring(0, Math.Min(fi.Name.Length - fi.Extension.Length, 4));
                files = Directory.GetFiles(Path.Combine(fi.DirectoryName, "Subs"), $"{prefix}*{lang.IdSubLanguage}.utf8.srt");
            } catch { }
            
            if (files != null && files.Length > 0)
            {
                for (int i=0; i<Math.Min(files.Length, 4); i++)
                {
                    bool exists = false;
                    foreach(var input in SubtitlesInputs)
                        if (input.Url == files[i])
                            { exists = true; break; }
                    if (exists) continue;

                    SubtitlesInputs.Add(new SubtitlesInput()
                    {
                        Url         = files[i],
                        Converted   = true,
                        Downloaded  = true,
                        Language    = lang
                    });
                }

            }
        }

        public SubtitlesInput SuggestSubtitles(Language lang)
        {
            foreach(var input in SubtitlesInputs)
                if (input.Tag == null && input.Language == lang)
                    return input;

            return null;
        }
    }
}
