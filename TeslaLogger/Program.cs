using Exceptionless;
using System;
using System.Data;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Diagnostics;
using TeslaLoggerNET8.Lucid;
using TeslaLoggerNET8.Kafka;

namespace TeslaLogger
{
    /// <summary>
    /// Main entry point and initialization orchestrator for the TeslaLogger application.
    /// </summary>
    /// <remarks>
    /// The Program class manages application startup, configuration, and initialization:
    /// - Framework version checking (.NET 8 verification)
    /// - Logging subsystem initialization
    /// - Database connection establishment
    /// - External service setup (MQTT, ABRP, Komoot, Nearb SuC)
    /// - Web server initialization  
    /// - Vehicle authentication and data collection start
    /// - Main event loop management
    /// 
    /// Initialization order is critical; see Main() for execution sequence.
    /// All static fields represent application-wide configuration.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Keine allgemeinen Ausnahmetypen abfangen", Justification = "<Pending>")]
    internal class Program
    {
        /// <summary>
        /// Global flag for verbose debug output.
        /// </summary>
        /// <remarks>
        /// When true, enables detailed logging of internal operations.
        /// Defaults to false for normal operation.
        /// </remarks>
        public static bool VERBOSE; // defaults to false
        
        /// <summary>
        /// Global flag for SQL debugging (logs SQL commands executed).
        /// </summary>
        /// <remarks>
        /// When true, logs all SQL commands to debug output.
        /// Defaults to false; enable for database troubleshooting.
        /// </remarks>
        public static bool SQLTRACE; // defaults to false
        
        /// <summary>
        /// Global flag for full SQL debugging (logs detailed SQL execution).
        /// </summary>
        /// <remarks>
        /// When true, logs complete SQL with parameter values and results.
        /// Defaults to false; very verbose, use only for detailed debugging.
        /// </remarks>
        public static bool SQLFULLTRACE; // defaults to false
        
        /// <summary>
        /// Maximum number of characters to log per SQL command for debugging.
        /// </summary>
        /// <remarks>
        /// Prevents excessively large log entries from batched SQL operations.
        /// Default: 250 characters.
        /// </remarks>
        public static int SQLTRACELIMIT = 250;
        
        /// <summary>
        /// Minutes to keep the vehicle online after last usage (prevents sleep).
        /// </summary>
        /// <remarks>
        /// After the vehicle stops being used, keep it awake for this duration.
        /// Default: 5 minutes.
        /// </remarks>
        public static int KeepOnlineMinAfterUsage = 5;
        
        /// <summary>
        /// Minutes to suspend making API calls to the Tesla API.
        /// </summary>
        /// <remarks>
        /// Used to respect rate limits and reduce API pressure.
        /// Default: 30 minutes.
        /// </remarks>
        public static int SuspendAPIMinutes = 30;
        
        /// <summary>
        /// The timestamp when the application was started.
        /// </summary>
        /// <remarks>
        /// Used for calculating application uptime and logging.
        /// </remarks>
        public static DateTime uptime = DateTime.Now;

        /// <summary>
        /// Memory cache keys for the application-level cache.
        /// </summary>
        public enum TLMemCacheKey
        {
            /// <summary>
            /// Cached value for GetOutsideTempAsync method.
            /// </summary>
            GetOutsideTempAsync,
            /// <summary>
            /// Cached value for housekeeping operations.
            /// </summary>
            Housekeeping
        }

        private static WebServer? webServer;
        
        /// <summary>
        /// Global cancellation token source for graceful application shutdown.
        /// </summary>
        /// <remarks>
        /// Used to propagate cancellation requests throughout the application's async operations.
        /// Handles Ctrl+C and other shutdown signals for proper resource cleanup.
        /// </remarks>
        private static readonly CancellationTokenSource ApplicationCancellationTokenSource = new();
        
        /// <summary>
        /// Gets the cancellation token for the entire application.
        /// </summary>
        /// <remarks>
        /// Use this token in all long-running async operations to enable graceful shutdown.
        /// </remarks>
        public static CancellationToken ApplicationCancellationToken => ApplicationCancellationTokenSource.Token;
        
        /// <summary>
        /// Indicates whether the OVMS (Open Vehicle Monitoring System) has been started.
        /// </summary>
        /// <remarks>
        /// Defaults to false; set to true only if OVMS module is necessary.
        /// </remarks>
        private static bool OVMSStarted; // defaults to false;

        /// <summary>
        /// Main application entry point orchestrating the complete startup sequence.
        /// </summary>
        /// <param name="args">Command-line arguments (currently unused).</param>
        /// <remarks>
        /// Execution sequence:
        /// 1. Initialize error reporting (Exceptionless)
        /// 2. Check .NET 8 framework version
        /// 3. Setup logging subsystem
        /// 4. Perform stage 1 initialization (basic setup)
        /// 5. Check Docker environment
        /// 6. Perform stage 2 initialization (advanced setup)
        /// 7. Connect to database
        /// 8. Initialize web server
        /// 9. Setup external services (TopoData, Maps, MQTT, etc.)
        /// 10. Update vehicle list and start collection
        /// 11. Enter main event loop
        /// 
        /// All exceptions are caught and logged; application does not terminate on errors.
        /// </remarks>
        private static async Task Main(string[] _)
        {
            try
            {
                try
                {
                    ExceptionlessClient.Default.Startup(ApplicationSettings.Default.ExceptionlessApiKey);
                    // ExceptionlessClient.Default.Configuration.UseFileLogger("exceptionless.log");
                    ExceptionlessClient.Default.Configuration.ServerUrl = ApplicationSettings.Default.ExceptionlessServerUrl;
                    ExceptionlessClient.Default.Configuration.SetVersion(Assembly.GetExecutingAssembly().GetName().Version);

                    ExceptionlessClient.Default.CreateLog("Program", $"Start {Assembly.GetExecutingAssembly().GetName().Version}", Exceptionless.Logging.LogLevel.Info).FirstCarUserID().Submit();
                }
                catch (Exception ex)
                {
                    Logfile.Log(ex.ToString());
                }

                Logfile.Log($"Processname: {System.Diagnostics.Process.GetCurrentProcess().ProcessName}");
                Logfile.Log($"Run on Linux: {Tools.RunOnLinux()}");

                RegisterCancellationHandlers();

                await InitCheckNet8(ApplicationCancellationToken).ConfigureAwait(false);

                InitDebugLogging();

                InitStage1();

                InitCheckDocker();

                InitStage2();

                await InitConnectToDB(ApplicationCancellationToken).ConfigureAwait(false);

                InitWebserver();

                InitOpenTopoDataService(ApplicationCancellationToken);

                InitStaticMapService(ApplicationCancellationToken);

                UpdateTeslalogger.StopComfortingMessagesThread();

                InitMQTT(ApplicationCancellationToken);

                MQTTClient.StartMQTTClient();

                InitTLStats(ApplicationCancellationToken);

                UpdateDbInBackground(ApplicationCancellationToken);

                Logfile.Log("Init finished, now enter main loop");

                await GetAllCars(ApplicationCancellationToken).ConfigureAwait(false);

                InitNearbySuCService(ApplicationCancellationToken);

                OnlineUpdateGeofenceInBackground(ApplicationCancellationToken);
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.Message);
                Logfile.ExceptionWriter(ex, "main loop");
                Logfile.Log("Teslalogger Stopped!");
                Tools.ExternalLog($"Teslalogger Stopped! {ex}");

                ex.ToExceptionless().FirstCarUserID().Submit();
                ExceptionlessClient.Default.ProcessQueueAsync();
            }
            finally
            {
                // Ensure graceful shutdown
                try
                {
                    Logfile.Log("Starting graceful shutdown...");
                    ApplicationCancellationTokenSource.Cancel();
                    Logfile.Log("Graceful shutdown initiated.");
                }
                catch (Exception ex)
                {
                    Logfile.Log($"Error during graceful shutdown: {ex.Message}");
                }

                if (!UpdateTeslalogger.DownloadUpdateAndInstallStarted)
                {
                    try
                    {
                        Logfile.Log("Startup doesn't sucessfully run DownloadUpdateAndInstall() - retry now!");
                        ExceptionlessClient.Default.SubmitLog("Program", "Startup doesn't sucessfully run DownloadUpdateAndInstall() - retry now!");

                        #pragma warning disable CS4014
                        UpdateTeslalogger.DownloadUpdateAndInstallAsync();
                        #pragma warning restore CS4014
                    }
                    catch (Exception ex)
                    {
                        Logfile.Log(ex.Message);
                        // Logfile.ExceptionWriter(ex, "Emergency DownloadUpdateAndInstall()");

                        ExceptionlessClient.Default.SubmitLog("Program", "Emergency DownloadUpdateAndInstall()");
                    }
                }
            }
        }

        /// <summary>
        /// Registers handlers for console cancellation (Ctrl+C) and provides graceful shutdown.
        /// </summary>
        /// <remarks>
        /// Sets up listeners for console termination signals to propagate cancellation throughout the app.
        /// </remarks>
        private static void RegisterCancellationHandlers()
        {
            try
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true; // Prevent immediate termination
                    Logfile.Log("Cancellation request received (Ctrl+C). Initiating graceful shutdown...");
                    try
                    {
                        ApplicationCancellationTokenSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Token source already disposed, ignore
                    }
                };

                // Register for AppDomain unload events (process termination)
                AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
                {
                    Logfile.Log("Process exit event fired. Initiating graceful shutdown...");
                    try
                    {
                        ApplicationCancellationTokenSource.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Token source already disposed, ignore
                    }
                };
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error registering cancellation handlers: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks the .NET framework version and switches to .NET 8 if necessary.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// If running on .NET Framework and .NET 8 is available, launches the .NET 8 version.
        /// </remarks>
        private static async Task InitCheckNet8(CancellationToken cancellationToken = default)
        {
            try
            {
                var net8version = Tools.GetNET8Version();
                if (net8version?.Contains("8.") == true)
                {
#if NET8_0
                    return;
#endif
                    if (!File.Exists("NOTUSETESLALOGGERNET8") && NET8TaskerToken())
                    {
                        Logfile.Log("Start Teslalogger.net8");

                        // Copy settings for .net8
                        if (!Directory.Exists("data"))
                        {
                            Directory.CreateDirectory("data");
                            File.Copy("settings.json", "data/settings.json");
                            File.Copy("encryption.txt", "data/encryption.txt");
                        }

                        UpdateTeslalogger.Chmod("startnet8.sh", 777, false);

                        var p = new Process();
                        p.StartInfo.FileName = "/bin/bash";
                        p.StartInfo.Arguments = $"startnet8.sh";
                        p.StartInfo.CreateNoWindow = true;
                        p.StartInfo.UseShellExecute = false;
                        p.Start();

                        await Task.Delay(5000, cancellationToken).ConfigureAwait(false);

                        Environment.Exit(0);
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        private static bool NET8TaskerToken()
        {
            try
            {
                return true;
                
                /*
                    WaitForDB();
                    return DBHelper.NET8TaskerToken();
                */
            }
            catch (Exception ex)
            {
                Logfile.Log($"NET8TaskerToken: cannot connect to DB: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Initializes the MQTT client for publish/subscribe messaging.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Starts the MQTT client in a background task that respects cancellation signals.
        /// </remarks>
        private static void InitMQTT(CancellationToken cancellationToken = default)
        {
            try
            {
                if(KVS.Get("MQTTSettings", out string mqttSettings) == KVS.SUCCESS)
                {
                    JObject settings = JObject.Parse(mqttSettings);
                    if ((long?)settings["mqtt_host"] > 0)
                    {
                        Task.Run(async () =>
                        {
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                MQTT.GetSingleton().RunMqtt();
                            }
                            catch (OperationCanceledException)
                            {
                                Logfile.Log("MQTT client cancelled");
                            }
                            catch (Exception ex)
                            {
                                ex.ToExceptionless().FirstCarUserID().Submit();
                                Logfile.Log(ex.ToString());
                            }
                        }, cancellationToken); 
                    }
                }
                else
                {
                    Logfile.Log("MQTT disabled (check settings)");
                }
            }
            catch (Newtonsoft.Json.JsonException jsonEx)
            {
                Logfile.Log($"Program: JSON parse error in InitMQTT - {jsonEx.Message}");
                jsonEx.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(jsonEx.ToString());
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

        }

        /// <summary>
        /// Initializes the Nearby Supercharger service for location-based queries.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Starts the nearby SuC service in a background task that respects cancellation signals.
        /// </remarks>
        private static void InitNearbySuCService(CancellationToken cancellationToken = default)
        {
            try
            {
                Task.Run(async() => {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await NearbySuCService.GetSingleton().Run();
                    }
                    catch (OperationCanceledException)
                    {
                        Logfile.Log("NearbySuCService cancelled");
                    }
                }, cancellationToken);                
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }

        }

        /// <summary>
        /// Retrieves all registered vehicles and initiates their data collection threads.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Applies throttling between vehicle initialization to prevent resource exhaustion.
        /// Respects cancellation tokens for graceful shutdown during startup.
        /// </remarks>
        internal static async Task GetAllCars(CancellationToken cancellationToken = default)
        {
            using (DataTable dt = DBHelper.GetCarsByTokenAge(true))
            {
                foreach (DataRow r in dt.Rows)
                {
                    // Check for cancellation before starting new vehicle thread
                    cancellationToken.ThrowIfCancellationRequested();

                    StartCarThread(r);
                    // small throttle delay
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
                dt.Clear();
            } 
        }

        internal static void StartCarThread(DataRow r, Car.TeslaState oldCarState = Car.TeslaState.Start)
        {
            int id = 0;
            try
            {
                id = Convert.ToInt32(r["id"], Tools.ciDeDE);
                String Name = r["tesla_name"].ToString();
                String Password = r["tesla_password"].ToString();
                int car_id_in_account = r["tesla_carid"] as Int32? ?? 0;
                if (Name.StartsWith("KOMOOT:", StringComparison.Ordinal))
                {
                    string komoot_vin = Komoot.CheckVIN(id, r["vin"].ToString());
                    Komoot _komoot = new Komoot(id, Name.Replace("KOMOOT:", string.Empty), Password);
                    Task.Run(async() =>
                    {
                        await _komoot.RunAsync();
                    });
                    Logfile.Log($"starting Komoot thread for ID {id} {Name.Replace("KOMOOT:", string.Empty)} <{komoot_vin}>");
                    return; // do not start a car thread for komoot "cars"
                }
                String tesla_token = r["tesla_token"] as String ?? "";
                if (tesla_token.StartsWith("OVMS:", StringComparison.Ordinal)) // OVMS Cars are not handled by Teslalogger
                {
                    if (!OVMSStarted)
                    {
                        Task.Run(() => Tools.StartOVMS());
                    }

                    OVMSStarted = true;
                    return;
                }

                DateTime tesla_token_expire = r["tesla_token_expire"] as DateTime? ?? DateTime.MinValue;
                string Model_Name = r["Model_Name"] as String ?? "";
                string car_type = r["car_type"] as String ?? "";
                string car_special_type = r["car_special_type"] as String ?? "";
                string car_trim_badging = r["car_trim_badging"] as String ?? "";
                string display_name = r["display_name"] as String ?? "";
                string vin = r["vin"] as String ?? "";
                string tasker_hash = r["tasker_hash"] as String ?? "";
                double? wh_tr = r["wh_tr"] as double?;
                string wheel_type = r["wheel_type"] as String ?? "";
                bool raven = false;
                bool isKafkaCar = false;
                if (r["raven"] is not DBNull && Convert.ToInt32(r["raven"]) == 1)
                    raven = true;

                bool fleetAPI = false;
                if (r["fleetAPI"] is not DBNull && Convert.ToInt32(r["fleetAPI"]) == 1)
                    fleetAPI = true;

                bool virtualKey = false;
                if (r["virtualkey"] is not DBNull && Convert.ToInt32(r["virtualkey"]) == 1)
                    virtualKey = true;
                else if (r["virtualkey"] is not DBNull && Convert.ToInt32(r["virtualkey"]) == 2)
                    isKafkaCar = true;

                string access_type = "";
                if (r["access_type"] is not DBNull)
                    access_type = r["access_type"].ToString();

#pragma warning disable CA2000 // Objekte verwerfen, bevor Bereich verloren geht
                if (car_type == "LUCID")
                {
                    LucidCar car = new LucidCar(id, Name, Password, car_id_in_account, "LUCID", tesla_token_expire, Model_Name, car_type, car_special_type, car_trim_badging, display_name, vin, tasker_hash, wh_tr, fleetAPI, oldCarState, wheel_type);
                }
                else if (isKafkaCar)
                {
                    KafkaCar car = new (id, Name, Password, car_id_in_account, tesla_token, tesla_token_expire, Model_Name, car_type, car_special_type, car_trim_badging, display_name, vin, tasker_hash, wh_tr, fleetAPI, oldCarState, wheel_type);
                }
                else
                {
                    Car car = new Car(id, Name, Password, car_id_in_account, tesla_token, tesla_token_expire, Model_Name, car_type, car_special_type, car_trim_badging, display_name, vin, tasker_hash, wh_tr, fleetAPI, oldCarState, wheel_type);
                    car.Raven = raven;
                    car._virtual_key = virtualKey;
                    car._access_type = access_type;
                }
#pragma warning restore CA2000 // Objekte verwerfen, bevor Bereich verloren geht
            }
            catch (Exception ex)
            {
                Logfile.Log($"{id}# :{ex}");
            }
        }

        private static void InitWebserver()
        {
            UpdateTeslalogger.CertUpdate();

            try
            {
                Task.Run(() => webServer = new WebServer());
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Initializes the TeslaLogger statistics collection service.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Starts the TLStats service in a background task that respects cancellation signals.
        /// </remarks>
        private static void InitTLStats(CancellationToken cancellationToken = default)
        {
            try
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await TLStats.RunAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Logfile.Log("TLStats service cancelled");
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Initializes the OpenTopoData elevation service for elevation queries.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Starts the OpenTopoData service in a background task that respects cancellation signals.
        /// </remarks>
        private static void InitOpenTopoDataService(CancellationToken cancellationToken = default)
        {
            try
            {
                if (Tools.UseOpenTopoData())
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            OpenTopoDataService.GetSingleton().Run();
                        }
                        catch (OperationCanceledException)
                        {
                            Logfile.Log("OpenTopoDataService cancelled");
                        }
                    }, cancellationToken);
                }
                else
                {
                    Logfile.Log("OpenTopoData disabled (enable in settings)");
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        private static void InitDebugLogging()
        {
            if (ApplicationSettings.Default.VerboseMode)
            {
                VERBOSE = true;
                Logfile.Log("VerboseMode: ON");
            }
            if (ApplicationSettings.Default.SQLTrace)
            {
                SQLTRACE = true;
                Logfile.Log("SQLTrace: ON");
            }
        }

        private static void InitStage2()
        {
            TestEncryption();
            Logfile.Log($"Path of settings.json: {FileManager.GetFilePath(TLFilename.SettingsFilename)}");
            Logfile.Log($"Path of invoices: {FileManager.GetInvoicePath()}");
            Logfile.Log($"Path of nohup.out: {FileManager.GetLogfilePath()}");
            Logfile.Log($"Path of backup folder: {FileManager.GetBackupPath()}");
            Logfile.Log($"Path of Map Cache: {FileManager.GetMapCachePath()}");
            Logfile.Log($"Path of SRTM Data: {FileManager.GetSRTMDataPath()}");

            KeepOnlineMinAfterUsage = Tools.GetSettingsInt("KeepOnlineMinAfterUsage", ApplicationSettings.Default.KeepOnlineMinAfterUsage);
            SuspendAPIMinutes = Tools.GetSettingsInt("SuspendAPIMinutes", ApplicationSettings.Default.SuspendAPIMinutes);

            Logfile.Log($"Current Culture: {Thread.CurrentThread.CurrentCulture}");
            Logfile.Log($"Mono Runtime: {Tools.GetMonoRuntimeVersion()}");
            ExceptionlessClient.Default.Configuration.DefaultData.Add("MonoRuntime", Tools.GetMonoRuntimeVersion());
            ExceptionlessClient.Default.Configuration.DefaultData.Add("OS", Tools.GetOsRelease());

            Logfile.Log($"Grafana Version: {Tools.GetGrafanaVersion()}");
            ExceptionlessClient.Default.Configuration.DefaultData.Add("GrafanaVersion", Tools.GetGrafanaVersion());

            Logfile.Log($"OS Version: {Tools.GetOsVersion()}");
            ExceptionlessClient.Default.Configuration.DefaultData.Add("OSVersion", Tools.GetOsVersion());

            Logfile.Log($"Update Settings: {Tools.GetOnlineUpdateSettings()}");
            ExceptionlessClient.Default.Configuration.DefaultData.Add("UpdateSettings", Tools.GetOnlineUpdateSettings().ToString());

            try
            {
                if (Tools.IsDotnet8())
                {
                    ExceptionlessClient.Default.Configuration.DefaultData.Add("dotnet", Environment.Version?.ToString());
                    ExceptionlessClient.Default.CreateFeatureUsage("USE_DOTNET8").FirstCarUserID().AddObject(Environment.Version.ToString(), "DOTNET8").Submit();
                }
            }
            catch (Exception ex)
            {
                Logfile.Log($"Warning: Failed to report .NET version: {ex.Message}");
            }

            Logfile.Log($"DBConnectionstring: {DBHelper.GetDBConnectionstring(true)}");

            Logfile.Log($"KeepOnlineMinAfterUsage: {KeepOnlineMinAfterUsage}");
            Logfile.Log($"SuspendAPIMinutes: {SuspendAPIMinutes}");
            Logfile.Log($"SleepPositions: {ApplicationSettings.Default.SleepPosition}");
            Logfile.Log($"UseScanMyTesla: {Tools.UseScanMyTesla()}");
            Logfile.Log($"StreamingPos: {Tools.StreamingPos()}");
            try
            {
                long freeDiskSpaceMB = Tools.FreeDiskSpaceMB();
                Logfile.Log($"Free disk space: {freeDiskSpaceMB}mb");
                if (freeDiskSpaceMB < 1000)
                {
                    Logfile.Log("Disk space is very low! trying to clean up ...");
                    Tools.LogDiskUsage();
                    Tools.CleanupBackupFolder();
                    Tools.CleanupExceptionsDir();
                    Tools.LogDiskUsage();
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, ex.ToString());
            }
        }

        private static void InitStage1()
        {
            Tools.SetThreadEnUS();
            UpdateTeslalogger.Chmod("nohup.out", 666, false);
            UpdateTeslalogger.Chmod("backup.sh", 777, false);
            UpdateTeslalogger.Chmod("TeslaLogger.exe", 755, false);

            Logfile.Log($"Runtime: {Environment.Version}");
            Logfile.Log($"TeslaLogger Version: {Assembly.GetExecutingAssembly().GetName().Version}");
            Logfile.Log($"Teslalogger Online Version: {WebHelper.GetOnlineTeslaloggerVersion()}");
            Logfile.Log($"Logfile Version: {Assembly.GetAssembly(typeof(Logfile)).GetName().Version}");
            Logfile.Log($"SRTM Version: {Assembly.GetAssembly(typeof(SRTM.SRTMData)).GetName().Version}");
            try
            {
                string versionpath = Path.Combine(FileManager.GetExecutingPath(), "VERSION");
                if (Tools.RunOnLinux())
                    versionpath = "/etc/teslalogger/VERSION";

                File.WriteAllText(versionpath, Assembly.GetExecutingAssembly().GetName().Version.ToString());
            }
            catch (Exception)
            { }
            try
            {
                if (File.Exists("BRANCH"))
                {
                    var branch = File.ReadAllText("BRANCH").Trim();
                    Logfile.Log($"YOU ARE USING BRANCH: {branch}");

                    ExceptionlessClient.Default.Configuration.DefaultData.Add("Branch", branch);
                    ExceptionlessClient.Default.CreateLog("Program", $"BRANCH: {branch}", Exceptionless.Logging.LogLevel.Warn).FirstCarUserID().Submit(); ;
                }
            }
            catch (Exception ex)
            {
                Logfile.Log(ex.ToString());
                ex.ToExceptionless().FirstCarUserID().Submit();
            }

            Logfile.Log($"OS: {Tools.GetOsRelease()}");
        }

        static void TestEncryption()
        {
            try
            {
                var path = FileManager.GetFilePath(TLFilename.EncryptionFilename);
                Logfile.Log($"Path of encryption.txt: {path}");

                var body = "jfsdoifjhoiwejgfüp9034eu7trfß90834ugf0ß9834uejpf90guj43pü09tgfuj45p90t8ugjedlkfgjd";
                var pass = StringCipher.GetPassPhrase();
                var encrypted = StringCipher.Encrypt(body);
                var decrypted = StringCipher.Decrypt(encrypted);
                if (body != decrypted)
                    Logfile.Log("Encryption doesn't work!!!");

            }
            catch (Exception ex)
            {
                ex.ToExceptionless().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Initializes the database connection and starts background update tasks.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Establishes database connectivity, starts async update tasks for Grafana configuration.
        /// </remarks>
        private static async Task InitConnectToDB(CancellationToken cancellationToken = default)
        {
            await WaitForDB(cancellationToken).ConfigureAwait(false);

            #pragma warning disable CS4014
            UpdateTeslalogger.Start();
            #pragma warning restore CS4014
            _ = Task.Factory.StartNew(async () =>
            {
                await UpdateTeslalogger.UpdateGrafanaAsync().ConfigureAwait(false);
            }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        }

        /// <summary>
        /// Waits for the database connection to become available.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Attempts to connect to the database up to 300 times with 15-second intervals.
        /// Respects cancellation tokens to enable graceful shutdown during database waits.
        /// </remarks>
        private static async Task WaitForDB(CancellationToken cancellationToken = default)
        {
            for (int x = 1; x <= 300; x++) // try 300 times until DB is up and running
            {
                // Check for cancellation before attempting connection
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    Logfile.Log($"DB Version: {DBHelper.GetVersion()}");
                    Logfile.Log($"Count Pos: {DBHelper.CountPos()}"); // test the DBConnection
                    break;
                }
                catch (OperationCanceledException)
                {
                    Logfile.Log($"Database connection wait cancelled at attempt {x}/300");
                    throw;
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("Connection refused")
                        || ex.Message.Contains("Unable to connect to any of the specified MySQL hosts")
                        || ex.Message.Contains("Reading from the stream has failed."))
                    {
                        Logfile.Log($"Wait for DB ({x}/300): Connection refused.");
                    }
                    else
                    {
                        ex.ToExceptionless().FirstCarUserID().Submit();
                        Logfile.Log($"DBCONNECTION {ex.Message}");
                    }

                    await Task.Delay(15000, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static void InitCheckDocker()
        {
            try
            {
                string newFilePath = FileManager.GetFilePath(TLFilename.SettingsFilename);

                if (!File.Exists(newFilePath))
                {
                    var p = Path.GetDirectoryName(newFilePath);
                    if (!Directory.Exists(p))
                        Directory.CreateDirectory(p);

                    string oldFilePath = "/etc/teslalogger/settings.json";

                    if (File.Exists(oldFilePath))
                    {
                        File.Move(oldFilePath, newFilePath);
                        Logfile.Log($"settings.json moved to :{newFilePath}");
                    }
                    else
                    {
                        Logfile.Log($"Creating empty settings.json {newFilePath}");
                        File.AppendAllText(newFilePath, GetDefaultConfigFileContent());
                        UpdateTeslalogger.Chmod(newFilePath, 666);
                    }
                }

                if (Tools.IsDocker())
                {
                    Logfile.Log("Docker: YES!");

                    ExceptionlessClient.Default.Configuration.DefaultData.Add("Docker", true);

                    if (!Directory.Exists("/etc/teslalogger/backup"))
                    {
                        Directory.CreateDirectory("/etc/teslalogger/backup");
                        UpdateTeslalogger.Chmod("/etc/teslalogger/backup", 777);
                    }

                    if (!Directory.Exists("/etc/teslalogger/Exception"))
                    {
                        Directory.CreateDirectory("/etc/teslalogger/Exception");
                        UpdateTeslalogger.Chmod("/etc/teslalogger/Exception", 777);
                    }
                }
                else
                {
                    Logfile.Log("Docker: NO!");
                }

                if (Tools.IsDockerNET8())
                {
                    Logfile.Log("Docker NET8: YES!");
                    
                    Tools.CopyFilesRecursively(new DirectoryInfo("/etc/teslalogger/git/TeslaLogger/GrafanaPlugins"), new DirectoryInfo("/var/lib/grafana/plugins"));
                    
                    if (!File.Exists(UpdateTeslalogger.TimeLinePanelLanguagePath))
                        UpdateTeslalogger.CopyLanguageFileToTimelinePanel("en");

                    if (!File.Exists(UpdateTeslalogger.TimeLinePanelSettingsPath))
                        UpdateTeslalogger.CopySettingsToTimelinePanel();
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        public static string GetDefaultConfigFileContent()
        {
            return "{\"SleepTimeSpanStart\":\"\",\"SleepTimeSpanEnd\":\"\",\"SleepTimeSpanEnable\":\"false\",\"Power\":\"hp\",\"Temperature\":\"celsius\",\"Length\":\"km\",\"Pressure\":\"bar\",\"Language\":\"en\",\"URL_Admin\":\"\",\"ScanMyTesla\":\"false\"}";
        }

        /// <summary>
        /// Runs background housekeeping tasks including CO2 updates and cache cleanup.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Uses a background task instead of a manual thread to allow awaiting async operations.
        /// Respects both UpdateTeslalogger.done and application cancellation tokens.
        /// </remarks>
        internal static void RunHousekeepingInBackground(CancellationToken cancellationToken = default)
        {
            _ = Task.Run(async () =>
            {
                // wait for DB updates
                try
                {
                    while (!UpdateTeslalogger.done.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        // Create composite cancellation token that covers both sources
                        using (var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(UpdateTeslalogger.done.Token, cancellationToken))
                        {
                            await Task.Delay(5000, linkedTokenSource.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (System.Threading.Tasks.TaskCanceledException)
                {
                    // cancellation requested, continue to housekeeping
                    Logfile.Log("RunHousekeepingInBackground: Cancellation requested");
                }

                DateTime start = DateTime.Now;
                Logfile.Log("RunHousekeepingInBackground started");
                Tools.Housekeeping();
                await DBHelper.UpdateCO2Async().ConfigureAwait(false);
                GeocodeCache.Cleanup();
                Logfile.Log($"RunHousekeepingInBackground finished, took {(DateTime.Now - start).TotalMilliseconds}ms");
            }, cancellationToken);
        }

        /// <summary>
        /// Initializes background geofence updates from online sources.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Periodically syncs geofence data online in a background task that respects cancellation.
        /// Uses a background task instead of manual Thread so we can await async methods.
        /// </remarks>
        internal static void OnlineUpdateGeofenceInBackground(CancellationToken cancellationToken = default)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // initially sleep 5min
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                    
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            await Geofence.GetInstance().OnlineUpdateAsync().ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            Logfile.Log("OnlineUpdateGeofenceInBackground cancelled");
                            break;
                        }
                        catch (Exception ex)
                        {
                            Logfile.ExceptionWriter(ex, "OnlineUpdateGeofenceInBackground");
                        }
                        
                        await Task.Delay(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    Logfile.Log("OnlineUpdateGeofenceInBackground task cancelled");
                }
            }, cancellationToken);
        }

        private static void ExitTeslaLogger(string? _msg, int _exitcode = 0)
        {
            Logfile.Log($"Exit: {_msg}");
            Environment.Exit(_exitcode);
        }

        /// <summary>
        /// Initializes the static map generation service for trip visualization.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Starts the StaticMapService in a background task that respects cancellation signals.
        /// </remarks>
        private static void InitStaticMapService(CancellationToken cancellationToken = default)
        {
            try
            {
                Task.Run(() =>
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        StaticMapService.GetSingleton().Run();
                    }
                    catch (OperationCanceledException)
                    {
                        Logfile.Log("StaticMapService cancelled");
                    }
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Initializes background database update tasks with proper cancellation support.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <remarks>
        /// Runs database maintenance tasks once per day, including elevation updates and charging analysis.
        /// Respects application-wide cancellation signals for graceful shutdown.
        /// </remarks>
        private static void UpdateDbInBackground(CancellationToken cancellationToken = default)
        {
            // Run only once a day per version
            string kvskey = "UpdateDbInBackground";
            string check = $"{DateTime.Now:yyyyMMdd}-{Assembly.GetExecutingAssembly().GetName().Version}";

            if (KVS.Get(kvskey, out string updateDbInBackground) == KVS.SUCCESS)
            {
                if (updateDbInBackground == check)
                {
                    Logfile.Log("UpdateDbInBackground: SKIP today");
                    // run HouseKeeping anyway
                    RunHousekeepingInBackground(cancellationToken);
                    return;
                }
            }

            Task.Run(async () =>
            {
                try
                {
                    // wait for DB updates - respect both UpdateTeslalogger.done and application cancellation token
                    while (!UpdateTeslalogger.done.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                        await Task.Delay(5000, cancellationToken).ConfigureAwait(false);

                    await Task.Delay(30000);

                    DateTime start = DateTime.Now;
                    Logfile.Log("UpdateDbInBackground started");

                    DBHelper.UpdateElevationForAllPoints();
                    WebHelper.UpdateAllPOIAddresses();
                    DBHelper.DeleteDuplicateTrips();

                    for (int i = 0; i < Car.Allcars.Count; i++)
                    {
                        Car c = Car.Allcars[i];
                        c.DbHelper.CombineChangingStates();
                        c.webhelper.UpdateAllEmptyAddresses();
                        c.DbHelper.UpdateEmptyChargeEnergy();
                        c.DbHelper.UpdateEmptyUnplugDate();
                        c.DbHelper.AnalyzeChargingStates();
                        c.DbHelper.UpdateAllDriveHeightStatistics();
                    }

                    DBHelper.UpdateAllNullAmpereCharging();
                    DBHelper.UpdateIncompleteTrips();
                    DBHelper.UpdateAllChargingMaxPower();

                    for (int x = 0; x < Car.Allcars.Count; x++)
                    {
                        Car c = Car.Allcars[x];
                        ShareData sd = new ShareData(c);
                        await sd.SendAllChargingDataAsync();
                        await sd.SendDegradationDataAsync();
                        await sd.SendAllDrivingDataAsync();
                    }

                    DBHelper.UpdateCarIDNull();

                    StaticMapService.CreateAllTripMaps();
                    StaticMapService.CreateAllChargingMaps();
                    StaticMapService.CreateAllParkingMaps();

                    // DBHelper.UpdateCO2();

                    Journeys.UpdateAllJourneys();

                    Car.LogActiveCars();

                    WebHelper.SearchFornewCars();

                    GeocodeCache.Cleanup();

                    DBHelper.MigratePosOdometerNullValues();

                    Logfile.Log($"UpdateDbInBackground finished, took {(DateTime.Now - start).TotalMilliseconds}ms");
                    RunHousekeepingInBackground(cancellationToken);

                    KVS.InsertOrUpdate(kvskey, check);
                }
                catch (Exception ex)
                {
                    ex.ToExceptionless().FirstCarUserID().Submit();
                    Logfile.Log(ex.ToString());
                }
            });
        }
    }
}


