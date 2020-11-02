using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;

namespace MockServer
{
    public class Database
    {
        // TODO read from mockserversettings.json
        internal static string DBConnectionstring = "Server=teslalogger;Database=teslaloggermock;Uid=root;Password=teslalogger;CharSet=utf8mb4;";

        public Database()
        {
        }

        internal static void ImportJSONFile(FileInfo file, FileInfo first, int sessionid)
        {
            Tools.Log($"ImportJSONFile {file.Name}");

            switch (Tools.ExtractEndpointFromJSONFileName(file))
            {
                case "charge_state":
                    ImportChargeState(file, Tools.ExtractTimestampFromJSONFileName(first));
                    break;
                case "climate_state":
                    ImportClimateState(file, Tools.ExtractTimestampFromJSONFileName(first));
                    break;
                case "drive_state":
                    ImportDriveState(file, Tools.ExtractTimestampFromJSONFileName(first));
                    break;
                case "vehicle_state":
                    ImportVehicleState(file, Tools.ExtractTimestampFromJSONFileName(first));
                    break;
                case "vehicles":
                    ImportVehicles(file, Tools.ExtractTimestampFromJSONFileName(first));
                    break;
            }
        }

        internal static async Task<int> CreateSessionAsync()
        {
            int? newsessionid = DBTools.GetMaxValue("ms_sessions", "sessionid").Result;
            if (newsessionid == null)
            {
                newsessionid = 1;
            }
            else
            {
                newsessionid += 1;
            }
            try
            {
                using (MySqlConnection conn = new MySqlConnection(Database.DBConnectionstring))
                {
                    await conn.OpenAsync();
                    using (MySqlCommand cmd = new MySqlCommand($"INSERT ms_sessions (sessionid) VALUES (@sessionid)", conn))
                    {
                        cmd.Parameters.AddWithValue("@sessionid", newsessionid);
                        Tools.Log(cmd);
                        int rows = cmd.ExecuteNonQueryAsync().Result;
                        if (rows > 0)
                        {
                            return (int)newsessionid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
            return -1;
        }

        private static void ImportChargeState(FileInfo _file, string _tsoffset)
        {
            /*
                 * {"response":
                 *     {
                 *      "battery_heater_on":false,
                 *      "battery_level":51,
                 *      "battery_range":148.56,
                 *      "charge_current_request":16,
                 *      "charge_current_request_max":16,
                 *      "charge_enable_request":true,
                 *      "charge_energy_added":0.0,
                 *      "charge_limit_soc":85,
                 *      "charge_limit_soc_max":100,
                 *      "charge_limit_soc_min":50,
                 *      "charge_limit_soc_std":90,
                 *      "charge_miles_added_ideal":0.0,
                 *      "charge_miles_added_rated":0.0,
                 *      "charge_port_cold_weather_mode":null,
                 *      "charge_port_door_open":false,
                 *      "charge_port_latch":"Blocking",
                 *      "charge_rate":0.0,
                 *      "charge_to_max_range":false,
                 *      "charger_actual_current":0,
                 *      "charger_phases":null,
                 *      "charger_pilot_current":16,
                 *      "charger_power":0,
                 *      "charger_voltage":0,
                 *      "charging_state":"Disconnected",
                 *      "conn_charge_cable":"<invalid>",
                 *      "est_battery_range":142.78,
                 *      "fast_charger_brand":"<invalid>",
                 *      "fast_charger_present":false,
                 *      "fast_charger_type":"<invalid>",
                 *      "ideal_battery_range":118.85,
                 *      "managed_charging_active":false,
                 *      "managed_charging_start_time":null,
                 *      "managed_charging_user_canceled":false,
                 *      "max_range_charge_counter":1,
                 *      "minutes_to_full_charge":0,
                 *      "not_enough_power_to_heat":false,
                 *      "scheduled_charging_pending":false,
                 *      "scheduled_charging_start_time":null,
                 *      "time_to_full_charge":0.0,
                 *      "timestamp":1598862369327,
                 *      "trip_charging":false,
                 *      "usable_battery_level":51,
                 *      "user_charge_enable_request":null
                 *     }
                 * }
                 */
            try
            {
                Dictionary<string, object> r2 = Tools.ExtractResponse(File.ReadAllText(_file.FullName));
                if (r2.ContainsKey("timestamp") && long.TryParse(r2["timestamp"].ToString(), out _))
                {
                    foreach (string key in r2.Keys.Where(k => k != null))
                    {
                        if (r2[key] != null)
                        {
                            switch (key)
                            {
                                case "timestamp":
                                    // apply offset
                                    if (long.TryParse(r2[key].ToString(), out long timestamp) && long.TryParse(_tsoffset, out long tsoffset))
                                    {
                                        long tsvalue = timestamp - tsoffset;
                                    }
                                    break;
                                default:
                                    Tools.Log($"key {key} value {r2[key]} typeof {DBTools.TypeToDBType(r2[key].GetType())}");
                                    break;
                            }
                        }
                        else
                        {
                            // TODO handle nulls
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Tools.Log("Exception", ex);
            }
        }

        private static void ImportClimateState(FileInfo file, string v)
        {
            /*
                 * {"response":
                 *     {
                 *      "battery_heater":false,
                 *      "battery_heater_no_power":false,
                 *      "climate_keeper_mode":"off",
                 *      "defrost_mode":0,
                 *      "driver_temp_setting":21.0,
                 *      "fan_status":0,
                 *      "inside_temp":26.5,
                 *      "is_auto_conditioning_on":false,
                 *      "is_climate_on":false,
                 *      "is_front_defroster_on":false,
                 *      "is_preconditioning":false,
                 *      "is_rear_defroster_on":false,
                 *      "left_temp_direction":-267,
                 *      "max_avail_temp":28.0,
                 *      "min_avail_temp":15.0,
                 *      "outside_temp":15.5,
                 *      "passenger_temp_setting":21.0,
                 *      "remote_heater_control_enabled":false,
                 *      "right_temp_direction":-267,
                 *      "seat_heater_left":0,
                 *      "seat_heater_rear_center":0,
                 *      "seat_heater_rear_left":0,
                 *      "seat_heater_rear_right":0,
                 *      "seat_heater_right":0,
                 *      "side_mirror_heaters":false,
                 *      "steering_wheel_heater":false,
                 *      "timestamp":1598862369248,
                 *      "wiper_blade_heater":false
                 *     }
                 * }
                 */
        }

        private static void ImportDriveState(FileInfo file, string v)
        {
            /*
                 * {"response":
                 *     {
                 *      "gps_as_of":1599039106,
                 *      "heading":253,
                 *      "latitude":123.577843,
                 *      "longitude":123.314109,
                 *      "native_latitude":123.577843,
                 *      "native_location_supported":1,
                 *      "native_longitude":123.314109,
                 *      "native_type":"wgs",
                 *      "power":0,
                 *      "shift_state":null,
                 *      "speed":null,
                 *      "timestamp":1599039108406
                 *     }
                 * }
                 */
        }

        private static void ImportVehicleState(FileInfo file, string v)
        {
            /*
                 * {"response":
                 *     {
                 *      "api_version":10,
                 *      "autopark_state_v2":"ready",
                 *      "autopark_style":"dead_man",
                 *      "calendar_supported":true,
                 *      "car_version":"2020.32.3 b9bd4364fd17",
                 *      "center_display_state":0,
                 *      "df":0,
                 *      "dr":0,
                 *      "ft":0,
                 *      "homelink_device_count":0,
                 *      "homelink_nearby":false,
                 *      "is_user_present":false,
                 *      "last_autopark_error":"no_error",
                 *      "locked":true,
                 *      "media_state":
                 *          {
                 *           "remote_control_enabled":true
                 *          },
                 *      "notifications_supported":true,
                 *      "odometer":47743.589221,
                 *      "parsed_calendar_supported":true,
                 *      "pf":0,
                 *      "pr":0,
                 *      "remote_start":false,
                 *      "remote_start_enabled":false,
                 *      "remote_start_supported":true,
                 *      "rt":0,
                 *      "smart_summon_available":false,
                 *      "software_update":
                 *         {
                 *          "download_perc":0,
                 *          "expected_duration_sec":2700,
                 *          "install_perc":1,
                 *          "status":"",
                 *          "version":""
                 *         },
                 *      "speed_limit_mode":
                 *         {
                 *          "active":false,
                 *          "current_limit_mph":85.0,
                 *          "max_limit_mph":90,
                 *          "min_limit_mph":50,
                 *          "pin_code_set":false
                 *         },
                 *      "summon_standby_mode_enabled":false,
                 *      "sun_roof_percent_open":0,
                 *      "sun_roof_state":"closed",
                 *      "timestamp":1598862368166,
                 *      "valet_mode":false,
                 *      "valet_pin_needed":true,
                 *      "vehicle_name":"Tessi"
                 *     }
                 * }
                 */
        }

        private static void ImportVehicles(FileInfo file, string v)
        {
            /* {"response":
                 *      [
                 *         {
                 *          "id":24342078186123456,
                 *          "vehicle_id":1154123456,
                 *          "vin":"5YJSA7H17FF123456",
                 *          "display_name":"Tessi",
                 *          "option_codes":"AD15,MDL3,PBSB,RENA,BT37,ID3W,RF3G,S3PB,DRLH,DV2W,W39B,APF0,COUS,BC3B,CH07,PC30,FC3P,FG31,GLFR,HL31,HM31,IL31,LTPB,MR31,FM3B,RS3H,SA3P,STCP,SC04,SU3C,T3CA,TW00,TM00,UT3P,WR00,AU3P,APH3,AF00,ZCST,MI00,CDM0",
                 *          "color":null,
                 *          "access_type":"OWNER",
                 *          "tokens":
                 *             [
                 *              "d5e62570d352asdf",
                 *              "919f1b2a7f73asdf"
                 *             ],
                 *          "state":"asleep",
                 *          "in_service":false,
                 *          "id_s":"24342078186123456",
                 *          "calendar_enabled":true,
                 *          "api_version":10,
                 *          "backseat_token":null,
                 *          "backseat_token_updated_at":null,
                 *          "vehicle_config":null
                 *         }
                 *      ],
                 *      "count":1
                 * }
                 */
        }
    }
}
