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

        #region (private) StubPeer

        /// <summary>
        /// The three routes of a peer that the credentials handshake touches:
        /// the versions, the details of one, and the credentials endpoint.
        /// </summary>
        /// <remarks>
        /// Both directions need it. When this hub is the one that walks, all
        /// three are called; when the peer walks, only the first two are,
        /// because it is this hub that calls back to see whether the URL in
        /// the credentials it was sent answers.
        /// </remarks>
        private sealed class StubPeer : IAsyncDisposable
        {

            /// <summary>The token the peer handed out for this hub to call it with.</summary>
            public const String TokenA = "stub-peer-token-a";

            /// <summary>The token the peer hands out in its credentials.</summary>
            public const String TokenC = "stub-peer-token-c";

            private readonly HTTPServer server;

            public String    VersionsURL          { get; }

            public JObject?  ReceivedCredentials  { get; private set; }

            public List<String> TokensSeen        { get; } = [];


            private StubPeer(HTTPServer Server, String VersionsURL)
            {
                this.server       = Server;
                this.VersionsURL  = VersionsURL;
            }


            public static async Task<StubPeer> Start(String Role      = "CPO",
                                                     String PartyId   = "GEF")
            {

                var port    = TestRoamingHubs.FreePort();
                var server  = new HTTPServer(IPAddress: IPv4Address.Localhost, TCPPort: IPPort.Parse(port));
                var origin  = $"http://127.0.0.1:{port}";
                var stub    = new StubPeer(server, $"{origin}/versions");
                var api     = server.AddHTTPAPI(HTTPPath.Root);

                api.AddHandler(
                    HTTPPath.Parse("/versions"),
                    request => {
                        stub.Remember(request);
                        return Task.FromResult(JSON(request, new JArray(
                            new JObject(
                                new JProperty("version",  "2.2.1"),
                                new JProperty("url",      $"{origin}/versions/2.2.1")
                            )
                        )));
                    },
                    HTTPMethod.GET
                );

                api.AddHandler(
                    HTTPPath.Parse("/versions/2.2.1"),
                    request => {
                        stub.Remember(request);
                        return Task.FromResult(JSON(request, new JObject(
                            new JProperty("version",    "2.2.1"),
                            new JProperty("endpoints",  new JArray(
                                new JObject(
                                    new JProperty("identifier",  "credentials"),
                                    new JProperty("role",        "RECEIVER"),
                                    new JProperty("url",         $"{origin}/2.2.1/credentials")
                                )
                            ))
                        )));
                    },
                    HTTPMethod.GET
                );

                api.AddHandler(
                    HTTPPath.Parse("/2.2.1/credentials"),
                    request => {
                        stub.Remember(request);
                        stub.ReceivedCredentials = JObject.Parse(request.HTTPBodyAsUTF8String ?? "{}");
                        return Task.FromResult(JSON(request, new JObject(
                            new JProperty("token",  TokenC),
                            new JProperty("url",    stub.VersionsURL),
                            new JProperty("roles",  new JArray(
                                new JObject(
                                    new JProperty("role",              Role),
                                    new JProperty("party_id",          PartyId),
                                    new JProperty("country_code",      "DE"),
                                    new JProperty("business_details",  new JObject(
                                        new JProperty("name",  $"Stub {Role}")
                                    ))
                                )
                            ))
                        )));
                    },
                    HTTPMethod.POST
                );

                await server.Start();

                return stub;

            }


            private void Remember(HTTPRequest Request)
            {

                // The token as it was presented: base64 in OCPI 2.2, which is
                // what the assertion undoes.
                if (Request.Authorization is HTTPTokenAuthentication tokenAuth)
                {
                    try
                    {
                        TokensSeen.Add(Encoding.UTF8.GetString(Convert.FromBase64String(tokenAuth.Token)));
                    }
                    catch (FormatException)
                    {
                        TokensSeen.Add(tokenAuth.Token);
                    }
                }

            }


            private static HTTPResponse JSON(HTTPRequest Request, JToken Data)

                => new HTTPResponse.Builder(Request) {
                       HTTPStatusCode  = HTTPStatusCode.OK,
                       ContentType     = HTTPContentType.Application.JSON_UTF8,
                       Content         = Encoding.UTF8.GetBytes(
                                             new JObject(
                                                 new JProperty("data",            Data),
                                                 new JProperty("status_code",     1000),
                                                 new JProperty("status_message",  "OK"),
                                                 new JProperty("timestamp",       DateTimeOffset.UtcNow.ToString("o"))
                                             ).ToString()
                                         ),
                       Connection      = ConnectionType.Close
                   }.AsImmutable;


            public async ValueTask DisposeAsync()
            {
                await server.Stop();
            }

        }

        #endregion

    }

}
