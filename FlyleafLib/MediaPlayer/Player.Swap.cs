using System;

using FlyleafLib.MediaFramework.MediaRenderer;

namespace FlyleafLib.MediaPlayer
{
    public partial class Player
    {
        public static void SwapPlayers(Player player1, Player player2)
        {
            player1.Log.Debug($"Swaping {player1.PlayerId} with {player2.PlayerId}");
            SwapStarted?.Invoke(null, new SwapStartedArgs(player1, player2));

            player1.UnsubscribeEvents();
            player2.UnsubscribeEvents();

            Renderer.Swap(player1.renderer, player2.renderer);
            
            var saveVideoView   = player1.VideoView;
            var saveControl     = player1._Control;

            player1.VideoView   = player2.VideoView;
            player1._Control    = player2._Control;

            player2.VideoView   = saveVideoView;
            player2._Control    = saveControl;

            player1._Control.Player = player1;
            player2._Control.Player = player2;

            if (player1.VideoView != null && player2.VideoView != null)
            {
                player1.VideoView.Player = player1;
                player2.VideoView.Player = player2;
            }

            player1.SubscribeEvents();
            player2.SubscribeEvents();

            player1.Log.Debug($"Swap finished {player1.PlayerId} with {player2.PlayerId}");
            SwapCompleted?.Invoke(null, new SwapCompletedArgs(player1, player2));
        }

        public static event EventHandler<SwapStartedArgs> SwapStarted;
        public static event EventHandler<SwapCompletedArgs> SwapCompleted;
        public class SwapStartedArgs : EventArgs
        {
            public Player   Player1 { get; }
            public Player   Player2 { get; }
            
            public SwapStartedArgs(Player player1, Player player2)
            {
                Player1 = player1;
                Player2 = player2;
            }
        }
        public class SwapCompletedArgs : EventArgs
        {
            public Player   Player1 { get; }
            public Player   Player2 { get; }
            
            public SwapCompletedArgs(Player player1, Player player2)
            {
                Player1 = player1;
                Player2 = player2;
            }
        }
    }
}
