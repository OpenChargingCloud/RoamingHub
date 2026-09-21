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

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The HubClientInfo module: who is on this hub, and whether they can be
    /// reached.
    /// </summary>
    /// <remarks>
    /// Against a running hub on a real port, the way the rest of these tests
    /// work, and over both surfaces it has: the OCPI endpoint a peer asks,
    /// and the JSON API the web interface reads. They have to agree, which is
    /// the point of the hub keeping one answer and handing it to both.
    ///
    /// OCPI 2.3.0 throughout, because that is where the module and the two
    /// credentials changes that go with it are implemented.
    /// </remarks>
    public class HubClientInfoTests : ARoamingHubTests
    {

        #region Configuration - a hub that offers 2.3.0

        /// <summary>
        /// The default is 2.2.1 alone, and everything here is about 2.3.0.
        /// </summary>
        protected override JObject Configuration

            => new (
                   new JProperty("nts",   new JObject(
                       new JProperty("enabled",  false)
                   )),
                   new JProperty("ocpi",  new JObject(
                       new JProperty("versions",  new JArray("2.3.0"))
                   ))
               );

        #endregion


        #region AnAddedPeerIsPlannedAndShowsUpOverOCPI()

        /// <summary>
        /// A peer that was added but has not registered is PLANNED - the
        /// contracts are not established - and every other peer can already
        /// see it.
        /// </summary>
        [Test]
        public async Task AnAddedPeerIsPlannedAndShowsUpOverOCPI()
        {

            using var admin = await SignedIn();

            var (id, ourToken, _) = await AddPeer(admin);

            #region ... as the web interface reads it

            var partner = await Partner(admin, id);

            Assert.Multiple(() => {
                Assert.That(partner.Value<String>("connection"),  Is.EqualTo("PLANNED"));
                Assert.That(partner.Value<Boolean>("registered"), Is.False);
                Assert.That(partner.Value<String>("lastSeen"),    Is.Null, "A peer that has never called is reported as seen.");
            });

            #endregion

            #region ... and as a peer asks for it

            var clientInfos = await HubClientInfo(ourToken);

            Assert.That(clientInfos, Has.Count.EqualTo(1));

            Assert.Multiple(() => {
                Assert.That(clientInfos[0].Value<String>("country_code"),  Is.EqualTo("DE"));
                Assert.That(clientInfos[0].Value<String>("party_id"),      Is.EqualTo("GEF"));
                Assert.That(clientInfos[0].Value<String>("role"),          Is.EqualTo("CPO"));
                Assert.That(clientInfos[0].Value<String>("status"),        Is.EqualTo("PLANNED"));
                Assert.That(clientInfos[0].Value<String>("last_updated"),  Is.Not.Null);
            });

            #endregion

        }

        #endregion

        #region TheCredentialsNameTheHubAndTheReachableParties()

        /// <summary>
        /// The two things OCPI 2.3.0 changed for a hub: it names itself in
        /// "hub_party_id", and its "roles" list the parties reachable through
        /// it rather than only itself.
        /// </summary>
        /// <remarks>
        /// In 2.2 and 2.2.1 the second of these was not so, which is why a
        /// peer of those versions learns who else is on the hub from the
        /// HubClientInfo module or not at all.
        /// </remarks>
        [Test]
        public async Task TheCredentialsNameTheHubAndTheReachableParties()
        {

            using var admin = await SignedIn();

            var (_, ourToken, _) = await AddPeer(admin);

            using var peer = Peer(ourToken);

            var credentials = (await OCPIResponse(await peer.GetAsync("/ext/v2.3.0/credentials")))["data"]!;

            Assert.That(credentials.Value<String>("hub_party_id"), Is.EqualTo("DEGDH"),
                        "A hub has to name itself in the credentials it hands out.");

            var roles = (credentials["roles"] as JArray)!.
                            Select(role => $"{role.Value<String>("country_code")}-{role.Value<String>("party_id")}/{role.Value<String>("role")}").
                            ToArray();

            Assert.Multiple(() => {
                Assert.That(roles, Does.Contain("DE-GDH/HUB"),  "The hub left itself out of its own credentials.");
                Assert.That(roles, Does.Contain("DE-GEF/CPO"),  "A party reachable through this hub is missing from its credentials.");
            });

        }

        #endregion

        #region ASuspendedPeerStaysButIsNoLongerOffered()

        /// <summary>
        /// Suspending is not deleting, and the difference is the whole reason
        /// the specification has no deletion here: the peer stays in the
        /// HubClientInfo list so that everybody learns it is switched off,
        /// and it leaves the credentials so that nobody is invited to address
        /// it any more.
        /// </summary>
        [Test]
        public async Task ASuspendedPeerStaysButIsNoLongerOffered()
        {

            using var admin = await SignedIn();

            var (id, ourToken, _) = await AddPeer(admin);

            #region Suspend it

            var suspended = await admin.PostAsync("/api/v1/ocpi/peers/DEGEF/suspend", EmptyBody);

            Assert.That(suspended.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                        await suspended.Content.ReadAsStringAsync());

            #endregion

            #region It is still there, and it is switched off

            var clientInfos = await HubClientInfo(ourToken);

            Assert.That(clientInfos, Has.Count.EqualTo(1), "A suspended peer was deleted; the specification forbids it.");
            Assert.That(clientInfos[0].Value<String>("status"), Is.EqualTo("SUSPENDED"));

            Assert.That((await Partner(admin, id)).Value<String>("connection"), Is.EqualTo("SUSPENDED"));

            #endregion

            #region ... and nobody is invited to address it

            using var peer = Peer(ourToken);

            var roles = ((await OCPIResponse(await peer.GetAsync("/ext/v2.3.0/credentials")))["data"]!["roles"] as JArray)!.
                            Select(role => role.Value<String>("party_id")).
                            ToArray();

            Assert.That(roles, Does.Not.Contain("GEF"),
                        "A suspended party is still advertised in the credentials.");

            #endregion

            #region Resume puts it back

            var resumed = await admin.PostAsync("/api/v1/ocpi/peers/DEGEF/resume", EmptyBody);

            Assert.That(resumed.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            Assert.That((await Partner(admin, id)).Value<String>("connection"), Is.EqualTo("PLANNED"),
                        "A resumed peer did not go back to what it actually is.");

            #endregion

        }

        #endregion

        #region ACallFromAPeerIsTheLivenessSignal()

        /// <summary>
        /// A hub does not have to ask whether its peers are there: every OCPI
        /// call they make says so, and that is what is written down.
        /// </summary>
        [Test]
        public async Task ACallFromAPeerIsTheLivenessSignal()
        {

            using var admin = await SignedIn();

            var (id, ourToken, _) = await AddPeer(admin);

            Assert.That((await Partner(admin, id)).Value<String>("lastSeen"), Is.Null);

            using var peer = Peer(ourToken);

            var answered = await peer.GetAsync("/ext/versions");

            Assert.That(answered.IsSuccessStatusCode, Is.True);

            Assert.That((await Partner(admin, id)).Value<String>("lastSeen"), Is.Not.Null,
                        "A peer called this hub and the hub did not notice it was there.");

        }

        #endregion

        #region AStrangerIsRefusedTheList()

        /// <summary>
        /// Who is on this hub is the business of the parties that are on it.
        /// </summary>
        /// <remarks>
        /// 401 and not 403, and the difference is worth keeping: a token this
        /// hub has never issued does not identify anybody, so the request is
        /// turned away before it reaches the module at all. A token that is
        /// known but blocked reaches it and is answered 403 - that is a party
        /// this hub recognises and will not serve.
        /// </remarks>
        [Test]
        public async Task AStrangerIsRefusedTheList()
        {

            using var admin = await SignedIn();

            await AddPeer(admin);

            using var stranger = Peer("nobody-handed-this-out");

            var refused = await stranger.GetAsync("/ext/v2.3.0/hubclientinfo");

            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                        "A stranger was told who is on this hub.");

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
                                                                              String      PartyId       = "GEF")
        {

            var response = await HTTP.PostAsync(
                                     "/api/v1/ocpi/partners",
                                     new StringContent(
                                         new JObject(
                                             new JProperty("version",      "2.3.0"),
                                             new JProperty("countryCode",  CountryCode),
                                             new JProperty("partyId",      PartyId),
                                             new JProperty("role",         Role),
                                             new JProperty("name",         "Test CPO")
                                         ).ToString(),
                                         Encoding.UTF8,
                                         "application/json"
                                     )
                                 );

            var text = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Adding the peer answered {(Int32) response.StatusCode}: {text}");

            var answer = JObject.Parse(text);

            return (answer.Value<String>("id")!,
                    answer.Value<String>("ourToken")!,
                    answer);

        }

        #endregion

        #region (private) Partner(HTTP, Id) / HubClientInfo(Token) / Peer(Token)

        /// <summary>
        /// One peer as the web interface reads it.
        /// </summary>
        private async Task<JObject> Partner(HttpClient HTTP, String Id)
        {

            var response = await HTTP.GetAsync("/api/v1/ocpi/partners");
            var partners = JObject.Parse(await response.Content.ReadAsStringAsync())["partners"] as JArray;

            return (JObject) partners!.First(candidate => candidate.Value<String>("id") == Id);

        }

        /// <summary>
        /// The HubClientInfo list as a peer asks for it.
        /// </summary>
        private async Task<List<JToken>> HubClientInfo(String Token)
        {

            using var peer = Peer(Token);

            var envelope = await OCPIResponse(await peer.GetAsync("/ext/v2.3.0/hubclientinfo"));

            return (envelope["data"] as JArray)?.ToList() ?? [];

        }

        /// <summary>
        /// An HTTP client that talks as a peer holding the given token.
        /// </summary>
        /// <remarks>
        /// Base64, because that is how OCPI 2.2 and later write a token in
        /// the Authorization header.
        /// </remarks>
        private HttpClient Peer(String Token)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                           "Token",
                                                           Convert.ToBase64String(Encoding.UTF8.GetBytes(Token))
                                                       );

            return http;

        }

        #endregion

        #region (private static) OCPIResponse(Response) / EmptyBody

        /// <summary>
        /// The OCPI envelope of a response, checked for the status code that
        /// says it worked.
        /// </summary>
        private static async Task<JObject> OCPIResponse(HttpResponseMessage Response)
        {

            var text = await Response.Content.ReadAsStringAsync();

            Assert.That(Response.IsSuccessStatusCode, Is.True,
                        $"The request answered {(Int32) Response.StatusCode}: {text}");

            var envelope = JObject.Parse(text);

            Assert.That(envelope.Value<Int32>("status_code"), Is.EqualTo(1000), text);

            return envelope;

        }

        private static StringContent EmptyBody
            => new ("{}", Encoding.UTF8, "application/json");

        #endregion

    }

}
