using MySql.Data.MySqlClient;
using Exceptionless;
using System;
using System.Diagnostics.CodeAnalysis;

namespace TeslaLogger
{
    /// <summary>
    /// SchemaUtilities: Database schema management and character set conversion (Batch 13)
    /// Handles UTF8mb4 charset migration and database/table/column schema validation
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Entry point for converting entire database to UTF8mb4 character set
        /// Ensures consistent Unicode support across database, tables, and columns
        /// </summary>
        /// <remarks>
        /// Process:
        /// 1. Validates and converts database charset to utf8mb4
        /// 2. Converts all tables to utf8mb4 charset and utf8mb4_unicode_ci collation
        /// 3. Converts all varchar columns to utf8mb4 charset
        /// 
        /// Reference: https://mathiasbynens.be/notes/mysql-utf8mb4
        /// This is necessary for proper emoji and multi-byte character support
        /// </remarks>
        public static void EnableUTF8mb4()
        {
            // https://mathiasbynens.be/notes/mysql-utf8mb4
            // check database
            Enable_utf8mb4_check_database("teslalogger");
            // check tables
            Enable_utf8mb4_check_tables("teslalogger");
        }

        /// <summary>
        /// Validates database charset and collation
        /// Compares with target utf8mb4/utf8mb4_unicode_ci and triggers conversion if needed
        /// </summary>
        /// <remarks>
        /// Security Note: Uses string interpolation for database name (generally safe for system names)
        /// Suppressed CA2100 because dbname is internal system identifier, not user input
        /// </remarks>
        /// <param name="dbname">Database name to validate (e.g., "teslalogger")</param>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        private static void Enable_utf8mb4_check_database(string? dbname)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    default_character_set_name,
    default_collation_name
FROM
    information_schema.schemata
WHERE SCHEMA_NAME  = '{dbname}'", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        if (dr.Read())
                        {
                            if (dr.HasRows && dr[0] is not null && dr[1] is not null)
                            {
                                if (!dr[0].ToString().Equals("utf8mb4", StringComparison.Ordinal)
                                    || !dr[1].ToString().Equals("utf8mb4_unicode_ci", StringComparison.Ordinal))
                                {
                                    Enable_utf8mb4_alter_database(dbname);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        /// <summary>
        /// Alters database to use UTF8mb4 charset and unicode collation
        /// Executes ALTER DATABASE command with 300 second timeout
        /// </summary>
        /// <remarks>
        /// SQL: ALTER DATABASE database_name CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci;
        /// Calls UpdateTeslalogger.AssertAlterDB() before execution for consistency check
        /// </remarks>
        /// <param name="dbname">Database name to alter</param>
        private static void Enable_utf8mb4_alter_database(string? dbname)
        {
            // ALTER DATABASE database_name CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    Logfile.Log($"ALTER DATABASE {dbname} CHARACTER SET = utf8mb4 COLLATE = utf8mb4_unicode_ci");
                    UpdateTeslalogger.AssertAlterDB();
                    _ = ExecuteSQLQuery($@"
ALTER DATABASE {dbname}
CHARACTER SET = utf8mb4
COLLATE = utf8mb4_unicode_ci", 300);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        /// <summary>
        /// Validates all tables in database for UTF8mb4 charset
        /// Iterates tables and checks collation; triggers conversion if needed
        /// Also checks individual column charsets via Enable_utf8mb4_check_columns
        /// </summary>
        /// <remarks>
        /// For each table:
        /// 1. Checks TABLE_COLLATION against utf8mb4_unicode_ci
        /// 2. If mismatch, calls Enable_utf8mb4_alter_table
        /// 3. Always calls Enable_utf8mb4_check_columns for varchar columns
        /// 
        /// Filters to BASE TABLEs only (excludes views)
        /// </remarks>
        /// <param name="dbname">Database name to validate tables for</param>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        private static void Enable_utf8mb4_check_tables(string? dbname)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand($@"
SELECT
    TABLE_NAME,
    TABLE_COLLATION
FROM
    information_schema.TABLES
WHERE
    TABLE_SCHEMA = '{dbname}'
    AND TABLE_TYPE = 'BASE TABLE'", con))
                    {
                        MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                        while (dr.Read())
                        {
                            if (dr.HasRows && dr[0] is not null && dr[1] is not null)
                            {
                                if (!dr[1].ToString().Equals("utf8mb4_unicode_ci", StringComparison.Ordinal))
                                {
                                    Enable_utf8mb4_alter_table(dbname, dr[0].ToString());
                                }
                                // check columns in table
                                Enable_utf8mb4_check_columns(dbname, dr[0].ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        /// <summary>
        /// Alters table to use UTF8mb4 charset and collation
        /// Uses CONVERT TO CHARACTER SET which automatically converts all columns
        /// Timeout is 3000 seconds (50 minutes) for large tables
        /// </summary>
        /// <remarks>
        /// SQL: ALTER TABLE table_name CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
        /// Calls UpdateTeslalogger.AssertAlterDB() before execution for consistency check
        /// CONVERT option is preferred over individual column changes for performance
        /// </remarks>
        /// <param name="dbname">Database name containing the table</param>
        /// <param name="tablename">Table name to alter</param>
        private static void Enable_utf8mb4_alter_table(string? dbname, string? tablename)
        {
            // ALTER TABLE table_name CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    Logfile.Log($"ALTER TABLE {dbname}.{tablename} CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
                    UpdateTeslalogger.AssertAlterDB();
                    _ = ExecuteSQLQuery($@"
ALTER TABLE {dbname}.{tablename}
CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", 3000);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        /// <summary>
        /// Validates varchar columns in table for UTF8mb4 charset
        /// Checks charset and collation for each varchar column
        /// Triggers column-level conversion if needed via Enable_utf8mb4_alter_column
        /// </summary>
        /// <remarks>
        /// For each varchar column:
        /// 1. Checks CHARACTER_SET_NAME against utf8mb4
        /// 2. Checks COLLATION_NAME against utf8mb4_unicode_ci
        /// 3. If mismatch, calls Enable_utf8mb4_alter_column with column type preservation
        /// 
        /// Only targets DATA_TYPE = 'varchar' columns
        /// </remarks>
        /// <param name="dbname">Database name</param>
        /// <param name="tablename">Table name to validate columns for</param>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        private static void Enable_utf8mb4_check_columns(string? dbname, string? tablename)
        {
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
                            if (dr.HasRows && dr[0] is not null && dr[1] is not null && dr[2] is not null)
                            {
                                if (!dr[1].ToString().Equals("utf8mb4", StringComparison.Ordinal)
                                    || !dr[2].ToString().Equals("utf8mb4_unicode_ci", StringComparison.Ordinal))
                                {
                                    Enable_utf8mb4_alter_column(dbname, tablename, dr[0].ToString(), dr[3].ToString());
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }

        /// <summary>
        /// Alters individual column to use UTF8mb4 charset and collation
        /// Preserves column type and NULL default from input parameter
        /// Timeout is 3000 seconds (50 minutes) for large tables
        /// </summary>
        /// <remarks>
        /// SQL: ALTER TABLE `table` CHANGE `column` `column` VARCHAR(...) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL;
        /// 
        /// Important: Column type (e.g., VARCHAR(255)) is passed as parameter to preserve size
        /// Calls UpdateTeslalogger.AssertAlterDB() before execution for consistency check
        /// </remarks>
        /// <param name="dbname">Database name</param>
        /// <param name="tablename">Table name containing column</param>
        /// <param name="columnname">Column name to alter</param>
        /// <param name="columntype">Column type (e.g., "varchar(255)") to preserve</param>
        private static void Enable_utf8mb4_alter_column(string? dbname, string? tablename, string? columnname, string? columntype)
        {
            // ALTER TABLE `shiftstate` CHANGE `state` `state` VARCHAR(5) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL;
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    Logfile.Log($"ALTER TABLE {dbname}.{tablename} CHANGE {columnname} {columnname} {columntype} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL");
                    UpdateTeslalogger.AssertAlterDB();
                    _ = ExecuteSQLQuery($@"
ALTER TABLE {dbname}.{tablename}
CHANGE {columnname} {columnname} {columntype} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NULL DEFAULT NULL", 3000);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.ExceptionWriter(ex, "");
            }
        }
    }
}
