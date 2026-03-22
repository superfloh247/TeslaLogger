using System;
using System.Data;

namespace TeslaLogger
{
    /// <summary>
    /// Safe database value access extension methods to prevent null reference exceptions
    /// and invalid cast exceptions.
    /// 
    /// Addresses critical issues where MySqlDataReader values are accessed without 
    /// checking for DBNull, leading to runtime crashes in production.
    /// </summary>
    /// <remarks>
    /// Usage:
    ///   string value = reader.GetStringOrNull(0) ?? "default";
    ///   int id = reader.GetInt32OrDefault(0, 0);
    ///   double amount = reader.GetDoubleOrDefault(1, 0.0);
    /// </remarks>
    public static class DataReaderExtensions
    {
        /// <summary>
        /// Safely gets a string value from the data reader, returning null if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <returns>The string value, or null if DBNull</returns>
        /// <exception cref="ArgumentNullException">Thrown if reader is null</exception>
        public static string? GetStringOrNull(this IDataReader reader, int ordinal)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        /// <summary>
        /// Safely gets a string value from the data reader, returning a default if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <param name="defaultValue">The default value to return if DBNull</param>
        /// <returns>The string value, or defaultValue if DBNull</returns>
        public static string GetStringOrDefault(this IDataReader reader, int ordinal, string defaultValue = "")
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetString(ordinal);
        }

        /// <summary>
        /// Safely gets an int32 value from the data reader, returning null if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <returns>The int value, or null if DBNull</returns>
        public static int? GetInt32OrNull(this IDataReader reader, int ordinal)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        }

        /// <summary>
        /// Safely gets an int32 value from the data reader, returning a default if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <param name="defaultValue">The default value to return if DBNull</param>
        /// <returns>The int value, or defaultValue if DBNull</returns>
        public static int GetInt32OrDefault(this IDataReader reader, int ordinal, int defaultValue = 0)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt32(ordinal);
        }

        /// <summary>
        /// Safely gets a double value from the data reader, returning null if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <returns>The double value, or null if DBNull</returns>
        public static double? GetDoubleOrNull(this IDataReader reader, int ordinal)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
        }

        /// <summary>
        /// Safely gets a double value from the data reader, returning a default if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <param name="defaultValue">The default value to return if DBNull</param>
        /// <returns>The double value, or defaultValue if DBNull</returns>
        public static double GetDoubleOrDefault(this IDataReader reader, int ordinal, double defaultValue = 0.0)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetDouble(ordinal);
        }

        /// <summary>
        /// Safely gets a float value from the data reader, returning a default if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <param name="defaultValue">The default value to return if DBNull</param>
        /// <returns>The float value, or defaultValue if DBNull</returns>
        public static float GetFloatOrDefault(this IDataReader reader, int ordinal, float defaultValue = 0.0f)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetFloat(ordinal);
        }

        /// <summary>
        /// Safely gets a boolean value from the data reader, returning a default if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <param name="defaultValue">The default value to return if DBNull</param>
        /// <returns>The boolean value, or defaultValue if DBNull</returns>
        public static bool GetBoolOrDefault(this IDataReader reader, int ordinal, bool defaultValue = false)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetBoolean(ordinal);
        }

        /// <summary>
        /// Safely gets a DateTime value from the data reader, returning null if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <returns>The DateTime value, or null if DBNull</returns>
        public static DateTime? GetDateTimeOrNull(this IDataReader reader, int ordinal)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
        }

        /// <summary>
        /// Safely gets a DateTime value from the data reader, returning a default if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <param name="defaultValue">The default value to return if DBNull</param>
        /// <returns>The DateTime value, or defaultValue if DBNull</returns>
        public static DateTime GetDateTimeOrDefault(this IDataReader reader, int ordinal, DateTime? defaultValue = null)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? (defaultValue ?? DateTime.MinValue) : reader.GetDateTime(ordinal);
        }

        /// <summary>
        /// Safely gets a decimal value from the data reader, returning a default if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <param name="defaultValue">The default value to return if DBNull</param>
        /// <returns>The decimal value, or defaultValue if DBNull</returns>
        public static decimal GetDecimalOrDefault(this IDataReader reader, int ordinal, decimal defaultValue = 0m)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetDecimal(ordinal);
        }

        /// <summary>
        /// Safely gets a byte value from the data reader, returning a default if the value is DBNull.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="ordinal">The column ordinal (index)</param>
        /// <param name="defaultValue">The default value to return if DBNull</param>
        /// <returns>The byte value, or defaultValue if DBNull</returns>
        public static byte GetByteOrDefault(this IDataReader reader, int ordinal, byte defaultValue = 0)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            return reader.IsDBNull(ordinal) ? defaultValue : reader.GetByte(ordinal);
        }

        /// <summary>
        /// Gets a value from the data reader by column name, safely handling DBNull values.
        /// 
        /// Uses case-insensitive column lookup to match database behavior.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="columnName">The column name</param>
        /// <returns>The value, or null if not found or DBNull</returns>
        public static object? GetValueByNameOrNull(this IDataReader reader, string columnName)
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            if (string.IsNullOrEmpty(columnName))
                throw new ArgumentNullException(nameof(columnName));
            
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
            }
            catch (IndexOutOfRangeException)
            {
                return null;  // Column not found
            }
        }

        /// <summary>
        /// Gets a string value from the data reader by column name, safely handling DBNull and missing columns.
        /// </summary>
        /// <param name="reader">The data reader</param>
        /// <param name="columnName">The column name</param>
        /// <param name="defaultValue">The default value if column not found or DBNull</param>
        /// <returns>The string value, or defaultValue if not found or DBNull</returns>
        public static string GetStringByNameOrDefault(this IDataReader reader, string columnName, string defaultValue = "")
        {
            if (reader is null)
                throw new ArgumentNullException(nameof(reader));
            
            if (string.IsNullOrEmpty(columnName))
                return defaultValue;
            
            try
            {
                int ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? defaultValue : reader.GetString(ordinal);
            }
            catch (IndexOutOfRangeException)
            {
                return defaultValue;  // Column not found
            }
        }
    }
}
