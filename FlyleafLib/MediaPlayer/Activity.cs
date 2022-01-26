using System;

namespace FlyleafLib.MediaPlayer
{
    public class Activity : NotifyPropertyChanged
    {
        /* Player Activity Mode ( Idle / Active / FullActive )
         * 
         * Config.Player.ActivityMode
         * Config.Player.ActivityTimeout
         * 
         * ActivityRefresh          (KeyUp, Control_MouseDown, Control_MouseMove)
         * ActivityCheck            (MasterThread)
         * 
         * TODO: FlyleafWindow.MouseMove and FlyleafWindow.MouseDown ?
         */

        public ActivityMode Mode        { get => mode; internal set => Set(ref _Mode, value); }
        internal ActivityMode _Mode = ActivityMode.FullActive, mode = ActivityMode.FullActive;

        public long KeyboardTimestmap   { get; internal set; }
        public long MouseTimestamp      { get; internal set; }

        Player player;
        Config Config => player.Config;

        public Activity(Player player)
        {
            this.player = player;
        }

        public ActivityMode Check()
        {
            if (!Config.Player.ActivityMode)
                return ActivityMode.FullActive;

            if ((DateTime.UtcNow.Ticks - MouseTimestamp  ) / 10000 < Config.Player.ActivityTimeout)
                return ActivityMode.FullActive;

            if (player.isAnyKeyDown || (DateTime.UtcNow.Ticks - KeyboardTimestmap ) / 10000 < Config.Player.ActivityTimeout)
                return ActivityMode.Active;

            return ActivityMode.Idle;
        }

        public void ForceIdle()
        {
            KeyboardTimestmap = 0;
            MouseTimestamp = 0;
        }

        public void ForceActive()
        {
            KeyboardTimestmap = DateTime.UtcNow.Ticks;
        }

        public void ForceFullActive()
        {
            MouseTimestamp = DateTime.UtcNow.Ticks;
        }

    }

    public enum ActivityMode
    {
        Idle,
        Active,     // Keyboard only
        FullActive  // Mouse
    }
}
