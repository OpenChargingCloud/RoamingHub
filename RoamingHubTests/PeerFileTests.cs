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
using System.Net.Http.Headers;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.RoamingHub.OCPI;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A peer added or removed, or registered in either direction, while the
    /// file the OCPI library keeps the peers of a version in cannot be
    /// written: 500 and why, and nothing changed, neither now nor at the next
    /// start - except a registration the peer accepted, which is kept and
    /// written down later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both were answered as done. The library wrote the line into a queue
    /// that told the debug log alone that it could not: an added peer was
    /// listed, its token opening this hub, and gone at the next start; a
    /// removed one was back at the next start, its token opening this hub
    /// again.
    /// </para>
    /// <para>
    /// The file is made unwritable the way that stops root as well: a
    /// directory where it would be. Both versions are offered, because each
    /// keeps its peers in a file of its own and is bound to this hub by a
    /// binding of its own.
    /// </para>
    /// </remarks>
    public class PeerFileTests : ARoamingHubTests
    {

        #region Configuration - a hub that offers both versions

        /// <summary>
        /// The default is 2.2.1 alone.
        /// </summary>
        protected override JObject Configuration

            => new (
                   new JProperty("nts",   new JObject(
                       new JProperty("enabled",  false)
                   )),
                   new JProperty("ocpi",  new JObject(
                       new JProperty("versions",  new JArray("2.2.1", "2.3.0"))
                   ))
               );

        #endregion

        #region NewRoamingHub() - a hub whose stopping can be made to fail

        /// <summary>
        /// A hub as any other, whose next stop can be made to fail - see
        /// HubWhoseStopCanFail.
        /// </summary>
        protected override RoamingHub NewRoamingHub()

            => new HubWhoseStopCanFail(
                   AccountsPath:  Path.Combine(Directory, "accounts"),
                   ConfigFile:    TestRoamingHubs.ConfigFile(Directory, Configuration),
                   Clock:         Clock
               );

        #endregion


        #region APeerIsNotAddedWhereItsFileCannotTakeIt(Version)

        /// <summary>
        /// A peer the file of its version cannot take is not added: 500 and
        /// why, not in the list, and not there at the next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APeerIsNotAddedWhereItsFileCannotTakeIt(String Version)
        {

            using var admin = await SignedIn();

            var file      = BlockPeersFile(Version);

            var response  = await admin.PostAsync("/api/v1/ocpi/partners", Peer(Version));
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPeers(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(text,   Does.Contain(Path.GetFileName(file)),  "The answer does not say which file refused.");
                Assert.That(listed, Does.Not.Contain(PeerId),              "The peer the file refused is listed all the same.");
            });

            Assert.That(await PeersAfterARestart(file), Does.Not.Contain(PeerId));

        }

        #endregion

        #region APeerIsNotRemovedWhereItsFileCannotTakeIt(Version)

        /// <summary>
        /// A peer whose removal the file of its version cannot take is not
        /// removed: 500 and why, still in the list, and still there at the
        /// next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APeerIsNotRemovedWhereItsFileCannotTakeIt(String Version)
        {

            using var admin = await SignedIn();

            await AddPeer(admin, Version);

            // What the next start below reads the peer back from.
            Assert.That(await File.ReadAllTextAsync(PeersFile(Version)), Does.Contain(protocols.OCPI.CommonHTTPAPI.addRemoteParty),
                        "The peer that was added is not in its file.");

            var file      = BlockPeersFile(Version);

            var response  = await admin.DeleteAsync($"/api/v1/ocpi/partners/{Version}/{PeerId}");
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPeers(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(text,   Does.Contain(Path.GetFileName(file)),  "The answer does not say which file refused.");
                Assert.That(listed, Does.Contain(PeerId),                  "The peer the file would not let go of is gone from the list.");
            });

            Assert.That(await PeersAfterARestart(file), Does.Contain(PeerId));

        }

        #endregion

        #region APeerIsRemovedForGood(Version)

        /// <summary>
        /// A peer removed while its file can be written is gone: 200, not in
        /// the list, and not there at the next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APeerIsRemovedForGood(String Version)
        {

            using var admin = await SignedIn();

            await AddPeer(admin, Version);

            var response  = await admin.DeleteAsync($"/api/v1/ocpi/partners/{Version}/{PeerId}");
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPeers(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
                Assert.That(listed, Does.Not.Contain(PeerId), "The peer that was removed is still listed.");
            });

            Assert.That(await PeersAfterARestart(), Does.Not.Contain(PeerId));

        }

        #endregion


        #region NothingIsSentWhereTheTokenToBeCalledBackWithCannotBeStored(Version)

        /// <summary>
        /// A registration whose first line the file cannot take - the token
        /// the peer would call back with - sends nothing: 500 and why, and
        /// nothing changed, neither now nor at the next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task NothingIsSentWhereTheTokenToBeCalledBackWithCannotBeStored(String Version)
        {

            using var admin = await SignedIn();

            await using var peer = await StubPeer.Start(Version: Version);

            await AddRegistrablePeer(admin, Version, peer);

            var file      = BlockPeersFile(Version);

            var response  = await admin.PostAsync($"/api/v1/ocpi/partners/{Version}/{PeerId}/register", JSONBody());
            var text      = await response.Content.ReadAsStringAsync();
            var answer    = JObject.Parse(text);
            var listed    = await ListedPeer(admin, Version);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,                   Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(answer.Value<Boolean>("ok"),           Is.False,                               "The registration its file refused is said to have worked.");
                Assert.That(answer.Value<String>("message"),       Does.Contain(Path.GetFileName(file)),   "The answer does not say which file refused.");
                Assert.That(answer.Value<String>("message"),       Does.StartWith("Nothing was sent"),     "The answer does not say that nothing was sent.");
                Assert.That(peer.ReceivedCredentials,              Is.Null,                                "The credentials were sent, though the token to call back with could not be stored.");
                Assert.That(listed?.Value<Boolean>("registered"),  Is.False,                               "The peer is said to be registered.");
            });

            var after = await PeerAfterARestart(Version, file);

            Assert.Multiple(() => {
                Assert.That(after,              Is.Not.Null,  "The peer is gone at the next start.");
                Assert.That(after?.Registered,  Is.False,     "The peer is registered at the next start.");
            });

        }

        #endregion

        #region ARegistrationThePeerAcceptedIsKeptAndWrittenDownWithTheNextChange(Version)

        /// <summary>
        /// A registration the peer accepted, whose line the file cannot take:
        /// 500 and why, and in effect all the same - the peer uses the new
        /// tokens already - and written down with the next change the file
        /// takes.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationThePeerAcceptedIsKeptAndWrittenDownWithTheNextChange(String Version)
        {

            using var admin = await SignedIn();

            await using var peer = await StubPeer.Start(Version: Version);

            await AddRegistrablePeer(admin, Version, peer);

            peer.WhenCredentialsArrive = () => BlockPeersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version);

            UnblockPeersFile(Version);

            var other = await admin.PostAsync("/api/v1/ocpi/partners", OtherPeer(Version));

            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.Created), $"The next change answered {(Int32) other.StatusCode}: {await other.Content.ReadAsStringAsync()}");

            var after = await PeerAfterARestart(Version);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,              Is.True,                     "The registration kept is not known at the next start, though the file took a change after it.");
                Assert.That(after?.TheirToken?.ToString(),  Is.EqualTo(StubPeer.TokenC), "The token the peer handed out is not known at the next start.");
            });

        }

        #endregion

        #region ARegistrationThePeerAcceptedIsWrittenDownWhenThisHubStops(Version)

        /// <summary>
        /// A registration kept is written down when this hub stops, where the
        /// file takes it by then.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationThePeerAcceptedIsWrittenDownWhenThisHubStops(String Version)
        {

            using var admin = await SignedIn();

            await using var peer = await StubPeer.Start(Version: Version);

            await AddRegistrablePeer(admin, Version, peer);

            peer.WhenCredentialsArrive = () => BlockPeersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version);

            UnblockPeersFile(Version);

            await RoamingHub.DisposeAsync();

            var after = await PeerAfterARestart(Version);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,              Is.True,                     "The registration kept is not known at the next start, though the file took lines when this hub stopped.");
                Assert.That(after?.TheirToken?.ToString(),  Is.EqualTo(StubPeer.TokenC), "The token the peer handed out is not known at the next start.");
            });

        }

        #endregion

        #region ARegistrationTheFileStillRefusesWhenThisHubStopsIsInTheLog(Version)

        /// <summary>
        /// A registration kept that the file still refuses when this hub stops
        /// is an error in the log: the next start will not know it.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationTheFileStillRefusesWhenThisHubStopsIsInTheLog(String Version)
        {

            using var admin = await SignedIn();

            await using var peer = await StubPeer.Start(Version: Version);

            await AddRegistrablePeer(admin, Version, peer);

            peer.WhenCredentialsArrive = () => BlockPeersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version);

            await RoamingHub.DisposeAsync();

            var said = RoamingHub.Log.Recent(100, Tag: "files").
                                      Where (entry => entry.Message.Contains("the next start will not know it")).
                                      ToArray();

            // So that the TearDown's second disposal has nothing left to say.
            UnblockPeersFile(Version);

            Assert.That(said, Has.Length.EqualTo(1), "The registration the next start will not know is not in the log, or more than once.");

            Assert.Multiple(() => {
                Assert.That(said[0].Level,    Is.EqualTo(LogLevel.Error));
                Assert.That(said[0].Tags,     Does.Contain("ocpi"));
                Assert.That(said[0].Message,  Does.Contain(PeerId), "The log does not name the peer.");
            });

        }

        #endregion

        #region ARegistrationThePeerAcceptedIsWrittenDownWhereThisHubFailsToStop(Version)

        /// <summary>
        /// A registration kept is written down when this hub stops, where the
        /// file takes it by then - even where stopping fails, which is said
        /// all the same.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationThePeerAcceptedIsWrittenDownWhereThisHubFailsToStop(String Version)
        {

            using var admin = await SignedIn();

            await using var peer = await StubPeer.Start(Version: Version);

            await AddRegistrablePeer(admin, Version, peer);

            peer.WhenCredentialsArrive = () => BlockPeersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version);

            UnblockPeersFile(Version);

            ((HubWhoseStopCanFail) RoamingHub).NextStopFails = true;

            Assert.ThrowsAsync<InvalidOperationException>(async () => await RoamingHub.DisposeAsync(),
                                                          "The stop that was made to fail is not said to have failed.");

            var after = await PeerAfterARestart(Version);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,              Is.True,                     "The registration kept is not known at the next start, though the file took lines when this hub failed to stop.");
                Assert.That(after?.TheirToken?.ToString(),  Is.EqualTo(StubPeer.TokenC), "The token the peer handed out is not known at the next start.");
            });

        }

        #endregion

        #region ARegistrationTheFileStillRefusesWhereThisHubFailsToStopIsInTheLog(Version)

        /// <summary>
        /// A registration kept that the file still refuses when this hub stops
        /// is an error in the log - even where stopping fails.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationTheFileStillRefusesWhereThisHubFailsToStopIsInTheLog(String Version)
        {

            using var admin = await SignedIn();

            await using var peer = await StubPeer.Start(Version: Version);

            await AddRegistrablePeer(admin, Version, peer);

            peer.WhenCredentialsArrive = () => BlockPeersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version);

            ((HubWhoseStopCanFail) RoamingHub).NextStopFails = true;

            Assert.ThrowsAsync<InvalidOperationException>(async () => await RoamingHub.DisposeAsync(),
                                                          "The stop that was made to fail is not said to have failed.");

            var said = RoamingHub.Log.Recent(100, Tag: "files").
                                      Where (entry => entry.Message.Contains("the next start will not know it")).
                                      ToArray();

            // So that the TearDown's second disposal has nothing left to say.
            UnblockPeersFile(Version);

            Assert.That(said, Has.Length.EqualTo(1), "The registration the next start will not know is not in the log, or more than once.");

            Assert.Multiple(() => {
                Assert.That(said[0].Level,    Is.EqualTo(LogLevel.Error));
                Assert.That(said[0].Tags,     Does.Contain("ocpi"));
                Assert.That(said[0].Message,  Does.Contain(PeerId), "The log does not name the peer.");
            });

        }

        #endregion

        #region APeerRegisteringWhileItsFileCannotTakeItKeepsItsToken(Version)

        /// <summary>
        /// A peer that registers here while the file cannot take it is
        /// answered OCPI 3000 with HTTP 500, and nothing changed: the token it
        /// came with still opens this hub, now and at the next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APeerRegisteringWhileItsFileCannotTakeItKeepsItsToken(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubPeer.Start(Version: Version);

            var ourToken  = await AddPeerWithOurToken(admin, Version);

            using var peer = PeerWith(ourToken);

            var (details, credentials) = await Endpoints(peer, Version);

            var file      = BlockPeersFile(Version);

            var posted    = await peer.PostAsync(credentials, CredentialsOf(cpo));
            var text      = await posted.Content.ReadAsStringAsync();
            var listed    = await ListedPeer(admin, Version);
            var stillOpen = await peer.GetAsync(details);

            Assert.Multiple(() => {
                Assert.That(posted.StatusCode,                     Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(JObject.Parse(text).Value<Int32>("status_code"), Is.EqualTo(3000), text);
                Assert.That(listed?.Value<Boolean>("registered"),  Is.False,                    "The peer is said to be registered, though the file refused it.");
                Assert.That(listed?.Value<String>("ourToken"),     Is.EqualTo(ourToken),        "The token the peer came with is not this hub's any more.");
                Assert.That(stillOpen.IsSuccessStatusCode,         Is.True,                     "The token the peer came with no longer opens this hub.");
            });

            var after = await PeerAfterARestart(Version, file);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,            Is.False,              "The peer is registered at the next start.");
                Assert.That(after?.OurToken?.ToString(),  Is.EqualTo(ourToken),  "The token the peer came with is not known at the next start.");
            });

        }

        #endregion

        #region APeerUnregisteringWhileItsFileCannotTakeItStaysRegistered(Version)

        /// <summary>
        /// A peer that unregisters while the file cannot take it is answered
        /// OCPI 3000 with HTTP 500, and stays registered, its token valid, now
        /// and at the next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APeerUnregisteringWhileItsFileCannotTakeItStaysRegistered(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubPeer.Start(Version: Version);

            var ourToken  = await AddPeerWithOurToken(admin, Version);

            using var first = PeerWith(ourToken);

            var (details, credentials) = await Endpoints(first, Version);

            var posted    = await first.PostAsync(credentials, CredentialsOf(cpo));
            var postedText = await posted.Content.ReadAsStringAsync();

            Assert.That(posted.IsSuccessStatusCode, Is.True, $"The registration answered {(Int32) posted.StatusCode}: {postedText}");

            var tokenC    = JObject.Parse(postedText)["data"]?.Value<String>("token")!;

            using var peer = PeerWith(tokenC);

            var file      = BlockPeersFile(Version);

            var deleted   = await peer.DeleteAsync(credentials);
            var text      = await deleted.Content.ReadAsStringAsync();
            var listed    = await ListedPeer(admin, Version);
            var stillOpen = await peer.GetAsync(details);

            Assert.Multiple(() => {
                Assert.That(deleted.StatusCode,                    Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(JObject.Parse(text).Value<Int32>("status_code"), Is.EqualTo(3000), text);
                Assert.That(listed?.Value<Boolean>("registered"),  Is.True,  "The peer is not registered any more, though the file refused its unregistration.");
                Assert.That(stillOpen.IsSuccessStatusCode,         Is.True,  "The token of the peer no longer opens this hub.");
            });

            var after = await PeerAfterARestart(Version, file);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,            Is.True,             "The peer is not registered at the next start.");
                Assert.That(after?.OurToken?.ToString(),  Is.EqualTo(tokenC),  "The token of the peer is not known at the next start.");
            });

        }

        #endregion

        #region ALineTheFileRefusesIsInTheLog(Version)

        /// <summary>
        /// A line the file of the peers refuses is an error in the log, naming
        /// the file and the command - and not the line, which holds tokens.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ALineTheFileRefusesIsInTheLog(String Version)
        {

            using var admin = await SignedIn();

            var file      = BlockPeersFile(Version);

            var response  = await admin.PostAsync("/api/v1/ocpi/partners", Peer(Version));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError), await response.Content.ReadAsStringAsync());

            var said = RoamingHub.Log.Recent(100, Tag: "files").ToArray();

            Assert.That(said, Has.Length.EqualTo(1), "The line its file refused is not in the log, or more than once.");

            Assert.Multiple(() => {
                Assert.That(said[0].Level,    Is.EqualTo(LogLevel.Error));
                Assert.That(said[0].Tags,     Does.Contain("ocpi"));
                Assert.That(said[0].Message,  Does.Contain(Path.GetFileName(file)), "The log does not name the file that refused.");
                Assert.That(said[0].Message,  Does.Contain(protocols.OCPI.CommonHTTPAPI.addRemoteParty), "The log does not name the command.");
            });

        }

        #endregion


        #region (private static) PeerId / Peer(Version)

        /// <summary>
        /// The peer every test here adds.
        /// </summary>
        private const String PeerId = "DE-GEF_CPO";

        /// <summary>
        /// Its request body, on the given version.
        /// </summary>
        private static StringContent Peer(String Version)

            => JSONBody(
                   new JProperty("version",      Version),
                   new JProperty("countryCode",  "DE"),
                   new JProperty("partyId",      "GEF"),
                   new JProperty("role",         "CPO"),
                   new JProperty("name",         "Test CPO")
               );

        #endregion

        #region (private static) AddPeer(Admin, Version)

        /// <summary>
        /// Add the peer through the JSON API, on the given version.
        /// </summary>
        private static async Task AddPeer(HttpClient  Admin,
                                          String      Version)
        {

            var response = await Admin.PostAsync("/api/v1/ocpi/partners", Peer(Version));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Adding the peer answered {(Int32) response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        }

        #endregion

        #region (private static) OtherPeer(Version) / RegistrablePeer(Version, Peer)

        /// <summary>
        /// Another peer, for a change after the one a test is about.
        /// </summary>
        private static StringContent OtherPeer(String Version)

            => JSONBody(
                   new JProperty("version",      Version),
                   new JProperty("countryCode",  "DE"),
                   new JProperty("partyId",      "GDF"),
                   new JProperty("role",         "EMSP"),
                   new JProperty("name",         "Test EMSP")
               );

        /// <summary>
        /// The peer every test here adds, with the token and the versions URL
        /// it handed out - so that this hub can register with it.
        /// </summary>
        private static StringContent RegistrablePeer(String    Version,
                                                     StubPeer  Peer)

            => JSONBody(
                   new JProperty("version",      Version),
                   new JProperty("countryCode",  "DE"),
                   new JProperty("partyId",      "GEF"),
                   new JProperty("role",         "CPO"),
                   new JProperty("name",         "Test CPO"),
                   new JProperty("theirToken",   StubPeer.TokenA),
                   new JProperty("versionsURL",  Peer.VersionsURL)
               );

        #endregion

        #region (private static) AddRegistrablePeer(Admin, Version, Peer) / AddPeerWithOurToken(Admin, Version)

        /// <summary>
        /// Add the peer with the token and the versions URL it handed out.
        /// </summary>
        private static async Task AddRegistrablePeer(HttpClient  Admin,
                                                     String      Version,
                                                     StubPeer    Peer)
        {

            var response = await Admin.PostAsync("/api/v1/ocpi/partners", RegistrablePeer(Version, Peer));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Adding the peer answered {(Int32) response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        }

        /// <summary>
        /// Add the peer, and hand back the token this hub made up for it.
        /// </summary>
        private static async Task<String> AddPeerWithOurToken(HttpClient  Admin,
                                                              String      Version)
        {

            var response = await Admin.PostAsync("/api/v1/ocpi/partners", Peer(Version));
            var text     = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), $"Adding the peer answered {(Int32) response.StatusCode}: {text}");

            return JObject.Parse(text).Value<String>("ourToken")!;

        }

        #endregion

        #region (private static) RegisterAcceptedAndNotSaved(Admin, Version)

        /// <summary>
        /// This hub registers with the peer, which accepts, and the file cannot
        /// take what that changed: 500 and why, and in effect all the same.
        /// </summary>
        private static async Task RegisterAcceptedAndNotSaved(HttpClient  Admin,
                                                              String      Version)
        {

            var response  = await Admin.PostAsync($"/api/v1/ocpi/partners/{Version}/{PeerId}/register", JSONBody());
            var text      = await response.Content.ReadAsStringAsync();
            var answer    = JObject.Parse(text);
            var listed    = await ListedPeer(Admin, Version);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,                   Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(answer.Value<Boolean>("ok"),           Is.False,                     "The registration whose line its file refused is said to have worked.");
                Assert.That(answer.Value<String>("message"),       Does.Contain("accepted"),     "The answer does not say that the peer accepted.");
                Assert.That(answer.Value<String>("message"),       Does.Contain("could not be written"), "The answer does not say that the file refused.");
                Assert.That(listed?.Value<Boolean>("registered"),  Is.True,                      "The registration the peer accepted is not in effect.");
                Assert.That(listed?.Value<String>("theirToken"),   Is.EqualTo(StubPeer.TokenC),  "The token the peer handed out in its answer is not used.");
            });

        }

        #endregion

        #region (private) PeerWith(Token) / Endpoints(Peer, Version) / CredentialsOf(Peer)

        /// <summary>
        /// A peer calling this hub with the given token, encoded the way OCPI
        /// 2.2 sends it.
        /// </summary>
        private HttpClient PeerWith(String Token)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                           "Token",
                                                           Convert.ToBase64String(Encoding.UTF8.GetBytes(Token))
                                                       );

            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return http;

        }

        /// <summary>
        /// Where this hub's details of the given version are, and its
        /// credentials endpoint - as a peer finds them.
        /// </summary>
        private static async Task<(String Details, String Credentials)> Endpoints(HttpClient  Peer,
                                                                                  String      Version)
        {

            var versions   = (await GetJSON(Peer, "/ext/versions"))["data"] as JArray;
            var details    = versions!.First(version => version.Value<String>("version") == Version).Value<String>("url")!;
            var endpoints  = (await GetJSON(Peer, details))["data"]?["endpoints"] as JArray;

            return (details, endpoints!.First(endpoint => endpoint.Value<String>("identifier") == "credentials").Value<String>("url")!);

        }

        /// <summary>
        /// What the given stub POSTs to this hub to register: its token C, its
        /// versions URL, and who it is.
        /// </summary>
        private static StringContent CredentialsOf(StubPeer Peer)

            => new (
                   new JObject(
                       new JProperty("token",  StubPeer.TokenC),
                       new JProperty("url",    Peer.VersionsURL),
                       new JProperty("roles",  new JArray(
                           new JObject(
                               new JProperty("role",              "CPO"),
                               new JProperty("party_id",          "GEF"),
                               new JProperty("country_code",      "DE"),
                               new JProperty("business_details",  new JObject(
                                   new JProperty("name",  "Test CPO")
                               ))
                           )
                       ))
                   ).ToString(),
                   Encoding.UTF8,
                   "application/json"
               );

        #endregion

        #region (private static) ListedPeer(HTTP, Version)

        /// <summary>
        /// The peer every test here adds, as the JSON API lists it, or null.
        /// </summary>
        private static async Task<JToken?> ListedPeer(HttpClient  HTTP,
                                                      String      Version)

            => ((await GetJSON(HTTP, "/api/v1/ocpi/partners"))["partners"] as JArray ?? new JArray()).
                   FirstOrDefault(peer => peer.Value<String>("id")      == PeerId &&
                                          peer.Value<String>("version") == Version);

        #endregion

        #region (private static) ListedPeers(HTTP)

        /// <summary>
        /// The peers the JSON API lists, by their identification.
        /// </summary>
        private static async Task<String[]> ListedPeers(HttpClient HTTP)

            => [.. ((await GetJSON(HTTP, "/api/v1/ocpi/partners"))["partners"] as JArray ?? new JArray()).
                   Select(peer => peer.Value<String>("id") ?? "")];

        #endregion

        #region (private) PeersFile(Version) / BlockPeersFile(Version)

        /// <summary>
        /// The file the library keeps the peers of a version in.
        /// </summary>
        private String PeersFile(String Version)

            => Path.Combine(
                   RoamingHub.OCPIDirectory,
                   Version switch {
                       "2.2.1"  => protocols.OCPIv2_2_1.CommonAPI.DefaultRemotePartyDBFileName,
                       "2.3.0"  => protocols.OCPIv2_3_0.CommonAPI.DefaultRemotePartyDBFileName,
                       _        => throw new ArgumentException($"This hub offers no OCPI {Version}.", nameof(Version))
                   }
               );

        /// <summary>
        /// Make the file the library keeps the peers of a version in
        /// unwritable: a directory where it is, which stops root as well. What
        /// it held is put aside, for the next start to find again.
        /// </summary>
        private String BlockPeersFile(String Version)
        {

            var file = PeersFile(Version);

            if (File.Exists(file))
                File.Move(file, file + ".aside");

            System.IO.Directory.CreateDirectory(file);

            return file;

        }

        /// <summary>
        /// Give the file of the peers of a version back what it held, while
        /// this hub runs.
        /// </summary>
        private void UnblockPeersFile(String Version)
        {

            var file = PeersFile(Version);

            System.IO.Directory.Delete(file);

            if (File.Exists(file + ".aside"))
                File.Move(file + ".aside", file);

        }

        #endregion

        #region (private) PeerAfterARestart(Version, BlockedPeersFile = null)

        /// <summary>
        /// The peer every test here adds, as the next start in the same
        /// directory knows it on the given version, or null - see
        /// PeersAfterARestart.
        /// </summary>
        private async Task<RemotePartySummary?> PeerAfterARestart(String   Version,
                                                                  String?  BlockedPeersFile   = null)

            => (await PeersAfterARestart(hub => hub.OCPIVersions.
                                                    Where     (version => version.Label == Version).
                                                    SelectMany(version => version.RemoteParties).
                                                    ToArray(),
                                         BlockedPeersFile)).
                   FirstOrDefault(peer => peer.Id.ToString() == PeerId);

        #endregion

        #region (private) PeersAfterARestart(BlockedPeersFile = null)

        /// <summary>
        /// Stop this hub, give a peers' file that was blocked back what it
        /// held, and ask the next start in the same directory which peers it
        /// knows.
        /// </summary>
        private async Task<String[]> PeersAfterARestart(String? BlockedPeersFile = null)

            => await PeersAfterARestart(hub => hub.OCPIVersions.
                                                   SelectMany(version => version.RemoteParties).
                                                   Select    (peer    => peer.Id.ToString()).
                                                   ToArray(),
                                        BlockedPeersFile);

        /// <summary>
        /// Stop this hub, give a peers' file that was blocked back what it
        /// held, and ask the next start in the same directory.
        /// </summary>
        private async Task<T> PeersAfterARestart<T>(Func<RoamingHub, T>  Ask,
                                                    String?              BlockedPeersFile   = null)
        {

            await RoamingHub.Stop();

            if (BlockedPeersFile is not null)
            {

                System.IO.Directory.Delete(BlockedPeersFile);

                if (File.Exists(BlockedPeersFile + ".aside"))
                    File.Move(BlockedPeersFile + ".aside", BlockedPeersFile);

            }

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {
                return Ask(again);
            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion


        #region (private class) HubWhoseStopCanFail

        /// <summary>
        /// A hub as TestRoamingHubs builds any other, whose next stop can be
        /// made to fail - the way stopping a server that had not begun to
        /// listen yet once failed.
        /// </summary>
        private sealed class HubWhoseStopCanFail(String          AccountsPath,
                                                 WWCPConfigFile  ConfigFile,
                                                 TimeProvider?   Clock)

            : RoamingHub(HTTPPort:        IPPort.Parse(TestPorts.Free()),
                         AccountsPath:    AccountsPath,
                         ConfigFile:      ConfigFile,
                         LogToConsole:    false,
                         BridgeDebugLog:  false,
                         TimeProvider:    Clock)

        {

            /// <summary>
            /// Whether the next stop fails, once this hub has ended what it
            /// ends before its server stops. The node below closes the server
            /// all the same, and a stop after that does nothing.
            /// </summary>
            public Boolean NextStopFails { get; set; }

            protected override async Task OnStopping()
            {

                await base.OnStopping();

                if (NextStopFails)
                    throw new InvalidOperationException("This hub was made to fail to stop.");

            }

        }

        #endregion

    }

}
