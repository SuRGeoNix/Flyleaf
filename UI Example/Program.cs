using System;

namespace PartyTime.UI_Example
{
#if WINDOWS || LINUX
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            using (var game = new frmDisplay())
                game.Run();
        }
    }
#endif
}
