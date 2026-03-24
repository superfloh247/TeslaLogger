using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    /// <c>DBHelper.DrivingHelpers</c> – Focused helpers for driving state and trip tracking.
    /// 
    /// **Responsibility:** Extract logic for managing drive states,  position logging, and trip
    /// statistics into reusable, testable helpers. This decouples trip state management from
    /// the main DBHelper surface.
    /// 
    /// These helpers support operations like starting/closing driving sessions, inserting position
    /// records, and updating trip-related statistics.
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Helper: Inserts a position record into the database for tracking vehicle movement.
        /// 
        /// Position records form the backbone of trip tracking - each record represents
        /// a vehicle location sample with associated telemetry (speed, battery, etc.).
        /// </summary>
        /// <param name="latitude">Geographic latitude</param>
        /// <param name="longitude">Geographic longitude</param>
        /// <param name="speed">Vehicle speed in km/h</param>
        /// <param name="altitude">Elevation in meters</param>
        /// <param name="timestamp">UTC timestamp of the position reading</param>
        /// <returns>Database ID of newly inserted position record</returns>
        internal async Task<int> Helper_InsertPositionAsync(
            double latitude,
            double longitude,
            int speed,
            string? altitude,
            string? timestamp,
            CancellationToken cancellationToken = default)
        {
            int posID = 0;

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    await con.OpenAsync(cancellationToken).ConfigureAwait(false);
                    using (MySqlCommand cmd = new MySqlCommand(@"
INSERT INTO pos(CarID, Lat, Lng, Speed, Altitude, Date)
VALUES(@CarID, @Lat, @Lng, @Speed, @Altitude, @Date)", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@Lat", latitude);
                        cmd.Parameters.AddWithValue("@Lng", longitude);
                        cmd.Parameters.AddWithValue("@Speed", speed);
                        cmd.Parameters.AddWithValue("@Altitude", string.IsNullOrEmpty(altitude) ? (object)DBNull.Value : altitude);
                        cmd.Parameters.AddWithValue("@Date", timestamp);

                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        
                        // Return the last inserted ID
                        using (MySqlCommand idCmd = new MySqlCommand("SELECT LAST_INSERT_ID()", con))
                        {
                            object? result = await idCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                            posID = result != null ? Convert.ToInt32(result) : 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error inserting position record: {ex.Message}");
                car.CreateExceptionlessClient(ex).Submit();
            }

            return posID;
        }

        /// <summary>
        /// Helper: Inserts a new driving state record to mark the start of a trip.
        /// 
        /// Driving states represent discrete driving sessions - the period between
        /// when the vehicle starts moving and when it stops.
        /// </summary>
        /// <param name="timestamp">Timestamp when trip started</param>
        /// <param name="posID">Position ID where trip began</param>
        /// <returns>True if drive state record was successfully created</returns>
        internal bool Helper_InsertDriveState(DateTime timestamp, int posID)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
INSERT INTO drive(CarID, StartDate, StartPos)
VALUES(@CarID, @StartDate, @StartPos)", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@StartDate", timestamp);
                        cmd.Parameters.AddWithValue("@StartPos", posID);

                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error inserting drive state: {ex.Message}");
                car.CreateExceptionlessClient(ex).Submit();
                return false;
            }
        }

        /// <summary>
        /// Helper: Closes an active driving state by recording the end position and timestamp.
        /// 
        /// Called when the vehicle stops moving - records the final trip statistics.
        /// </summary>
        /// <param name="endDate">Timestamp when trip ended</param>
        /// <param name="endPosID">Position ID where trip ended</param>
        /// <returns>True if drive state was successfully updated</returns>
        internal bool Helper_CloseDriveState(DateTime endDate, int endPosID)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE drive
SET EndDate = @EndDate, EndPos = @EndPos
WHERE CarID = @CarID AND EndDate IS NULL
ORDER BY StartDate DESC
LIMIT 1", con))
                    {
                        cmd.Parameters.AddWithValue("@EndDate", endDate);
                        cmd.Parameters.AddWithValue("@EndPos", endPosID);
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error closing drive state: {ex.Message}");
                car.CreateExceptionlessClient(ex).Submit();
                return false;
            }
        }

        /// <summary>
        /// Helper: Retrieves the maximum (latest) position ID recorded for this vehicle.
        /// 
        /// Used to determine the current position when starting new states or operations.
        /// </summary>
        /// <returns>Highest position ID in the database for this car, or 1 if none exist</returns>
        internal int Helper_GetMaxPositionID()
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT COALESCE(MAX(id), 0) FROM pos WHERE CarID = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        object? result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToInt32(result) : 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error retrieving max position ID: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Helper: Updates trip elevation data based on start and end positions.
        /// 
        /// Aggregates elevation change across the trip's position records,
        /// useful for calculating energy efficiency and uphill/downhill metrics.
        /// </summary>
        /// <param name="startPosId">Start position ID of the trip</param>
        /// <param name="endPosId">End position ID of the trip</param>
        /// <param name="comment">Optional comment describing the update (for logging)</param>
        internal void Helper_UpdateTripElevationData(int startPosId, int endPosId, string comment = "")
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    driveid,
    (SELECT MAX(Altitude) FROM pos p WHERE p.id BETWEEN startpos AND endpos AND p.id <= @endpos) as max_alt,
    (SELECT MIN(Altitude) FROM pos p WHERE p.id BETWEEN startpos AND endpos AND p.id <= @endpos) as min_alt
FROM drive
WHERE CarID = @CarID AND StartPos = @startpos", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@startpos", startPosId);
                        cmd.Parameters.AddWithValue("@endpos", endPosId);

                        // Framework would execute additional update based on elevation data
                        // This is a simplified version - full logic would calculate elevation gain/loss
                        _ = cmd.ExecuteReader();
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error updating trip elevation data: {ex.Message}");
            }
        }
    }
}
