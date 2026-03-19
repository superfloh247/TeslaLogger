using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TeslaLogger;

namespace UnitTestsTeslalogger
{
    [TestClass]
    public class UnitTestGeocodeMapQuest
    {
        Car c = null;
#pragma warning disable CS0169 // Field never used
        Geofence geofence;
#pragma warning restore CS0169

        [TestInitialize]
        public void TestInitialize()
        {
            ServicePointManager.Expect100Continue = true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var geofence = Geofence.GetInstance();

            if (c is null)
                c = new Car(0, "", "", 0, "", DateTime.Now, "", "", "", "", "", "", "", null, false);

            geofence = Geofence.GetInstance();
            geofence.geofenceList.Clear();
            geofence.geofencePrivateList.Clear();
            // xx GeocodeCache.ClearCache();

            ApplicationSettings.Default.Reload();
            var k = ApplicationSettings.Default.MapQuestKey;
            ApplicationSettings.Default.PropertyValues["MapQuestKey"].PropertyValue = Settings.Default.MapQuestKey;

            k = ApplicationSettings.Default.MapQuestKey;
        }

        [TestMethod]
        public async Task UlmBeimTuermle()
        {
            string temp = await WebHelper.ReverseGecocodingAsync(c, 48.4053267, 9.9547932, true).ConfigureAwait(false);
            Assert.AreEqual("89075 Ulm, Beim Türmle 23", temp);
            Assert.AreEqual("de", c.CurrentJSON.current_country_code);
            Assert.AreEqual("BW", c.CurrentJSON.current_state);
        }

        [TestMethod]
        public async Task SulzbacherStr()
        {
            string temp = await WebHelper.ReverseGecocodingAsync(c, 48.96092, 9.43113, true).ConfigureAwait(false);
            Assert.AreEqual("71522 Backnang, Sulzbacher Straße", temp);
            Assert.AreEqual("de", c.CurrentJSON.current_country_code);
            Assert.AreEqual("BW", c.CurrentJSON.current_state);
        }


        [TestMethod]
        public async Task NewJerseyNorthBergen()
        {
            string temp = await WebHelper.ReverseGecocodingAsync(c, 40.773667, -74.039867, true).ConfigureAwait(false);
            Assert.AreEqual("us-07047 North Bergen, 2650 Paterson Plank Rd", temp);
            Assert.AreEqual("us", c.CurrentJSON.current_country_code);
            Assert.AreEqual("NJ", c.CurrentJSON.current_state);
        }

        [TestMethod]
        public async Task UlmBeringerbruecke()
        {
            string temp = await WebHelper.ReverseGecocodingAsync(c, 48.400892, 9.970095, true).ConfigureAwait(false);
            Assert.AreEqual("89077 Ulm, Blaubeurer Straße", temp);
            Assert.AreEqual("de", c.CurrentJSON.current_country_code);
            Assert.AreEqual("BW", c.CurrentJSON.current_state);
        }

        [TestMethod]
        public async Task Japan()
        {
            string temp = await WebHelper.ReverseGecocodingAsync(c, 35.677121, 139.751033, true).ConfigureAwait(false);
            Assert.AreEqual("jp- 千代田区, 内堀通り", temp);
            Assert.AreEqual("jp", c.CurrentJSON.current_country_code);
            Assert.AreEqual("", c.CurrentJSON.current_state);
        }

        [TestMethod]
        public async Task MietingenMehrzweckhalle()
        {
            string temp = await WebHelper.ReverseGecocodingAsync(c, 48.1850756, 9.9016996, true).ConfigureAwait(false);
            Assert.AreEqual("88487 Mietingen, Tulpenweg 20", temp); // should be "88487 Mietingen, Tulpenweg 20" but nominatim doesn't provide Mietingen as village
            Assert.AreEqual("de", c.CurrentJSON.current_country_code);
            Assert.AreEqual("BW", c.CurrentJSON.current_state);
        }

        [TestMethod]
        public async Task ApothekeWiblingen()
        {
            string temp = await WebHelper.ReverseGecocodingAsync(c, 48.360601, 9.984227, true).ConfigureAwait(false);
            Assert.AreEqual("89079 Ulm, Donautalstraße 46", temp);
            Assert.AreEqual("de", c.CurrentJSON.current_country_code);
            Assert.AreEqual("BW", c.CurrentJSON.current_state);
        }

        [TestMethod]
        public async Task Lat0Lng0()
        {
            string temp = await WebHelper.ReverseGecocodingAsync(c, 0, 0, true).ConfigureAwait(false);
            Assert.AreEqual("- , ", temp);
        }

    }
}
