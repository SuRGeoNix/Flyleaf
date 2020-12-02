using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Collections.Generic;

using SharpDX;
using static SuRGeoNix.Flyleaf.OSDMessage;

namespace SuRGeoNix.Flyleaf
{
    public class Subtitles
    {
        public static string SSAtoSubStyles(string s, out List<SubStyle> styles)
        {
            int     pos     = 0;
            string  sout    = "";
            styles          = new List<SubStyle>();

            SubStyle bold       = new SubStyle(SubStyles.BOLD);
            SubStyle italic     = new SubStyle(SubStyles.ITALIC);
            SubStyle underline  = new SubStyle(SubStyles.UNDERLINE);
            SubStyle strikeout  = new SubStyle(SubStyles.STRIKEOUT);
            SubStyle color      = new SubStyle(SubStyles.COLOR);

            //SubStyle fontname      = new SubStyle(SubStyles.FONTNAME);
            //SubStyle fontsize      = new SubStyle(SubStyles.FONTSIZE);

            s = s.LastIndexOf(",,") == -1 ? s : s.Substring(s.LastIndexOf(",,") + 2).Replace("\\N", "\n").Trim();

            for (int i=0; i<s.Length; i++)
            {
                if (s[i] == '{') continue;

                if (s[i] == '\\' && s[i-1] == '{')
                {
                    int codeLen = s.IndexOf('}', i) -i;
                    if (codeLen == -1) continue;

                    string code = s.Substring(i, codeLen).Trim();

                    switch (code[1])
                    {
                        case 'c':
                            if ( code.Length == 2 )
                            {
                                if (color.from == -1) break;

                                color.len = pos - color.from;
                                if ((Color) color.value != Color.Transparent) styles.Add(color);
                                color = new SubStyle(SubStyles.COLOR);                                
                            }
                            else
                            {
                                color.from = pos;
                                color.value = Color.Transparent;
                                if (code.Length < 7) break;

                                int colorEnd = code.LastIndexOf("&");
                                if (colorEnd < 6) break;

                                string hexColor = code.Substring(4, colorEnd - 4);
                                int red = int.Parse(hexColor.Substring(hexColor.Length-2, 2), System.Globalization.NumberStyles.HexNumber);
                                int green = 0;
                                int blue = 0;

                                if (hexColor.Length-2 > 0)
                                {
                                    hexColor = hexColor.Substring(0, hexColor.Length-2);
                                    green = int.Parse(hexColor.Substring(hexColor.Length-2, 2), System.Globalization.NumberStyles.HexNumber);
                                }
                                if (hexColor.Length-2 > 0)
                                {
                                    hexColor = hexColor.Substring(0, hexColor.Length-2);
                                    blue = int.Parse(hexColor.Substring(hexColor.Length-2, 2), System.Globalization.NumberStyles.HexNumber);
                                }

                                color.value = new Color(red, green, blue);
                            }
                            break;

                        case 'b':
                            if ( code[2] == '0' )
                            {
                                if (bold.from == -1) break;

                                bold.len = pos - bold.from;
                                styles.Add(bold);
                                bold = new SubStyle(SubStyles.BOLD);
                            }
                            else
                            {
                                bold.from = pos;
                                //bold.value = code.Substring(2, code.Length-2);
                            }

                            break;

                        case 'u':
                            if ( code[2] == '0' )
                            {
                                if (underline.from == -1) break;

                                underline.len = pos - underline.from;
                                styles.Add(underline);
                                underline = new SubStyle(SubStyles.UNDERLINE);
                            }
                            else
                            {
                                underline.from = pos;
                            }
                            
                            break;

                        case 's':
                            if ( code[2] == '0' )
                            {
                                if (strikeout.from == -1) break;

                                strikeout.len = pos - strikeout.from;
                                styles.Add(strikeout);
                                strikeout = new SubStyle(SubStyles.STRIKEOUT);
                            }
                            else
                            {
                                strikeout.from = pos;
                            }
                            
                            break;

                        case 'i':
                            if ( code[2] == '0' )
                            {
                                if (italic.from == -1) break;

                                italic.len = pos - italic.from;
                                styles.Add(italic);
                                italic = new SubStyle(SubStyles.ITALIC);
                            }
                            else
                            {
                                italic.from = pos;
                            }
                            
                            break;
                    }

                    i += codeLen;
                    continue;
                }

                sout += s[i];
                pos ++;
            }

            // Non-Closing Codes
            int soutPostLast = sout.Length;
            if (bold.from != -1) { bold.len = soutPostLast - bold.from; styles.Add(bold); }
            if (italic.from != -1) { italic.len = soutPostLast - italic.from; styles.Add(italic); }
            if (strikeout.from != -1) { strikeout.len = soutPostLast - strikeout.from; styles.Add(strikeout); }
            if (underline.from != -1) { underline.len = soutPostLast - underline.from; styles.Add(underline); }
            if (color.from != -1 && (Color) color.value != Color.Transparent) { color.len = soutPostLast - color.from; styles.Add(color); }

            return sout;
        }
        public static bool Convert(string fileNameIn, string fileNameOut, Encoding input, Encoding output)
        {
            try
            {
                StreamReader sr = new StreamReader(new FileStream(fileNameIn, FileMode.Open), input);
                StreamWriter sw = new StreamWriter(new FileStream(fileNameOut, FileMode.Create), output);

                sw.Write(sr.ReadToEnd());
                sw.Flush();
                sr.Close();
                sw.Close();

            } catch (Exception e) { Console.WriteLine($"[Subs Convert] {e.Message}"); return false; }

            return true;
        }
        public static Encoding Detect(string fileName)
        {
            Encoding ret = Encoding.Default;

            // Check if BOM
            if ((ret = CheckByBOM(fileName)) != Encoding.Default) return ret;

            // Check if UTF-8
            using (BufferedStream fstream = new BufferedStream(File.OpenRead(fileName)))
            {
                if (IsUtf8(fstream)) ret = Encoding.UTF8;
            }

            return ret;
        }
        public static Encoding CheckByBOM(string path)
        {
            if (path == null) throw new ArgumentNullException("path");

            var encodings = Encoding.GetEncodings()
                .Select(e => e.GetEncoding())
                .Select(e => new { Encoding = e, Preamble = e.GetPreamble() })
                .Where(e => e.Preamble.Any())
                .ToArray();

            var maxPrembleLength = encodings.Max(e => e.Preamble.Length);
            byte[] buffer = new byte[maxPrembleLength];

            using (var stream = File.OpenRead(path))
            {
                stream.Read(buffer, 0, (int)Math.Min(maxPrembleLength, stream.Length));
            }

            return encodings
                .Where(enc => enc.Preamble.SequenceEqual(buffer.Take(enc.Preamble.Length)))
                .Select(enc => enc.Encoding)
                .FirstOrDefault() ?? Encoding.Default;
        }
        public static bool IsUtf8(Stream stream)
        {
            int count = 4 * 1024;
            byte[] buffer;
            int read;
            while (true)
            {
                buffer = new byte[count];
                stream.Seek(0, SeekOrigin.Begin);
                read = stream.Read(buffer, 0, count);
                if (read < count)
                {
                    break;
                }
                buffer = null;
                count *= 2;
            }
            return IsUtf8(buffer, read);
        }
        public static bool IsUtf8(byte[] buffer, int length)
        {
            int position = 0;
            int bytes = 0;
            while (position < length)
            {
                if (!IsValid(buffer, position, length, ref bytes))
                {
                    return false;
                }
                position += bytes;
            }
            return true;
        }
        public static bool IsValid(byte[] buffer, int position, int length, ref int bytes)
        {
            if (length > buffer.Length)
            {
                throw new ArgumentException("Invalid length");
            }

            if (position > length - 1)
            {
                bytes = 0;
                return true;
            }

            byte ch = buffer[position];

            if (ch <= 0x7F)
            {
                bytes = 1;
                return true;
            }

            if (ch >= 0xc2 && ch <= 0xdf)
            {
                if (position >= length - 2)
                {
                    bytes = 0;
                    return false;
                }
                if (buffer[position + 1] < 0x80 || buffer[position + 1] > 0xbf)
                {
                    bytes = 0;
                    return false;
                }
                bytes = 2;
                return true;
            }

            if (ch == 0xe0)
            {
                if (position >= length - 3)
                {
                    bytes = 0;
                    return false;
                }

                if (buffer[position + 1] < 0xa0 || buffer[position + 1] > 0xbf ||
                    buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf)
                {
                    bytes = 0;
                    return false;
                }
                bytes = 3;
                return true;
            }


            if (ch >= 0xe1 && ch <= 0xef)
            {
                if (position >= length - 3)
                {
                    bytes = 0;
                    return false;
                }

                if (buffer[position + 1] < 0x80 || buffer[position + 1] > 0xbf ||
                    buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf)
                {
                    bytes = 0;
                    return false;
                }

                bytes = 3;
                return true;
            }

            if (ch == 0xf0)
            {
                if (position >= length - 4)
                {
                    bytes = 0;
                    return false;
                }

                if (buffer[position + 1] < 0x90 || buffer[position + 1] > 0xbf ||
                    buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf ||
                    buffer[position + 3] < 0x80 || buffer[position + 3] > 0xbf)
                {
                    bytes = 0;
                    return false;
                }

                bytes = 4;
                return true;
            }

            if (ch == 0xf4)
            {
                if (position >= length - 4)
                {
                    bytes = 0;
                    return false;
                }

                if (buffer[position + 1] < 0x80 || buffer[position + 1] > 0x8f ||
                    buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf ||
                    buffer[position + 3] < 0x80 || buffer[position + 3] > 0xbf)
                {
                    bytes = 0;
                    return false;
                }

                bytes = 4;
                return true;
            }

            if (ch >= 0xf1 && ch <= 0xf3)
            {
                if (position >= length - 4)
                {
                    bytes = 0;
                    return false;
                }

                if (buffer[position + 1] < 0x80 || buffer[position + 1] > 0xbf ||
                    buffer[position + 2] < 0x80 || buffer[position + 2] > 0xbf ||
                    buffer[position + 3] < 0x80 || buffer[position + 3] > 0xbf)
                {
                    bytes = 0;
                    return false;
                }

                bytes = 4;
                return true;
            }

            return false;
        }
    }
}
