using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Exceptionless;

namespace TeslaLogger.Services
{
    /// <summary>
    /// Manages application version checking, updates, and installation.
    /// Handles version comparisons, downloads, and system-level update operations.
    /// </summary>
    public class ApplicationUpdateManager : IApplicationUpdateManager
    {
        private static DateTime lastVersionCheck = DateTime.UtcNow;
        private static readonly SemaphoreSlim lastVersionCheckLock = new(1, 1);
        private const string UpdateProgressFile = "/tmp/teslalogger-cmd-restart.txt";

        /// <summary>
        /// Raised when update progress changes.
        /// </summary>
        public event EventHandler<UpdateProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Initializes a new instance of the ApplicationUpdateManager class.
        /// </summary>
        public ApplicationUpdateManager()
        {
        }

        /// <summary>
        /// Checks for new version availability asynchronously.
        /// </summary>
        public async Task CheckForNewVersionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await lastVersionCheckLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    lastVersionCheck = DateTime.UtcNow;
                    OnProgressChanged("Checking", 0, "Checking for new version...");
                    
                    // Call the existing UpdateTeslalogger method
                    await Task.Run(() => UpdateTeslalogger.CheckForNewVersion(), cancellationToken)
                        .ConfigureAwait(false);

                    OnProgressChanged("Checking", 100, "Version check completed");
                }
                finally
                {
                    lastVersionCheckLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Logfile.Log("Version check was canceled");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in CheckForNewVersionAsync: {ex}");
            }
        }

        /// <summary>
        /// Determines if an update is needed by comparing versions.
        /// </summary>
        public async Task<bool> UpdateNeededAsync(string currentVersion, string onlineVersion)
        {
            try
            {
                return await Task.Run(() =>
                {
                    if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(onlineVersion))
                    {
                        return false;
                    }

                    try
                    {
                        var current = Version.Parse(currentVersion);
                        var online = Version.Parse(onlineVersion);
                        return online > current;
                    }
                    catch (Exception ex)
                    {
                        Logfile.Log($"Error parsing versions: current={currentVersion}, online={onlineVersion}, error={ex.Message}");
                        return false;
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in UpdateNeededAsync: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Downloads and installs the latest application update asynchronously.
        /// </summary>
        public async Task DownloadAndInstallUpdateAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                OnProgressChanged("Preparing", 0, "Starting update process...");
                
                if (!IsNET8Installed())
                {
                    OnProgressChanged("Error", 0, ".NET 8 not installed");
                    Logfile.Log("Update failed: .NET 8 not installed");
                    return;
                }

                if (File.Exists(UpdateProgressFile))
                {
                    Logfile.Log("Update already in progress, skipping");
                    OnProgressChanged("Skipped", 0, "Update already in progress");
                    return;
                }

                // Create progress file
                File.WriteAllText(UpdateProgressFile, DateTime.Now.ToLongTimeString());
                Logfile.Log("Start update");
                OnProgressChanged("Initializing", 10, "Initializing update...");
                
                ExceptionlessClient.Default
                    .CreateLog("Install", $"Start update from {Assembly.GetExecutingAssembly().GetName().Version}")
                    .Submit();

                // Handle Docker updates via Watchtower
                if (Tools.IsDockerNET8())
                {
                    await HandleDockerUpdateAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                // Handle standalone updates
                if (Tools.IsMono() || Tools.IsDotnet8())
                {
                    await HandleStandaloneUpdateAsync(cancellationToken).ConfigureAwait(false);
                }

                OnProgressChanged("Complete", 100, "Update process completed");
            }
            catch (OperationCanceledException)
            {
                Logfile.Log("Update was cancelled");
                OnProgressChanged("Cancelled", 0, "Update was cancelled");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in DownloadAndInstallUpdateAsync: {ex}");
                OnProgressChanged("Error", 0, $"Update failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies that .NET 8 is properly installed on the system.
        /// </summary>
        public bool IsNET8Installed()
        {
            try
            {
                // Simple check: verify dotnet executable is available and reports version 8.x
                string output = Tools.ExecMono("dotnet", "--version", false);
                return output.StartsWith("8.", StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error checking .NET 8 installation: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Performs pre-update system checks and validations.
        /// </summary>
        public async Task<bool> PerformPreUpdateChecksAsync()
        {
            try
            {
                return await Task.Run(() =>
                {
                    OnProgressChanged("PreCheck", 10, "Checking system requirements...");
                    
                    // Check .NET 8
                    if (!IsNET8Installed())
                    {
                        OnProgressChanged("PreCheck", 0, ".NET 8 is not installed");
                        return false;
                    }

                    OnProgressChanged("PreCheck", 50, ".NET 8 verified");

                    // Check disk space
                    if (!CheckDiskSpace())
                    {
                        OnProgressChanged("PreCheck", 0, "Insufficient disk space");
                        return false;
                    }

                    OnProgressChanged("PreCheck", 100, "All pre-update checks passed");
                    return true;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in PerformPreUpdateChecksAsync: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Gets the timestamp of the last version check.
        /// </summary>
        public DateTime GetLastVersionCheck()
        {
            return lastVersionCheck;
        }

        /// <summary>
        /// Restarts the application to complete the update process.
        /// </summary>
        public async Task RestartApplicationAsync()
        {
            try
            {
                OnProgressChanged("Restarting", 100, "Restarting application...");
                
                await Task.Delay(2000).ConfigureAwait(false); // Give UI time to update
                
                // Signal application to restart via process exit code
                Environment.Exit(100);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in RestartApplicationAsync: {ex}");
            }
        }

        #region Private Helper Methods

        private async Task HandleDockerUpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                Logfile.Log("Using Watchtower for Docker update");
                OnProgressChanged("Docker", 25, "Triggering Watchtower update...");

                using (HttpClient client = new())
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer teslalogger");
                    try
                    {
                        await client.GetAsync("http://watchtower:8080/v1/update", cancellationToken)
                            .ConfigureAwait(false);
                        OnProgressChanged("Docker", 100, "Watchtower update triggered");
                    }
                    catch (HttpRequestException ex)
                    {
                        ex.ToExceptionless().FirstCarUserID().Submit();
                        Logfile.ExceptionWriter(ex, "Failed to connect to Watchtower");
                        OnProgressChanged("Error", 0, "Docker update failed");
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "Exception during Watchtower update");
                OnProgressChanged("Error", 0, "Docker update error");
            }
        }

        private async Task HandleStandaloneUpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                OnProgressChanged("Backup", 20, "Creating backup...");

                // Set file permissions
                UpdateTeslalogger.Chmod("VERSION", 666, false);
                UpdateTeslalogger.Chmod("settings.json", 666, false);
                UpdateTeslalogger.Chmod("/etc/teslalogger/cmd_updated.txt", 666, false);
                UpdateTeslalogger.Chmod("MQTTClient.exe.config", 666, false);

                // Create backup
                if (!File.Exists("NOBACKUPONUPDATE"))
                {
                    Logfile.Log("Creating backup");
                    Tools.ExecMono("/bin/bash", "/etc/teslalogger/backup.sh");
                }

                OnProgressChanged("Git", 40, "Updating via Git...");

                // Check for Git and pull updates
                if (!Tools.ExecMono("git", "--version", false).Contains("git version"))
                {
                    Logfile.Log("Git not found, update skipped");
                    OnProgressChanged("Error", 0, "Git not available");
                    return;
                }

                // Pull latest changes from Git
                string output = Tools.ExecMono("git", "pull", false);
                Logfile.Log($"Git pull output: {output}");

                OnProgressChanged("Building", 60, "Building application...");

                // Rebuild application
                await RebuildApplicationAsync(cancellationToken).ConfigureAwait(false);

                OnProgressChanged("Finishing", 90, "Finalizing update...");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in HandleStandaloneUpdateAsync: {ex}");
                OnProgressChanged("Error", 0, "Standalone update failed");
            }
        }

        private async Task RebuildApplicationAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Rebuild using dotnet build
                await Task.Run(() =>
                {
                    Tools.ExecMono("dotnet", "build", false);
                }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error rebuilding application: {ex}");
            }
        }

        private bool CheckDiskSpace()
        {
            try
            {
                // Simple check: ensure temp directory is writable and has space
                string tempPath = Path.GetTempPath();
                DriveInfo driveInfo = new(tempPath);
                return driveInfo.AvailableFreeSpace > 500 * 1024 * 1024; // 500 MB minimum
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error checking disk space: {ex}");
                return false;
            }
        }

        private void OnProgressChanged(string stage, int percentComplete, string message)
        {
            ProgressChanged?.Invoke(this, new UpdateProgressEventArgs
            {
                Stage = stage,
                PercentComplete = Math.Clamp(percentComplete, 0, 100),
                Message = message
            });
        }

        #endregion
    }
}
