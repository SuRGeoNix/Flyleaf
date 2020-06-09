using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows.Forms;
using System.Drawing;

namespace PartyTime
{
    public class Subtitles
    {
        public static void ASSToRichText(RichTextBox rtb, string text)
        {
            rtb.Text = "";

            bool bold = false;
            bool italic = false;
            bool changed = false;
            string cur = "";
            FontStyle fontStyle = FontStyle.Regular;

            for (int i=0; i<text.Length; i++)
            {

                if ( changed )
                {
                    if ( !bold && !italic ) fontStyle = FontStyle.Regular;
                    else
                    if (  bold &&  italic ) fontStyle = FontStyle.Bold | FontStyle.Italic;
                    else
                    if (  bold && !italic ) fontStyle = FontStyle.Bold;
                    else
                    if ( !bold &&  italic ) 
                        fontStyle = FontStyle.Italic;

                    rtb.AppendText(cur);

                    cur = "";
                    changed = false;
                    rtb.SelectionFont = new Font(rtb.Font, fontStyle); 
                }

                if ( text.Length > i + 4 && text[i] == '{' &&  text[i+1] == '\\')
                {
                    if      ( text[i+2] == 'i' && (text[i+3] == '0' || text[i+3] == '1') && text[i+4] == '}' )
                    {
                        italic = text[i+3] == '1' ? true : false;
                        changed = true;
                        i += 4;
                    } 
                    else if ( text[i+2] == 'b' && (text[i+3] == '0' || text[i+3] == '1') && text[i+4] == '}' )
                    {
                        bold = text[i+3] == '1' ? true : false;
                        changed = true;
                        i += 4;
                    }
                }

                if ( !changed ) cur += text[i];

            }
            
            rtb.AppendText(cur);
            rtb.SelectAll();
            rtb.SelectionAlignment = HorizontalAlignment.Center;
        }

        public static string Convert(string fileName, Encoding input, Encoding output)
        {
            string tmpFile = null;
            try
            {
                tmpFile = Path.GetTempPath() + Guid.NewGuid().ToString() + ".srt";
                StreamReader sr = new StreamReader(new FileStream(fileName, FileMode.Open), input);
                StreamWriter sw = new StreamWriter(new FileStream(tmpFile, FileMode.CreateNew), output);

                sw.Write(sr.ReadToEnd());
                sw.Flush();
                sr.Close();
                sw.Close();
            } catch (Exception) { return null; }

            return tmpFile;
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