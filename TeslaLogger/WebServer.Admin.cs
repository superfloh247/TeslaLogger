using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Caching;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Exceptionless;
using Newtonsoft.Json;
using System.Web;
using System.Net.Http;
using HttpMultipartParser;
using System.Reflection;
using System.Xml.Linq;

namespace TeslaLogger
{
    public partial class WebServer
    {
        // Admin panel endpoint handlers (extracted from WebServer.cs)
        // These methods handle administrative operations like backups, updates, configuration
        
        private void Admin_Writefile(HttpListenerRequest request, HttpListenerResponse response)
        {
            var u = request.Url;
            string filename = u.Segments[2].ToString();

            bool allowedFiles = allowed_getfiles.Contains(filename);

            System.Diagnostics.Debug.WriteLine($"Webserver writefile: {filename}");

            if (!allowedFiles)
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                WriteString(response, @"Forbidden!");
            }

            string p = FileManager.GetFilePath(filename);

            if (!File.Exists(p))
            {
                p = p.Replace(@"Debug\", "");
                p = p.Replace(@"net8.0\", "");
            }

            if (filename == "settings.json")
                p = FileManager.GetFilePath(TLFilename.SettingsFilename);
            else if (filename == "geofence-private.csv")
            {
                p = FileManager.GetFilePath(TLFilename.GeofencePrivateFilename);
            }

            System.Diagnostics.Debug.WriteLine($"Webserver writefile: {p}");
            Logfile.Log($"Webserver writefile: {p}");

            if (File.Exists(p))
                File.Delete(p);

            string data = GetDataFromRequestInputStream(request);

            var pd = Path.GetDirectoryName(p);
            if (!Directory.Exists(pd))
                Directory.CreateDirectory(pd);


            File.WriteAllText(p, data);
            WriteString(response, "ok");
            return;
        }

        private void Admin_Getfile(HttpListenerRequest request, HttpListenerResponse response)
        {
            var u = request.Url;
            string filename = u.Segments[2].ToString();

            bool allowedFiles = (filename.StartsWith("language-") && filename.EndsWith(".txt"))
                || allowed_getfiles.Contains(filename);

            System.Diagnostics.Debug.WriteLine($"Webserver getfile: {filename}");

            if (!allowedFiles)
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                WriteString(response, @"Forbidden!");
            }

            string p = FileManager.GetFilePath(filename);

            if (!File.Exists(p))
            {
                p = p.Replace(@"Debug\", "");
                p = p.Replace(@"net8.0\", "");
            }

            if (!File.Exists(p))
            {
                p = p.Replace(@"Debug/", "");
                p = p.Replace(@"net8.0/", "");
            }

            if (filename == "settings.json")
                p = FileManager.GetFilePath(TLFilename.SettingsFilename);
            else if (filename == "geofence-private.csv")
                p = FileManager.GetFilePath(TLFilename.GeofencePrivateFilename);

            System.Diagnostics.Debug.WriteLine($"Webserver getfile: {p}");

            if (File.Exists(p))
            {
                string content = File.ReadAllText(p);
                WriteString(response, content);
                return;
            }

            response.StatusCode = (int)HttpStatusCode.NotFound;
            WriteString(response, @"File Not Found!");
        }

        private static void Admin_SetCarInactive(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                Logfile.Log("SetCarInactive");

                string data = GetDataFromRequestInputStream(request);

                dynamic r = JsonConvert.DeserializeObject(data);

                int id = Convert.ToInt32(r["id"]);

                if (Tools.IsPropertyExist(r, "deactivatecar"))
                {
                    Logfile.Log($"Set Car Inactive #{id}");

                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();

                        using (var cmd2 = new MySqlCommand(@"
UPDATE
  cars
SET
  tesla_password = NULL,
  tesla_token = NULL,
  refresh_token = NULL,
  tesla_name = """"
WHERE
  id = @id", con))
                        {
                            cmd2.Parameters.AddWithValue("@id", id);
                            _ = SQLTracer.TraceNQ(cmd2, out _);

                            Car c = Car.GetCarByID(id);
                            if (c is not null)
                            {
                                c.Log("Set Car Inactive");
                                c.ExitCarThread("Car deactivated!");
                            }

                            WriteString(response, "OK");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().MarkAsCritical().Submit();
                WriteString(response, "ERROR");
                Logfile.Log(ex.ToString());
            }
        }

        private static void Admin_GetVersion(HttpListenerRequest _, HttpListenerResponse response)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            WriteString(response, version);
        }

        private static void Admin_RestoreChargingCostsFromBackup3(HttpListenerRequest request, HttpListenerResponse response)
        {
            Logfile.Log("RestoreChargingCostsFromBackup3");
            string errorText = string.Empty;
            StringBuilder html = new StringBuilder();
            html.Append(@"
<html>
    <head>
    </head>
    <body>
        <h2>Restore chargingstate cost_per_minute and cost_per_session from backup - step 3 of 3</h2>
        <br />
        <ul>");
            if (request.HttpMethod == HttpMethod.Post.Method)
            {
                try
                {
                    using (Stream stream = request.InputStream) // here we have data
                    {
                        using (var reader = new StreamReader(stream, request.ContentEncoding))
                        {
                            string body = reader.ReadToEnd();
                            // body contains key=value pairs id=id separated by &
                            // eg 1834=1834&1835=1835
                            var kvps = HttpUtility.ParseQueryString(body);
                            foreach (string sID in kvps.AllKeys)
                            {
                                if (int.TryParse(sID, out int id))
                                {
                                    Tools.DebugLog($"restore id:{id}");
                                    int CarID = int.MinValue;
                                    // get data from chargingstate_bak
                                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                                    {
                                        con.Open();
                                        using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE
    chargingstate
SET
    cost_per_minute =(
    SELECT
        cost_per_minute
    FROM
        chargingstate_bak
    WHERE
        id = @id
),
    cost_per_session =(
    SELECT
        cost_per_session
    FROM
        chargingstate_bak
    WHERE
        id = @id
)WHERE
    chargingstate.id = @id", con))
                                        {
                                            cmd.Parameters.AddWithValue("@id", id);
                                            _ = SQLTracer.TraceNQ(cmd, out _);
                                        }
                                    }
                                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                                    {
                                        con.Open();
                                        using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    CarID
FROM
    chargingstate
WHERE
    id = @id", con))
                                        {
                                            cmd.Parameters.AddWithValue("@id", id);
                                            MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                                            if (dr.Read() && dr[0] != DBNull.Value)
                                            {
                                                _ = int.TryParse(dr[0].ToString(), out CarID);
                                            }
                                        }
                                    }
                                    if (CarID > 0)
                                    {
                                        Car car = Car.GetCarByID(CarID);
                                        if (car is not null)
                                        {
                                            car.DbHelper.UpdateChargePrice(id, true);
                                            html.Append($@"
            <li>successfully updated id:{id} from backup - cost_total has been recalculated</li>");
                                        }
                                        else
                                        {
                                            errorText += $"<br/> unable to find car for CarID:{CarID}";
                                        }
                                    }
                                    else
                                    {
                                        errorText += $"<br/> unable to find CarID for chargingstate.id:{id}";
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logfile.Log(ex.ToString());
                    ex.ToExceptionless().FirstCarUserID().Submit();
                    errorText = ex.ToString();
                }
            }
            else
            {
                errorText = $"http method error";
            }
            if (!string.IsNullOrEmpty(errorText))
            {
                WriteString(response, errorText);
                return;
            }
            html.Append(@"
        </ul>
    </body>
</html>");
            WriteString(response, html.ToString(), "text/html");
        }

        private static void Admin_RestoreChargingCostsFromBackup2(HttpListenerRequest request, HttpListenerResponse response)
        {
            Logfile.Log("RestoreChargingCostsFromBackup2");
            string errorText = string.Empty;
            string fileName = string.Empty;
            bool removeFile = false;
            string sqlExtract = string.Empty;
            string sqlCreate = string.Empty;
            StringBuilder html = new StringBuilder();
            // receive file name or uploaded file
            if (request.HttpMethod == HttpMethod.Post.Method)
            {
                RestoreChargingCostsFromBackupReceiveFile(request, ref fileName, ref removeFile);
                // filename or file received, now check file
                if (!string.IsNullOrEmpty(fileName))
                {
                    RestoreChargingCostsFromBackupCheckReceivedFile(ref errorText, fileName, ref sqlExtract);
                }
                if (string.IsNullOrEmpty(errorText))
                {
                    // INSERT statement extracted successfully, now extract CREATE TABLE
                    RestoreChargingCostsFromBackupCreateTable(ref errorText, fileName, ref sqlCreate);
                }
                if (string.IsNullOrEmpty(errorText))
                {
                    // both SQL files created successfully, now load into DB
                    RestoreChargingCostsFromBackupLoadDB(ref errorText, sqlExtract, sqlCreate);
                }
                if (string.IsNullOrEmpty(errorText))
                {
                    // chargingstate_bak loaded successfully
                    RestoreChargingCostsFromBackupCompare(response, ref errorText, html);
                }
            }
            else
            {
                errorText = $"http method error";
            }
            if (!string.IsNullOrEmpty(errorText))
            {
                WriteString(response, errorText);
                return;
            }
            WriteString(response, html.ToString(), "text/html");
        }

        private static void Admin_GetCarsFromAccount(HttpListenerRequest request, HttpListenerResponse response, bool fleetAPI)
        {
            string responseString = "";
            string contentType = null;

            try
            {
                Logfile.Log("GetCarsFromAccount");
                string data = GetDataFromRequestInputStream(request);
                dynamic r = JsonConvert.DeserializeObject(data);

                string access_token = r["access_token"];
                var car = new Car(-1, "", "", -1, access_token, DateTime.Now, "", "", "", "", "", "", "", 0.0, fleetAPI); // TODO Check
                car.webhelper.Tesla_token = access_token;

                if (fleetAPI)
                {
                    car.SetCurrentState(Car.TeslaState.Online);
                    car.webhelper.GetRegion();
                }

                car.webhelper.GetAllVehicles(out string resultContent, out Newtonsoft.Json.Linq.JArray vehicles, true);

                if (vehicles is null)
                {
                    if (resultContent?.Contains("error_description") == true)
                    {
                        dynamic j = JsonConvert.DeserializeObject(resultContent);
                        string error = j["error"] ?? "NULL";
                        string error_description = j["error_description"] ?? "NULL";

                        responseString = $"ERROR: {error} / Error Description: {error_description}";

                        Logfile.Log(responseString);
                        car.CreateExeptionlessLog("GetCarsFromAccount", responseString, Exceptionless.Logging.LogLevel.Fatal).Submit();

                        WriteString(response, responseString);
                        return;
                    }
                }

Logfile.Log($"Found {vehicles.Count} Vehicles");

                var o = new List<KeyValuePair<string, string>>();
                o.Add(new KeyValuePair<string, string>("", "Please Select"));

                for (int x = 0; x < vehicles.Count; x++)
                {
                    var cc = vehicles[x];
                    if(cc is null)
                    {
                        Logfile.Log($"Car #{x} was invalid");
                        Tools.DebugLog($"Car #{x} was invalid in response \"{resultContent}\"");
                        continue;
                    }
                    var ccVin = cc["vin"]?.ToString();
                    var ccDisplayName = cc["display_name"]?.ToString();
                    if (ccVin is null)
                    {
                        var resourceType = cc["resource_type"];
                        if (resourceType is null)
                        {
                            Logfile.Log($"Car #{x} has invalid VIN");
                            Tools.DebugLog($"Car #{x} has invalid VIN in response \"{resultContent}\"");
                        }
                        else
                        {
                            Logfile.Log($"Car #{x} was not a car, but {resourceType}. Ignoring...");
                        }
                        continue;
                    }
                    if (ccDisplayName is null)
                    {
                        Logfile.Log($"Car #{x} has invalid display name");
                        Tools.DebugLog($"Car #{x} has invalid display_name in response \"{resultContent}\"");
                        ccDisplayName = "";
                    }

                    o.Add(new KeyValuePair<string, string>(ccVin.ToString(), $"VIN: {ccVin} / Name: {ccDisplayName}"));
                }

                responseString = JsonConvert.SerializeObject(o);
                contentType = "application/json";

            }
            catch (UnauthorizedAccessException)
            {
                responseString = "Unauthorized";
                Logfile.Log("Wrong Access Token!!!");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            WriteString(response, responseString, contentType);
        }

        private static void Admin_Wallbox(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                Logfile.Log("Wallbox");

                string data = GetDataFromRequestInputStream(request);

                dynamic r = JsonConvert.DeserializeObject(data);

                if (Tools.IsPropertyExist(r, "test"))
                {
                    Logfile.Log("Test Wallbox");

                    string type = r["type"];
                    string host = r["host"];
                    string param = r["param"];

                    ElectricityMeterBase e = ElectricityMeterBase.Instance(type, host, param);

                    var obj = new
                    {
                        Version = e.GetVersion(),
                        Utility_kWh = e.GetUtilityMeterReading_kWh(),
                        Vehicle_kWh = e.GetVehicleMeterReading_kWh()
                    };

                    string ret = JsonConvert.SerializeObject(obj);

                    WriteString(response, ret, "application/json");
                }
                else if (Tools.IsPropertyExist(r, "save"))
                {
                    var carid = r["carid"];
                    
                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand("update cars set meter_type=@meter_type, meter_host=@meter_host, meter_parameter=@meter_parameter where id=@carid", con))
                        {
                            cmd.Parameters.AddWithValue("@carid", r["carid"]);
                            cmd.Parameters.AddWithValue("@meter_type", r["type"]);
                            cmd.Parameters.AddWithValue("@meter_host", r["host"]);
                            cmd.Parameters.AddWithValue("@meter_parameter", r["param"]);
                            _ = SQLTracer.TraceNQ(cmd, out _);

                            WriteString(response, "OK");
                        }
                    }
                }
                else if (Tools.IsPropertyExist(r, "load"))
                {
                    int carid = r["carid"];
                    var dr = DBHelper.GetCar(carid);
                    var obj = new
                    {
                        type = dr["meter_type"],
                        host = dr["meter_host"],
                        param = dr["meter_parameter"]
                    };

                    string ret = JsonConvert.SerializeObject(obj);
                    WriteString(response, ret, "application/json");
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
                WriteString(response, "error");
            }
        }

        private static void Admin_SetAdminPanelPassword(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                Logfile.Log("SetAdminPanelPassword");

                string data = GetDataFromRequestInputStream(request);
                string file_htaccess = "/var/www/html/.htaccess";

                dynamic r = JsonConvert.DeserializeObject(data);

                if (Tools.IsPropertyExist(r, "delete") || request?.QueryString?["delete"] == "1")
                {
                    Logfile.Log("delete Admin Panel Password");
                    
                    if (File.Exists(file_htaccess))
                    {
                        File.Delete(file_htaccess);
                        Logfile.Log($"delete: {file_htaccess}");
                        WriteString(response, "OK");
                        return;
                    }
                    WriteString(response, "ERROR");
                }
                else if (Tools.IsPropertyExist(r, "password"))
                {
                    Logfile.Log("set Admin Panel Password");

                    string content = "AuthType Basic\n";
                    content += "AuthName \"Restricted Area\"\n";
                    content += "AuthUserFile /etc/teslalogger/.htpasswd\n";
                    content += "Require valid-user\n";

                    File.WriteAllText(file_htaccess, content);

                    string password = r["password"];
#pragma warning disable CA5350 // Keine schwachen kryptografischen Algorithmen verwenden
                    using (SHA1 sha1 = SHA1.Create())
#pragma warning restore CA5350 // Keine schwachen kryptografischen Algorithmen verwenden
                    {
                        byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(password));
                        content = string.Format(Tools.ciEnUS, "{0}:{{SHA}}{1}", "admin", Convert.ToBase64String(hash));
                    }
                    string filename_htpasswd = "/etc/teslalogger/.htpasswd";
                    File.WriteAllText(filename_htpasswd, content);
                    WriteString(response, "OK");
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                WriteString(response, "ERROR");
                Logfile.Log(ex.ToString());
            }
        }

        private static void Admin_DownloadLogs(HttpListenerRequest request, HttpListenerResponse response)
        {
            Queue<string> result = new();
            // set defaults
            DateTime startdt = DateTime.Now.AddHours(-48);
            DateTime enddt = DateTime.Now.AddSeconds(1);
            // parse query string
            if (request.QueryString.Count > 0 && request.QueryString.HasKeys())
            {
                foreach (string key in request.QueryString.AllKeys)
                {
                    if (request.QueryString.GetValues(key).Length == 1)
                    {
                        switch (key)
                        {
                            case "from":
                                Tools.DebugLog($"from {request.QueryString.GetValues(key)[0]}");
                                if (!DateTime.TryParse(request.QueryString.GetValues(key)[0], out startdt))
                                {
                                    startdt = DateTime.Now.AddHours(-48);
                                }
                                break;
                            case "to":
                                Tools.DebugLog($"to {request.QueryString.GetValues(key)[0]}");
                                if (!DateTime.TryParse(request.QueryString.GetValues(key)[0], out enddt))
                                {
                                    enddt = DateTime.Now.AddSeconds(1);
                                }
                                break;
                            default:
                                break;
                        }
                    }
                }
            }
            if (File.Exists(Path.Combine(Logfile.GetExecutingPath(), "nohup.out")))
            {
                System.Globalization.CultureInfo ciDeDE = new System.Globalization.CultureInfo("de-DE");
                int linenumber = 0;
                int startlinenumber = 0;
                int endlinennumber = 0;
                int TLstartlinenumber = 0;
                string startdate = startdt.ToString(ciDeDE);
                string enddate = enddt.ToString(ciDeDE);
                Tools.DebugLog($"startdate {startdate}");
                Tools.DebugLog($"enddate {enddate}");
                // parse nohup.out
                foreach (string line in File.ReadAllLines(Path.Combine(Logfile.GetExecutingPath(), "nohup.out")))
                {
                    if (startlinenumber == 0)
                    {
                        if (line.Contains(" : TeslaLogger Version: "))
                        {
                            TLstartlinenumber = linenumber;
                        }
                    }
                    if (startlinenumber == 0 && line.Length > startdate.Length && DateTime.TryParse(line.Substring(0, startdate.Length), ciDeDE, System.Globalization.DateTimeStyles.AssumeLocal, out DateTime linedt) && linedt >= startdt)
                    {
                        startlinenumber = linenumber;
                    }
                    if (endlinennumber == 0 && line.Length > startdate.Length && DateTime.TryParse(line.Substring(0, enddate.Length), ciDeDE, System.Globalization.DateTimeStyles.AssumeLocal, out linedt) && linedt >= enddt)
                    {
                        endlinennumber = linenumber;
                    }
                    linenumber++;
                }
                Tools.DebugLog($"linenumber {linenumber}");
                if (endlinennumber == 0)
                {
                    endlinennumber = linenumber - 1;
                }
                Tools.DebugLog($"TLstartlinenumber {TLstartlinenumber}");
                Tools.DebugLog($"startlinenumber {startlinenumber}");
                Tools.DebugLog($"endlinennumber {endlinennumber}");
                // grab line from nohup.out
                linenumber = 0;
                // do TLstartlinenumber + 17 and startlinenumber overlap?
                if (startlinenumber - TLstartlinenumber < 17)
                {
                    startlinenumber += 17 - (TLstartlinenumber - startlinenumber);
                }
                foreach (string line in File.ReadAllLines(Path.Combine(Logfile.GetExecutingPath(), "nohup.out")))
                {
                    // TL start was before startlinenumber
                    if (TLstartlinenumber < startlinenumber)
                    {
                        if (linenumber >= TLstartlinenumber && linenumber <= TLstartlinenumber + 17)
                        {
                            result.Enqueue(line);
                        }
                    }
                    if (linenumber >= startlinenumber && linenumber <= endlinennumber)
                    {
                        result.Enqueue(line);
                    }
                    linenumber++;
                }
            }
            WriteString(response, string.Join(Environment.NewLine, result));
        }

        private static void Admin_OpenTopoDataQueue(HttpListenerRequest request, HttpListenerResponse response)
        {
            Logfile.Log("Admin: OpenTopoDataQueue ...");
            if (Tools.UseOpenTopoData())
            {
                double queue = OpenTopoDataService.GetSingleton().QueueLength;
                // 100 pos every 2 Minutes
                WriteString(response, $"OpenTopoData Queue contains {queue} positions. It will take approx {Math.Ceiling(queue / 100) * 2} minutes to process.");
            }
            else
            {
                WriteString(response, "OpenTopoData is disabled");
            }
        }

        private static void Admin_ExportTrip(HttpListenerRequest request, HttpListenerResponse response)
        {
            // source: https://github.com/rowich/Teslalogger2gpx/blob/master/Teslalogger2GPX.ps1
            // parse request
            if (request.QueryString.Count == 3 && request.QueryString.HasKeys())
            {
                long from = long.MinValue;
                long to = long.MinValue;
                int carID = int.MinValue;
                foreach (string key in request.QueryString.AllKeys)
                {
                    if (request.QueryString.GetValues(key).Length == 1)
                    {
                        switch (key)
                        {
                            case "from":
                                _ = long.TryParse(request.QueryString.GetValues(key)[0], out from);
                                break;
                            case "to":
                                _ = long.TryParse(request.QueryString.GetValues(key)[0], out to);
                                break;
                            case "carID":
                                _ = int.TryParse(request.QueryString.GetValues(key)[0], out carID);
                                break;
                            default:
                                break;
                        }
                    }
                }
                if (from != long.MinValue && to != long.MinValue && carID != int.MinValue)
                {
                    // request parsed successfully
                    // create GPX header
                    StringBuilder GPX = new StringBuilder();
                    GPX.Append(@"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" creator=""Teslalogger GPX Export"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:gpxtpx=""http://www.garmin.com/xmlschemas/TrackPointExtension/v1"" xmlns:gpxx=""http://www.garmin.com/xmlschemas/GpxExtensions/v3"" elementFormDefault=""qualified"">
<metadata>
    <name>teslalogger.gpx</name>
</metadata>
");
                    string DateLast = "n/a";
                    string PosLast = "n/a";
                    // now get pos data
                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand("SELECT lat,lng,Datum,altitude,address FROM pos WHERE id >= @from AND id <= @to and CarID = @CarID ORDER BY Datum ASC", con))
                        {
                            cmd.Parameters.AddWithValue("@from", from);
                            cmd.Parameters.AddWithValue("@to", to);
                            cmd.Parameters.AddWithValue("@CarID", carID);
                            MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                            while (dr.Read())
                            {
                                if (dr[0] is Double && 
                                     dr[1] is Double &&
                                     dr[2] is DateTime)
                                {
                                    double lat = (double)dr[0];
                                    double lng = (double)dr[1];
                                    DateTime Datum = (DateTime)dr[2];

                                    string Pos = ($"lat=\"{lat.ToString(Tools.ciEnUS)}\" lon=\"{lng.ToString(Tools.ciEnUS)}\"");
                                    if (!Pos.Equals(PosLast, System.StringComparison.Ordinal))
                                    {
                                        // convert date/time into GPX format (insert a "T")
                                        // 2020-01-30 09:19:55 --> 2020-01-30T09:19:55
                                        string Date = $"{Datum:yyyy-MM-dd}"+"T"+$"{Datum:HH:mm:ss}";
                                        string alt = "";
                                        if (double.TryParse(dr[3].ToString(), out double altitude))
                                        {
                                            alt = $"<ele>{altitude}</ele>";
                                        }
                                        string name = "";
                                        if (dr[4] is not null && dr[4] != DBNull.Value)
                                        {
                                            name = $"<name>{SecurityElement.Escape(dr[4].ToString())}</name>";
                                        }
                                        // create new Track element if day has changed since last element. New track node gets the name of the day (allows filtering for days later on)
                                        if (!DateLast.Equals(Date.Substring(0, 10), System.StringComparison.Ordinal))
                                        {
                                            if (!DateLast.Equals("n/a", System.StringComparison.Ordinal))
                                            {
                                            GPX.Append($"</trkseg></trk>{Environment.NewLine}");
                                            }
                                            DateLast = Date.Substring(0, 10);
                                            GPX.Append($"<trk><name>{DateLast}</name><trkseg>{Environment.NewLine}");
                                        }
                                        GPX.Append($"    <trkpt {Pos}>{alt}<time>{Date}</time>{name}</trkpt>{Environment.NewLine}");
                                        PosLast = Pos;
                                    }
                                }
                            }
                        }
                    }
                    // create GPX footer
                    GPX.Append(@"</trkseg>
</trk>
</gpx>
");
                    response.AddHeader("Content-Type", "application/gpx+xml; charset=utf-8");
                    response.AddHeader("Content-Disposition", "inline; filename=\"trip.gpx\"");
                    Tools.DebugLog($"GPX:{Environment.NewLine}{GPX}");
                    WriteString(response, GPX.ToString(), "application/gpx+xml");
                }
                else
                {
                    WriteString(response, "error parsing request");
                }
            }
            else
            {
                WriteString(response, "malformed request");
            }
        }

        private static void Admin_passwortinfo(HttpListenerRequest request, HttpListenerResponse response)
        {
            System.Diagnostics.Debug.WriteLine("passwortinfo");
            string data = GetDataFromRequestInputStream(request);
            int id = 0;

            if (String.IsNullOrEmpty(data))
            {
                id = Convert.ToInt32(request.QueryString["id"], Tools.ciEnUS);
            }
            else
            {
                dynamic r = JsonConvert.DeserializeObject(data);
                id = Convert.ToInt32(r["id"]);
            }

            var c = Car.GetCarByID(id);
            if (c is not null)
                WriteString(response, c.Passwortinfo.ToString());
            else
                WriteString(response, $"CarId not found: {id}");
        }

        private static void Admin_UpdateGrafana(HttpListenerRequest request, HttpListenerResponse response)
        {
            Tools.lastGrafanaSettings = DateTime.UtcNow.AddDays(-1);
            _ = Task.Run(() => { UpdateTeslalogger.UpdateGrafana(); });
            Tools._StreamingPos = null;
            WriteString(response, @"OK");
        }

        private static void Admin_Update(HttpListenerRequest request, HttpListenerResponse response)
        {
            // TODO copy what update.php does
            WriteString(response, "");
        }

        private static void Admin_SetPassword(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                Logfile.Log("SetPassword");

                string data = GetDataFromRequestInputStream(request);

                dynamic r = JsonConvert.DeserializeObject(data);

                int id = Convert.ToInt32(r["id"]);

                if (Tools.IsPropertyExist(r, "deletecar"))
                {
                    Logfile.Log($"Delete Car #{id}");

                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();


                        using (var cmd2 = new MySqlCommand("delete from cars where id = @id", con))
                        {
                            cmd2.Parameters.AddWithValue("@id", id);
                            _ = SQLTracer.TraceNQ(cmd2, out _);

                            Car c = Car.GetCarByID(id);
                            if (c is not null)
                            {
                                c.ExitCarThread("Car deleted!");
                            }

                            WriteString(response, "OK");
                        }
                    }
                }
                else if (Tools.IsPropertyExist(r, "reconnect"))
                {
                    Logfile.Log($"reconnect Car #{id}");

                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();


                        using (var cmd2 = new MySqlCommand("update cars set tesla_token='', refresh_token='' where id = @id", con))
                        {
                            cmd2.Parameters.AddWithValue("@id", id);
                            _ = SQLTracer.TraceNQ(cmd2, out _);

                            Car c = Car.GetCarByID(id);
                            if (c is not null)
                            {
                                c.Restart("Reconnect!",0);
                            }

                            WriteString(response, "OK");
                        }
                    }
                }
                else
                {
                    string vin = r["carid"].ToString();
                    string email = r["email"];
                    string password = r["password"];
                    bool freesuc = r["freesuc"];

                    string access_token = StringCipher.Encrypt(r["access_token"].ToString());
                    string refresh_token = StringCipher.Encrypt(r["refresh_token"].ToString());

                    bool FleetAPI = false;

                    if (r["fleetAPI"] is not null)
                         FleetAPI = r["fleetAPI"];

                    if (id == -1)
                    {
                        Logfile.Log("Insert Password");

                        using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                        {
                            con.Open();

                            using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    MAX(a) + 1
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
) AS t", con)) // 
                            {
                                //decimal newid = SQLTracer.TraceSc(cmd) as decimal? ?? 1;
                                int newid = 1;
                                object queryresult = SQLTracer.TraceSc(cmd);
                                if (queryresult is not null && !int.TryParse(queryresult.ToString(), out newid))
                                {
                                    // assign default id 1 if parsing the queryresult fails
                                    newid = 1;
                                }

                                Logfile.Log($"New CarID: {newid} SQL Query result: <{queryresult}>");

                                using (var cmd2 = new MySqlCommand("insert cars (id, tesla_name, tesla_password, vin, display_name, freesuc, tesla_token, refresh_token, tesla_token_expire, fleetAPI) values (@id, @tesla_name, @tesla_password, @vin, @display_name, @freesuc,  @tesla_token, @refresh_token, @tesla_token_expire, @fleetAPI)", con))
                                {
                                    cmd2.Parameters.AddWithValue("@id", newid);
                                    cmd2.Parameters.AddWithValue("@tesla_name", email);
                                    cmd2.Parameters.AddWithValue("@tesla_password", password);
                                    cmd2.Parameters.AddWithValue("@vin", vin);
                                    cmd2.Parameters.AddWithValue("@display_name", $"Car {newid}");
                                    cmd2.Parameters.AddWithValue("@freesuc", freesuc ? 1 : 0);
                                    cmd2.Parameters.AddWithValue("@tesla_token", access_token);
                                    cmd2.Parameters.AddWithValue("@refresh_token", refresh_token);
                                    cmd2.Parameters.AddWithValue("@tesla_token_expire", DateTime.Now);
                                    cmd2.Parameters.AddWithValue("@fleetAPI", FleetAPI);
                                    _ = SQLTracer.TraceNQ(cmd2, out _);

                                    var dt = DBHelper.GetCarDT(Convert.ToInt32(newid));
                                    if (dt?.Rows?.Count > 0)
                                        Program.StartCarThread(dt.Rows[0]);

                                    WriteString(response, $"ID:{newid}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Logfile.Log($"Update Password ID:{id}");
                        int dbID = Convert.ToInt32(id);

                        using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                        {
                            con.Open();

                            using (MySqlCommand cmd = new MySqlCommand("update cars set freesuc=@freesuc,  tesla_token=@tesla_token, refresh_token=@refresh_token, fleetAPI=@fleetAPI, tesla_token_expire=@tesla_token_expire where id=@id", con))
                            {
                                cmd.Parameters.AddWithValue("@id", dbID);
                                cmd.Parameters.AddWithValue("@freesuc", freesuc ? 1 : 0);
                                cmd.Parameters.AddWithValue("@tesla_token", access_token);
                                cmd.Parameters.AddWithValue("@refresh_token", refresh_token);
                                cmd.Parameters.AddWithValue("@fleetAPI", FleetAPI ? 1 : 0);
                                cmd.Parameters.AddWithValue("@tesla_token_expire", DateTime.Now);
                                _ = SQLTracer.TraceNQ(cmd, out _);

                                Car c = Car.GetCarByID(dbID);
                                if (c is not null)
                                {
                                    c.ExitCarThread("Credentials changed!");
                                }

                                var dt = DBHelper.GetCarDT(Convert.ToInt32(id));
                                if (dt?.Rows?.Count > 0) 
                                    Program.StartCarThread(dt.Rows[0]);
                                WriteString(response, "OK");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().MarkAsCritical().Submit();
                WriteString(response, "ERROR");
                Logfile.Log(ex.ToString());
            }
        }

        private static void Admin_SetPasswordOVMS(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                Logfile.Log("SetPasswordOVMS");

                string data = GetDataFromRequestInputStream(request);

                dynamic r = JsonConvert.DeserializeObject(data);

                int id = Convert.ToInt32(r["id"]);                
                string login = r["login"];
                string password = r["password"];
                string carname = r["carname"];

                if (id == -1)
                {
                    Logfile.Log("Insert Password");

                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();

                        using (MySqlCommand cmd = new MySqlCommand("select max(id)+1 from cars", con))
                        {
                            long newid = SQLTracer.TraceSc(cmd) as long? ?? 1;

                            using (var cmd2 = new MySqlCommand("insert cars (id, tesla_name, tesla_password, tesla_token, display_name) values (@id, @tesla_name, @tesla_password, @tesla_token, @display_name)", con))
                            {
                                cmd2.Parameters.AddWithValue("@id", newid);
                                cmd2.Parameters.AddWithValue("@tesla_name", login);
                                cmd2.Parameters.AddWithValue("@tesla_password", password);
                                cmd2.Parameters.AddWithValue("@tesla_token", $"OVMS:{carname}");
                                cmd2.Parameters.AddWithValue("@display_name", carname);
                                _ = SQLTracer.TraceNQ(cmd2, out _);

                                WriteString(response, $"ID:{newid}");
                            }
                        }
                    }
                }
                else
                {
                    Logfile.Log($"Update Password ID:{id}");
                    int dbID = Convert.ToInt32(id);

                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();

                        using (MySqlCommand cmd = new MySqlCommand("update cars set tesla_name=@tesla_name, tesla_password=@tesla_password, tesla_token=@tesla_token, display_name=@display_name where id=@id", con))
                        {
                            cmd.Parameters.AddWithValue("@id", dbID);
                            cmd.Parameters.AddWithValue("@tesla_name", login);
                            cmd.Parameters.AddWithValue("@tesla_password", password);
                            cmd.Parameters.AddWithValue("@tesla_token", $"OVMS:{carname}");
                            cmd.Parameters.AddWithValue("@display_name", carname);

                            _ = SQLTracer.TraceNQ(cmd, out _);

                            WriteString(response, "OK");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                WriteString(response, "ERROR");
                Logfile.Log(ex.ToString());
            }
        }

        private static void Admin_ReloadGeofence(HttpListenerRequest request, HttpListenerResponse response)
        {
            Logfile.Log("Admin: ReloadGeofence ...");
            Geofence.GetInstance().Init();

            if (request.QueryString.Count == 1 && string.Concat(request.QueryString.GetValues(0)).Equals("html", System.StringComparison.Ordinal))
            {
                IEnumerable<string> geofence = Geofence.GetInstance().geofenceList.Select(
                    a => string.Format(Tools.ciEnUS, "<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>geofence</td></tr>",
                        a.name,
                        a.lat,
                        a.lng,
                        a.radius,
                        string.Concat(a.specialFlags.Select(
                            sp => string.Format(Tools.ciEnUS, "{0}<br/>",
                            sp.ToString()))
                        )
                    )
                );
                IEnumerable<string> geofenceprivate = Geofence.GetInstance().geofencePrivateList.Select(
                    a => string.Format(Tools.ciEnUS, "<tr><td>{0}</td><td>{1}</td><td>{2}</td><td>{3}</td><td>{4}</td><td>geofence-private</td></tr>",
                        a.name,
                        a.lat,
                        a.lng,
                        a.radius,
                        string.Concat(a.specialFlags.Select(
                            sp => string.Format(Tools.ciEnUS, "{0}<br/>",
                            sp.ToString()))
                        )
                    )
                );
                WriteString(response, $"<html><head></head><body><table border=\"1\">{string.Concat(geofence)}{string.Concat(geofenceprivate)}</table></body></html>", "text/html; charset=utf-8");
            }
            else
            {
                WriteString(response, "{\"response\":{\"reason\":\"\", \"result\":true}}", "application/json");
            }
            WebHelper.UpdateAllPOIAddresses();
            Logfile.Log("Admin: ReloadGeofence done");
        }

        private static void Admin_Setcost(HttpListenerRequest request, HttpListenerResponse response)
        {
            string json = "";

            try
            {
                Logfile.Log("SetCost");

                if (request.QueryString["JSON"] is not null)
                {
                    json = request.QueryString["JSON"];
                }
                else
                {
                    json = GetDataFromRequestInputStream(request);
                }

                // json = Tools.ConvertBase64toString("");

                Logfile.Log($"JSON: {json}");

                dynamic j = JsonConvert.DeserializeObject(json);

                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("update chargingstate set cost_total = @cost_total, cost_currency=@cost_currency, cost_per_kwh=@cost_per_kwh, cost_per_session=@cost_per_session, cost_per_minute=@cost_per_minute, cost_idle_fee_total=@cost_idle_fee_total, cost_kwh_meter_invoice=@cost_kwh_meter_invoice  where id= @id", con))
                    {

                        if (DBHelper.DBNullIfEmptyOrZero(j["cost_total"].Value) is DBNull && DBHelper.IsZero(j["cost_per_session"].Value))
                        {
                            cmd.Parameters.AddWithValue("@cost_total", 0);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@cost_total", DBHelper.DBNullIfEmptyOrZero(j["cost_total"].Value));
                        }

                        cmd.Parameters.AddWithValue("@cost_currency", DBHelper.DBNullIfEmpty(j["cost_currency"].Value));
                        cmd.Parameters.AddWithValue("@cost_per_kwh", DBHelper.DBNullIfEmpty(j["cost_per_kwh"].Value));
                        cmd.Parameters.AddWithValue("@cost_per_session", DBHelper.DBNullIfEmpty(j["cost_per_session"].Value));
                        cmd.Parameters.AddWithValue("@cost_per_minute", DBHelper.DBNullIfEmpty(j["cost_per_minute"].Value));
                        cmd.Parameters.AddWithValue("@cost_idle_fee_total", DBHelper.DBNullIfEmpty(j["cost_idle_fee_total"].Value));
                        cmd.Parameters.AddWithValue("@cost_kwh_meter_invoice", DBHelper.DBNullIfEmpty(j["cost_kwh_meter_invoice"].Value));

                        cmd.Parameters.AddWithValue("@id", j["id"].Value);
                        int done = _ = SQLTracer.TraceNQ(cmd, out _);

                        Logfile.Log($"SetCost OK: {done}");
                        WriteString(response, "OK");
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().AddObject(json, "JSON").Submit();
                Logfile.Log(ex.ToString());
                WriteString(response, "ERROR");
            }
        }

        private static void Admin_Getchargingstate(HttpListenerRequest request, HttpListenerResponse response)
        {
            string id = request.QueryString["id"];
            string responseString = "";

            try
            {
                Logfile.Log("HTTP getchargingstate");
                using (DataTable dt = new DataTable())
                {
                    using (MySqlDataAdapter da = new MySqlDataAdapter(@"SELECT chargingstate.*, lat, lng, address, chargingstate.charge_energy_added as kWh 
                            FROM chargingstate join pos on chargingstate.pos = pos.id 
                            join charging on chargingstate.EndChargingID = charging.id where chargingstate.id = @id", DBHelper.DBConnectionstring))
                    {
                        da.SelectCommand.Parameters.AddWithValue("@id", id);
                        SQLTracer.TraceDA(dt, da);

                        responseString = dt.Rows.Count > 0 ? Tools.DataTableToJSONWithJavaScriptSerializer(dt) : "not found!";
                    }
                    dt.Clear();
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            Logfile.Log($"JSON: {responseString}");

            WriteString(response, responseString, "application/json");
        }

        private static void Admin_Getchargingstates(HttpListenerRequest request, HttpListenerResponse response)
        {
            var startStr = request.QueryString["start"];
            var gotStart = long.TryParse(startStr, out var start);
            var endStr = request.QueryString["end"];
            var gotEnd = long.TryParse(endStr, out var end);
            var responseString = "";

            try
            {
                Logfile.Log("HTTP getchargingstate");

                using (var dt = new DataTable())
                {
                    var query = new StringBuilder();
                    query.Append(@"SELECT chargingstate.id, UNIX_TIMESTAMP(chargingstate.StartDate)*1000 as StartDate, UNIX_TIMESTAMP(chargingstate.EndDate)*1000 as EndDate
                            FROM chargingstate join pos on chargingstate.pos = pos.id 
                            join charging on chargingstate.EndChargingID = charging.id");
                    if (gotStart)
                    {
                        query.Append(" WHERE UNIX_TIMESTAMP(chargingstate.StartDate)*1000 >= @start");
                        if (gotEnd)
                        {
                            query.Append(" AND UNIX_TIMESTAMP(chargingstate.EndDate)*1000 <= @end");
                        }
                    }
                    else if (gotEnd)
                    {
                        query.Append(" WHERE UNIX_TIMESTAMP(chargingstate.EndDate)*1000 <= @end");
                    }

                    //Tools.DebugLog("Query: " + query);

#pragma warning disable CA2100 // Review SQL queries for security vulnerabilities
                    using (var da = new MySqlDataAdapter(query.ToString(), DBHelper.DBConnectionstring))
                    {
                        if (gotStart)
                        {
                            da.SelectCommand.Parameters.AddWithValue("@start", start);
                        }
                        if (gotEnd)
                        {
                            da.SelectCommand.Parameters.AddWithValue("@end", end);
                        }

                        SQLTracer.TraceDA(dt, da);

                        responseString = dt.Rows.Count > 0 ? Tools.DataTableToJSONWithJavaScriptSerializer(dt) : "[]";
                    }
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
                    dt.Clear();
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            Tools.DebugLog($"JSON: {responseString}");

            WriteString(response, responseString, "application/json");
        }

        private static void Admin_GetAllCars(HttpListenerRequest _, HttpListenerResponse response)
        {
            string responseString = "";

            try
            {
                lock (Car.Allcars)
                {
                    using (DataTable dt = new DataTable())
                    {
                        using (MySqlDataAdapter da = new MySqlDataAdapter("SELECT id, display_name, tasker_hash, model_name, vin, tesla_name, tesla_carid, lastscanmytesla, freesuc, fleetAPI, needVirtualKey, needCommandPermission, needFleetAPI, access_type, virtualkey, car_type FROM cars order by display_name", DBHelper.DBConnectionstring))
                        {
                            SQLTracer.TraceDA(dt, da);

                            dt.Columns.Add("SupportedByFleetTelemetry");
                            dt.Columns.Add("inactive");
                            dt.Columns.Add("vehicle_location");

                            foreach (DataRow dr in dt.Rows)
                            {
                                try
                                {
                                    Car c = Car.GetCarByID(Convert.ToInt32(dr["id"]));
                                    if (c is not null)
                                    {
                                        dr["SupportedByFleetTelemetry"] = c.SupportedByFleetTelemetry() ? 1 : 0;
                                        dr["vehicle_location"] = c.vehicle_location;
                                    }
                                    else if (dr["tesla_name"] is not null && dr["tesla_name"].ToString().StartsWith("KOMOOT:", StringComparison.Ordinal))
                                    {
                                        dr["inactive"] = 0;
                                    }
                                    else
                                    {
                                        dr["inactive"] = 1;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ex.ToExceptionless().FirstCarUserID().Submit();
                                    Logfile.Log(ex.ToString());
                                }
                            }

                            responseString = dt.Rows.Count > 0 ? Tools.DataTableToJSONWithJavaScriptSerializer(dt) : "not found!";
                        }
                        dt.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

            WriteString(response, responseString, "application/json");
        }

        private static void Admin_GetPOI(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.QueryString.Count == 2 && request.QueryString.HasKeys())
            {
                double lat = double.NaN;
                double lng = double.NaN;
                foreach (string key in request.QueryString.AllKeys)
                {
                    if (request.QueryString.GetValues(key).Length == 1)
                    {
                        switch (key)
                        {
                            case "lat":
                                _ = double.TryParse(request.QueryString.GetValues(key)[0], out lat);
                                break;
                            case "lng":
                                _ = double.TryParse(request.QueryString.GetValues(key)[0], out lng);
                                break;
                            default:
                                break;
                        }
                    }
                }
                if (!double.IsNaN(lat) && !double.IsNaN(lng))
                {
                    Address addr = Geofence.GetInstance().GetPOI(lat, lng, false);
                    if (addr is not null)
                    {
                        Dictionary<string, object> data = new()
                        {
                            { "name", addr.name },
                            { "rawName", addr.rawName },
                            { "lat", addr.lat },
                            { "lng", addr.lng },
                            { "radius", addr.radius },
                            { "IsHome", addr.IsHome },
                            { "IsWork", addr.IsWork },
                            { "IsCharger", addr.IsCharger },
                            { "NoSleep", addr.NoSleep },
                        };
                        Dictionary<string, object> specialflags = new();
                        foreach (KeyValuePair<Address.SpecialFlags, string> flag in addr.specialFlags)
                        {
                            specialflags.Add(flag.Key.ToString(), flag.Value);
                        }
                        data.Add("SpecialFlags", specialflags);
                        WriteString(response, JsonConvert.SerializeObject(data), "application/json; charset=utf-8");
                        return;
                    }
                }
            }
            // finally close response
            WriteString(response, "");
        }

        private static void Admin_UpdateElevation(HttpListenerRequest _, HttpListenerResponse response)
        {
            int from = 1;
            int to = 1;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand("Select max(id) from pos", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read() && dr[0] != DBNull.Value)
                        {
                            int.TryParse(dr[0].ToString(), out to);
                        }
                        con.Close();
                    }
                }
            }
            catch (Exception ex) 
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
            }
            Logfile.Log($"Admin: UpdateElevation ({from} -> {to}) ...");
            WriteString(response, $"Admin: UpdateElevation ({from} -> {to}) ...");
            DBHelper.UpdateTripElevation(from, to, null, "/admin/UpdateElevation");
            Logfile.Log("Admin: UpdateElevation done");
        }

        private static void Admin_RestoreChargingCostsFromBackup1(HttpListenerRequest _, HttpListenerResponse response)
        {
            Logfile.Log("RestoreChargingCostsFromBackup1");
            // handle GET request
            // list available backup files
            // offer upload possibility for backup file
            List<string> fileList = new();
            try
            {
                foreach (string fileName in Directory.GetFiles("/etc/teslalogger/backup", "mysqldump2023*"))
                {
                    // check file
                    FileInfo fi = new FileInfo(fileName);
                    if (fi.Length == 0)
                    {
                        // file has zero bytes
                        continue;
                    }
                    // filter backups: ingore too old and newer than 2023-05-03
                    Match m = Regex.Match(fileName, "mysqldump2023([0-9]{4})");
                    if (m.Success && m.Groups.Count == 2 && m.Groups[1].Captures.Count == 1)
                    {
                        if (int.TryParse(m.Groups[1].Captures[0].ToString(), out int monthday)) {
                            if (monthday < 504)
                            {
                                fileList.Add(fileName);
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
            Tools.DebugLog($"local file list: {string.Join(",", fileList)}");
            StringBuilder html = new StringBuilder();
            html.Append(@"
<html>
    <head>
        <script type=""text/javascript"">
function showDiv() {
    document.getElementsByClassName(""container-box"")[0].style.display = ""block"";
    document.getElementsByClassName(""overlay-box"")[0].style.display = ""block"";
}
function checkform() {
    if(document.getElementById(""restoreFromRemoteFile"").value.length < 6) {
        alert(""please select a file"");
        return false;
    } else {
        showDiv();
        document.myForm.submit();
    }
}
        </script>
        <style>
.container-box {
    background: #666666;
    opacity: .8;
    width:100%;
    height: 100%;
    text-align:center;
    display: none;
}
.overlay-box {
    background-color:#fff;
    width:480px;
    display: none;
    margin: auto;
}
        </style>
    </head>
    <body>
        <div class=""container-box"">
            <div class=""overlay-box"">TeslaLogger is processing your file, please be patient<br /><br />this may take some minutes depending on the size of your backup</div>
        </div>
        <h2>Restore chargingstate cost_per_minute and cost_per_session from backup - step 1 of 3</h2>
");
            if (fileList.Count > 0) {
                html.Append(@"
        <br /><h3>available backups:</h3>
        <br />
        <ul>");
                foreach(string fileName in fileList)
                {
                    html.Append($@"
            <li>{fileName}
                <form action=""RestoreChargingCostsFromBackup2"" method=""POST"">
                    <input type=""hidden"" id=""restoreFromLocalFile"" name=""restoreFromLocalFile"" value=""{fileName}"">
                    <input type=""submit"" onClick=""showDiv();"" value=""Continue with {fileName}"">
                </form>
            </li>");
                }
                html.Append(@"
        </ul>");
            }
            html.Append(@"
        upload your own backup file (make sure it is from a TeslaLogger version before 1.54.20 released on 2023-05-04)
        <form name=""myForm"" action=""RestoreChargingCostsFromBackup2"" method=""POST"" enctype=""multipart/form-data"">
            <label for=""restoreFromRemoteFile"">Select a file:</label>
            <input type=\""file"" id=""restoreFromRemoteFile"" name=""restoreFromRemoteFile"">
            <input type=""button"" onClick=""checkform();"" value=""Upload and continue"">
        </form>");
            html.Append(@"
    </body>
</html>");
            WriteString(response, html.ToString(), "text/html");
        }
    }
}
