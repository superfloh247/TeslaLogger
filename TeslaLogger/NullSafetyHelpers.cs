using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace TeslaLogger
{
    /// <summary>
    /// Minimal null-safety helper library to eliminate common null-reference warnings.
    /// These helpers provide safe access patterns for JSON objects, dictionaries, and value conversions.
    /// </summary>
    internal static class NullSafetyHelpers
    {
        #region String Conversions

        /// <summary>
        /// Safely convert any nullable value to string.
        /// Returns empty string if value is null.
        /// </summary>
        public static string SafeString(object? value)
        {
            if (value is null) return string.Empty;
            if (value is string str) return str;
            try { return value.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Safely get string from nullable string, with optional fallback.
        /// </summary>
        public static string SafeString(string? value, string fallback = "")
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        #endregion

        #region Numeric Conversions

        /// <summary>
        /// Safely parse int from nullable string.
        /// Returns 0 if parsing fails.
        /// </summary>
        public static int SafeInt(string? value, int fallback = 0)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            if (int.TryParse(value, out var result)) return result;
            return fallback;
        }

        /// <summary>
        /// Safely parse long from nullable string.
        /// Returns 0L if parsing fails.
        /// </summary>
        public static long SafeLong(string? value, long fallback = 0L)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            if (long.TryParse(value, out var result)) return result;
            return fallback;
        }

        /// <summary>
        /// Safely parse double from nullable string.
        /// Returns 0.0 if parsing fails.
        /// </summary>
        public static double SafeDouble(string? value, double fallback = 0.0)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            if (double.TryParse(value, out var result)) return result;
            return fallback;
        }

        /// <summary>
        /// Safely parse decimal from nullable string.
        /// Returns 0m if parsing fails.
        /// </summary>
        public static decimal SafeDecimal(string? value, decimal fallback = 0m)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            if (decimal.TryParse(value, out var result)) return result;
            return fallback;
        }

        /// <summary>
        /// Safely parse bool from nullable string.
        /// Returns false if parsing fails.
        /// </summary>
        public static bool SafeBool(string? value, bool fallback = false)
        {
            if (string.IsNullOrEmpty(value)) return fallback;
            if (bool.TryParse(value, out var result)) return result;
            // Try common string patterns
            return value.Equals("1", StringComparison.OrdinalIgnoreCase) || 
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("true", StringComparison.OrdinalIgnoreCase) ?
                   true : fallback;
        }

        #endregion

        #region Dictionary Access

        /// <summary>
        /// Safely get value from dictionary by key.
        /// Returns null or default(T) if key not found.
        /// </summary>
        public static T? SafeGet<T>(Dictionary<string, T>? dict, string? key) where T : class
        {
            if (dict is null || key is null) return null;
            dict.TryGetValue(key, out var value);
            return value;
        }

        /// <summary>
        /// Safely get string from dictionary by key.
        /// Returns empty string if key not found or value is null.
        /// </summary>
        public static string SafeGetString(Dictionary<string, string>? dict, string? key, string fallback = "")
        {
            if (dict is null || key is null) return fallback;
            if (dict.TryGetValue(key, out var value)) 
                return value ?? fallback;
            return fallback;
        }

        /// <summary>
        /// Safely get object from dictionary by key.
        /// Returns null if key not found.
        /// </summary>
        public static object? SafeGetObject(Dictionary<string, object>? dict, string? key)
        {
            if (dict is null || key is null) return null;
            dict.TryGetValue(key, out var value);
            return value;
        }

        #endregion

        #region JSON Operations

        /// <summary>
        /// Safely parse JSON string to JObject.
        /// Returns null if parsing fails.
        /// </summary>
        public static JObject? SafeJObject(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JObject.Parse(json); }
            catch { return null; }
        }

        /// <summary>
        /// Safely get string value from JObject property.
        /// Returns empty string if property not found or is null.
        /// </summary>
        public static string SafeJProperty(JObject? jobj, string? property, string fallback = "")
        {
            if (jobj is null || property is null) return fallback;
            try
            {
                var token = jobj[property];
                if (token is null) return fallback;
                var str = token.ToString();
                return string.IsNullOrEmpty(str) ? fallback : str;
            }
            catch { return fallback; }
        }

        /// <summary>
        /// Safely get JToken from JObject property.
        /// Returns null if property not found.
        /// </summary>
        public static JToken? SafeJToken(JObject? jobj, string? property)
        {
            if (jobj is null || property is null) return null;
            try { return jobj[property]; }
            catch { return null; }
        }

        /// <summary>
        /// Safely get array from JObject property.
        /// Returns empty array if not found or not an array.
        /// </summary>
        public static JArray? SafeJArray(JObject? jobj, string? property)
        {
            if (jobj is null || property is null) return null;
            try
            {
                var token = jobj[property];
                return token as JArray;
            }
            catch { return null; }
        }

        #endregion

        #region Null Coalescing Helpers

        /// <summary>
        /// Return first non-null value from params.
        /// </summary>
        public static T? Coalesce<T>(params T?[] values) where T : class
        {
            foreach (var value in values)
            {
                if (value is not null) return value;
            }
            return null;
        }

        /// <summary>
        /// Return first non-empty string from params.
        /// </summary>
        public static string CoalesceString(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return string.Empty;
        }

        #endregion

        #region Collection Helpers

        /// <summary>
        /// Safely get count of collection.
        /// Returns 0 if collection is null.
        /// </summary>
        public static int SafeCount<T>(ICollection<T>? collection)
        {
            return collection?.Count ?? 0;
        }

        /// <summary>
        /// Safely check if collection has items.
        /// </summary>
        public static bool SafeHasItems<T>(ICollection<T>? collection)
        {
            return collection is not null && collection.Count > 0;
        }

        #endregion

        #region Type Conversions

        /// <summary>
        /// Safely cast object to type T.
        /// Returns null if cast fails.
        /// </summary>
        public static T? SafeCast<T>(object? value) where T : class
        {
            return value as T;
        }

        /// <summary>
        /// Safely try cast with fallback.
        /// </summary>
        public static T SafeCastOrDefault<T>(object? value, T defaultValue) where T : struct
        {
            if (value is T t) return t;
            return defaultValue;
        }

        #endregion
    }
}
