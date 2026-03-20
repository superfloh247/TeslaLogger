using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Caching;

namespace TeslaLogger
{
    /// <summary>
    /// Represents the current state and telemetry values of a Tesla vehicle at a point in time.
    /// </summary>
    /// <remarks>
    /// CurrentJSON encapsulates all vehicle state information retrieved from the Tesla API:
    /// 
    /// State properties:
    /// - Charging status (current_charging, plugged_in)
    /// - Driving status (current_driving, current_speed)
    /// - Sleep state (current_online, current_sleeping, current_falling_asleep)
    /// - Power information (current_power, current_charger_power)
    /// 
    /// Battery/Range properties:
    /// - Battery level (current_battery_level)
    /// - Estimated range (current_battery_range_km, current_ideal_battery_range_km)
    /// - Odometer (current_odometer)
    /// 
    /// Charging properties:
    /// - Voltage/Current (current_charger_voltage, current_charger_actual_current)
    /// - Charge rate and time to full (current_charge_rate_km, current_time_to_full_charge)
    /// - Charger type and location (current_charger_brand)
    /// 
    /// Trip properties:
    /// - Trip timestamps, duration, and distances
    /// - Max speed/power during trip
    /// - Range change during trip
    /// 
    /// This class serves as the source of truth for vehicle state snapshots.
    /// All fields default to 0/false/empty unless explicitly set.
    /// Thread-safe: Uses ConcurrentDictionary for shared state tracking.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Keine allgemeinen Ausnahmetypen abfangen", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051:Sichtbare Instanzfelder nicht deklarieren", Justification = "<Pending>")]
    public class CurrentJSON
    {
        /// <summary>
        /// Global cache holding JSON state representations indexed by car ID.
        /// </summary>
        /// <remarks>
        /// Thread-safe dictionary maintaining current state for all configured vehicles.
        /// Used for quick state lookups without database queries.
        /// </remarks>
        public static readonly ConcurrentDictionary<int, string> jsonStringHolder = new ConcurrentDictionary<int, string>();
        
        /// <summary>
        /// Gets or sets a value indicating whether the vehicle is currently charging.
        /// </summary>
        /// <remarks>
        /// True if an active charging session is in progress.
        /// Defaults to false for non-charging state.
        /// </remarks>
        public bool current_charging; // defaults to false
        
        /// <summary>
        /// Gets or sets a value indicating whether the vehicle is currently driving.
        /// </summary>
        /// <remarks>
        /// True if wheels are in motion (speed > threshold).
        /// Defaults to false.
        /// </remarks>
        public bool current_driving; // defaults to false
        
        /// <summary>
        /// Gets or sets a value indicating whether the vehicle is online and connected to the network.
        /// </summary>
        /// <remarks>
        /// True if the vehicle can be reached by API calls.
        /// Defaults to false when vehicle is sleeping or offline.
        /// </remarks>
        public bool current_online; // defaults to false
        
        /// <summary>
        /// Gets or sets a value indicating whether the vehicle is in sleep mode.
        /// </summary>
        /// <remarks>
        /// True when the vehicle is dormant to conserve battery.
        /// Defaults to false during normal operation.
        /// </remarks>
        public bool current_sleeping; // defaults to false
        
        /// <summary>
        /// Gets or sets a value indicating whether the vehicle is transitioning to sleep.
        /// </summary>
        /// <remarks>
        /// True during the grace period before entering sleep mode.
        /// Defaults to false.
        /// </remarks>
        public bool current_falling_asleep; // defaults to false
        
        /// <summary>
        /// Gets or sets a value indicating whether the vehicle is plugged into a charger.
        /// </summary>
        /// <remarks>
        /// True when physically connected to a charging cable (regardless of active charging).
        /// Defaults to false when not plugged in.
        /// </remarks>
        public bool current_plugged_in; // defaults to false
        
        /// <summary>
        /// Gets or sets the timestamp when this state snapshot was captured.
        /// </summary>
        /// <remarks>
        /// Unix timestamp (seconds since epoch) of the API call that retrieved this state.
        /// Defaults to 0 (epoch) if not set.
        /// </remarks>
        private long timestamp; // defaults to 0

        /// <summary>
        /// Gets or sets the current vehicle speed in km/h.
        /// </summary>
        /// <remarks>
        /// Defaults to 0 when stationary.
        /// Retrieved from drive_state in Tesla API.
        /// </remarks>
        public int current_speed; // defaults to 0
        
        /// <summary>
        /// Gets or sets the current power output/consumption in kilowatts.
        /// </summary>
        /// <remarks>
        /// Positive value during acceleration, negative during regenerative braking.
        /// Defaults to 0 at idle.
        /// </remarks>
        public int current_power; // defaults to 0
        
        /// <summary>
        /// Gets or sets the current odometer reading in kilometers.
        /// </summary>
        /// <remarks>
        /// Total distance traveled by the vehicle.
        /// Defaults to 0; populated from vehicle_state via API.
        /// </remarks>
        public double current_odometer; // defaults to 0
        
        /// <summary>
        /// Gets or sets the calculated ideal battery range in kilometers.
        /// </summary>
        /// <remarks>
        /// Theoretical maximum range based on battery capacity and efficiency.
        /// Defaults to 0.
        /// </remarks>
        public double current_ideal_battery_range_km; // defaults to 0
        
        /// <summary>
        /// Gets or sets the estimated battery range in kilometers.
        /// </summary>
        /// <remarks>
        /// Real-world estimated range considering driving patterns.
        /// Defaults to 0; calculated based on battery level and efficiency.
        /// </remarks>
        public double current_battery_range_km; // defaults to 0
        
        /// <summary>
        /// Gets or sets the outside ambient temperature in Celsius.
        /// </summary>
        /// <remarks>
        /// Retrieved from climate_state API endpoint.
        /// Defaults to 0; temperature can be negative in cold climates.
        /// </remarks>
        public double current_outside_temperature; // defaults to 0
        
        /// <summary>
        /// Gets or sets the current battery state of charge (SoC) percentage.
        /// </summary>
        /// <remarks>
        /// Range: 0-100 representing battery fullness.
        /// Defaults to 0; retrieved from charge_state API.
        /// </remarks>
        public double current_battery_level; // defaults to 0

        /// <summary>
        /// Gets or sets the charger AC voltage in volts.
        /// </summary>
        /// <remarks>
        /// Typical values: 120V (US 1-phase), 240V (US 2-phase), 400V (3-phase European).
        /// Defaults to 0 when not charging.
        /// </remarks>
        public int current_charger_voltage; // defaults to 0
        
        /// <summary>
        /// Gets or sets the number of active charging phases.
        /// </summary>
        /// <remarks>
        /// 1 = single-phase, 3 = three-phase charging (more power).
        /// Defaults to 0 when not charging.
        /// </remarks>
        public int current_charger_phases; // defaults to 0
        
        /// <summary>
        /// Gets or sets the calculated number of charging phases.
        /// </summary>
        /// <remarks>
        /// Computed value based on voltage and current.
        /// Defaults to 0.
        /// </remarks>
        public int current_charger_phases_calc; // defaults to 0
        
        /// <summary>
        /// Gets or sets the actual charging current in amperes.
        /// </summary>
        /// <remarks>
        /// Current delivered to the battery during charging.
        /// Defaults to 0 when not charging.
        /// </remarks>
        public int current_charger_actual_current; // defaults to 0
        
        /// <summary>
        /// Gets or sets the calculated charging current based on power/voltage.
        /// </summary>
        /// <remarks>
        /// Derived value: Power / Voltage.
        /// Defaults to 0.
        /// </remarks>
        public int current_charger_actual_current_calc; // defaults to 0
        
        /// <summary>
        /// Gets or sets the requested charging current in amperes.
        /// </summary>
        /// <remarks>
        /// User-configured or vehicle-determined target charging current.
        /// Actual may differ based on charger capability.
        /// </remarks>
        public int current_charge_current_request; // defaults to 0
        
        /// <summary>
        /// Gets or sets the total energy added to the battery in this charging session (kWh).
        /// </summary>
        /// <remarks>
        /// Resets when charging session ends.
        /// Defaults to 0 when not charging.
        /// </remarks>
        public double current_charge_energy_added; // defaults to 0
        
        /// <summary>
        /// Gets or sets the current charger power delivery in kilowatts.
        /// </summary>
        /// <remarks>
        /// Calculated as Voltage × Current / 1000.
        /// Defaults to 0.
        /// </remarks>
        public double current_charger_power; // defaults to 0
        
        /// <summary>
        /// Gets or sets the calculated charger power in watts.
        /// </summary>
        /// <remarks>
        /// High-precision power delivery calculation.
        /// Defaults to 0 when not charging.
        /// </remarks>
        public int current_charger_power_calc_w; // defaults to 0
        
        /// <summary>
        /// Gets or sets the charging rate in kilometers per hour of charging.
        /// </summary>
        /// <remarks>
        /// Indicates how quickly the battery range increases during charging.
        /// Defaults to 0 when not charging.
        /// </remarks>
        public double current_charge_rate_km; // defaults to 0
        
        /// <summary>
        /// Gets or sets the estimated time until fully charged in hours.
        /// </summary>
        /// <remarks>
        /// Calculated based on current charge rate and remaining capacity.
        /// Defaults to 0; can be shown to user for charge time estimation.
        /// </remarks>
        public double current_time_to_full_charge; // defaults to 0
        
        /// <summary>
        /// Gets or sets a value indicating whether the charge door is open.
        /// </summary>
        /// <remarks>
        /// Indicates if the charge port access panel is open.
        /// Defaults to false.
        /// </remarks>
        public bool current_charge_port_door_open; // defaults to false
        
        /// <summary>
        /// Gets or sets the brand/type of charger being used.
        /// </summary>
        /// <remarks>
        /// Examples: "Tesla", "ChargePoint", "Electrify America".
        /// Empty string if not charging or unknown.
        /// </remarks>
        public string current_charger_brand = "";
        
        /// <summary>
        /// Gets or sets a value indicating whether a fast charger (DC) is present.
        /// </summary>
        /// <remarks>
        /// True if connected to a DC fast charging station.
        /// Defaults to false for standard AC Level 2 charging.
        /// </remarks>
        public bool current_fast_charger_present; // defaults to false

        /// <summary>
        /// Gets or sets the current vehicle firmware version.
        /// </summary>
        /// <remarks>
        /// Retrieved from vehicle_config API endpoint.
        /// Empty string if not retrieved yet.
        /// </remarks>
        public string current_car_version = "";
        
        /// <summary>
        /// Gets or sets the software update status.
        /// </summary>
        /// <remarks>
        /// Examples: "scheduled", "available", "installing", empty if not updating.
        /// Defaults to empty string.
        /// </remarks>
        public string software_update_status = ""; // defaults to null;
        
        /// <summary>
        /// Gets or sets the available software version for update.
        /// </summary>
        /// <remarks>
        /// Empty if no update is available.
        /// </remarks>
        public string software_update_version = ""; // defaults to null;

        /// <summary>
        /// Gets or sets the start timestamp of the current trip.
        /// </summary>
        /// <remarks>
        /// Initialized when driving begins.
        /// Defaults to DateTime.MinValue when not on a trip.
        /// </remarks>
        public DateTime current_trip_start = DateTime.MinValue;
        
        /// <summary>
        /// Gets or sets the end timestamp of the current trip.
        /// </summary>
        /// <remarks>
        /// Set when driving ends.
        /// Defaults to DateTime.MinValue if trip hasn't ended.
        /// </remarks>
        public DateTime current_trip_end = DateTime.MinValue;
        
        /// <summary>
        /// Gets or sets the odometer reading at the start of the trip.
        /// </summary>
        /// <remarks>
        /// Captured when trip begins.
        /// Defaults to 0.
        /// </remarks>
        public double current_trip_km_start; // defaults to 0;
        
        /// <summary>
        /// Gets or sets the odometer reading at the end of the trip.
        /// </summary>
        /// <remarks>
        /// captured when trip completes.
        /// Defaults to 0.
        /// </remarks>
        public double current_trip_km_end; // defaults to 0;
        
        /// <summary>
        /// Gets or sets the maximum speed reached during the current trip.
        /// </summary>
        /// <remarks>
        /// Tracked throughout the trip.
        /// Defaults to 0.
        /// </remarks>
        public double current_trip_max_speed; // defaults to 0;
        
        /// <summary>
        /// Gets or sets the maximum power output during the current trip.
        /// </summary>
        /// <remarks>
        /// Peak power during acceleration.
        /// Defaults to 0.
        /// </remarks>
        public double current_trip_max_power; // defaults to 0;
        
        /// <summary>
        /// Gets or sets the range estimate at trip start.
        /// </summary>
        /// <remarks>
        /// Captures starting range for trip energy analysis.
        /// Defaults to 0.
        /// </remarks>
        public double current_trip_start_range; // defaults to 0;
        
        /// <summary>
        /// Gets or sets the range estimate at trip end.
        /// </summary>
        /// <remarks>
        /// Captures ending range for trip energy consumption calculation.
        /// Defaults to 0.
        /// </remarks>
        public double current_trip_end_range; // defaults to 0;
        
        /// <summary>
        /// Gets or sets the Wh per kilometer efficiency ratio for the vehicle.
        /// </summary>
        /// <remarks>
        /// Used for energy consumption and range calculations.
        /// Defaults to 0.19 (typical Model 3 efficiency).
        /// </remarks>
        public double Wh_TR = 0.19;

        /// <summary>
        /// Gets or sets the trip duration in seconds.
        /// </summary>
        /// <remarks>
        /// Total time spent driving in current trip.
        /// Defaults to 0.
        /// </remarks>
        public int current_trip_duration_sec; // defaults to 0;

        private double latitude; // defaults to 0;
        private double longitude; // defaults to 0;
        public int charge_limit_soc; // defaults to 0;
        public int heading; // defaults to 0;
        public double current_inside_temperature; // defaults to 0;
        public bool current_battery_heater; // defaults to false;
        public bool current_is_sentry_mode; // defaults to false;
        public bool current_is_preconditioning; // defaults to false;

        public string current_country_code = "";
        public string current_state = "";

        public double tpms_pressure_fr; // defaults to 0;
        public double tpms_pressure_fl; // defaults to 0;
        public double tpms_pressure_rr; // defaults to 0;
        public double tpms_pressure_rl; // defaults to 0;

        public DateTime lastScanMyTeslaReceived = DateTime.MinValue;
        public double? SMTCellTempAvg; // defaults to null;
        public double? SMTCellMinV; // defaults to null;
        public double? SMTCellAvgV; // defaults to null;
        public double? SMTCellMaxV; // defaults to null;
        public double? SMTCellImbalance; // defaults to null;
        public double? SMTBMSmaxCharge; // defaults to null;
        public double? SMTBMSmaxDischarge; // defaults to null;
        public double? SMTACChargeTotal; // defaults to null;
        public double? SMTDCChargeTotal; // defaults to null;
        public double? SMTNominalFullPack; // defaults to null;

        public double? SMTSpeed; // defaults to null;
        public double? SMTBatteryPower; // defaults to null;

        public string active_route_destination; // defaults to null;
        public long? active_route_energy_at_arrival; // defaults to null;
        public long? active_route_km_to_arrival; // defaults to null;
        public double? active_route_minutes_to_arrival; // defaults to null;
        public double? active_route_traffic_minutes_delay; // defaults to null;
        public double? active_route_latitude; // defaults to null;
        public double? active_route_longitude; // defaults to null;

        public string FatalError;

        public string current_json = "";
        private DateTime lastJSONwrite = DateTime.MinValue;
        Car car;

        internal CurrentJSON(Car car)
        {
            this.car = car;
        }

        public void CheckCreateCurrentJSON()
        {
            TimeSpan ts = DateTime.UtcNow - lastJSONwrite;
            if (ts.TotalMinutes > 5)
            {
                CreateCurrentJSON();
            }
        }

        public void CreateCurrentJSON()
        {
            try
            {
                lastJSONwrite = DateTime.UtcNow;

                int duration = 0;
                double distance = 0;
                double trip_kwh = 0.0;
                double trip_avg_wh = 0.0;

                try
                {
                    if (current_trip_end == DateTime.MinValue)
                    {
                        duration = (int)(DateTime.Now - current_trip_start).TotalSeconds;
                        distance = current_odometer - current_trip_km_start;
                        trip_kwh = (current_trip_start_range - current_ideal_battery_range_km) * Wh_TR;

                        if (distance > 0)
                        {
                            trip_avg_wh = trip_kwh / distance * 1000;
                        }
                    }
                    else
                    {
                        duration = (int)(current_trip_end - current_trip_start).TotalSeconds;
                        distance = current_trip_km_end - current_trip_km_start;
                        trip_kwh = (current_trip_start_range - current_trip_end_range) * Wh_TR;

                        if (distance > 0)
                        {
                            trip_avg_wh = trip_kwh / distance * 1000;
                        }
                    }
                }
                catch (Exception ex)
                {
                    car.CreateExceptionlessClient(ex).Submit();

                    Logfile.Log(ex.ToString());
                    duration = 0;
                }
                if (duration < 0)
                {
                    duration = 0;
                }

                var apistate = car.GetTeslaAPIState();

                apistate.GetBool("charge_port_door_open", out current_charge_port_door_open);
                apistate.GetString("software_update.status", out software_update_status);
                apistate.GetString("software_update.version", out software_update_version);

                apistate.GetInt("fd_window", out int fd_window);
                apistate.GetInt("fp_window", out int fp_window);
                apistate.GetInt("rd_window", out int rd_window);
                apistate.GetInt("rp_window", out int rp_window);

                apistate.GetInt("pf", out int pf);
                apistate.GetInt("pr", out int pr);
                apistate.GetInt("df", out int df);
                apistate.GetInt("dr", out int dr);

                apistate.GetInt("ft", out int frunk);
                apistate.GetInt("rt", out int trunk);

                bool locked = true;
                if (apistate.HasValue("locked")) // after restart the locked state is false, that tends to confuse
                    apistate.GetBool("locked", out locked);

                int open_windows = fd_window + fp_window + rd_window + rp_window;
                int open_doors = 
                    pf > 0 ? 1 :0 
                    + pr > 0 ? 1 : 0
                    + df > 0 ? 1 : 0
                    + dr > 0 ? 1 : 0;

                Dictionary<string, object> values = new Dictionary<string, object>
                {
                   { "charging", current_charging},
                   { "driving", current_driving },
                   { "online", current_online },
                   { "sleeping", current_sleeping },
                   { "falling_asleep", current_falling_asleep },
                   { "plugged_in", current_plugged_in },
                   { "speed", current_speed},
                   { "power", current_power },
                   { "odometer", current_odometer },
                   { "ideal_battery_range_km", current_ideal_battery_range_km},
                   { "battery_range_km", current_battery_range_km},
                   { "outside_temp", current_outside_temperature},
                   { "battery_level", current_battery_level},
                   { "charger_voltage", current_charger_voltage},
                   { "charger_phases", current_charger_phases},
                   { "charger_phases_calc", current_charger_phases_calc},
                   { "charger_actual_current", current_charger_actual_current},
                   { "charger_actual_current_calc", current_charger_actual_current_calc},
                   { "charge_current_request", current_charge_current_request},
                   { "charge_energy_added", current_charge_energy_added},
                   { "charger_power", current_charger_power},
                   { "charger_power_calc_w", current_charger_power_calc_w},
                   { "charge_rate_km", current_charge_rate_km},
                   { "charge_port_door_open", current_charge_port_door_open },
                   { "time_to_full_charge", current_time_to_full_charge},
                   { "fast_charger_brand", current_charger_brand},
                   { "fast_charger_present", current_fast_charger_present},
                   { "car_version", current_car_version },
                   { "trip_start", current_trip_start.ToString("t",Tools.ciDeDE) },
                   { "trip_start_dt", current_trip_start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", Tools.ciEnUS) },
                   { "trip_max_speed", current_trip_max_speed },
                   { "trip_max_power", current_trip_max_power },
                   { "trip_duration_sec", duration },
                   { "trip_kwh", Math.Round(trip_kwh, 1) },
                   { "trip_avg_kwh", Math.Round(trip_avg_wh, 1) },
                   { "trip_distance", Math.Round(distance, 1) },
                   { "ts", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", Tools.ciEnUS)},
                   { "latitude", latitude },
                   { "longitude", longitude },
                   { "charge_limit_soc", charge_limit_soc},
                   { "inside_temperature", current_inside_temperature },
                   { "battery_heater", current_battery_heater },
                   { "is_preconditioning", current_is_preconditioning },
                   { "sentry_mode", current_is_sentry_mode },
                   { "country_code", current_country_code },
                   { "state", current_state },
                   { "display_name", car.DisplayName},
                   { "heading", heading},
                   { "software_update_status", software_update_status },
                   { "software_update_version" , software_update_version },
                   { "active_route_destination" , active_route_destination },
                   { "active_route_energy_at_arrival" , active_route_energy_at_arrival },
                   { "active_route_minutes_to_arrival" , active_route_minutes_to_arrival },
                   { "active_route_traffic_minutes_delay" , active_route_traffic_minutes_delay },
                   { "active_route_latitude" , active_route_latitude },
                   { "active_route_longitude" , active_route_longitude },
                   { "open_windows" , open_windows},
                   { "open_doors" , open_doors},
                   { "frunk" , frunk},
                   { "trunk" , trunk},
                   { "locked" , locked},
                   { "FatalError", FatalError},
                   { "tpms_pressure_fr", tpms_pressure_fr },
                   { "tpms_pressure_fl", tpms_pressure_fl },
                   { "tpms_pressure_rr", tpms_pressure_rr },
                   { "tpms_pressure_rl", tpms_pressure_rl }
                };

                TimeSpan ts = DateTime.Now - lastScanMyTeslaReceived;
                if (ts.TotalMinutes < 5)
                {
                    values.Add("SMTCellTempAvg", SMTCellTempAvg);
                    values.Add("SMTCellMinV", SMTCellMinV);
                    values.Add("SMTCellAvgV", SMTCellAvgV);
                    values.Add("SMTCellMaxV", SMTCellMaxV);
                    values.Add("SMTCellImbalance", SMTCellImbalance);
                    values.Add("SMTBMSmaxCharge", SMTBMSmaxCharge);
                    values.Add("SMTBMSmaxDischarge", SMTBMSmaxDischarge);
                    values.Add("SMTACChargeTotal", SMTACChargeTotal);
                    values.Add("SMTDCChargeTotal", SMTDCChargeTotal);
                    values.Add("SMTNominalFullPack", SMTNominalFullPack);
                }

                Address addr = Geofence.GetInstance().GetPOI(latitude, longitude, false);
                if (addr is not null && addr.rawName is not null)
                {
                    values.Add("TLGeofence", addr.rawName);
                    values.Add("TLGeofenceIsHome", addr.IsHome);
                    values.Add("TLGeofenceIsCharger", addr.IsCharger);
                    values.Add("TLGeofenceIsWork", addr.IsWork);
                }
                else
                {
                    values.Add("TLGeofence", "-");
                    values.Add("TLGeofenceIsHome", false);
                    values.Add("TLGeofenceIsCharger", false);
                    values.Add("TLGeofenceIsWork", false);
                }

                current_json = JsonConvert.SerializeObject(values);

                jsonStringHolder[car.CarInDB] = current_json;

                // FileManager.WriteCurrentJsonFile(car.CarInDB, current_json);
            }
            catch (Exception ex)
            {
                car.CreateExceptionlessClient(ex).Submit();

                Logfile.Log(ex.ToString());
                current_json = "";
            }
        }

        public void SetPosition(double lat, double lng, long ts)
        {
            if (ts > timestamp)
            {
                latitude = lat;
                longitude = lng;
                timestamp = ts;
            }
        }

        public double Latitude
        {
            get => latitude;
            set => latitude = value;
        }

        public double Longitude
        {
            get => longitude;
            set => longitude = value;
        }

        internal void ToKVS()
        {
            ToKVS(car.CarInDB);
        }

        internal static void ToKVS(int CarInDB)
        {
            KVS.InsertOrUpdate($"currentJSON_{CarInDB}", jsonStringHolder[CarInDB]);
        }

        internal void FromKVS()
        {
            FromKVS(car.CarInDB);
        }

        internal static void FromKVS(int CarInDB)
        {
            if (KVS.Get($"currentJSON_{CarInDB}", out string cJSON) == KVS.SUCCESS)
            {
                jsonStringHolder[CarInDB] = cJSON;
            }
            else
            {
                jsonStringHolder[CarInDB] = "{}";
            }
        }
    }
}


