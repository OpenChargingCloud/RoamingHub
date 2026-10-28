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

using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.RoamingHub.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// What the peering fixtures share: a peer calling this hub with its token, a peer added through the JSON API, and
    /// the envelope of an OCPI answer.
    /// </summary>
    public abstract class APeeringTests : ARoamingHubTests
    {

        #region (protected) Peer(Token)

        /// <summary>
        /// A peer calling this hub with the token it was given, encoded the
        /// way OCPI 2.2 sends it.
        /// </summary>
        protected HttpClient Peer(String Token)
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

        #region (protected) AddPeer(HTTP, ...)

        /// <summary>
        /// Add a peer through the JSON API and hand back the token this hub
        /// made up for it.
        /// </summary>
        protected async Task<(String Id, String Token, JObject Answer)> AddPeer(HttpClient  HTTP,
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

        #region (protected static) OCPIResponse(Response)

        protected static async Task<JObject> OCPIResponse(HttpResponseMessage Response)
        {

            var text = await Response.Content.ReadAsStringAsync();

            Assert.That(Response.IsSuccessStatusCode, Is.True, $"{Response.RequestMessage?.Method} {Response.RequestMessage?.RequestUri} answered {(Int32) Response.StatusCode}: {text}");

            return JObject.Parse(text);

        }

        #endregion

    }

}
