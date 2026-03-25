/// <summary>
/// PHASE 1F CONFIGURATION HELPERS - DBHelper Token & Configuration Decomposition
/// 
/// This partial class extracts token management and configuration operations
/// from the main DBHelper class, improving separation of concerns and maintainability.
/// 
/// Includes:
/// - Tesla API token management (access & refresh tokens)
/// - ABRP (A Better Route Planner) integration credentials
/// - SuC Bingo (Supercharger Bingo) integration credentials
/// - Car configuration and settings persistence
/// - Encrypted credential storage and retrieval
/// 
/// Benefits:
/// - Improves code organization (7,564 LOC main class → specialized helpers)
/// - Centralizes token lifecycle management
/// - Enables secure credential handling with encryption
/// - Facilitates testing of token operations in isolation
/// - Follows Single Responsibility Principle
/// 
/// Location: TeslaLogger/DBHelper.ConfigHelpers.cs
/// Related: StringCipher (encryption), Car.cs (credential properties)
/// </summary>
/// 
using System;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using MySql.Data.MySqlClient;
using Exceptionless;

namespace TeslaLogger
{
    /// <summary>
    /// Configuration and token management helpers for vehicle credentials.
    /// Partial class extension to DBHelper for managing API tokens and integration settings.
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Retrieves Tesla API tokens for the vehicle.
        /// Decrypts stored credentials from the database for API authentication.
        /// </summary>
        /// <param name="tesla_token">Output parameter: Decrypted access token for API calls</param>
        /// <returns>
        /// Decrypted refresh token for obtaining new access tokens
        /// Empty string if no token found
        /// </returns>
        /// <remarks>
        /// Security: Both tokens are stored encrypted in database, decrypted upon retrieval
        /// Tokens are sensitive and should not be logged or exposed
        /// Called during: API session initialization, token refresh flows
        /// </remarks>
        internal string? Helper_GetTeslaTokens(out string? tesla_token)
        {
            tesla_token = "";

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    refresh_token,
    tesla_token
FROM
    cars
WHERE
    id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            string? refresh_token_encrypted = dr.GetStringOrNull(0) ?? "";
                            string? refresh_token = StringCipher.Decrypt(refresh_token_encrypted);

                            string? tesla_token_encrypted = dr.GetStringOrNull(1) ?? "";
                            tesla_token = StringCipher.Decrypt(tesla_token_encrypted);

                            return refresh_token;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "Helper_GetTeslaTokens");
            }

            return string.Empty;
        }

        /// <summary>
        /// Retrieves refresh token for a given access token.
        /// Static helper for looking up a vehicle by its Tesla API access token.
        /// </summary>
        /// <param name="access_token">Tesla API access token to search for</param>
        /// <returns>
        /// Corresponding refresh token (encrypted in DB, returned as-is)
        /// Empty string if token not found
        /// </returns>
        /// <remarks>
        /// Performance: Single database query with token index lookup
        /// Used for: Token reverse lookup during API callbacks
        /// </remarks>
        internal static string? Helper_GetRefreshTokenFromAccessToken(string? access_token)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT
    refresh_token
FROM
    cars
WHERE
    tesla_token = @tesla_token", con))
                    {
                        cmd.Parameters.AddWithValue("@tesla_token", access_token);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            string? refresh_token = dr.GetStringOrNull(0) ?? "";
                            return refresh_token;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "Helper_GetRefreshTokenFromAccessToken");
            }

            return string.Empty;
        }

        /// <summary>
        /// Updates Tesla API access token in the database.
        /// Encrypts and stores the new access token along with its expiration time.
        /// </summary>
        /// <param name="tesla_token">New access token from Tesla API</param>
        /// <param name="token_expires">Expiration timestamp for the token</param>
        /// <remarks>
        /// Security: Token is encrypted before storage
        /// Logging: Token is truncated in logs to avoid exposure
        /// Called by: Token refresh flows during API authentication
        /// Usage: Helper_UpdateTeslaToken("eyJhbGci...", DateTime.UtcNow.AddHours(1))
        /// </remarks>
        internal void Helper_UpdateTeslaToken(string? tesla_token, DateTime token_expires)
        {
            try
            {
                if (string.IsNullOrEmpty(tesla_token) || tesla_token.Length < 20)
                {
                    car.CreateExeptionlessLog("Tesla Token", "Tesla Token EMPTY!!!", Exceptionless.Logging.LogLevel.Warn).Submit();
                    return;
                }

                car.Log("Helper_UpdateTeslaToken");

                string encrypted_token = StringCipher.Encrypt(tesla_token);

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE cars 
SET tesla_token = @tesla_token, 
    tesla_token_expire = @tesla_token_expire 
WHERE id = @id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        cmd.Parameters.AddWithValue("@tesla_token", encrypted_token);
                        cmd.Parameters.AddWithValue("@tesla_token_expire", token_expires);

                        int done = SQLTracer.TraceNQ(cmd, out _);

                        car.Log($"Helper_UpdateTeslaToken OK: {done} - {tesla_token.Substring(0, 20)}xxxxxx");
                        car.CreateExeptionlessLog("Tesla Token", "Update Tesla Token OK", Exceptionless.Logging.LogLevel.Info).Submit();
                        car.ExternalLog("Helper_UpdateTeslaToken");
                    }
                }
            }
            catch (System.Threading.ThreadAbortException)
            {
                System.Diagnostics.Debug.WriteLine("Thread Stop!");
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                car.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Updates Tesla API refresh token in the database.
        /// Encrypts and stores the new refresh token for future token refresh operations.
        /// </summary>
        /// <param name="refresh_token">New refresh token from Tesla API</param>
        /// <remarks>
        /// Security: Token is encrypted before storage
        /// Validation: Minimum 20 characters required to prevent storage of invalid tokens
        /// Logging: Token is truncated in logs to avoid exposure
        /// Called by: Token refresh flows, login operations
        /// Importance: Refresh tokens are long-lived (can be reused for weeks)
        /// </remarks>
        internal void Helper_UpdateRefreshToken(string? refresh_token)
        {
            try
            {
                if (refresh_token is null || refresh_token.Length < 20)
                {
                    car.Log("SKIP Helper_UpdateRefreshToken - Token too short!");
                    return;
                }

                string encrypted_token = StringCipher.Encrypt(refresh_token);

                car.Log("Helper_UpdateRefreshToken");
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE cars 
SET refresh_token = @refresh_token 
WHERE id = @id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        cmd.Parameters.AddWithValue("@refresh_token", encrypted_token);

                        int done = SQLTracer.TraceNQ(cmd, out _);
                        car.Log($"Helper_UpdateRefreshToken OK: {done} - {refresh_token.Substring(0, 20)}xxxxxxxx");
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                car.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Configures ABRP (A Better Route Planner) integration for the vehicle.
        /// Sets up API token and mode for route planning optimization.
        /// </summary>
        /// <param name="abrp_token">ABRP service API token</param>
        /// <param name="abrp_mode">ABRP tracking mode: 1=enabled, -1=disabled, etc.</param>
        /// <returns>
        /// true if configuration successful
        /// false if ABRP mode is disabled (-1) or error occurred
        /// </returns>
        /// <remarks>
        /// Integration: A Better Route Planner charging network optimization
        /// Purpose: Enables advanced trip planning with regenerative energy optimization
        /// Optional: ABRP integration is optional; vehicle can function without it
        /// Process:
        /// 1. Validates configuration with test data submission
        /// 2. Updates local Car properties
        /// 3. Stores in database for persistence
        /// </remarks>
        internal bool Helper_SetABRP(string? abrp_token, int abrp_mode)
        {
            car.ABRPToken = abrp_token;
            car.ABRPMode = abrp_mode;

            try
            {
                // Test configuration with API submission
                car.webhelper.SendDataToAbetterrouteplannerAsync(
                    Tools.ToUnixTime(DateTime.UtcNow) * 1000,
                    car.CurrentJSON?.current_battery_level ?? 0,
                    0,
                    true,
                    car.CurrentJSON?.current_power ?? 0,
                    car.CurrentJSON?.Latitude ?? 0,
                    car.CurrentJSON?.Longitude ?? 0
                ).Wait();

                if (car.ABRPMode == -1)
                    return false;

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE cars 
SET ABRP_token = @token, 
    ABRP_mode = @mode 
WHERE id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@token", abrp_token ?? "");
                        cmd.Parameters.AddWithValue("@mode", abrp_mode);

                        _ = SQLTracer.TraceNQ(cmd, out _);

                        car.Log($"Helper_SetABRP successful: mode={abrp_mode}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                car.Log(ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Retrieves ABRP (A Better Route Planner) integration settings for the vehicle.
        /// </summary>
        /// <param name="ABRP_token">Output: ABRP API token</param>
        /// <param name="ABRP_mode">Output: ABRP tracking mode</param>
        /// <returns>
        /// true if settings found and retrieved
        /// false if no ABRP configuration exists
        /// </returns>
        /// <remarks>
        /// Called during: Application startup, settings refresh
        /// Optional: Returns false if ABRP not configured
        /// </remarks>
        internal bool Helper_GetABRP(out string? ABRP_token, out int ABRP_mode)
        {
            ABRP_token = "";
            ABRP_mode = 0;

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT ABRP_token, ABRP_mode 
FROM cars 
WHERE id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);

                        if (dr.Read())
                        {
                            ABRP_token = dr[0].ToString();
                            ABRP_mode = Convert.ToInt32(dr[1], Tools.ciEnUS);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                car.Log(ex.ToString());
            }

            return false;
        }

        /// <summary>
        /// Configures SuC Bingo (Supercharger Bingo) integration for the vehicle.
        /// Sets up credentials for track supercharger visit achievements.
        /// </summary>
        /// <param name="sucBingo_user">SuC Bingo username/user ID</param>
        /// <param name="sucBingo_apiKey">SuC Bingo API key for authentication</param>
        /// <returns>
        /// true if configuration successful
        /// false if error occurred
        /// </returns>
        /// <remarks>
        /// Integration: Supercharger Bingo achievement tracking service
        /// Purpose: Gamification of supercharger network usage
        /// Optional: SuC Bingo is optional; vehicle can function without it
        /// Credentials stored unencrypted in this implementation
        /// (Note: Consider encrypting credentials in future updates)
        /// </remarks>
        internal bool Helper_SetSuCBingo(string? sucBingo_user, string? sucBingo_apiKey)
        {
            car.SuCBingoUser = sucBingo_user;
            car.SuCBingoApiKey = sucBingo_apiKey;

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE cars 
SET SuCBingo_user = @user, 
    SuCBingo_apiKey = @apikey 
WHERE id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        cmd.Parameters.AddWithValue("@user", sucBingo_user ?? "");
                        cmd.Parameters.AddWithValue("@apikey", sucBingo_apiKey ?? "");

                        _ = SQLTracer.TraceNQ(cmd, out _);

                        car.Log($"Helper_SetSuCBingo successful: user={sucBingo_user}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                car.Log(ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Retrieves SuC Bingo (Supercharger Bingo) integration credentials for the vehicle.
        /// </summary>
        /// <param name="sucBingo_user">Output: SuC Bingo username/user ID</param>
        /// <param name="sucBingo_apiKey">Output: SuC Bingo API key</param>
        /// <returns>
        /// true if credentials found and retrieved
        /// false if no SuC Bingo configuration exists
        /// </returns>
        /// <remarks>
        /// Called during: Application startup, settings refresh
        /// Optional: Returns false if SuC Bingo not configured
        /// </remarks>
        internal bool Helper_GetSuCBingo(out string? sucBingo_user, out string? sucBingo_apiKey)
        {
            sucBingo_user = "";
            sucBingo_apiKey = "";

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
SELECT SuCBingo_user, SuCBingo_apiKey 
FROM cars 
WHERE id = @CarID", con))
                    {
                        cmd.Parameters.AddWithValue("@CarID", car.CarInDB);
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);

                        if (dr.Read())
                        {
                            sucBingo_user = dr[0].ToString();
                            sucBingo_apiKey = dr[1].ToString();
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                car.Log(ex.ToString());
            }

            return false;
        }

        /// <summary>
        /// Persists vehicle configuration and metadata to the database.
        /// Updates vehicle display name, model details, efficiency metrics, and technical specs.
        /// </summary>
        /// <remarks>
        /// Called during: Application initialization, configuration changes
        /// Updates: display_name, car_type, battery, wheel_type, efficiency metrics, VIN, etc.
        /// Purpose: Store vehicle metadata for reporting and state management
        /// Note: Does not update authentication tokens (see Helper_UpdateTeslaToken, Helper_UpdateRefreshToken)
        /// </remarks>
        internal void Helper_WriteCarSettings()
        {
            if (car.CarInDB == 0)  // Skip for test instances
                return;

            try
            {
                car.Log("Helper_WriteCarSettings");

                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(@"
UPDATE cars 
SET display_name = @display_name,
    Raven = @Raven,
    Wh_TR = @Wh_TR,
    DB_Wh_TR = @DB_Wh_TR,
    DB_Wh_TR_count = @DB_Wh_TR_count,
    car_type = @car_type,
    car_special_type = @car_special_type,
    car_trim_badging = @trim_badging,
    model_name = @model_name,
    Battery = @Battery,
    tasker_hash = @tasker_hash,
    vin = @vin,
    wheel_type = @wheel_type 
WHERE id = @id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        cmd.Parameters.AddWithValue("@Raven", car.Raven);
                        cmd.Parameters.AddWithValue("@Wh_TR", car.WhTR);
                        cmd.Parameters.AddWithValue("@DB_Wh_TR", car.DBWhTR);
                        cmd.Parameters.AddWithValue("@DB_Wh_TR_count", car.DBWhTRcount);
                        cmd.Parameters.AddWithValue("@car_type", car.CarType ?? "");
                        cmd.Parameters.AddWithValue("@car_special_type", car.CarSpecialType ?? "");
                        cmd.Parameters.AddWithValue("@trim_badging", car.TrimBadging ?? "");
                        cmd.Parameters.AddWithValue("@model_name", car.ModelName ?? "");
                        cmd.Parameters.AddWithValue("@Battery", car.Battery ?? "");
                        cmd.Parameters.AddWithValue("@display_name", car.DisplayName ?? "");
                        cmd.Parameters.AddWithValue("@tasker_hash", car.TaskerHash ?? "");
                        cmd.Parameters.AddWithValue("@vin", car.Vin ?? "");
                        cmd.Parameters.AddWithValue("@wheel_type", car.wheel_type ?? "");

                        int done = SQLTracer.TraceNQ(cmd, out _);
                        car.Log($"Helper_WriteCarSettings OK: {done} rows updated");
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                car.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Updates an arbitrary car configuration column in the database.
        /// Generic helper for setting car-related metadata that doesn't fit other specialized methods.
        /// </summary>
        /// <param name="column">Column name to update</param>
        /// <param name="value">Value to set</param>
        /// <remarks>
        /// Purpose: Generic column update for flexibility
        /// Security: No parameterization of column name (internal use only)
        /// Called by: Configuration UI, administrative operations
        /// Examples:
        /// - Helper_UpdateCarColumn("display_name", "My Model 3")
        /// - Helper_UpdateCarColumn("Raven", "1")
        /// </remarks>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
            Justification = "Column name is internal-only, not user-supplied")]
        internal void Helper_UpdateCarColumn(string? column, string? value)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
UPDATE cars 
SET {column} = @value 
WHERE id = @id", con))
                    {
                        cmd.Parameters.AddWithValue("@id", car.CarInDB);
                        cmd.Parameters.AddWithValue("@value", value ?? "");

                        _ = SQLTracer.TraceNQ(cmd, out _);
                        car.Log($"Helper_UpdateCarColumn({column}) OK");
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                car.Log(ex.ToString());
            }
        }

        /// <summary>
        /// Gets all vehicles sorted by API token expiration age.
        /// Useful for identifying which vehicles need token refresh soon.
        /// </summary>
        /// <param name="descending">
        /// true: Most recently expired tokens first
        /// false (default): About to expire tokens first
        /// </param>
        /// <returns>
        /// DataTable with vehicle records sorted by tesla_token_expire
        /// </returns>
        /// <remarks>
        /// Called by: Token refresh scheduler, administrative queries
        /// Performance: Single database query with sorting
        /// Use case: Identify stale tokens needing refresh
        /// </remarks>
        internal static DataTable Helper_GetCarsByTokenExpiration(bool descending = false)
        {
            return GetCars($"tesla_token_expire {(descending ? "DESC" : "ASC")}");
        }
    }
}
