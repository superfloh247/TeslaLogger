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
    /// <c>DBHelper.ChargingHelpers</c> – Focused helpers for charging state management.
    /// 
    /// **Responsibility:** Extract complex logic from charging methods into reusable,
    /// testable helpers. This reduces method complexity and improves maintainability.
    ///  
    /// These helpers are internal-only and designed to support charging-related
    /// operations such as inserting new sessions, retrieving meter readings, and
    /// calculating costs.
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Helper: Retrieves electricity meter readings for a given vehicle and charging context.
        /// 
        /// Used to capture starting meter values when charging begins (for non-fast-charging scenarios).
        /// </summary>
        /// <param name="wh">WebHelper instance containing charging context (null-safe for FleetAPI)</param>
        /// <returns>
        /// Tuple of (meter_vehicle_kwh_start, meter_utility_kwh_start).
        /// Returns DBNull.Value if meters are unavailable or fast charging is active.
        /// </returns>
        internal (object meterVehicle, object meterUtility) Helper_GetElectricityMeterReadings(WebHelper? wh)
        {
            object meter_vehicle_kwh_start = DBNull.Value;
            object meter_utility_kwh_start = DBNull.Value;

            try
            {
                if (wh is not null && !wh.fast_charger_present)
                {
                    var electricityMeter = ElectricityMeterBase.Instance(wh.car);
                    if (electricityMeter is not null)
                    {
                        var vehicleReading = electricityMeter.GetVehicleMeterReading_kWh();
                        meter_vehicle_kwh_start = (object?)vehicleReading ?? DBNull.Value;

                        var utilityReading = electricityMeter.GetUtilityMeterReading_kWh();
                        meter_utility_kwh_start = (object?)utilityReading ?? DBNull.Value;

                        car.Log($"Meter: {electricityMeter}");
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }

            return (meter_vehicle_kwh_start, meter_utility_kwh_start);
        }

        /// <summary>
        /// Helper: Retrieves the starting charging ID and timestamp for a new charging session.
        /// 
        /// The "charging ID" represents a row from the 'charging' table marking the beginning
        /// of the charging event in the API data stream.
        /// </summary>
        /// <param name="chargeID">Output: ID of the charging event from the 'charging' table</param>
        /// <param name="chargeStart">Output: Timestamp when charging started</param>
        /// <returns>True if a charging ID was found, false otherwise</returns>
        internal bool Helper_GetStartChargingState(out int chargeID, out DateTime chargeStart)
        {
            chargeID = GetMaxChargeid(out chargeStart, out _);
            return chargeID > 0;
        }

        /// <summary>
        /// Helper: Inserts a new charging state record into the database.
        /// 
        /// This encapsulates the SQL INSERT logic for starting a new charging session,
        /// separating data preparation from the main StartChargingStateAsync method.
        /// </summary>
        /// <param name="wh">WebHelper with charging context and vehicle data</param>
        /// <param name="posid">Current position ID (where charging started)</param>
        /// <param name="chargeID">ID of the charging event row</param>
        /// <param name="chargeStart">Timestamp of charging start</param>
        /// <param name="meter_vehicle_kwh_start">Starting vehicle meter reading</param>
        /// <param name="meter_utility_kwh_start">Starting utility meter reading</param>
        /// <param name="cancellationToken">Cancellation token for async operation</param>
        /// <returns>The new charging state ID (from database INSERT)</returns>
        internal async Task<long> Helper_InsertChargingStateRecordAsync(
            WebHelper wh,
            int posid,
            int chargeID,
            DateTime chargeStart,
            object meter_vehicle_kwh_start,
            object meter_utility_kwh_start,
            CancellationToken cancellationToken = default)
        {
            long chargingstateid = 0;

            bool fast_charger_present = wh.fast_charger_present;
            if (car.telemetryParser?.dcCharging == true)
                fast_charger_present = true;

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                await con.OpenAsync(cancellationToken).ConfigureAwait(false);
                using (MySqlCommand cmd = new MySqlCommand(@"
INSERT
    chargingstate(
        CarID,
        StartDate,
        Pos,
        StartChargingID,
        fast_charger_brand,
        fast_charger_type,
        conn_charge_cable,
        fast_charger_present,
        meter_vehicle_kwh_start,
        meter_utility_kwh_start,
        wheel_type
    )
VALUES(
    @CarID,
    @StartDate,
    @Pos,
    @StartChargingID,
    @fast_charger_brand,
    @fast_charger_type,
    @conn_charge_cable,
    @fast_charger_present,
    @meter_vehicle_kwh_start,
    @meter_utility_kwh_start,
    @wheel_type
)", con))
                {
                    cmd.Parameters.AddWithValue("@CarID", wh.car.CarInDB);
                    cmd.Parameters.AddWithValue("@StartDate", chargeStart);
                    cmd.Parameters.AddWithValue("@Pos", posid);
                    cmd.Parameters.AddWithValue("@StartChargingID", chargeID);
                    cmd.Parameters.AddWithValue("@fast_charger_brand", wh.fast_charger_brand);
                    cmd.Parameters.AddWithValue("@fast_charger_type", wh.fast_charger_type);
                    cmd.Parameters.AddWithValue("@conn_charge_cable", wh.conn_charge_cable);
                    cmd.Parameters.AddWithValue("@fast_charger_present", fast_charger_present);
                    cmd.Parameters.AddWithValue("@meter_vehicle_kwh_start", meter_vehicle_kwh_start);
                    cmd.Parameters.AddWithValue("@meter_utility_kwh_start", meter_utility_kwh_start);
                    cmd.Parameters.AddWithValue("@wheel_type", wh.car.wheel_type);

                    _ = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    return chargingstateid;
                }
            }
        }

        /// <summary>
        /// Helper: Updates the vehicle's current charging state in memory and JSON storage.
        /// 
        /// Called after a new charging session is inserted to reflect the charging state
        /// in the vehicle's current telemetry snapshot.
        /// </summary>
        /// <param name="vehicleState">Vehicle instance to update</param>
        internal void Helper_UpdateVehicleChargingState(Car vehicleState)
        {
            vehicleState.CurrentJSON.current_charging = true;
            vehicleState.CurrentJSON.CreateCurrentJSON();
        }
    }
}
