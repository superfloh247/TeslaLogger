using System;
using System.Threading;
using System.Threading.Tasks;

namespace TeslaLogger.Services
{
    /// <summary>
    /// Event arguments for application update progress notifications.
    /// </summary>
    public class UpdateProgressEventArgs : EventArgs
    {
        /// <summary>Gets the current update stage (e.g., "Checking", "Downloading", "Installing").</summary>
        public required string Stage { get; set; }

        /// <summary>Gets the completion percentage (0-100).</summary>
        public int PercentComplete { get; set; }

        /// <summary>Gets the descriptive message for the current stage.</summary>
        public required string Message { get; set; }
    }

    /// <summary>
    /// Manages application version checking, updates, and installation.
    /// Handles version comparisons, downloads, and system-level update operations.
    /// </summary>
    public interface IApplicationUpdateManager
    {
        /// <summary>
        /// Raised when update progress changes.
        /// </summary>
        event EventHandler<UpdateProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Checks for new version availability asynchronously.
        /// Queries online service to determine if an update is available.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the operation.</param>
        /// <returns>A task representing the asynchronous version check operation.</returns>
        Task CheckForNewVersionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines if an update is needed by comparing versions.
        /// Implements semantic versioning comparison logic.
        /// </summary>
        /// <param name="currentVersion">The current application version.</param>
        /// <param name="onlineVersion">The latest available version from the server.</param>
        /// <returns>True if an update is needed, false otherwise.</returns>
        Task<bool> UpdateNeededAsync(string currentVersion, string onlineVersion);

        /// <summary>
        /// Downloads and installs the latest application update asynchronously.
        /// Handles backup creation, Git pulls, and post-install verification.
        /// Raises ProgressChanged events at each stage.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token for the download/install operation.</param>
        /// <returns>A task representing the asynchronous download and install operation.</returns>
        Task DownloadAndInstallUpdateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Verifies that .NET 8 is properly installed on the system.
        /// Checks for dotnet executable and version compatibility.
        /// </summary>
        /// <returns>True if .NET 8 is installed and verified, false otherwise.</returns>
        bool IsNET8Installed();

        /// <summary>
        /// Performs pre-update system checks and validations.
        /// Verifies prerequisites for safe update installation.
        /// </summary>
        /// <returns>A task representing the asynchronous pre-check operation.</returns>
        Task<bool> PerformPreUpdateChecksAsync();

        /// <summary>
        /// Gets the timestamp of the last version check.
        /// Used to avoid excessive server queries.
        /// </summary>
        /// <returns>DateTime of the last version check.</returns>
        DateTime GetLastVersionCheck();

        /// <summary>
        /// Restarts the application to complete the update process.
        /// May use system commands or graceful shutdown depending on environment.
        /// </summary>
        /// <returns>A task representing the asynchronous restart operation.</returns>
        Task RestartApplicationAsync();
    }
}
