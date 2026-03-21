using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace TeslaLogger
{
    /// <summary>
    /// Helper class for safe navigation and parsing of Komoot API JSON responses.
    /// Provides type-safe alternatives to dynamic object access with proper null handling.
    /// </summary>
    internal static class KomootJsonHelper
    {
        /// <summary>
        /// Safely retrieves a string value from a JSON object with a default fallback.
        /// </summary>
        /// <param name="jObject">The JSON object to read from.</param>
        /// <param name="path">The property path (e.g., "property" or "parent.child").</param>
        /// <param name="defaultValue">The default value if the property is missing or null.</param>
        /// <returns>The string value, or the default value if not found or null.</returns>
        internal static string GetString(JObject jObject, string path, string defaultValue = "")
        {
            if (jObject == null) return defaultValue;
            
            try
            {
                JToken token = jObject.SelectToken(path);
                return token?.Value<string>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Safely retrieves a double value from a JSON object with a default fallback.
        /// </summary>
        /// <param name="jObject">The JSON object to read from.</param>
        /// <param name="path">The property path (e.g., "property" or "parent.child").</param>
        /// <param name="defaultValue">The default value if the property is missing, null, or cannot be parsed.</param>
        /// <returns>The double value, or the default value if not found or unparseable.</returns>
        internal static double GetDouble(JObject jObject, string path, double defaultValue = double.NaN)
        {
            if (jObject == null) return defaultValue;
            
            try
            {
                JToken token = jObject.SelectToken(path);
                return token?.Value<double>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Safely retrieves a long value from a JSON object with a default fallback.
        /// </summary>
        /// <param name="jObject">The JSON object to read from.</param>
        /// <param name="path">The property path.</param>
        /// <param name="defaultValue">The default value if the property is missing, null, or cannot be parsed.</param>
        /// <returns>The long value, or the default value if not found or unparseable.</returns>
        internal static long GetLong(JObject jObject, string path, long defaultValue = -1)
        {
            if (jObject == null) return defaultValue;
            
            try
            {
                JToken token = jObject.SelectToken(path);
                return token?.Value<long>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Safely retrieves an integer value from a JSON object with a default fallback.
        /// </summary>
        /// <param name="jObject">The JSON object to read from.</param>
        /// <param name="path">The property path.</param>
        /// <param name="defaultValue">The default value if the property is missing, null, or cannot be parsed.</param>
        /// <returns>The integer value, or the default value if not found or unparseable.</returns>
        internal static int GetInt(JObject jObject, string path, int defaultValue = -1)
        {
            if (jObject == null) return defaultValue;
            
            try
            {
                JToken token = jObject.SelectToken(path);
                return token?.Value<int>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Safely retrieves a boolean value from a JSON object with a default fallback.
        /// </summary>
        /// <param name="jObject">The JSON object to read from.</param>
        /// <param name="path">The property path.</param>
        /// <param name="defaultValue">The default value if the property is missing, null, or cannot be parsed.</param>
        /// <returns>The boolean value, or the default value if not found or unparseable.</returns>
        internal static bool GetBool(JObject jObject, string path, bool defaultValue = false)
        {
            if (jObject == null) return defaultValue;
            
            try
            {
                JToken token = jObject.SelectToken(path);
                return token?.Value<bool>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Safely checks if a JSON object contains a property at the specified path.
        /// </summary>
        /// <param name="jObject">The JSON object to check.</param>
        /// <param name="path">The property path.</param>
        /// <returns>True if the property exists and is not null; false otherwise.</returns>
        internal static bool HasProperty(JObject jObject, string path)
        {
            if (jObject == null) return false;
            
            try
            {
                JToken token = jObject.SelectToken(path);
                return token != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Safely retrieves an array of items from a JSON object as JObject enumerable.
        /// Handles both direct array properties and nested paths.
        /// </summary>
        /// <param name="jObject">The JSON object to read from.</param>
        /// <param name="path">The property path to the array.</param>
        /// <returns>An enumerable of JObject items, empty enumerable if not found or not an array.</returns>
        internal static IEnumerable<JObject> GetArray(JObject jObject, string path)
        {
            if (jObject == null) return Enumerable.Empty<JObject>();
            
            try
            {
                JToken token = jObject.SelectToken(path);
                if (token is JArray array)
                {
                    return array.OfType<JObject>();
                }
                return Enumerable.Empty<JObject>();
            }
            catch
            {
                return Enumerable.Empty<JObject>();
            }
        }

        /// <summary>
        /// Safely gets the count of items in an array property.
        /// </summary>
        /// <param name="jObject">The JSON object to read from.</param>
        /// <param name="path">The property path to the array.</param>
        /// <returns>The count of items in the array, or 0 if not found or not an array.</returns>
        internal static int GetArrayCount(JObject jObject, string path)
        {
            if (jObject == null) return 0;
            
            try
            {
                JToken token = jObject.SelectToken(path);
                if (token is JArray array)
                {
                    return array.Count;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Validates that all required properties exist in a JSON object.
        /// </summary>
        /// <param name="jObject">The JSON object to validate.</param>
        /// <param name="requiredPaths">Array of property paths that must exist.</param>
        /// <returns>True if all required properties exist; false otherwise.</returns>
        internal static bool ValidateRequired(JObject jObject, params string[] requiredPaths)
        {
            if (jObject == null) return false;
            
            foreach (string path in requiredPaths)
            {
                if (!HasProperty(jObject, path))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Gets a JObject from a JToken, safely handling type mismatches.
        /// </summary>
        /// <param name="token">The token to convert.</param>
        /// <returns>A JObject if the token is an object; null otherwise.</returns>
        internal static JObject AsObject(JToken token)
        {
            return token as JObject;
        }

        /// <summary>
        /// Safely parses a JSON string into a JObject.
        /// </summary>
        /// <param name="json">The JSON string to parse.</param>
        /// <param name="onError">Optional action to call if parsing fails.</param>
        /// <returns>The parsed JObject, or null if parsing fails.</returns>
        internal static JObject? TryParseJson(string json, Action<string>? onError = null)
        {
            if (string.IsNullOrEmpty(json)) return null;
            
            try
            {
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"JSON parsing failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validates the Komoot tour coordinate structure.
        /// Expected structure: jObject.SelectToken("_embedded.coordinates.items")
        /// </summary>
        /// <param name="tourJson">The JSON object containing tour data.</param>
        /// <returns>True if the required coordinate structure exists; false otherwise.</returns>
        internal static bool ValidateTourCoordinatesStructure(JObject tourJson)
        {
            return ValidateRequired(tourJson, "_embedded.coordinates.items");
        }

        /// <summary>
        /// Validates that a coordinate object has all required position properties.
        /// </summary>
        /// <param name="coordinateObject">The coordinate JObject to validate.</param>
        /// <returns>True if all required position properties exist; false otherwise.</returns>
        internal static bool ValidateCoordinateProperties(JObject coordinateObject)
        {
            return ValidateRequired(coordinateObject, "lat", "lng", "alt", "t");
        }

        /// <summary>
        /// Validates the Komoot tour list structure from API response.
        /// </summary>
        /// <param name="responseJson">The JSON object containing the API response.</param>
        /// <returns>True if the required tour list structure exists; false otherwise.</returns>
        internal static bool ValidateTourListStructure(JObject responseJson)
        {
            return ValidateRequired(responseJson, "_embedded.tours");
        }

        /// <summary>
        /// Validates the pagination structure in a tour list response.
        /// </summary>
        /// <param name="responseJson">The JSON object containing the API response.</param>
        /// <returns>True if the required pagination structure exists; false otherwise.</returns>
        internal static bool ValidatePaginationStructure(JObject responseJson)
        {
            return ValidateRequired(responseJson, "_links.next.href");
        }
    }
}
