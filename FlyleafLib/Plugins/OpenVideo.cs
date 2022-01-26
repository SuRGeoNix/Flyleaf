using System;
using System.Collections.Generic;
using System.IO;

using FlyleafLib.MediaFramework.MediaInput;

namespace FlyleafLib.Plugins
{
    public class OpenVideo : PluginBase, IOpen, IProvideVideo, ISuggestVideoInput
    {
        /* TODO
         * Should be replace the history by a new plugin with different a new IProvideUserInputs for recent/history
         * Currently audio inputs come as video inputs and can cause issues, should review decoder context and provide also AudioInputs
         */

        public bool             IsPlaylist  => true;
        public new int          Priority    { get; set; } = 3000;
        public List<VideoInput> VideoInputs => Handler.UserInput.VideoInputs;

        public override void OnInitializing()
        {
            //VideoInputs.Clear();
        }

        public bool IsValidInput(string url)
        {
            return true;
        }

        public OpenResults Open(string url)
        {
            if (VideoInputs.Count > 0 && VideoInputs[0].Url != null && VideoInputs[0].Url.ToLower() == url.ToLower())
                return new OpenResults();

            VideoInput videoInput = new VideoInput();
            InputData  inputData  = new InputData();

            if (File.Exists(url))
            {
                var fi = new FileInfo(url);
                inputData.Title     = fi.Name;
                inputData.Folder    = fi.DirectoryName;
                inputData.FileSize  = fi.Length;
            }
            else
            {
                try { Uri uri = new Uri(url); inputData.Title = Path.GetFileName(uri.LocalPath); } catch { }

                inputData.Folder = Path.GetTempPath();
            }

            videoInput.Url = url;
            videoInput.InputData = inputData;

            VideoInputs.Add(videoInput);

            return new OpenResults();
        }

        public OpenResults Open(Stream iostream)
        {
            VideoInputs.Add(new VideoInput()
            {
                IOStream  = iostream,
                InputData = new InputData()
                {
                    Title   = "Custom IO Stream",
                    Folder  = Path.GetTempPath(),
                    FileSize= iostream.Length
                }
            });

            return new OpenResults();
        }

        public override OpenResults OnOpenVideo(VideoInput input)
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name)
                return null;

            //Handler.UserInputUrl = input.IOStream != null ? "Custom IO Stream" : input.Url;

            return new OpenResults();
        }

        public VideoInput SuggestVideo()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name || VideoInputs.Count == 0)
                return null;

            return VideoInputs[0];
        }
    }
}