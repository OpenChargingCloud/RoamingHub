/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of RoamingHub <https://github.com/OpenChargingCloud/RoamingHub>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Net;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Aegir;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP;
using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.TestKit;
using cloud.charging.open.RoamingHub.Configuration;

using ChargingStationOperator  = cloud.charging.open.protocols.WWCP.POI.ChargingStationOperator;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    #region POIConfigurationTests

    /// <summary>
    /// The "poi" section of the configuration file: what the histories of the roaming networks keep, and whether the
    /// commits they drop are archived; the one sentence naming the field when it is wrong.
    /// </summary>
    [TestFixture]
    public class POIConfigurationTests
    {

        #region WithoutTheSectionTheDefaultsOfWWCPPOIHold()

        [Test]
        public void WithoutTheSectionTheDefaultsOfWWCPPOIHold()
        {

            Assert.That(RoamingHubConfiguration.TryParse([], out var configuration, out var error), Is.True, error);

            var poi       = configuration!.POI ?? new POIConfiguration();
            var defaults  = new RoamingNetworkHistoryRetention();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(configuration.POI,                                      Is.Null);
                Assert.That(poi.EffectiveRetention,                                 Is.EqualTo(defaults));
                Assert.That(poi.EffectiveArchive,                                   Is.True, "Archived, unless the file says not to.");
            }

        }

        #endregion

        #region AFullSectionIsReadAndWrittenAgain()

        [Test]
        public void AFullSectionIsReadAndWrittenAgain()
        {

            var section = new JObject(
                              new JProperty("retainCommits",              5000),
                              new JProperty("compactAfterLogEntries",     20000),
                              new JProperty("versionCheckpointInterval",  50),
                              new JProperty("recentVersions",             10),
                              new JProperty("archive",                    false)
                          );

            Assert.That(RoamingHubConfiguration.TryParse(new JObject(new JProperty(POIConfiguration.SectionName, section)), out var configuration, out var error), Is.True, error);

            var retention = configuration!.POI!.EffectiveRetention;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(retention.RetainCommits,                                Is.EqualTo(5000));
                Assert.That(retention.CompactAfterLogEntries,                       Is.EqualTo(20000));
                Assert.That(retention.VersionCheckpointInterval,                    Is.EqualTo(50));
                Assert.That(retention.RecentVersions,                               Is.EqualTo(10));
                Assert.That(configuration.POI.EffectiveArchive,                     Is.False);
                Assert.That(JToken.DeepEquals(configuration.ToJSON()[POIConfiguration.SectionName], section), Is.True);
                Assert.That(configuration.ToString(),                               Does.Contain("not archived"));
            }

        }

        #endregion

        #region AFieldThatIsWrongIsNamed(Field, Value, Said)

        [TestCase("retainCommits",           "-1",     "'poi.retainCommits' must be between 0 and")]
        [TestCase("compactAfterLogEntries",  "0",      "'poi.compactAfterLogEntries' must be between 1 and")]
        [TestCase("recentVersions",          "1.5",    "'poi.recentVersions' must be a whole number")]
        [TestCase("archive",                 "\"no\"", "'poi.archive'")]
        public void AFieldThatIsWrongIsNamed(String Field, String Value, String Said)
        {

            var json = new JObject(new JProperty(POIConfiguration.SectionName, new JObject(new JProperty(Field, JToken.Parse(Value)))));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(RoamingHubConfiguration.TryParse(json, out _, out var error), Is.False);
                Assert.That(error,                                                         Does.Contain(Said));
            }

        }

        #endregion

        #region ASectionThatIsNoObjectIsRefused()

        [Test]
        public void ASectionThatIsNoObjectIsRefused()
        {

            using (Assert.EnterMultipleScope())
            {
                Assert.That(RoamingHubConfiguration.TryParse(new JObject(new JProperty(POIConfiguration.SectionName, new JArray())), out _, out var error), Is.False);
                Assert.That(error,                                                                                                                     Is.EqualTo("'poi' must be a JSON object."));
            }

        }

        #endregion

    }

    #endregion

    #region POIRetentionTests

    /// <summary>
    /// A hub keeping little of the histories of its roaming networks: it compacts their logs, and the commits it drops go
    /// into the archive beside their stores, from where they are served and proved as before, also after a restart.
    /// </summary>
    public class POIRetentionTests : ARoamingHubTests
    {

        #region Configuration

        protected override JObject Configuration
        {
            get
            {
                var configuration = TestRoamingHubs.Offline;
                configuration[POIConfiguration.SectionName] = new JObject(
                                                                   new JProperty("retainCommits",              1),
                                                                   new JProperty("compactAfterLogEntries",     4),
                                                                   new JProperty("versionCheckpointInterval",  1),
                                                                   new JProperty("recentVersions",             1)
                                                               );
                return configuration;
            }
        }

        #endregion

        #region (static) Eventually(Condition)

        private static async Task<Boolean> Eventually(Func<Boolean> Condition)
        {

            for (var i = 0; i < 200; i++)
            {

                if (Condition())
                    return true;

                await Task.Delay(25);

            }

            return Condition();

        }

        #endregion


        #region TheCommitsDroppedAreArchivedAndStillServed()

        [Test]
        public async Task TheCommitsDroppedAreArchivedAndStillServed()
        {

            var id       = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,                         Is.True);
            Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(id, out var history),     Is.True);

            var genesis  = history!.Root;

            for (var i = 1; i <= 6; i++)
            {

                var added = RoamingHub.AddParty(id,
                                                nameof(ChargingStationOperator),
                                                new JObject(new JProperty("@id",           $"DE*QA{i}"),
                                                            new JProperty("signingKeys",   new JArray(POIPublicKey.From(POITests.NewKey(COSECurve.P256)).ToJSON())),
                                                            new JProperty("identityKeys",  new JArray(POIPublicKey.From(POITests.NewKey(COSECurve.P521)).ToJSON()))));

                Assert.That(added.Status, Is.EqualTo(RoamingNetworkPartyStatus.Added), added.Message);

            }

            // Compacted in the background, once the log is long enough.
            var compacted     = await Eventually(() => history.Root.Id != genesis.Id && history.Archive?.Segments.Count > 0);

            var archived      = !history.TryGetCommit(genesis.Id, out _) && history.TryGetArchivedCommit(genesis.Id, out _);
            var segmentsFile  = Path.Combine(RoamingHub.POIDirectory, "QA", POIConfiguration.ArchiveDirectoryName, RoamingNetworkHistoryFileArchive.SegmentsFileName);

            using var http    = Anonymous();

            var served        = await http.GetAsync($"poi/RNs/QA/history/commits/{genesis.Id}");
            var segments      = JObject.Parse(await http.GetStringAsync("poi/RNs/QA/history/archive"))["segments"] as JArray;

            var root          = history.Root.Id;

            // After a restart, from the store and the archive beside it.
            await RoamingHub.DisposeAsync();

            var again         = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {

                Assert.That(again.POI.TryGetRoamingNetworkHistory(id, out var loaded), Is.True);

                using (Assert.EnterMultipleScope())
                {

                    Assert.That(compacted,                                                Is.True, "The history was not compacted into the archive.");
                    Assert.That(archived,                                                 Is.True, "The genesis is not in the archive.");
                    Assert.That(File.Exists(segmentsFile),                                Is.True, segmentsFile);
                    Assert.That(served.StatusCode,                                        Is.EqualTo(HttpStatusCode.OK), "An archived commit is served as before.");
                    Assert.That(segments,                                                 Is.Not.Empty);

                    Assert.That(loaded!.Root.Id,                                          Is.EqualTo(root));
                    Assert.That(loaded.Head.RoamingNetwork.ChargingStationOperators.Count(),  Is.EqualTo(6));
                    Assert.That(loaded.TryGetArchivedCommit(genesis.Id, out var fromArchive) && fromArchive.Id == genesis.Id,  Is.True,
                                "After the restart, the genesis is still in the archive.");

                }

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

    }

    #endregion

}
