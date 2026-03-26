using Exceptionless;
using MySql.Data.MySqlClient;
using System;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
#pragma warning disable CS8601 // Possible null reference assignment
#pragma warning disable CS8602 // Dereference of possibly null reference
#pragma warning disable CS8603 // Possible null reference return
#pragma warning disable CS8604 // Possible null reference argument
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable type

#nullable enable

namespace TeslaLogger
{
    [SuppressMessage("Globalization", "CA1303:Literale nicht als lokalisierte Parameter übergeben", Justification = "<Pending>")]
    [SuppressMessage("Design", "CA1031:Keine allgemeinen Ausnahmetypen abfangen", Justification = "<Pending>")]
    public partial class DBHelper
    {
        /// <summary>
        /// Retrieves the refresh token associated with an access token.
        /// </summary>
        /// <param name="access_token">The Tesla API access token</param>
        /// <returns>The refresh token if found; empty string otherwise</returns>
        public static string? GetRefreshTokenFromAccessToken(string? access_token)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    refresh_token
FROM
    cars
WHERE
    tesla_token = @tesla_token", con))
                    {
                        cmd.Parameters.AddWithValue("@tesla_token", access_token);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            string refresh_token = dr.GetStringOrNull(0) ?? "";
                            return refresh_token;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless();
                Logfile.Log(ex.ToString());
            }

            return "";
        }

        /// <summary>
        /// Validates NET8 Tasker token availability.
        /// Currently returns true; Tasker hash lookup logic is commented pending implementation.
        /// </summary>
        /// <returns>True if tasker token is available; false otherwise</returns>
        public static bool NET8TaskerToken()
        {
            try
            {
                return true;

                /*
                using (MySqlConnection con = new MySqlConnection($"{DBConnectionstring};Allow User Variables=True"))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"SELECT tasker_hash FROM teslalogger.cars where left(tasker_hash,1) in (1,2,3,4,5)", con)) // 
                    {
                        using (MySqlDataReader dr = SQLTracer.TraceDR(cmd))
                        {
                            if (dr.Read())
                            {
                                Logfile.Log($"NET8TaskerToken: true - {dr.GetString(0)}");
                                return true;
                            }
                            else
                            {
                                Logfile.Log("NET8TaskerToken: false");
                                return false;
                            }
                        }
                    }
                }*/
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                ex.ToExceptionless().Submit();
            }
            return false;
        }
    }
}
