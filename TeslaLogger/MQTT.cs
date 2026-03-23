using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Exceptionless;
using Newtonsoft.Json;
using System.Linq;
using Org.BouncyCastle.Utilities.Encoders;
using System.Web;
using System.Security.Cryptography.X509Certificates;
using System.Net.Sockets;

// MQTTnet replaces the legacy M2Mqtt library
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

#nullable enable

namespace TeslaLogger
{
    public interface IWebDownloader
    {
        string DownloadString(string url);
    }

    public interface IMqttClient
    {
        bool IsConnected { get; }

        event MqttMsgPublishEventHandler MqttMsgPublishReceived;
        ushort Subscribe(string[] topics, byte[] qosLevels);
        ushort Publish(string topic, byte[] message, byte qosLevel, bool retain);
        byte Connect(string clientId, string username, string password, bool willRetain, byte willQosLevel, bool willFlag, string willTopic, string willMessage, bool cleanSession, ushort keepAlivePeriod);
        ushort Unsubscribe(string[] topics);
    }

    // compatibility types previously provided by M2Mqtt
    public delegate void MqttMsgPublishEventHandler(object sender, MqttMsgPublishEventArgs e);

    public class MqttMsgPublishEventArgs : EventArgs
    {
        public string? Topic { get; set; }
        public byte[]? Message { get; set; }
        public byte QosLevel { get; set; }
        public bool Retain { get; set; }
    }

    public enum MqttSslProtocols
    {
        None = 0,
        Tls = 1,
        Tls11 = 2,
        Tls12 = 3,
        Tls13 = 4
    }

    public static class MqttMsgBase
    {
        public const byte QOS_LEVEL_AT_MOST_ONCE = 0;
        public const byte QOS_LEVEL_AT_LEAST_ONCE = 1;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Keine allgemeinen Ausnahmetypen abfangen", Justification = "<Pending>")]
    internal class MQTT
    {
        private static MQTT? _Mqtt;

        private string? clientid;
        private string? host;
        private int port = 1883;
        private string topic = "teslalogger";
        private bool singletopics;
        private bool publishJson;
        private bool discoveryEnable;
        private string discoverytopic = "homeassistant";
        private string? user;
        private string? password;
        private static int httpport = 5000;
        private static int heartbeatCounter;
        private static bool connecting;

        private IMqttClient? client;

        System.Collections.Generic.HashSet<string> allCars = new();
        System.Collections.Generic.Dictionary<int, string> lastjson = new();

        private MQTT()
        {
            Logfile.Log("MQTT: initialized");
        }

        public static MQTT GetSingleton()
        {
            if (_Mqtt is null)
            {
                _Mqtt = new MQTT();
            }
            return _Mqtt;
        }
        /// <summary>
        /// Asynchronously runs the MQTT client with support for cancellation.
        /// Initializes connections and processes telemetry in non-blocking async pattern.
        /// </summary>
        internal async Task RunMqttAsync(CancellationToken cancellationToken = default)
        {
            // https://github.com/bassmaster187/TeslaLogger/issues/1434
            // We could make sleep below much longer, but that bears the risk that car_1 is already asleep again before we finish MQTT discovery
            // -> only increase to 40 seconds and handle 404 later

            // initially delay 40 seconds to let the cars get from Start to Online (non-blocking)
            try
            {
                await Task.Delay(40000, cancellationToken).ConfigureAwait(false);

                httpport = Tools.GetHttpPort();
                allCars = GetAllcars();

                ParseSettings();

                client = MqttClientWrapper.CreateClient(host, port, false, null, null, MqttSslProtocols.None);

                await ConnectionCheckAsync(cancellationToken).ConfigureAwait(false);

                if (client.IsConnected)
                {
                    Logfile.Log("MQTT: Connected!");
                    foreach (string vin in allCars)
                    {

                        client.Subscribe(new[] {
                        $"{topic}/command/{vin}/+"
                        },
                            new[] {
                            MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE
                            });
                    }

                    client.MqttMsgPublishReceived += Client_MqttMsgPublishReceived;
                    _ = Task.Run(async () => await MQTTConnectionHandlerAsync(client, cancellationToken).ConfigureAwait(false), cancellationToken);

                    if (discoveryEnable && singletopics)
                    {
                        foreach (string vin in allCars)
                        {
                            PublishDiscovery(vin);
                        }
                    }
                }
                else
                {
                    Logfile.Log("MQTT: Connection failed!");
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    await WorkAsync(cancellationToken).ConfigureAwait(false);
                    // delay 1 second between work cycles (non-blocking)
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                Logfile.Log("MQTT: RunMqttAsync cancelled");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"MQTT: RunMqttAsync Exception: {ex.Message}");
                Tools.DebugLog("MQTT: RunMqttAsync Exception", ex);
            }
        }

        private void ParseSettings()
        {
            try
            {
                if (KVS.Get("MQTTSettings", out string mqttSettingsJson) == KVS.SUCCESS)
                {
                JObject r = JObject.Parse(mqttSettingsJson);
                    if (r["mqtt_host"]?.ToString() is not null)
                    {
                        host = r["mqtt_host"]?.ToString();
                    }
                    else
                    {
                        Logfile.Log("MQTT: No host setting -> MQTT disabled! Check settings and reboot");
                        return;
                    }
                    if (r["mqtt_port"]?.Value<int?>() > 0)
                    {
                        port = (int)r["mqtt_port"];
                    }
                    if (r["mqtt_user"]?.ToString() is not null && r["mqtt_passwd"]?.ToString() is not null)
                    {
                        user = r["mqtt_user"]?.ToString();
                        password = r["mqtt_passwd"]?.ToString();
                    }
                    if (r["mqtt_topic"]?.ToString() is not null)
                    {
                        topic = r["mqtt_topic"]?.ToString();
                    }
                    if (r["mqtt_publishjson"]?.Value<bool?>() ?? false)
                    {
                        publishJson = (bool)r["mqtt_publishjson"];
                    }
                    if (r["mqtt_singletopics"]?.Value<bool?>() ?? false)
                    {
                        singletopics = (bool)r["mqtt_singletopics"];
                    }
                    if (r["mqtt_discoveryenable"]?.Value<bool?>() ?? false)
                    {
                        discoveryEnable = (bool)r["mqtt_discoveryenable"];
                    }
                    if (r["mqtt_discoverytopic"]?.ToString() is not null)
                    {
                        discoverytopic = r["mqtt_discoverytopic"]?.ToString();
                    }
                    if (r["mqtt_clientid"]?.ToString() is not null)
                    {
                        clientid = r["mqtt_clientid"]?.ToString();
                    }
                    Logfile.Log("MQTT: Settings found");
                }
                else
                {
                    Logfile.Log("MQTT: Settings not found!");
                }
            }
            catch (Newtonsoft.Json.JsonException jsonEx)
            {
                Logfile.Log($"MQTT: JSON parse error in ParseSettings - {jsonEx.Message}");
                jsonEx.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log("MQTT: Settings not found!");
            }
            catch (Exception ex)
            {
                Logfile.Log($"MQTT: Error in ParseSettings - {ex.Message}");
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log("MQTT: Settings not found!");
            }
        }

        /// <summary>
        /// Asynchronously processes MQTT work cycle: publishes car telemetry and handles subscriptions.
        /// </summary>
        internal async Task WorkAsync(CancellationToken cancellationToken = default)
        {
            // TODO: in unittest, initialization is not done like in real code
            if (allCars is null && Tools.IsUnitTest())
            {
                allCars = GetAllcars();
            }

            try
            {
                // Not connected ? do nothing
                if (!await ConnectionCheckAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
                var needsAllCarRefresh = false;

                //heartbeat
                if (heartbeatCounter % 10 == 0)
                {
                    client.Publish($@"{topic}/system/status", Encoding.UTF8.GetBytes("online"),
                                MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
                    //Tools.DebugLog("MQTT: hearbeat!");
                    heartbeatCounter = 0;
                }
                heartbeatCounter++;

                var cars = allCars.ToList();
                foreach (string vin in cars)
                {
                    string temp = null;
                    string carTopic = $"{topic}/car/{vin}";
                    string jsonTopic = $"{topic}/json/{vin}/currentjson";

                    int carId = Car.GetCarIDFromVIN(vin);

                    if (carId < 0)
                    {
                        // https://github.com/bassmaster187/TeslaLogger/issues/1434
                        // carId is not found ? -> either removed after discovery, or other error occurred in TL
                        // -> sinal to refresh allCars and continue.

                        Tools.DebugLog($"MQTT: VIN {vin} returned car ID {carId}. Skipping...");
                        needsAllCarRefresh = true;
                        continue;
                    }

                    try
                    {
                        temp = RetrieveJsonString($"http://localhost:{httpport}/currentjson/{carId}");

                    }
                    catch (System.Net.WebException wex) when (wex.Response is HttpWebResponse httpResponse && httpResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        Logfile.Log($"MQTT: Could not retrieve CurrentJson for car id {carId}: {wex.Message}");
                        Tools.DebugLog("MQTT: CurrentJson Exception", wex);
                        needsAllCarRefresh = true;
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Logfile.Log($"MQTT: CurrentJson Exception: {ex.Message}");
                        Tools.DebugLog("MQTT: CurrentJson Exception", ex);
                        // ex.ToExceptionless().FirstCarUserID().Submit();
                        await Task.Delay(60000, cancellationToken).ConfigureAwait(false); // wait 60 seconds after exception
                    }

                    if (!lastjson.ContainsKey(carId) || temp != lastjson[carId])
                    {
                        lastjson[carId] = temp;
                        if (publishJson)
                        {
                            client.Publish(jsonTopic, Encoding.UTF8.GetBytes(lastjson[carId]),
                                MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
                        }

                        if (singletopics)
                        {
                            var topics = JsonConvert.DeserializeObject<Dictionary<string, string>>(temp);
                            foreach (var keyvalue in topics)
                            {
                                var safeValue = GetSafeValueForPublishing(keyvalue);
                                if(keyvalue.Key == "charge_limit_soc" && safeValue == "0")
                                {
                                    safeValue = "50";
                                }

                                client.Publish($"{carTopic}/{keyvalue.Key}", Encoding.UTF8.GetBytes(safeValue),
                                MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);

                            }
                            Double.TryParse(topics["latitude"], out double lat);
                            Double.TryParse(topics["longitude"], out double lon);
                            PublichGPSTracker(vin, lat, lon);
                        }

                    }
                }

                if (!needsAllCarRefresh)
                {
                    return;
                }
                allCars = GetAllcars();

                UnsubscribeFromRemovedCars(cars.Except(allCars));
            }
            catch (Newtonsoft.Json.JsonException jsonEx)
            {
                Logfile.Log($"MQTT: Work JSON parse error: {jsonEx.Message}");
                Tools.DebugLog("MQTT: Work JSON Exception", jsonEx);
                jsonEx.ToExceptionless().FirstCarUserID().Submit();
                await Task.Delay(60000, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Logfile.Log("MQTT: WorkAsync cancelled");
            }
            catch (Exception ex)
            {
                Logfile.Log($"MQTT: Work Exception: {ex.Message}");
                Tools.DebugLog("MQTT: Work Exception", ex);
                ex.ToExceptionless().FirstCarUserID().Submit();
                await Task.Delay(60000, cancellationToken).ConfigureAwait(false);
            }
        }

        private void UnsubscribeFromRemovedCars(IEnumerable<string> vinsToUnsubscribe)
        {
            if(!client.IsConnected)
            {
                return;
            }

            client.Unsubscribe(vinsToUnsubscribe.Select(vin => $"{topic}/command/{vin}/+").ToArray());
        }

        /// <summary>
        /// Compute safe value for publishing in MQTT topic
        /// E.q. HomeAssistant will throw errors, if "NULL" is found, but a numeric value is expected
        /// </summary>
        /// <param name="keyvalue">a KeyValuePair where Key is the topic and Value is the value</param>
        /// <returns></returns>
        private static string GetSafeValueForPublishing(KeyValuePair<string, string> keyvalue)
        {
            if (!(keyvalue.Value is null))
            { 
                return keyvalue.Value; 
            }

            switch (keyvalue.Key)
            {
                case "active_route_energy_at_arrival":
                case "active_route_km_to_arrival":
                    return "0";
                case "active_route_minutes_to_arrival":
                case "active_route_traffic_minutes_delay":
                case "active_route_latitude":
                case "active_route_longitude":
                    return "0.0";
                default:
                    return "NULL";
            }
        }

        private void Client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e)
        {
            try
            {
                var msg = Encoding.ASCII.GetString(e.Message);

                //Example: "teslalogger/command/LRW123456/set_charging_amps", raw value "13"
                string commandRegex = topic + @"/command/(.{17})/(.+)";
                Tools.DebugLog("MQTT: Client_MqttMsgPublishReceived");

                Match m = Regex.Match(e.Topic, commandRegex);
                if (m.Success && m.Groups.Count == 3 && m.Groups[1].Captures.Count == 1 && m.Groups[2].Captures.Count == 1)
                {
                    string vin = m.Groups[1].Captures[0].ToString();
                    string command = m.Groups[2].Captures[0].ToString();
                    try
                    {
                        using (WebClient wc = new WebClient())
                        {
                            string json = wc.DownloadString($"http://localhost:{httpport}/command/{vin}/{command}?{msg}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logfile.Log($"MQTT: Subscribe exception: {ex.Message}");
                        Tools.DebugLog("MQTT: PublishReceived Exception", ex);
                        // Removed Thread.Sleep(20000) - ARM32 events should not block
                        // Retry will occur on next message arrival or next work cycle
                    }
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"MQTT: PublishReceived Exeption: {ex}");
                Tools.DebugLog("MQTT: PublishReceived Exception", ex);
                ex.ToExceptionless().FirstCarUserID().Submit();
            }
        }

        /// <summary>
        /// Asynchronously checks MQTT connection status and attempts reconnection if needed.
        /// Uses non-blocking delays (Task.Delay) instead of Thread.Sleep.
        /// </summary>
        private async Task<bool> ConnectionCheckAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (client is not null)
                {
                    if (client.IsConnected)
                    {
                        if (connecting)
                        {
                            connecting = false;
                            Tools.DebugLog("MQTT: connected, connecting = false");
                            Logfile.Log("MQTT: Connected!");
                        }
                        return true;
                    }
                    else
                    {
                        if (!connecting)
                        {
                            string newClientId;
                            if(clientid is not null)
                            {
                                newClientId = clientid;
                            }
                            else
                            {
                                newClientId = Guid.NewGuid().ToString().Substring(0, 18);
                            }
                            connecting = true;
                            Tools.DebugLog("MQTT: not connected, connecting = true");
                            if (user is not null && password is not null)
                            {
                                Logfile.Log($"MQTT: Connecting with credentials: {host}:{port} with ClientID: {newClientId}");
                                client.Connect(newClientId, user, password, false, 0, true, $@"{topic}/system/status", "offline", true, 30);
                            }
                            else
                            {
                                Logfile.Log($"MQTT: Connecting without credentials: {host}:{port} with ClientID: {newClientId}");
                                client.Connect(newClientId, null, null, false, 0, true, $@"{topic}/system/status", "offline", true, 30);
                            }

                            return false;
                            
                        }
                        return false;
                    }
                }
                else 
                {
                    Logfile.Log("MQTT: ConnectionCheck client is null!");
                    return false;
                }
            }
            catch (System.Net.WebException wex)
            {
                Logfile.Log($"MQTT: ConnectionCheck WebException: {wex.Message}");
                connecting = false;
                await Task.Delay(60000, cancellationToken).ConfigureAwait(false);

            }
            catch (Exception cex)
            {
                if (cex.InnerException is SocketException se)
                {
                    if (se.ErrorCode == 10060)
                    {
                        Logfile.Log("MQTT: Connection Error: Connection timed out");
                        connecting = false;
                        await Task.Delay(60000, cancellationToken).ConfigureAwait(false);
                        return false;
                    }
                    else if (se.ErrorCode == 10061)
                    {
                        Logfile.Log("MQTT: Connection Error: Connection refused");
                        connecting = false;
                        await Task.Delay(60000, cancellationToken).ConfigureAwait(false);
                        return false;
                    }
                }

                Logfile.Log($"MQTT: ConnectionCheck Exception: {cex}");
                connecting = false;
                await Task.Delay(60000, cancellationToken).ConfigureAwait(false);
            }
            return false;
        }

        /// <summary>
        /// Asynchronously monitors MQTT connection status and reconnects if needed.
        /// Runs in background task with graceful cancellation support.
        /// </summary>
        private async Task MQTTConnectionHandlerAsync(IMqttClient client, CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

                    await ConnectionCheckAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (System.Net.WebException wex)
                {
                    Logfile.Log($"MQTT: MQTTConnectionHandler WebException: {wex.Message}");
                    await Task.Delay(60000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Logfile.Log("MQTT: MQTTConnectionHandler cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    await Task.Delay(30000, cancellationToken).ConfigureAwait(false);
                    Logfile.Log($"MQTT: MQTTConnectionHandler Exception: {ex}");
                }
            }
        }

        private static HashSet<string> GetAllcars()
        {
            HashSet<string> h = new();
            string json = "";

            try
            {
                json = RetrieveJsonString($"http://localhost:{httpport}/getallcars");
            }
            catch (Exception ex)
            {
                Logfile.Log($"MQTT: GetAllCars: {ex.Message}");
                ex.ToExceptionless().FirstCarUserID().Submit();
                // Removed Thread.Sleep(20000) - ARM32 blocking eliminated
                // Retry occurs at next iteration of work cycle
            }


            try
            {
                JArray cars = JArray.Parse(json);
                foreach (JToken car in cars)
                {
                    int id = (int)car["id"];
                    string inactiveFlag = car["inactive"]?.ToString();
                    var carObj = Car.GetCarByID(id);
                    if (carObj is null || carObj.GetCurrentState() == Car.TeslaState.Inactive || inactiveFlag == "1")
                    {
                        continue; //skip inactive cars
                    }

                    string vin = car["vin"]?.ToString();
                    string display_name = car["display_name"]?.ToString();

                    if (!String.IsNullOrEmpty(vin))
                    {
                        Logfile.Log($"MQTT: car found: {display_name}");
                        h.Add(vin);
                    }
                }
            }
            catch (Newtonsoft.Json.JsonException jsonEx)
            {
                Logfile.Log($"MQTT: Cars JSON parse error: {jsonEx.Message}");
                jsonEx.ToExceptionless().FirstCarUserID().Submit();
                // Removed Thread.Sleep(20000) - ARM32 blocking eliminated
                // Retry occurs at next iteration of work cycle
            }
            catch (Exception ex)
            {
                Logfile.Log($"MQTT: HashSet Exception: {ex}");
                ex.ToExceptionless().FirstCarUserID().Submit();
                // Removed Thread.Sleep(20000) - ARM32 blocking eliminated
                // Retry occurs at next iteration of work cycle
            }

            return h;

        }

        private static string RetrieveJsonString(string url)
        {
            return MQTTWebDownloader.GetSingleton().DownloadString(url);
        }

        internal void PublishDiscovery(string vin)
        {

            int carId = Car.GetCarIDFromVIN(vin);
            if(carId <= 0)
            {
                Logfile.Log($"MQTT: AutoDiscovery for {vin}: car not found");
                return;
            }
            string model = "Model " + vin[3]; //Car.GetCarByID(carId).CarType;
            var car = Car.GetCarByID(carId);
            if (car is null)
            {
                Logfile.Log($"MQTT: AutoDiscovery for {vin}: car {carId} not found or not active");
            }
            string name = car.DisplayName;
            string sw = car.CurrentJSON.current_car_version;

            foreach (string entity in MQTTAutoDiscovery.autoDiscovery.Keys)
            {
                Dictionary<string, string> entitycontainer = MQTTAutoDiscovery.autoDiscovery[entity];
                
                //mandotory
                entitycontainer.TryGetValue("name", out string entityName);
                entitycontainer.TryGetValue("type", out string entityType);

                entitycontainer.TryGetValue("discovery_active", out string active);
                if (active == "true")
                {

                    Dictionary<string, object> device = new()
                {
                   { "ids", vin },
                   { "mf", "Tesla" },
                   { "mdl", model },
                   { "name", name },
                   { "sw", sw }
                };
                    Dictionary<string, object> entityConfig = new()
                {
                   { "dev", device }
                };

                    
                    //optional
                    entitycontainer.TryGetValue("icon", out string entityIcon);
                    entitycontainer.TryGetValue("class", out string entityClass);
                    entitycontainer.TryGetValue("unit", out string entityUnit);
                    //type dependent:
                    //switch
                    entitycontainer.TryGetValue("pl_on", out string entityTextOn);
                    entitycontainer.TryGetValue("pl_off", out string entityTextOff);
                    entitycontainer.TryGetValue("cmd_topic", out string entityControlTopic);
                    //number
                    entitycontainer.TryGetValue("min", out string entityMin);
                    entitycontainer.TryGetValue("max", out string entityMax);
                    entitycontainer.TryGetValue("step", out string entityStep);

                    entityConfig.Add("name", entityName);
                    entityConfig.Add("uniq_id", vin + "_" + entity);
                    entityConfig.Add("stat_t", $"{topic}/car/{vin}/{entity}");


                    if (entityIcon is not null)
                    {
                        entityConfig.Add("icon", entityIcon);
                    }
                    if (entityClass is not null)
                    {
                        entityConfig.Add("dev_cla", entityClass);
                    }
                    if (entityUnit is not null)
                    {
                        entityConfig.Add("unit_of_meas", entityUnit);
                    }
                    if (entityTextOn is not null)
                    {
                        entityConfig.Add("pl_on", entityTextOn);
                    }
                    if (entityTextOff is not null)
                    {
                        entityConfig.Add("pl_off", entityTextOff);
                    }
                    if (entityControlTopic is not null)
                    {
                        entityConfig.Add("cmd_t", $"{topic}/command/{vin}/{entityControlTopic}");
                    }
                    if (entityMin is not null)
                    {
                        entityConfig.Add("min", entityMin);
                    }
                    if (entityMax is not null)
                    {
                        entityConfig.Add("max", entityMax);
                    }
                    if (entityStep is not null)
                    {
                        entityConfig.Add("step", entityStep);
                    }
                    var configJson = JsonConvert.SerializeObject(entityConfig);

                    client.Publish($"{discoverytopic}/{entityType}/{vin}/{entity}/config", Encoding.UTF8.GetBytes(configJson ?? "NULL"),
                                        MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
                    
                    Tools.DebugLog($"MQTT: AutoDiscovery for {vin}: " + entity);
                }
                else
                {
                    //if discovery_active is false or null, delete retainded discovery message from broker: send "null" to discovery config topic
                    client.Publish($"{discoverytopic}/{entityType}/{vin}/{entity}/config", null,
                                        MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
                    Tools.DebugLog($"MQTT: AutoDiscovery removed {vin}: " + entity);
                }
                
            }

            //special case: GPS Tracker
            string dicoveryGPSTracker = JsonConvert.SerializeObject(new
            {
                name = name,
                json_attributes_topic = $"{topic}/car/{vin}/gps_tracker",
                state_topic = $"{topic}/car/{vin}/TLGeofenceIsHome",
                payload_home = "true",
                payload_not_home = "false"
            }) ;

            client.Publish($"{discoverytopic}/device_tracker/{vin}/config", Encoding.UTF8.GetBytes(dicoveryGPSTracker ?? "NULL"),
                    MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
            
            Tools.DebugLog($"MQTT: AutoDiscovery for {vin}: device_tracker");

        }

        internal void PublichGPSTracker(string vin, double lat, double lon)
        {
            try
            {

                string gpsTrackerTopic = $"{topic}/car/{vin}/gps_tracker";

                string json = JsonConvert.SerializeObject(new { latitude = lat, longitude = lon, gps_accuracy = 1.0 });

                client.Publish(gpsTrackerTopic, Encoding.UTF8.GetBytes(json ?? "NULL"),
                                    MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, true);
            }
            catch (Exception ex)
            {
                Logfile.Log("MQTT: PublichGPSTracker Exception: " + ex.Message);
                ex.ToExceptionless().FirstCarUserID().Submit();
                // Removed Thread.Sleep(60000) - ARM32 blocking eliminated
                // Next GPS update will retry
            }

        }

        internal void PublishMqttValue(string vin, String name, object newvalue)
        {
            string carTopic = $"{topic}/car/{vin}";
            string jsonTopic = $"{topic}/json/{vin}";
            try
            {
                // Note: Using synchronous check for backward compatibility
                // In future, convert call sites to async pattern
                if(client?.IsConnected == true)
                {
                    client.Publish(carTopic + "/" + name, Encoding.UTF8.GetBytes(newvalue.ToString() ?? "NULL"),
                                    MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE, true);
                }

            }
            catch (Exception ex)
            {
                Logfile.Log("MQTT: PublishMqttValue Exception: " + ex.Message);
                ex.ToExceptionless().FirstCarUserID().Submit();
                // Removed Thread.Sleep(60000) - ARM32 blocking eliminated
                // Next publish attempt will handle retry
            }
        }
    }

    internal class MQTTWebDownloader : IWebDownloader
    {
        private static IWebDownloader _instance;

        public static IWebDownloader GetSingleton() => _instance ?? (_instance = new MQTTWebDownloader());

        public string DownloadString(string url)
        {
            string json;
            using (WebClient wc = new WebClient())
            {
                json = wc.DownloadString(url);
            }

            return json;
        }
    }

    internal class MqttClientWrapper : IMqttClient
    {
        private MQTTnet.Client.IMqttClient _client;
        private string _brokerHost = "";
        private int _brokerPort;

        public bool IsConnected => _client?.IsConnected ?? false;

        #pragma warning disable CS0067 // Event never used
        public event MqttMsgPublishEventHandler MqttMsgPublishReceived;
        #pragma warning restore CS0067

        public static IMqttClient CreateClient(string brokerHostName, int brokerPort, bool secure, X509Certificate caCert, X509Certificate clientCert, MqttSslProtocols sslProtocol)
        {
            var wrapper = new MqttClientWrapper();
            wrapper._brokerHost = brokerHostName;
            wrapper._brokerPort = brokerPort;
            var factory = new MqttFactory();
            wrapper._client = factory.CreateMqttClient();
            // TODO: Fix MQTT message received event handler for MQTTnet v4
            // The event name and signature need to be determined for MQTTnet 4.3.6.1152
            return wrapper;
        }

        private MqttClientWrapper() { }

        /// <summary>
        /// Synchronous wrapper for MQTT broker connection.
        /// USE: For backward compatibility only. Prefer ConnectAsync() for new code.
        /// </summary>
        /// <remarks>
        /// This method uses sync-over-async pattern for API compatibility.
        /// If called from async context, use ConnectAsync() instead to avoid thread pool blocking.
        /// </remarks>
        public byte Connect(string clientId, string username, string password, bool willRetain, byte willQosLevel, bool willFlag, string willTopic, string willMessage, bool cleanSession, ushort keepAlivePeriod)
        {
            try
            {
                return ConnectAsync(clientId, username, password, willRetain, willQosLevel, willFlag, willTopic, willMessage, cleanSession, keepAlivePeriod).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return 1; // Error code
            }
        }

        /// <summary>
        /// Asynchronously connects to MQTT broker with proper async/await pattern.
        /// RECOMMENDED method for .NET 8 modern async code.
        /// </summary>
        /// <remarks>
        /// Use this method in async contexts to avoid blocking thread pool threads.
        /// </remarks>
        public async Task<byte> ConnectAsync(string clientId, string username, string password, bool willRetain, byte willQosLevel, bool willFlag, string willTopic, string willMessage, bool cleanSession, ushort keepAlivePeriod, CancellationToken ct = default)
        {
            var builder = new MqttClientOptionsBuilder()
                .WithClientId(clientId)
                .WithTcpServer(_brokerHost, _brokerPort);

            if (!string.IsNullOrEmpty(username)) builder.WithCredentials(username, password);
            builder.WithCleanSession(cleanSession);
            builder.WithKeepAlivePeriod(TimeSpan.FromSeconds(keepAlivePeriod));

            var options = builder.Build();
            await _client.ConnectAsync(options, ct).ConfigureAwait(false);
            return 0;
        }

        /// <summary>
        /// Synchronous wrapper for MQTT message publishing.
        /// USE: For backward compatibility only. Prefer PublishAsync() for new code.
        /// </summary>
        public ushort Publish(string topic, byte[] message, byte qosLevel, bool retain)
        {
            try
            {
                return PublishAsync(topic, message, qosLevel, retain).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return 1; // Error code
            }
        }

        /// <summary>
        /// Asynchronously publishes message to MQTT broker.
        /// RECOMMENDED method for .NET 8 modern async code.
        /// </summary>
        public async Task<ushort> PublishAsync(string topic, byte[] message, byte qosLevel, bool retain, CancellationToken ct = default)
        {
            var appMsg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(message)
                .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qosLevel)
                .WithRetainFlag(retain)
                .Build();
            await _client.PublishAsync(appMsg, ct).ConfigureAwait(false);
            return 0;
        }

        /// <summary>
        /// Synchronous wrapper for MQTT topic subscription.
        /// USE: For backward compatibility only. Prefer SubscribeAsync() for new code.
        /// </summary>
        public ushort Subscribe(string[] topics, byte[] qosLevels)
        {
            try
            {
                return SubscribeAsync(topics, qosLevels).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return 1; // Error code
            }
        }

        /// <summary>
        /// Asynchronously subscribes to MQTT topics.
        /// RECOMMENDED method for .NET 8 modern async code.
        /// </summary>
        public async Task<ushort> SubscribeAsync(string[] topics, byte[] qosLevels, CancellationToken ct = default)
        {
            List<MQTTnet.Packets.MqttTopicFilter> filters = new();
            for (int i = 0; i < topics.Length; i++)
            {
                filters.Add(new MqttTopicFilterBuilder()
                    .WithTopic(topics[i])
                    .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qosLevels[i])
                    .Build());
            }
            await _client.SubscribeAsync(new MQTTnet.Client.MqttClientSubscribeOptions() { TopicFilters = filters }, ct).ConfigureAwait(false);
            return 0;
        }

        /// <summary>
        /// Synchronous wrapper for MQTT topic unsubscription.
        /// USE: For backward compatibility only. Prefer UnsubscribeAsync() for new code.
        /// </summary>
        public ushort Unsubscribe(string[] topics)
        {
            try
            {
                return UnsubscribeAsync(topics).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return 1; // Error code
            }
        }

        /// <summary>
        /// Asynchronously unsubscribes from MQTT topics.
        /// RECOMMENDED method for .NET 8 modern async code.
        /// </summary>
        public async Task<ushort> UnsubscribeAsync(string[] topics, CancellationToken ct = default)
        {
            await _client.UnsubscribeAsync(new MQTTnet.Client.MqttClientUnsubscribeOptions() { TopicFilters = topics.ToList() }, ct).ConfigureAwait(false);
            return 0;
        }
    }
}


