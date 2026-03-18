using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.Caching;
using Exceptionless;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeslaLogger
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Keine allgemeinen Ausnahmetypen abfangen", Justification = "<Pending>")]
    class ElectricityMeterEVCC : ElectricityMeterBase
    {
        string host;
        string parameter;
        string loadpointcarname;
        internal string api_state;

        static WebClient client; 
        Guid guid = Guid.NewGuid();

        public ElectricityMeterEVCC(string host, string parameter)
        {
            if (client is null)
            {
                client = new WebClient();
            }

            this.host = host;
            this.parameter = parameter;

            if(parameter is not null)
            {
                loadpointcarname = parameter;
            }
        }

        string GetCurrentData()
        {
            try
            {
                if (api_state is not null)
                {
                    return api_state;
                }

                string cacheKey = "evcc_" + guid.ToString();
                object o = MemoryCache.Default.Get(cacheKey);

                if (o is not null)
                    return (string)o;

                string url = host + "/api/state";
                string lastJSON = client.DownloadString(url);

                MemoryCache.Default.Add(cacheKey, lastJSON, DateTime.Now.AddSeconds(10));
                return lastJSON;
            }
            catch (Exception ex)
            {
                if (ex is WebException wx)
                {
                    if ((wx.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
                    {
                        Logfile.Log(wx.Message);
                        return "";
                    }

                }
                if (!WebHelper.FilterNetworkoutage(ex))
                    ex.ToExceptionless().FirstCarUserID().Submit();

                Logfile.Log(ex.ToString());
            }

            return "";
        }

        JToken getLoadPointJson(JObject json)
        {
            JToken loadpoint = null;
            // loadpointcarname can be vehicle title ("TestCar1", vehicle name ("tsla") or loadpoint name ("Wallbox1")
            // Maybe vehicle name?
            loadpoint = json.SelectToken($"$.loadpoints[?(@.vehicleName == '{loadpointcarname}')]");

            if (loadpoint is null)
            {
                // it's not a vehicle name, maybe vehicle title?
                if (json["vehicles"] is JObject vehiclesObj)
                {
                    foreach (var vehicle in vehiclesObj)
                    {
                        string vehicleTitle = vehicle.Value["title"]?.ToString() ?? "";
                        string vehicleName = vehicle.Key;
                        if (vehicleTitle == loadpointcarname)
                        {
                            loadpoint = json.SelectToken($"$.loadpoints[?(@.vehicleName == '{vehicleName}')]");
                            if (loadpoint is not null) break;
                        }
                    }
                }
                // it's also not a vehicle title. Maybe loadpoint title?
                if (loadpoint is null)
                {

                    loadpoint = json.SelectToken($"$.loadpoints[?(@.title == '{loadpointcarname}')]");
                    if (loadpoint is null)
                    {
                        return null;
                    }
                }
            }
            return loadpoint;
        }

        public override double? GetUtilityMeterReading_kWh()
        {
            string j = null;
            try
            {
                j = GetCurrentData();

                JObject jsonResult = JObject.Parse(j);

                if (jsonResult is null)
                    return null;

                if (!Tools.IsPropertyExist(jsonResult, "grid"))
                    return null;

                JToken grid = jsonResult.SelectToken($"$.grid");
                if (grid is not null)
                {
                    Dictionary<string, object> r1 = grid.ToObject<Dictionary<string, object>>();

                    if (r1.ContainsKey("energy"))
                    {
                        double.TryParse(r1["energy"].ToString(), out double value);
                        return value;
                    }
                }
                else
                {
                    Dictionary<string, object> r1 = jsonResult["result"].ToObject<Dictionary<string, object>>();

                    if (r1.ContainsKey("gridEnergy"))
                    {
                        double.TryParse(r1["gridEnergy"].ToString(), out double value);

                        return value;
                    }
                }
                return null;
                

            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, j);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, j);
            }

            return null;
        }

        public override double? GetVehicleMeterReading_kWh()
        {
            string j = null;
            try
            {
                j = GetCurrentData();

                JObject jsonResult = JObject.Parse(j);
                if (jsonResult is null)
                    return null;

                if (!Tools.IsPropertyExist(jsonResult, "loadpoints"))
                    return null;

                JToken loadpoint = getLoadPointJson(jsonResult);

                if (loadpoint is null)
                    return null;

                Dictionary<string, object> r1 = loadpoint.ToObject<Dictionary<string, object>>();

                if (r1.ContainsKey("chargeTotalImport"))
                {
                    double.TryParse(r1["chargeTotalImport"].ToString(), out double value);
                    return value;
                }
                else
                {
                    return null;
                }

            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, j);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, j);
            }

            return null;
        }

        public override double? GetSessionPrice()
        {
            string j = null;
            try
            {
                j = GetCurrentData();

                JObject jsonResult = JObject.Parse(j);
                if (jsonResult is null)
                    return null;

                if (!Tools.IsPropertyExist(jsonResult, "loadpoints"))
                    return null;

                JToken loadpoint = getLoadPointJson(jsonResult);

                if (loadpoint is null)
                    return null;

                Dictionary<string, object> r1 = loadpoint.ToObject<Dictionary<string, object>>();

                if (r1.ContainsKey("sessionPrice") && r1["sessionPrice"] is not null)
                {
                    if(double.TryParse(r1["sessionPrice"].ToString(), out double value))
                        return value;
                    else
                        return null;
                }
                else
                {
                    return null;
                }

            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, j);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, j);
            }

            return null;
        }

        public override bool? IsCharging()
        {
            string j = null;
            try
            {
                j = GetCurrentData();

                JObject jsonResult = JObject.Parse(j);
                if (jsonResult is null)
                    return null;

                if (!Tools.IsPropertyExist(jsonResult, "loadpoints"))
                    return null;

                JToken loadpoint = getLoadPointJson(jsonResult);

                if (loadpoint is null)
                    return null;

                Dictionary<string, object> r1 = loadpoint.ToObject<Dictionary<string, object>>();

                if (r1.ContainsKey("charging"))
                {
                    r1.TryGetValue("charging", out object value);
                    return (bool)value;
                }
                else
                {
                    return false;
                }

            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, j);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, j);
            }

            return null;
        }

        public override string GetVersion()
        {
            string j = null;
            try
            {
                j = GetCurrentData();

                JObject jsonResult = JObject.Parse(j);
                if (jsonResult is null)
                    return null;

                if (!Tools.IsPropertyExist(jsonResult, "version"))
                    return null;

                return jsonResult["version"]?.ToString();
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                if (!WebHelper.FilterNetworkoutage(ex))
                    ex.ToExceptionless().FirstCarUserID().Submit();

                Logfile.ExceptionWriter(ex, j);
            }
            catch (Exception ex)
            {
                if (!WebHelper.FilterNetworkoutage(ex))
                    ex.ToExceptionless().FirstCarUserID().Submit();

                Logfile.ExceptionWriter(ex, j);
            }

            return "";
        }

    }

}


