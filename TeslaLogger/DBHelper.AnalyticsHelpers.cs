using MySql.Data.MySqlClient;
using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;

#pragma warning disable CS8600
#pragma warning disable CS8601
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

#nullable enable

namespace TeslaLogger
{
    /// <summary>
    /// <c>DBHelper.AnalyticsHelpers</c> – Focused helpers for fleet analytics and efficiency calculations.
    /// 
    /// **Responsibility:** Extract complex analytics queries and calculations into reusable helpers.
    /// This includes economy calculations (Wh/km), efficiency metrics, and consumption aggregations.
    /// 
    /// These helpers are internal and designed to support analytics operations and reporting features.
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Helper: Retrieves consumption data for a given time period.
        /// 
        /// Queries the database for all driving records within the specified date range,
        /// returning key metrics needed for efficiency calculations.
        /// </summary>
        /// <param name="startDT">Start date/time for the analysis period</param>
        /// <param name="endDT">End date/time for the analysis period</param>
        /// <returns>DataTable with columns: km, wh, speed, etc.</returns>
        internal DataTable? Helper_GetConsumptionData(DateTime startDT, DateTime endDT)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    SUM(CAST(km AS DECIMAL(8,2))) AS km,
    SUM(CAST(kwh AS DECIMAL(8,2))) AS kwh,
    SUM(CAST(km AS DECIMAL(8,2))) / SUM(CAST(kwh AS DECIMAL(8,2))) AS km_per_kwh,
    SUM(CAST(kwh AS DECIMAL(8,2))) / SUM(CAST(km AS DECIMAL(8,2))) * 1000 AS wh_per_km
FROM
    drive
WHERE
    CarID = @carid
    AND StartDate >= @start
    AND EndDate <= @end", con))
                    {
                        cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                        cmd.Parameters.AddWithValue("@start", startDT);
                        cmd.Parameters.AddWithValue("@end", endDT);

                        using (var adapter = new MySqlDataAdapter(cmd))
                        {
                            var dt = new DataTable();
                            adapter.Fill(dt);
                            return dt;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error retrieving consumption data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Helper: Calculates efficiency metric for a specific data row.
        /// 
        /// Accepts consumption data and computes a requested metric type
        /// (e.g., Wh/km, km/Wh, kWh, km, etc.).
        /// </summary>
        /// <param name="dataRow">DataRow from consumption data query</param>
        /// <param name="metricType">Type of metric: "wh_km", "km_wh", "kwh", "km", etc.</param>
        /// <returns>Calculated metric value or 0 if data is invalid</returns>
        internal double Helper_CalculateEfficiencyMetric(DataRow? dataRow, string metricType)
        {
            if (dataRow == null)
                return 0;

            try
            {
                double km = Convert.ToDouble(dataRow["km"] ?? 0);
                double kwh = Convert.ToDouble(dataRow["kwh"] ?? 0);

                return metricType switch
                {
                    "wh_km" => kwh > 0 ? (kwh * 1000) / km : 0,  // Wh per km
                    "km_wh" => kwh > 0 ? km / kwh : 0,            // km per Wh
                    "kwh" => kwh,                                  // Total kWh
                    "km" => km,                                    // Total km
                    _ => 0
                };
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error calculating efficiency metric: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Helper: Validates that consumption data meets minimum quality thresholds.
        /// 
        /// Checks for common data quality issues (missing values, zero km, zero energy, etc.)
        /// and logs warnings if detected.
        /// </summary>
        /// <param name="dataTable">DataTable to validate</param>
        /// <returns>True if data is valid and sufficient for analysis, false otherwise</returns>
        internal bool Helper_ValidateConsumptionData(DataTable? dataTable)
        {
            if (dataTable == null || dataTable.Rows.Count == 0)
            {
                Logfile.Log("No consumption data found for analysis period");
                return false;
            }

            try
            {
                var row = dataTable.Rows[0];
                
                double km = Convert.ToDouble(row["km"] ?? 0);
                double kwh = Convert.ToDouble(row["kwh"] ?? 0);

                // Minimum thresholds
                if (km < 1.0)
                {
                    Logfile.Log($"Consumption data validation: km {km} < minimum 1.0");
                    return false;
                }

                if (kwh < 0.1)
                {
                    Logfile.Log($"Consumption data validation: kwh {kwh} < minimum 0.1");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error validating consumption data: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Helper: Retrieves average tire pressure readings for a time period.
        /// 
        /// Used in analytics reporting; aggregates TPMS sensor data.
        /// </summary>
        /// <param name="startDT">Start of period</param>
        /// <param name="endDT">End of period</param>
        /// <param name="tPMS_FL">Output: Average front-left pressure</param>
        /// <param name="tPMS_FR">Output: Average front-right pressure</param>
        /// <param name="tPMS_RL">Output: Average rear-left pressure</param>
        /// <param name="tPMS_RR">Output: Average rear-right pressure</param>
        internal void Helper_GetAVG_TPMS_Data(
            DateTime startDT,
            DateTime endDT,
            out double tPMS_FL,
            out double tPMS_FR,
            out double tPMS_RL,
            out double tPMS_RR)
        {
            tPMS_FL = 0;
            tPMS_FR = 0;
            tPMS_RL = 0;
            tPMS_RR = 0;

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    AVG(CAST(TPMS_FL AS DECIMAL(5,2))) AS tpms_fl,
    AVG(CAST(TPMS_FR AS DECIMAL(5,2))) AS tpms_fr,
    AVG(CAST(TPMS_RL AS DECIMAL(5,2))) AS tpms_rl,
    AVG(CAST(TPMS_RR AS DECIMAL(5,2))) AS tpms_rr
FROM
    drive
WHERE
    CarID = @carid
    AND StartDate >= @start
    AND EndDate <= @end
    AND TPMS_FL is not null", con))
                    {
                        cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                        cmd.Parameters.AddWithValue("@start", startDT);
                        cmd.Parameters.AddWithValue("@end", endDT);

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                tPMS_FL = reader.IsDBNull(0) ? 0 : (double)reader.GetDecimal(0);
                                tPMS_FR = reader.IsDBNull(1) ? 0 : (double)reader.GetDecimal(1);
                                tPMS_RL = reader.IsDBNull(2) ? 0 : (double)reader.GetDecimal(2);
                                tPMS_RR = reader.IsDBNull(3) ? 0 : (double)reader.GetDecimal(3);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error retrieving TPMS data: {ex.Message}");
            }
        }
    }
}
