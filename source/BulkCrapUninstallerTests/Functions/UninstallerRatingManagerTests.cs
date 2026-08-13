using System;
using System.IO;
using BulkCrapUninstaller;
using BulkCrapUninstaller.Functions.Ratings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BulkCrapUninstallerTests.Functions
{
    [TestClass]
    public class UninstallerRatingManagerTests
    {
        private static readonly string[] TestEntryNames = {"Test_1", "Test_2", "Test_3", "Test_4"};
        private UninstallerRatingManager _manager;
        
        [TestInitialize]
        public void TestInitialize()
        {
            _manager = new UninstallerRatingManager(1);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _manager?.ClearRatings();
        }

        [TestMethod]
        public void RefreshStatsTest()
        {
            Assert.Inconclusive("Expensive, no need to always test");

            _manager.FetchRatings();
            if (_manager.RemoteRatingCount == 0)
                Assert.Fail();
        }

        [TestMethod]
        public void GetRatingTest()
        {
            _manager.FetchRatings();
            var rating = _manager.GetRating(TestEntryNames[0]);

            if (rating.IsEmpty)
                Assert.Fail();
        }

        [TestMethod]
        public void SetMyRatingTest()
        {
            _manager.FetchRatings();

            try
            {
                _manager.SetMyRating(null, UninstallerRating.Bad);
                Assert.Fail();
            }
            catch (ArgumentNullException)
            {
            }
            try
            {
                _manager.SetMyRating(TestEntryNames[0], UninstallerRating.Unknown);
                Assert.Fail();
            }
            catch (ArgumentException)
            {
            }

            _manager.SetMyRating(TestEntryNames[0], UninstallerRating.Good);
            Assert.AreEqual((int) UninstallerRating.Good, _manager.GetRating(TestEntryNames[0]).MyRating);

            var rating = _manager.GetRating("Test_SetMyRatingTest");
            var newRating = rating.MyRating == (int) UninstallerRating.Bad
                ? UninstallerRating.Good
                : UninstallerRating.Bad;
            _manager.SetMyRating("Test_SetMyRatingTest", newRating);
            Assert.AreEqual((int) newRating, _manager.GetRating("Test_SetMyRatingTest").MyRating);
        }

        /// <summary>
        /// Round-trips the rating cache through disk.
        /// </summary>
        /// <remarks>
        /// Seeds ratings locally rather than calling FetchRatings(). MOKSH ships with no reporting
        /// backend, so a fetch returns nothing and the old version of this test - which asserted that
        /// the server had sent something - could never pass. The serialize/deserialize behaviour it
        /// is named for does not need a network round trip to exercise, and testing it locally makes
        /// the test deterministic rather than dependent on a third party being reachable.
        /// </remarks>
        [TestMethod]
        public void SerializeDeserializeCasheTest()
        {
            foreach (var entryName in TestEntryNames)
                _manager.SetMyRating(entryName, UninstallerRating.Good);

            var count = _manager.RemoteRatingCount + _manager.UserRatingCount;
            Assert.AreEqual(TestEntryNames.Length, count, "Failed to seed the ratings to serialize");

            var filename = Path.Combine(Program.AssemblyLocation.FullName, "TestTempDir");

            var dir = new DirectoryInfo(filename);
            dir.Create();
            try
            {
                _manager.SerializeCache(dir);

                TestCleanup();
                TestInitialize();

                Assert.AreEqual(0, _manager.RemoteRatingCount + _manager.UserRatingCount,
                    "Ratings should be empty before deserializing");

                _manager.DeserializeCache(dir);
                Assert.AreEqual(count, _manager.RemoteRatingCount + _manager.UserRatingCount);

                foreach (var entryName in TestEntryNames)
                    Assert.AreEqual((int)UninstallerRating.Good, _manager.GetRating(entryName).MyRating);
            }
            finally
            {
                try { dir.Delete(true); }
                catch (IOException) { /* Leftover temp dir is not worth failing the test over */ }
            }
        }
    }
}