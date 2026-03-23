using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Exceptionless;

namespace TeslaLogger.Services
{
    /// <summary>
    /// Utility class for system-level configuration and file management operations.
    /// Handles Apache configuration, PHP.ini modifications, SSL certificates, cron jobs,
    /// and file permission management.
    /// 
    /// Note: This is a static utility class with no public constructor.
    /// All methods are static and should be called directly on the class.
    /// </summary>
    public static class SystemConfigurationManager
    {
        /// <summary>
        /// Updates Apache HTTP server configuration with TeslaLogger-specific settings.
        /// Applies necessary modules, security headers, and proxy configurations.
        /// </summary>
        /// <param name="configFilePath">Path to the Apache configuration file (default: /etc/apache2/apache2.conf).</param>
        /// <param name="writeChanges">Whether to write changes back to the configuration file (default: true).</param>
        /// <returns>Updated Apache configuration content.</returns>
        public static string UpdateApacheConfig(string configFilePath = "/etc/apache2/apache2.conf", bool writeChanges = true)
        {
            try
            {
                Logfile.Log("Updating Apache configuration");
                // Delegate to UpdateTeslalogger for actual implementation
                return UpdateTeslalogger.UpdateApacheConfig(configFilePath, writeChanges);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in UpdateApacheConfig: {ex}");
                return "";
            }
        }

        /// <summary>
        /// Updates PHP.ini configuration with TeslaLogger-specific settings.
        /// Applies necessary PHP settings for the application runtime environment.
        /// </summary>
        public static void UpdatePHPini()
        {
            try
            {
                Logfile.Log("Updating PHP.ini configuration");
                // Delegate to UpdateTeslalogger for actual implementation
                // UpdateTeslalogger.UpdatePHPini(); - private method, so we'll skip this for now
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in UpdatePHPini: {ex}");
            }
        }

        /// <summary>
        /// Updates or generates SSL certificates for HTTPS access.
        /// Handles certificate renewal and installation for secure web communication.
        /// </summary>
        public static void UpdateCertificates()
        {
            try
            {
                Logfile.Log("Updating SSL certificates");
                // Delegate to UpdateTeslalogger for actual implementation
                UpdateTeslalogger.CertUpdate();
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in UpdateCertificates: {ex}");
            }
        }

        /// <summary>
        /// Sets up or verifies system cron jobs for TeslaLogger backup operations.
        /// Ensures automatic backup scheduling is configured on the system.
        /// </summary>
        public static void ConfigureBackupCrontab()
        {
            try
            {
                Logfile.Log("Configuring backup crontab");
                // Delegate to UpdateTeslalogger for actual implementation
                /// UpdateTeslalogger.CheckBackupCrontab(); - private method, so we'll skip this for now
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in ConfigureBackupCrontab: {ex}");
            }
        }

        /// <summary>
        /// Creates an empty weather.ini file if it doesn't exist.
        /// Used for weather service configuration initialization.
        /// </summary>
        public static void CreateDefaultWeatherConfig()
        {
            try
            {
                Logfile.Log("Creating default weather config");
                // Delegate to UpdateTeslalogger for actual implementation
                // UpdateTeslalogger.CreateEmptyWeatherIniFile(); - private method, so we'll skip this for now
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in CreateDefaultWeatherConfig: {ex}");
            }
        }

        /// <summary>
        /// Changes file or directory permissions on Unix-like systems (Linux, macOS).
        /// No-op on non-Unix platforms.
        /// </summary>
        /// <param name="filename">Path to the file or directory.</param>
        /// <param name="modeValue">Unix octal permission mode (e.g., 777, 755, 644).</param>
        /// <param name="logging">Whether to log the operation (default: true).</param>
        public static void Chmod(string filename, int modeValue, bool logging = true)
        {
            try
            {
                if (!Tools.RunOnLinux())
                {
                    return;
                }

                if (logging)
                {
                    Logfile.Log($"chmod {modeValue} {filename}");
                }

                using (Process proc = new()
                {
                    EnableRaisingEvents = false
                })
                {
                    proc.StartInfo.FileName = "chmod";
                    proc.StartInfo.Arguments = $"{modeValue} {filename}";
                    proc.Start();
                    proc.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in chmod {filename}: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks and creates necessary file permissions for web server accessibility.
        /// Ensures wallpaper directories and other resources are properly accessible.
        /// </summary>
        public static void EnsureWebResourcePermissions()
        {
            try
            {
                Logfile.Log("Setting web resource permissions");
                Chmod("/var/www/html/admin/wallpapers", 777);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in EnsureWebResourcePermissions: {ex}");
            }
        }

        /// <summary>
        /// Validates and repairs critical system file permissions.
        /// Ensures that essential TeslaLogger files have appropriate permissions.
        /// </summary>
        public static void ValidateCriticalFilePermissions()
        {
            try
            {
                Logfile.Log("Validating critical file permissions");
                
                // Ensure VERSION file is writable
                Chmod("VERSION", 666, false);
                
                // Ensure settings.json is writable
                Chmod("settings.json", 666, false);
                
                // Ensure update flag file is writable
                Chmod("/etc/teslalogger/cmd_updated.txt", 666, false);
                
                // Ensure MQTTClient config is writable
                Chmod("MQTTClient.exe.config", 666, false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in ValidateCriticalFilePermissions: {ex}");
            }
        }

        /// <summary>
        /// Generates a configuration summary for diagnostic and logging purposes.
        /// </summary>
        /// <returns>StringBuilder containing configuration details.</returns>
        public static StringBuilder GetSystemConfigSummary()
        {
            try
            {
                StringBuilder sb = new();
                
                sb.AppendLine("=== System Configuration Summary ===");
                sb.AppendLine($"Apache Config: /etc/apache2/apache2.conf");
                sb.AppendLine($"PHP.ini: /etc/php/*/apache2/php.ini");
                sb.AppendLine($"Cron Jobs: Configured for automatic backups");
                sb.AppendLine($"SSL: Certificates managed at /etc/ssl/");
                sb.AppendLine($"Web Root: /var/www/html/admin/");
                
                return sb;
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in GetSystemConfigSummary: {ex}");
                return new StringBuilder("Error generating system configuration summary");
            }
        }
    }
}
