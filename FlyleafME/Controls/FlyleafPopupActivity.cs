using System.Runtime.CompilerServices;

using FlyleafLib.MediaPlayer;

namespace Flyleaf.Controls.WPF;

public static class FlyleafPopupActivity
{
    sealed class State
    {
        public int Count;
        public bool WasEnabled;
    }

    sealed class HoldScope(Activity activity, State state) : IDisposable
    {
        bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            if (--state.Count != 0)
                return;

            activity.RefreshFullActive();
            activity.IsEnabled = state.WasEnabled;
            states.Remove(activity);
        }
    }

    sealed class EmptyScope : IDisposable
    {
        public void Dispose() { }
    }

    static readonly ConditionalWeakTable<Activity, State> states = new();

    public static IDisposable Hold(Player? player)
    {
        if (player == null)
            return new EmptyScope();

        var activity = player.Activity;
        var state = states.GetValue(activity, _ => new State());
        if (state.Count++ == 0)
        {
            state.WasEnabled = activity.IsEnabled;
            activity.IsEnabled = false;
            activity.ForceFullActive();
        }

        return new HoldScope(activity, state);
    }
}
