using Exceptionless;
using MySql.Data.MySqlClient;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

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
        /// Inserts a new position record into the database with complete telemetry data.
        /// Updates the car's current JSON state and calculates trip statistics.
        /// </summary>
        /// <param name="timestamp">Unix timestamp (milliseconds since epoch)</param>
        /// <param name="latitude">GPS latitude coordinate</param>
        /// <param name="longitude">GPS longitude coordinate</param>
        /// <param name="speed">Speed in mph</param>
        /// <param name="power">Power in kW (nullable)</param>
        /// <param name="odometer">Trip odometer reading (nullable)</param>
        /// <param name="idealBatteryRangeKm">Ideal battery range in km (-1 for NULL)</param>
        /// <param name="batteryRangeKm">Actual battery range in km (-1 for NULL)</param>
        /// <param name="batteryLevel">Battery charge level 0-100 (-1 for NULL)</param>
        /// <param name="insideTemp">Interior temperature in °C (nullable)</param>
        /// <param name="outsideTemp">Exterior temperature in °C (nullable)</param>
        /// <param name="altitude">Altitude in meters (nullable, empty string for NULL)</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>The ID of the inserted position record</returns>
        /// <remarks>
        /// This method performs extensive parameter validation and unit conversion:
        /// - Converts speed from mph to km/h
        /// - Converts power from W to kW (×1.35962 factor)
        /// - Handles NULL values for optional parameters
        /// - Updates the Car's CurrentJSON with latest telemetry
        /// - Calls Insert_active_route_energy_at_arrival for route tracking
        /// - Recalculates trip maximums (max_speed, max_power)
        /// </remarks>
        public async Task<int> InsertPosAsync(string? timestamp, double latitude, double longitude, int speed, decimal? power, double? odometer, double idealBatteryRangeKm, double batteryRangeKm, double batteryLevel, double? insideTemp, double? outsideTemp, string? altitude, CancellationToken cancellationToken = default)
        {
            int posid = 0;
            //double? inside_temp = car.CurrentJSON.current_inside_temperature;
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                await con.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (MySqlCommand cmd = new MySqlCommand(@"
INSERT
    pos(
        CarID,
        Datum,
        lat,
        lng,
        speed,
        POWER,
        odometer,
        ideal_battery_range_km,
        battery_range_km,
        outside_temp,
        altitude,
        battery_level,
        inside_temp,
        battery_heater,
        is_preconditioning,
        sentry_mode
    )
VALUES(
    @CarID,
    @Datum,
    @lat,
    @lng,
    @speed,
    @power,
    @odometer,
    @ideal_battery_range_km,
    @battery_range_km,
    @outside_temp,
    @altitude,
    @battery_level,
    @inside_temp,
    @battery_heater,
    @is_preconditioning,
    @sentry_mode
)", con))
                {
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                    cmd.Parameters.AddWithValue("@Datum", UnixToDateTime(long.Parse(timestamp, Tools.ciEnUS)));
                    cmd.Parameters.AddWithValue("@lat", latitude);
                    cmd.Parameters.AddWithValue("@lng", longitude);
                    cmd.Parameters.AddWithValue("@speed", (int)Tools.MphToKmhRounded(speed));
                    
                    if (power is null)
                        cmd.Parameters.AddWithValue("@power", DBNull.Value);
                    else
                        cmd.Parameters.AddWithValue("@power", Convert.ToInt32(power * 1.35962M));
                    
                    cmd.Parameters.AddWithValue("@odometer", odometer ?? (object)DBNull.Value);

                    if (idealBatteryRangeKm == -1)
                    {
                        cmd.Parameters.AddWithValue("@ideal_battery_range_km", DBNull.Value);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@ideal_battery_range_km", idealBatteryRangeKm);
                    }

                    if (batteryRangeKm == -1)
                    {
                        cmd.Parameters.AddWithValue("@battery_range_km", DBNull.Value);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@battery_range_km", batteryRangeKm);
                    }

                    if (outsideTemp is null)
                    {
                        cmd.Parameters.AddWithValue("@outside_temp", DBNull.Value);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@outside_temp", (double)outsideTemp);
                    }

                    if (altitude is not null && altitude.Length == 0)
                    {
                        cmd.Parameters.AddWithValue("@altitude", DBNull.Value);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@altitude", altitude);
                    }

                    if (batteryLevel == -1)
                    {
                        cmd.Parameters.AddWithValue("@battery_level", DBNull.Value);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@battery_level", batteryLevel);
                    }

                    if (insideTemp is null)
                    {
                        cmd.Parameters.AddWithValue("@inside_temp", DBNull.Value);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@inside_temp", (double)insideTemp);
                    }

                    cmd.Parameters.AddWithValue("@battery_heater", car.CurrentJSON.current_battery_heater ? 1 : 0);
                    cmd.Parameters.AddWithValue("@is_preconditioning", car.CurrentJSON.current_is_preconditioning ? 1 : 0);
                    cmd.Parameters.AddWithValue("@sentry_mode", car.CurrentJSON.current_is_sentry_mode ? 1 : 0);

                    _ = SQLTracer.TraceNQ(cmd, out long posID);

                    Insert_active_route_energy_at_arrival(posID);

                    using (MySqlCommand cmdid = new MySqlCommand("SELECT LAST_INSERT_ID()", con))
                    {
                        posid = Convert.ToInt32(await cmdid.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                    }

                    try
                    {
                        car.CurrentJSON.current_speed = (int)(speed * 1.609344M);
                        
                        if (power is not null)
                            car.CurrentJSON.current_power = (int)(power * 1.35962M);

                        car.CurrentJSON.SetPosition(latitude, longitude, long.Parse(timestamp, Tools.ciEnUS));

                        if (odometer is not null && odometer > 0)
                        {
                            car.CurrentJSON.current_odometer = odometer.Value;
                        }

                        if (idealBatteryRangeKm >= 0)
                        {
                            car.CurrentJSON.current_ideal_battery_range_km = idealBatteryRangeKm;
                        }

                        if (batteryRangeKm >= 0)
                        {
                            car.CurrentJSON.current_battery_range_km = batteryRangeKm;
                        }

                        if (car.CurrentJSON.current_trip_km_start == 0)
                        {
                            if (odometer is not null)
                                car.CurrentJSON.current_trip_km_start = odometer.Value;
                            else
                                car.Log("current_trip_km_start not set !!!");

                            car.CurrentJSON.current_trip_start_range = car.CurrentJSON.current_ideal_battery_range_km;
                        }

                        car.CurrentJSON.current_trip_max_speed = Math.Max(car.CurrentJSON.current_trip_max_speed, car.CurrentJSON.current_speed);
                        car.CurrentJSON.current_trip_max_power = Math.Max(car.CurrentJSON.current_trip_max_power, car.CurrentJSON.current_power);

                    }
                    catch (Exception ex)
                    {
                        car.CreateExceptionlessClient(ex).Submit();
                        car.Log(ex.ToString());
                    }
                }
            }

            car.CurrentJSON.CreateCurrentJSON();

            return posid;
        }

        /// <summary>
        /// Gets the maximum position ID (highest recorded position) from the database.
        /// Returns the ID of the most recent position record inserted.
        /// </summary>
        /// <returns>Maximum position ID, or 0 if no positions exist</returns>
        public static int CountPos()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand("Select max(id) from pos", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        object o = dr[0];
                        if (o == DBNull.Value)
                            return 0;

                        return Convert.ToInt32(dr[0], Tools.ciEnUS);
                    }
                }
            }

            return 0;
        }

        /// <summary>
        /// Finds the driving state record ID by matching start and end position IDs.
        /// Used to link position data to drive sessions.
        /// </summary>
        /// <param name="startPosId">Position ID at start of drive</param>
        /// <param name="endPosId">Position ID at end of drive</param>
        /// <returns>Drive state ID if found, -1 otherwise</returns>
        private static int GetDriveStateByStartPosEndPos(int startPosId, int endPosId)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id
FROM
    drivestate
WHERE
    StartPos = @StartPos
    AND EndPos = @EndPos", con))
                    {
                        cmd.Parameters.AddWithValue("@StartPos", startPosId);
                        cmd.Parameters.AddWithValue("@EndPos", endPosId);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (int.TryParse(dr[0].ToString(), out int driveId))
                            {
                                return driveId;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Tools.DebugLog(ex.ToString());
            }
            return -1;
        }
    }
}
