using System.Collections.Generic;
using System.Threading.Tasks;

namespace TeslaLogger.Services
{
    /// <summary>
    /// Manages Grafana dashboard configuration, JSON templating, and datasource updates.
    /// Handles dashboard JSON modifications, plugin configuration, and language-specific content replacement.
    /// </summary>
    public interface IGrafanaDashboardConfigurer
    {
        /// <summary>
        /// Updates all Grafana dashboards with current configuration and settings.
        /// Processes dashboard JSON, applies language translations, and updates datasource references.
        /// This is a comprehensive operation that handles language files, Grafana version updates, and plugin configuration.
        /// </summary>
        /// <returns>A task representing the asynchronous Grafana update operation.</returns>
        Task UpdateGrafanaAsync();

        /// <summary>
        /// Updates all dashboard JSON files to reference the correct datasource UID.
        /// Scans dashboard JSON and replaces old UID with new UID while preserving structure.
        /// </summary>
        /// <param name="dashboardJson">The dashboard JSON content to process.</param>
        /// <param name="newDataSourceUID">The new datasource UID to inject.</param>
        /// <returns>Modified dashboard JSON with updated datasource UID.</returns>
        string UpdateDatasourceUID(string dashboardJson, string newDataSourceUID);

        /// <summary>
        /// Enables unsigned plugin loading in Grafana configuration.
        /// Adds configuration entries for custom plugins that don't have official signatures.
        /// </summary>
        /// <param name="configFilePath">Path to Grafana.ini configuration file.</param>
        /// <param name="overwriteFile">Whether to write changes back to the configuration file.</param>
        /// <returns>Updated Grafana configuration content.</returns>
        string AllowUnsignedPlugins(string configFilePath, bool overwriteFile);

        /// <summary>
        /// Copies language-specific file to the Timeline panel plugin.
        /// Enables multi-language support in the custom Timeline visualization panel.
        /// </summary>
        /// <param name="language">Language code (e.g., "en", "de", "fr").</param>
        /// <returns>A task representing the asynchronous copy operation.</returns>
        Task CopyLanguageFileToTimelinePanelAsync(string language);

        /// <summary>
        /// Copies application settings to the Timeline panel plugin configuration.
        /// Synchronizes settings between main application and the Timeline visualization.
        /// </summary>
        /// <returns>A task representing the asynchronous copy operation.</returns>
        Task CopySettingsToTimelinePanelAsync();

        /// <summary>
        /// Updates dashboard JSON to set the default vehicle (car) display.
        /// Modifies dashboard template variables to show specific vehicle data by default.
        /// </summary>
        /// <param name="dashboardJson">The dashboard JSON content to process.</param>
        /// <param name="carName">Display name of the vehicle.</param>
        /// <param name="carId">Unique identifier of the vehicle.</param>
        /// <param name="carLabel">Localized label for "Car" in the UI.</param>
        /// <returns>Modified dashboard JSON with default car configured.</returns>
        string UpdateDefaultCar(string dashboardJson, string carName, string carId, string carLabel);

        /// <summary>
        /// Replaces {{value_xxx}} placeholder tags with translated values.
        /// Used to localize numeric values and field references in dashboards.
        /// </summary>
        /// <param name="content">Dashboard JSON content with value tags.</param>
        /// <param name="translations">Dictionary mapping old values to translated values.</param>
        /// <returns>Content with all value tags replaced.</returns>
        string ReplaceValuesTags(string content, Dictionary<string, string> translations);

        /// <summary>
        /// Replaces {{alias_xxx}} placeholder tags with translated aliases.
        /// Used to localize field aliases and display names in dashboard queries.
        /// </summary>
        /// <param name="content">Dashboard JSON content with alias tags.</param>
        /// <param name="translations">Dictionary mapping old aliases to translated aliases.</param>
        /// <returns>Content with all alias tags replaced.</returns>
        string ReplaceAliasTags(string content, Dictionary<string, string> translations);

        /// <summary>
        /// Replaces {{lang_xxx}} placeholder tags with language-specific translations.
        /// Handles multiple placeholder variations and quoted vs. unquoted contexts.
        /// </summary>
        /// <param name="content">Dashboard JSON content with language tags.</param>
        /// <param name="languageKeys">Array of keys to translate.</param>
        /// <param name="translations">Dictionary mapping language keys to translated values.</param>
        /// <param name="quoted">Whether the tags are quoted in the JSON.</param>
        /// <returns>Content with all language tags replaced.</returns>
        string ReplaceLanguageTags(string content, string[] languageKeys, Dictionary<string, string> translations, bool quoted);

        /// <summary>
        /// Replaces {{title}} placeholder tags with localized dashboard titles.
        /// Used to translate top-level dashboard names.
        /// </summary>
        /// <param name="content">Dashboard JSON content with title tags.</param>
        /// <param name="titleKey">Original title key to search for.</param>
        /// <param name="translations">Dictionary mapping title keys to translated values.</param>
        /// <returns>Content with title tag replaced.</returns>
        string ReplaceTitleTag(string content, string titleKey, Dictionary<string, string> translations);

        /// <summary>
        /// Replaces {{name}} placeholder tags with translated component names.
        /// Used to localize component display names in dashboards.
        /// </summary>
        /// <param name="content">Dashboard JSON content with name tags.</param>
        /// <param name="nameKey">Original name key to search for.</param>
        /// <param name="translations">Dictionary mapping name keys to translated values.</param>
        /// <returns>Content with name tag replaced.</returns>
        string ReplaceNameTag(string content, string nameKey, Dictionary<string, string> translations);

        /// <summary>
        /// Extracts title, UID, and Grafana link from dashboard JSON.
        /// Parses dashboard metadata to build dashboard URLs.
        /// </summary>
        /// <param name="dashboardJson">Dashboard JSON content to parse.</param>
        /// <param name="grafanaUrl">Base URL of Grafana server.</param>
        /// <param name="title">Output parameter: Dashboard title.</param>
        /// <param name="uid">Output parameter: Dashboard unique identifier.</param>
        /// <param name="link">Output parameter: Full Grafana dashboard link.</param>
        void ExtractDashboardMetadata(string dashboardJson, string grafanaUrl, out string title, out string uid, out string link);

        /// <summary>
        /// Gets the language localization file path for a given language.
        /// Constructs path to language-specific translation files.
        /// </summary>
        /// <param name="language">Language code (e.g., "en", "de").</param>
        /// <returns>Full path to language file, or empty string if not found.</returns>
        string GetLanguageFilePath(string language);
    }
}
