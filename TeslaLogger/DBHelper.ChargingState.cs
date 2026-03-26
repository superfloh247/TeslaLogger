using Exceptionless;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
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
        /// Updates all charging records where actual current is missing (NULL).
        /// Calculates current from power/voltage for single-phase chargers.
        /// Uses batched updates for performance optimization.
        /// </summary>
        public static void UpdateAllNullAmpereCharging()
        {
            Tools.DebugLog("UpdateAllNullAmpereCharging() [OPTIMIZED]");
            try
            {
                int totalUpdated = 0;
                int batchSize = 5000;
                var swTotal = new Stopwatch();
                swTotal.Start();

                while (true)
                {
                    // No ORDER BY (wastes CPU), added date filtering to reduce dataset, batched with LIMIT
                    string sql = $@"
UPDATE charging
SET charger_actual_current = ROUND(charger_power * 1000 / charger_voltage, 2)
WHERE
    charger_voltage > 250
    AND charger_power > 1
    AND charger_phases = 1
    AND charger_actual_current = 0
    AND Datum > DATE_SUB(NOW(), INTERVAL 60 DAY)
LIMIT {batchSize}";

                    int updated = ExecuteSQLQuery(sql, 120);
                    totalUpdated += updated;

                    if (updated < batchSize)
                    {
                        swTotal.Stop();
                        Logfile.Log($"UpdateAllNullAmpereCharging: {totalUpdated} rows updated in {swTotal.ElapsedMilliseconds}ms");
                        break;  // No more updates needed
                    }

                    System.Threading.Thread.Sleep(50);  // Brief pause between batches
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Starts a new charging session for the vehicle.
        /// Handles electricity meter integration, FleetAPI compatibility, and async position updates.
        /// </summary>
        /// <param name="wh">The WebHelper instance with current charging data</param>
        /// <param name="cancellationToken">Cancellation token for async operations</param>
        /// <remarks>
        /// This method orchestrates the complete charging state lifecycle:
        /// 1. Gets electricity meter readings
        /// 2. Records current position
        /// 3. Handles FleetAPI authentication
        /// 4. Inserts charging state record
        /// 5. Updates vehicle state
        /// 6. Starts background monitoring tasks
        /// </remarks>
        public async Task StartChargingStateAsync(WebHelper wh, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(wh);

            // Step 1: Get electricity meter readings (if available)
            var (meter_vehicle_kwh_start, meter_utility_kwh_start) = Helper_GetElectricityMeterReadings(wh);
            var electricityMeter = !wh.fast_charger_present ? ElectricityMeterBase.Instance(wh.car) : null;

            // Step 2: Get current position
            int posid = GetMaxPosid();

            // Step 3: Handle FleetAPI charging state
            if (car.FleetAPI)
            {
                await car.webhelper.IsChargingAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                UpdatePosFromCurrentJSON(posid);
            }

            // Step 4: Get charging metadata
            Helper_GetStartChargingState(out int chargeID, out DateTime chargeStart);

            // Step 5: Insert charging state record and get ID
            long chargingstateid = await Helper_InsertChargingStateRecordAsync(
                wh, posid, chargeID, chargeStart,
                meter_vehicle_kwh_start, meter_utility_kwh_start,
                cancellationToken).ConfigureAwait(false);

            // Step 6: Update vehicle charging state
            Helper_UpdateVehicleChargingState(wh.car);

            // Step 7: Background task - monitor meter charging status (if meter exists)
            if (electricityMeter is not null && electricityMeter.IsCharging() != true)
            {
                _ = Task.Run(async () =>
                {
                    for (int x = 0; x < 10; x++)
                    {
                        if (electricityMeter.IsCharging() == true)
                        {
                            car.Log("Meter: Charging!");
                            return;
                        }

                        car.Log("Meter: Not Charging!");
                        await Task.Delay(6000).ConfigureAwait(false);
                    }

                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        await con.OpenAsync().ConfigureAwait(false);
                        using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE chargingstate SET meter_vehicle_kwh_start = NULL, meter_utility_kwh_start = NULL WHERE id = @id", con))
                        {
                            cmd.Parameters.AddWithValue("@id", chargingstateid);
                            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }
                    }
                }, car.cts.Token);
            }

            // Step 8: Background task - update charging position after delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(30000).ConfigureAwait(false);
                await Helper_UpdateChargingStatePositionAsync(wh, chargingstateid).ConfigureAwait(false);
            }, car.cts.Token);
        }

        /// <summary>
        /// Retrieves all charging sessions with position and CO2 data.
        /// Used for CO2 emissions calculation and country-specific data enrichment.
        /// </summary>
        /// <returns>DataTable containing charging sessions with position and environmental data</returns>
        public static DataTable GetAllChargingstates()
        {
            var dt = new DataTable();

            MySqlDataAdapter da = new MySqlDataAdapter(@"SELECT chargingstate.id, StartDate, EndDate, address, lat, lng, charge_energy_added, country, co2_g_kWh
                FROM chargingstate join pos on chargingstate.pos = pos.id 
                order by lat, lng", DBHelper.DBConnectionstring);
            da.Fill(dt);

            return dt;
        }

        /// <summary>
        /// Updates charging state with country and CO2 emissions data.
        /// Stores the CO2 emissions rate (g/kWh) for the charging location.
        /// </summary>
        /// <param name="ChargingStateID">The charging state record ID to update</param>
        /// <param name="country">The country code or name</param>
        /// <param name="CO2">CO2 emissions rate in g/kWh (or 0 for NULL)</param>
        private static void UpdateChargingStateCountryCO2(int ChargingStateID, string? country, int CO2)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE 
  chargingstate 
SET 
  country = @country, co2_g_kWh = @co2
WHERE 
  id = @id", con))
                    {
                        cmd.Parameters.AddWithValue("@country", country);
                        cmd.Parameters.AddWithValue("@id", ChargingStateID);
                        cmd.Parameters.AddWithValue("@co2", CO2 == 0 ? (object)DBNull.Value : (object)CO2);
                        int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                        Logfile.Log($"UpdateChargingStateCountry({ChargingStateID}): {rowsUpdated} rows updated");
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();

                Tools.DebugLog($"Exception during DBHelper.UpdateChargingStateCountryCO2(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.UpdateChargingStateCountryCO2()");
            }
        }
    }
}
