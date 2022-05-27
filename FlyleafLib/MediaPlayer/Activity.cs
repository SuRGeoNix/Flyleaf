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

        public ActivityMode Mode
            {
            get => mode;
            set {

                if (value == mode)
                    return;

                mode = value;

                if (value == ActivityMode.Idle)
                {
                    KeyboardTimestamp = 0;
                    MouseTimestamp = 0;
                }
                else if (value == ActivityMode.Active)
                    KeyboardTimestamp = DateTime.UtcNow.Ticks;
                else
                    MouseTimestamp = DateTime.UtcNow.Ticks;

                Utils.UI(() => SetMode());
                }
            }
        internal ActivityMode _Mode = ActivityMode.FullActive, mode = ActivityMode.FullActive;

        public long KeyboardTimestamp   { get; internal set; }
        public long MouseTimestamp      { get; internal set; }

        Player player;
        Config Config => player.Config;
        static bool     isCursorHidden;

        public Activity(Player player)
        {
            this.player = player;
            KeyboardTimestamp = MouseTimestamp = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Updates Mode UI value and shows/hides mouse cursor if required
        /// Must be called from a UI Thread
        /// </summary>
        internal void SetMode()
        {
            _Mode = mode;
            Raise(nameof(Mode));
            player.Log.Debug(mode.ToString());

            if (player.IsFullScreen && player.Config.Player.MouseBindings.HideCursorOnFullScreenIdle && player.Activity.Mode == ActivityMode.Idle)
            {
                while (Utils.NativeMethods.ShowCursor(false) >= 0) { }
                isCursorHidden = true;
            }    
            else if (isCursorHidden && player.Activity.Mode == ActivityMode.FullActive)
            {
                while (Utils.NativeMethods.ShowCursor(true) < 0) { }
                isCursorHidden = false;
            }
        }

        /// <summary>
        /// Refreshes mode value based on current timestamps
        /// </summary>
        internal void RefreshMode()
        {
            if (!Config.Player.ActivityMode)
                mode = ActivityMode.FullActive;

            else if ((DateTime.UtcNow.Ticks - MouseTimestamp  ) / 10000 < Config.Player.ActivityTimeout)
                mode = ActivityMode.FullActive;

            else if (player.isAnyKeyDown || (DateTime.UtcNow.Ticks - KeyboardTimestamp ) / 10000 < Config.Player.ActivityTimeout)
                mode = ActivityMode.Active;

            else 
                mode = ActivityMode.Idle;
        }

        /// <summary>
        /// Sets Mode to Idle
        /// </summary>
        public void ForceIdle()
        {
            Mode = ActivityMode.Idle;
        }
        /// <summary>
        /// Sets Mode to Active
        /// </summary>
        public void ForceActive()
        {
            Mode = ActivityMode.Active;
        }
        /// <summary>
        /// Sets Mode to Full Active
        /// </summary>
        public void ForceFullActive()
        {
            Mode = ActivityMode.FullActive;
        }

        /// <summary>
        /// Updates Active Timestamp
        /// </summary>
        public void RefreshActive()
        {
            KeyboardTimestamp = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Updates Full Active Timestamp
        /// </summary>
        public void RefreshFullActive()
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
