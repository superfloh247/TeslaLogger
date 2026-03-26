using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
#pragma warning disable CS8601 // Possible null reference assignment
#pragma warning disable CS8602 // Dereference of possibly null reference
#pragma warning disable CS8603 // Possible null reference return
#pragma warning disable CS8604 // Possible null reference argument
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable type

namespace TeslaLogger
{
    /// <summary>
    /// Partial class for DBHelper handling vehicle state transitions and Mothership telemetry tracking.
    /// Responsible for managing online/offline/asleep states, HTTP status code tracking, and API command statistics.
    /// Key responsibilities:
    /// - Vehicle state lifecycle management (StartStateAsync, CloseStateAsync)
    /// - Mothership command telemetry recording and analysis
    /// - HTTP status code inventory maintenance
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Initializes Mothership command tracking by loading all commands from database.
        /// Must be called before AddMothershipDataToDBAsync is used.
        /// </summary>
        public static void EnableMothership()
        {
            GetMothershipCommandsFromDBAsync().Wait();
            mothershipEnabled = true;
        }

        /// <summary>
        /// Populates the httpcodes reference table with all HttpStatusCode enum values.
        /// This table is used for normalizing and tracking HTTP response codes across the application.
        /// Safe to call multiple times (uses INSERT IGNORE).
        /// </summary>
        public static void UpdateHTTPStatusCodes()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                foreach (HttpStatusCode hsc in Enum.GetValues(typeof(HttpStatusCode)))
                {
                    using (MySqlCommand cmd = new MySqlCommand(@"
INSERT IGNORE
    httpcodes(id, TEXT)
VALUES(@id, @text)", con))
                    {
                        cmd.Parameters.AddWithValue("@id", (int)hsc);
                        cmd.Parameters.AddWithValue("@text", hsc.ToString());
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Asynchronously closes the current vehicle state record by setting EndDate and EndPos.
        /// This is called when transitioning to a new state or closing the current session.
        /// Updates CurrentJSON to reflect state closure.
        /// </summary>
        /// <param name="maxPosid">Position ID marking end of this state period</param>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        public async Task CloseStateAsync(int maxPosid, CancellationToken cancellationToken = default)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                await con.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    state
SET
    EndDate = @enddate,
    EndPos = @EndPos
WHERE
    EndDate IS NULL
    AND CarID = @CarID", con))
                {
                    cmd.Parameters.AddWithValue("@enddate", DateTime.Now);
                    cmd.Parameters.AddWithValue("@EndPos", maxPosid);
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            car.CurrentJSON.CreateCurrentJSON();
        }

        /// <summary>
        /// Asynchronously transitions vehicle to a new state (online/asleep/offline).
        /// Updates CurrentJSON with appropriate flags (current_online, current_sleeping).
        /// Closes the previous state before opening a new one to maintain state sequence integrity.
        /// </summary>
        /// <param name="state">Target state: "online", "asleep", or "offline"</param>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        public async Task StartStateAsync(string? state, CancellationToken cancellationToken = default)
        {
            if (state is not null)
            {
                if (state == "online")
                {
                    car.CurrentJSON.current_online = true;
                    car.CurrentJSON.current_sleeping = false;
                }
                else if (state == "asleep")
                {
                    car.CurrentJSON.current_online = false;
                    car.CurrentJSON.current_sleeping = true;
                }
                else if (state == "offline")
                {
                    car.CurrentJSON.current_online = false;
                    car.CurrentJSON.current_sleeping = false;
                }

                car.CurrentJSON.CreateCurrentJSON();
            }

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                await con.OpenAsync(cancellationToken).ConfigureAwait(false);

                using (MySqlCommand cmd1 = new MySqlCommand(@"
SELECT
    state
FROM
    state
WHERE
    EndDate IS NULL
    AND CarID = @carid", con))
                {
                    cmd1.Parameters.AddWithValue("@carid", car.CarInDB);
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd1);
                    if (dr.Read())
                    {
                        if (dr.GetStringOrDefault(0, "") == state)
                        {
                            return;
                        }
                    }
                    dr.Close();

                    int MaxPosid = GetMaxPosid();
                    await CloseStateAsync(MaxPosid, cancellationToken).ConfigureAwait(false);

                    car.Log($"state: {state}");

                    using (MySqlCommand cmd = new MySqlCommand(@"
INSERT
    state(
        StartDate,
        state,
        StartPos,
        CarID
    )
VALUES(
    @StartDate,
    @state,
    @StartPos,
    @CarID
)", con))
                    {
                        cmd.Parameters.AddWithValue("@StartDate", DateTime.Now);
                        cmd.Parameters.AddWithValue("@state", state);
                        cmd.Parameters.AddWithValue("@StartPos", MaxPosid);
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Records Mothership API command execution with elapsed time as duration.
        /// Calculates duration from start time to current UTC time and delegates to duration-based overload.
        /// Used for tracking API performance metrics with timestamp precision.
        /// </summary>
        /// <param name="command">API command endpoint string</param>
        /// <param name="start">Command start timestamp (UTC assumed)</param>
        /// <param name="httpcode">HTTP response code from API</param>
        /// <param name="carid">Tesla API vehicle ID (or 0/-1 for null)</param>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        public static async Task AddMothershipDataToDBAsync(string? command, DateTime start, int httpcode, int carid, CancellationToken cancellationToken = default)
        {
            if (mothershipEnabled == false)
            {
                return;
            }

            DateTime end = DateTime.UtcNow;
            TimeSpan ts = end - start;
            double duration = ts.TotalSeconds;
            await AddMothershipDataToDBAsync(command, duration, httpcode, carid, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Records Mothership API command execution with explicit duration.
        /// Normalizes long command strings (e.g., vehicle_data_everything) for storage efficiency.
        /// Creates new command entry if not found in cache, then records telemetry.
        /// </summary>
        /// <param name="command">API command endpoint string; normalized before storage</param>
        /// <param name="duration">Command execution duration in seconds (precise to centisecond)</param>
        /// <param name="httpcode">HTTP response code from API (200, 401, 404, 408, 503, etc.)</param>
        /// <param name="carid">Tesla API vehicle ID or 0/-1 for null/global command</param>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        public static async Task AddMothershipDataToDBAsync(string? command, double duration, int httpcode, int carid, CancellationToken cancellationToken = default)
        {
            if (command.Contains(WebHelper.vehicle_data_everything))
                command = command.Replace(WebHelper.vehicle_data_everything, "vehicle_data_everything");

            if (!mothershipCommands.ContainsKey(command))
            {
                await AddCommandToDBAsync(command).ConfigureAwait(false);
                await GetMothershipCommandsFromDBAsync().ConfigureAwait(false);
            }
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                await con.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (MySqlCommand cmd = new MySqlCommand(@"
INSERT
    mothership(
        ts,
        commandid,
        duration,
        httpcode,
        carid
    )
VALUES(
    @ts,
    @commandid,
    @duration,
    @httpcode,
    @carid
)", con))
                {
                    cmd.Parameters.AddWithValue("@ts", DateTime.Now);
                    cmd.Parameters.AddWithValue("@commandid", mothershipCommands[command]);
                    cmd.Parameters.AddWithValue("@duration", duration);
                    cmd.Parameters.AddWithValue("@httpcode", httpcode);
                    if (carid == 0 || carid == -1)
                        cmd.Parameters.AddWithValue("@carid", DBNull.Value);
                    else
                        cmd.Parameters.AddWithValue("@carid", carid);
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Internal helper: Adds a new Mothership command string to the database.
        /// Used during lazy initialization when a new command is encountered.
        /// </summary>
        /// <param name="command">Command string to store</param>
        private static async Task AddCommandToDBAsync(string? command)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                await con.OpenAsync().ConfigureAwait(false);
                using (MySqlCommand cmd = new MySqlCommand("insert mothershipcommands (command) values (@command)", con))
                {
                    cmd.Parameters.AddWithValue("@command", command);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Internal helper: Loads all Mothership commands from database into the mothershipCommands cache.
        /// Called during EnableMothership() initialization and when new commands are encountered.
        /// Uses thread-safe TryAdd to handle concurrent access.
        /// </summary>
        private static async Task GetMothershipCommandsFromDBAsync()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                await con.OpenAsync().ConfigureAwait(false);
                using (MySqlCommand cmd = new MySqlCommand("SELECT id, command FROM mothershipcommands", con))
                {
                    var dr = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await dr.ReadAsync().ConfigureAwait(false))
                    {
                        int id = Convert.ToInt32(dr["id"], Tools.ciDeDE);
                        string command = dr[1].ToString();
                        if (!mothershipCommands.ContainsKey(command))
                        {
                            mothershipCommands.TryAdd(command, id);
                        }
                    }
                }
            }
        }
    }
}
