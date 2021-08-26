using System;
using System.Windows.Forms;

namespace WinForms_Sample__Basic_
{
    static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.Run(new BasicNaked());
            //Application.Run(new TestingWPFwithinWinForms());
        }
    }
}
