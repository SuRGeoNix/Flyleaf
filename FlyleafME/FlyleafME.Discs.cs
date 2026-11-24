using System.Windows;
using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

public partial class FlyleafME
{
    bool CanOpenDisc => CanChangeQueue && Engine.IsLoaded && Player is { IsDisposed: false };

    void OpenDisc(bool append, bool directInput = false)
    {
        if (!CanOpenDisc) return;
        var target = Player;
        try
        {
            string[] inputs;
            using (FlyleafPopupActivity.Hold(target))
            {
                var dialog = new FlyleafDiscDialog(append, Resources) { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() != true || !CanOpenDisc || !ReferenceEquals(Player, target)) return;
                inputs = dialog.Inputs;
            }
            if (directInput && !append && inputs.Length == 1) OpenInput(inputs[0]);
            else if (HasPlaylistActionHandler) RequestPlaylistAction(append ? FlyleafPlaylistAction.AddInputs : FlyleafPlaylistAction.OpenInputs, inputs);
            else if (directInput && !append && OpenFilesRequested is { } openFiles) openFiles(this, inputs);
            else ApplyQueueInputs(inputs, append);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
}
