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
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

// Those of POI 3, not those of WWCP_Core the OCPI side knows.
using RoamingNetwork  = cloud.charging.open.protocols.WWCP.POI.RoamingNetwork;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A hub follows a roaming network of another hub (SHAREHOLDERS.md, phase 2): a copy of it, taken only as the
    /// trust anchor lets it be trusted, kept current, served, and changed from there by nobody.
    /// </summary>
    /// <remarks>
    /// Two hubs in one test: the one of the fixture makes the roaming network and operates it, another one, started
    /// by the test, follows it over the WebSocket of its history.
    /// </remarks>
    public class FollowTests : ARoamingHubTests
    {

        #region Data

        private readonly List<(RoamingHub Hub, String Directory)> others = [];

        #endregion

        #region (helper) AnotherHub(Directory = null) / StopTheOthers()

        /// <summary>
        /// Another hub, started, in a directory of its own or in the given one, where one was before.
        /// </summary>
        private async Task<RoamingHub> AnotherHub(String? Directory = null)
        {

            var directory  = Directory ?? TestRoamingHubs.TemporaryDirectory("follower");
            var hub        = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(directory, TestRoamingHubs.Offline));

            others.Add((hub, directory));

            return hub;

        }

        [TearDown]
        public async Task StopTheOthers()
        {

            foreach (var (hub, _) in others)
                await hub.DisposeAsync();

            foreach (var directory in others.Select(other => other.Directory).Distinct())
                TestRoamingHubs.Remove(directory);

            others.Clear();

        }

        #endregion

        #region (helper) HistoryOf(Hub, Id) / TrustAnchorOf(Hub, Id) / URLOf(Hub, Id) / Until(Condition)

        private static RoamingNetworkHistory HistoryOf(RoamingHub Hub, String Id)
        {

            Assert.That(Hub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse(Id), out var history), Is.True, $"'{Id}' is not served");

            return history!;

        }

        /// <summary>
        /// The trust anchor of a roaming network a hub operates, as the text of its QR code: handed out by its operator.
        /// </summary>
        private static String TrustAnchorOf(RoamingHub Hub, String Id)
        {

            var network = HistoryOf(Hub, Id).Head.RoamingNetwork;

            return network.CreateTrustAnchor(network.Operator!.KeyStatement!, URLOf(Hub, Id)).ToQRCode();

        }

        /// <summary>
        /// Where a hub serves a roaming network: ~/RNs/{RN_Id} below its POI API.
        /// </summary>
        private static URL URLOf(RoamingHub Hub, String Id)

            => URL.Parse($"{Hub.WebInterfaceURL.ToString().TrimEnd('/')}/{RoamingHub.POIPathSegment}/RNs/{Id}");

        /// <summary>
        /// Wait for what the other server sends to arrive.
        /// </summary>
        private static async Task<Boolean> Until(Func<Boolean> Condition, Int32 Seconds = 15)
        {

            for (var tries = 0; tries < Seconds * 20; tries++)
            {

                if (Condition())
                    return true;

                await Task.Delay(50);

            }

            return Condition();

        }

        #endregion


        #region AHubFollowsARoamingNetworkOfAnother()

        /// <summary>
        /// The copy is the roaming network as its operator has it, commit by commit, also after its signing key was
        /// replaced; it is served by the hub following, listed as followed, and nothing of it is changed from there.
        /// Followed no longer, it is gone.
        /// </summary>
        [Test]
        public async Task AHubFollowsARoamingNetworkOfAnother()
        {

            Assert.That(RoamingHub.CreateRoamingNetwork(RoamingNetwork_Id.Parse("QA")).Success,  Is.True);
            Assert.That(POITests.AddedParty(RoamingHub, "QA", "DE*QAC").Success,                 Is.True);

            var follower     = await AnotherHub();
            var qa           = HistoryOf(RoamingHub, "QA");
            var id           = RoamingNetwork_Id.Parse("QA");

            // By its trust anchor alone: it names where the roaming network is.
            var result       = await follower.FollowRoamingNetwork(TrustAnchorOf(RoamingHub, "QA"));

            Assert.That(result.Status,  Is.EqualTo(RoamingNetworkFollowingStatus.Synchronized), result.Message);

            var copy         = HistoryOf(follower, "QA");
            var first        = copy.Head.Commit.Id;

            // What its operator commits afterwards arrives: a party, and its signing key replaced.
            Assert.That(POITests.AddedParty(RoamingHub, "QA", "DE*QAD").Success,                 Is.True);
            Assert.That(RoamingHub.RotateNetworkOperatorKey(POIKeyKind.Signing).Success,         Is.True);

            var caughtUp     = await Until(() => HistoryOf(follower, "QA").Head.Commit.Id == qa.Head.Commit.Id);

            // Nothing of it is changed from here: neither by this hub, which does not operate it, nor by a change set sent to it.
            var added        = POITests.AddedParty(follower, "QA", "DE*QAE");

            using var anybody  = new HttpClient { BaseAddress = new Uri(follower.WebInterfaceURL.ToString()) };

            var served       = await anybody.GetAsync("poi/RNs/QA");
            var listed       = JObject.Parse(await anybody.GetStringAsync("api/v1/poi/followed"));
            var submitted    = await anybody.SendAsync(new HttpRequestMessage(new HttpMethod("COMMIT"), "poi/RNs/QA/history/changeSets") {
                                                           Content = new StringContent("{}", Encoding.UTF8, "application/json")
                                                       });

            var again        = await follower.FollowRoamingNetwork(TrustAnchorOf(RoamingHub, "QA"), URLOf(RoamingHub, "QA"));
            var nothing      = await follower.FollowRoamingNetwork("no trust anchor");
            var made         = follower.CreateRoamingNetwork(id);

            var directory    = follower.FollowedRoamingNetworks.Single().Directory;
            var unfollowed   = await follower.UnfollowRoamingNetwork(id);
            var twice        = await follower.UnfollowRoamingNetwork(id);

            Assert.Multiple(() => {

                Assert.That(first,                                                     Is.Not.EqualTo(qa.Head.Commit.Id), "it was followed before the commits after it");
                Assert.That(caughtUp,                                                  Is.True, follower.FollowedRoamingNetworks.FirstOrDefault()?.Error ?? "the copy did not catch up");
                Assert.That(HistoryOf(RoamingHub, "QA").Head.Commit.Revision,          Is.EqualTo(3));

                Assert.That(added.Success,                                             Is.False, "this hub does not operate what it follows");
                Assert.That(submitted.StatusCode,                                      Is.Not.EqualTo(HttpStatusCode.OK).And.Not.EqualTo(HttpStatusCode.Created));

                Assert.That(served.StatusCode,                                         Is.EqualTo(HttpStatusCode.OK));
                Assert.That(listed["followed"]?[0]?["@id"]?.Value<String>(),           Is.EqualTo("QA"));
                Assert.That(listed["followed"]?[0]?["url"]?.Value<String>(),           Is.EqualTo(URLOf(RoamingHub, "QA").ToString()));
                Assert.That(listed["followed"]?[0]?["connected"]?.Value<Boolean>(),    Is.True);

                Assert.That(again.Status,                                              Is.EqualTo(RoamingNetworkFollowingStatus.Exists));
                Assert.That(nothing.Status,                                            Is.EqualTo(RoamingNetworkFollowingStatus.Invalid));
                Assert.That(made.Success,                                              Is.False, "a roaming network followed is none to be made here");

                Assert.That(unfollowed.Status,                                         Is.EqualTo(RoamingNetworkFollowingStatus.Unfollowed), unfollowed.Message);
                Assert.That(twice.Status,                                              Is.EqualTo(RoamingNetworkFollowingStatus.NotFollowed));
                Assert.That(follower.POI.TryGetRoamingNetworkHistory(id, out _),       Is.False);
                Assert.That(follower.FollowedRoamingNetworks,                          Is.Empty);
                Assert.That(System.IO.Directory.Exists(directory),                     Is.False);

            });

        }

        #endregion

        #region AHubGoesOnFollowingAfterItsRestart()

        /// <summary>
        /// What is followed is kept beside its copy: a hub started again serves the copy it has and catches up with
        /// what its operator committed in between.
        /// </summary>
        [Test]
        public async Task AHubGoesOnFollowingAfterItsRestart()
        {

            Assert.That(RoamingHub.CreateRoamingNetwork(RoamingNetwork_Id.Parse("QA")).Success,  Is.True);

            var follower   = await AnotherHub();
            var directory  = others.Single().Directory;
            var qa         = HistoryOf(RoamingHub, "QA");

            var result     = await follower.FollowRoamingNetwork(TrustAnchorOf(RoamingHub, "QA"), URLOf(RoamingHub, "QA"));

            Assert.That(result.Status,  Is.EqualTo(RoamingNetworkFollowingStatus.Synchronized), result.Message);

            await follower.DisposeAsync();
            others.Clear();

            Assert.That(POITests.AddedParty(RoamingHub, "QA", "DE*QAC").Success,  Is.True);

            var restarted  = await AnotherHub(directory);
            var followed   = restarted.FollowedRoamingNetworks;
            var caughtUp   = await Until(() => restarted.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out var copy) &&
                                               copy.Head.Commit.Id == qa.Head.Commit.Id);

            Assert.Multiple(() => {
                Assert.That(followed.Select(network => network.Id.ToString()),  Is.EqualTo(new[] { "QA" }));
                Assert.That(followed.Single().URL,                              Is.EqualTo(URLOf(RoamingHub, "QA")));
                Assert.That(caughtUp,                                           Is.True, restarted.FollowedRoamingNetworks.FirstOrDefault()?.Error ?? "the copy did not catch up");
                Assert.That(qa.Head.Commit.Revision,                            Is.EqualTo(1));
            });

        }

        #endregion

        #region NothingIsTakenThatTheTrustAnchorDoesNotLeadTo()

        /// <summary>
        /// A roaming network of the same identification at another server, operated by somebody else, is not the one a
        /// trust anchor names: no copy of it is taken, and this hub says why and keeps trying.
        /// </summary>
        [Test]
        public async Task NothingIsTakenThatTheTrustAnchorDoesNotLeadTo()
        {

            Assert.That(RoamingHub.CreateRoamingNetwork(RoamingNetwork_Id.Parse("QA")).Success,  Is.True);

            // Somebody else's QA, and its trust anchor.
            var stranger   = await AnotherHub();

            Assert.That(stranger.CreateRoamingNetwork(RoamingNetwork_Id.Parse("QA")).Success,    Is.True);

            var follower   = await AnotherHub();

            // The trust anchor of the one, the URL of the other.
            var result     = await follower.FollowRoamingNetwork(TrustAnchorOf(stranger, "QA"), URLOf(RoamingHub, "QA"), TimeSpan.FromSeconds(3));

            Assert.Multiple(() => {
                Assert.That(result.Status,                                                              Is.EqualTo(RoamingNetworkFollowingStatus.Following), result.Message);
                Assert.That(follower.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out _),  Is.False, "nothing of it is served");
                Assert.That(follower.FollowedRoamingNetworks.Single().History,                          Is.Null);
            });

        }

        #endregion

    }

}
