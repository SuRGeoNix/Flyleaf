using System.Globalization;
using System.Windows.Input;

using FlyleafLib.Controls.WPF;
using FlyleafLib.MediaFramework.MediaPlaylist;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaPlayer;

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
    public ICommand Reopen                  { get; set; }

    public ICommand Play                    { get; set; }
    public ICommand Pause                   { get; set; }
    public ICommand Stop                    { get; set; }
    public ICommand TogglePlayPause         { get; set; }

    public ICommand SeekBackward            { get; set; }
    public ICommand SeekBackward2           { get; set; }
    public ICommand SeekBackward3           { get; set; }
    public ICommand SeekForward             { get; set; }
    public ICommand SeekForward2            { get; set; }
    public ICommand SeekForward3            { get; set; }
    public ICommand SeekToChapter           { get; set; }

    public ICommand ShowFramePrev           { get; set; }
    public ICommand ShowFrameNext           { get; set; }

    public ICommand NormalScreen            { get; set; }
    public ICommand FullScreen              { get; set; }
    public ICommand ToggleFullScreen        { get; set; }

    public ICommand ToggleReversePlayback   { get; set; }
    public ICommand ToggleLoopPlayback      { get; set; }
    public ICommand StartRecording          { get; set; }
    public ICommand StopRecording           { get; set; }
    public ICommand ToggleRecording         { get; set; }

    public ICommand TakeSnapshot            { get; set; }
    public ICommand ZoomIn                  { get; set; }
    public ICommand ZoomOut                 { get; set; }
    public ICommand RotationSet             { get; set; }
    public ICommand RotateLeft              { get; set; }
    public ICommand RotateRight             { get; set; }
    public ICommand ResetAll                { get; set; }

    public ICommand SpeedSet                { get; set; }
    public ICommand SpeedUp                 { get; set; }
    public ICommand SpeedUp2                { get; set; }
    public ICommand SpeedDown               { get; set; }
    public ICommand SpeedDown2              { get; set; }

    public ICommand VolumeUp                { get; set; }
    public ICommand VolumeDown              { get; set; }
    public ICommand ToggleMute              { get; set; }

    public ICommand ForceIdle               { get; set; }
    public ICommand ForceActive             { get; set; }
    public ICommand ForceFullActive         { get; set; }
    public ICommand RefreshActive           { get; set; }
    public ICommand RefreshFullActive       { get; set; }

    public ICommand ResetFilters            { get; set; }

    Player player;

    public Commands(Player player)
    {
        this.player = player;
        var vcfg = player.Config.Video;
        var acfg = player.Config.Audio;
        var scfg = player.Config.Subtitles;

        Open                    = new RelayCommand(OpenAction);
        OpenFromClipboard       = new RelayCommandSimple(player.OpenFromClipboard);
        OpenFromFileDialog      = new RelayCommandSimple(player.OpenFromFileDialog);
        Reopen                  = new RelayCommand(ReopenAction);

        Play                    = new RelayCommandSimple(player.Play);
        Pause                   = new RelayCommandSimple(player.Pause);
        TogglePlayPause         = new RelayCommandSimple(player.TogglePlayPause);
        Stop                    = new RelayCommandSimple(player.Stop);

        SeekBackward            = new RelayCommandSimple(player.SeekBackward);
        SeekBackward2           = new RelayCommandSimple(player.SeekBackward2);
        SeekBackward3           = new RelayCommandSimple(player.SeekBackward3);
        SeekForward             = new RelayCommandSimple(player.SeekForward);
        SeekForward2            = new RelayCommandSimple(player.SeekForward2);
        SeekForward3            = new RelayCommandSimple(player.SeekForward3);
        SeekToChapter           = new RelayCommand(SeekToChapterAction);

        ShowFrameNext           = new RelayCommandSimple(player.ShowFrameNext);
        ShowFramePrev           = new RelayCommandSimple(player.ShowFramePrev);

        NormalScreen            = new RelayCommandSimple(player.NormalScreen);
        FullScreen              = new RelayCommandSimple(player.FullScreen);
        ToggleFullScreen        = new RelayCommandSimple(player.ToggleFullScreen);

        ToggleReversePlayback   = new RelayCommandSimple(player.ToggleReversePlayback);
        ToggleLoopPlayback      = new RelayCommandSimple(player.ToggleLoopPlayback);
        StartRecording          = new RelayCommandSimple(player.StartRecording);
        StopRecording           = new RelayCommandSimple(player.StopRecording);
        ToggleRecording         = new RelayCommandSimple(player.ToggleRecording);

        TakeSnapshot            = new RelayCommandSimple(TakeSnapshotAction);
        ZoomIn                  = new RelayCommandSimple(vcfg.ZoomIn);
        ZoomOut                 = new RelayCommandSimple(vcfg.ZoomOut);
        RotationSet             = new RelayCommand(RotationSetAction);
        RotateLeft              = new RelayCommandSimple(vcfg.RotateLeft);
        RotateRight             = new RelayCommandSimple(vcfg.RotateRight);
        ResetAll                = new RelayCommandSimple(player.ResetAll);

        SpeedSet                = new RelayCommand(SpeedSetAction);
        SpeedUp                 = new RelayCommandSimple(player.SpeedUp);
        SpeedDown               = new RelayCommandSimple(player.SpeedDown);
        SpeedUp2                = new RelayCommandSimple(player.SpeedUp2);
        SpeedDown2              = new RelayCommandSimple(player.SpeedDown2);

        VolumeUp                = new RelayCommandSimple(acfg.VolumeUp);
        VolumeDown              = new RelayCommandSimple(acfg.VolumeDown);
        ToggleMute              = new RelayCommandSimple(acfg.ToggleMute);

        AudioDelaySet           = new RelayCommand(AudioDelaySetAction);
        AudioDelaySet2          = new RelayCommand(AudioDelaySetAction2);
        AudioDelayAdd           = new RelayCommandSimple(acfg.DelayAdd);
        AudioDelayAdd2          = new RelayCommandSimple(acfg.DelayAdd2);
        AudioDelayRemove        = new RelayCommandSimple(acfg.DelayRemove);
        AudioDelayRemove2       = new RelayCommandSimple(acfg.DelayRemove2);

        SubtitlesDelaySet       = new RelayCommand(SubtitlesDelaySetAction);
        SubtitlesDelaySet2      = new RelayCommand(SubtitlesDelaySetAction2);
        SubtitlesDelayAdd       = new RelayCommandSimple(scfg.DelayAdd);
        SubtitlesDelayAdd2      = new RelayCommandSimple(scfg.DelayAdd2);
        SubtitlesDelayRemove    = new RelayCommandSimple(scfg.DelayRemove);
        SubtitlesDelayRemove2   = new RelayCommandSimple(scfg.DelayRemove2);

        ForceIdle               = new RelayCommandSimple(player.Activity.ForceIdle);
        ForceActive             = new RelayCommandSimple(player.Activity.ForceActive);
        ForceFullActive         = new RelayCommandSimple(player.Activity.ForceFullActive);
        RefreshActive           = new RelayCommandSimple(player.Activity.RefreshActive);
        RefreshFullActive       = new RelayCommandSimple(player.Activity.RefreshFullActive);

        ResetFilters            = new RelayCommand(ResetFiltersAction);
    }

    private void RotationSetAction(object obj)
        => player.Config.Video.Rotation = uint.Parse(obj.ToString());

    private void ResetFiltersAction(object filter)
    {
        var cfg = player.Config.Video;
        var vp = cfg.VideoProcessor == VideoProcessors.Auto ? player.Renderer.VideoProcessor : cfg.VideoProcessor;

        if (vp == VideoProcessors.D3D11)
            foreach (var kv in cfg.D3Filters)
                kv.Value.Value = kv.Value.Default;
        else
            foreach (var kv in cfg.FLFilters)
                kv.Value.Value = kv.Value.Default; 

        cfg.SDRDisplayNitsCustom   = 0;
        cfg.HDRtoSDRMethod         = HDRtoSDRMethod.Hable;
    }

    public void SpeedSetAction(object speed)
    {
        string speedstr = speed.ToString().Replace(',', '.');
        if (double.TryParse(speedstr, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            player.Speed = value;
    }

    public void AudioDelaySetAction     (object delay)
        => player.Config.Audio.Delay        = int.Parse(delay.ToString()) * (long)10000;
    public void AudioDelaySetAction2    (object delay)
        => player.Config.Audio.Delay       += int.Parse(delay.ToString()) * (long)10000;
    public void SubtitlesDelaySetAction (object delay)
        => player.Config.Subtitles.Delay    = int.Parse(delay.ToString()) * (long)10000;
    public void SubtitlesDelaySetAction2(object delay)
        => player.Config.Subtitles.Delay   += int.Parse(delay.ToString()) * (long)10000;

    public void TakeSnapshotAction()
        => Task.Run(() => { try { player.TakeSnapshotToFile(); } catch { } });

    public void SeekToChapterAction(object chapter)
    {
        if (player.Chapters == null || player.Chapters.Count == 0)
            return;

        if (chapter is MediaFramework.MediaDemuxer.Demuxer.Chapter)
            player.SeekToChapter((MediaFramework.MediaDemuxer.Demuxer.Chapter)chapter);
        else if (int.TryParse(chapter.ToString(), out int chapterId) && chapterId < player.Chapters.Count)
            player.SeekToChapter(player.Chapters[chapterId]);
    }

    public void OpenAction(object input)
    {
        if (input == null)
            return;

        if (input is StreamBase streamBase)
            player.OpenAsync(streamBase);
        else if (input is PlaylistItem playlistItem)
            player.OpenAsync(playlistItem);
        else if (input is ExternalStream extStream)
            player.OpenAsync(extStream);
        else if (input is Stream stream)
            player.OpenAsync(stream);
        else
            player.OpenAsync(input.ToString());
    }

    public void ReopenAction(object playlistItem)
    {
        if (playlistItem == null)
            return;

        PlaylistItem item = (PlaylistItem)playlistItem;
        if (item.OpenedCounter > 0)
        {
            var session = player.GetSession(item);
            session.isReopen = true;
            session.CurTime = 0;

            // TBR: in case of disabled audio/video/subs it will save the session with them to be disabled

            // TBR: This can cause issues and it might not useful either
            //if (session.CurTime < 60 * (long)1000 * 10000)
            //    session.CurTime = 0;

            player.OpenAsync(session);
        }
        else
            player.OpenAsync(item);
    }
}
