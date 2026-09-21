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
    /// What the hub writes down about its peers: the calls, who they were
    /// between, and the stream they arrive on.
    /// </summary>
    /// <remarks>
    /// The point of all of it is one question - "which peer talked to whom,
    /// about what" - so every test here asks a piece of that question of the
    /// JSON API rather than of the log object, because that is where somebody
    /// tracking a peering down would ask it.
    /// </remarks>
    public class TrafficTests : AHubTests
    {

        #region (private) Peer(Token, From, To)

        /// <summary>
        /// A peer calling this hub, saying who it is and who it is calling
        /// about - which is what the OCPI headers are for and what makes a
        /// line in the traffic more than a list of paths.
        /// </summary>
        private HttpClient Peer(String   Token,
                                String?  FromCountry   = null,
                                String?  FromParty     = null,
                                String?  ToCountry     = null,
                                String?  ToParty       = null)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                           "Token",
                                                           Convert.ToBase64String(Encoding.UTF8.GetBytes(Token))
                                                       );

            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (FromCountry is not null)  http.DefaultRequestHeaders.Add("OCPI-from-country-code", FromCountry);
            if (FromParty   is not null)  http.DefaultRequestHeaders.Add("OCPI-from-party-id",     FromParty);
            if (ToCountry   is not null)  http.DefaultRequestHeaders.Add("OCPI-to-country-code",   ToCountry);
            if (ToParty     is not null)  http.DefaultRequestHeaders.Add("OCPI-to-party-id",       ToParty);

            return http;

        }

        #endregion

        #region (private) AddPeer(HTTP, Role, PartyId)

        private async Task<String> AddPeer(HttpClient  HTTP,
                                           String      Role      = "CPO",
                                           String      PartyId   = "GEF")
        {

            var response = await HTTP.PostAsync("/api/v1/ocpi/partners", JSONBody(
                                     new JProperty("version",      "2.2.1"),
                                     new JProperty("countryCode",  "DE"),
                                     new JProperty("partyId",      PartyId),
                                     new JProperty("role",         Role),
                                     new JProperty("name",         $"Test {Role}")
                                 ));

            var text = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), text);

            return JObject.Parse(text).Value<String>("ourToken")!;

        }

        #endregion

        #region (private) Calls(HTTP, Query)

        private static async Task<JArray> Calls(HttpClient  HTTP,
                                                String      Query   = "")
        {

            var answer = await GetJSON(HTTP, $"/api/v1/traffic{Query}");

            return answer["calls"] as JArray ?? [];

        }

        #endregion

        #region ACallByAPeerIsWrittenDownWithBothItsParties()

        /// <summary>
        /// The question a hub exists to answer: who called, who it was for,
        /// what was asked, and what came back.
        /// </summary>
        [Test]
        public async Task ACallByAPeerIsWrittenDownWithBothItsParties()
        {

            using var admin = await SignedIn();

            var token = await AddPeer(admin);

            using var peer = Peer(token, "DE", "GEF", "DE", "GDF");

            var asked = await peer.GetAsync("/ext/v2.2.1/credentials");

            Assert.That(asked.IsSuccessStatusCode, Is.True);

            var calls = await Calls(admin);

            Assert.That(calls, Is.Not.Empty, "A peer called and the traffic log is empty.");

            var call = calls.Last();

            Assert.Multiple(() => {

                Assert.That(call.Value<String>("direction"),      Is.EqualTo("in"));
                Assert.That(call.Value<String>("peer"),           Is.EqualTo("DE-GEF_CPO"), "The token was ours and the call does not name the peer it belongs to.");
                Assert.That(call.Value<String>("from"),           Is.EqualTo("DE-GEF"));
                Assert.That(call.Value<String>("to"),             Is.EqualTo("DE-GDF"));
                Assert.That(call.Value<String>("version"),        Is.EqualTo("2.2.1"));
                Assert.That(call.Value<String>("module"),         Is.EqualTo("credentials"));
                Assert.That(call.Value<String>("method"),         Is.EqualTo("GET"));
                Assert.That(call.Value<Int32>("httpStatusCode"),  Is.EqualTo(200));
                Assert.That(call.Value<Int32>("ocpiStatusCode"),  Is.EqualTo(1000), "The OCPI envelope was not read, so a refusal answered with 200 would read as a success.");
                Assert.That(call.Value<Boolean>("ok"),            Is.True);

                // Not kept unless asked for: what travels through a hub is
                // its peers' business.
                Assert.That(call.Value<String>("requestBody"),    Is.Null);
                Assert.That(call.Value<String>("responseBody"),   Is.Null);

            });

        }

        #endregion

        #region ACallByAStrangerIsWrittenDownToo()

        /// <summary>
        /// The case somebody is actually looking at the traffic for: a peer
        /// that cannot get in. It has no name here, because the token was not
        /// one of ours, and the socket is what is left to go on.
        /// </summary>
        [Test]
        public async Task ACallByAStrangerIsWrittenDownToo()
        {

            using var stranger = Peer("nobody-gave-me-this");

            var refused = await stranger.GetAsync("/ext/versions");

            Assert.That(refused.IsSuccessStatusCode, Is.False);

            using var admin = await SignedIn();

            var call = (await Calls(admin)).Last();

            Assert.Multiple(() => {
                Assert.That(call.Value<String>("peer"),           Is.Null, "A token this hub does not know was matched to a peer.");
                Assert.That(call.Value<String>("remoteSocket"),   Is.Not.Null);
                Assert.That(call.Value<Int32>("httpStatusCode"),  Is.EqualTo(401));
                Assert.That(call.Value<Boolean>("ok"),            Is.False);
                Assert.That(call.Value<String>("module"),         Is.EqualTo("versions"));
            });

        }

        #endregion

        #region TheHubsOwnBusinessIsNotTraffic()

        /// <summary>
        /// The JSON API and the web interface are this hub's own and belong
        /// in the event log. A traffic log that filled up with a browser
        /// polling would be one nobody could read.
        /// </summary>
        [Test]
        public async Task TheHubsOwnBusinessIsNotTraffic()
        {

            using var admin = await SignedIn();

            await GetJSON(admin, "/api/v1/status");
            await GetJSON(admin, "/api/v1/configuration");

            Assert.That(await Calls(admin), Is.Empty, "Something that was not an OCPI call was written to the traffic log.");

        }

        #endregion

        #region TheTrafficCanBeNarrowedToOnePeer()

        /// <summary>
        /// "What has DE*GEF been doing here?" - the question somebody walks
        /// up with, and a party at either end of a call answers it.
        /// </summary>
        [Test]
        public async Task TheTrafficCanBeNarrowedToOnePeer()
        {

            using var admin = await SignedIn();

            var cpoToken   = await AddPeer(admin, "CPO",  "GEF");
            var emspToken  = await AddPeer(admin, "EMSP", "GDF");

            using var cpo  = Peer(cpoToken,  "DE", "GEF", "DE", "GDF");
            using var emsp = Peer(emspToken, "DE", "GDF", "DE", "GEF");

            await cpo. GetAsync("/ext/v2.2.1/credentials");
            await emsp.GetAsync("/ext/versions");
            await emsp.GetAsync("/ext/versions");

            var all      = await Calls(admin);
            var justCPO  = await Calls(admin, "?peer=DE-GEF_CPO");
            var byParty  = await Calls(admin, "?peer=DE-GDF");

            Assert.Multiple(() => {

                Assert.That(all.Count, Is.EqualTo(3));

                // The peer as this hub knows it: one call came in on its token.
                Assert.That(justCPO.Count, Is.EqualTo(1));

                // And the party as the headers write it, which catches the
                // calls it was the other end of as well: the CPO's one call
                // was addressed to DE-GDF, and the EMSP made two of its own.
                Assert.That(byParty.Count, Is.EqualTo(3));

            });

        }

        #endregion

        #region AReaderCatchesUpWithoutReadingEverythingAgain()

        /// <summary>
        /// What "after" is for: a reader that has been away asks for what it
        /// missed, which is how the page and the stream stay one picture.
        /// </summary>
        [Test]
        public async Task AReaderCatchesUpWithoutReadingEverythingAgain()
        {

            using var admin = await SignedIn();

            var token = await AddPeer(admin);

            using var peer = Peer(token);

            await peer.GetAsync("/ext/versions");

            var first  = await GetJSON(admin, "/api/v1/traffic");
            var lastId = first.Value<UInt64>("lastId");

            Assert.That(lastId, Is.GreaterThan(0));

            await peer.GetAsync("/ext/versions");
            await peer.GetAsync("/ext/v2.2.1/credentials");

            var since = (await GetJSON(admin, $"/api/v1/traffic?after={lastId}"))["calls"] as JArray;

            Assert.Multiple(() => {
                Assert.That(since?.Count, Is.EqualTo(2), "Asking for everything after an identification did not hand back exactly what came after it.");
                Assert.That(since?.All(call => call.Value<UInt64>("id") > lastId), Is.True);
            });

        }

        #endregion

        #region TheTrafficNeedsItsOwnPermission()

        /// <summary>
        /// The traffic is the peers' business passing through, and reading it
        /// is a different question from reading the configuration. A viewer
        /// may do the second and not the first.
        /// </summary>
        [Test]
        public async Task TheTrafficNeedsItsOwnPermission()
        {

            using var admin = await SignedIn();

            // The account this hub made for itself is a system administrator,
            // so it may; what says the permission is enforced at all is a
            // request without a session.
            using var stranger = Anonymous();

            var refused = await stranger.GetAsync("/api/v1/traffic");

            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            var allowed = await admin.GetAsync("/api/v1/traffic");

            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        }

        #endregion

        #region ARegistrationThisHubStartedIsInTheTrafficToo()

        /// <summary>
        /// The one call this hub makes that nobody asked it for. Inbound
        /// calls are taken off the HTTP server; this one has to be put in by
        /// hand, and a traffic log that had it missing would leave a reader
        /// wondering where the peer's token came from.
        /// </summary>
        [Test]
        public async Task ARegistrationThisHubStartedIsInTheTrafficToo()
        {

            using var admin = await SignedIn();

            // Pointed at a port nothing is listening on: the registration
            // fails, and a failed call is exactly as much a thing to account
            // for as one that worked.
            var nowhere  = $"http://127.0.0.1:{TestHubs.FreePort()}/versions";

            var response = await admin.PostAsync("/api/v1/ocpi/partners", JSONBody(
                                     new JProperty("version",      "2.2.1"),
                                     new JProperty("countryCode",  "DE"),
                                     new JProperty("partyId",      "GEF"),
                                     new JProperty("role",         "CPO"),
                                     new JProperty("name",         "Test CPO"),
                                     new JProperty("theirToken",   "their-token"),
                                     new JProperty("versionsURL",  nowhere)
                                 ));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

            await admin.PostAsync("/api/v1/ocpi/partners/2.2.1/DE-GEF_CPO/register", JSONBody());

            var outbound = (await Calls(admin)).
                               FirstOrDefault(call => call.Value<String>("direction") == "out");

            Assert.That(outbound, Is.Not.Null, "This hub walked to a peer and the traffic log does not say so.");

            Assert.Multiple(() => {
                Assert.That(outbound!.Value<String>("peer"),    Is.EqualTo("DE-GEF_CPO"));
                // Written the way the headers write a party, which is how
                // both ends of every line here are written - see
                // RecordOutboundCall.
                Assert.That(outbound.Value<String>("from"),     Is.EqualTo("DE-GDH"), "An outbound call does not say which party this hub is.");
                Assert.That(outbound.Value<String>("module"),   Is.EqualTo("credentials"));
                Assert.That(outbound.Value<Boolean>("ok"),      Is.False);
                Assert.That(outbound.Value<String>("error"),    Is.Not.Null.And.Not.Empty);
            });

        }

        #endregion

        #region TheStreamCarriesACallAsItHappens()

        /// <summary>
        /// The Server-Sent Events stream, which is the point of writing any
        /// of this down as it happens rather than afterwards.
        /// </summary>
        [Test]
        public async Task TheStreamCarriesACallAsItHappens()
        {

            using var admin = await SignedIn();

            var token = await AddPeer(admin);

            using var listening = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var request  = new HttpRequestMessage(HttpMethod.Get, "/api/v1/traffic/events");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            var response = await admin.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, listening.Token);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));

            using var stream = new StreamReader(await response.Content.ReadAsStreamAsync(listening.Token));

            // The peer calls once the stream is open, so that what arrives is
            // this call and not a replay of something older.
            using var peer = Peer(token, "DE", "GEF", "DE", "GDF");

            await peer.GetAsync("/ext/versions");

            var sawTheCall = false;

            while (!listening.IsCancellationRequested)
            {

                var line = await stream.ReadLineAsync(listening.Token);

                if (line is null)
                    break;

                if (line.StartsWith("data:") && line.Contains("\"direction\":\"in\""))
                {
                    sawTheCall = line.Contains("DE-GEF") && line.Contains("\"module\":\"versions\"");
                    break;
                }

            }

            Assert.That(sawTheCall, Is.True, "The call never arrived on the traffic stream, or arrived without the parties it was between.");

        }

        #endregion

    }

}
