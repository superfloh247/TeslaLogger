using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Exceptionless;

namespace TeslaLogger
{
    /// <summary>
    /// Partial class for WebHelper handling geographic address lookups and geocoding.
    /// Responsible for reverse geocoding via Nominatim/MapQuest and managing address caches.
    /// Uses async/await patterns to avoid blocking on ARM32 platforms.
    /// </summary>
    public partial class WebHelper
    {
        /// <summary>
        /// Asynchronously performs reverse geocoding to find address from latitude/longitude.
        /// Supports both Nominatim (OpenStreetMap) and MapQuest geocoding services.
        /// Implements rate limiting to avoid service bans.
        /// </summary>
        /// <param name="car">Vehicle context for logging and country tracking</param>
        /// <param name="latitude">Latitude coordinate</param>
        /// <param name="longitude">Longitude coordinate</param>
        /// <param name="forceGeocoding">Skip cache and force API call</param>
        /// <param name="insertGeocodecache">Store result in local geocode cache</param>
        /// <returns>Address string or empty if not found</returns>
        /// <remarks>
        /// Priority: Geofence POI lookup → GeocodeCache → MapQuest/Nominatim API
        /// Rate limiting enforced per service terms (typically 1 req/sec minimum)
        /// </remarks>
        internal static async Task<string> ReverseGecocodingAsync(Car? car, double latitude, double longitude, bool forceGeocoding = false, bool insertGeocodecache = true)
        {
            string url = "";
            string resultContent = "";
            try
            {
                if (latitude == 0 && longitude == 0)
                {
                    return "";
                }

                if (!forceGeocoding)
                {
                    Address? a = null;
                    a = Geofence.GetInstance().GetPOI(latitude, longitude);
                    if (a is not null)
                    {
                        Logfile.Log("Reverse geocoding by Geofence");
                        return a.name;
                    }

                    string? value = GeocodeCache.Search(latitude, longitude);
                    if (!string.IsNullOrEmpty(value))
                    {
                        Logfile.Log("Reverse geocoding by GeocodeCache");
                        return value;
                    }
                }

                Tools.SetThreadEnUS();

                int elapsed = Environment.TickCount - lastGeocoding;
                if (elapsed <6000)
                {
                    await Task.Delay(6000 - elapsed).ConfigureAwait(false);
                }
                lastGeocoding = Environment.TickCount;

                using (WebClient webClient = new WebClient())
                {

                    webClient.Headers.Add("User-Agent: TL 1.1");
                    webClient.Encoding = Encoding.UTF8;

                    url = !string.IsNullOrEmpty(ApplicationSettings.Default.MapQuestKey)
                        ? "http://www.mapquestapi.com/geocoding/v1/reverse"
                        : "http://nominatim.openstreetmap.org/reverse";

                    if (!string.IsNullOrEmpty(ApplicationSettings.Default.MapQuestKey))
                    {
                        url += "?location=";
                        url += latitude.ToString();
                        url += ",";
                        url += longitude.ToString();
                        url += "&key=";
                        url += ApplicationSettings.Default.MapQuestKey;
                    }
                    else
                    {
                        url += "?format=jsonv2&lat=";
                        url += latitude.ToString();
                        url += "&lon=";
                        url += longitude.ToString();
                        url += "&email=mail";
                        url += "@";
                        url += "teslalogger";
                        url += ".de";
                    }

                    DateTime start = DateTime.UtcNow;
                    resultContent = await webClient.DownloadStringTaskAsync(new Uri(url)).ConfigureAwait(false);
                    if (car is not null)
                    {
                        _ = DBHelper.AddMothershipDataToDBAsync("ReverseGeocoding", start, 0, car.CarInDB);
                    }
                    JObject jsonResult = JObject.Parse(resultContent);
                    string adresse = "";

                    if (!string.IsNullOrEmpty(ApplicationSettings.Default.MapQuestKey))
                    {
                        JToken? res = jsonResult["results"];
                        JToken? res0 = (JArray?)res == null ? null : ((JArray?)res)?[0];
                        JToken? loc = res0?["locations"];
                        JToken? loc0 = (JArray?)loc == null ? null : ((JArray?)loc)?[0];
                        string postcode = "";

                        if (loc0?.HasProperty("postalCode") == true)
                            postcode = loc0["postalCode"]?.ToString() ?? "";

                        string country_code = "";

                        if (loc0?.HasProperty("adminArea1") == true && loc0?["adminArea1Type"]?.ToString() == "Country")
                            country_code = loc0["adminArea1"]?.ToString().ToLower() ?? "";

                        if (country_code.Length > 0 && car is not null)
                        {
                            car.CurrentJSON.current_country_code = country_code;
                            car.CurrentJSON.current_state = loc0?.HasProperty("adminArea3") == true ? loc0?["adminArea3"]?.ToString() ?? "" : "";
                        }

                        string road = "";
                        if (loc0?.HasProperty("street") == true)
                        {
                            road = loc0?["street"]?.ToString() ?? "";

                            try
                            {
                                if (country_code != "us")
                                    road = Regex.Replace(road, "^([0-9]+)?\\s?(.+)", "$2 $1").Trim();
                            }
                            catch (Exception ex)
                            {
                                ex.ToExceptionless().FirstCarUserID().AddObject(road, "road").Submit();
                            }
                        }

                        string city = "";

                        if (loc0?.HasProperty("adminArea5") == true)
                            city = loc0?["adminArea5"]?.ToString() ?? "";

                        if (country_code != "de")
                        {
                            adresse += country_code + "-";
                        }

                        adresse += postcode + " " + city + ", " + road;

                        System.Diagnostics.Debug.WriteLine("MapquestGeocode: " + adresse);
                    }
                    else
                    {
                        dynamic r2 = jsonResult["address"];
                        string postcode = "";
                        if (r2.ContainsKey("postcode"))
                            postcode = r2["postcode"].ToString();

                        string country_code = "";

                        if (r2.ContainsKey("country_code"))
                            country_code = r2["country_code"].ToString();

                        if (country_code.Length > 0 && car is not null)
                        {
                            car.CurrentJSON.current_country_code = country_code;
                            car.CurrentJSON.current_state = r2.ContainsKey("state") ? r2["state"].ToString() : "";
                        }

                        string road = "";
                        if (r2.ContainsKey("road"))
                            road = r2["road"].ToString();

                        string city = "";
                        if (r2.ContainsKey("city"))
                            city = r2["city"].ToString();
                        else if (r2.ContainsKey("town"))
                            city = r2["town"].ToString();
                        else if (r2.ContainsKey("village"))
                            city = r2["village"].ToString();

                        string house_number = "";
                        if (r2.ContainsKey("house_number"))
                            house_number = r2["house_number"].ToString();

                        string name = "";
                        if (r2.ContainsKey("name") && r2["name"] is not null)
                            name = r2["name"].ToString();

                        string address29 = "";
                        if (r2.ContainsKey("address29") && r2["address29"] is not null)
                        {
                            address29 = r2["address29"].ToString();
                        }

                        if (address29.Length > 0)
                        {
                            adresse += address29 + ", ";
                        }

                        if (country_code != "de")
                        {
                            adresse += country_code + "-";
                        }

                        adresse += postcode + " " + city + ", " + road + " " + house_number;

                        if (name.Length > 0)
                        {
                            adresse += " / " + name;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine(url + "\r\n" + adresse);

                    if (insertGeocodecache)
                    {
                        GeocodeCache.Insert(latitude, longitude, adresse);
                    }

                    if (!string.IsNullOrEmpty(ApplicationSettings.Default.MapQuestKey))
                    {
                        MapQuestCount++;
                        Logfile.Log($"Reverse geocoding by MapQuest: {MapQuestCount}");
                    }
                    else
                    {
                        NominatimCount++;
                        Logfile.Log($"Reverse geocoding by Nominatim: {NominatimCount}");
                    }

                    return adresse;
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().AddObject(resultContent, "ResultContent").AddObject(url, "Url").Submit();

                if (url is null)
                {
                    url = "NULL";
                }

                if (resultContent is null)
                {
                    resultContent = "NULL";
                }

                Logfile.ExceptionWriter(ex, url + "\r\n" + resultContent);
            }

            return "";
        }

        /// <summary>
        /// Asynchronously retrieves the country code for a geographic coordinate via Nominatim.
        /// </summary>
        /// <param name="latitude">Latitude coordinate</param>
        /// <param name="longitude">Longitude coordinate</param>
        /// <returns>ISO 3166-1 alpha-2 country code (e.g., "de", "us") or empty string if not found</returns>
        public static async Task<string> ReverseGecocodingCountryAsync(double latitude, double longitude)
        {
            string url = "";
            string resultContent = "";
            try
            {
                Tools.SetThreadEnUS();

                await Task.Delay(5000).ConfigureAwait(false);

                using (WebClient webClient = new WebClient())
                {

                    webClient.Headers.Add("User-Agent: TL 1.1");
                    webClient.Encoding = Encoding.UTF8;

                    url = "http://nominatim.openstreetmap.org/reverse";

                    url += "?format=jsonv2&lat=";
                    url += latitude.ToString();
                    url += "&lon=";
                    url += longitude.ToString();
                    url += "&email=mail";
                    url += "@";
                    url += "teslalogger";
                    url += ".de";

                    DateTime start = DateTime.UtcNow;
                    resultContent = await webClient.DownloadStringTaskAsync(new Uri(url)).ConfigureAwait(false);
                    _ = DBHelper.AddMothershipDataToDBAsync("ReverseGeocoding", start, 0, 0);

                    JObject? jsonResult = NullSafetyHelpers.SafeJObject(resultContent);
                    if (jsonResult == null)
                        return "";

                    JObject? r2 = jsonResult["address"] as JObject;
                    if (r2 == null)
                        return "";

                    string country_code = r2.GetSafeString("country_code", "");

                    if (!string.IsNullOrEmpty(country_code))
                    {
                        return country_code;
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().AddObject(resultContent, "ResultContent").AddObject(url, "Url").Submit();

                if (url is null)
                {
                    url = "NULL";
                }

                if (resultContent is null)
                {
                    resultContent = "NULL";
                }

                Logfile.ExceptionWriter(ex, url + "\r\n" + resultContent);
            }

            return "";
        }

        /// <summary>
        /// Updates a position record with new address and altitude information.
        /// </summary>
        /// <param name="id">Position record ID</param>
        /// <param name="address">New address string</param>
        /// <param name="altitude">Elevation in meters</param>
        private static void UpdateAddressByPosId(int id, string address, double altitude)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("update pos set address=@address, altitude=@altitude where id = @id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.Parameters.AddWithValue("@address", address);
                        cmd.Parameters.AddWithValue("@altitude", altitude);
                        _ = SQLTracer.TraceNQ(cmd, out _);

                        System.Diagnostics.Debug.WriteLine("id updated: " + id + " address: " + address);
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "UpdateAddressByPosId");
            }
        }

        /// <summary>
        /// Asynchronously updates all addresses with empty values using Nominatim geocoding.
        /// Uses Task.Delay (non-blocking) instead of Thread.Sleep to avoid rate limiting bans and conserve resources on ARM32.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for graceful shutdown</param>
        /// <remarks>
        /// Nominatim requires 10 second delays between requests to avoid bans.
        /// This async implementation respects that requirement without blocking thread pool threads.
        /// Processes both driving states and charging states for comprehensive address coverage.
        /// </remarks>
        public async Task UpdateAllEmptyAddressesAsync(CancellationToken cancellationToken = default)
        {
            Tools.DebugLog("UpdateAllEmptyAddressesAsync()");
            using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"SELECT  
                    pos_start.address AS Start_address,
                    pos_end.address AS End_address,
                    pos_start.id AS PosStartId,
                    pos_start.lat AS PosStartLat,
                    pos_start.lng AS PosStartLng,
                    pos_end.id AS PosEndId,
                    pos_end.lat AS PosEndtLat,
                    pos_end.lng AS PosEndLng
                FROM
                    drivestate
                    JOIN pos pos_start ON drivestate.StartPos = pos_start.id
                    JOIN pos pos_end ON drivestate.EndPos = pos_end.id
                WHERE
                    ((pos_end.odometer - pos_start.odometer) > 0.1) and (pos_start.address IS null or pos_end.address IS null or pos_start.address = '' or pos_end.address = '')", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    while (dr.Read())
                    {
                        await Task.Delay(10000, cancellationToken).ConfigureAwait(false);

                        try
                        {
#pragma warning disable CS8602 // Dereference of possibly null reference
                            if (!(dr["Start_address"] != DBNull.Value && dr["Start_address"].ToString().Length > 0))
                            {
                                int id = (int)dr["PosStartId"];
                                double lat = (double)dr["PosStartLat"];
                                double lng = (double)dr["PosStartLng"];

                                string? addressResult = await ReverseGecocodingAsync(car, lat, lng).ConfigureAwait(false);

                                if (!string.IsNullOrEmpty(addressResult))
                                {
                                    UpdateAddressByPosId(id, addressResult, 0);
                                }
                            }

                            if (!(dr["End_address"] != DBNull.Value && dr["End_address"].ToString().Length > 0))
#pragma warning restore CS8602
                            {
                                int id = (int)dr["PosEndId"];
                                double lat = (double)dr["PosEndtLat"];
                                double lng = (double)dr["PosEndLng"];

                                string? addressResult = await ReverseGecocodingAsync(car, lat, lng).ConfigureAwait(false);

                                if (!string.IsNullOrEmpty(addressResult))
                                {
                                    UpdateAddressByPosId(id, addressResult, 0);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            car.CreateExceptionlessClient(ex).Submit();
                            ExceptionWriter(ex, "");
                        }
                    }
                }
            }

            using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"SELECT pos.id, lat, lng FROM chargingstate join pos on chargingstate.Pos = pos.id where address IS null OR address = '' or pos.id = ''", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    while (dr.Read())
                    {
                        await Task.Delay(10000, cancellationToken).ConfigureAwait(false);

                        try
                        {
                            int id = (int)dr[0];
                            double lat = (double)dr[1];
                            double lng = (double)dr[2];

                            string? addressResult = await ReverseGecocodingAsync(car, lat, lng).ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(addressResult))
                            {
                                UpdateAddressByPosId(id, addressResult, 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            car.CreateExceptionlessClient(ex).Submit();
                            ExceptionWriter(ex, "");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Backward-compatible synchronous wrapper for UpdateAllEmptyAddressesAsync.
        /// </summary>
        /// <remarks>
        /// DEPRECATED: Use UpdateAllEmptyAddressesAsync for ARM32 compatibility and non-blocking operation.
        /// This wrapper uses blocking GetAwaiter().GetResult() and should only be used for legacy code paths.
        /// </remarks>
        [Obsolete("Use UpdateAllEmptyAddressesAsync instead for ARM32 compatibility")]
        public void UpdateAllEmptyAddresses()
        {
            UpdateAllEmptyAddressesAsync(CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Updates all Point-Of-Interest (POI) addresses in batches.
        /// POIs include charging stations and other notable locations.
        /// </summary>
        public static void UpdateAllPOIAddresses()
        {
            Tools.DebugLog("UpdateAllPOIAddresses()");
            try
            {
                if (Geofence.GetInstance().RacingMode)
                {
                    return;
                }

                int t = Environment.TickCount;
                int count = 0;
                Logfile.Log("UpdateAllPOIAddresses start");

                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();

                    using (MySqlCommand cmdBucket = new MySqlCommand(@"
SELECT DISTINCT
    Pos
FROM
    chargingstate
UNION DISTINCT
SELECT
    StartPos
FROM
    drivestate
UNION DISTINCT
SELECT
    EndPos
FROM
    drivestate
ORDER BY
    Pos
DESC", con))
                    {
                        Tools.DebugLog(cmdBucket);
                        var bucketdr = SQLTracer.TraceDR(cmdBucket);
                        var loop = true;

                        do
                        {
                            StringBuilder bucket = new StringBuilder();
                            for (int x = 0; x < 100; x++)
                            {
                                if (!bucketdr.Read())
                                {
                                    loop = false;
                                    break;
                                }

                                if (bucket.Length > 0)
                                    bucket.Append(",");

                                string? posid = bucketdr[0].ToString();
                                bucket.Append(posid);
                            }

                            count = UpdateAllPOIAddresses(count, bucket.ToString());
                        }
                        while (loop);
                    }


                    t = Environment.TickCount - t;
                    Logfile.Log($"UpdateAllPOIAddresses end {t}ms count:{count}");
                }
            }
            catch (MySql.Data.MySqlClient.MySqlException mex)
            {
                Tools.DebugLog(mex.ToString());
                Tools.DebugLog($"SQLState: <{mex.SqlState}>");
                foreach (var key in mex.Data.Keys)
                {
                    Tools.DebugLog($"SQL Data key:<{key}> value:<{mex.Data[key]}>");
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Internal implementation of POI address updates for a specific batch of position IDs.
        /// </summary>
        /// <param name="count">Current counter of updated addresses</param>
        /// <param name="bucket">Comma-separated list of position IDs to process</param>
        /// <returns>Updated count of addresses modified</returns>
        internal static int UpdateAllPOIAddresses(int count, string bucket)
        {
            if (bucket.Length == 0)
            {
                return count;
            }
            using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    lat,
    lng,
    pos.id,
    address,
    fast_charger_brand,
    max_charger_power 
FROM
    pos    
    LEFT JOIN chargingstate ON pos.id = chargingstate.pos
WHERE
    pos.id IN (" + MySql.Data.MySqlClient.MySqlHelper.EscapeString(bucket) + ")", con))
                {
                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    while (dr.Read())
                    {
                        count = UpdatePOIAdress(count, dr);
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Updates the most recent charging station address.
        /// </summary>
        internal void UpdateLastChargingAdress()
        {
            using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
            {
                con.Open();
                using (MySqlCommand cmd = new MySqlCommand(@"Select lat, lng, pos.id, address, fast_charger_brand, max_charger_power 
                        from chargingstate join pos on pos.id = chargingstate.pos
                        where chargingstate.CarID=@CarID 
                        order by chargingstate.id desc limit 1", con))
                {
                    cmd.Parameters.AddWithValue("@CarID", car.CarInDB);

                    MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                    int count = 0;
                    while (dr.Read())
                    {
                        count = UpdatePOIAdress(count, dr);
                    }
                }
            }
        }

        /// <summary>
        /// Updates a single POI address record, looking up via Geofence or geocoding service.
        /// </summary>
        private static int UpdatePOIAdress(int count, MySqlDataReader dr)
        {
            try
            {
                System.Threading.Thread.Sleep(1);
                double lat = (double)dr["lat"];
                double lng = (double)dr["lng"];
                int id = (int)dr["id"];
                string brand = dr["fast_charger_brand"] as string ?? "";
                int max_power = dr["max_charger_power"] as int? ?? 0;
                object name = dr[3];

                Address? a = Geofence.GetInstance().GetPOI(lat, lng, false, brand, max_power);
                if (a is null)
                {
                    if (name == DBNull.Value || name.ToString()?.Length == 0)
                    {
                        string? newName = ReverseGecocodingAsync(null, lat, lng, true, true).Result;
                        if (newName is not null && newName.Length > 0)
                        {
                            UpdatePosAddressName(id, newName);
                            count++;
                        }
                        else
                            DBHelper.UpdateAddress(null, id);
                    }
                    return count;
                }

                if (name == DBNull.Value || a.name != name.ToString())
                {
                    count++;
                    UpdatePosAddressName(id, a.name);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($" Exception in UpdateAllPOIAddresses: {ex.Message}");
            }
            return count;
        }

        /// <summary>
        /// Updates the stored address name for a position record.
        /// </summary>
        private static void UpdatePosAddressName(int id, string addressname)
        {
            using (MySqlConnection con2 = new MySqlConnection(DBHelper.DBConnectionstring))
            {
                con2.Open();
                using (MySqlCommand cmd2 = new MySqlCommand("update pos set address=@address where id = @id", con2))
                {
                    cmd2.Parameters.AddWithValue("@id", id);
                    cmd2.Parameters.AddWithValue("@address", addressname);
                    _ = SQLTracer.TraceNQ(cmd2, out _);
                }
            }
        }
    }
}
