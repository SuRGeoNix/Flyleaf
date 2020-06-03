using System;
using System.Windows.Forms;

namespace PartyTime.UI_Example
{
#if WINDOWS || LINUX
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetCompatibleTextRenderingDefault(false);
            Application.EnableVisualStyles();
            using (var game = new frmDisplay())
                game.Run();
        }
    }
#endif
}