using Exceptionless;
using MySql.Data.MySqlClient;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace TeslaLogger
{
    /// <summary>
    /// Partial class containing SQL query execution infrastructure.
    /// Responsible for executing SQL commands (non-queries and scalar queries)
    /// against the MySQL database with proper error handling and tracing.
    /// </summary>
    public partial class DBHelper
    {
        /// <summary>
        /// Synchronously executes a non-query SQL command (INSERT, UPDATE, DELETE, etc.).
        /// </summary>
        /// <param name="sql">The SQL command to execute. Should not be null.</param>
        /// <param name="timeout">Command timeout in seconds. Default is 30 seconds.</param>
        /// <returns>The number of rows affected by the command.</returns>
        /// <exception cref="Exception">Re-throws any database or execution exceptions after logging.</exception>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static int ExecuteSQLQuery(string? sql, int timeout = 30)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(sql, con))
                    {
                        if (timeout != 30)
                        {
                            cmd.CommandTimeout = timeout;
                        }
                        return SQLTracer.TraceNQ(cmd, out _);
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in: {sql}");
                Logfile.ExceptionWriter(ex, sql);
                throw;
            }
        }

        /// <summary>
        /// Asynchronously executes a non-query SQL command (INSERT, UPDATE, DELETE, etc.).
        /// Supports cancellation tokens for long-running operations on ARM32 platforms.
        /// </summary>
        /// <param name="sql">The SQL command to execute. Should not be null.</param>
        /// <param name="timeout">Command timeout in seconds. Default is 30 seconds.</param>
        /// <param name="cancellationToken">Cancellation token for cooperative cancellation.</param>
        /// <returns>A task representing the async operation, returning the number of rows affected.</returns>
        /// <exception cref="Exception">Re-throws any database or execution exceptions after logging.</exception>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static async Task<int> ExecuteSQLQueryAsync(string? sql, int timeout = 30, CancellationToken cancellationToken = default)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    await con.OpenAsync(cancellationToken).ConfigureAwait(false);
                    using (MySqlCommand cmd = new MySqlCommand(sql, con))
                    {
                        if (timeout != 30)
                        {
                            cmd.CommandTimeout = timeout;
                        }
                        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in: {sql}");
                Logfile.ExceptionWriter(ex, sql);
                throw;
            }
        }

        /// <summary>
        /// Synchronously executes a scalar SQL query (SELECT that returns a single value).
        /// Example: SELECT COUNT(*) FROM table, SELECT MAX(id) FROM table
        /// </summary>
        /// <param name="sql">The SQL query to execute. Should not be null.</param>
        /// <param name="timeout">Command timeout in seconds. Default is 30 seconds.</param>
        /// <returns>The scalar value returned by the query, or null if no result.</returns>
        /// <exception cref="Exception">Re-throws any database or execution exceptions after logging.</exception>
        [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public static object? ExecuteSQLScalar(string? sql, int timeout = 30)
        {
            try
            {
                using (MySqlConnection con = new MySqlConnection(DBConnectionstring))
                {
                    con.Open();
                    using (MySqlCommand cmd = new MySqlCommand(sql!, con))
                    {
                        if (timeout != 30)
                        {
                            cmd.CommandTimeout = timeout;
                        }
                        return SQLTracer.TraceSc(cmd);
                    }
                }
            }
            catch (Exception ex)
            {
                ex.ToExceptionless().FirstCarUserID().Submit();
                Logfile.Log($"Error in: {sql}");
                Logfile.ExceptionWriter(ex, sql);
                throw;
            }
        }
    }
}
