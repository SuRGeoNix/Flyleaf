using System;

namespace FlyleafLib.Controls.WPF;

public static class DebugLogger
{
    private static readonly bool IsEnabled = true;
    private static LogHandler Log = new("[DebugLogger".PadRight(25, ' ') + "]");

    public static void Print(string message)
    {
        if (IsEnabled)
        {
            Console.WriteLine(message);
            Log.Debug(message);
        }
    }
}
