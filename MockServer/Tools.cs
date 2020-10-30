using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MockServer
{
    public class Tools
    {
        public Tools()
        {
        }

        internal static string ExtractTimestampFromJSONFileName(FileInfo file)
        {
            string pattern = "^([0-9]+)_";
            Match m = Regex.Match(file.Name, pattern);
            if (m.Success && m.Groups.Count == 2 && m.Groups[1].Captures.Count == 1)
            {
                if (m.Groups[1].Captures[0] != null)
                {
                    return m.Groups[1].Captures[0].ToString();
                }
            }
            return string.Empty;
        }

        internal static DateTime ConvertFromFileTimestamp(string timestamp)
        {
            string pattern = "^([0-9]{4})([0-9]{2})([0-9]{2})([0-9]{2})([0-9]{2})([0-9]{2})([0-9]{3})";
            Match m = Regex.Match(timestamp, pattern);
            if (m.Success && m.Groups.Count == 8
                && m.Groups[1].Captures.Count == 1
                && m.Groups[2].Captures.Count == 1
                && m.Groups[3].Captures.Count == 1
                && m.Groups[4].Captures.Count == 1
                && m.Groups[5].Captures.Count == 1
                && m.Groups[6].Captures.Count == 1
                && m.Groups[7].Captures.Count == 1)
            {
                if (int.TryParse(m.Groups[1].Captures[0].ToString(), out int year)
                    && int.TryParse(m.Groups[2].Captures[0].ToString(), out int month)
                    && int.TryParse(m.Groups[3].Captures[0].ToString(), out int day)
                    && int.TryParse(m.Groups[4].Captures[0].ToString(), out int hour)
                    && int.TryParse(m.Groups[5].Captures[0].ToString(), out int minute)
                    && int.TryParse(m.Groups[6].Captures[0].ToString(), out int second)
                    && int.TryParse(m.Groups[7].Captures[0].ToString(), out int millisecond))
                {
                    return new DateTime(year, month, day, hour, minute, second, millisecond);
                }
            }
            return DateTime.MinValue;
        }
    }
}
