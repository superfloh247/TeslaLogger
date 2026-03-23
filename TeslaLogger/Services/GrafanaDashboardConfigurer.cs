using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Exceptionless;

namespace TeslaLogger.Services
{
    /// <summary>
    /// Manages Grafana dashboard configuration, JSON templating, and datasource updates.
    /// Handles dashboard JSON modifications, plugin configuration, and language-specific content replacement.
    /// </summary>
    public class GrafanaDashboardConfigurer : IGrafanaDashboardConfigurer
    {
        public const string TimeLinePanelLanguagePath = "/var/lib/grafana/plugins/teslalogger-timeline-panel/dist/language.txt";
        public const string TimeLinePanelSettingsPath = "/var/lib/grafana/plugins/teslalogger-timeline-panel/dist/settings.json";

        /// <summary>
        /// Initializes a new instance of the GrafanaDashboardConfigurer class.
        /// </summary>
        public GrafanaDashboardConfigurer()
        {
        }

        /// <summary>
        /// Updates all Grafana dashboards with current configuration and settings.
        /// </summary>
        public async Task UpdateGrafanaAsync()
        {
            try
            {
                Logfile.Log("Start Grafana update");
                
                await Task.Run(() => UpdateTeslalogger.UpdateGrafanaAsync())
                    .ConfigureAwait(false);
                
                Logfile.Log("End Grafana update");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in UpdateGrafanaAsync: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Updates all dashboard JSON files to reference the correct datasource UID.
        /// </summary>
        public string UpdateDatasourceUID(string dashboardJson, string newDataSourceUID)
        {
            try
            {
                string pattern = "(\\\"datasource\\\":\\s+{\\s+\\\"type\\\":\\s+\\\"mysql\\\",\\s+\\\"uid\\\":\\s+\\\")(.*?)(\\\")";
                Regex r = new(pattern, RegexOptions.Compiled | RegexOptions.Singleline);
                var m = r.Match(dashboardJson);
                if (!m.Success)
                {
                    return dashboardJson;
                }

                return r.Replace(dashboardJson, $"${{1}}{newDataSourceUID}${{3}}");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in UpdateDatasourceUID: {ex}");
                return dashboardJson;
            }
        }

        /// <summary>
        /// Enables unsigned plugin loading in Grafana configuration.
        /// </summary>
        public string AllowUnsignedPlugins(string configFilePath, bool overwriteFile)
        {
            try
            {
                Logfile.Log("Start Grafana.ini -> AllowUnsignedPlugins");

                var content = File.ReadAllText(configFilePath);
                if (content.Contains("[plugins]"))
                {
                    if (!content.Contains("allow_loading_unsigned_plugins"))
                    {
                        Logfile.Log("Grafana.ini -> AllowUnsignedPlugins with [plugin] section");
                        content = content.Replace("[plugins]", "[plugins]\r\nallow_loading_unsigned_plugins=natel-discrete-panel,pr0ps-trackmap-panel,teslalogger-timeline-panel\r\n");
                        if (overwriteFile)
                        {
                            File.WriteAllText(configFilePath, content);
                            Logfile.Log("Grafana.ini -> AllowUnsignedPlugins - Write File");
                        }
                        return content;
                    }

                    Logfile.Log("Grafana.ini -> AllowUnsignedPlugins - Plugins Section available");
                    return content;
                }

                if (content.Contains("allow_loading_unsigned_plugins"))
                {
                    Logfile.Log("Grafana.ini -> AllowUnsignedPlugins - allow_loading_unsigned_plugins available");
                    return content;
                }

                Logfile.Log("Grafana.ini -> AllowUnsignedPlugins");

                content += "[plugins]\r\nallow_loading_unsigned_plugins=natel-discrete-panel,pr0ps-trackmap-panel,teslalogger-timeline-panel\r\n";

                if (overwriteFile)
                {
                    File.WriteAllText(configFilePath, content);
                    Logfile.Log("Grafana.ini -> AllowUnsignedPlugins - Write File");
                }
                return content;
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in AllowUnsignedPlugins: {ex}");
                return "";
            }
        }

        /// <summary>
        /// Copies language-specific file to the Timeline panel plugin.
        /// </summary>
        public async Task CopyLanguageFileToTimelinePanelAsync(string language)
        {
            try
            {
                await Task.Run(() =>
                {
                    string languageFilepath = GetLanguageFilePath(language);
                    if (File.Exists(languageFilepath))
                    {
                        Logfile.Log($"Copy {languageFilepath} to {TimeLinePanelLanguagePath}");
                        File.Copy(languageFilepath, TimeLinePanelLanguagePath, true);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in CopyLanguageFileToTimelinePanelAsync: {ex}");
            }
        }

        /// <summary>
        /// Copies application settings to the Timeline panel plugin configuration.
        /// </summary>
        public async Task CopySettingsToTimelinePanelAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    string settingsFilepath = "/etc/teslalogger/settings.json";
                    if (Tools.IsDockerNET8())
                        settingsFilepath = "/etc/teslalogger/data/settings.json";

                    if (File.Exists(settingsFilepath))
                    {
                        Logfile.Log($"Copy {settingsFilepath} to {TimeLinePanelSettingsPath}");
                        File.Copy(settingsFilepath, TimeLinePanelSettingsPath, true);
                    }
                    else
                        Logfile.Log($"Copy: {settingsFilepath} NOT FOUND!!!");

                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in CopySettingsToTimelinePanelAsync: {ex}");
            }
        }

        /// <summary>
        /// Updates dashboard JSON to set the default vehicle (car) display.
        /// </summary>
        public string UpdateDefaultCar(string dashboardJson, string carName, string carId, string carLabel)
        {
            try
            {
                if (carName is null || carName.Length == 0)
                    return dashboardJson;

                Regex regexAlias = new("(templating.*\\\"text\\\":\\s\\\")(\\\".*?value\\\":\\s\\\")(.*?)(\\\")(.*?display_name)(.*?label\\\":\\s\\\")(.*?)(\\\")", RegexOptions.Singleline | RegexOptions.Multiline);
                return regexAlias.Replace(dashboardJson, $"${{1}}{carName}${{2}}{carId}${{4}}${{5}}${{6}}{carLabel}${{8}}");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in UpdateDefaultCar: {ex}");
                return dashboardJson;
            }
        }

        /// <summary>
        /// Replaces {{value_xxx}} placeholder tags with translated values.
        /// </summary>
        public string ReplaceValuesTags(string content, Dictionary<string, string> translations)
        {
            try
            {
                Regex regexAlias = new("\\\"displayName\\\",\\s*\\\"value\\\":.*?\\\"(.+)\\\"");

                MatchCollection matches = regexAlias.Matches(content);

                foreach (Match match in matches)
                {
                    content = ReplaceValueTag(content, match.Groups[1].Value, translations);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in ReplaceValuesTags: {ex}");
            }

            return content;
        }

        /// <summary>
        /// Replaces {{alias_xxx}} placeholder tags with translated aliases.
        /// </summary>
        public string ReplaceAliasTags(string content, Dictionary<string, string> translations)
        {
            try
            {
                Regex regexAlias = new("\\\"alias\\\":.*?\\\"(.+)\\\"");

                MatchCollection matches = regexAlias.Matches(content);

                foreach (Match match in matches)
                {
                    content = ReplaceAliasTag(content, match.Groups[1].Value, translations);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in ReplaceAliasTags: {ex}");
            }

            return content;
        }

        /// <summary>
        /// Replaces {{lang_xxx}} placeholder tags with language-specific translations.
        /// </summary>
        public string ReplaceLanguageTags(string content, string[] languageKeys, Dictionary<string, string> translations, bool quoted)
        {
            foreach (string langKey in languageKeys)
            {
                content = ReplaceLanguageTag(content, langKey, translations, quoted);
            }

            return content;
        }

        /// <summary>
        /// Replaces {{title}} placeholder tags with localized dashboard titles.
        /// </summary>
        public string ReplaceTitleTag(string content, string titleKey, Dictionary<string, string> translations)
        {
            try
            {
                if (!translations.ContainsKey(titleKey))
                {
                    Logfile.Log($"Key '{titleKey}' not found in translations (title)");
                    return content;
                }

                Regex regexAlias = new($"\\\"title\\\":.*?\\\"{titleKey}\\\"");
                string replace = $"\"title\": \"{translations[titleKey]}\"";

                return regexAlias.Replace(content, replace);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                return content;
            }
        }

        /// <summary>
        /// Replaces {{name}} placeholder tags with translated component names.
        /// </summary>
        public string ReplaceNameTag(string content, string nameKey, Dictionary<string, string> translations)
        {
            try
            {
                if (!translations.ContainsKey(nameKey))
                {
                    Logfile.Log($"Key '{nameKey}' not found in translations (name)");
                    return content;
                }

                Regex regexAlias = new($"\\\"name\\\":.*?\\\"{nameKey}\\\"");
                string replace = $"\"name\": \"{translations[nameKey]}\"";

                return regexAlias.Replace(content, replace);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                return content;
            }
        }

        /// <summary>
        /// Extracts title, UID, and Grafana link from dashboard JSON.
        /// </summary>
        public void ExtractDashboardMetadata(string dashboardJson, string grafanaUrl, out string title, out string uid, out string link)
        {
            title = "";
            uid = "";
            link = "";
            try
            {
                JObject j = JObject.Parse(dashboardJson);
                title = j["title"]?.ToString() ?? "";
                uid = j["uid"]?.ToString() ?? "";

                if (!grafanaUrl.EndsWith("/", StringComparison.Ordinal))
                {
                    grafanaUrl += "/";
                }

                link = $"{grafanaUrl}d/{uid}/{title}";
            }
            catch (Newtonsoft.Json.JsonException jsonEx)
            {
                Logfile.Log($"GrafanaDashboardConfigurer: JSON parse error - {jsonEx.Message}");
                jsonEx.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(jsonEx, "");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        /// <summary>
        /// Gets the language localization file path for a given language.
        /// </summary>
        public string GetLanguageFilePath(string language)
        {
            try
            {
                // Construct language file path based on language code
                // Language files are typically in: /etc/teslalogger/languages/
                string languageFile = $"/etc/teslalogger/languages/{language}.txt";
                
                if (File.Exists(languageFile))
                {
                    return languageFile;
                }

                // Fallback to German if not found
                languageFile = $"/etc/teslalogger/languages/de.txt";
                return File.Exists(languageFile) ? languageFile : "";
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error getting language file path: {ex}");
                return "";
            }
        }

        #region Private Helper Methods

        private static string ReplaceValueTag(string content, string valueKey, Dictionary<string, string> translations)
        {
            try
            {
                if (!translations.ContainsKey(valueKey))
                {
                    Logfile.Log($"Key '{valueKey}' not found in translations (value)");
                    return content;
                }

                Regex regexAlias = new($"\\\"value\\\":.*?\\\"{valueKey}\\\"");
                string replace = $"\"value\": \"{translations[valueKey]}\"";

                return regexAlias.Replace(content, replace);
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error in ReplaceValueTag: {ex}");
                return content;
            }
        }

        private static string ReplaceAliasTag(string content, string aliasKey, Dictionary<string, string> translations)
        {
            try
            {
                if (!translations.ContainsKey(aliasKey))
                {
                    Logfile.Log($"Key '{aliasKey}' not found in translations (alias)");
                    return content;
                }

                Regex regexAlias = new($"\\\"alias\\\":.*?\\\"{aliasKey}\\\"");
                string replace = $"\"alias\": \"{translations[aliasKey]}\"";

                return regexAlias.Replace(content, replace);
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error in ReplaceAliasTag: {ex}");
                return content;
            }
        }

        private static string ReplaceLanguageTag(string content, string languageKey, Dictionary<string, string> translations, bool quoted)
        {
            try
            {
                if (!translations.ContainsKey(languageKey))
                {
                    Logfile.Log($"Key '{languageKey}' not found in translations");
                    return content;
                }

                if (quoted)
                {
                    content = ReplaceLanguageTagQuoted(content, languageKey, translations[languageKey]);
                }
                else
                {
                    content = content.Replace(languageKey, translations[languageKey]);
                }

                return content;
            }
            catch (Exception ex)
            {
                Logfile.Log($"Error in ReplaceLanguageTag: {ex}");
                return content;
            }
        }

        private static string ReplaceLanguageTagQuoted(string content, string oldText, string newText)
        {
            content = content.Replace($"'{oldText}'", $"'{newText}'");
            return content.Replace($"\"{oldText}\"", $"\"{newText}\"");
        }

        #endregion
    }
}
