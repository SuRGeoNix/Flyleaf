using System.Windows.Input;

using FlyleafLib.Controls.WPF;

namespace FlyleafLib.MediaPlayer
{
    public class Commands
    {
        public ICommand AudioDelaySet           { get; set; }
        public ICommand AudioDelaySet2          { get; set; }
        public ICommand AudioDelayAdd           { get; set; }
        public ICommand AudioDelayAdd2          { get; set; }
        public ICommand AudioDelayRemove        { get; set; }
        public ICommand AudioDelayRemove2       { get; set; }

        public ICommand SubtitlesDelaySet       { get; set; }
        public ICommand SubtitlesDelaySet2      { get; set; }
        public ICommand SubtitlesDelayAdd       { get; set; }
        public ICommand SubtitlesDelayAdd2      { get; set; }
        public ICommand SubtitlesDelayRemove    { get; set; }
        public ICommand SubtitlesDelayRemove2   { get; set; }

        public ICommand Open                    { get; set; }
        public ICommand OpenFromClipboard       { get; set; }
        public ICommand OpenFromFileDialog      { get; set; }

        public ICommand Play                    { get; set; }
        public ICommand Pause                   { get; set; }
        public ICommand Stop                    { get; set; }
        public ICommand TogglePlayPause         { get; set; }

        public ICommand SeekBackward            { get; set; }
        public ICommand SeekBackward2           { get; set; }
        public ICommand SeekForward             { get; set; }
        public ICommand SeekForward2            { get; set; }
        public ICommand SeekToChapter           { get; set; }

        public ICommand ShowFramePrev           { get; set; }
        public ICommand ShowFrameNext           { get; set; }

        public ICommand NormalScreen            { get; set; }
        public ICommand FullScreen              { get; set; }
        public ICommand ToggleFullScreen        { get; set; }

        public ICommand ToggleReversePlayback   { get; set; }
        public ICommand StartRecording          { get; set; }
        public ICommand StopRecording           { get; set; }
        public ICommand ToggleRecording         { get; set; }

        public ICommand TakeSnapshot            { get; set; }
        public ICommand ZoomIn                  { get; set; }
        public ICommand ZoomOut                 { get; set; }
        public ICommand ResetAll                { get; set; }

        public ICommand SpeedSet                { get; set; }
        public ICommand SpeedUp                 { get; set; }
        public ICommand SpeedDown               { get; set; }

        public ICommand VolumeUp                { get; set; }
        public ICommand VolumeDown              { get; set; }
        public ICommand ToggleMute              { get; set; }

        Player player;

        public Commands(Player player)
        {
            this.player = player;

            Open                    = new RelayCommand(OpenAction);
            OpenFromClipboard       = new RelayCommandSimple(player.OpenFromClipboard);
            OpenFromFileDialog      = new RelayCommandSimple(player.OpenFromFileDialog);

            Play                    = new RelayCommandSimple(player.Play);
            Pause                   = new RelayCommandSimple(player.Pause);
            TogglePlayPause         = new RelayCommandSimple(player.TogglePlayPause);
            Stop                    = new RelayCommandSimple(player.Stop);

            SeekBackward            = new RelayCommandSimple(player.SeekBackward);
            SeekBackward2           = new RelayCommandSimple(player.SeekBackward2);
            SeekForward             = new RelayCommandSimple(player.SeekForward);
            SeekForward2            = new RelayCommandSimple(player.SeekForward2);
            SeekToChapter           = new RelayCommand(SeekToChapterAction);

            ShowFrameNext           = new RelayCommandSimple(player.ShowFrameNext);
            ShowFramePrev           = new RelayCommandSimple(player.ShowFramePrev);

            NormalScreen            = new RelayCommandSimple(player.NormalScreen);
            FullScreen              = new RelayCommandSimple(player.FullScreen);
            ToggleFullScreen        = new RelayCommandSimple(player.ToggleFullScreen);

            ToggleReversePlayback   = new RelayCommandSimple(player.ToggleReversePlayback);
            StartRecording          = new RelayCommandSimple(player.StartRecording);
            StopRecording           = new RelayCommandSimple(player.StopRecording);
            ToggleRecording         = new RelayCommandSimple(player.ToggleRecording);

            TakeSnapshot            = new RelayCommandSimple(TakeSnapshotAction);
            ZoomIn                  = new RelayCommandSimple(player.ZoomIn);
            ZoomOut                 = new RelayCommandSimple(player.ZoomOut);
            ResetAll                = new RelayCommandSimple(player.ResetAll);

            SpeedSet                = new RelayCommand(SpeedSetAction);
            SpeedUp                 = new RelayCommandSimple(player.SpeedUp);
            SpeedDown               = new RelayCommandSimple(player.SpeedDown);

            VolumeUp                = new RelayCommandSimple(player.Audio.VolumeUp);
            VolumeDown              = new RelayCommandSimple(player.Audio.VolumeDown);
            ToggleMute              = new RelayCommandSimple(player.Audio.ToggleMute);

            AudioDelaySet           = new RelayCommand(AudioDelaySetAction);
            AudioDelaySet2          = new RelayCommand(AudioDelaySetAction2);
            AudioDelayAdd           = new RelayCommandSimple(player.Audio.DelayAdd);
            AudioDelayAdd2          = new RelayCommandSimple(player.Audio.DelayAdd2);
            AudioDelayRemove        = new RelayCommandSimple(player.Audio.DelayRemove);
            AudioDelayRemove2       = new RelayCommandSimple(player.Audio.DelayRemove2);

            SubtitlesDelaySet       = new RelayCommand(SubtitlesDelaySetAction);
            SubtitlesDelaySet2      = new RelayCommand(SubtitlesDelaySetAction2);
            SubtitlesDelayAdd       = new RelayCommandSimple(player.Subtitles.DelayAdd);
            SubtitlesDelayAdd2      = new RelayCommandSimple(player.Subtitles.DelayAdd2);
            SubtitlesDelayRemove    = new RelayCommandSimple(player.Subtitles.DelayRemove);
            SubtitlesDelayRemove2   = new RelayCommandSimple(player.Subtitles.DelayRemove2);
        }

        public void SpeedSetAction(object speed)
        {
            string speedstr = speed.ToString().Replace(',', '.');
            if (double.TryParse(speedstr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double value))
                player.Speed = value;
        }

        public void AudioDelaySetAction(object delay)
        {
            player.Config.Audio.Delay = (int.Parse(delay.ToString())) * (long)10000;
        }
        public void AudioDelaySetAction2(object delay)
        {
            player.Config.Audio.Delay += (int.Parse(delay.ToString())) * (long)10000;
        }
        public void SubtitlesDelaySetAction(object delay)
        {
            player.Config.Subtitles.Delay = (int.Parse(delay.ToString())) * (long)10000;
        }
        public void SubtitlesDelaySetAction2(object delay)
        {
            player.Config.Subtitles.Delay += (int.Parse(delay.ToString())) * (long)10000;
        }

        public void TakeSnapshotAction()
        {
            player.TakeSnapshot();
        }

        public void SeekToChapterAction(object chapter)
        {
            player.SeekToChapter((MediaFramework.MediaDemuxer.Demuxer.Chapter)chapter);
        }

        public void OpenAction(object input)
        {
            if (input is MediaFramework.MediaStream.StreamBase)
                player.OpenAsync((MediaFramework.MediaStream.StreamBase)input);
            else if (input is MediaFramework.MediaInput.InputBase)
                player.OpenAsync((MediaFramework.MediaInput.InputBase)input);
        }
    }
}
