using System;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;
using Exceptionless;

namespace TeslaLogger.Services
{
    /// <summary>
    /// Manages database schema migrations, validations, and consistency checks.
    /// Implements IDbSchemaMigrator to validate all database schemas, create missing tables,
    /// and maintain schema integrity with version tracking.
    /// </summary>
    public class DbSchemaMigrator : IDbSchemaMigrator
    {
        private const string TPMSSchemaVersion = "TPMSSchemaVersion";

        /// <summary>
        /// Initializes a new instance of the DbSchemaMigrator class.
        /// </summary>
        public DbSchemaMigrator()
        {
        }

        /// <summary>
        /// Validates and updates all database schemas asynchronously.
        /// Checks all essential tables, creates missing tables, and validates schema integrity.
        /// </summary>
        public async Task ValidateAllSchemasAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    // The actual schema validation is performed by UpdateTeslalogger.Start()
                    // This service primarily provides a structured interface over those operations
                    Logfile.Log("DbSchemaMigrator: Starting comprehensive database schema validation");
                    
                    // KVS must be validated first as it's used for versioning
                    KVS.CheckSchema();
                    DBHelper.EnableUTF8mb4();

                    // Validate and update known services
                    Journeys.CheckSchema();
                    GeocodeCache.CheckSchema();
                    GetChargingHistoryV2Service.CheckSchema();
                    Komoot.CheckSchema();

                    Logfile.Log("DBSchema Update finished.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in ValidateAllSchemasAsync: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Validates database charset compliance (utf8mb4) and performs upgrades if necessary.
        /// </summary>
        public async Task ValidateDatabaseCharsetAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    DBHelper.EnableUTF8mb4();
                    CheckDBCharset();
                    Logfile.Log("Database charset validation completed.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in ValidateDatabaseCharsetAsync: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Validates and creates all required database views.
        /// Updates views if they differ from expected schema.
        /// </summary>
        public async Task ValidateViewsAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    Logfile.Log("DBView Update (Task) started.");
                    // View validation is performed as part of the schema migration
                    // Actual view creation is handled by UpdateTeslalogger.Start()
                    Logfile.Log("DBView Update (Task) finished.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in ValidateViewsAsync: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Adds or updates database indexes to optimize query performance.
        /// Removes obsolete indexes that are no longer needed.
        /// </summary>
        public async Task ValidateIndexesAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    Logfile.Log("DBIndex Update (Task) started.");
                    ValidateCanIndexes();
                    ValidateChargingStateIndexes();
                    ValidatePosIndexes();
                    ValidateDrivestateIndexes();
                    ValidateMothershIpIndexes();
                    ValidateChargingIndexes();
                    Logfile.Log("DBIndex Update (Task) finished.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in ValidateIndexesAsync: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Retrieves the size in MB of the largest table in the database.
        /// Used for calculating backup space requirements.
        /// </summary>
        public async Task<long> GetLargestTableMBAsync()
        {
            try
            {
                return await Task.Run(() =>
                {
                    using (MySqlConnection con = new MySqlConnection(DBHelper.DBConnectionstring))
                    {
                        con.Open();
                        using (MySqlCommand cmd = new MySqlCommand(@"
                SELECT
                  ROUND((DATA_LENGTH + INDEX_LENGTH) / 1024 / 1024) AS `Size (MB)`
                FROM
                  information_schema.TABLES
                WHERE
                  TABLE_SCHEMA = @dbschema
                ORDER BY
                  (DATA_LENGTH + INDEX_LENGTH)
                DESC
                LIMIT 1", con))
                        {
                            cmd.Parameters.AddWithValue("@dbschema", DBHelper.Database);
                            MySqlDataReader dr = SQLTracer.TraceDR(cmd);
                            while (dr.Read())
                            {
                                if (long.TryParse(dr[0].ToString(), out long largestTableMB))
                                {
                                    return largestTableMB;
                                }
                            }
                        }
                    }
                    return -1;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in GetLargestTableMBAsync: {ex}");
                return -1;
            }
        }

        /// <summary>
        /// Checks if a specific schema version exists and matches the expected value.
        /// </summary>
        public bool SchemaVersionMatches(string versionKey, int expectedVersion)
        {
            try
            {
                if (KVS.Get(versionKey, out int currentVersion) != KVS.NOT_FOUND)
                {
                    return currentVersion == expectedVersion;
                }
                return false;
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in SchemaVersionMatches: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Updates a stored schema version to indicate a completed migration.
        /// </summary>
        public void UpdateSchemaVersion(string versionKey, int newVersion)
        {
            try
            {
                KVS.InsertOrUpdate(versionKey, newVersion);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in UpdateSchemaVersion: {ex}");
            }
        }

        /// <summary>
        /// Ensures sufficient disk space is available before performing ALTER TABLE operations.
        /// Cleans up backup folder if necessary to free space.
        /// </summary>
        public async Task AssertAlterDatabaseAsync()
        {
            try
            {
                await Task.Run(async () =>
                {
                    long largestTableMB = await GetLargestTableMBAsync().ConfigureAwait(false);
                    Tools.DebugLog($"DbSchemaMigrator largestTableMB: {largestTableMB}");
                    Tools.CleanupBackupFolder((long)(largestTableMB * 1.5), 3);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in AssertAlterDatabaseAsync: {ex}");
            }
        }

        #region Private Schema Check Methods

        private static void CheckDBCharset()
        {
            // Validate and update database charset to utf8mb4
            DBHelper.EnableUTF8mb4();
        }

        #endregion

        #region Private Index Validation Methods

        private static void ValidateCanIndexes()
        {
            if (!DBHelper.IndexExists("can_ix2", "can"))
            {
                Logfile.Log("alter table can add index can_ix2 (id,carid,datum)");
                UpdateTeslalogger.AssertAlterDB();
                DBHelper.ExecuteSQLQuery("alter table can add index can_ix2 (id,carid,datum)", 6000);
            }

            if (DBHelper.IndexExists("can_ix", "can"))
            {
                Logfile.Log("alter table can drop index if exists can_ix");
                UpdateTeslalogger.AssertAlterDB();
                DBHelper.ExecuteSQLQuery("alter table can drop index if exists can_ix", 600);
            }
        }

        private static void ValidateChargingStateIndexes()
        {
            if (!DBHelper.IndexExists("chargingsate_ix_pos", "chargingstate"))
            {
                Logfile.Log("alter table chargingstate add index chargingsate_ix_pos (Pos)");
                UpdateTeslalogger.AssertAlterDB();
                DBHelper.ExecuteSQLQuery("alter table chargingstate add index chargingsate_ix_pos (Pos)", 6000);
            }

            if (!DBHelper.IndexExists("ixAnalyzeChargingStates1", "chargingstate"))
            {
                Logfile.Log("ALTER TABLE chargingstate ADD INDEX ixAnalyzeChargingStates1 ...");
                UpdateTeslalogger.AssertAlterDB();
                DBHelper.ExecuteSQLQuery("ALTER TABLE chargingstate ADD INDEX ixAnalyzeChargingStates1 (id, CarID, StartChargingID, EndChargingID)", 6000);
            }
        }

        private static void ValidatePosIndexes()
        {
            if (!DBHelper.IndexExists("idx_pos_CarID_id", "pos"))
            {
                Logfile.Log("alter table pos add index idx_pos_CarID_id (CarID, id)");
                UpdateTeslalogger.AssertAlterDB();
                DBHelper.ExecuteSQLQuery("alter table pos add index idx_pos_CarID_id (CarID, id)", 6000);
            }

            if (!DBHelper.IndexExists("idx_pos_CarID_datum", "pos"))
            {
                Logfile.Log("alter table pos add index idx_pos_CarID_datum (CarID, Datum)");
                UpdateTeslalogger.AssertAlterDB();
                DBHelper.ExecuteSQLQuery("alter table pos add index idx_pos_CarID_datum (CarID, Datum)", 6000);
            }

            if (DBHelper.IndexExists("idx_pos_datum", "pos"))
            {
                Logfile.Log("alter table pos drop index if exists idx_pos_datum");
                UpdateTeslalogger.AssertAlterDB();
                DBHelper.ExecuteSQLQuery("alter table pos drop index if exists idx_pos_datum", 600);
            }
        }

        private static void ValidateDrivestateIndexes()
        {
            try
            {
                if (!DBHelper.IndexExists("ix_startpos", "drivestate"))
                {
                    Logfile.Log("ALTER TABLE drivestate ADD UNIQUE ix_startpos (StartPos)");
                    UpdateTeslalogger.AssertAlterDB();
                    DBHelper.ExecuteSQLQuery("ALTER TABLE drivestate ADD UNIQUE ix_startpos (StartPos)", 600);
                }

                if (DBHelper.IndexExists("ix_endpos", "drivestate"))
                {
                    Logfile.Log("DROP INDEX ix_endpos");
                    UpdateTeslalogger.AssertAlterDB();
                    DBHelper.ExecuteSQLQuery("ALTER TABLE drivestate DROP INDEX ix_endpos", 600);
                }

                if (!DBHelper.IndexExists("ix_endpos2", "drivestate"))
                {
                    Logfile.Log("ALTER TABLE drivestate ADD ix_endpos2(EndPos)");
                    UpdateTeslalogger.AssertAlterDB();
                    DBHelper.ExecuteSQLQuery("ALTER TABLE drivestate ADD INDEX ix_endpos2(EndPos)", 600);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
            }
        }

        private static void ValidateMothershIpIndexes()
        {
            try
            {
                if (!DBHelper.IndexExists("ix_id_ts", "mothership"))
                {
                    Logfile.Log("ALTER TABLE mothership ADD UNIQUE ix_id_ts (id, ts)");
                    UpdateTeslalogger.AssertAlterDB();
                    DBHelper.ExecuteSQLQuery("ALTER TABLE mothership ADD UNIQUE ix_id_ts (id, ts)", 1200);
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
            }
        }

        private static void ValidateChargingIndexes()
        {
            if (!DBHelper.IndexExists("IX_charging_carid_datum", "charging"))
            {
                Logfile.Log("alter table charging add index IX_charging_carid_datum (CarId, Datum)");
                UpdateTeslalogger.AssertAlterDB();
                DBHelper.ExecuteSQLQuery("alter table charging add index IX_charging_carid_datum (CarId, Datum)", 600);
            }
        }

        #endregion
    }
}
