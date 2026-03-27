using MySql.Data.MySqlClient;
using Exceptionless;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace TeslaLogger
{
    /// <summary>
    /// DataUtilities: Data type conversion and serialization utilities (Batch 15)
    /// Provides helper methods for NULL handling, timestamp conversion, and JSON formatting
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Converts Unix timestamp (milliseconds since epoch) to local DateTime
        /// Assumes input is UTC milliseconds and converts to local time
        /// </summary>
        /// <param name="t">Unix timestamp in milliseconds</param>
        /// <returns>DateTime in local timezone</returns>
        public static DateTime UnixToDateTime(long t)
        {
            DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            dt = dt.AddMilliseconds(t);
            dt = dt.ToLocalTime();
            return dt;
        }

        /// <summary>
        /// Returns DBNull.Value if object is null, empty string, or numeric zero
        /// Used for database NULL conversions to prevent storing empty/zero values
        /// </summary>
        /// <remarks>
        /// Checks for:
        /// - Empty string ("")
        /// - String "0" or "0.00"
        /// - Null value
        /// Returns DBNull.Value for any match, otherwise returns original value
        /// </remarks>
        /// <param name="val">Value to check for emptiness or zero</param>
        /// <returns>DBNull.Value if empty/zero, otherwise original value</returns>
        public static object? DBNullIfEmptyOrZero(object? val)
        {
            if (val is String s && s.Length == 0)
                return DBNull.Value;

            if (val is null)
                return DBNull.Value;

            String temp = val.ToString();
            if (val.ToString() == "0" || val.ToString() == "0.00")
                return DBNull.Value;

            return val;
        }

        /// <summary>
        /// Returns DBNull.Value if object is null, empty string, or empty JSON value
        /// Used for database NULL conversions
        /// </summary>
        /// <remarks>
        /// Checks for:
        /// - Empty string ("")
        /// - Null value
        /// - JValue object without values (Newtonsoft.Json)
        /// Returns DBNull.Value for any match, otherwise returns original value
        /// </remarks>
        /// <param name="val">Value to check for emptiness</param>
        /// <returns>DBNull.Value if empty/null, otherwise original value</returns>
        public static object? DBNullIfEmpty(object? val)
        {
            if (val is String s && s.Length == 0)
                return DBNull.Value;

            if (val is null)
                return DBNull.Value;

            if (val is Newtonsoft.Json.Linq.JValue j && !j.HasValues)
                return DBNull.Value;

            return val;
        }

        /// <summary>
        /// Checks if string represents numeric zero (0, 0.0, 0.00, etc.)
        /// Returns false if string is null or cannot be parsed as number
        /// </summary>
        /// <remarks>
        /// Process:
        /// 1. If null or empty string → return false
        /// 2. Attempt parse as double
        /// 3. If successful and value == 0 → return true
        /// 4. Otherwise → return false
        /// </remarks>
        /// <param name="val">String value to check</param>
        /// <returns>True if value is numeric zero, false otherwise</returns>
        public static bool IsZero(string? val)
        {
            if (val is null || val.Length == 0)
            {
                return false;
            }

            if (double.TryParse(val, out double v))
            {
                if (v == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Executes SQL query and returns result as jQuery DataTable compatible JSON
        /// Wraps result set with DataTable metadata (record count, display count)
        /// </summary>
        /// <remarks>
        /// JSON structure for jQuery DataTables:
        /// {
        ///   "aaData": [ { col1: val, col2: val, ... }, ... ],
        ///   "iTotalRecords": total_row_count,
        ///   "iTotalDisplayRecords": displayed_row_count
        /// }
        /// 
        /// Error handling: Catches exceptions via Exceptionless and logs to Logfile
        /// </remarks>
        /// <param name="sql">SQL query string to execute</param>
        /// <returns>JSON string formatted for jQuery DataTables, or empty string on error</returns>
        public static string? GetJQueryDataTableJSON(string? sql)
        {
            string json = "";
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(sql, con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        json = DBHelper.GetJQueryDataTableJSON(dr);
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            return json;
        }

        /// <summary>
        /// Converts MySqlDataReader result set to jQuery DataTable compatible JSON
        /// Serializes all columns from each row as dictionary entries
        /// </summary>
        /// <remarks>
        /// JSON structure:
        /// {
        ///   "aaData": [
        ///     { "column_name": value, "column_name": value, ... },
        ///     { "column_name": value, "column_name": value, ... },
        ///     ...
        ///   ],
        ///   "iTotalRecords": row_count,
        ///   "iTotalDisplayRecords": row_count
        /// }
        /// 
        /// Uses Newtonsoft.Json.JsonConvert for serialization
        /// All columns from DataReader are included in output
        /// </remarks>
        /// <param name="dr">MySqlDataReader with result set</param>
        /// <returns>JSON string formatted for jQuery DataTables</returns>
        public static string? GetJQueryDataTableJSON(MySqlDataReader? dr)
        {
            var o = new Dictionary<string, object>();

            var aaData = new List<Dictionary<string, object>>();
            o.Add("aaData", aaData);

            int rows = 0;
            while (dr.Read())
            {
                rows++;
                var r = new Dictionary<string, object>();
                for (int x = 0; x < dr.FieldCount; x++)
                {
                    r.Add(dr.GetName(x), dr.GetValue(x));
                }

                aaData.Add(r);
            }

            o.Add("iTotalRecords", rows);
            o.Add("iTotalDisplayRecords", rows);

            var json = JsonConvert.SerializeObject(o);
            return json;
        }
    }
}
