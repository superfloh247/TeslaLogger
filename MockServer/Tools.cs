using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using MySqlConnector;

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

        internal static string ExtractEndpointFromJSONFileName(FileInfo file)
        {
            string pattern = "^[0-9]+_([a-z_]+)_[0-9]+.json$";
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

        internal static Dictionary<string, object> ExtractResponse(string _JSON)
        {
            object jsonResult = new JavaScriptSerializer().DeserializeObject(_JSON);
            object r1 = ((Dictionary<string, object>)jsonResult)["response"];
            Dictionary<string, object> r2 = (Dictionary<string, object>)r1;
            return r2;
        }

        internal static void Log(string text, Exception ex = null, [CallerFilePath] string _cfp = null, [CallerLineNumber] int _cln = 0)
        {
            Console.WriteLine($"{text} ({Path.GetFileName(_cfp)}:{_cln})");
            if (ex != null)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        internal static void Log(MySqlCommand cmd, [CallerFilePath] string _cfp = null, [CallerLineNumber] int _cln = 0, [CallerMemberName] string _cmn = null)
        {
            try
            {
                string msg = "SQL: " + cmd.CommandText;
                foreach (MySqlParameter p in cmd.Parameters)
                {
                    string pValue = "";
                    switch (p.DbType)
                    {
                        case DbType.AnsiString:
                        case DbType.AnsiStringFixedLength:
                        case DbType.Date:
                        case DbType.DateTime:
                        case DbType.DateTime2:
                        case DbType.Guid:
                        case DbType.String:
                        case DbType.StringFixedLength:
                        case DbType.Time:
                            if (p.Value != null)
                            {
                                pValue = $"'{p.Value.ToString().Replace("'", "\\'")}'";
                            }
                            else
                            {
                                pValue = "'NULL'";
                            }
                            break;
                        case DbType.Decimal:
                        case DbType.Double:
                        case DbType.Int16:
                        case DbType.Int32:
                        case DbType.Int64:
                        case DbType.UInt16:
                        case DbType.UInt32:
                        case DbType.UInt64:
                        case DbType.VarNumeric:
                        case DbType.Object:
                        case DbType.SByte:
                        case DbType.Single:
                        case DbType.Binary:
                        case DbType.Boolean:
                        case DbType.Byte:
                        case DbType.Currency:
                        case DbType.DateTimeOffset:
                        case DbType.Xml:
                        default:
                            if (p.Value != null)
                            {
                                pValue = p.Value.ToString();
                            }
                            else
                            {
                                pValue = "NULL";
                            }
                            break;
                    }
                    msg = msg.Replace(p.ParameterName, pValue);
                }
                Log($"{_cmn}: " + msg, null, _cfp, _cln);
            }
            catch (Exception ex)
            {
                Log("Exception in SQL DEBUG", ex);
            }
        }

        internal static long FileDateToTimestamp(FileInfo first)
        {
            // TODO handle time zone problems
            return Convert.ToInt64(Tools.ConvertFromFileTimestamp(Tools.ExtractTimestampFromJSONFileName(first)).Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds);
        }

        internal static long TimeStampNow()
        {
            return Convert.ToInt64(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds);
        }
    }
}
