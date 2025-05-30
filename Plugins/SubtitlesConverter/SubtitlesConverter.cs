using System;
using System.Globalization;
using System.IO;
using System.Text;

using UniversalDetector;

namespace FlyleafLib.Plugins.SubtitlesConverter;

public class SubtitlesConverter : PluginBase
{
    public new int Priority { get; set; } = 2000;

    public override void OnOpenExternalSubtitles()
    {
        if (Selected.ExternalSubtitlesStream.Converted)
            return;

        string foundFrom = "";
        Encoding subsEnc = null;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            ICharsetDetector detector = new CharsetDetector();

            using (var stream = new FileStream(Selected.ExternalSubtitlesStream.Url, FileMode.Open))
            {
                int buflen = (int)Math.Min(stream.Length, 20 * 1024);
                byte[] buf = new byte[buflen];
                stream.Read(buf, 0, buflen);
                detector.Feed(buf, 0, buf.Length);
                detector.DataEnd();

                if (detector.Charset != null)
                    subsEnc = Encoding.GetEncoding(detector.Charset);

                foundFrom = $"CharsetDetector with {detector.Confidence} confidence";
            }
        }
        catch (Exception) { }

        if (subsEnc == null)
        {
            int ansi = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;

            try
            {
                subsEnc = Encoding.GetEncoding(ansi);
                foundFrom = "System Default 1";
            }
            catch (Exception) { }

            if (subsEnc == null)
            {
                try
                {
                    foreach (EncodingInfo ei in Encoding.GetEncodings())
                    {
                        Encoding e = ei.GetEncoding();
                        // 20'127: US-ASCII
                        if (e.WindowsCodePage == ansi && e.CodePage != 20127)
                        {
                            foundFrom = "System Default 2";
                            subsEnc = e;
                            break;
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        if (subsEnc == null)
        {
            Log.Info($"Could not detect input");
            subsEnc = Encoding.UTF8;
        }

        bool isTemp = Selected.ExternalSubtitlesStream.Url.StartsWith(Path.GetTempPath());

        if (subsEnc != Encoding.UTF8 || isTemp)
        {
            try
            {
                var folder = Path.Combine(Playlist.FolderBase, Selected.Folder, "Subs");
                Directory.CreateDirectory(folder);

                FileInfo fi = new FileInfo(Selected.ExternalSubtitlesStream.Url);
                var filename = Utils.FindNextAvailableFile(Path.Combine(folder, $"{fi.Name.Remove(fi.Name.Length - fi.Extension.Length)}.{Selected.ExternalSubtitlesStream.Language.IdSubLanguage}.utf8.srt"));

                var newUrl = Path.Combine(folder, filename);

                if (subsEnc != Encoding.UTF8)
                {
                    Log.Info($"Converting from {subsEnc.BodyName} | Path: {filename} | Detector: {foundFrom}");
                    Convert(Selected.ExternalSubtitlesStream.Url, newUrl, subsEnc, new UTF8Encoding(false));

                    if (isTemp)
                        File.Delete(Selected.ExternalSubtitlesStream.Url);
                }
                else
                    File.Move(Selected.ExternalSubtitlesStream.Url, newUrl);

                Selected.ExternalSubtitlesStream.Url = newUrl;
            }
            catch (Exception e) { Log.Error($"Convert failed ({e.Message})"); }
        }

        Selected.ExternalSubtitlesStream.Converted = true;
    }

    public bool Convert(string fileNameIn, string fileNameOut, Encoding input, Encoding output)
    {
        try
        {
            StreamReader sr = new StreamReader(new FileStream(fileNameIn,  FileMode.Open  ), input );
            StreamWriter sw = new StreamWriter(new FileStream(fileNameOut, FileMode.Create), output);

            sw.Write(sr.ReadToEnd());
            sw.Flush();
            sr.Close();
            sw.Close();
        }
        catch (Exception e) { Log.Error($"Convert Error: {e.Message}"); return false; }

        return true;
    }
}
