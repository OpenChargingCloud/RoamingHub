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
        private async Task<RoamingHub> Followed(String Id)
        {

            var follower  = await AnotherHub();
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
        /// at one hub, and none does the next by itself. Then it resigns: dismissed by the one staying, signed by both.
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

            var admitted   = await RoamingHub.AdmitShareholder(id, beta);
            var notYet     = HoldersOf(RoamingHub, "QA");

            var joined     = await follower.  JoinRoamingNetworkOperator(id);
            var both       = HoldersOf(RoamingHub, "QA");
            var quorum     = HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.Operator!.MinConsensus;

            await CaughtUp("QA", follower);

            var holdsKeys  = follower.NetworkOperator.SigningKeyIdIn(HistoryOf(follower, "QA").Head.RoamingNetwork) is not null;
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
        /// the three dismiss the third without it.
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
            Assert.That((await second.SignFollowedCommit(id)).Status,  Is.EqualTo(RoamingNetworkCommitSigningStatus.Signed));

            Assert.That(await Until(() => HistoryOf(RoamingHub, "QA").CurrentAttestations().Any(attestation => attestation.CommitId == HistoryOf(RoamingHub, "QA").Head.Commit.Id &&
                                                                                                               attestation.KeyId    == second.NetworkOperator.SigningKey.Id)),
                        Is.True, "the attestation of the second holder did not arrive");

            var third    = await Followed("QA");
            var gamma    = third.NetworkOperator.Holder!;

            #region The third one is admitted by both

            var asked        = await third.     JoinRoamingNetworkOperator(id);
            var byAlpha      = await RoamingHub.AdmitShareholder(id, gamma);    // signs the key statement: one of two
            var byBeta       = await second.    AdmitShareholder(id, gamma);    // signs it as well
            var made         = await RoamingHub.AdmitShareholder(id, gamma);    // makes the change set of it, and signs that
            var tooEarly     = await third.     JoinRoamingNetworkOperator(id); // its proof: but two decide
            var signedByBeta = await second.    AdmitShareholder(id, gamma);    // the second of the two
            var three        = HoldersOf(RoamingHub, "QA");

            await CaughtUp("QA", second, third);

            #endregion

            #region And dismissed by both, without it

            var outByAlpha   = await RoamingHub.DismissShareholder(id, gamma);
            var outByBeta    = await second.    DismissShareholder(id, gamma);
            var outMade      = await RoamingHub.DismissShareholder(id, gamma);
            var outSigned    = await second.    DismissShareholder(id, gamma);
            var two          = HoldersOf(RoamingHub, "QA");

            #endregion

            Assert.Multiple(() => {

                Assert.That(asked.Status,         Is.EqualTo(ShareholderChangeStatus.Waiting),    asked.Message);
                Assert.That(byAlpha.Status,       Is.EqualTo(ShareholderChangeStatus.Waiting),    byAlpha.Message);
                Assert.That(byAlpha.Message,      Does.Contain("waits for the other holders"));
                Assert.That(byBeta.Status,        Is.EqualTo(ShareholderChangeStatus.Waiting),    byBeta.Message);
                Assert.That(made.Status,          Is.EqualTo(ShareholderChangeStatus.Waiting),    made.Message);
                Assert.That(made.Message,         Does.Contain("is made and signed by this hub"));
                Assert.That(tooEarly.Status,      Is.EqualTo(ShareholderChangeStatus.Waiting),    tooEarly.Message);
                Assert.That(signedByBeta.Status,  Is.EqualTo(ShareholderChangeStatus.Committed),  signedByBeta.Message);

                Assert.That(three,                Is.EquivalentTo(new[] { alpha, beta, gamma }));

                Assert.That(outByAlpha.Status,    Is.EqualTo(ShareholderChangeStatus.Waiting),    outByAlpha.Message);
                Assert.That(outByBeta.Status,     Is.EqualTo(ShareholderChangeStatus.Waiting),    outByBeta.Message);
                Assert.That(outMade.Status,       Is.EqualTo(ShareholderChangeStatus.Waiting),    outMade.Message);
                Assert.That(outSigned.Status,     Is.EqualTo(ShareholderChangeStatus.Committed),  outSigned.Message);

                Assert.That(two,                  Is.EquivalentTo(new[] { alpha, beta }));
                Assert.That(HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.Operator!.MinConsensus,  Is.EqualTo(2));

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
            var restated2    = await second.    RestateOperatorKeys(id);    // signs it as well
            var restated3    = await RoamingHub.RestateOperatorKeys(id);    // makes the change set, and signs it
            var restated4    = await second.    RestateOperatorKeys(id);    // signs it: committed
            var statedAfter  = qa.Head.RoamingNetwork.Operator!.KeyStatement!;

            await CaughtUp("QA", second);

            #endregion

            #region A key held of an operator followed is kept

            // Replaced in what this hub makes only: what it follows names the old one still, which is kept for it.
            var heldBefore   = second.NetworkOperator.SigningKeyIdIn(HistoryOf(second, "QA").Head.RoamingNetwork);
            var rotated      = second.RotateNetworkOperatorKey(POIKeyKind.Signing);
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
                Assert.That(restated2.Status,                                      Is.EqualTo(ShareholderChangeStatus.Waiting),   restated2.Message);
                Assert.That(restated3.Status,                                      Is.EqualTo(ShareholderChangeStatus.Waiting),   restated3.Message);
                Assert.That(restated4.Status,                                      Is.EqualTo(ShareholderChangeStatus.Committed), restated4.Message);
                Assert.That(statedAfter.SignedAt,                                  Is.GreaterThan(statedBefore.SignedAt));
                Assert.That(statedAfter.Signatures,                                Has.Length.EqualTo(2));
                Assert.That(statedAfter.MinConsensus,                              Is.EqualTo(2));

                Assert.That(rotated.Success,                                       Is.True, rotated.Message);
                Assert.That(heldBefore,                                            Is.Not.Null);
                Assert.That(heldAfter,                                             Is.EqualTo(heldBefore), "the key the roaming network followed names is not forgotten");

            });

        }

        #endregion

        #region AHolderRenewsItsSigningKeyAloneAndNoIdentityKey()

        /// <summary>
        /// Where two decide, each holder renews its own signing key alone, with its identity key (SPECIFICATION.md of
        /// WWCP POI, section 4.1): the hub that makes the roaming network there, and the hub following by a change set
        /// of its certificate sent there. Its commits and its attestations are signed by the new key from then on. An
        /// identity key says who holds the keys, which is decided together: it is not replaced by its holder alone.
        /// </summary>
        [Test]
        public async Task AHolderRenewsItsSigningKeyAloneAndNoIdentityKey()
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

            #region No identity key

            var identityOld  = RoamingHub.NetworkOperator.IdentityKey.Id;
            var noIdentity   = await RoamingHub.RotateNetworkOperatorKeyEverywhere(POIKeyKind.Identity);
            var noIdentity2  = await second.    RotateNetworkOperatorKeyEverywhere(POIKeyKind.Identity);
            var identityNow  = qa.Head.RoamingNetwork.Operator!.IdentityKeys.Single(key => key.Holder == alpha).Id;

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

                Assert.That(noIdentity.Success,       Is.False);
                Assert.That(noIdentity.Message,       Does.Contain("replaced by them together"));
                Assert.That(noIdentity2.Success,      Is.False);
                Assert.That(noIdentity2.Message,      Does.Contain("replaced by them together"));
                Assert.That(identityNow,              Is.EqualTo(identityOld));

            });

        }

        #endregion

    }

}
