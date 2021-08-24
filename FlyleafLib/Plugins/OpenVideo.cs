using System;
using System.Collections.Generic;
using System.IO;
using FlyleafLib.MediaFramework.MediaInput;

namespace FlyleafLib.Plugins
{
    public class OpenVideo : PluginBase, IOpen, IProvideVideo, ISuggestVideoInput
    {
        public List<VideoInput> VideoInputs { get; set; } = new List<VideoInput>();

        public bool IsPlaylist => true;

        public override void OnInitialized()
        {
            //VideoInputs.Clear();
        }

        public bool IsValidInput(string url)
        {
            return true;
        }

        public OpenResults Open(string url)
        {
            foreach(var input in VideoInputs)
                if (input.Url.ToLower() == url.ToLower()) return new OpenResults();

            VideoInput videoInput = new VideoInput();
            InputData inputData = new InputData();

            if (File.Exists(url))
            {
                var fi = new FileInfo(url);
                inputData.Title = fi.Name;
                inputData.Folder = fi.DirectoryName;
                inputData.FileSize = fi.Length;
            }
            else
            {
                inputData.Title = url;
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
                IOStream = iostream,
                InputData = new InputData()
                {
                    Title = "Custom IO Stream",
                    Folder = Path.GetTempPath(),
                    FileSize = iostream.Length
                }
            });

            return new OpenResults();
        }

        public VideoInput SuggestVideo()
        {
            if (Handler.OpenedPlugin.Name != Name) return null;

            return VideoInputs[VideoInputs.Count -1];
        }
    }
}