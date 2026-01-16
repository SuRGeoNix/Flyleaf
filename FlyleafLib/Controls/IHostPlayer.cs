namespace FlyleafLib.Controls;

public interface IHostPlayer
{
    bool Player_CanHideCursor();
    bool Player_GetFullScreen();
    void Player_SetFullScreen(bool value);
    void Player_RatioChanged(double keepRatio);
    bool Player_HandlesRatioResize(int width, int height);
    void Player_Disposed();
}
