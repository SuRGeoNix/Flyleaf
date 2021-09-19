using System;
using System.Globalization;
using System.IO;
using System.Text;

using UniversalDetector;

using FlyleafLib.MediaFramework.MediaInput;

namespace FlyleafLib.Plugins.SubtitlesConverter
{
    public class SubtitlesConverter : PluginBase
    {
        public new int Priority { get; set; } = 2000;

        public override OpenResults OnOpenSubtitles(SubtitlesInput input)
        {
            if (input.Converted) return null;

            string foundFrom = "";
            Encoding subsEnc = null;
#if !NETFRAMEWORK
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            try
            {
                ICharsetDetector detector = new CharsetDetector();

                using (var stream = new FileStream(input.Url, FileMode.Open))
                {
                    int buflen = (int)Math.Min(stream.Length, 1024);
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
#if NETFRAMEWORK
                subsEnc = Encoding.Default;
#else
                int ansi = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;

                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
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
#endif
            }

            if (subsEnc == null)
            {
                Log($"Could not detect input");
                subsEnc = Encoding.UTF8;
            }

            if (subsEnc != Encoding.UTF8)
            {
                Log($"Converting from {subsEnc} | Folder: {Handler.VideoInput.InputData.Folder} | Detector: {foundFrom}");

                try
                {
                    FileInfo fi = new FileInfo(input.Url);
                    var newUrl = Path.Combine(Handler.VideoInput.InputData.Folder, "Subs", fi.Name.Remove(fi.Name.Length - fi.Extension.Length) + ".utf8.srt");
                    Directory.CreateDirectory(Path.Combine(Handler.VideoInput.InputData.Folder, "Subs"));
                    Convert(input.Url, newUrl, subsEnc, new UTF8Encoding(false));
                    input.Url = newUrl;
                }
                catch (Exception e) { Log($"Convert Error: {e.Message}"); }
            }

            input.Converted = true;

            return null;
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
            catch (Exception e) { Log($"Convert Error: {e.Message}"); return false; }

            return true;
        }
    }
}