using System;
using System.Collections.Generic;
using System.IO;
using FlyleafLib.MediaFramework.MediaInput;

namespace FlyleafLib.Plugins
{
    public class OpenVideo : PluginBase, IOpen, IProvideVideo, ISuggestVideoInput
    {
        public bool             IsPlaylist  => true;
        public new int          Priority    { get; set; } = 3000;
        public List<VideoInput> VideoInputs { get; set; } = new List<VideoInput>();

        public VideoInput curSuggestInput; // Pointer to the latest opened input

        public bool IsValidInput(string url)
        {
            return true;
        }

        public OpenResults Open(string url)
        {
            foreach(var input in VideoInputs)
                if (input.Url.ToLower() == url.ToLower()) 
                    { curSuggestInput = input; return new OpenResults(); }

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
                Uri uri;
                try { uri = new Uri(url); inputData.Title = Path.GetFileName(uri.LocalPath); } catch (Exception) { }

                //inputData.Title = url;
                inputData.Folder = Path.GetTempPath();
            }

            videoInput.Url = url;
            videoInput.InputData = inputData;

            VideoInputs.Add(videoInput);
            curSuggestInput = videoInput;

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

            curSuggestInput = VideoInputs[VideoInputs.Count - 1];

            return new OpenResults();
        }

        public override OpenResults OnOpenVideo(VideoInput input)
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return null;

            Handler.UserInputUrl = input.IOStream != null ? "Custom IO Stream" : input.Url;
            curSuggestInput = input;

            return new OpenResults();
        }

        public VideoInput SuggestVideo()
        {
            if (Handler.OpenedPlugin == null || Handler.OpenedPlugin.Name != Name) return null;

            return curSuggestInput;
        }
    }
}