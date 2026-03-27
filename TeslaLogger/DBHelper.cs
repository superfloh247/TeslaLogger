using Exceptionless;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp.Unsafe;

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
        private static Dictionary<string, int> mothershipCommands = new();
        private static bool mothershipEnabled; // defaults to false
        private Car car;
        bool CleanPasswortDone; // defaults to false

        private static Random random = new Random();

        // connection string logic has been moved to DBHelper.Connection.cs

        internal DBHelper(Car car)
        {
            this.car = car;
        }

        // State management methods moved to DBHelper.StateManagement.cs

        internal static void SetCost()
        {
            try
            {
                Logfile.Log("SetCost");

                string json = System.IO.File.ReadAllText(FileManager.GetSetCostPath);
                try
                {
                    JObject j = JObject.Parse(json);
                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    chargingstate
SET
    cost_total = @cost_total,
    cost_currency = @cost_currency,
    cost_per_kwh = @cost_per_kwh,
    cost_per_session = @cost_per_session,
    cost_per_minute = @cost_per_minute,
    cost_idle_fee_total = @cost_idle_fee_total
WHERE
    id = @id", con))
                    {

                        if (j["cost_total"] is null || j["cost_total"]?.ToString() == "" || j["cost_total"]?.ToString() == "0" || j["cost_total"]?.ToString() == "0.00")
                        {
                            cmd.Parameters.AddWithValue("@cost_total", DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@cost_total", j["cost_total"]);
                        }

                        cmd.Parameters.AddWithValue("@cost_currency", j["cost_currency"]);
                        cmd.Parameters.AddWithValue("@cost_per_kwh", j["cost_per_kwh"]);
                        cmd.Parameters.AddWithValue("@cost_per_session", j["cost_per_session"]);
                        cmd.Parameters.AddWithValue("@cost_per_minute", j["cost_per_minute"]);
                        cmd.Parameters.AddWithValue("@cost_idle_fee_total", j["cost_idle_fee_total"]);
                        cmd.Parameters.AddWithValue("@id", j["id"]);
                        int done = SQLTracer.TraceNQ(cmd, out _);

                        Logfile.Log($"SetCost OK: {done}");
                    }
                }
                }
                catch (Newtonsoft.Json.JsonException jsonEx)
                {
                    Logfile.Log($"DBHelper.SetCost: JSON parse error - {jsonEx.Message}");
                    jsonEx.ToExceptionless().FirstCarUserID().Submit();
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();

                Logfile.Log(ex.ToString());
            }
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        internal static void UpdateCarIDNull()
        {
            Tools.DebugLog("UpdateCarIDNull()");
            string[] tables = { "can", "car_version", "charging", "chargingstate", "drivestate", "pos", "shiftstate", "state" };
            foreach (string table in tables)
            {
                try
                {
                    int rows = 0;
                    int t = Environment.TickCount;
                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand($@"
UPDATE
    {table}
SET
    carid = 1
WHERE
    carid IS NULL", con))
                        {
                            cmd.CommandTimeout = 6000;
                            _ = SQLTracer.TraceNQ(cmd, out _);
                        }
                    }
                    t = Environment.TickCount - t;

                    if (rows > 0)
                    {
                        Logfile.Log($"update {table} set carid = 1 where carid is null; ms: {t} / Rows: {rows}");
                    }

                }
                catch (Exception ex)
                {
                    ex.ToExceptionless().FirstCarUserID().Submit();
                    Logfile.Log(ex.ToString());
                }
            }
        }

        internal string? GetRefreshToken(out string? tesla_token)
        {
            tesla_token = "";

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    refresh_token,
    tesla_token
FROM
    cars
WHERE
    id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            string refresh_token = dr.GetStringOrNull(0) ?? "";
                            refresh_token = StringCipher.Decrypt(refresh_token);

                            tesla_token = dr.GetStringOrNull(1) ?? "";
                            tesla_token = StringCipher.Decrypt(tesla_token);

                            return refresh_token;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }

            return "";
        }

        internal bool SetCarName(string? car_name)
        {
            car.CarName = car_name;

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    cars
SET
    display_name = @carname
WHERE
    id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@carname", car_name);
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }
            return true;
        }

        internal bool SetABRP(string? abrp_token, int abrp_mode)
        {
            car.ABRPToken = abrp_token;
            car.ABRPMode = abrp_mode;

            try
            {
                car.webhelper.SendDataToAbetterrouteplannerAsync(Tools.ToUnixTime(DateTime.UtcNow) * 1000, car.CurrentJSON.current_battery_level, 0, true, car.CurrentJSON.current_power, car.CurrentJSON.Latitude, car.CurrentJSON.Longitude).Wait();

                if (car.ABRPMode == -1)
                    return false;

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    cars
SET
    ABRP_token = @token,
    ABRP_mode = @mode
WHERE
    id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@token", abrp_token);
                        cmd.Parameters.AddWithValue("@mode", abrp_mode);
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }
            return true;
        }

        internal bool SetSucBingo(string? sucBingo_user, string? sucBingo_apiKey)
        {
            car.SuCBingoUser = sucBingo_user;
            car.SuCBingoApiKey = sucBingo_apiKey;

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    cars
SET
    SuCBingo_user = @user,
    SuCBingo_apiKey = @apikey
WHERE
    id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@user", sucBingo_user);
                        cmd.Parameters.AddWithValue("@apikey", sucBingo_apiKey);
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }
            return true;
        }

        internal bool GetCarName(out string? car_name)
        {
            car_name = "";

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("SELECT display_name FROM cars where id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            car_name = dr[0].ToString();
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            return false;
        }

        internal bool GetABRP(out string? ABRP_token, out int ABRP_mode)
        {
            ABRP_token = "";
            ABRP_mode = 0;

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("SELECT ABRP_token, ABRP_mode FROM cars where id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            ABRP_token = dr[0].ToString();
                            ABRP_mode = Convert.ToInt32(dr[1], Tools.ciEnUS);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            return false;
        }

        internal bool GetSuCBingo(out string? sucBingo_user, out string? sucBingo_apiKey)
        {
            sucBingo_user = "";
            sucBingo_apiKey = "";

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("SELECT SuCBingo_user, SuCBingo_apiKey FROM cars where id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            sucBingo_user = dr[0].ToString();
                            sucBingo_apiKey = dr[1].ToString();
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            return false;
        }

        internal void UpdateEmptyChargeEnergy()
        {
            Tools.DebugLog("UpdateEmptyChargeEnergy()");
            Queue<int> emptyChargeEnergy = new();
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
  id
FROM
  chargingstate
WHERE
  CarID=@CarID
  AND (charge_energy_added IS NULL OR charge_energy_added = 0)
  AND EndDate IS NOT NULL", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read() && dr[0] is not DBNull)
                        {
                            if (int.TryParse(dr[0].ToString(), out int id))
                            {
                                emptyChargeEnergy.Enqueue(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();

                Tools.DebugLog($"Exception during UpdateEmptyChargeEnergy(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during UpdateEmptyChargeEnergy()");
            }
            foreach (int ID in emptyChargeEnergy)
            {
                UpdateChargeEnergyAdded(ID);
            }
        }









        private Tuple<int, DateTime, DateTime> GetStartEndFromCharginState(int id)
        {
            Tuple<int, DateTime, DateTime> tuple = Tuple.Create(-1, DateTime.MinValue, DateTime.MinValue);
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
  chargingstate.id,
  chargingstate.StartDate,
  chargingstate.EndDate
FROM
  chargingstate
WHERE
  chargingstate.CarId = @CarID
  AND chargingstate.id = @ChargingStateID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", id);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (DateTime.TryParse(dr[1].ToString(), out DateTime StartDate)
                                && DateTime.TryParse(dr[2].ToString(), out DateTime EndDate))
                            {
                                tuple = Tuple.Create(id, StartDate, EndDate);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Logfile.ExceptionWriter(ex, "GetStartEndFromCharginState");
                car.Log(ex.ToString());
            }

            return tuple;
        }

        internal void InsertTPMS(int TireId, double pressure, DateTime dtFL)
        {
            try
            {
                string cacheKey = $"TPMS_{TireId}_{car.CarInDB}";
                object cacheValue = MemoryCache.Default.Get(cacheKey);
                if (cacheValue is not null)
                    return;

                MemoryCache.Default.Add(cacheKey, true, DateTime.Now.AddMinutes(2));

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"insert TPMS (CarID, Datum, TireID, Pressure) values (@CarID, @Datum, @TireID, @Pressure)", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@Datum", dtFL);
                        cmd.Parameters.AddWithValue("@TireID", TireId);
                        cmd.Parameters.AddWithValue("@Pressure", pressure);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (MySqlException ex)
            {
                if (ex.ErrorCode == -2147467259) // Duplicate entry
                    return;

                car.SendException2Exceptionless(ex);
                car.Log(ex.ToString());
            }
            catch (Exception ex)
            {
                car.SendException2Exceptionless(ex);
                car.Log(ex.ToString());
            }
        }

        internal void UpdateTeslaToken()
        {
            try
            {
                if (String.IsNullOrEmpty(car.webhelper.Tesla_token))
                {
                    car.CreateExeptionlessLog("Tesla Token", "Tesla Token EMPTY!!!", Exceptionless.Logging.LogLevel.Warn).Submit();
                    return;
                }

                car.Log("UpdateTeslaToken");

                string token = car.webhelper.Tesla_token;

                token = StringCipher.Encrypt(token);

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("update cars set tesla_token = @tesla_token, tesla_token_expire=@tesla_token_expire where id=@id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        cmd.Parameters.AddWithValue("@tesla_token", token);
                        cmd.Parameters.AddWithValue("@tesla_token_expire", car.webhelper.nextTeslaTokenFromRefreshToken);
                        int done = SQLTracer.TraceNQ(cmd, out _);

                        car.Log($"update tesla_token OK: {done} - {car.webhelper.Tesla_token.Substring(0, 20)}xxxxxx");

                        car.CreateExeptionlessLog("Tesla Token", "Update Tesla Token OK", Exceptionless.Logging.LogLevel.Info).Submit();

                        car.ExternalLog("UpdateTeslaToken");
                        // car.Restart("Access Token updated", 0);
                    }
                }
            }
            catch (System.Threading.ThreadAbortException)
            {
                System.Diagnostics.Debug.WriteLine("Thread Stop!");
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                car.Log(ex.ToString());
            }
        }

        internal void WriteCarSettings()
        {
            if (car.CarInDB == 0) // used for tests
                return;

            try
            {
                car.Log("UpdateTeslaToken");
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("update cars set display_name=@display_name, Raven=@Raven, Wh_TR=@Wh_TR, DB_Wh_TR=@DB_Wh_TR, DB_Wh_TR_count=@DB_Wh_TR_count, car_type=@car_type, car_special_type=@car_special_type, car_trim_badging=@trim_badging, model_name=@model_name, Battery=@Battery, tasker_hash=@tasker_hash, vin=@vin, wheel_type=@wheel_type where id=@id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        cmd.Parameters.AddWithValue("@Raven", car.Raven);
                        cmd.Parameters.AddWithValue("@Wh_TR", car.WhTR);
                        cmd.Parameters.AddWithValue("@DB_Wh_TR", car.DBWhTR);
                        cmd.Parameters.AddWithValue("@DB_Wh_TR_count", car.DBWhTRcount);
                        cmd.Parameters.AddWithValue("@car_type", car.CarType);
                        cmd.Parameters.AddWithValue("@car_special_type", car.CarSpecialType);
                        cmd.Parameters.AddWithValue("@trim_badging", car.TrimBadging);
                        cmd.Parameters.AddWithValue("@model_name", car.ModelName);
                        cmd.Parameters.AddWithValue("@Battery", car.Battery);
                        cmd.Parameters.AddWithValue("@display_name", car.DisplayName);
                        cmd.Parameters.AddWithValue("@tasker_hash", car.TaskerHash);
                        cmd.Parameters.AddWithValue("@vin", car.Vin);
                        cmd.Parameters.AddWithValue("@wheel_type", car.wheel_type);

                        int done = SQLTracer.TraceNQ(cmd, out _);

                        car.Log($"update tesla_token OK: {done}");
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }

        // Mothership helper methods moved to DBHelper.StateManagement.cs

        internal void UpdateRefreshToken(string? refresh_token)
        {
            try
            {
                if (refresh_token is null || refresh_token.Length < 20)
                {
                    car.Log("SKIP UpdateRefreshToken !!!");
                    return;
                }

                refresh_token = StringCipher.Encrypt(refresh_token);

                car.Log("UpdateRefreshToken");
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("update cars set refresh_token = @refresh_token where id=@id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        cmd.Parameters.AddWithValue("@refresh_token", refresh_token);
                        int done = SQLTracer.TraceNQ(cmd, out _);

                        car.Log($"UpdateRefreshToken OK: {done} - {refresh_token.Substring(0, 20)}xxxxxxxx");
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }

        internal void UpdateCarColumn(string? column, string? value)
        {
            try
            {
                car.Log($"Update {column} / Value: {value}");
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"update cars set {column} = @value where id=@id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        cmd.Parameters.AddWithValue("@value", value);
                        int done = SQLTracer.TraceNQ(cmd, out _);

                        //car.Log($"Update {column} OK: " + done + " - " + value);
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }

        internal virtual void CleanPasswort()
        {
            try
            {
                if (CleanPasswortDone)
                {
                    return;
                }

                // car.Log("CleanPasswort");
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("update cars set tesla_password = '' where id=@id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        int done = SQLTracer.TraceNQ(cmd, out _);

                        // car.Log("CleanPasswort OK: " + done);
                        CleanPasswortDone = true;
                    }
                }

            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }

        internal string? GetFirmwareFromDate(DateTime dateTime)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand("SELECT version FROM car_version where StartDate < @date and CarID=@CarID order by StartDate desc limit 1", con))
                {
                    cmd.Parameters.AddWithValue("@date", dateTime);
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        string version = dr[0].ToString();

                        if (version.Contains(" "))
                        {
                            version = version.Substring(0, version.IndexOf(" ", StringComparison.Ordinal));
                        }

                        return version;
                    }
                }
            }

            return "";
        }













        private void UpdateChargeEnergyAdded(int ChargingStateID, double charge_energy_added)
        {
            try
            {
                if (Double.IsNaN(charge_energy_added))
                {
                    return;
                }

                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    chargingstate
SET
    charge_energy_added = @charge_energy_added
WHERE
    CarID = @CarID
    AND id = @ChargingStateID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@charge_energy_added", charge_energy_added);
                        cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                        int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                        car.Log($"UpdateChargeEnergyAdded({ChargingStateID}): {rowsUpdated} rows updated to charge_energy_added {charge_energy_added}");
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during DBHelper.UpdateChargeEnergyAdded(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.UpdateChargeEnergyAdded()");
            }
        }

        private void UpdateChargeEnergyAdded(int ChargingStateID)
        {
            double charge_energy_added = 0.0;
            bool charge_energy_added_found = false;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    charging.charge_energy_added
FROM
    charging
WHERE
    charging.CarId = @CarID
    AND charging.id >=(
        SELECT
            StartChargingID
        FROM
            chargingstate
        WHERE
            id = @ChargingStateID
    ) AND charging.id <=(
        SELECT
        EndChargingID
        FROM
            chargingstate
        WHERE
            id = @ChargingStateID
    )
ORDER BY
    id ASC", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        double last_charge_energy_added = 0.0;
                        while (dr.Read())
                        {
                            if (double.TryParse(dr[0].ToString(), out double new_charge_energy_added))
                            {
                                if (new_charge_energy_added < last_charge_energy_added)
                                {
                                    Tools.DebugLog($"UpdateChargeEnergyAdded c_e_a dropped from {last_charge_energy_added} to {new_charge_energy_added}");
                                    charge_energy_added += last_charge_energy_added;
                                }
                                last_charge_energy_added = new_charge_energy_added;
                            }
                        }
                        charge_energy_added += last_charge_energy_added;
                        charge_energy_added_found = true;
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Logfile.ExceptionWriter(ex, "UpdateChargeEnergyAdded");
                car.Log(ex.ToString());
            }

            if (charge_energy_added_found)
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
    charge_energy_added = @charge_energy_added
WHERE
    CarID = @CarID
    AND id = @ChargingStateID", con))
                        {
                            cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                            cmd.Parameters.AddWithValue("@charge_energy_added", charge_energy_added);
                            cmd.Parameters.AddWithValue("@ChargingStateID", ChargingStateID);
                            int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                            car.Log($"UpdateChargeEnergyAdded({ChargingStateID}): {rowsUpdated} rows updated to charge_energy_added {charge_energy_added}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    car.CreateExceptionlessClient(ex).Submit();

                    Tools.DebugLog($"Exception during DBHelper.UpdateChargeEnergyAdded(): {ex}");
                    Logfile.ExceptionWriter(ex, "Exception during DBHelper.UpdateChargeEnergyAdded()");
                }
            }
            else
            {
                Tools.DebugLog($"UpdateChargeEnergyAdded error - could not calculate charge_energy_added for ID {ChargingStateID}");
            }
            RecalculateChargeEnergyAdded(ChargingStateID);
        }







        private double GetOdometerFromChargingstate(int openChargingState)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    pos.odometer
FROM
    chargingstate,
    pos
WHERE
    pos.CarID = @CarID
    AND chargingstate.id = @ChargingStateID
    AND chargingstate.Pos = pos.id", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", openChargingState);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read() && dr[0] != DBNull.Value)
                        {
                            if (double.TryParse(dr[0].ToString(), out double odometer))
                            {
                                return odometer;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during CloseChargingState(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during CloseChargingState()");
            }
            return double.NaN;
        }

        private Queue<int> FindOpenChargingStates()
        {
            Queue<int> openChargingStates = new();
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id
FROM
    chargingstate
WHERE
    CarID = @CarID
    AND EndDate IS NULL
ORDER BY
    StartDate ASC", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read() && dr[0] != DBNull.Value)
                        {
                            if (int.TryParse(dr[0].ToString(), out int id))
                            {
                                openChargingStates.Enqueue(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during FindOpenChargingStates(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during FindOpenChargingStates()");
            }
            return openChargingStates;
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        internal static bool IndexExists(string index, string table)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    *
FROM
    information_schema.statistics
WHERE
    table_name = '{table}'
    AND INDEX_NAME ='{index}'", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal void GetLastTrip()
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    *
FROM
    trip
WHERE
    CarID = @carid
ORDER BY
    StartDate DESC
LIMIT 1", con)
                    {
                        CommandTimeout = 6000
                    })
                    {
                        cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);

                        if (dr.Read())
                        {
                            car.CurrentJSON.current_trip_start = (DateTime)dr["StartDate"];
                            car.CurrentJSON.current_trip_end = (DateTime)dr["EndDate"];

                            if (dr["StartKm"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_trip_km_start = Convert.ToDouble(dr["StartKm"], Tools.ciEnUS);
                            }

                            if (dr["EndKm"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_trip_km_end = Convert.ToDouble(dr["EndKm"], Tools.ciEnUS);
                            }

                            if (dr["speed_max"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_trip_max_speed = Convert.ToDouble(dr["speed_max"], Tools.ciEnUS);
                            }

                            if (dr["power_max"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_trip_max_power = Convert.ToDouble(dr["power_max"], Tools.ciEnUS);
                            }

                            if (dr["StartRange"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_trip_start_range = Convert.ToDouble(dr["StartRange"], Tools.ciEnUS);
                            }

                            if (dr["EndRange"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_trip_end_range = Convert.ToDouble(dr["EndRange"], Tools.ciEnUS);
                            }
                        }
                        dr.Close();
                    }

                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    ideal_battery_range_km,
    battery_range_km,
    battery_level,
    lat,
    lng
FROM
    pos
WHERE
    CarID = @CarID
ORDER BY
    id DESC
LIMIT 1", con)
                    {
                        CommandTimeout = 6000
                    })
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr["ideal_battery_range_km"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_ideal_battery_range_km = Convert.ToDouble(dr["ideal_battery_range_km"], Tools.ciEnUS);
                            }

                            if (dr["battery_range_km"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_battery_range_km = Convert.ToDouble(dr["battery_range_km"], Tools.ciEnUS);
                            }

                            if (dr["battery_level"] != DBNull.Value)
                            {
                                car.CurrentJSON.current_battery_level = Convert.ToDouble(dr["battery_level"], Tools.ciEnUS);
                            }

                            if (dr["lat"] != DBNull.Value)
                            {
                                car.CurrentJSON.Latitude = Convert.ToDouble(dr["lat"], Tools.ciEnUS);
                            }

                            if (dr["lng"] != DBNull.Value)
                            {
                                car.CurrentJSON.Longitude = Convert.ToDouble(dr["lng"], Tools.ciEnUS);
                            }
                        }
                        dr.Close();
                    }

                    car.CurrentJSON.CreateCurrentJSON();
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                car.Log(ex.ToString());
            }
        }

        public void CloseDriveState(DateTime EndDate)
        {
            car.Log($"CloseDriveState EndDate: {EndDate:yyyy-MM-dd HH:mm:ss}");


            int StartPos = 0;
            int MaxPosId = GetMaxPosid();
            DateTime StartDate = DateTime.MaxValue;

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    StartPos, StartDate
FROM
    drivestate
WHERE
    EndDate IS NULL
    AND CarID = @carid", con))
                {
                    cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        StartPos = Convert.ToInt32(dr[0], Tools.ciEnUS);
                        StartDate = (DateTime)(dr[1]);

                        car.Log($"CloseDriveState StartDate: {StartDate.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS)} / StartPos: {StartPos} ");
                    }
                    dr.Close();
                }
            }

            int rowsUpdated = 0;

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    drivestate
SET
    EndDate = @EndDate,
    EndPos = @Pos
WHERE
    EndDate IS NULL
    AND CarID = @CarID", con))
                {
                    cmd.Parameters.AddWithValue("@EndDate", EndDate);
                    cmd.Parameters.AddWithValue("@Pos", MaxPosId);
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                    rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                }
            }

            if (rowsUpdated > 0)
            {
                Insert_active_route_energy_at_arrival(MaxPosId, true);
            }

            if (StartPos != 0)
            {
                UpdateDriveStatistics(StartPos, MaxPosId, true);

                if (StartDate != DateTime.MaxValue)
                    UpdateAllPOS_AP_Column(car.CarInDB, StartDate, EndDate);
            }

            car.CurrentJSON.current_driving = false;
            car.CurrentJSON.current_speed = 0;
            car.CurrentJSON.current_power = 0;

            _ = Task.Factory.StartNew(() =>
            {
                if (StartPos > 0)
                {
                    UpdateTripElevation(StartPos, MaxPosId, car, " (Task)");

                    StaticMapService.GetSingleton().Enqueue(car.CarInDB, StartPos, MaxPosId, 0, 0, StaticMapProvider.MapMode.Dark, StaticMapProvider.MapSpecial.None);
                    StaticMapService.CreateParkingMapFromPosid(StartPos);
                    StaticMapService.CreateParkingMapFromPosid(MaxPosId);
                }
            }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        internal static void UpdateTripElevation(int startPosId, int endPosId, Car car, string comment = "")
        {
            if (Geofence.GetInstance().RacingMode)
            {
                return;
            }

            if (startPosId == 0 || endPosId == 0)
            {
                return;
            }

            Logfile.Log($"UpdateTripElevation{comment} start:{startPosId} end:{endPosId}");

            string inhalt = "";
            try
            {
                //SRTM.Logging.LogProvider.SetCurrentLogProvider(SRTM.Logging.Logger.)
                SRTM.SRTMData srtmData = new SRTM.SRTMData(FileManager.GetSRTMDataPath());

                using (DataTable dt = new DataTable())
                {
                    using (MySqlDataAdapter da = new MySqlDataAdapter(@"
SELECT
    id,
    lat,
    lng
FROM
    pos
WHERE
    id >= @startPos
    AND id <= @maxPosId
    AND altitude IS NULL
    AND lat IS NOT NULL
    AND lng IS NOT NULL", DBConnectionstring))
                    {
                        da.SelectCommand.Parameters.AddWithValue("@startPos", startPosId);
                        da.SelectCommand.Parameters.AddWithValue("@maxPosId", endPosId);
                        da.SelectCommand.CommandTimeout = 600;
                        _ = SQLTracer.TraceDA(dt, da);

                        int x = 0;

                        foreach (DataRow dr in dt.Rows)
                        {
                            string sql = null;
                            try
                            {
                                double latitude = (double)dr[1];
                                double longitude = (double)dr[2];

                                if (latitude > 90 || latitude < -90 || longitude > 180 || longitude < -180 || (latitude == 0 && longitude == 0))
                                    continue;

                                int? height = srtmData.GetElevation(latitude, longitude);

                                if (height is not null && height < 8000 && height > -428)
                                {
                                    ExecuteSQLQuery($@"
UPDATE
    pos
SET
    altitude = {height}
WHERE
    id = {dr[0]}");
                                }
                                else
                                {
                                    if (Tools.UseOpenTopoData() && long.TryParse($"{dr[0]}", out long posID))
                                    {
                                        OpenTopoDataService.GetSingleton().Enqueue(posID, latitude, longitude);
                                    }
                                }

                                x++;

                                if (x > 250)
                                {
                                    x = 0;
                                    Logfile.Log($"UpdateTripElevation ID:{dr[0]}");
                                }
                            }
                            catch (Exception ex)
                            {
                                if (car is not null)
                                {
                                    car.CreateExceptionlessClient(ex).Submit();
                                }
                                else
                                {
                                    ex.ToExceptionless().FirstCarUserID().Submit();
                                }

                                Logfile.ExceptionWriter(ex, sql ?? "NULL");
                            }
                        }
                    }
                    dt.Clear();
                }
            }
            catch (Exception ex)
            {
                if (car is not null)
                {
                    car.CreateExceptionlessClient(ex).Submit();
                }
                else
                {
                    ex.ToExceptionless().FirstCarUserID().Submit();
                }

                Logfile.ExceptionWriter(ex, inhalt);
                Logfile.Log(ex.ToString());
            }

            Logfile.Log($"UpdateTripElevation finished start:{startPosId} end:{endPosId}");

            _ = Task.Factory.StartNew(() =>
            {
                if (startPosId > 0 && car is not null)
                {
                    car.DbHelper.UpdateDriveHeightStatistics(startPosId, endPosId);
                }
            }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        internal void UpdateDriveHeightStatistics(int startPosId, int endPosId)
        {
            int driveId = GetDriveStateByStartPosEndPos(startPosId, endPosId);
            if (driveId < 0)
            {
                Tools.DebugLog($"no drivestate found for startPosId:{startPosId} endPosId:{endPosId}");
                return;
            }
            // wait until all pos altitude values are filled
            while (OpenTopoDataService.GetSingleton().QueueLength > 0)
            {
                System.Threading.Thread.Sleep(60000);
            }
            decimal meters_up = decimal.Zero;
            decimal meters_down = decimal.Zero;
            decimal distance_up_km = decimal.Zero;
            decimal distance_down_km = decimal.Zero;
            decimal distance_flat_km = decimal.Zero;
            decimal height_max = decimal.Zero;
            decimal height_min = decimal.Zero;
            decimal odo_start = decimal.Zero;
            decimal odo_end = decimal.Zero;
            decimal last_altitude = decimal.Zero;
            decimal last_odo = decimal.Zero;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT DISTINCT
    altitude,
    odometer
FROM
    pos
WHERE
    id >= @StartPos
    AND id <= @EndPos
    AND CarID = @CarID
    AND CAST(odometer as INT) <> CAST(odometer as DOUBLE)
ORDER BY
    odometer", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@StartPos", startPosId);
                        cmd.Parameters.AddWithValue("@EndPos", endPosId);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read()
                            && decimal.TryParse(dr[0].ToString(), out decimal altitude)
                            && decimal.TryParse(dr[1].ToString(), out decimal odometer)
                            && odo_start == decimal.Zero)
                        {
                            // initialize
                            odo_start = odometer;
                            last_odo = odometer;
                            height_max = altitude;
                            height_min = altitude;
                            last_altitude = altitude;
                        }
                        while (dr.Read())
                        {
                            if (decimal.TryParse(dr[0].ToString(), out decimal altitude)
                                && decimal.TryParse(dr[1].ToString(), out decimal odometer))
                            {
                                if (last_altitude == altitude)
                                {
                                    distance_flat_km += odometer - last_odo;
                                }
                                else if (last_altitude > altitude)
                                {
                                    distance_down_km += odometer - last_odo;
                                    meters_down += last_altitude - altitude;
                                }
                                else if (last_altitude < altitude)
                                {
                                    distance_up_km += odometer - last_odo;
                                    meters_up += altitude - last_altitude;
                                }
                                last_altitude = altitude;
                                last_odo = odometer;
                                odo_end = odometer;
                                if (altitude > height_max)
                                {
                                    height_max = altitude;
                                }
                                if (altitude < height_min)
                                {
                                    height_min = altitude;
                                }
                            }
                        }
                        decimal odo_distance = odo_end - odo_start;
                        decimal computed_distance = distance_down_km + distance_up_km + distance_flat_km;
                        if (computed_distance != 0)
                        {
                            decimal correction_factor = odo_distance / computed_distance;
                            distance_flat_km *= correction_factor;
                            distance_up_km *= correction_factor;
                            distance_down_km *= correction_factor;
                        }
                        Tools.DebugLog($"UpdateDriveHeightStatistics: driveId:{driveId} odo_distance:{odo_distance} computed_distance:{computed_distance} distance_flat_km:{distance_flat_km} distance_up_km:{distance_up_km} distance_down_km:{distance_down_km} meters_up:{meters_up} meters_down:{meters_down} height_max:{height_max} height_min:{height_min}");
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog(ex.ToString());
            }
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE 
    drivestate 
SET 
    meters_up = @meters_up,
    meters_down = @meters_down,
    distance_up_km = @distance_up_km,
    distance_down_km = @distance_down_km,
    distance_flat_km = @distance_flat_km,
    height_max = @height_max,
    height_min = @height_min
WHERE 
    CarID = @CarID
    AND id = @driveid", con))
                    {
                        cmd.Parameters.AddWithValue("@driveid", driveId);
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@meters_up", meters_up);
                        cmd.Parameters.AddWithValue("@meters_down", meters_down);
                        cmd.Parameters.AddWithValue("@distance_up_km", distance_up_km);
                        cmd.Parameters.AddWithValue("@distance_down_km", distance_down_km);
                        cmd.Parameters.AddWithValue("@distance_flat_km", distance_flat_km);
                        cmd.Parameters.AddWithValue("@height_max", height_max);
                        cmd.Parameters.AddWithValue("@height_min", height_min);
                        int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                        car.Log($"UpdateDriveHeightStatistics({driveId}): {rowsUpdated} rows updated");
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Tools.DebugLog($"Exception during DBHelper.UpdateDriveHeightStatistics(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.UpdateDriveHeightStatistics()");
            }
        }

        public void UpdateAllDriveHeightStatistics()
        {
            Tools.DebugLog("UpdateAllDriveHeightStatistics()");
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    StartPos,
    EndPos
FROM
    drivestate
WHERE
    EndPos > StartPos
    AND meters_up IS NULL
    AND CarID = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            if (int.TryParse(dr[0].ToString(), out int startPosId)
                                && int.TryParse(dr[1].ToString(), out int endPosId))
                            {
                                UpdateDriveHeightStatistics(startPosId, endPosId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Tools.DebugLog(ex.ToString());
            }
        }

        public static void UpdateElevationForAllPoints()
        {
            Tools.DebugLog("UpdateElevationForAllPoints()");
            try
            {
                foreach (string f in System.IO.Directory.EnumerateFiles(FileManager.GetSRTMDataPath(), "*.txt"))
                {
                    try
                    {
                        if (new System.IO.FileInfo(f).Length < 100)
                        {
                            Logfile.Log($"Found Empty SRTM File: {f}");

                            System.IO.File.Delete(f);
                        }
                    }
                    catch (Exception ex)
                    {
                        ex.ToExceptionless().FirstCarUserID().Submit();
                        Logfile.Log(ex.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        internal static void UpdateAddress(Car c, int posid)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("select lat, lng from pos where id = @id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", posid);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            double lat = Convert.ToDouble(dr[0], Tools.ciEnUS);
                            double lng = Convert.ToDouble(dr[1], Tools.ciEnUS);
                            dr.Close();

                            WebHelper.ReverseGecocodingAsync(c, lat, lng).ContinueWith(task =>
                            {
                                try
                                {
                                    using (MySqlConnection con2 = new MySqlConnection(DBConnectionstring))
                                    {
                                        con2.Open();
                                        using (MySqlCommand cmd2 = new MySqlCommand(@"
UPDATE
  pos
SET
  address = @adress
WHERE
  id = @id", con2))
                                        {
                                            cmd2.Parameters.AddWithValue("@id", posid);
                                            string address = task.Result;
                                            if (address.Length > 250)
                                                address = address.Substring(0, 250);

                                            cmd2.Parameters.AddWithValue("@adress", address);
                                            if (task.Result.Length > 250)
                                            {
                                                var exl = (new Exception()).ToExceptionless().FirstCarUserID();
                                                exl.AddObject(task.Result, $"ReverseGecocodingAsync returned long (l:{task.Result.Length}) address for lat:{lat} lng:{lng}");
                                                exl.Submit();
                                            }
                                            _ = SQLTracer.TraceNQ(cmd2, out _);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ex.ToExceptionless().FirstCarUserID().Submit();
                                    Logfile.Log(ex.ToString());
                                }
                            }, TaskScheduler.Default);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        DateTime? GetDatumFromPos(int PosId)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand("select Datum from pos where id = @id", con))
                {
                    cmd.Parameters.AddWithValue("@id", PosId);
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        return (DateTime)dr[0];
                    }
                }
            }

            return null;
        }

        public void UpdateDriveStatistics(int startPos, int endPos, bool logging = false)
        {
            try
            {
                Tools.SetThreadEnUS();

                if (logging)
                {
                    car.Log($"UpdateDriveStatistics StartPos: {startPos} - EndPos: {endPos}");
                }

                DateTime? startDT = GetDatumFromPos(startPos);
                DateTime? endDT = GetDatumFromPos(endPos);
                int ap_sec_sum = -1;
                int ap_sec_max = -1;
                double TPMS_FL = -1;
                double TPMS_FR = -1;
                double TPMS_RL = -1;
                double TPMS_RR = -1;


                if (logging)
                {
                    car.Log($"UpdateDriveStatistics startDT: {startDT?.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS)} - endDT: {endDT?.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS)}");
                }

                if (startDT is not null && endDT is not null)
                {
                    GetAutopilotSeconds((DateTime)startDT, (DateTime)endDT, out ap_sec_sum, out ap_sec_max);
                    GetAVG_TPMS((DateTime)startDT, (DateTime)endDT, out TPMS_FL, out TPMS_FR, out TPMS_RL, out TPMS_RR);
                }

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    avg(outside_temp) as outside_temp_avg,
    max(speed) as speed_max,
    max(power) as power_max,
    min(power) as power_min,
    avg(power) as power_avg
FROM
    pos
WHERE
    id BETWEEN @startpos AND @endpos
    AND CarID=@CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@startpos", startPos);
                        cmd.Parameters.AddWithValue("@endpos", endPos);
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.CommandTimeout = 6000;
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            using (MySqlConnection con2 = new MySqlConnection(DBConnectionstring))
                            {
                                con2.Open();
                                using (MySqlCommand cmd2 = new MySqlCommand(@"
                                    UPDATE
                                        drivestate
                                    SET
                                        outside_temp_avg  = @outside_temp_avg,
                                        speed_max = @speed_max,
                                        power_max = @power_max,
                                        power_min = @power_min,
                                        power_avg = @power_avg,
                                        AP_sec_sum = @ap_sec_sum,
                                        AP_sec_max = @AP_sec_max,
                                        TPMS_FL = @TPMS_FL,
                                        TPMS_FR = @TPMS_FR,
                                        TPMS_RL = @TPMS_RL,
                                        TPMS_RR = @TPMS_RR
                                    WHERE
                                        StartPos = @StartPos
                                        AND EndPos = @EndPos  ", con2))
                                {
                                    cmd2.Parameters.AddWithValue("@StartPos", startPos);
                                    cmd2.Parameters.AddWithValue("@EndPos", endPos);

                                    cmd2.Parameters.AddWithValue("@outside_temp_avg", dr["outside_temp_avg"]);
                                    cmd2.Parameters.AddWithValue("@speed_max", dr["speed_max"]);
                                    cmd2.Parameters.AddWithValue("@power_max", dr["power_max"]);
                                    cmd2.Parameters.AddWithValue("@power_min", dr["power_min"]);
                                    cmd2.Parameters.AddWithValue("@power_avg", dr["power_avg"]);

                                    if (ap_sec_sum != -1 && ap_sec_max != -1)
                                    {
                                        cmd2.Parameters.AddWithValue("@ap_sec_sum", ap_sec_sum);
                                        cmd2.Parameters.AddWithValue("@AP_sec_max", ap_sec_max);
                                    }
                                    else
                                    {
                                        cmd2.Parameters.AddWithValue("@ap_sec_sum", DBNull.Value);
                                        cmd2.Parameters.AddWithValue("@AP_sec_max", DBNull.Value);
                                    }

                                    cmd2.Parameters.AddWithValue("@TPMS_FL", TPMS_FL > 0 ? (object)TPMS_FL : (object)DBNull.Value);
                                    cmd2.Parameters.AddWithValue("@TPMS_FR", TPMS_FR > 0 ? (object)TPMS_FR : (object)DBNull.Value);
                                    cmd2.Parameters.AddWithValue("@TPMS_RL", TPMS_RL > 0 ? (object)TPMS_RL : (object)DBNull.Value);
                                    cmd2.Parameters.AddWithValue("@TPMS_RR", TPMS_RR > 0 ? (object)TPMS_RR : (object)DBNull.Value);


                                    _ = SQLTracer.TraceNQ(cmd2, out _);
                                }
                            }
                        }
                    }
                }

                // If Startpos doesn't have an "ideal_battery_rage_km", it will be updated from the first valid dataset
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    *
FROM
    pos
WHERE
    id = @startpos", con))
                    {
                        cmd.Parameters.AddWithValue("@startpos", startPos);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr["ideal_battery_range_km"] == DBNull.Value)
                            {
                                DateTime dt1 = (DateTime)dr["Datum"];
                                dr.Close();

                                using (MySqlCommand cmd2 = new MySqlCommand(@"
SELECT
    *
FROM
    pos
WHERE
    id > @startPos
    AND ideal_battery_range_km IS NOT NULL
    AND battery_level IS NOT NULL
    AND CarID = @CarID
ORDER BY
    id ASC
LIMIT 1", con))
                                {
                                    cmd2.Parameters.AddWithValue("@startPos", startPos);
                                    cmd2.Parameters.AddWithValue("@CarID", car.CarInDB);
                                    dr = SQLTracer.TraceDR(cmd2);

                                    if (dr.Read())
                                    {
                                        DateTime dt2 = (DateTime)dr["Datum"];
                                        TimeSpan ts = dt2 - dt1;

                                        object ideal_battery_range_km = dr["ideal_battery_range_km"];
                                        object battery_level = dr["battery_level"];

                                        if (ts.TotalSeconds < 120)
                                        {
                                            dr.Close();

                                            using (var cmd3 = new MySqlCommand(@"
UPDATE
    pos
SET
    ideal_battery_range_km = @ideal_battery_range_km,
    battery_level = @battery_level
WHERE
    id = @startPos", con))
                                            {
                                                cmd3.Parameters.AddWithValue("@startPos", startPos);
                                                cmd3.Parameters.AddWithValue("@ideal_battery_range_km", ideal_battery_range_km.ToString());
                                                cmd3.Parameters.AddWithValue("@battery_level", battery_level.ToString());
                                                _ = SQLTracer.TraceNQ(cmd3, out _);

                                                car.Log($"Trip from {dt1} ideal_battery_range_km updated!");
                                            }
                                        }
                                        else
                                        {
                                            // Logfile.Log($"Trip from {dt1} ideal_battery_range_km is NULL, but last valid data is too old: {dt2}!");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }


                // If Endpos doesn't have an "ideal_battery_rage_km", it will be updated from the last valid dataset
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    *
FROM
    pos
WHERE
    id = @endpos", con))
                    {
                        cmd.Parameters.AddWithValue("@endpos", endPos);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr["ideal_battery_range_km"] == DBNull.Value)
                            {
                                DateTime dt1 = (DateTime)dr["Datum"];
                                dr.Close();

                                using (var cmd2 = new MySqlCommand(@"
SELECT
    *
FROM
    pos
WHERE
    id < @endpos
    AND ideal_battery_range_km IS NOT NULL
    AND battery_level IS NOT NULL
    AND CarID = @CarID
ORDER BY
    id DESC
LIMIT 1", con))
                                {
                                    cmd2.Parameters.AddWithValue("@endpos", endPos);
                                    cmd2.Parameters.AddWithValue("@CarID", car.CarInDB);
                                    dr = SQLTracer.TraceDR(cmd2);

                                    if (dr.Read())
                                    {
                                        DateTime dt2 = (DateTime)dr["Datum"];
                                        TimeSpan ts = dt1 - dt2;

                                        object ideal_battery_range_km = dr["ideal_battery_range_km"];
                                        object battery_level = dr["battery_level"];

                                        if (ts.TotalSeconds < 120)
                                        {
                                            dr.Close();

                                            using (var cmd3 = new MySqlCommand(@"
UPDATE
    pos
SET
    ideal_battery_range_km = @ideal_battery_range_km,
    battery_level = @battery_level
WHERE
    id = @endpos", con))
                                            {
                                                cmd3.Parameters.AddWithValue("@endpos", endPos);
                                                cmd3.Parameters.AddWithValue("@ideal_battery_range_km", ideal_battery_range_km.ToString());
                                                cmd3.Parameters.AddWithValue("@battery_level", battery_level.ToString());
                                                _ = SQLTracer.TraceNQ(cmd3, out _);

                                                car.Log($"Trip from {dt1} ideal_battery_range_km updated!");
                                            }
                                        }
                                        else
                                        {
                                            // Logfile.Log($"Trip from {dt1} ideal_battery_range_km is NULL, but last valid data is too old: {dt2}!");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }

        private void GetAVG_TPMS(DateTime startDT, DateTime endDT, out double tPMS_FL, out double tPMS_FR, out double tPMS_RL, out double tPMS_RR)
        {
            // car.Log($"GetAVG_TPMS {startDT.ToString()}");
            tPMS_FL = -1;
            tPMS_FR = -1;
            tPMS_RL = -1;
            tPMS_RR = -1;

            if (startDT < new DateTime(2022, 11, 20)) // Feature was introduced later
                return;

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"Select avg(Pressure) as p, Tireid from TPMS 
                        where TPMS.Datum > @startdate and TPMS.Datum < @enddate and TPMS.CarId = @carid group by Tireid", con))
                {
                    cmd.Parameters.AddWithValue("@startdate", startDT.AddHours(-1).ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                    cmd.Parameters.AddWithValue("@enddate", startDT.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                    cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    while (dr.Read())
                    {
                        int Tireid = Convert.ToInt32(dr["Tireid"]);
                        double p = Convert.ToDouble(dr["p"]);
                        switch (Tireid)
                        {
                            case 1: tPMS_FL = p; break;
                            case 2: tPMS_FR = p; break;
                            case 3: tPMS_RL = p; break;
                            case 4: tPMS_RR = p; break;
                        }
                    }
                }
            }
        }

        public void StartDriveState(DateTime now)
        {
            // driving means that charging must be over
            UpdateUnplugDate();
            int posID = 0;

            if (car.FleetAPI) // maxpos in Fleetapi is useless because lat & lng = 0
            {
                posID = car.telemetryParser.lastposid;
                if (posID > 0)
                {
                    DBHelper.UpdateAddress(car, posID);
                    UpdatePosFromCurrentJSON(posID);
                }
            }

            if (posID == 0)
                posID = GetMaxPosid();
            
            if (!InsertDrivestate(now, posID)) // if starting a drive state is failing because of duplicate startposid, the pos will be duplicated and retry
            {
                posID = (int)DBHelper.DuplicatePos(posID);
                car.Log($"DuplicatePos: {posID}");
                if (posID > 0)
                    InsertDrivestate(now, posID);
            }

            Insert_active_route_energy_at_arrival(posID, true);

            car.CurrentJSON.current_driving = true;
            car.CurrentJSON.current_charge_energy_added = 0;
            car.CurrentJSON.current_trip_start = DateTime.Now;
            car.CurrentJSON.current_trip_end = DateTime.MinValue;
            car.CurrentJSON.current_trip_km_start = 0;
            car.CurrentJSON.current_trip_km_end = 0;
            car.CurrentJSON.current_trip_max_speed = 0;
            car.CurrentJSON.current_trip_max_power = 0;
            car.CurrentJSON.current_trip_start_range = 0;
            car.CurrentJSON.current_trip_end_range = 0;

            car.CurrentJSON.CreateCurrentJSON();
        }

        private bool InsertDrivestate(DateTime now, int posID)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("insert drivestate (StartDate, StartPos, CarID, wheel_type) values (@StartDate, @Pos, @CarID, @wheel_type)", con))
                    {
                        cmd.Parameters.AddWithValue("@StartDate", now);
                        cmd.Parameters.AddWithValue("@Pos", posID);
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@wheel_type", car.wheel_type);
                        _ = SQLTracer.TraceNQ(cmd, out _);
                        return true;
                    }
                }
            }
            catch (MySqlException ex)
            {
                if (ex.ErrorCode == -2147467259) // Duplicate entry
                {
                    car.Log(ex.Message);
                    return false;
                }

                car.Log(ex.ToString());
                car.SendException2Exceptionless(ex);
            }
            return false;
        }

        private void UpdatePosFromCurrentJSON(int posID)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
                    UPDATE
                        pos
                    SET
                        power = 1,
                        odometer = @odometer,
                        ideal_battery_range_km = @ideal_battery_range_km,
                        outside_temp = @outside_temp,
                        battery_level = @battery_level,
                        battery_range_km = @battery_range_km
                    WHERE
                        id = @id and power is null and odometer is null", con))
                {
                    cmd.Parameters.AddWithValue("@id", posID);
                    cmd.Parameters.AddWithValue("@odometer", car.CurrentJSON.current_odometer);
                    cmd.Parameters.AddWithValue("@ideal_battery_range_km", car.CurrentJSON.current_ideal_battery_range_km);
                    cmd.Parameters.AddWithValue("@outside_temp", car.CurrentJSON.current_outside_temperature);
                    cmd.Parameters.AddWithValue("@battery_level", car.CurrentJSON.current_battery_level);
                    cmd.Parameters.AddWithValue("@battery_range_km", car.CurrentJSON.current_battery_range_km);
                    int x = SQLTracer.TraceNQ(cmd, out _);

                    car.Log($"UpdatePosFromCurrentJSON {posID} - affected: {x}");
                }
            }
        }

        int last_active_route_energy_at_arrival = int.MinValue;

        private void Insert_active_route_energy_at_arrival(long posID, bool force = false)
        {
            int active_route_energy_at_arrival = int.MinValue;
            if (force)
            {
                last_active_route_energy_at_arrival = -1;
            }
            if (car.GetTeslaAPIState().GetInt("active_route_energy_at_arrival", out active_route_energy_at_arrival))
            {
                if (last_active_route_energy_at_arrival != active_route_energy_at_arrival)
                {
                    try
                    {
                        using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                        {
                            con.Open();
                            using (MySqlCommand cmd = new MySqlCommand(@"
INSERT active_route_energy_at_arrival
(
    posID,
    val
)
VALUES (
    @posID,
    @val
)", con))
                            {
                                cmd.Parameters.AddWithValue("@posID", posID);
                                cmd.Parameters.AddWithValue("@val", active_route_energy_at_arrival);
                                _ = SQLTracer.TraceNQ(cmd, out _);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        car.CreateExceptionlessClient(ex).Submit();
                        car.Log(ex.ToString());
                    }
                    last_active_route_energy_at_arrival = active_route_energy_at_arrival;
                }
            }
        }

        private DateTime lastChargingInsert = DateTime.Today;


        internal void InsertCharging(string? timestamp, string? battery_level, string? charge_energy_added, string? charger_power, double ideal_battery_range, double battery_range, string? charger_voltage, string? charger_phases, string? charger_actual_current, double? outside_temp, bool forceinsert, string? charger_pilot_current, string? charge_current_request)
        {
            Tools.SetThreadEnUS();

            if (charger_phases.Length == 0)
            {
                charger_phases = "1";
            }

            double kmIdeal_Battery_Range = Tools.MlToKm(ideal_battery_range, 1);
            double kmBattery_Range = Tools.MlToKm(battery_range, 1);

            double powerkW = Convert.ToDouble(charger_power, Tools.ciEnUS);

            int power = Convert.ToInt32(charger_power, Tools.ciEnUS);
            int voltage = int.Parse(charger_voltage, Tools.ciEnUS);
            int actual_current = Convert.ToInt32(charger_actual_current, Tools.ciEnUS);
            int requested_current = Convert.ToInt32(charge_current_request, Tools.ciEnUS);
            int current_calculated = CalculateCurrent(actual_current, requested_current);
            int phases_calculated = CalculatePhases(power, voltage, current_calculated);
            int power_calculated = CalculatePower(voltage, phases_calculated, current_calculated);

            // default waitbetween2pointsdb
            double waitbetween2pointsdb = 1000.0 / powerkW;
            // if charging started less than 5 minutes ago, insert one charging data point every ~60 seconds
            try
            {
                // get charging_state, must not be older than 5 minutes = 300 seconds = 300000 milliseconds
                if (car.GetTeslaAPIState().GetState("charging_state", out Dictionary<TeslaAPIState.Key, object> charging_state, 300000))
                {
                    if (charging_state[TeslaAPIState.Key.Value].ToString() == "Charging")
                    {
                        // check if charging_state value Charging is not older than 5 minutes
                        long now = (long)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds;
                        if (long.TryParse(charging_state[TeslaAPIState.Key.ValueLastUpdate].ToString(), out long valueLastUpdate))
                        {
                            if (now - valueLastUpdate < 300000)
                            {
                                // charging_state changed to Charging less than 5 minutes ago
                                // set waitbetween2pointsdb to 15 seconds
                                waitbetween2pointsdb = 15;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Tools.DebugLog("Exception waitbetween2pointsdb", ex);
            }

            double deltaSeconds = (DateTime.Now - lastChargingInsert).TotalSeconds;

            if (forceinsert || deltaSeconds > waitbetween2pointsdb)
            {
                lastChargingInsert = DateTime.Now;

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
INSERT
    charging(
        CarID,
        Datum,
        battery_level,
        charge_energy_added,
        charger_power,
        charger_power_calc_w,
        ideal_battery_range_km,
        battery_range_km,
        charger_voltage,
        charger_phases,
        charger_phases_calc,
        charger_actual_current,
        charger_actual_current_calc,
        outside_temp,
        charger_pilot_current,
        charge_current_request,
        battery_heater
    )
VALUES(
    @CarID,
    @Datum,
    @battery_level,
    @charge_energy_added,
    @charger_power,
    @charger_power_calc_w,
    @ideal_battery_range_km,
    @battery_range_km,
    @charger_voltage,
    @charger_phases,
    @charger_phases_calc,
    @charger_actual_current,
    @charger_actual_current_calc,
    @outside_temp,
    @charger_pilot_current,
    @charge_current_request,
    @battery_heater
)", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@Datum", UnixToDateTime(long.Parse(timestamp, Tools.ciEnUS)).ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                        cmd.Parameters.AddWithValue("@battery_level", battery_level);
                        cmd.Parameters.AddWithValue("@charge_energy_added", charge_energy_added);
                        cmd.Parameters.AddWithValue("@charger_power", charger_power);
                        cmd.Parameters.AddWithValue("@charger_power_calc_w", power_calculated);
                        cmd.Parameters.AddWithValue("@ideal_battery_range_km", kmIdeal_Battery_Range);
                        cmd.Parameters.AddWithValue("@battery_range_km", kmBattery_Range);
                        cmd.Parameters.AddWithValue("@charger_voltage", voltage);
                        cmd.Parameters.AddWithValue("@charger_phases", charger_phases);
                        cmd.Parameters.AddWithValue("@charger_phases_calc", phases_calculated);
                        cmd.Parameters.AddWithValue("@charger_actual_current", charger_actual_current);
                        cmd.Parameters.AddWithValue("@charger_actual_current_calc", current_calculated);
                        cmd.Parameters.AddWithValue("@battery_heater", car.CurrentJSON.current_battery_heater ? 1 : 0);

                        if (charger_pilot_current is not null && int.TryParse(charger_pilot_current, out int i))
                        {
                            cmd.Parameters.AddWithValue("@charger_pilot_current", i);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@charger_pilot_current", DBNull.Value);
                        }

                        if (charge_current_request is not null && int.TryParse(charge_current_request, out i))
                        {
                            cmd.Parameters.AddWithValue("@charge_current_request", i);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@charge_current_request", DBNull.Value);
                        }

                        if (outside_temp is null)
                        {
                            cmd.Parameters.AddWithValue("@outside_temp", DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@outside_temp", (double)outside_temp);
                        }
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }

            try
            {
                if (Convert.ToInt32(battery_level, Tools.ciEnUS) >= 0)
                {
                    car.CurrentJSON.current_battery_level = Convert.ToDouble(battery_level, Tools.ciEnUS);
                }

                car.CurrentJSON.current_charge_energy_added = Convert.ToDouble(charge_energy_added, Tools.ciEnUS);
                if (kmIdeal_Battery_Range >= 0)
                {
                    car.CurrentJSON.current_ideal_battery_range_km = kmIdeal_Battery_Range;
                }

                if (kmBattery_Range >= 0)
                {
                    car.CurrentJSON.current_battery_range_km = kmBattery_Range;
                }
                car.CurrentJSON.current_charger_power = power;
                car.CurrentJSON.current_charger_voltage = voltage;
                car.CurrentJSON.current_charger_actual_current = actual_current;
                car.CurrentJSON.current_charge_current_request = requested_current;
                car.CurrentJSON.current_charger_phases = Convert.ToInt32(charger_phases, Tools.ciEnUS);
                car.CurrentJSON.current_charger_actual_current_calc = current_calculated;
                car.CurrentJSON.current_charger_phases_calc = phases_calculated;
                car.CurrentJSON.current_charger_power_calc_w = power_calculated;
                car.CurrentJSON.CreateCurrentJSON();
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }

        public int GetMaxPosid(bool withReverseGeocoding = true)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    MAX(id)
FROM
    pos
WHERE
    CarID = @CarID", con))
                {
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read() && dr[0] != DBNull.Value)
                    {
                        int pos = Convert.ToInt32(dr[0], Tools.ciEnUS);
                        if (withReverseGeocoding)
                        {
                            UpdateAddress(car, pos);
                        }

                        if (car.FleetAPI)
                            UpdatePosFromCurrentJSON(pos);

                        return pos;
                    }
                }
            }

            return 0;
        }

        public int GetMaxPosidLatLng(out double lat, out double lng)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id,
    lat,
    lng
FROM
    pos
WHERE
    id IN(
        SELECT
            MAX(id)
        FROM
            pos
        WHERE
            CarID = @CarID
    )", con))
                {
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read() && dr[0] != DBNull.Value)
                    {
                        _ = double.TryParse(dr[1].ToString(), out lat);
                        _ = double.TryParse(dr[2].ToString(), out lng);
                        return Convert.ToInt32(dr[0], Tools.ciEnUS);
                    }
                }
            }
            lat = double.NaN;
            lng = double.NaN;
            return 0;
        }

        public int GetMaxChargeid(out DateTime chargeStart, out double? charge_energy_added)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id,
    datum,
    charge_energy_added
FROM
    charging
WHERE
    CarID = @CarID
ORDER BY
    datum DESC
LIMIT 1", con))
                {
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read() && dr[0] != DBNull.Value && dr[1] != DBNull.Value)
                    {
                        if (!DateTime.TryParse(dr[1].ToString(), out chargeStart))
                        {
                            chargeStart = DateTime.Now;
                        }

                        charge_energy_added = Convert.ToDouble(dr[2]);

                        return Convert.ToInt32(dr[0], Tools.ciEnUS);
                    }
                }
            }
            chargeStart = DateTime.Now;
            charge_energy_added = null;
            return 0;
        }

        internal int GetMaxChargingstateId(out double lat, out double lng, out DateTime UnplugDate, out DateTime EndDate)
        {
            UnplugDate = DateTime.MinValue;
            EndDate = DateTime.MinValue;
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    chargingstate.id,
    lat,
    lng,
    UnplugDate,
    EndDate
FROM
    chargingstate
JOIN
    pos
ON
    chargingstate.pos = pos.id
WHERE
    chargingstate.id IN(
    SELECT
        MAX(id)
    FROM
        chargingstate
    WHERE
        carid = @CarID
)", con))
                {
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read() && dr[0] != DBNull.Value && dr[1] != DBNull.Value && dr[2] != DBNull.Value)
                    {
                        if (!double.TryParse(dr[1].ToString(), out lat)) { lat = double.NaN; }
                        if (!double.TryParse(dr[2].ToString(), out lng)) { lng = double.NaN; }
                        if (!DateTime.TryParse(dr[3].ToString(), out UnplugDate)) { UnplugDate = DateTime.MinValue; }
                        if (!DateTime.TryParse(dr[4].ToString(), out EndDate)) { EndDate = DateTime.MinValue; }
                        return Convert.ToInt32(dr[0], Tools.ciEnUS);
                    }
                }
            }
            lat = double.NaN;
            lng = double.NaN;
            return 0;
        }

        internal void SetCarVersion(string? car_version)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
INSERT
    car_version(
        StartDate,
        VERSION,
        CarID
    )
VALUES (
    @StartDate,
    @version,
    @CarID
)", con))
                    {
                        cmd.Parameters.AddWithValue("@StartDate", DateTime.Now);
                        cmd.Parameters.AddWithValue("@version", car_version);
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.ExceptionWriter(ex, car_version);
            }
        }

        internal string? GetLastCarVersion()
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    VERSION
FROM
    car_version
WHERE
    CarId = @CarID
ORDER BY
    id DESC
LIMIT 1", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            return dr[0].ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.ExceptionWriter(ex, "GetLastCarVersion");
                car.Log(ex.ToString());
            }

            return "";
        }

        public static string? GetVersion()
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand("SELECT @@version", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        return dr[0].ToString();
                    }
                }
            }

            return "NULL";
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static bool TableExists(string? table)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    *
FROM
    information_schema.tables
WHERE
    table_name = '{table}'", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static string? GetColumnType(string? table, string? column)
        {
            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    DATA_TYPE
FROM
    INFORMATION_SCHEMA.COLUMNS
WHERE
    table_name = '{table}'
    AND COLUMN_NAME = '{column}'", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    if (dr.Read())
                    {
                        return dr[0].ToString();
                    }
                }
            }

            return string.Empty;
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static bool ColumnExists(string? table, string? column)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"SHOW COLUMNS FROM `{table}` LIKE '{column}';", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            return true;
                        }
                    }
                }
            } catch (MySqlException ex)
            {
                if (ex.Number == 1146)  // Table doesn't exist
                    return false;

                throw;
            }

            return false;
        }

        internal int GetScanMyTeslaSignalsLastWeek()
        {
            string cacheKey = $"GetScanMyTeslaSignalsLastWeek_{car.CarInDB}";
            object cacheValue = MemoryCache.Default.Get(cacheKey);
            if (cacheValue is not null)
            {
                return (int)cacheValue;
            }

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    COUNT(*)
FROM
    can
WHERE
    CarID = @CarID
    AND datum >= DATE(NOW()) - INTERVAL 7 DAY", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            int count = Convert.ToInt32(dr[0], Tools.ciEnUS);

                            MemoryCache.Default.Add(cacheKey, count, DateTime.Now.AddHours(4));
                            return count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.ExceptionWriter(ex, "GetScanMyTeslaPacketsLastWeek");
                car.Log(ex.ToString());
            }
            return 0;
        }

        internal int GetScanMyTeslaPacketsLastWeek()
        {
            string cacheKey = $"GetScanMyTeslaPacketsLastWeek_{car.CarInDB}";
            object cacheValue = MemoryCache.Default.Get(cacheKey);
            if (cacheValue is not null)
            {
                return (int)cacheValue;
            }

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    COUNT(*)
FROM
    (
    SELECT
        COUNT(*) AS cnt
    FROM
        can
    WHERE
        CarID = @CarID
        AND datum >= DATE(NOW()) - INTERVAL 7 DAY
    GROUP BY
        UNIX_TIMESTAMP(datum)) AS T1", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader r = SQLTracer.TraceDR(cmd);
                        if (r.Read())
                        {
                            int count = Convert.ToInt32(r[0], Tools.ciEnUS);

                            MemoryCache.Default.Add(cacheKey, count, DateTime.Now.AddHours(4));
                            return count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.ExceptionWriter(ex, "GetScanMyTeslaPacketsLastWeek");
                car.Log(ex.ToString());
            }
            return 0;
        }

        public int GetAvgMaxRage()
        {
            string cacheKey = $"GetAvgMaxRage_{car.CarInDB}";
            object cacheValue = MemoryCache.Default.Get(cacheKey);
            if (cacheValue is not null)
            {
                return (int)cacheValue;
            }

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    AVG(charging_End.ideal_battery_range_km / charging_End.battery_level * 100) AS 'TRmax'
FROM
    charging
INNER JOIN
    chargingstate
ON
    charging.id = chargingstate.StartChargingID
INNER JOIN
    pos
ON
    chargingstate.pos = pos.id
LEFT OUTER JOIN
    charging AS charging_End
ON
    chargingstate.EndChargingID = charging_End.id
WHERE
    chargingstate.CarID = @CarID
    AND chargingstate.StartDate > SUBDATE(NOW(), INTERVAL 60 DAY)
    AND TIMESTAMPDIFF(MINUTE, chargingstate.StartDate, chargingstate.EndDate) > 3
    AND pos.odometer > 1", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr[0] == DBNull.Value)
                            {
                                MemoryCache.Default.Add(cacheKey, 0, DateTime.Now.AddMinutes(5));
                                return 0;
                            }

                            int count = Convert.ToInt32(dr[0], Tools.ciEnUS);
                            MemoryCache.Default.Add(cacheKey, count, DateTime.Now.AddHours(1));
                            return count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.ExceptionWriter(ex, "GetAvgMaxRage");
                car.Log(ex.ToString());
            }
            return 0;
        }

        virtual public async Task<string?> UpdateCountryCodeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    await con.OpenAsync(cancellationToken).ConfigureAwait(false);
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    lat,
    lng
FROM
    pos
WHERE
    id = (
        SELECT
            MAX(id)
        FROM
            pos
        WHERE
            CarID = @CarID
    )", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        var dr = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                        if (await dr.ReadAsync(cancellationToken).ConfigureAwait(false))
                        {
                            double lat = Convert.ToDouble(dr[0], Tools.ciEnUS);
                            double lng = Convert.ToDouble(dr[1], Tools.ciEnUS);
                            dr.Close();

                            await WebHelper.ReverseGecocodingAsync(car, lat, lng, true, false);
                            return car.CurrentJSON.current_country_code;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }

            return "";
        }

        [SuppressMessage("Security", "CA2100:SQL-Abfragen auf Sicherheitsrisiken überprüfen", Justification = "<Pending>")]
        public void GetAvgConsumption(out double sumkm, out double avgkm, out double kwh100km, out double avgsocdiff, out double maxkm)
        {
            sumkm = 0;
            avgkm = 0;
            kwh100km = 0;
            avgsocdiff = 0;
            maxkm = 0;

            try
            {
                using (DataTable dt = new DataTable())
                {
                    string sql = $@"
SELECT
    SUM(km_diff) AS sumkm,
    AVG(km_diff) AS avgkm,
    AVG(avg_consumption_kwh_100km) AS kwh100km,
    AVG(pos.battery_level - posend.battery_level) AS avgsocdiff,
    AVG(km_diff / (pos.battery_level - posend.battery_level) * 100) AS maxkm
FROM
    trip
JOIN
    pos
ON
    trip.startposid = pos.id
JOIN
    pos AS posend
ON
    trip.endposid = posend.id
WHERE
    km_diff BETWEEN 100 AND 800
    AND pos.battery_level IS NOT NULL
    AND trip.carid = {car.CarInDB};";

                    using (MySqlDataAdapter da = new MySqlDataAdapter(sql, DBConnectionstring))
                    {
                        _ = SQLTracer.TraceDA(dt, da);

                        if (dt.Rows.Count == 1)
                        {
                            var r = dt.Rows[0];

                            if (r["sumkm"] == DBNull.Value)
                            {
                                car.Log($"GetAvgConsumption: nothing found!!!");
                                return;
                            }

                            sumkm = Math.Round((double)r["sumkm"], 1);
                            avgkm = Math.Round((double)r["avgkm"], 1);
                            kwh100km = Math.Round((double)r["kwh100km"], 1);
                            avgsocdiff = Math.Round((double)r["avgsocdiff"], 1);
                            maxkm = Math.Round((double)r["maxkm"], 1);

                            car.Log($"GetAvgConsumption: sumkm: {sumkm}; avgkm: {avgkm}; kwh/100km: {kwh100km}; avgsocdiff: {avgsocdiff}; maxkm: {maxkm}");
                        }
                    }
                    dt.Clear();
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                car.Log(ex.ToString());
            }
        }

        DataTable GetLatestDC_Charging_with_50PercentSOC()
        {
            DataTable dt = new DataTable();
            string sql = @"
SELECT
    c1.Datum AS sd,
    c2.Datum AS ed,
    chargingstate.carid
FROM
    chargingstate
JOIN
    charging c1
ON
    c1.id = startchargingid
JOIN
    charging c2
ON
    c2.id = endchargingid
WHERE
    max_charger_power > 30
    AND c1.battery_level < 50
    AND c2.battery_level > 50
    AND chargingstate.carid = @carid
ORDER BY
    chargingstate.startdate DESC
LIMIT 5";

            using (MySqlDataAdapter da = new MySqlDataAdapter(sql, DBConnectionstring))
            {
                da.SelectCommand.Parameters.AddWithValue("@carid", car.CarInDB);
                _ = SQLTracer.TraceDA(dt, da);
            }

            return dt;
        }

        virtual public double GetVoltageAt50PercentSOC(out DateTime start, out DateTime ende)
        {
            start = DateTime.MinValue;
            ende = DateTime.MinValue;

            try
            {
                using (DataTable dt = GetLatestDC_Charging_with_50PercentSOC())
                {
                    string sql = @"
SELECT
    AVG(charger_voltage)
FROM
    charging
WHERE
    carid = @carid
    AND Datum BETWEEN @start AND @ende
    AND charger_voltage > 300";

                    foreach (DataRow dr in dt.Rows)
                    {
                        using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                        {
                            con.Open();
                            using (MySqlCommand cmd = new MySqlCommand(sql, con))
                            {
                                start = (DateTime)dr["sd"];
                                ende = (DateTime)dr["ed"];

                                cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                                cmd.Parameters.AddWithValue("@start", start);
                                cmd.Parameters.AddWithValue("@ende", ende);
                                object ret = SQLTracer.TraceSc(cmd);

                                if (ret == DBNull.Value)
                                {
                                    continue;
                                }

                                return Convert.ToDouble(ret, Tools.ciEnUS);
                            }
                        }
                    }
                    dt.Clear();
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }
            return 0;
        }

        public static void EnableUTF8mb4()
        {
            // https://mathiasbynens.be/notes/mysql-utf8mb4
            // check database
            Enable_utf8mb4_check_database("teslalogger");
            // check tables
            Enable_utf8mb4_check_tables("teslalogger");
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        private static void Enable_utf8mb4_check_database(string? dbname)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    default_character_set_name,
    default_collation_name
FROM
    information_schema.schemata
WHERE SCHEMA_NAME  = '{dbname}'", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr.HasRows && dr[0] is not null && dr[1] is not null)
                            {
                                if (!dr[0].ToString().Equals("utf8mb4", StringComparison.Ordinal)
                                    || !dr[1].ToString().Equals("utf8mb4_unicode_ci", StringComparison.Ordinal))
                                {
                                    Enable_utf8mb4_alter_database(dbname);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        private static void Enable_utf8mb4_alter_database(string? dbname)
        {
            // ALTER DATABASE database_name CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    Logfile.Log($"ALTER DATABASE {dbname} CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci");
                    UpdateTeslalogger.AssertAlterDB();
                    _ = ExecuteSQLQuery($@"
ALTER DATABASE {dbname}
CHARACTER SET = utf8mb4
COLLATE = utf8mb4_unicode_ci", 300);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        private static void Enable_utf8mb4_check_tables(string? dbname)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    TABLE_NAME,
    TABLE_COLLATION
FROM
    information_schema.TABLES
WHERE
    TABLE_SCHEMA = '{dbname}'
    AND TABLE_TYPE = 'BASE TABLE'", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            if (dr.HasRows && dr[0] is not null && dr[1] is not null)
                            {
                                if (!dr[1].ToString().Equals("utf8mb4_unicode_ci", StringComparison.Ordinal))
                                {
                                    Enable_utf8mb4_alter_table(dbname, dr[0].ToString());
                                }
                                // check columns in table
                                Enable_utf8mb4_check_columns(dbname, dr[0].ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        private static void Enable_utf8mb4_alter_table(string? dbname, string? tablename)
        {
            // ALTER TABLE table_name CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    Logfile.Log($"ALTER TABLE {dbname}.{tablename} CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
                    UpdateTeslalogger.AssertAlterDB();
                    _ = ExecuteSQLQuery($@"
ALTER TABLE {dbname}.{tablename}
CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", 3000);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        private static void Enable_utf8mb4_check_columns(string? dbname, string? tablename)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    COLUMN_NAME,
    CHARACTER_SET_NAME,
    COLLATION_NAME,
    COLUMN_TYPE
FROM
    INFORMATION_SCHEMA.COLUMNS
WHERE
    TABLE_SCHEMA = '{dbname}'
    AND TABLE_NAME = '{tablename}'
    AND DATA_TYPE = 'varchar'", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            if (dr.HasRows && dr[0] is not null && dr[1] is not null && dr[2] is not null)
                            {
                                if (!dr[1].ToString().Equals("utf8mb4", StringComparison.Ordinal)
                                    || !dr[2].ToString().Equals("utf8mb4_unicode_ci", StringComparison.Ordinal))
                                {
                                    Enable_utf8mb4_alter_column(dbname, tablename, dr[0].ToString(), dr[3].ToString());
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        private static void Enable_utf8mb4_alter_column(string? dbname, string? tablename, string? columnname, string? columntype)
        {
            // ALTER TABLE `shiftstate` CHANGE `state` `state` VARCHAR(5) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    Logfile.Log($"ALTER TABLE {dbname}.{tablename} CHANGE {columnname} {columnname} {columntype} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL");
                    UpdateTeslalogger.AssertAlterDB();
                    _ = ExecuteSQLQuery($@"
ALTER TABLE {dbname}.{tablename}
CHANGE {columnname} {columnname} {columntype} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL", 3000);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        internal static void MigrateFloorRound()
        {
            string migrationstatusfile = "migrate_floor_round.txt";

            /*
             * DB stores speed in km/h
             * API has speed in mph
             * rounding takes place twice: car display in km/h -> API speed in mph -> mpt to km/h in TeslaLogger
             * 
             * migrate errors coming from older versions originating from floor() vs. round()
             * 
             */

            if (!File.Exists(migrationstatusfile))
            {
                try
                {
                    StringBuilder migrationlog = new StringBuilder();
                    Logfile.Log("MigrateFloorRound() start");
                    migrationlog.Append($"{DateTime.Now} MigrateFloorRound() start{Environment.NewLine}");

                    // add indexes to speed up things
                    Logfile.Log("MigrateFloorRound() ADD INDEX speed");
                    migrationlog.Append($"{DateTime.Now} ADD INDEX speed{Environment.NewLine}");
                    int sqlresult = ExecuteSQLQuery("ALTER TABLE pos ADD INDEX idx_migration_speed (speed)", 6000);
                    migrationlog.Append($"{DateTime.Now} sqlresult {sqlresult}{Environment.NewLine}");

                    // get max speed

                    int maxspeed_kmh = 0;

                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    MAX(speed)
FROM
    pos", con))
                        {
                            MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                            if (dr.Read() && dr[0] != DBNull.Value)
                            {
                                _ = int.TryParse(dr[0].ToString(), out maxspeed_kmh);
                            }
                        }
                        con.Close();
                    }

                    if (maxspeed_kmh == 0)
                    {
                        maxspeed_kmh = 500;
                    }

                    Logfile.Log($"maxspeed_kmh: {maxspeed_kmh}");
                    migrationlog.Append($"maxspeed_kmh: {maxspeed_kmh}{Environment.NewLine}");

                    // migrate floor round error for pos.speed

                    for (int speed_mph = (int)Math.Round(maxspeed_kmh * 0.62137119223733) + 1; speed_mph > 0; speed_mph--)
                    {
                        int speed_floor = (int)(speed_mph * 1.609344); // old conversion
                        int speed_round = (int)Tools.MphToKmhRounded(speed_mph); // new conversion
                        if (speed_floor != speed_round)
                        {
                            DateTime start = DateTime.Now;
                            Logfile.Log($"MigrateFloorRound(): speed {speed_floor} -> {speed_round}");
                            migrationlog.Append($"{DateTime.Now} speed {speed_floor} -> {speed_round}{Environment.NewLine}");
                            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                            {
                                con.Open();
                                using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    pos
SET
    speed = @speedround
WHERE
    speed = @speedfloor", con))
                                {
                                    cmd.Parameters.Add("speedround", MySqlDbType.Int32).Value = speed_round;
                                    cmd.Parameters.Add("speedfloor", MySqlDbType.Int32).Value = speed_floor;
                                    int updated_rows = SQLTracer.TraceNQ(cmd, out _);
                                    Logfile.Log($" rows updated: {updated_rows} duration: {(DateTime.Now - start).TotalMilliseconds}ms");
                                    migrationlog.Append($"{DateTime.Now} rows updated: {updated_rows} duration: {(DateTime.Now - start).TotalMilliseconds}ms{Environment.NewLine}");
                                }
                                con.Close();
                            }
                        }
                    }

                    // update all drivestate statistics
                    foreach (Car c in Car.Allcars)
                    {
                        using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                        {
                            con.Open();
                            using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    StartPos,
    EndPos
FROM
    drivestate
WHERE
    CarID = @CarID", con))
                            {
                                cmd.Parameters.Add("@CarID", MySqlDbType.UByte).Value = c.CarInDB;
                                MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                                while (dr.Read())
                                {
                                    if (dr[0] is not null && int.TryParse(dr[0].ToString(), out int startpos)
                                        && dr[1] is not null && int.TryParse(dr[1].ToString(), out int endpos))
                                    {
                                        DateTime start = DateTime.Now;
                                        c.DbHelper.UpdateDriveStatistics(startpos, endpos, false);
                                        c.Log($"UpdateDriveStatistics: {startpos} -> {endpos} duration: {(DateTime.Now - start).TotalMilliseconds}ms");
                                        migrationlog.Append($"{DateTime.Now} {c.CarInDB}# UpdateDriveStatistics: {startpos} -> {endpos} duration: {(DateTime.Now - start).TotalMilliseconds}ms{Environment.NewLine}");
                                    }
                                }
                            }
                        }
                    }

                    // remove indexes
                    Logfile.Log("MigrateFloorRound() DROP INDEX speed");
                    migrationlog.Append($"{DateTime.Now} DROP INDEX speed{Environment.NewLine}");
                    sqlresult = ExecuteSQLQuery("ALTER TABLE pos DROP INDEX idx_migration_speed", 6000);
                    migrationlog.Append($"{DateTime.Now} sqlresult {sqlresult}{Environment.NewLine}");

                    // cleanup DB files
                    Logfile.Log("MigrateFloorRound() REBUILD");
                    migrationlog.Append($"{DateTime.Now} REBUILD{Environment.NewLine}");
                    sqlresult = ExecuteSQLQuery("ALTER TABLE pos FORCE", 6000);
                    migrationlog.Append($"{DateTime.Now} sqlresult {sqlresult}{Environment.NewLine}");

                    Logfile.Log("MigrateFloorRound() finished");
                    migrationlog.Append($"{DateTime.Now} MigrateFloorRound() finished{Environment.NewLine}");

                    // persist that migration ran successful to prevent another run
                    File.WriteAllText(migrationstatusfile, migrationlog.ToString());
                }
                catch (Exception ex)
                {
                    ex.ToExceptionless().FirstCarUserID().Submit();
                    Tools.DebugLog("Exception MigrateFloorRound()", ex);
                }
            }
        }

        private void CloseChargingState(int openChargingState)
        {
            object meter_vehicle_kwh_end = DBNull.Value;
            object meter_utility_kwh_end = DBNull.Value;
            object session_price_end = DBNull.Value;

            try
            {
                var v = ElectricityMeterBase.Instance(car);
                if (v is not null)
                {
                    meter_vehicle_kwh_end = v.GetVehicleMeterReading_kWh();
                    meter_utility_kwh_end = v.GetUtilityMeterReading_kWh();
                    session_price_end = v.GetSessionPrice();

                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Logfile.Log(ex.ToString());
            }

            try
            {
                car.Log($"CloseChargingState id:{openChargingState}");
                StaticMapService.CreateChargingMapOnChargingCompleted(car.CarInDB);
                int chargeID = GetMaxChargeid(out DateTime chargeEnd, out double? _);
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    chargingstate
SET
    EndDate = @EndDate,
    EndChargingID = @EndChargingID,
    meter_vehicle_kwh_end = @meter_vehicle_kwh_end,
    meter_utility_kwh_end = @meter_utility_kwh_end,
    meter_vehicle_kwh_sum = @meter_vehicle_kwh_end - meter_vehicle_kwh_start,
    meter_utility_kwh_sum = @meter_utility_kwh_end - meter_utility_kwh_start,
    cost_kwh_meter_invoice = @meter_vehicle_kwh_end - meter_vehicle_kwh_start,
    cost_per_session = @cost_per_session
WHERE
    id = @ChargingStateID
    AND CarID = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@EndDate", chargeEnd);
                        cmd.Parameters.AddWithValue("@EndChargingID", chargeID);
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@ChargingStateID", openChargingState);
                        cmd.Parameters.AddWithValue("@meter_vehicle_kwh_end", meter_vehicle_kwh_end);
                        cmd.Parameters.AddWithValue("@meter_utility_kwh_end", meter_utility_kwh_end);
                        cmd.Parameters.AddWithValue("@cost_per_session", session_price_end);
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();
                Tools.DebugLog($"Exception during CloseChargingState(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during CloseChargingState()");
            }
            if (car.GetTeslaAPIState().GetString("charging_state", out string chargingState) && chargingState == "Disconnected")
            {
                UpdateUnplugDate();
            }
            else
            {
                Tools.DebugLog($"CloseChargingState() charging_state:{chargingState}");
            }

        }

        internal static int GetNextAvailableCarID()
        {
            int newid = 1;
            using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
            {
                con.Open();

                using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    MAX(a) +1
FROM
    (
    SELECT
        MAX(id) AS a
    FROM
        cars
    UNION ALL
    SELECT
        MAX(carid) AS a
    FROM
        pos
) AS t", con))
                {
                    object oid = SQLTracer.TraceSc(cmd);
                    if (oid is not null)
                    {
                        newid = Convert.ToInt32(oid);
                    }
                }
            }
            return newid;
        }

        internal static decimal InsertNewCar(string? email, string? password, int teslacarid, bool freesuc, string? access_token, string? refresh_token, string? vin, string? display_name, bool fleetAPI)
        {
            Logfile.Log($"Insert new Car: {display_name}, VIN: {vin}, TeslaCarId: {teslacarid}");
            int newid = GetNextAvailableCarID();
            using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
            {
                using (var cmd2 = new MySqlCommand("insert cars (id, tesla_name, tesla_password, tesla_carid, display_name, freesuc, tesla_token, refresh_token, vin, fleetAPI) values (@id, @tesla_name, @tesla_password, @tesla_carid, @display_name, @freesuc,  @tesla_token, @refresh_token, @vin, @fleetAPI)", con))
                {
                    cmd2.Parameters.AddWithValue("@id", newid);
                    cmd2.Parameters.AddWithValue("@tesla_name", email);
                    cmd2.Parameters.AddWithValue("@tesla_password", password);
                    cmd2.Parameters.AddWithValue("@tesla_carid", teslacarid);
                    cmd2.Parameters.AddWithValue("@display_name", display_name);
                    cmd2.Parameters.AddWithValue("@freesuc", freesuc ? 1 : 0);
                    cmd2.Parameters.AddWithValue("@tesla_token", access_token);
                    cmd2.Parameters.AddWithValue("@refresh_token", refresh_token);
                    cmd2.Parameters.AddWithValue("@vin", vin);
                    cmd2.Parameters.AddWithValue("@fleetAPI", fleetAPI);
                    _ = SQLTracer.TraceNQ(cmd2, out _);

#pragma warning disable CA2000 // Objekte verwerfen, bevor Bereich verloren geht
                    Car nc = new Car(Convert.ToInt32(newid), email, password, teslacarid, access_token, DateTime.Now, "", "", "", "", display_name, vin, "", null, fleetAPI);
#pragma warning restore CA2000 // Objekte verwerfen, bevor Bereich verloren geht
                }
            }
            return newid;
        }

        internal static async Task UpdateCO2Async()
        {
            try
            {
                Logfile.Log("UpdateCO2");

                CO2 co2 = new CO2();

                double lastLat = 0;
                double lastLng = 0;
                string lastCountry = "";
                string lastAddress = "";

                string calculateCountry = "";
                DateTime? calculateDate = null;

                var dt = GetAllChargingstates();
                foreach (DataRow dr in dt.Rows)
                {
                    await Task.Delay(10).ConfigureAwait(false);

                    calculateCountry = "";
                    calculateDate = null;

                    try
                    {

                        if (dr["country"] == DBNull.Value)
                        {
                            double lat = Convert.ToDouble(dr["lat"]);
                            double lng = Convert.ToDouble(dr["lng"]);
                            string address = dr["address"].ToString();
                            string country = "";
                            DateTime dateTime = (DateTime)dr["StartDate"];

                            var age = DateTime.Now - dateTime;
                            if (age.TotalHours < 5) // current data might not be available
                                continue;

                            if (!String.IsNullOrEmpty(lastCountry))
                            {
                                double distance = Geofence.GetDistance(lastLng, lastLat, lng, lat);
                                if (distance < 5000)
                                {
                                    country = lastCountry;
                                    Logfile.Log($"country from last pos: {country} - distance: {distance}m - Address 1: {lastAddress} / Address 2: {address}");
                                }
                            }

                            if (String.IsNullOrEmpty(country))
                                country = WebHelper.ReverseGecocodingCountryAsync(lat, lng).Result;

                            if (!String.IsNullOrEmpty(country))
                            {
                                int id = Convert.ToInt32(dr["id"]);
                                dr["country"] = country;
                                dr.AcceptChanges();

                                calculateCountry = country;
                                calculateDate = dateTime;

                                int c = await co2.GetDataAsync(country, dateTime);
                                UpdateChargingStateCountryCO2(id, country, c);

                                lastLat = lat;
                                lastLng = lng;
                                lastCountry = country;
                                lastAddress = address;
                            }
                        }
                        else
                        {
                            if (dr["country"].ToString().Length > 0)
                            {
                                lastLat = Convert.ToDouble(dr["lat"]);
                                lastLng = Convert.ToDouble(dr["lng"]);
                                lastCountry = dr["country"].ToString();
                                lastAddress = dr["address"].ToString();

                                if (dr["co2_g_kWh"] == DBNull.Value)
                                {
                                    int id = Convert.ToInt32(dr["id"]);
                                    DateTime dateTime = (DateTime)dr["StartDate"];

                                    calculateCountry = lastCountry;
                                    calculateDate = dateTime;

                                    int c = await co2.GetDataAsync(lastCountry, dateTime);
                                    if (c > 0)
                                    {
                                        UpdateChargingStateCountryCO2(id, lastCountry, c);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logfile.Log(ex.ToString());
                        ex.ToExceptionless().FirstCarUserID().AddObject(calculateCountry).AddObject(calculateDate).Submit();
                    }
                }

                Logfile.Log("UpdateCO2 finish");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
            }
        }

        internal int FindChargingStateIDByStartDate(DateTime dtStart)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id
FROM
    chargingstate
WHERE
    CarID = @CarID
    AND ABS(TIMEDIFF(StartDate, @DTStart)) < 1800
ORDER BY
    ABS(TIMEDIFF(StartDate, @DTStart)) ASC
LIMIT 1
", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@DTStart", dtStart.ToString("yyyy-MM-dd HH:mm:ss"));
                        if (int.TryParse(SQLTracer.TraceSc(cmd).ToString(), out int chargingstateid))
                        {
                            return chargingstateid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();

                Tools.DebugLog($"Exception during DBHelper.FindChargingSessionByStartDate(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.FindChargingSessionByStartDate()");
            }
            return -1;
        }

        internal static string GetSuCNameFromChargingStateID(int chargingstateid)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    pos.address
FROM
    pos
JOIN
    chargingstate ON chargingstate.pos = pos.id
WHERE
    chargingstate.id = @chargingid
", con))
                    {
                        cmd.Parameters.AddWithValue("@chargingid", chargingstateid);
                        return (string)SQLTracer.TraceSc(cmd);
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();

                Tools.DebugLog($"Exception during DBHelper.FindChargingSessionByStartDate(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.FindChargingSessionByStartDate()");
            }
            return string.Empty;
        }

        internal List<int> GetSuCChargingStatesWithEmptySessionId()
        {
            List<int> resultList = new();
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
	chargingstate.id
FROM
	chargingstate
JOIN pos on
	chargingstate.pos = pos.id
WHERE
	chargingstate.sessionId IS NULL
	AND chargingstate.CarID = @CarID
	AND (
    	(chargingstate.fast_charger_brand = @brand
		AND (chargingstate.fast_charger_type = @type1
			OR chargingstate.fast_charger_type = @type2))
	OR 
    	(pos.address LIKE '%Supercharger%')
    	)
", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@brand", "Tesla");
                        cmd.Parameters.AddWithValue("@type1", "Tesla");
                        cmd.Parameters.AddWithValue("@type2", "Combo");
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            if (dr[0] is not null && int.TryParse(dr[0].ToString(), out int id))
                            {
                                resultList.Add(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Tools.DebugLog($"Exception during DBHelper.GetSuCChargingStatesWithEmptySessionId(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.GetSuCChargingStatesWithEmptySessionId()");
            }
            Tools.DebugLog($"GetSuCChargingStatesWithEmptySessionId #{car.CarInDB}:{resultList.Count}");
            return resultList;
        }

        internal List<int> GetSuCChargingStatesWithSessionId()
        {
            List<int> resultList = new();
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id
FROM
    chargingstate
WHERE
    sessionId IS NOT NULL
    AND CarID = @CarID
    AND fast_charger_brand = @brand
    AND (fast_charger_type = @type1 OR fast_charger_type = @type2)
", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@brand", "Tesla");
                        cmd.Parameters.AddWithValue("@type1", "Tesla");
                        cmd.Parameters.AddWithValue("@type2", "Combo");
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            if (dr[0] is not null && int.TryParse(dr[0].ToString(), out int id))
                            {
                                resultList.Add(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Tools.DebugLog($"Exception during DBHelper.GetSuCChargingStatesWithSessionId(): {ex}");
                Logfile.ExceptionWriter(ex, "Exception during DBHelper.GetSuCChargingStatesWithSessionId()");
            }
            Tools.DebugLog($"GetSuCChargingStatesWithSessionId #{car.CarInDB}:{resultList.Count}");
            return resultList;
        }

        internal static void MigratePosOdometerNullValues()
        {
            Tools.DebugLog("MigratePosOdometerNullValues start");
            try
            {
                if (KVS.Get("MigratePosOdometerNullValuesMaxPosID", out int migratePosOdometerNullValuesMaxPosID) == KVS.NOT_FOUND)
                {
                    migratePosOdometerNullValuesMaxPosID = 0;
                }
                int maxPosID = migratePosOdometerNullValuesMaxPosID;
                // find all pos.id where odometer IS NULL
                List<Tuple<int, int>> IDs = new();
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    id,
    carid
FROM
    pos
WHERE
    id > @migratePosOdometerNullValuesMaxPosID
    AND odometer IS NULL", con))
                    {
                        cmd.CommandTimeout = 6000;
                        cmd.Parameters.AddWithValue("@migratePosOdometerNullValuesMaxPosID", migratePosOdometerNullValuesMaxPosID);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read() && dr[0] != DBNull.Value
                            && dr[1] != DBNull.Value)
                        {
                            if (int.TryParse(dr[0].ToString(), out int posid)
                                && int.TryParse(dr[1].ToString(), out int carid))
                            {
                                IDs.Add(new Tuple<int, int>(posid, carid));
                                maxPosID = Math.Max(posid, maxPosID);
                            }
                        }
                    }
                }
                Tools.DebugLog($"MigratePosOdometerNullValues IDs:{IDs.Count}");
                if (IDs.Count == 0)
                {
                    using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    MAX(id)
FROM
    pos", con))
                        {
                            cmd.CommandTimeout = 6000;
                            maxPosID = (int)SQLTracer.TraceSc(cmd);
                        }
                    }
                }
                else if (IDs.Count > 0)
                {
                    foreach (Tuple<int, int> ID in IDs)
                    {
                        Tools.DebugLog($"MigratePosOdometerNullValues Tuple:{ID.Item1},{ID.Item2}");
                        double lowerOdo = 0;
                        double higherOdo = 0;
                        double odo = 0;
                        //find nearest pos with odometer NOT NULL lower than posid
                        using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                        {
                            con.Open();
                            using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    odometer
FROM
    pos
WHERE
    odometer IS NOT NULL
    AND CarID = @CarID
    AND id < @posid
ORDER BY
    id DESC
LIMIT 1", con))
                            {
                                cmd.Parameters.AddWithValue("@CarID", ID.Item2);
                                cmd.Parameters.AddWithValue("@posid", ID.Item1);
                                cmd.CommandTimeout = 6000;
                                MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                                if (dr.Read() && dr[0] != DBNull.Value)
                                {
                                    if (double.TryParse(dr[0].ToString(), out double lodo)
                                        && lodo > 0)
                                    {
                                        Tools.DebugLog($"MigratePosOdometerNullValues lowerOdo: {lodo}");
                                        lowerOdo = lodo;
                                    }
                                }
                            }
                        }
                        //find nearest pos with odometer NOT NULL higher than posid
                        using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                        {
                            con.Open();
                            using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    odometer
FROM
    pos
WHERE
    odometer IS NOT NULL
    AND CarID = @CarID
    AND id > @posid
ORDER BY
    id ASC
LIMIT 1", con))
                            {
                                cmd.Parameters.AddWithValue("@CarID", ID.Item2);
                                cmd.Parameters.AddWithValue("@posid", ID.Item1);
                                cmd.CommandTimeout = 6000;
                                MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                                if (dr.Read() && dr[0] != DBNull.Value)
                                {
                                    if (double.TryParse(dr[0].ToString(), out double hodo)
                                        && hodo > 0)
                                    {
                                        Tools.DebugLog($"MigratePosOdometerNullValues higherOdo:{hodo}");
                                        higherOdo = hodo;
                                    }
                                }
                            }
                        }
                        // compute average from lower and higher or just take lower?
                        if (lowerOdo > 0 && higherOdo > 0)
                        {
                            odo = (lowerOdo + higherOdo) / 2;
                        }
                        else
                        {
                            odo = lowerOdo;
                        }
                        if (odo > 0)
                        {
                            // update pos.odometer
                            Tools.DebugLog($"MigratePosOdometerNullValues id:{ID.Item1} new odo:{odo}");
                            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                            {
                                con.Open();
                                using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    pos
SET
    odometer = @odometer
WHERE
    id = @id", con))
                                {
                                    cmd.Parameters.AddWithValue("@id", ID.Item1);
                                    cmd.Parameters.AddWithValue("@odometer", odo);
                                    int rowsUpdated = SQLTracer.TraceNQ(cmd, out _);
                                    Tools.DebugLog($"MigratePosOdometerNullValues id:{ID.Item1} new odo:{odo} updated:{rowsUpdated}");
                                }
                            }
                        }
                    }
                }
                KVS.InsertOrUpdate("MigratePosOdometerNullValuesMaxPosID", maxPosID);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        internal bool CheckVirtualKey()
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("SELECT needVirtualKey, virtualkey FROM cars where id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr[0] == DBNull.Value)
                                return false;

                            if (dr["needVirtualKey"].ToString() == "0" && dr["virtualKey"].ToString() == "1")
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            return false;
        }

        internal bool GetRegion()
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("SELECT fleetAPIaddress FROM cars where id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr[0] == DBNull.Value)
                                return false;

                            string url = dr[0].ToString();
                            if (String.IsNullOrEmpty(url)) 
                                return false;

                            car.FleetApiAddress = url;
                            car.Log($"FleetApiAddress: {url}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            return false;
        }

        public bool GetAutopilotSeconds(DateTime start, DateTime end, out int sumsec, out int maxsec)
        {
            // car.Log("GetAutopilotSeconds");
            sumsec = -1;
            maxsec = -1;
            try
            {
                if (start < new DateTime(2024, 2, 20)) // feature introduced later
                    return false;

                var svStart = start.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS);
                var svEnd = end.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS);

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring+ ";Allow User Variables=True"))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"select date, state from cruisestate
                          where carid={car.CarInDB} and date between '{svStart}' and '{svEnd}' and state in (-1,0,1) order by date", con))
                    {
                        // car.Log("SQL: " + cmd.CommandText);

                        DateTime? startstate = null;
                        double sum = 0;
                        double max = 0;
                        int count = 0;

                        var dr = cmd.ExecuteReader();
                        while (dr.Read())
                        {
                            count++;
                            DateTime date = dr.GetDateTime(0);
                            int state = dr.GetInt32(1);

                            if (startstate is null && state == 1)
                            {
                                startstate = date;
                            }
                            else if (startstate is not null &&  state != 1)
                            {
                                TimeSpan ts = date - (DateTime)startstate;
                                double tssec = ts.TotalSeconds;

                                sum += tssec;
                                max = Math.Max(tssec, max);

                                startstate = null;
                                // System.Diagnostics.Debug.WriteLine("Sec: " + tssec);
                            }
                        }

                        if (count > 0)
                        {
                            sumsec = (Int32)sum;
                            maxsec = (Int32)max;
                            return true;
                        }
                    }
                }
            } catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                ex.ToExceptionless().Submit();
            }

            return false;
        }

        internal static void UpdateAllPOS_AP_Column(int carid, DateTime start, DateTime end)
        {
            try
            {
                Tools.SetThreadEnUS();

                using (MySqlConnection con = new MySqlConnection($"{DBConnectionstring};Allow User Variables=True"))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(
@"SELECT 
    T1.date AS startdate,
    T1.state AS startstate,
    T2.date AS enddate,
    T2.state AS endstate
FROM
    (SELECT 
        (@rowid1:=@rowid1 + 1) T1rid, carid, date, state
    FROM
        cruisestate
    JOIN (SELECT @rowid1:=0) a) T1
        LEFT JOIN
    (SELECT 
        (@rowid2:=@rowid2 + 1) T2rid, date, state, carid
    FROM
        cruisestate
    JOIN (SELECT @rowid2:=0) b) T2 ON T1.T1rid + 1 = T2.T2rid
WHERE
    T1.carid = @carid
        AND T1.date BETWEEN @start AND @end
        AND T2.date BETWEEN @start AND @end
ORDER BY startdate", con))
                    {
                        cmd.Parameters.AddWithValue("@carid", carid);
                        cmd.Parameters.AddWithValue("@start", start.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                        cmd.Parameters.AddWithValue("@end", end.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));

                        var dr = cmd.ExecuteReader();
                        while (dr.Read())
                        {
                            try
                            {
                                using (MySqlConnection con2 = new MySqlConnection(DBConnectionstring))
                                {
                                    int state = Convert.ToInt32(dr["startstate"]);
                                    DateTime startstate = (DateTime)dr["startdate"];
                                    DateTime endstate = (DateTime)dr["enddate"];

                                    con2.Open();
                                    using (MySqlCommand cmd2 = new MySqlCommand(@"update pos set ap=@ap where carid=@carid and datum between @start and @end", con2))
                                    {
                                        cmd2.Parameters.AddWithValue("@carid", carid);
                                        cmd2.Parameters.AddWithValue("@ap", state);
                                        cmd2.Parameters.AddWithValue("@start", startstate.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                                        cmd2.Parameters.AddWithValue("@end", endstate.ToString("yyyy-MM-dd HH:mm:ss", Tools.ciEnUS));
                                        cmd2.ExecuteNonQuery();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logfile.Log(ex.ToString());
                                ex.ToExceptionless().FirstCarUserID().Submit();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                ex.ToExceptionless().FirstCarUserID().Submit();
            }
        }

        internal static long DuplicatePos(int id)
        {
             string sql = $@"INSERT INTO `pos` (`Datum`,`lat`,`lng`,`speed`,`power`,`odometer`,`ideal_battery_range_km`,`address`,`outside_temp`,`altitude`,`battery_level`,`inside_temp`,`battery_heater`,`is_preconditioning`,`sentry_mode`,`battery_range_km`,`CarID`,`AP`) 
                select now() ,`lat`,`lng`,`speed`,`power`,`odometer`,`ideal_battery_range_km`,`address`,`outside_temp`,`altitude`,`battery_level`,`inside_temp`,`battery_heater`,`is_preconditioning`,`sentry_mode`,`battery_range_km`,`CarID`,`AP`
                from pos
                where id = {id};";

            using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(sql, con))
                {
                    cmd.ExecuteNonQuery();
                    return cmd.LastInsertedId;
                }
            }
        }

        internal double GetDrivenKm(DateTime startDate, DateTime endDate)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection($"{DBConnectionstring};Allow User Variables=True"))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"SELECT max(odometer) - min(odometer)  
                        FROM pos where carid = @carid and 
                        Datum between @startdate and @enddate", con))
                    {
                        cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                        cmd.Parameters.AddWithValue("@startdate", startDate);
                        cmd.Parameters.AddWithValue("@enddate", endDate);
                        var odo = cmd.ExecuteScalar();
                        if (odo is not null && double.TryParse(odo.ToString(), out double drivenkm))
                        {
                            return drivenkm;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                ex.ToExceptionless().Submit();
            }
            return 0;
        }

        internal void InsertCan(int id, double val)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"INSERT INTO can
                        (datum, id,val,CarID)
                        VALUES
                        (@datum, @id, @val, @carid)", con))
                    {
                        cmd.Parameters.AddWithValue("@datum", DateTime.Now);
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@val", val);
                        cmd.Parameters.AddWithValue("@carid", car.CarInDB);
                        _ = SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                ex.ToExceptionless().Submit();
            }
        }
    }
}


