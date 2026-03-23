using System.Threading.Tasks;

namespace TeslaLogger.Services
{
    /// <summary>
    /// Manages database schema validation, migrations, and consistency checks.
    /// Responsible for schema versioning, table creation, view management, and charset validation.
    /// </summary>
    public interface IDbSchemaMigrator
    {
        /// <summary>
        /// Validates and updates all database schemas asynchronously.
        /// Checks all essential tables, creates missing tables, and maintains schema integrity.
        /// </summary>
        /// <returns>A task representing the asynchronous validation operation.</returns>
        Task ValidateAllSchemasAsync();

        /// <summary>
        /// Validates database charset compliance (utf8mb4) and performs upgrades if necessary.
        /// </summary>
        /// <returns>A task representing the asynchronous charset validation operation.</returns>
        Task ValidateDatabaseCharsetAsync();

        /// <summary>
        /// Validates and creates all required database views.
        /// Updates views if they differ from expected schema.
        /// </summary>
        /// <returns>A task representing the asynchronous view validation operation.</returns>
        Task ValidateViewsAsync();

        /// <summary>
        /// Adds or updates database indexes to optimize query performance.
        /// Removes obsolete indexes that are no longer needed.
        /// </summary>
        /// <returns>A task representing the asynchronous index validation operation.</returns>
        Task ValidateIndexesAsync();

        /// <summary>
        /// Retrieves the size in MB of the largest table in the database.
        /// Used for calculating backup space requirements.
        /// </summary>
        /// <returns>Size in megabytes of the largest table.</returns>
        Task<long> GetLargestTableMBAsync();

        /// <summary>
        /// Checks if a specific schema version exists and matches the expected value.
        /// Used to track schema migration progress via key-value store.
        /// </summary>
        /// <param name="versionKey">The schema version key to check.</param>
        /// <param name="expectedVersion">The expected version number.</param>
        /// <returns>True if the schema version matches, false otherwise.</returns>
        bool SchemaVersionMatches(string versionKey, int expectedVersion);

        /// <summary>
        /// Updates a stored schema version to indicate a completed migration.
        /// </summary>
        /// <param name="versionKey">The schema version key to update.</param>
        /// <param name="newVersion">The new version number to store.</param>
        void UpdateSchemaVersion(string versionKey, int newVersion);

        /// <summary>
        /// Ensures sufficient disk space is available before performing ALTER TABLE operations.
        /// Cleans up backup folder if necessary to free space.
        /// </summary>
        /// <returns>A task representing the asynchronous disk space assertion.</returns>
        Task AssertAlterDatabaseAsync();
    }
}
