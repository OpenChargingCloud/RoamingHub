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
using RoamingNetwork              = cloud.charging.open.protocols.WWCP.POI.RoamingNetwork;
using ChargingStationOperator     = cloud.charging.open.protocols.WWCP.POI.ChargingStationOperator;
using ChargingStationOperator_Id  = cloud.charging.open.protocols.WWCP.POI.ChargingStationOperator_Id;

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
        /// Another hub, started, in a directory of its own or in the given one, where one was before. It looks at
        /// what waits where several hold the keys of an operator only when a test says so, unless it shall when told:
        /// most tests look at every step alone.
        /// </summary>
        private async Task<RoamingHub> AnotherHub(String? Directory = null, Boolean WhenTold = false, JObject? POI = null)
        {

            var directory      = Directory ?? TestRoamingHubs.TemporaryDirectory("follower");
            var configuration  = TestRoamingHubs.Offline;

            // What its "poi" section says, read at its start.
            if (POI is not null)
                configuration.Add("poi", POI);

            var hub        = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(directory, (JObject) configuration.DeepClone()));

            hub.LooksWhenTold = WhenTold;

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
            Assert.That(RoamingHub.RotateNetworkOperatorSigningKey().Success,         Is.True);

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

        #region ATrustAnchorFromBeforeTheHoldersChangedLeadsToTheKeysOfToday()

        /// <summary>
        /// A trust anchor handed out while one held the keys of the operator still follows the roaming network once
        /// two hold them: the hub following asks for the key statements of the operator before its first copy, and
        /// each is signed by the quorum of the one before it (SPECIFICATION.md of WWCP POI, section 8.6). The trust
        /// anchor kept beside the copy is the one of today then. One of another operator leads nowhere, as before.
        /// </summary>
        [Test]
        public async Task ATrustAnchorFromBeforeTheHoldersChangedLeadsToTheKeysOfToday()
        {

            var id        = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            // Handed out now: one holder, one deciding.
            var old       = TrustAnchorOf(RoamingHub, "QA");
            var qa        = HistoryOf(RoamingHub, "QA");

            var second    = await Followed("QA", WhenTold: true);
            var beta      = second.NetworkOperator;

            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,      Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await RoamingHub.AdmitShareholder(id, beta.Holder!)).Success,  Is.True);
            Assert.That(await Until(() => HoldersOf(RoamingHub, "QA").Length == 2),      Is.True, "the hub admitted did not prove its key by itself");

            // Two decide, so a first copy is given once both signed and attested its head.
            Assert.That(await Until(() => qa.CurrentAttestations().Any(attestation => attestation.CommitId == qa.Head.Commit.Id &&
                                                                                      attestation.KeyId    == beta.SigningKey.Id)),
                        Is.True, "the head is not attested by the hub admitted");

            var third     = await AnotherHub();
            var following = await third.FollowRoamingNetwork(old);
            var followed  = third.FollowedRoamingNetworks.Single();
            var kept      = File.ReadAllText(Path.Combine(followed.Directory, RoamingHub.FollowedFileName));

            // Another operator's roaming network of the same name, at the same place: its key statements lead nowhere.
            var stranger  = await AnotherHub();

            Assert.That(stranger.CreateRoamingNetwork(id).Success,  Is.True);

            var fourth    = await AnotherHub();
            var nowhere   = await fourth.FollowRoamingNetwork(TrustAnchorOf(stranger, "QA"), URLOf(RoamingHub, "QA"), TimeSpan.FromSeconds(3));

            Assert.Multiple(() => {

                Assert.That(following.Status,                          Is.EqualTo(RoamingNetworkFollowingStatus.Synchronized), following.Message);
                Assert.That(followed.History?.Head.Commit.Id,          Is.EqualTo(qa.Head.Commit.Id));
                Assert.That(followed.Trust.TrustAnchor.Holders.Count,  Is.EqualTo(2), "the trust anchor was not led to the keys of today");
                Assert.That(followed.Trust.TrustAnchor.MinConsensus,   Is.EqualTo(2));
                Assert.That(kept,                                      Does.Contain(followed.Trust.TrustAnchor.ToQRCode()), "the trust anchor of today is not kept beside the copy");
                Assert.That(kept,                                      Does.Not.Contain(old));

                Assert.That(nowhere.Status,                            Is.EqualTo(RoamingNetworkFollowingStatus.Following), nowhere.Message);
                Assert.That(fourth.FollowedRoamingNetworks.Single().History,  Is.Null);
                Assert.That(fourth.FollowedRoamingNetworks.Single().Trust.TrustAnchor.Holders.Count,  Is.EqualTo(1));

            });

        }

        #endregion

        #region AShareholderSignsTheCommitsItFollows()

        /// <summary>
        /// A hub holding keys of the operator of a roaming network it follows signs a commit of it when told to
        /// (SHAREHOLDERS.md, phase 3): the signature is taken where it is followed, and the commit is signed by both
        /// holders, there and in the copy. A hub that holds no key of the operator signs nothing.
        /// </summary>
        /// <remarks>
        /// The hub following is admitted here by hand, with what each hub signs as a shareholder; the commands asking
        /// and admitting are phase 4.
        /// </remarks>
        [Test]
        public async Task AShareholderSignsTheCommitsItFollows()
        {

            var id         = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            var follower   = await AnotherHub();
            var alpha      = RoamingHub.NetworkOperator;
            var beta       = follower.  NetworkOperator;
            var qa         = HistoryOf(RoamingHub, "QA");

            var following  = await follower.FollowRoamingNetwork(TrustAnchorOf(RoamingHub, "QA"));

            Assert.That(following.Status,  Is.EqualTo(RoamingNetworkFollowingStatus.Synchronized), following.Message);

            // No shareholder yet: nothing to sign with.
            var notYet     = await follower.SignFollowedCommit(id);

            #region The hub following is admitted: two of two

            var now        = org.GraphDefined.Vanaheimr.Illias.Timestamp.Now;
            var head       = qa.Head.RoamingNetwork;
            var operatorId = head.Operator!.Id;

            var statement  = beta.CoSign(alpha.StateKeysOf(id, operatorId, [ alpha.IdentityKey, beta.IdentityKey ], 2, now, TimeSpan.FromDays(30)));

            var admission  = beta.ProveIdentityKey(
                                 alpha.SignAsShareholder(
                                     alpha.SignAsShareholder(
                                         head.Admit(beta.IdentityKey, beta.CertifySigningKeyFor(operatorId, now), statement),
                                         POIKeyKind.Identity
                                     ),
                                     POIKeyKind.Signing
                                 ),
                                 beta.IdentityKey
                             );

            var admitted   = qa.Commit(admission);

            Assert.That(admitted.IsCommitted,  Is.True, admitted.ErrorResponse);

            #endregion

            // The copy follows it: the key statement of both is signed by the one trusted so far.
            var caughtUp   = await Until(() => HistoryOf(follower, "QA").Head.Commit.Id == qa.Head.Commit.Id);

            Assert.That(caughtUp,  Is.True, follower.FollowedRoamingNetworks.FirstOrDefault()?.Error ?? "the copy did not catch up");

            var before     = RoamingHub.OperatorSignersOf(qa.Head.Commit, qa.Head.RoamingNetwork);

            // What whoever checks the roaming network pins now: both holders, two deciding.
            var trustAnchor  = qa.Head.RoamingNetwork.CreateTrustAnchor(statement, URLOf(RoamingHub, "QA"));

            Boolean AttestedByTheFollower()
                => qa.CurrentAttestations().Any(attestation => attestation.CommitId == qa.Head.Commit.Id &&
                                                               attestation.KeyId    == beta.SigningKey.Id);

            // A head nobody signed here is not attested by itself, whatever the timer does.
            follower.KeepAttesting();
            await Task.Delay(500);

            var unsignedAttested  = AttestedByTheFollower();
            var vouchedByOne      = qa.VerifyHead(trustAnchor, out _);

            var signed     = await follower.SignFollowedCommit(id);
            var arrived    = await Until(() => qa.Head.Commit.Signatures.Length == 2);

            // Signed, so attested: by both holders now, as the quorum requires.
            var attested   = await Until(AttestedByTheFollower);
            var vouched    = qa.VerifyHead(trustAnchor, out var vouchingError);
            var again      = await follower.SignFollowedCommit(id);

            var unknown    = await follower.SignFollowedCommit(RoamingNetwork_Id.Parse("PROD"));
            var itsOwn     = await RoamingHub.SignFollowedCommit(id);

            Assert.Multiple(() => {

                Assert.That(notYet.Status,                                                    Is.EqualTo(RoamingNetworkCommitSigningStatus.NoShareholder), notYet.Message);

                Assert.That(qa.Head.RoamingNetwork.Operator!.MinConsensus,                    Is.EqualTo(2));
                Assert.That(before,                                                           Is.EqualTo(new[] { alpha.Holder }), "its commit is signed where it was made");

                Assert.That(signed.Status,                                                    Is.EqualTo(RoamingNetworkCommitSigningStatus.Signed), signed.Message);
                Assert.That(arrived,                                                          Is.True, "the signature did not arrive where the roaming network is followed");

                Assert.That(RoamingHub.OperatorSignersOf(qa.Head.Commit, qa.Head.RoamingNetwork),
                            Is.EquivalentTo(new[] { alpha.Holder, beta.Holder }));

                Assert.That(RoamingHub.OperatorSignersOf(HistoryOf(follower, "QA").Head.Commit, HistoryOf(follower, "QA").Head.RoamingNetwork),
                            Is.EquivalentTo(new[] { alpha.Holder, beta.Holder }), "in the copy as well");

                Assert.That(unsignedAttested,                                                 Is.False, "a head this hub did not sign is not attested by it");
                Assert.That(vouchedByOne,                                                     Is.False, "one of two holders attesting is no quorum");
                Assert.That(attested,                                                         Is.True,  "the attestation did not arrive where the roaming network is followed");
                Assert.That(vouched,                                                          Is.True,  vouchingError);

                Assert.That(again.Status,                                                     Is.EqualTo(RoamingNetworkCommitSigningStatus.AlreadySigned));
                Assert.That(unknown.Status,                                                   Is.EqualTo(RoamingNetworkCommitSigningStatus.NotFollowed));
                Assert.That(itsOwn.Status,                                                    Is.EqualTo(RoamingNetworkCommitSigningStatus.NotFollowed), "a hub signs what it commits itself");

            });

        }

        #endregion


        #region (helper) Followed(Id) / HoldersOf(Hub, Id) / CaughtUp(Id, Followers)

        /// <summary>
        /// Another hub following the given roaming network of the hub of the fixture.
        /// </summary>
        private async Task<RoamingHub> Followed(String Id, Boolean WhenTold = false)
        {

            var follower  = await AnotherHub(WhenTold: WhenTold);
            var result    = await follower.FollowRoamingNetwork(TrustAnchorOf(RoamingHub, Id));

            Assert.That(result.Status,  Is.EqualTo(RoamingNetworkFollowingStatus.Synchronized), result.Message);

            return follower;

        }

        private static String[] HoldersOf(RoamingHub Hub, String Id)

            => [ .. POIPartyKeys.Holders(HistoryOf(Hub, Id).Head.RoamingNetwork.Operator!.IdentityKeys).Order(StringComparer.Ordinal) ];

        /// <summary>
        /// Wait for the copies to be where the roaming network is.
        /// </summary>
        private async Task CaughtUp(String Id, params RoamingHub[] Followers)
        {

            foreach (var follower in Followers)
                Assert.That(await Until(() => HistoryOf(follower, Id).Head.Commit.Id == HistoryOf(RoamingHub, Id).Head.Commit.Id),
                            Is.True,
                            follower.FollowedRoamingNetworks.FirstOrDefault()?.Error ?? "a copy did not catch up");

        }

        #endregion

        #region AHubAsksIsAdmittedAndResigns()

        /// <summary>
        /// A hub following a roaming network asks to hold the keys of its operator as well, the hub that makes it admits
        /// it, and it signs the change set admitting it: two of two (SHAREHOLDERS.md, phase 4). Each step is one command
        /// at one hub here; what a hub does of them by itself is the test after the next. Then it resigns: dismissed by
        /// the one staying, signed by both.
        /// </summary>
        [Test]
        public async Task AHubAsksIsAdmittedAndResigns()
        {

            var id         = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            var follower   = await Followed("QA");
            var alpha      = RoamingHub.NetworkOperator.Holder!;
            var beta       = follower.  NetworkOperator.Holder!;

            var nobody     = await RoamingHub.AdmitShareholder(id, beta);

            var asked      = await follower.  JoinRoamingNetworkOperator(id);
            var askedAgain = await follower.  JoinRoamingNetworkOperator(id);
            var waiting    = RoamingHub.ShareholdersOf(id);
            var stillOne   = HoldersOf(RoamingHub, "QA");

            // As the pages of both hubs show it: where it is made, and where it is a copy, whose changes waiting
            // are asked where it is made.
            var seenThere  = await RoamingHub.ShareholdersSeenOf(id);
            var seenHere   = await follower.  ShareholdersSeenOf(id);

            var admitted   = await RoamingHub.AdmitShareholder(id, beta);
            var notYet     = HoldersOf(RoamingHub, "QA");

            var joined     = await follower.  JoinRoamingNetworkOperator(id);
            var both       = HoldersOf(RoamingHub, "QA");
            var quorum     = HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.Operator!.MinConsensus;

            await CaughtUp("QA", follower);

            var holdsKeys  = follower.NetworkOperator.SigningKeyIdIn(HistoryOf(follower, "QA").Head.RoamingNetwork) is not null;
            var seenHeld   = await follower.  ShareholdersSeenOf(id);
            var already    = await follower.  JoinRoamingNetworkOperator(id);

            // It resigns: begun where the roaming network is made, by the one staying, signed by the one leaving as well.
            var dismissed  = await RoamingHub.DismissShareholder(id, beta);
            var twoStill   = HoldersOf(RoamingHub, "QA");
            var resigned   = await follower.  DismissShareholder(id, beta);
            var alone      = HoldersOf(RoamingHub, "QA");

            Assert.Multiple(() => {

                Assert.That(nobody.Status,       Is.EqualTo(ShareholderChangeStatus.NotFound),     nobody.Message);

                Assert.That(asked.Status,        Is.EqualTo(ShareholderChangeStatus.Waiting),      asked.Message);
                Assert.That(askedAgain.Status,   Is.EqualTo(ShareholderChangeStatus.Waiting),      askedAgain.Message);
                Assert.That(waiting?["changes"]?[0]?["holder"]?.Value<String>(),   Is.EqualTo(beta));
                Assert.That(waiting?["changes"]?[0]?["kind"]?.Value<String>(),     Is.EqualTo("admit"));
                Assert.That(stillOne,            Is.EqualTo(new[] { alpha }),                      "asking admits nobody");

                Assert.That(seenThere?["followed"]?.Value<Boolean>(),                Is.False);
                Assert.That(seenThere?["thisHub"]?.Value<String>(),                  Is.EqualTo(alpha));
                Assert.That(seenThere?["changes"]?[0]?["holder"]?.Value<String>(),   Is.EqualTo(beta));
                Assert.That(seenHere?["followed"]?.Value<Boolean>(),                 Is.True);
                Assert.That(seenHere?["thisHub"],                                    Is.Null,          "a hub asking is no holder yet");
                Assert.That(seenHere?["holders"]?.Count(),                           Is.EqualTo(1));
                Assert.That(seenHere?["changes"]?[0]?["holder"]?.Value<String>(),    Is.EqualTo(beta), "what waits is asked where the roaming network is made");
                Assert.That(seenHeld?["thisHub"]?.Value<String>(),                   Is.EqualTo(beta));
                Assert.That(seenHeld?["holders"]?.Count(),                           Is.EqualTo(2));

                Assert.That(admitted.Status,     Is.EqualTo(ShareholderChangeStatus.Waiting),      admitted.Message);
                Assert.That(notYet,              Is.EqualTo(new[] { alpha }),                      "without the proof of the one asking, nobody is admitted");

                Assert.That(joined.Status,       Is.EqualTo(ShareholderChangeStatus.Committed),    joined.Message);
                Assert.That(both,                Is.EquivalentTo(new[] { alpha, beta }));
                Assert.That(quorum,              Is.EqualTo(2));
                Assert.That(holdsKeys,           Is.True,                                          "the copy names the keys of the hub following");
                Assert.That(already.Status,      Is.EqualTo(ShareholderChangeStatus.NothingToDo),  already.Message);

                Assert.That(dismissed.Status,    Is.EqualTo(ShareholderChangeStatus.Waiting),      dismissed.Message);
                Assert.That(twoStill,            Is.EquivalentTo(new[] { alpha, beta }),           "of two, nobody is dismissed by the other one alone");
                Assert.That(resigned.Status,     Is.EqualTo(ShareholderChangeStatus.Committed),    resigned.Message);
                Assert.That(alone,               Is.EqualTo(new[] { alpha }));
                Assert.That(HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.Operator!.MinConsensus,  Is.EqualTo(1));

            });

        }

        #endregion

        #region TwoAdmitAThirdAndDismissIt()

        /// <summary>
        /// Where two decide, both sign: the key statement of the three first, each at its own hub, then the change set
        /// the hub of the roaming network makes of it, and the one asking its proof. Two of three afterwards; and two of
        /// the three dismiss the third without it. Each of the three decides once (SHAREHOLDERS.md, phase 6): what
        /// follows from that the hubs do by themselves - the change set made and signed, the proof of the one asking,
        /// and the commit signed by all who signed its change set.
        /// </summary>
        [Test]
        public async Task TwoAdmitAThirdAndDismissIt()
        {

            var id       = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            var second   = await Followed("QA");
            var alpha    = RoamingHub.NetworkOperator.Holder!;
            var beta     = second.    NetworkOperator.Holder!;

            // The second one, as in the test before.
            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,  Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await RoamingHub.AdmitShareholder(id, beta)).Status,      Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,  Is.EqualTo(ShareholderChangeStatus.Committed));

            await CaughtUp("QA", second);

            // A head of two holders is one both vouch for: the second signs it, and attests it from then on. Only then a
            // third hub gets a first copy, which is anchored in the attestations of as many holders as decide.
            // It signed the change set admitting it, so it signs its commit by itself, when it looks.
            var signedByItself = await second.AdvanceShareholders();

            Assert.That(signedByItself,                                Has.Count.EqualTo(1), String.Join(" ", signedByItself));
            Assert.That((await second.SignFollowedCommit(id)).Status,  Is.EqualTo(RoamingNetworkCommitSigningStatus.AlreadySigned));

            Assert.That(await Until(() => HistoryOf(RoamingHub, "QA").CurrentAttestations().Any(attestation => attestation.CommitId == HistoryOf(RoamingHub, "QA").Head.Commit.Id &&
                                                                                                               attestation.KeyId    == second.NetworkOperator.SigningKey.Id)),
                        Is.True, "the attestation of the second holder did not arrive");

            var third    = await Followed("QA");
            var gamma    = third.NetworkOperator.Holder!;

            #region The third one is admitted by both

            var asked        = await third.     JoinRoamingNetworkOperator(id);
            var nothingYet   = await third.     AdvanceShareholders();          // nobody admitted it yet
            var undecided    = await second.    AdvanceShareholders();          // admitting is for somebody to say
            var byAlpha      = await RoamingHub.AdmitShareholder(id, gamma);    // signs the key statement: one of two
            var stillTwo     = HoldersOf(RoamingHub, "QA");
            var byBeta       = await second.    AdmitShareholder(id, gamma);    // signs it as well: its change set is made there, and signed here
            var notYetThree  = HoldersOf(RoamingHub, "QA");
            var proved       = await third.     AdvanceShareholders();          // its proof, by itself: it asked
            var three        = HoldersOf(RoamingHub, "QA");

            await CaughtUp("QA", second, third);

            // And the commit, by the two that follow it: both signed its change set.
            var commitByBeta  = await second.AdvanceShareholders();
            var commitByGamma = await third. AdvanceShareholders();
            var signedByAll   = await Until(() => RoamingHub.OperatorSignersOf(HistoryOf(RoamingHub, "QA").Head.Commit, HistoryOf(RoamingHub, "QA").Head.RoamingNetwork).Count == 3);
            var nothingMore   = await second.AdvanceShareholders();

            #endregion

            #region And dismissed by both, without it

            var outByAlpha   = await RoamingHub.DismissShareholder(id, gamma);
            var outByBeta    = await second.    DismissShareholder(id, gamma);  // its change set is made there, and signed here: committed
            var two          = HoldersOf(RoamingHub, "QA");

            #endregion

            Assert.Multiple(() => {

                Assert.That(asked.Status,         Is.EqualTo(ShareholderChangeStatus.Waiting),    asked.Message);
                Assert.That(byAlpha.Status,       Is.EqualTo(ShareholderChangeStatus.Waiting),    byAlpha.Message);
                Assert.That(byAlpha.Message,      Does.Contain("waits for the other holders"));
                Assert.That(nothingYet,           Is.Empty,                                       "a hub asking does nothing more by itself while nobody admitted it");
                Assert.That(undecided,            Is.Empty,                                       "no hub admits by itself");
                Assert.That(stillTwo,             Is.EquivalentTo(new[] { alpha, beta }));
                Assert.That(byBeta.Status,        Is.EqualTo(ShareholderChangeStatus.Waiting),    byBeta.Message);
                Assert.That(byBeta.Message,       Does.Contain("is signed by this hub. It waits for signatures"), "the change set was made where the roaming network is made, and signed here");
                Assert.That(notYetThree,          Is.EquivalentTo(new[] { alpha, beta }),         "without the proof of the one asking, nobody is admitted");
                Assert.That(proved.FirstOrDefault(), Does.Contain("committed"),                 String.Join(" ", proved));

                Assert.That(three,                Is.EquivalentTo(new[] { alpha, beta, gamma }));

                Assert.That(commitByBeta,         Has.Count.EqualTo(1),                           String.Join(" ", commitByBeta));
                Assert.That(proved.Count + commitByGamma.Count,  Is.EqualTo(2),                   "the one admitted signs the commit admitting it, once its copy has it");
                Assert.That(signedByAll,          Is.True,                                        "the commit admitting the third is not signed by all three");
                Assert.That(nothingMore,          Is.Empty,                                       "asked again, a hub has nothing to go on with");

                Assert.That(outByAlpha.Status,    Is.EqualTo(ShareholderChangeStatus.Waiting),    outByAlpha.Message);
                Assert.That(outByBeta.Status,     Is.EqualTo(ShareholderChangeStatus.Committed),  outByBeta.Message);

                Assert.That(two,                  Is.EquivalentTo(new[] { alpha, beta }));
                Assert.That(HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.Operator!.MinConsensus,  Is.EqualTo(2));

            });

        }

        #endregion

        #region ToldTheHubsGoOnAtOnce()

        /// <summary>
        /// The hub where the roaming network is made tells those following it what waits (SHAREHOLDERS.md, phase 6.2):
        /// a notice over the WebSocket of its history. So somebody decides once per change at each hub - asking,
        /// admitting, dismissing - and everything else the hubs do at once, by themselves: nobody says anything twice,
        /// and nobody waits for the timer.
        /// </summary>
        [Test]
        public async Task ToldTheHubsGoOnAtOnce()
        {

            var id       = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            Boolean SignedBy(Int32 Holders)
            {
                var head = HistoryOf(RoamingHub, "QA").Head;
                return HoldersOf(RoamingHub, "QA").Length == Holders &&
                       RoamingHub.OperatorSignersOf(head.Commit, head.RoamingNetwork).Count == Holders;
            }

            Boolean AttestedBy(RoamingHub Hub)
            {
                var history = HistoryOf(RoamingHub, "QA");
                return history.CurrentAttestations().Any(attestation => attestation.CommitId == history.Head.Commit.Id &&
                                                                        attestation.KeyId    == Hub.NetworkOperator.SigningKey.Id);
            }

            var second   = await Followed("QA", WhenTold: true);
            var alpha    = RoamingHub.NetworkOperator.Holder!;
            var beta     = second.    NetworkOperator.Holder!;

            #region The second one: asked there, admitted here

            var askedByBeta    = await second.    JoinRoamingNetworkOperator(id);
            var betaAdmitted   = await RoamingHub.AdmitShareholder(id, beta);

            // Its proof, and its signature of the commit admitting it, which it attests from then on.
            var signedByTwo    = await Until(() => SignedBy(2));
            var attestedByTwo  = await Until(() => AttestedBy(second));

            #endregion

            #region The third one: asked there, admitted at both

            var third    = await Followed("QA", WhenTold: true);
            var gamma    = third.NetworkOperator.Holder!;

            var askedByGamma   = await third.     JoinRoamingNetworkOperator(id);
            var byAlpha        = await RoamingHub.AdmitShareholder(id, gamma);
            var byBeta         = await second.    AdmitShareholder(id, gamma);

            var signedByThree  = await Until(() => SignedBy(3));
            var three          = HoldersOf(RoamingHub, "QA");

            #endregion

            #region And dismissed by both

            var outByAlpha     = await RoamingHub.DismissShareholder(id, gamma);
            var outByBeta      = await second.    DismissShareholder(id, gamma);

            var signedByTwoAgain  = await Until(() => SignedBy(2));

            #endregion

            Assert.Multiple(() => {

                Assert.That(askedByBeta.Status,   Is.EqualTo(ShareholderChangeStatus.Waiting),    askedByBeta.Message);
                Assert.That(betaAdmitted.Success, Is.True,                                        betaAdmitted.Message);
                Assert.That(signedByTwo,          Is.True,                                        "the second did not prove its key and sign the commit admitting it by itself");
                Assert.That(attestedByTwo,        Is.True,                                        "the second does not attest the head it signed");

                Assert.That(askedByGamma.Status,  Is.EqualTo(ShareholderChangeStatus.Waiting),    askedByGamma.Message);
                Assert.That(byAlpha.Status,       Is.EqualTo(ShareholderChangeStatus.Waiting),    byAlpha.Message);
                Assert.That(byBeta.Success,       Is.True,                                        byBeta.Message);
                Assert.That(signedByThree,        Is.True,                                        "the third was not admitted, or the commit admitting it is not signed by all three");
                Assert.That(three,                Is.EquivalentTo(new[] { alpha, beta, gamma }));

                Assert.That(outByAlpha.Status,    Is.EqualTo(ShareholderChangeStatus.Waiting),    outByAlpha.Message);
                Assert.That(outByBeta.Status,     Is.EqualTo(ShareholderChangeStatus.Committed),  outByBeta.Message);
                Assert.That(signedByTwoAgain,     Is.True,                                        "the commit dismissing the third is not signed by the two left");
                Assert.That(HoldersOf(RoamingHub, "QA"),  Is.EquivalentTo(new[] { alpha, beta }));

            });

        }

        #endregion

        #region TheConfigurationSaysWhatAHubTakesPartIn()

        /// <summary>
        /// The "poi" section says where a hub is reached, which roaming networks it follows, and of which it asks to
        /// hold the keys of the operator as well (SHAREHOLDERS.md, phase 6.3): done at its start, as if somebody had
        /// said it then. So one decision is left, to admit it; and asking is done once, also over a restart.
        /// </summary>
        [Test]
        public async Task TheConfigurationSaysWhatAHubTakesPartIn()
        {

            var id       = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            var poi      = new JObject(
                               new JProperty("url",     "https://hub-b.example/poi"),
                               new JProperty("follow",  new JArray(
                                   new JObject(
                                       new JProperty("trustAnchor",  TrustAnchorOf(RoamingHub, "QA")),
                                       new JProperty("hold",         true)
                                   )
                               ))
                           );

            var second     = await AnotherHub(WhenTold: true, POI: poi);
            var directory  = others.Single().Directory;
            var beta       = second.NetworkOperator.Holder!;

            // Followed, and asked, by itself.
            var followed   = await Until(() => second.IsFollowed(id) && second.FollowedRoamingNetworks.Single().History is not null);
            var asked      = await Until(() => RoamingHub.ShareholdersOf(id)?["changes"] is JArray changes &&
                                               changes.Any(change => change["holder"]?.Value<String>() == beta));

            // The one decision.
            var admitted   = await RoamingHub.AdmitShareholder(id, beta);
            var two        = await Until(() => HoldersOf(RoamingHub, "QA").Length == 2);
            var urls       = HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.URLs.Select(url => url.ToString()).ToArray();
            var keptAsked  = File.ReadAllText(Path.Combine(directory, RoamingHub.POIDirectoryName, "QA", RoamingHub.FollowedFileName));

            await CaughtUp("QA", second);

            // Its trust anchor of the copy names where the hub says it is reached, not where it was asked.
            using var http = new HttpClient();
            var anchorText = await http.GetStringAsync($"{URLOf(second, "QA")}/trustAnchor");

            // Started again, the same configuration: nothing is followed twice, and nothing asked again.
            await second.DisposeAsync();
            others.Clear();

            var restarted  = await AnotherHub(directory, WhenTold: true, POI: poi);
            var again      = await restarted.FollowAsConfigured();
            var looked     = await restarted.AdvanceShareholders();

            Assert.Multiple(() => {

                Assert.That(followed,           Is.True,                                        "the roaming network the configuration names is not followed");
                Assert.That(asked,              Is.True,                                        "the hub did not ask to hold the keys as its configuration says");
                Assert.That(admitted.Success,   Is.True,                                        admitted.Message);
                Assert.That(two,                Is.True,                                        "the hub admitted did not prove its key by itself");
                Assert.That(urls,               Does.Contain("https://hub-b.example/poi/RNs/QA"), "the roaming network does not name the copy of the hub admitted");
                Assert.That(keptAsked,          Does.Contain("askedToHold"));
                Assert.That(anchorText,         Does.Contain("hub-b.example"));

                Assert.That(again,              Is.Empty,                                       String.Join(" ", again));
                Assert.That(looked.Where(done => done.Contains("configuration")),  Is.Empty,   String.Join(" ", looked));
                Assert.That(restarted.FollowedRoamingNetworks.Select(network => network.Id.ToString()),  Is.EqualTo(new[] { "QA" }));

            });

        }

        #endregion

        #region ARoamingNetworkMadeNamesWhereItsHubIsReached()

        /// <summary>
        /// A roaming network made at a hub that says where it is reached names that URL from its genesis on; one made
        /// at a hub that does not, names none.
        /// </summary>
        [Test]
        public async Task ARoamingNetworkMadeNamesWhereItsHubIsReached()
        {

            var reached  = await AnotherHub(POI: new JObject(new JProperty("url", "https://hub-c.example/poi/")));

            Assert.That(reached.   CreateRoamingNetwork(RoamingNetwork_Id.Parse("QC")).Success,  Is.True);
            Assert.That(RoamingHub.CreateRoamingNetwork(RoamingNetwork_Id.Parse("QA")).Success,  Is.True);

            Assert.Multiple(() => {
                Assert.That(HistoryOf(reached,    "QC").Head.RoamingNetwork.URLs.Select(url => url.ToString()),  Is.EqualTo(new[] { "https://hub-c.example/poi/RNs/QC" }));
                Assert.That(HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.URLs,                                Is.Empty);
            });

        }

        #endregion

        #region TheConfigurationOfWhatIsFollowedIsChecked()

        /// <summary>
        /// What the "poi" section says of where the hub is reached and of what it follows is read as it is written,
        /// or refused with one sentence.
        /// </summary>
        [Test]
        public void TheConfigurationOfWhatIsFollowedIsChecked()
        {

            Assert.That(RoamingHub.CreateRoamingNetwork(RoamingNetwork_Id.Parse("QA")).Success,  Is.True);

            var anchor = TrustAnchorOf(RoamingHub, "QA");

            static (Boolean Read, global::cloud.charging.open.RoamingHub.Configuration.POIConfiguration? POI, String? Error) Parsed(JObject POI)
            {
                var read = global::cloud.charging.open.RoamingHub.Configuration.RoamingHubConfiguration.TryParse(new JObject(new JProperty("poi", POI)), out var configuration, out var error);
                return (read, configuration?.POI, error);
            }

            JObject Follow(params JProperty[] Properties)
                => new (new JProperty("follow", new JArray(new JObject(Properties))));

            var good       = Parsed(new JObject(
                                 new JProperty("url",     "https://hub-b.example/poi/"),
                                 new JProperty("follow",  new JArray(new JObject(
                                     new JProperty("trustAnchor",  anchor),
                                     new JProperty("url",          URLOf(RoamingHub, "QA").ToString()),
                                     new JProperty("hold",         true),
                                     new JProperty("signCommits",  "all")
                                 )))
                             ));

            var approved   = Parsed(Follow(new JProperty("trustAnchor", anchor)));
            var some       = Parsed(Follow(new JProperty("trustAnchor", anchor), new JProperty("signCommits", "some")));

            var noURL      = Parsed(new JObject(new JProperty("url", "hub-b.example")));
            var query      = Parsed(new JObject(new JProperty("url", "https://hub-b.example/poi?x=1")));
            var noArray    = Parsed(new JObject(new JProperty("follow", "QA")));
            var noAnchor   = Parsed(Follow(new JProperty("trustAnchor", "QA")));
            var missing    = Parsed(Follow(new JProperty("hold", true)));
            var unknown    = Parsed(Follow(new JProperty("trustAnchor", anchor), new JProperty("admit", true)));
            var twice      = Parsed(new JObject(new JProperty("follow", new JArray(new JObject(new JProperty("trustAnchor", anchor)),
                                                                                   new JObject(new JProperty("trustAnchor", anchor))))));

            Assert.Multiple(() => {

                Assert.That(good.Read,                                          Is.True, good.Error);
                Assert.That(good.POI!.URL.ToString(),                           Is.EqualTo("https://hub-b.example/poi"));
                Assert.That(good.POI.URLOf(RoamingNetwork_Id.Parse("QA")).ToString(),  Is.EqualTo("https://hub-b.example/poi/RNs/QA"));
                Assert.That(good.POI.EffectiveFollow.Single().TrustAnchor.Id,   Is.EqualTo(RoamingNetwork_Id.Parse("QA")));
                Assert.That(good.POI.EffectiveFollow.Single().Hold,             Is.True);
                Assert.That(good.POI.EffectiveFollow.Single().SignCommits,      Is.EqualTo(global::cloud.charging.open.RoamingHub.Configuration.POISignCommits.All));
                Assert.That(approved.POI!.SignCommitsOf(RoamingNetwork_Id.Parse("QA")),  Is.EqualTo(global::cloud.charging.open.RoamingHub.Configuration.POISignCommits.Approved), "what a hub signed the change set of, unless said otherwise");
                Assert.That(approved.POI.SignCommitsOf(RoamingNetwork_Id.Parse("QB")),   Is.EqualTo(global::cloud.charging.open.RoamingHub.Configuration.POISignCommits.Approved), "also of what the section does not name");
                Assert.That(good.POI.EffectiveFollow.Single().URL,              Is.EqualTo(URLOf(RoamingHub, "QA")));
                Assert.That(Parsed(good.POI.ToJSON()).POI!.ToJSON().ToString(), Is.EqualTo(good.POI.ToJSON().ToString()), "written and read again, it is the same");

                Assert.That(noURL.Read,     Is.False);  Assert.That(noURL.Error,     Does.Contain("poi.url"));
                Assert.That(query.Read,     Is.False);  Assert.That(query.Error,     Does.Contain("poi.url"));
                Assert.That(noArray.Read,   Is.False);  Assert.That(noArray.Error,   Does.Contain("poi.follow"));
                Assert.That(noAnchor.Read,  Is.False);  Assert.That(noAnchor.Error,  Does.Contain("poi.follow[0].trustAnchor"));
                Assert.That(missing.Read,   Is.False);  Assert.That(missing.Error,   Does.Contain("poi.follow[0].trustAnchor"));
                Assert.That(unknown.Read,   Is.False);  Assert.That(unknown.Error,   Does.Contain("'admit'"));
                Assert.That(some.Read,      Is.False);  Assert.That(some.Error,      Does.Contain("poi.follow[0].signCommits"));
                Assert.That(twice.Read,     Is.False);  Assert.That(twice.Error,     Does.Contain("twice"));

            });

        }

        #endregion

        #region TheConfigurationSaysWhichCommitsAHubSigns(SignCommits, Admission, Others)

        /// <summary>
        /// "signCommits" beside the trust anchor of a roaming network followed says which of its commits a hub signs
        /// by itself as one of the holders of the keys of its operator (SHAREHOLDERS.md, phase 6.4): none; those of
        /// the change sets it signed, as before and where nothing is said; or all it took. The commit admitting it is
        /// one it approved, and the other holder renewing its signing key alone is one it did not.
        /// </summary>
        [TestCase("none",      false, false)]
        [TestCase(null,        true,  false)]
        [TestCase("approved",  true,  false)]
        [TestCase("all",       true,  true)]
        public async Task TheConfigurationSaysWhichCommitsAHubSigns(String? SignCommits, Boolean Admission, Boolean Others)
        {

            var id        = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            var qa        = HistoryOf(RoamingHub, "QA");
            var before    = qa.Head.Commit.Id;
            var entry     = new JObject(
                                new JProperty("trustAnchor",  TrustAnchorOf(RoamingHub, "QA")),
                                new JProperty("hold",         true)
                            );

            if (SignCommits is not null)
                entry.Add("signCommits", SignCommits);

            var second    = await AnotherHub(WhenTold: true, POI: new JObject(new JProperty("follow", new JArray(entry))));
            var beta      = second.NetworkOperator.Holder!;

            Boolean SignedBySecond(RoamingNetworkCommit Commit)
                => RoamingHub.OperatorSignersOf(Commit, qa.Head.RoamingNetwork).Contains(beta);

            Assert.That(await Until(() => RoamingHub.ShareholdersOf(id)?["changes"] is JArray changes &&
                                          changes.Any(change => change["holder"]?.Value<String>() == beta)),
                        Is.True, "the hub did not ask to hold the keys as its configuration says");

            var admitted  = await RoamingHub.AdmitShareholder(id, beta);

            Assert.That(admitted.Success,                                         Is.True, admitted.Message);
            Assert.That(await Until(() => HoldersOf(RoamingHub, "QA").Length == 2),  Is.True, "the hub admitted did not prove its key by itself");

            await CaughtUp("QA", second);

            // The commit admitting it: of a change set it signed.
            var admission       = qa.Head.Commit.Id;
            var admissionSigned = await Until(() => SignedBySecond(qa.Head.Commit), Admission ? 15 : 1);

            // A commit it signed no change set of: the other holder renews its signing key, alone.
            var renewed   = await RoamingHub.RotateNetworkOperatorKeyEverywhere(POIKeyKind.Signing);

            Assert.That(renewed.Success,     Is.True, renewed.Message);
            Assert.That(qa.Head.Commit.Id,   Is.Not.EqualTo(admission));

            await CaughtUp("QA", second);

            var othersSigned  = await Until(() => SignedBySecond(qa.Head.Commit), Others ? 15 : 1);
            var looked        = await second.AdvanceShareholders();

            Assert.That(qa.TryGetCommit(before,    out var genesis),   Is.True);
            Assert.That(qa.TryGetCommit(admission, out var admitting), Is.True);

            Assert.Multiple(() => {

                Assert.That(admissionSigned,             Is.EqualTo(Admission),  "the commit of the change set admitting it");
                Assert.That(SignedBySecond(admitting!),  Is.EqualTo(Admission));
                Assert.That(othersSigned,                Is.EqualTo(Others),     "a commit of a change set it did not sign");
                Assert.That(SignedBySecond(qa.Head.Commit),  Is.EqualTo(Others), String.Join(" ", looked));
                Assert.That(SignedBySecond(genesis!),    Is.False,               "what was before it held a key is not signed by it");

                // Told to, it signs whatever it is.
                Assert.That(second.SignFollowedCommit(id).Result.Status,
                            Is.EqualTo(Others ? RoamingNetworkCommitSigningStatus.AlreadySigned : RoamingNetworkCommitSigningStatus.Signed));

            });

        }

        #endregion

        #region WhatWaitsIsKeptOverARestart()

        /// <summary>
        /// A change of the holders waiting where the roaming network is made is kept beside its store
        /// (SHAREHOLDERS.md, phase 6): after a restart of that hub it waits still, with the signatures it had, and is
        /// gone from there once it is done.
        /// </summary>
        [Test]
        public async Task WhatWaitsIsKeptOverARestart()
        {

            var id         = RoamingNetwork_Id.Parse("QA");
            var maker      = await AnotherHub();
            var directory  = others[^1].Directory;

            Assert.That(maker.CreateRoamingNetwork(id).Success,  Is.True);

            var follower   = await AnotherHub();
            var network    = HistoryOf(maker, "QA").Head.RoamingNetwork;
            var following  = await follower.FollowRoamingNetwork(network.CreateTrustAnchor(network.Operator!.KeyStatement!, URLOf(maker, "QA")).ToQRCode());

            Assert.That(following.Status,  Is.EqualTo(RoamingNetworkFollowingStatus.Synchronized), following.Message);

            var beta       = follower.NetworkOperator.Holder!;
            var asked      = await follower.JoinRoamingNetworkOperator(id);
            var file       = Path.Combine(maker.POIDirectory, "QA", RoamingHub.ShareholderChangesFileName);
            var kept       = File.Exists(file);

            await maker.DisposeAsync();
            others.RemoveAll(other => ReferenceEquals(other.Hub, maker));

            var again      = await AnotherHub(directory);
            var waiting    = again.ShareholdersOf(id);

            // Admitted there: the change set waits for the proof of the one asking, which is not reached any more -
            // the hub is at other ports now - and the change is kept with its change set.
            var admitted   = await again.AdmitShareholder(id, beta);
            var withIts    = JArray.Parse(File.ReadAllText(file))[0]["changeSet"]?.Value<String>();

            Assert.Multiple(() => {

                Assert.That(asked.Status,                                         Is.EqualTo(ShareholderChangeStatus.Waiting), asked.Message);
                Assert.That(kept,                                                 Is.True, "what waits is not written beside the store");
                Assert.That(waiting?["changes"]?.Count(),                         Is.EqualTo(1), "what waited is forgotten at a restart");
                Assert.That(waiting?["changes"]?[0]?["holder"]?.Value<String>(),  Is.EqualTo(beta));
                Assert.That(waiting?["changes"]?[0]?["kind"]?.Value<String>(),    Is.EqualTo("admit"));
                Assert.That(admitted.Status,                                      Is.EqualTo(ShareholderChangeStatus.Waiting), admitted.Message);
                Assert.That(withIts,                                              Is.Not.Null, "the change set of what waits is not kept");

            });

        }

        #endregion

        #region UnderAQuorumWhatTheOperatorOwnsWaitsForTheOthers()

        /// <summary>
        /// Where two decide, the hub that makes the roaming network adds no party alone (SHAREHOLDERS.md, phase 5): its
        /// change set waits, the other holder sees it at its own hub and approves it, or refuses it, which withdraws
        /// it. The keys of the operator are stated anew by both, each at its hub. And a key this hub holds of an
        /// operator it follows is kept when it replaces its own.
        /// </summary>
        [Test]
        public async Task UnderAQuorumWhatTheOperatorOwnsWaitsForTheOthers()
        {

            var id         = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            var second     = await Followed("QA");
            var beta       = second.NetworkOperator.Holder!;
            var qa         = HistoryOf(RoamingHub, "QA");

            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,  Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await RoamingHub.AdmitShareholder(id, beta)).Status,      Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,  Is.EqualTo(ShareholderChangeStatus.Committed));

            await CaughtUp("QA", second);

            Boolean Names(String Id)
                => qa.Head.RoamingNetwork.TryGetChargingStationOperatorById(ChargingStationOperator_Id.Parse(Id), out _);

            #region A party waits for the second holder, who approves it

            // Signed by this hub and by the identity key of the party, its proof: at one holder it would be added now.
            var added        = POITests.AddedParty(RoamingHub, "QA", "DE*QAK");
            var addingId     = qa.PendingChangeSets.Single().Id;

            var waitsAtFirst = await RoamingHub.PendingChangeSetsOf(id);
            var waitsAtOther = await second.    PendingChangeSetsOf(id);
            var notYet       = Names("DE*QAK");

            var approved     = await second.ApprovePendingChangeSet(id, addingId);
            var there        = Names("DE*QAK");
            var approvedTwice= await second.ApprovePendingChangeSet(id, addingId);

            await CaughtUp("QA", second);

            #endregion

            #region Another one is refused: withdrawn

            var another      = POITests.AddedParty(RoamingHub, "QA", "DE*QAL");
            var refused      = await second.RefusePendingChangeSet(id, qa.PendingChangeSets.Single().Id);
            var waitsNoMore  = await RoamingHub.PendingChangeSetsOf(id);
            var notThere     = Names("DE*QAL");

            #endregion

            #region The keys of the operator are stated anew by both

            var statedBefore = qa.Head.RoamingNetwork.Operator!.KeyStatement!;

            await Task.Delay(50);

            var restated1    = await RoamingHub.RestateOperatorKeys(id);    // begins, and signs the key statement
            var restated2    = await second.    RestateOperatorKeys(id);    // signs it as well: its change set is made there, and signed here
            var statedAfter  = qa.Head.RoamingNetwork.Operator!.KeyStatement!;

            await CaughtUp("QA", second);

            #endregion

            #region A key held of an operator followed is kept

            // Replaced in what this hub makes only: what it follows names the old one still, which is kept for it.
            var heldBefore   = second.NetworkOperator.SigningKeyIdIn(HistoryOf(second, "QA").Head.RoamingNetwork);
            var rotated      = second.RotateNetworkOperatorSigningKey();
            var heldAfter    = second.NetworkOperator.SigningKeyIdIn(HistoryOf(second, "QA").Head.RoamingNetwork);

            #endregion

            Assert.Multiple(() => {

                Assert.That(added.Success,                                         Is.False, "it waits for the second holder");
                Assert.That(waitsAtFirst?.Count,                                   Is.EqualTo(1));
                Assert.That(waitsAtOther?[0]?["changeSet"]?["@id"]?.Value<String>(),  Is.EqualTo(addingId.ToString()), "told at the hub following as well");
                Assert.That(notYet,                                                Is.False, "one of two adds no party");

                Assert.That(approved.Status,                                       Is.EqualTo(ShareholderChangeStatus.Committed), approved.Message);
                Assert.That(there,                                                 Is.True);
                Assert.That(approvedTwice.Status,                                  Is.EqualTo(ShareholderChangeStatus.NotFound),  approvedTwice.Message);

                Assert.That(another.Success,                                       Is.False);
                Assert.That(refused.Status,                                        Is.EqualTo(ShareholderChangeStatus.Committed), refused.Message);
                Assert.That(refused.Message,                                       Does.Contain("is withdrawn"));
                Assert.That(waitsNoMore?.Count,                                    Is.EqualTo(0));
                Assert.That(notThere,                                              Is.False);

                Assert.That(restated1.Status,                                      Is.EqualTo(ShareholderChangeStatus.Waiting),   restated1.Message);
                Assert.That(restated2.Status,                                      Is.EqualTo(ShareholderChangeStatus.Committed), restated2.Message);
                Assert.That(statedAfter.SignedAt,                                  Is.GreaterThan(statedBefore.SignedAt));
                Assert.That(statedAfter.Signatures,                                Has.Length.EqualTo(2));
                Assert.That(statedAfter.MinConsensus,                              Is.EqualTo(2));

                Assert.That(rotated.Success,                                       Is.True, rotated.Message);
                Assert.That(heldBefore,                                            Is.Not.Null);
                Assert.That(heldAfter,                                             Is.EqualTo(heldBefore), "the key the roaming network followed names is not forgotten");

            });

        }

        #endregion

        #region AHolderRenewsItsSigningKeyAlone()

        /// <summary>
        /// Where two decide, each holder renews its own signing key alone, with its identity key (SPECIFICATION.md of
        /// WWCP POI, section 4.1): the hub that makes the roaming network there, and the hub following by a change set
        /// of its certificate sent there. Its commits and its attestations are signed by the new key from then on.
        /// </summary>
        [Test]
        public async Task AHolderRenewsItsSigningKeyAlone()
        {

            var id         = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            var second     = await Followed("QA");
            var alpha      = RoamingHub.NetworkOperator.Holder!;
            var beta       = second.    NetworkOperator.Holder!;
            var qa         = HistoryOf(RoamingHub, "QA");

            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,  Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await RoamingHub.AdmitShareholder(id, beta)).Status,      Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,  Is.EqualTo(ShareholderChangeStatus.Committed));

            await CaughtUp("QA", second);

            String SigningKeyOf(String Holder)
                => qa.Head.RoamingNetwork.Operator!.SigningKeys.Single(key => key.Holder == Holder).Id;

            #region The hub that makes it

            var alphaOld     = RoamingHub.NetworkOperator.SigningKey.Id;
            var byAlpha      = await RoamingHub.RotateNetworkOperatorKeyEverywhere(POIKeyKind.Signing);
            var alphaNew     = RoamingHub.NetworkOperator.SigningKey.Id;
            var alphaNamed   = SigningKeyOf(alpha);
            var alphaSigned  = qa.Head.Commit.Signatures.Select(signature => signature.KeyId).ToArray();
            var alphaProved  = qa.Head.Commit.ChangeSet!.Signatures.Select(signature => signature.KeyId).ToArray();

            await CaughtUp("QA", second);

            #endregion

            #region The hub following

            var betaOld      = second.NetworkOperator.SigningKey.Id;
            var byBeta       = await second.RotateNetworkOperatorKeyEverywhere(POIKeyKind.Signing);
            var betaNew      = second.NetworkOperator.SigningKey.Id;
            var betaNamed    = SigningKeyOf(beta);
            var betaProved   = qa.Head.Commit.ChangeSet!.Signatures.Select(signature => signature.KeyId).ToArray();

            await CaughtUp("QA", second);

            // Its new key signs what it follows from then on.
            var signed       = await second.SignFollowedCommit(id);
            var signedWith   = HistoryOf(second, "QA").Head.Commit.Signatures.Select(signature => signature.KeyId).ToArray();

            #endregion

            Assert.Multiple(() => {

                Assert.That(byAlpha.Success,          Is.True, byAlpha.Message);
                Assert.That(alphaNew,                 Is.Not.EqualTo(alphaOld));
                Assert.That(alphaNamed,               Is.EqualTo(alphaNew));
                Assert.That(alphaSigned,              Is.EqualTo(new[] { alphaNew }),                                 "the commit by the new key");
                Assert.That(alphaProved,              Is.EqualTo(new[] { RoamingHub.NetworkOperator.IdentityKey.Id }), "the change set by its identity key, alone");

                Assert.That(byBeta.Success,           Is.True, byBeta.Message);
                Assert.That(byBeta.Changed.Select(changed => changed.ToString()),  Does.Contain("QA"));
                Assert.That(betaNew,                  Is.Not.EqualTo(betaOld));
                Assert.That(betaNamed,                Is.EqualTo(betaNew));
                Assert.That(betaProved,               Is.EqualTo(new[] { second.NetworkOperator.IdentityKey.Id }));

                Assert.That(signed.Status,            Is.EqualTo(RoamingNetworkCommitSigningStatus.Signed), signed.Message);
                Assert.That(signedWith,               Does.Contain(betaNew));

            });

        }

        #endregion

        #region AnIdentityKeyIsReplacedInTwoChangesWhichAFollowerFollows()

        /// <summary>
        /// A hub replaces its identity key in two changes of every roaming network that names it (SHAREHOLDERS.md): the
        /// new one next to the old one, then the old one out, so that one key statement leads to the next. Alone
        /// where it decides alone, at once, and a hub following by the old trust anchor follows on. Where two decide,
        /// each change waits for the other holder, who agrees at its own hub - whether the one replacing its key makes
        /// the roaming network or follows it. Its new key is its current one once no roaming network names the old one;
        /// and it signs with it as a holder from then on.
        /// </summary>
        [Test]
        public async Task AnIdentityKeyIsReplacedInTwoChangesWhichAFollowerFollows()
        {

            var id         = RoamingNetwork_Id.Parse("QA");

            Assert.That(RoamingHub.CreateRoamingNetwork(id).Success,  Is.True);

            var second     = await Followed("QA");
            var alpha      = RoamingHub.NetworkOperator.Holder!;
            var beta       = second.    NetworkOperator.Holder!;
            var qa         = HistoryOf(RoamingHub, "QA");

            String[] IdentityKeysOf(String Holder)
                => [ .. qa.Head.RoamingNetwork.Operator!.IdentityKeys.Where(key => key.Holder == Holder).Select(key => key.Id) ];

            #region Alone: both changes at once, and the hub following by the old trust anchor follows on

            var alpha0       = RoamingHub.NetworkOperator.IdentityKey.Id;
            var alone        = await RoamingHub.RotateNetworkOperatorKeyEverywhere(POIKeyKind.Identity);
            var alpha1       = RoamingHub.NetworkOperator.IdentityKey.Id;
            var namedAlone   = IdentityKeysOf(alpha);

            await CaughtUp("QA", second);

            #endregion

            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,  Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await RoamingHub.AdmitShareholder(id, beta)).Status,      Is.EqualTo(ShareholderChangeStatus.Waiting));
            Assert.That((await second.    JoinRoamingNetworkOperator(id)).Status,  Is.EqualTo(ShareholderChangeStatus.Committed));

            await CaughtUp("QA", second);

            // Asked again and again, it goes on where it was, the other holder agreeing in between.
            async Task<List<String>> Replaced(RoamingHub Replacing, RoamingHub Agreeing, String Holder)
            {

                var said = new List<String>();

                for (var round = 0; round < 8; round++)
                {

                    var update = await Replacing.RotateNetworkOperatorKeyEverywhere(POIKeyKind.Identity);

                    said.Add(update.Message);

                    Assert.That(update.Success,  Is.True, update.Message);

                    if (update.Complete)
                        return said;

                    var agreed = await Agreeing.RekeyShareholder(id, Holder);

                    Assert.That(agreed.Success,  Is.True, agreed.Message);

                    await CaughtUp("QA", second);

                }

                Assert.Fail("The identity key was not replaced: " + String.Join(" | ", said));

                return said;

            }

            #region The hub that makes the roaming network, with the other holder

            var first        = await RoamingHub.RotateNetworkOperatorKeyEverywhere(POIKeyKind.Identity);
            var nextOfAlpha  = RoamingHub.NetworkOperator.NextIdentityKey?.Id;
            var stillAlpha1  = RoamingHub.NetworkOperator.IdentityKey.Id;

            // As its directory has it, read again: what a hub started again would go on with.
            var stored       = HubRoamingNetworkOperator.OpenOrCreate(Path.Combine(Directory, HubRoamingNetworkOperator.DirectoryName));
            var notAlone     = IdentityKeysOf(alpha);
            var nobody       = await second.RekeyShareholder(id, "nobody");

            Assert.That((await second.RekeyShareholder(id, alpha)).Success,  Is.True);

            var byAlpha      = await Replaced(RoamingHub, second, alpha);
            var alpha2       = RoamingHub.NetworkOperator.IdentityKey.Id;
            var namedAlpha   = IdentityKeysOf(alpha);

            #endregion

            #region The hub following, with the hub that makes it

            var beta0        = second.NetworkOperator.IdentityKey.Id;
            var byBeta       = await Replaced(second, RoamingHub, beta);
            var beta1        = second.NetworkOperator.IdentityKey.Id;
            var namedBeta    = IdentityKeysOf(beta);

            #endregion

            #region With their new keys: the keys of the operator stated anew by both

            await Task.Delay(50);

            var restated1    = await RoamingHub.RestateOperatorKeys(id);
            var restated2    = await second.    RestateOperatorKeys(id);
            var restated3    = await RoamingHub.RestateOperatorKeys(id);
            var restated4    = await second.    RestateOperatorKeys(id);
            var stated       = qa.Head.RoamingNetwork.Operator!.KeyStatement!;

            await CaughtUp("QA", second);

            #endregion

            Assert.Multiple(() => {

                Assert.That(alone.Complete,                                 Is.True, alone.Message);
                Assert.That(alpha1,                                         Is.Not.EqualTo(alpha0));
                Assert.That(namedAlone,                                     Is.EqualTo(new[] { alpha1 }));

                Assert.That(first.Success,                                  Is.True,  first.Message);
                Assert.That(first.Complete,                                 Is.False, first.Message);
                Assert.That(first.Waiting?.Keys.Select(key => key.ToString()),  Is.EqualTo(new[] { "QA" }));
                Assert.That(nextOfAlpha,                                    Is.Not.Null.And.Not.EqualTo(alpha1));
                Assert.That(stillAlpha1,                                    Is.EqualTo(alpha1), "the old one stays the current one while a roaming network names it");
                Assert.That(stored.NextIdentityKey?.Id,                     Is.EqualTo(nextOfAlpha), "the next identity key is kept");
                Assert.That(stored.IdentityKey.Id,                          Is.EqualTo(alpha1));
                Assert.That(stored.KeptKeyIds,                              Is.Empty);
                Assert.That(notAlone,                                       Is.EqualTo(new[] { alpha1 }), "one of two changes no identity key");
                Assert.That(nobody.Status,                                  Is.EqualTo(ShareholderChangeStatus.NotFound));

                Assert.That(alpha2,                                         Is.EqualTo(nextOfAlpha));
                Assert.That(namedAlpha,                                     Is.EqualTo(new[] { alpha2 }));
                Assert.That(RoamingHub.NetworkOperator.NextIdentityKey,     Is.Null);
                Assert.That(RoamingHub.NetworkOperator.KeptKeyIds,          Is.Empty, String.Join(" | ", byAlpha));

                Assert.That(beta1,                                          Is.Not.EqualTo(beta0));
                Assert.That(namedBeta,                                      Is.EqualTo(new[] { beta1 }));
                Assert.That(second.NetworkOperator.NextIdentityKey,         Is.Null);
                Assert.That(second.NetworkOperator.KeptKeyIds,              Is.Empty, String.Join(" | ", byBeta));

                Assert.That(restated4.Status,                               Is.EqualTo(ShareholderChangeStatus.Committed), $"{restated1.Message} | {restated2.Message} | {restated3.Message} | {restated4.Message}");
                Assert.That(stated.Signatures.Select(signature => signature.KeyId),  Is.EquivalentTo(new[] { alpha2, beta1 }));

                Assert.That(second.FollowedRoamingNetworks.Single().Error,  Is.Null);

            });

        }

        #endregion

    }

}
