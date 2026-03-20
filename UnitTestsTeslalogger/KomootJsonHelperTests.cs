using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using TeslaLogger;

namespace UnitTestsTeslalogger
{
    /// <summary>
    /// Unit tests for KomootJsonHelper - validates type-safe JSON navigation functionality.
    /// </summary>
    [TestClass]
    public class KomootJsonHelperTests
    {
        private JObject _testJson;

        [TestInitialize]
        public void Setup()
        {
            // Create a test JSON structure similar to Komoot API responses
            string jsonString = @"{
                ""id"": 987654321,
                ""name"": ""Test Tour"",
                ""distance"": 2272.77,
                ""type"": ""tour_recorded"",
                ""sport"": ""cycling"",
                ""date"": ""2025-02-12T17:00:10.291Z"",
                ""user"": {
                    ""username"": ""testuser"",
                    ""displayname"": ""Test User"",
                    ""is_premium"": true
                },
                ""_embedded"": {
                    ""coordinates"": {
                        ""items"": [
                            {
                                ""lat"": 54.4484,
                                ""lng"": 33.340371,
                                ""alt"": 42.1,
                                ""t"": 0
                            },
                            {
                                ""lat"": 54.4485,
                                ""lng"": 33.340380,
                                ""alt"": 42.2,
                                ""t"": 2000
                            }
                        ]
                    },
                    ""tours"": [
                        {
                            ""id"": 123456,
                            ""type"": ""tour_recorded"",
                            ""sport"": ""touringbicycle"",
                            ""date"": ""2025-02-10T15:00:00.000Z"",
                            ""distance"": 1500.0
                        }
                    ]
                },
                ""_links"": {
                    ""next"": {
                        ""href"": ""https://api.komoot.de/v007/users/testuser/tours/?page=1""
                    }
                }
            }";

            _testJson = JObject.Parse(jsonString);
        }

        [TestMethod]
        public void GetString_WithValidPath_ReturnsCorrectValue()
        {
            // Arrange
            string path = "name";

            // Act
            string result = KomootJsonHelper.GetString(_testJson, path);

            // Assert
            Assert.AreEqual("Test Tour", result);
        }

        [TestMethod]
        public void GetString_WithNestedPath_ReturnsCorrectValue()
        {
            // Arrange
            string path = "user.displayname";

            // Act
            string result = KomootJsonHelper.GetString(_testJson, path);

            // Assert
            Assert.AreEqual("Test User", result);
        }

        [TestMethod]
        public void GetString_WithMissingProperty_ReturnsDefaultValue()
        {
            // Arrange
            string path = "nonexistent";

            // Act
            string result = KomootJsonHelper.GetString(_testJson, path, "default");

            // Assert
            Assert.AreEqual("default", result);
        }

        [TestMethod]
        public void GetString_WithNullObject_ReturnsDefaultValue()
        {
            // Arrange
            JObject nullJson = null;

            // Act
            string result = KomootJsonHelper.GetString(nullJson, "any", "default");

            // Assert
            Assert.AreEqual("default", result);
        }

        [TestMethod]
        public void GetDouble_WithValidPath_ReturnsCorrectValue()
        {
            // Arrange
            string path = "distance";

            // Act
            double result = KomootJsonHelper.GetDouble(_testJson, path);

            // Assert
            Assert.AreEqual(2272.77, result, 0.01);
        }

        [TestMethod]
        public void GetDouble_WithMissingProperty_ReturnsNaN()
        {
            // Arrange
            string path = "nonexistent";

            // Act
            double result = KomootJsonHelper.GetDouble(_testJson, path);

            // Assert
            Assert.IsTrue(double.IsNaN(result));
        }

        [TestMethod]
        public void GetInt_WithValidPath_ReturnsCorrectValue()
        {
            // Arrange
            string path = "id";

            // Act
            int result = KomootJsonHelper.GetInt(_testJson, path);

            // Assert
            Assert.AreEqual(987654321, result);
        }

        [TestMethod]
        public void GetLong_WithValidPath_ReturnsCorrectValue()
        {
            // Arrange
            string path = "id";

            // Act
            long result = KomootJsonHelper.GetLong(_testJson, path);

            // Assert
            Assert.AreEqual(987654321L, result);
        }

        [TestMethod]
        public void GetBool_WithValidPath_ReturnsCorrectValue()
        {
            // Arrange
            string path = "user.is_premium";

            // Act
            bool result = KomootJsonHelper.GetBool(_testJson, path);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void GetBool_WithMissingProperty_ReturnsFalse()
        {
            // Arrange
            string path = "nonexistent.bool";

            // Act
            bool result = KomootJsonHelper.GetBool(_testJson, path);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void HasProperty_WithExistingProperty_ReturnsTrue()
        {
            // Arrange
            string path = "name";

            // Act
            bool result = KomootJsonHelper.HasProperty(_testJson, path);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void HasProperty_WithMissingProperty_ReturnsFalse()
        {
            // Arrange
            string path = "nonexistent";

            // Act
            bool result = KomootJsonHelper.HasProperty(_testJson, path);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void HasProperty_WithNestedPath_ReturnsCorrectValue()
        {
            // Arrange
            string existingPath = "user.displayname";
            string missingPath = "user.nonexistent";

            // Act
            bool existingResult = KomootJsonHelper.HasProperty(_testJson, existingPath);
            bool missingResult = KomootJsonHelper.HasProperty(_testJson, missingPath);

            // Assert
            Assert.IsTrue(existingResult);
            Assert.IsFalse(missingResult);
        }

        [TestMethod]
        public void GetArray_WithValidPath_ReturnsCorrectItems()
        {
            // Arrange
            string path = "_embedded.coordinates.items";

            // Act
            var items = KomootJsonHelper.GetArray(_testJson, path);

            // Assert
            Assert.IsNotNull(items);
            var itemList = items.ToList();
            Assert.AreEqual(2, itemList.Count);
        }

        [TestMethod]
        public void GetArray_WithMissingPath_ReturnsEmptyEnumerable()
        {
            // Arrange
            string path = "nonexistent.items";

            // Act
            var items = KomootJsonHelper.GetArray(_testJson, path);

            // Assert
            Assert.AreEqual(0, items.Count());
        }

        [TestMethod]
        public void GetArrayCount_WithValidPath_ReturnsCorrectCount()
        {
            // Arrange
            string path = "_embedded.coordinates.items";

            // Act
            int count = KomootJsonHelper.GetArrayCount(_testJson, path);

            // Assert
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void GetArrayCount_WithMissingPath_ReturnsZero()
        {
            // Arrange
            string path = "nonexistent.items";

            // Act
            int count = KomootJsonHelper.GetArrayCount(_testJson, path);

            // Assert
            Assert.AreEqual(0, count);
        }

        [TestMethod]
        public void ValidateRequired_WithAllPropertiesPresent_ReturnsTrue()
        {
            // Arrange
            string[] requiredPaths = { "id", "name", "distance" };

            // Act
            bool result = KomootJsonHelper.ValidateRequired(_testJson, requiredPaths);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ValidateRequired_WithMissingProperty_ReturnsFalse()
        {
            // Arrange
            string[] requiredPaths = { "id", "nonexistent", "name" };

            // Act
            bool result = KomootJsonHelper.ValidateRequired(_testJson, requiredPaths);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ValidateTourCoordinatesStructure_WithValidStructure_ReturnsTrue()
        {
            // Act
            bool result = KomootJsonHelper.ValidateTourCoordinatesStructure(_testJson);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ValidateTourCoordinatesStructure_WithMissingStructure_ReturnsFalse()
        {
            // Arrange
            JObject invalidJson = JObject.Parse(@"{ ""id"": 123 }");

            // Act
            bool result = KomootJsonHelper.ValidateTourCoordinatesStructure(invalidJson);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ValidateCoordinateProperties_WithAllRequired_ReturnsTrue()
        {
            // Arrange
            var coordinates = _testJson["_embedded"]["coordinates"]["items"][0] as JObject;

            // Act
            bool result = KomootJsonHelper.ValidateCoordinateProperties(coordinates);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ValidateCoordinateProperties_WithMissingProperty_ReturnsFalse()
        {
            // Arrange
            JObject invalidCoordinate = JObject.Parse(@"{ ""lat"": 54.4484, ""lng"": 33.340371 }");

            // Act
            bool result = KomootJsonHelper.ValidateCoordinateProperties(invalidCoordinate);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void ValidateTourListStructure_WithValidStructure_ReturnsTrue()
        {
            // Act
            bool result = KomootJsonHelper.ValidateTourListStructure(_testJson);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ValidatePaginationStructure_WithValidStructure_ReturnsTrue()
        {
            // Act
            bool result = KomootJsonHelper.ValidatePaginationStructure(_testJson);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void ValidatePaginationStructure_WithMissingStructure_ReturnsFalse()
        {
            // Arrange
            JObject invalidJson = JObject.Parse(@"{ ""id"": 123, ""_links"": {} }");

            // Act
            bool result = KomootJsonHelper.ValidatePaginationStructure(invalidJson);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryParseJson_WithValidJson_ReturnsJObject()
        {
            // Arrange
            string validJson = @"{ ""name"": ""Test"", ""value"": 123 }";

            // Act
            JObject result = KomootJsonHelper.TryParseJson(validJson);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Test", result["name"].Value<string>());
            Assert.AreEqual(123, result["value"].Value<int>());
        }

        [TestMethod]
        public void TryParseJson_WithInvalidJson_ReturnsNull()
        {
            // Arrange
            string invalidJson = "{ invalid json }";

            // Act
            JObject result = KomootJsonHelper.TryParseJson(invalidJson);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TryParseJson_WithNullString_ReturnsNull()
        {
            // Act
            JObject result = KomootJsonHelper.TryParseJson(null);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TryParseJson_WithErrorCallback_CallsErrorAction()
        {
            // Arrange
            string invalidJson = "{ invalid json }";
            bool errorCalled = false;
            string errorMessage = "";

            // Act
            JObject result = KomootJsonHelper.TryParseJson(invalidJson, 
                err => { errorCalled = true; errorMessage = err; });

            // Assert
            Assert.IsNull(result);
            Assert.IsTrue(errorCalled);
            Assert.IsTrue(errorMessage.Contains("JSON parsing failed"));
        }

        [TestMethod]
        public void AsObject_WithJObject_ReturnsObject()
        {
            // Arrange
            JObject expectedObject = JObject.Parse(@"{ ""name"": ""Test"" }");
            JToken token = expectedObject;

            // Act
            JObject result = KomootJsonHelper.AsObject(token);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Test", result["name"].Value<string>());
        }

        [TestMethod]
        public void AsObject_WithNonObjectToken_ReturnsNull()
        {
            // Arrange
            JArray array = JArray.Parse(@"[1, 2, 3]");
            JToken token = array[0];

            // Act
            JObject result = KomootJsonHelper.AsObject(token);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetArray_IterationPreservesNestedValues()
        {
            // Arrange
            string path = "_embedded.coordinates.items";

            // Act
            var items = KomootJsonHelper.GetArray(_testJson, path).ToList();

            // Assert
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(54.4484, items[0]["lat"].Value<double>(), 0.0001);
            Assert.AreEqual(33.340371, items[0]["lng"].Value<double>(), 0.0001);
            Assert.AreEqual(0, items[0]["t"].Value<int>());
            
            Assert.AreEqual(54.4485, items[1]["lat"].Value<double>(), 0.0001);
            Assert.AreEqual(2000, items[1]["t"].Value<int>());
        }

        [TestMethod]
        public void IntegrationTest_ParseCompleteCoordinateStructure()
        {
            // Arrange - Simulate real usage from Komoot API parsing

            // Act - Get coordinates like ParseTourJSON does
            var coordinates = KomootJsonHelper.GetArray(_testJson, "_embedded.coordinates.items").ToList();

            // Assert
            Assert.AreEqual(2, coordinates.Count);

            foreach (var coord in coordinates)
            {
                // Validate each coordinate has required properties
                Assert.IsTrue(KomootJsonHelper.ValidateCoordinateProperties(coord));

                // Extract values
                double lat = KomootJsonHelper.GetDouble(coord, "lat");
                double lng = KomootJsonHelper.GetDouble(coord, "lng");
                double alt = KomootJsonHelper.GetDouble(coord, "alt");
                int deltaT = KomootJsonHelper.GetInt(coord, "t");

                Assert.IsFalse(double.IsNaN(lat));
                Assert.IsFalse(double.IsNaN(lng));
                Assert.IsFalse(double.IsNaN(alt));
                Assert.IsTrue(deltaT >= 0);
            }
        }
    }
}
