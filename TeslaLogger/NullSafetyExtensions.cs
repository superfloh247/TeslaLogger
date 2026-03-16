using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace TeslaLogger
{
    /// <summary>
    /// Extension methods for null-safe operations on common types.
    /// Provides fluent API access to null-safety patterns.
    /// </summary>
    internal static class NullSafetyExtensions
    {
        #region String Extensions

        /// <summary>
        /// Convert string to non-null empty if null.
        /// </summary>
        public static string OrEmpty(this string? value)
        {
            return value ?? string.Empty;
        }

        /// <summary>
        /// Convert string to fallback value if null or empty.
        /// </summary>
        public static string Or(this string? value, string fallback)
        {
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        #endregion

        #region Numeric Extensions

        /// <summary>
        /// Safely parse string to int.
        /// </summary>
        public static int ToSafeInt(this string? value, int fallback = 0)
        {
            return NullSafetyHelpers.SafeInt(value, fallback);
        }

        /// <summary>
        /// Safely parse string to long.
        /// </summary>
        public static long ToSafeLong(this string? value, long fallback = 0L)
        {
            return NullSafetyHelpers.SafeLong(value, fallback);
        }

        /// <summary>
        /// Safely parse string to double.
        /// </summary>
        public static double ToSafeDouble(this string? value, double fallback = 0.0)
        {
            return NullSafetyHelpers.SafeDouble(value, fallback);
        }

        /// <summary>
        /// Safely parse string to decimal.
        /// </summary>
        public static decimal ToSafeDecimal(this string? value, decimal fallback = 0m)
        {
            return NullSafetyHelpers.SafeDecimal(value, fallback);
        }

        /// <summary>
        /// Safely parse string to bool.
        /// </summary>
        public static bool ToSafeBool(this string? value, bool fallback = false)
        {
            return NullSafetyHelpers.SafeBool(value, fallback);
        }

        #endregion

        #region Object Extensions

        /// <summary>
        /// Safely convert object to string.
        /// </summary>
        public static string ToSafeString(this object? value)
        {
            return NullSafetyHelpers.SafeString(value);
        }

        #endregion

        #region Dictionary Extensions

        /// <summary>
        /// Safely get value from dictionary.
        /// </summary>
        public static T? GetSafe<T>(this Dictionary<string, T>? dict, string? key) where T : class
        {
            return NullSafetyHelpers.SafeGet(dict, key);
        }

        /// <summary>
        /// Safely get string from dictionary.
        /// </summary>
        public static string GetSafeString(this Dictionary<string, string>? dict, string? key, string fallback = "")
        {
            return NullSafetyHelpers.SafeGetString(dict, key, fallback);
        }

        /// <summary>
        /// Safely get object from dictionary.
        /// </summary>
        public static object? GetSafeObject(this Dictionary<string, object>? dict, string? key)
        {
            return NullSafetyHelpers.SafeGetObject(dict, key);
        }

        /// <summary>
        /// Try to get value, returning success status.
        /// </summary>
        public static bool TryGetSafe<T>(this Dictionary<string, T>? dict, string? key, out T? value) where T : class
        {
            value = null;
            if (dict is null || key is null) return false;
            return dict.TryGetValue(key, out value);
        }

        #endregion

        #region JSON Extensions

        /// <summary>
        /// Safely parse JSON to JObject.
        /// </summary>
        public static JObject? ToJObject(this string? json)
        {
            return NullSafetyHelpers.SafeJObject(json);
        }

        /// <summary>
        /// Safely get property value from JObject as string.
        /// </summary>
        public static string GetSafeString(this JObject? jobj, string? property, string fallback = "")
        {
            return NullSafetyHelpers.SafeJProperty(jobj, property, fallback);
        }

        /// <summary>
        /// Safely get property token from JObject.
        /// </summary>
        public static JToken? GetSafeToken(this JObject? jobj, string? property)
        {
            return NullSafetyHelpers.SafeJToken(jobj, property);
        }

        /// <summary>
        /// Safely get array from JObject.
        /// </summary>
        public static JArray? GetSafeArray(this JObject? jobj, string? property)
        {
            return NullSafetyHelpers.SafeJArray(jobj, property);
        }

        /// <summary>
        /// Safely get int from JObject property.
        /// </summary>
        public static int GetSafeInt(this JObject? jobj, string? property, int fallback = 0)
        {
            var value = jobj?.GetSafeToken(property)?.ToString();
            return NullSafetyHelpers.SafeInt(value, fallback);
        }

        /// <summary>
        /// Safely get long from JObject property.
        /// </summary>
        public static long GetSafeLong(this JObject? jobj, string? property, long fallback = 0L)
        {
            var value = jobj?.GetSafeToken(property)?.ToString();
            return NullSafetyHelpers.SafeLong(value, fallback);
        }

        /// <summary>
        /// Safely get double from JObject property.
        /// </summary>
        public static double GetSafeDouble(this JObject? jobj, string? property, double fallback = 0.0)
        {
            var value = jobj?.GetSafeToken(property)?.ToString();
            return NullSafetyHelpers.SafeDouble(value, fallback);
        }

        /// <summary>
        /// Safely get decimal from JObject property.
        /// </summary>
        public static decimal GetSafeDecimal(this JObject? jobj, string? property, decimal fallback = 0m)
        {
            var value = jobj?.GetSafeToken(property)?.ToString();
            return NullSafetyHelpers.SafeDecimal(value, fallback);
        }

        #endregion

        #region Collection Extensions

        /// <summary>
        /// Safely get count - returns 0 if null.
        /// </summary>
        public static int GetSafeCount<T>(this ICollection<T>? collection)
        {
            return NullSafetyHelpers.SafeCount(collection);
        }

        /// <summary>
        /// Check if collection has items (not null and count > 0).
        /// </summary>
        public static bool HasItems<T>(this ICollection<T>? collection)
        {
            return NullSafetyHelpers.SafeHasItems(collection);
        }

        #endregion

        #region Null Checking

        /// <summary>
        /// Check if string is null or empty.
        /// </summary>
        public static bool IsEmpty(this string? value)
        {
            return string.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Check if string is not null and not empty.
        /// </summary>
        public static bool IsNotEmpty(this string? value)
        {
            return !string.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Execute action if value is not null.
        /// </summary>
        public static void IfNotNull<T>(this T? value, Action<T> action) where T : class
        {
            if (value is not null) action(value);
        }

        /// <summary>
        /// Execute action if string is not empty.
        /// </summary>
        public static void IfNotEmpty(this string? value, Action<string> action)
        {
            if (!string.IsNullOrEmpty(value)) action(value);
        }

        #endregion

        #region Type Conversions

        /// <summary>
        /// Safely cast to type T.
        /// </summary>
        public static T? SafeCast<T>(this object? value) where T : class
        {
            return NullSafetyHelpers.SafeCast<T>(value);
        }

        #endregion
    }
}
