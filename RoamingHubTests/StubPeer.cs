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

using System.Text;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A peer on the other end of a real socket: the routes of one that this
    /// hub actually calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The three the credentials handshake touches - the versions, the details
    /// of one, and the credentials endpoint - and, where it is asked for, the
    /// HubClientInfo receiver this hub pushes to.
    /// </para>
    /// <para>
    /// Both directions of the peering need it. When this hub is the one that
    /// walks, all three are called; when the peer walks, only the first two
    /// are, because it is this hub that calls back to see whether the URL in
    /// the credentials it was sent answers at all.
    /// </para>
    /// <para>
    /// It records what it was sent rather than asserting anything itself: what
    /// counts as right differs per test, and a stub with opinions is a stub
    /// that has to be argued with.
    /// </para>
    /// </remarks>
    internal sealed class StubPeer : IAsyncDisposable
    {

        #region Data

        /// <summary>The token the peer handed out for this hub to call it with.</summary>
        public const String TokenA = "stub-peer-token-a";

        /// <summary>The token the peer hands out in its credentials.</summary>
        public const String TokenC = "stub-peer-token-c";

        private readonly HTTPServer server;

        #endregion

        #region Properties

        /// <summary>Where this peer says its versions are.</summary>
        public String        VersionsURL          { get; }

        /// <summary>What this hub POSTed to its credentials endpoint, if anything.</summary>
        public JObject?      ReceivedCredentials  { get; private set; }

        /// <summary>Every token this peer was presented with, decoded.</summary>
        public List<String>  TokensSeen           { get; } = [];

        /// <summary>
        /// Every ClientInfo object this hub pushed here, in the order it
        /// arrived.
        /// </summary>
        public List<JObject> ClientInfosPushed    { get; } = [];

        #endregion

        #region Constructor(s)

        private StubPeer(HTTPServer Server, String VersionsURL)
        {
            this.server       = Server;
            this.VersionsURL  = VersionsURL;
        }

        #endregion


        #region (static) Start(Role = "CPO", PartyId = "GEF", Version = "2.2.1", WithHubClientInfo = false)

        /// <summary>
        /// Start one, on a port nobody was listening on a moment ago.
        /// </summary>
        /// <param name="Role">What this peer says it is.</param>
        /// <param name="PartyId">Its party identification.</param>
        /// <param name="Version">The single OCPI version it offers.</param>
        /// <param name="WithHubClientInfo">Whether it offers the HubClientInfo receiver endpoint a hub pushes to.</param>
        public static async Task<StubPeer> Start(String   Role                = "CPO",
                                                 String   PartyId             = "GEF",
                                                 String   Version             = "2.2.1",
                                                 Boolean  WithHubClientInfo   = false)
        {

            var port    = TestRoamingHubs.FreePort();
            var server  = new HTTPServer(IPAddress: IPv4Address.Localhost, TCPPort: IPPort.Parse(port));
            var origin  = $"http://127.0.0.1:{port}";
            var stub    = new StubPeer(server, $"{origin}/versions");
            var api     = server.AddHTTPAPI(HTTPPath.Root);

            #region GET ~/versions

            api.AddHandler(
                HTTPPath.Parse("/versions"),
                request => {
                    stub.Remember(request);
                    return Task.FromResult(JSON(request, new JArray(
                        new JObject(
                            new JProperty("version",  Version),
                            new JProperty("url",      $"{origin}/versions/{Version}")
                        )
                    )));
                },
                HTTPMethod.GET
            );

            #endregion

            #region GET ~/versions/{version}

            var endpoints = new JArray(
                                new JObject(
                                    new JProperty("identifier",  "credentials"),
                                    new JProperty("role",        "RECEIVER"),
                                    new JProperty("url",         $"{origin}/{Version}/credentials")
                                )
                            );

            if (WithHubClientInfo)
                endpoints.Add(
                    new JObject(
                        new JProperty("identifier",  "hubclientinfo"),
                        new JProperty("role",        "RECEIVER"),
                        new JProperty("url",         $"{origin}/{Version}/clientinfo")
                    )
                );

            api.AddHandler(
                HTTPPath.Parse($"/versions/{Version}"),
                request => {
                    stub.Remember(request);
                    return Task.FromResult(JSON(request, new JObject(
                        new JProperty("version",    Version),
                        new JProperty("endpoints",  endpoints)
                    )));
                },
                HTTPMethod.GET
            );

            #endregion

            #region POST ~/{version}/credentials

            api.AddHandler(
                HTTPPath.Parse($"/{Version}/credentials"),
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

            #endregion

            #region PUT ~/{version}/clientinfo/{country_code}/{party_id}

            if (WithHubClientInfo)
                api.AddHandler(
                    HTTPPath.Parse($"/{Version}/clientinfo/{{country_code}}/{{party_id}}"),
                    request => {

                        stub.Remember(request);

                        var pushed = JObject.Parse(request.HTTPBodyAsUTF8String ?? "{}");

                        // The party is in the path as well as in the body, and
                        // a receiver that trusted only one of them would not
                        // notice a hub sending them apart. Kept as it arrived,
                        // with the path beside it, so a test can say so.
                        pushed["_path_country_code"] = request.ParsedURLParameters.ElementAtOrDefault(0);
                        pushed["_path_party_id"]     = request.ParsedURLParameters.ElementAtOrDefault(1);

                        stub.ClientInfosPushed.Add(pushed);

                        return Task.FromResult(JSON(request, pushed));

                    },
                    HTTPMethod.PUT
                );

            #endregion

            await server.Start();

            return stub;

        }

        #endregion

        #region (private) Remember(Request)

        private void Remember(HTTPRequest Request)
        {

            // The token as it was presented: base64 in OCPI 2.2 and later,
            // which is what this undoes so that an assertion can read it.
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

        #endregion

        #region (private static) JSON(Request, Data)

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

        #endregion

        #region DisposeAsync()

        public async ValueTask DisposeAsync()
        {
            await server.Stop();
        }

        #endregion

    }

}
