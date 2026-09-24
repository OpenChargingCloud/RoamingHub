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
using System.Net.Http.Headers;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.RoamingHub.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The peering, over the wire and from either end: a peer added and given
    /// a token, the peer coming here with it, and this hub going to a peer
    /// that handed one out.
    /// </summary>
    /// <remarks>
    /// The peer in these tests is a plain HTTP client with an OCPI token,
    /// which is what a CPO or an EMSP is from where this hub stands, and - for
    /// the direction where this hub has to call somebody - a stub with the
    /// three routes the credentials handshake touches.
    /// </remarks>
    public class PeeringTests : ARoamingHubTests
    {

        #region (private) Peer(Token)

        /// <summary>
        /// A peer calling this hub with the token it was given, encoded the
        /// way OCPI 2.2 sends it.
        /// </summary>
        private HttpClient Peer(String Token)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                           "Token",
                                                           Convert.ToBase64String(Encoding.UTF8.GetBytes(Token))
                                                       );

            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return http;

        }

        #endregion

        #region (private) AddPeer(HTTP, ...)

        /// <summary>
        /// Add a peer through the JSON API and hand back the token this hub
        /// made up for it.
        /// </summary>
        private async Task<(String Id, String Token, JObject Answer)> AddPeer(HttpClient  HTTP,
                                                                              String      Role          = "CPO",
                                                                              String      CountryCode   = "DE",
                                                                              String      PartyId       = "GEF",
                                                                              String?     TheirToken    = null,
                                                                              String?     VersionsURL   = null)
        {

            var body = new List<JProperty> {
                           new ("version",      "2.2.1"),
                           new ("countryCode",  CountryCode),
                           new ("partyId",      PartyId),
                           new ("role",         Role),
                           new ("name",         $"Test {Role}")
                       };

            if (TheirToken  is not null)  body.Add(new JProperty("theirToken",  TheirToken));
            if (VersionsURL is not null)  body.Add(new JProperty("versionsURL", VersionsURL));

            var response = await HTTP.PostAsync("/api/v1/ocpi/partners", JSONBody([.. body]));
            var text     = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), $"Adding the peer answered {(Int32) response.StatusCode}: {text}");

            var answer = JObject.Parse(text);

            return (answer.Value<String>("id")!, answer.Value<String>("ourToken")!, answer);

        }

        #endregion

        #region (private static) OCPIResponse(Response)

        private static async Task<JObject> OCPIResponse(HttpResponseMessage Response)
        {

            var text = await Response.Content.ReadAsStringAsync();

            Assert.That(Response.IsSuccessStatusCode, Is.True, $"{Response.RequestMessage?.Method} {Response.RequestMessage?.RequestUri} answered {(Int32) Response.StatusCode}: {text}");

            return JObject.Parse(text);

        }

        #endregion

        #region TheVersionsAreListedAndSay2_2_1_AndUp()

        /// <summary>
        /// A hub offers no 2.1.1 and cannot: the role arrived with OCPI 2.2.
        /// A peer that reads the versions list has to be able to see that
        /// without being told.
        /// </summary>
        [Test]
        public async Task TheVersionsAreListedAndSay2_2_1_AndUp()
        {

            using var http = Anonymous();

            var answer   = await OCPIResponse(await http.GetAsync("/ext/versions"));
            var versions = answer["data"] as JArray;

            Assert.That(versions, Is.Not.Null, "The versions list carries no data.");

            var listed = versions!.Select(version => version.Value<String>("version")).ToArray();

            Assert.Multiple(() => {

                Assert.That(answer.Value<Int32>("status_code"), Is.EqualTo(1000));
                Assert.That(listed, Is.EquivalentTo(OCPIConfiguration.DefaultVersions));
                Assert.That(listed, Does.Not.Contain("2.1.1"), "A hub is offering OCPI 2.1.1, which has no hub role.");

                foreach (var version in versions)
                    Assert.That(version.Value<String>("url"),
                                Is.EqualTo($"{BaseURL.TrimEnd('/')}/ext/versions/{version.Value<String>("version")}"),
                                "A version is advertised at a URL this hub does not serve.");

            });

        }

        #endregion

        #region ThisHubSaysItIsAHub()

        /// <summary>
        /// What a peer reads out of the credentials: the role, which is the
        /// whole difference between this and the other two.
        /// </summary>
        [Test]
        public async Task ThisHubSaysItIsAHub()
        {

            using var admin = await SignedIn();

            var (id, token, _) = await AddPeer(admin);

            Assert.That(id, Is.EqualTo("DE-GEF_CPO"));

            using var peer = Peer(token);

            var credentials = (await OCPIResponse(await peer.GetAsync("/ext/v2.2.1/credentials")))["data"];

            Assert.Multiple(() => {
                Assert.That(credentials?.Value<String>("token"),                         Is.EqualTo(token));
                Assert.That(credentials?.Value<String>("url"),                           Is.EqualTo($"{BaseURL.TrimEnd('/')}/ext/versions"));
                Assert.That(credentials?["roles"]?.First?.Value<String>("role"),         Is.EqualTo("HUB"));
                Assert.That(credentials?["roles"]?.First?.Value<String>("party_id"),     Is.EqualTo("GDH"));
                Assert.That(credentials?["roles"]?.First?.Value<String>("country_code"), Is.EqualTo("DE"));
            });

        }

        #endregion

        #region TheServerHeaderNamesThisHub()

        /// <summary>
        /// What a peer's HTTP client is told it reached, in the Server header
        /// of every answer on its way to the credentials: this hub, by the
        /// name its HTTP server and its JSON API already give.
        /// </summary>
        /// <remarks>
        /// Three answers rather than one, because two parts of the OCPI
        /// library give them and each is handed a name of its own: the
        /// versions list comes from the Common HTTP API every version hangs
        /// off, the version details and the credentials from the Common API
        /// of the version. The first of those said "EMSP", a leftover of the
        /// program this hub was derived from - and it is the first thing a
        /// peer asks, so every peering began with this hub introducing itself
        /// as an EMSP.
        /// </remarks>
        [Test]
        public async Task TheServerHeaderNamesThisHub()
        {

            using var admin = await SignedIn();

            var (_, token, _) = await AddPeer(admin);

            using var peer = Peer(token);

            var versions     = await peer.GetAsync("/ext/versions");
            var details      = await peer.GetAsync("/ext/versions/2.2.1");
            var credentials  = await peer.GetAsync("/ext/v2.2.1/credentials");

            var thisHub      = $"OpenChargingCloud RoamingHub v{RoamingHub.Version}";

            Assert.Multiple(() => {

                // Answered by the OCPI library, and not by the server turning
                // the request away: a 404 carries the server's own name, which
                // is the right one already, and would prove nothing.
                Assert.That(versions.   StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(details.    StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(credentials.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                Assert.That(versions.   Headers.Server.ToString(), Is.EqualTo(thisHub), "The versions list names somebody else.");
                Assert.That(details.    Headers.Server.ToString(), Is.EqualTo(thisHub), "The version details name somebody else.");
                Assert.That(credentials.Headers.Server.ToString(), Is.EqualTo(thisHub), "The credentials name somebody else.");

            });

        }

        #endregion

        #region BothKindsOfPeerAreTakenAndNothingElseIs()

        /// <summary>
        /// A hub is a room, and the two roles that belong in it are the ends
        /// of a roaming agreement. A hub peers with a hub as well; a charging
        /// station operator's service provider platform does not become an
        /// "NSP" by being typed in as one.
        /// </summary>
        [Test]
        public async Task BothKindsOfPeerAreTakenAndNothingElseIs()
        {

            using var admin = await SignedIn();

            var (cpo,  _, _) = await AddPeer(admin, Role: "CPO",  PartyId: "GEF");
            var (emsp, _, _) = await AddPeer(admin, Role: "EMSP", PartyId: "GDF");

            Assert.Multiple(() => {
                Assert.That(cpo,  Is.EqualTo("DE-GEF_CPO"));
                Assert.That(emsp, Is.EqualTo("DE-GDF_EMSP"));
            });

            var refused = await admin.PostAsync("/api/v1/ocpi/partners", JSONBody(
                                    new JProperty("version",      "2.2.1"),
                                    new JProperty("countryCode",  "DE"),
                                    new JProperty("partyId",      "NSP"),
                                    new JProperty("role",         "NSP"),
                                    new JProperty("name",         "Somebody else")
                                ));

            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await refused.Content.ReadAsStringAsync(), Does.Contain("CPO, EMSP and HUB"));

        }

        #endregion

        #region ThisHubCannotBeItsOwnPeer()

        [Test]
        public async Task ThisHubCannotBeItsOwnPeer()
        {

            using var admin = await SignedIn();

            var itself = await admin.PostAsync("/api/v1/ocpi/partners", JSONBody(
                                   new JProperty("version",      "2.2.1"),
                                   new JProperty("countryCode",  "DE"),
                                   new JProperty("partyId",      "GDH"),
                                   new JProperty("role",         "HUB"),
                                   new JProperty("name",         "Us, again")
                               ));

            Assert.That(itself.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        }

        #endregion

        #region APeerRegistersWithThisHubOfItsOwnAccord()

        /// <summary>
        /// The first direction: the peer was handed a token, and it comes
        /// here - fetches the versions, finds the credentials endpoint, and
        /// POSTs its own credentials.
        /// </summary>
        /// <remarks>
        /// This hub then calls the peer back to fetch its versions before it
        /// answers, which is why a stub is listening for this direction too:
        /// a credentials POST naming a URL nobody answers at is not a
        /// registration.
        /// </remarks>
        [Test]
        public async Task APeerRegistersWithThisHubOfItsOwnAccord()
        {

            using var admin = await SignedIn();

            await using var cpo = await StubPeer.Start();

            var (id, ourToken, added) = await AddPeer(admin);

            Assert.That(added["partner"]?.Value<Boolean>("canRegister"), Is.False,
                        "Nothing was said about where the peer is and this hub thinks it could go there.");

            using var peer = Peer(ourToken);

            var versions  = (await OCPIResponse(await peer.GetAsync("/ext/versions")))["data"] as JArray;

            var details   = versions!.First(version => version.Value<String>("version") == "2.2.1").Value<String>("url");

            var endpoints = (await OCPIResponse(await peer.GetAsync(details)))["data"]?["endpoints"] as JArray;

            var credentialsURL = endpoints!.First(endpoint => endpoint.Value<String>("identifier") == "credentials").Value<String>("url")!;

            var posted = await peer.PostAsync(
                                   credentialsURL,
                                   new StringContent(
                                       new JObject(
                                           new JProperty("token",  StubPeer.TokenC),
                                           new JProperty("url",    cpo.VersionsURL),
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
                                   )
                               );

            var postedText = await posted.Content.ReadAsStringAsync();

            Assert.That(posted.IsSuccessStatusCode, Is.True, $"POST credentials answered {(Int32) posted.StatusCode}: {postedText}");

            var envelope = JObject.Parse(postedText);

            Assert.That(envelope.Value<Int32>("status_code"), Is.EqualTo(1000), postedText);

            var ours = envelope["data"];

            Assert.Multiple(() => {

                Assert.That(ours?.Value<String>("url"),                           Is.EqualTo(RoamingHub.OCPIVersionsURL.ToString()));
                Assert.That(ours?["roles"]?.First?.Value<String>("role"),         Is.EqualTo("HUB"));
                Assert.That(ours?["roles"]?.First?.Value<String>("party_id"),     Is.EqualTo("GDH"));

                // A fresh token: the one handed over by hand opened the door
                // once and is spent.
                Assert.That(ours?.Value<String>("token"), Is.Not.Empty);
                Assert.That(ours?.Value<String>("token"), Is.Not.EqualTo(ourToken));

            });

            var after = ((await GetJSON(admin, "/api/v1/ocpi/partners"))["partners"] as JArray)!.
                            First(candidate => candidate.Value<String>("id") == id);

            Assert.Multiple(() => {
                Assert.That(after.Value<Boolean>("registered"),      Is.True, "The peer registered and this hub does not say so.");
                Assert.That(after.Value<String>("theirToken"),       Is.EqualTo(StubPeer.TokenC));
                Assert.That(after.Value<String>("theirVersionsURL"), Is.EqualTo(cpo.VersionsURL));
                Assert.That(cpo.TokensSeen, Does.Contain(StubPeer.TokenC),
                            "This hub never called the peer back with the token it was sent.");
            });

        }

        #endregion

        #region ThisHubRegistersWithAPeerOfItsOwnAccord(Role, PartyId)

        /// <summary>
        /// The other direction: the peer handed out a token and its versions
        /// URL, and this hub goes there - fetches the versions, finds the
        /// credentials endpoint, POSTs its own credentials, and takes the
        /// peer's token from the answer.
        /// </summary>
        /// <remarks>
        /// Both kinds of peer, because which client this hub reaches one with
        /// depends on the peer's role - a hub talks to a CPO where an EMSP
        /// would and to an EMSP where a CPO would - and a test that only ran
        /// one of them would leave half of that untried.
        /// </remarks>
        [TestCase("CPO",  "GEF")]
        [TestCase("EMSP", "GDF")]
        public async Task ThisHubRegistersWithAPeerOfItsOwnAccord(String Role, String PartyId)
        {

            using var admin = await SignedIn();

            await using var peer = await StubPeer.Start(Role, PartyId);

            var (id, ourTokenBefore, _) = await AddPeer(
                                                    admin,
                                                    Role:         Role,
                                                    PartyId:      PartyId,
                                                    TheirToken:   StubPeer.TokenA,
                                                    VersionsURL:  peer.VersionsURL
                                                );

            var before = ((await GetJSON(admin, "/api/v1/ocpi/partners"))["partners"] as JArray)!.
                             First(candidate => candidate.Value<String>("id") == id);

            Assert.Multiple(() => {
                Assert.That(before.Value<Boolean>("canRegister"), Is.True,  "A peer with a token and a versions URL is not offered for registration.");
                Assert.That(before.Value<Boolean>("registered"),  Is.False);
            });

            var register = await admin.PostAsync($"/api/v1/ocpi/partners/2.2.1/{id}/register", JSONBody());
            var text     = await register.Content.ReadAsStringAsync();

            Assert.That(register.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"The registration answered {(Int32) register.StatusCode}: {text}");

            var answer = JObject.Parse(text);

            Assert.That(answer.Value<Boolean>("ok"), Is.True, answer.Value<String>("message"));

            var after = (answer["partners"]?["partners"] as JArray)!.First(candidate => candidate.Value<String>("id") == id);

            Assert.Multiple(() => {

                Assert.That(after.Value<Boolean>("registered"),  Is.True, "The registration went through and the peer is not reported as registered.");
                Assert.That(after.Value<String>("theirToken"),   Is.EqualTo(StubPeer.TokenC), "The token the peer handed out in its answer was not taken.");
                Assert.That(after.Value<String>("remoteStatus"), Is.EqualTo("ONLINE"));

                // What the peer was told about us.
                Assert.That(peer.ReceivedCredentials, Is.Not.Null, "The peer never received this hub's credentials.");
                Assert.That(peer.ReceivedCredentials?.Value<String>("url"),                        Is.EqualTo(RoamingHub.OCPIVersionsURL.ToString()));
                Assert.That(peer.ReceivedCredentials?["roles"]?.First?.Value<String>("role"),      Is.EqualTo("HUB"));
                Assert.That(peer.ReceivedCredentials?["roles"]?.First?.Value<String>("party_id"),  Is.EqualTo("GDH"));

                // The token this hub sent is the one the peer must use from
                // now on - a fresh one, and the one the list shows.
                Assert.That(peer.ReceivedCredentials?.Value<String>("token"), Is.EqualTo(after.Value<String>("ourToken")));
                Assert.That(peer.ReceivedCredentials?.Value<String>("token"), Is.Not.EqualTo(ourTokenBefore));

                // And it presented the token the peer had handed out.
                Assert.That(peer.TokensSeen, Does.Contain(StubPeer.TokenA));

            });

        }

        #endregion

        #region ThePeersAreKeptBetweenStarts()

        [Test]
        public async Task ThePeersAreKeptBetweenStarts()
        {

            using var admin = await SignedIn();

            var (id, token, _) = await AddPeer(admin);

            await RoamingHub.Stop();

            var again = TestRoamingHubs.New(Directory, Configuration, Clock);

            try
            {

                await again.Start();

                var kept = again.OCPIVersions.SelectMany(version => version.RemoteParties).ToArray();

                Assert.Multiple(() => {
                    Assert.That(kept.Select(peer => peer.Id.ToString()), Does.Contain(id));
                    Assert.That(kept.First(peer => peer.Id.ToString() == id).OurToken?.ToString(), Is.EqualTo(token));
                    Assert.That(kept.First(peer => peer.Id.ToString() == id).Version,              Is.EqualTo("2.2.1"));
                });

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

    }

}
