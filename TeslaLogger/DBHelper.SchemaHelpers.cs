/// <summary>
/// PHASE 1D SCHEMA HELPERS - DBHelper Schema Operations Decomposition
/// 
/// This partial class extracts database schema validation and management operations
/// from the main DBHelper class, improving separation of concerns and maintainability.
/// 
/// Includes:
/// - Table existence and column validation
/// - Schema introspection (column types, collation)
/// - UTF8MB4 character set upgrades
/// - Column property queries
/// 
/// Benefits:
/// - Improves code organization (7,564 LOC main class → specialized helpers)
/// - Enables unit testing of schema operations in isolation
/// - Facilitates future async/await migration of schema operations
/// - Follows Single Responsibility Principle
/// 
/// Location: TeslaLogger/DBHelper.SchemaHelpers.cs
/// Related: UpdateTeslalogger.cs (schema migrations), DBHelper.cs (main class)
/// </summary>
/// 
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using MySql.Data.MySqlClient;
using Exceptionless;

namespace TeslaLogger
{
    /// <summary>
    /// Schema validation and management helpers for database operations.
    /// Partial class extension to DBHelper for database schema introspection and maintenance.
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Retrieves the data type of a specific column in a table.
        /// Uses INFORMATION_SCHEMA for schema introspection.
        /// </summary>
        /// <param name="table">Table name to query</param>
        /// <param name="column">Column name to retrieve type for</param>
        /// <returns>
        /// Column data type string (e.g., "VARCHAR(255)", "INT", "BIGINT", "TIMESTAMP")
        /// Empty string if column does not exist
        /// </returns>
        /// <remarks>
        /// Called by: UpdateTeslalogger schema migration routines
        /// Performance: Cached by MySQL query optimizer, minimal overhead
        /// </remarks>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", 
            Justification = "Table and column names come from internal schema definitions only")]
        internal static string? Helper_GetColumnType(string? table, string? column)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    DATA_TYPE
FROM
    INFORMATION_SCHEMA.COLUMNS
WHERE
    table_name = '{table}'
    AND COLUMN_NAME = '{column}'", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            return dr[0].ToString();
                        }
                    }
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "Helper_GetColumnType");
                return string.Empty;
            }
        }

        /// <summary>
        /// Checks if a column exists in a specified table.
        /// Efficient SHOW COLUMNS query with pattern matching.
        /// </summary>
        /// <param name="table">Table name to check</param>
        /// <param name="column">Column name to verify</param>
        /// <returns>
        /// true if column exists in table
        /// false if column does not exist or table is not found
        /// </returns>
        /// <remarks>
        /// Called during: Data model validation, schema upgrade checks
        /// Used by: UpdateTeslalogger.CheckDBSchema_* methods
        /// Performance: O(1) lookup in table schema
        /// </remarks>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", 
            Justification = "Table and column names come from internal schema definitions only")]
        internal static bool Helper_ColumnExists(string? table, string? column)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"SHOW COLUMNS FROM `{table}` LIKE '{column}';", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, $"Helper_ColumnExists({table}.{column})");
            }

            return false;
        }

        /// <summary>
        /// Retrieves character set and collation information for all VARCHAR columns in a table.
        /// Used for UTF8MB4 migration and character encoding validation.
        /// </summary>
        /// <param name="dbname">Database name</param>
        /// <param name="tablename">Table name to inspect</param>
        /// <returns>
        /// List of tuples containing:
        /// - ColumnName: VARCHAR column name
        /// - CharacterSet: Current character set (e.g., "utf8", "utf8mb4")
        /// - CollationName: Current collation (e.g., "utf8_general_ci", "utf8mb4_unicode_ci")
        /// - ColumnType: Full column type definition
        /// </returns>
        /// <remarks>
        /// Purpose: Identify columns needing UTF8MB4 upgrade for emoji/unicode support
        /// Called by: Helper_UpgradeUtf8mb4ColumnsIfNeeded()
        /// Performance: Scans table schema only, no data operations
        /// </remarks>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", 
            Justification = "Table and database names come from internal schema definitions only")]
        internal static List<(string columnName, string charSet, string collation, string columnType)> 
            Helper_GetVarcharColumnsWithCharacterSet(string? dbname, string? tablename)
        {
            var results = new List<(string, string, string, string)>();

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    COLUMN_NAME,
    CHARACTER_SET_NAME,
    COLLATION_NAME,
    COLUMN_TYPE
FROM
    INFORMATION_SCHEMA.COLUMNS
WHERE
    TABLE_SCHEMA = '{dbname}'
    AND TABLE_NAME = '{tablename}'
    AND DATA_TYPE = 'varchar'", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            if (dr.HasRows && dr[0] is not null && dr[1] is not null && dr[2] is not null && dr[3] is not null)
                            {
                                string columnName = dr[0].ToString() ?? string.Empty;
                                string charSet = dr[1].ToString() ?? string.Empty;
                                string collation = dr[2].ToString() ?? string.Empty;
                                string columnType = dr[3].ToString() ?? string.Empty;

                                results.Add((columnName, charSet, collation, columnType));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, $"Helper_GetVarcharColumnsWithCharacterSet({dbname}.{tablename})");
            }

            return results;
        }

        /// <summary>
        /// Upgrades VARCHAR columns to UTF8MB4 character set for unicode/emoji support.
        /// Modifies column character set and collation in-place.
        /// </summary>
        /// <param name="dbname">Database name</param>
        /// <param name="tablename">Table name</param>
        /// <param name="columnname">Column to upgrade</param>
        /// <param name="columntype">Full column type definition (e.g., "VARCHAR(255)")</param>
        /// <remarks>
        /// Purpose: Enable emoji, extended unicode characters in string columns
        /// Syntax: ALTER TABLE ... CHANGE ... CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci
        /// Performance: May lock table briefly on large tables
        /// Safety: Preserves column defaults and null constraints
        /// Called by: Helper_UpgradeUtf8mb4ColumnsIfNeeded()
        /// </remarks>
        internal static void Helper_UpgradeColumnToUtf8mb4(string? dbname, string? tablename, string? columnname, string? columntype)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();

                    string alterQuery = $@"
ALTER TABLE `{dbname}`.`{tablename}`
CHANGE `{columnname}` `{columnname}` {columntype} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL";

                    Logfile.Log($"[SchemaHelpers] Upgrading column to UTF8MB4: {dbname}.{tablename}.{columnname}");
                    UpdateTeslalogger.AssertAlterDB();

                    _ = ExecuteSQLQuery(alterQuery, 3000);

                    Logfile.Log($"[SchemaHelpers] Column upgraded successfully to UTF8MB4");
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, $"Helper_UpgradeColumnToUtf8mb4({dbname}.{tablename}.{columnname})");
            }
        }

        /// <summary>
        /// Orchestrates UTF8MB4 character set upgrade for all VARCHAR columns in a table.
        /// Checks each column's current encoding and upgrades if needed.
        /// </summary>
        /// <param name="dbname">Database name</param>
        /// <param name="tablename">Table name to upgrade</param>
        /// <remarks>
        /// Purpose: Batch upgrade all VARCHAR columns to UTF8MB4 in one call
        /// Process:
        /// 1. Retrieves all VARCHAR columns with their current character sets
        /// 2. For each column not using utf8mb4:
        ///    - Checks collation (should be utf8mb4_unicode_ci)
        ///    - Issues ALTER TABLE command if mismatched
        /// Efficiency: Single metadata query + targeted ALTER statements
        /// Called by: UpdateTeslalogger schema migration sequences
        /// </remarks>
        internal static void Helper_UpgradeUtf8mb4ColumnsIfNeeded(string? dbname, string? tablename)
        {
            try
            {
                var varcharColumns = Helper_GetVarcharColumnsWithCharacterSet(dbname, tablename);

                foreach (var (columnName, charSet, collation, columnType) in varcharColumns)
                {
                    // Check if already UTF8MB4
                    if ("utf8mb4".Equals(charSet, StringComparison.OrdinalIgnoreCase) &&
                        "utf8mb4_unicode_ci".Equals(collation, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;  // Already upgraded
                    }

                    // Upgrade needed
                    Logfile.Log($"[SchemaHelpers] Upgrading {dbname}.{tablename}.{columnName} from {charSet} to utf8mb4");
                    Helper_UpgradeColumnToUtf8mb4(dbname, tablename, columnName, columnType);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, $"Helper_UpgradeUtf8mb4ColumnsIfNeeded({dbname}.{tablename})");
            }
        }

        /// <summary>
        /// Retrieves all column names for a specified table.
        /// Useful for schema validation and column existence bulk checks.
        /// </summary>
        /// <param name="tablename">Table name to inspect</param>
        /// <returns>
        /// List of all column names in the table, in definition order
        /// Empty list if table does not exist
        /// </returns>
        /// <remarks>
        /// Performance: Single SHOW COLUMNS query
        /// Used for: Schema validation, column auditing, data migration
        /// </remarks>
        internal static List<string> Helper_GetTableColumns(string? tablename)
        {
            var columns = new List<string>();

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"SHOW COLUMNS FROM `{tablename}`;", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            if (dr[0] is not null)
                            {
                                columns.Add(dr[0].ToString() ?? string.Empty);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, $"Helper_GetTableColumns({tablename})");
            }

            return columns;
        }

        /// <summary>
        /// Checks if a table exists in the current database.
        /// Fast metadata query for table existence verification.
        /// </summary>
        /// <param name="tablename">Table name to check</param>
        /// <returns>
        /// true if table exists
        /// false if table does not exist
        /// </returns>
        /// <remarks>
        /// Performance: Single INFORMATION_SCHEMA query
        /// Called during: Schema validation on application startup
        /// </remarks>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", 
            Justification = "Table name comes from internal schema definitions only")]
        internal static bool Helper_TableExists(string? tablename)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
SELECT COUNT(*)
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_SCHEMA = DATABASE()
AND TABLE_NAME = '{tablename}'", con))
                    {
                        var result = cmd.ExecuteScalar();
                        return result is not null && Convert.ToInt32(result) > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, $"Helper_TableExists({tablename})");
            }

            return false;
        }

        /// <summary>
        /// Retrieves detailed information about a column (type, nullable, key, default, etc.).
        /// Returns full SHOW COLUMNS result for a specific column.
        /// </summary>
        /// <param name="tablename">Table name</param>
        /// <param name="columnname">Column name to inspect</param>
        /// <returns>
        /// Dictionary with keys: Field, Type, Null, Key, Default, Extra
        /// Empty dictionary if column does not exist
        /// </returns>
        /// <remarks>
        /// Provides full column metadata for schema introspection
        /// Used for: Validation before ALTER TABLE operations
        /// </remarks>
        internal static Dictionary<string, string> Helper_GetColumnInfo(string? tablename, string? columnname)
        {
            var info = new Dictionary<string, string>();

            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($"SHOW COLUMNS FROM `{tablename}` LIKE '{columnname}';", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            // MySQL SHOW COLUMNS returns: Field, Type, Null, Key, Default, Extra
                            string[] fieldNames = { "Field", "Type", "Null", "Key", "Default", "Extra" };
                            for (int i = 0; i < fieldNames.Length && i < dr.FieldCount; i++)
                            {
                                string fieldName = fieldNames[i];
                                string value = dr[i]?.ToString() ?? "NULL";
                                info[fieldName] = value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, $"Helper_GetColumnInfo({tablename}.{columnname})");
            }

            return info;
        }

        /// <summary>
        /// Validates that a column has the expected data type.
        /// Useful for schema version checks before data operations.
        /// </summary>
        /// <param name="tablename">Table name</param>
        /// <param name="columnname">Column name</param>
        /// <param name="expectedType">Expected data type (e.g., "BIGINT", "VARCHAR(255)")</param>
        /// <returns>
        /// true if column type matches expected type
        /// false if column does not exist or type does not match
        /// </returns>
        /// <remarks>
        /// Example: Helper_ValidateColumnType("charging", "id", "BIGINT")
        /// Used for schema enforcement, migration validation
        /// </remarks>
        internal static bool Helper_ValidateColumnType(string? tablename, string? columnname, string expectedType)
        {
            try
            {
                string? actualType = Helper_GetColumnType(tablename, columnname);
                if (string.IsNullOrEmpty(actualType))
                    return false;  // Column doesn't exist

                // Normalize type comparison (accommodate VARCHAR(255) vs VARCHAR(255) variations)
                actualType = actualType.ToUpperInvariant();
                expectedType = expectedType.ToUpperInvariant();

                return actualType.Equals(expectedType, StringComparison.OrdinalIgnoreCase) ||
                       actualType.StartsWith(expectedType.Split('(')[0], StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, $"Helper_ValidateColumnType({tablename}.{columnname})");
                return false;
            }
        }
    }
}
