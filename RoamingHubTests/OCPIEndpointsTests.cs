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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using NUnit.Framework;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The endpoints the OCPI page lists for every version, held against what
    /// a peer finds there.
    /// </summary>
    /// <remarks>
    /// The page cannot ask the OCPI library where they are: the library builds
    /// its URLs per request, from the Host header, and a page has no request
    /// to hand it. So the hub works them out for itself, and nothing but a
    /// test notices when the two part ways - which they had, twice over: the
    /// modules were listed below a "hub/" segment the library does not serve
    /// them under, and 2.2.1 listed a module the library does not have for it
    /// at all.
    ///
    /// Both versions offered, because each went wrong in its own way, and one
    /// peer added on each, because a peer calls the endpoints of the version
    /// it was added on.
    /// </remarks>
    public class OCPIEndpointsTests : ARoamingHubTests
    {

        #region Configuration - a hub that offers both versions

        /// <summary>
        /// The default is 2.2.1 alone, and the one module this hub has is on
        /// 2.3.0.
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


        #region EveryEndpointOnThePageAnswersAPeer()

        /// <summary>
        /// Every URL the page lists - the version details, the credentials and
        /// every module - answers a peer holding the token it was given.
        /// </summary>
        /// <remarks>
        /// Answers the way OCPI answers: HTTP 200, and an envelope with the
        /// status code 1000. A 200 with anything else in it is not an endpoint
        /// a peer can use. What the page listed before did not get that far:
        /// it answered 404, from the HTTP server, with a body that says nothing
        /// about OCPI at all.
        ///
        /// And the one module this hub serves has to be among them, or a page
        /// that listed nothing would pass.
        /// </remarks>
        [Test]
        public async Task EveryEndpointOnThePageAnswersAPeer()
        {

            using var admin   = await SignedIn();

            var tokens        = await OnePeerPerVersion(admin);

            var listed        = new List<String>();
            var unanswered    = new List<String>();

            foreach (var version in await ThePage(admin))
            {

                var label       = version.Value<String>("version")!;

                using var peer  = Peer(tokens[label]);

                foreach (var (endpoint, url) in Listed(version))
                {

                    listed.Add($"{label} {endpoint}");

                    if (await Unanswered(peer, url) is { } why)
                        unanswered.Add($"OCPI {label} {endpoint} at {url} {why}");

                }

            }

            Assert.Multiple(() => {

                Assert.That(unanswered, Is.Empty,
                            "The page lists endpoints a peer is not answered at.");

                Assert.That(listed,     Does.Contain("2.3.0 hubclientinfo"),
                            "The page leaves out the one module this hub serves.");

            });

        }

        #endregion

        #region ThePageListsWhatAPeerIsTold()

        /// <summary>
        /// The page says it shows what a peer is told, so it has to be that:
        /// the version details where the versions list says they are, and the
        /// endpoints the version details name - no more, no fewer, and at the
        /// same URLs.
        /// </summary>
        /// <remarks>
        /// The other direction from the test above. That one catches a row a
        /// peer cannot use; this one catches an endpoint the library starts to
        /// offer and the page never mentions - the modules of a version are a
        /// list kept by hand, and the library is not asked.
        /// </remarks>
        [Test]
        public async Task ThePageListsWhatAPeerIsTold()
        {

            using var admin   = await SignedIn();

            var tokens        = await OnePeerPerVersion(admin);

            var differences   = new List<String>();

            foreach (var version in await ThePage(admin))
            {

                var label        = version.Value<String>("version")!;
                var details      = version.Value<String>("details")!;

                using var peer   = Peer(tokens[label]);

                // Where the versions list sends a peer for this version ...
                var toldDetails  = ((await OCPIData(peer, "/ext/versions")) as JArray)?.
                                       FirstOrDefault(candidate => candidate.Value<String>("version") == label)?.
                                       Value<String>("url");

                if (toldDetails != details)
                    differences.Add($"OCPI {label}: the page has the version details at {details}, the versions list at {toldDetails ?? "(nowhere)"}");

                // ... and what the version details name there.
                var told         = (((await OCPIData(peer, details))?["endpoints"] as JArray) ?? []).
                                       Select(endpoint => $"{endpoint.Value<String>("identifier")} {endpoint.Value<String>("url")}").
                                       Order().
                                       ToArray();

                var shown        = Listed(version).
                                       Where (row => row.Endpoint != "version details").
                                       Select(row => $"{row.Endpoint} {row.URL}").
                                       Order().
                                       ToArray();

                if (!shown.SequenceEqual(told))
                    differences.Add($"OCPI {label}: the page lists [{String.Join(", ", shown)}], the version details name [{String.Join(", ", told)}]");

            }

            Assert.That(differences, Is.Empty,
                        "The page shows a way into this hub other than the one a peer is told.");

        }

        #endregion


        #region (private) OnePeerPerVersion(Admin)

        /// <summary>
        /// A peer added on each version - a CPO on 2.2.1, an EMSP on 2.3.0 -
        /// and the token each was given, by version.
        /// </summary>
        private async Task<Dictionary<String, String>> OnePeerPerVersion(HttpClient Admin)

            => new Dictionary<String, String> {
                   ["2.2.1"]  = await AddPeer(Admin, "2.2.1", "CPO",  "GEF"),
                   ["2.3.0"]  = await AddPeer(Admin, "2.3.0", "EMSP", "GDF")
               };

        #endregion

        #region (private) AddPeer(Admin, Version, Role, PartyId)

        /// <summary>
        /// Add a peer through the JSON API, on the given version, and hand back
        /// the token this hub made up for it.
        /// </summary>
        private async Task<String> AddPeer(HttpClient  Admin,
                                           String      Version,
                                           String      Role,
                                           String      PartyId)
        {

            var response = await Admin.PostAsync("/api/v1/ocpi/partners", JSONBody(
                                     new JProperty("version",      Version),
                                     new JProperty("countryCode",  "DE"),
                                     new JProperty("partyId",      PartyId),
                                     new JProperty("role",         Role),
                                     new JProperty("name",         $"Test {Role}")
                                 ));

            var text     = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Adding the peer answered {(Int32) response.StatusCode}: {text}");

            return JObject.Parse(text).Value<String>("ourToken")!;

        }

        #endregion

        #region (private static) ThePage(Admin) / Listed(Version)

        /// <summary>
        /// The endpoints, per version, the way the OCPI page reads them.
        /// </summary>
        private static async Task<JArray> ThePage(HttpClient Admin)
        {

            var byVersion = (await GetJSON(Admin, "/api/v1/configuration/ocpi"))["endpoints"]?["byVersion"] as JArray;

            Assert.That(byVersion, Is.Not.Null, "The OCPI configuration lists no endpoints per version.");

            return byVersion!;

        }

        /// <summary>
        /// The rows of one version on the page: the version details, the
        /// credentials, and every module.
        /// </summary>
        private static IEnumerable<(String Endpoint, String URL)> Listed(JToken Version)
        {

            yield return ("version details",  Version.Value<String>("details")!);
            yield return ("credentials",      Version.Value<String>("credentials")!);

            foreach (var module in (Version["modules"] as JObject)?.Properties() ?? [])
                yield return (module.Name, module.Value.ToString());

        }

        #endregion

        #region (private) Peer(Token)

        /// <summary>
        /// An HTTP client that talks as a peer holding the given token.
        /// </summary>
        /// <remarks>
        /// Base64, because that is how OCPI 2.2 and later write a token in the
        /// Authorization header.
        /// </remarks>
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

        #region (private static) Unanswered(Peer, URL) / OCPIData(Peer, URL)

        /// <summary>
        /// Why a URL did not answer the peer the way OCPI answers, or null
        /// when it did.
        /// </summary>
        private static async Task<String?> Unanswered(HttpClient  Peer,
                                                      String      URL)
        {

            var response  = await Peer.GetAsync(URL);
            var text      = await response.Content.ReadAsStringAsync();

            if (response.StatusCode != HttpStatusCode.OK)
                return $"answered {(Int32) response.StatusCode}: {text}";

            try
            {

                var statusCode = JObject.Parse(text).Value<Int32?>("status_code");

                return statusCode == 1000
                           ? null
                           : $"answered with the OCPI status {statusCode?.ToString() ?? "(none)"}: {text}";

            }
            catch (JsonReaderException)
            {
                return $"answered with something that is not OCPI: {text}";
            }

        }

        /// <summary>
        /// The data of an OCPI answer, or null when the request did not
        /// succeed.
        /// </summary>
        private static async Task<JToken?> OCPIData(HttpClient  Peer,
                                                    String      URL)
        {

            var response = await Peer.GetAsync(URL);

            return response.IsSuccessStatusCode
                       ? JObject.Parse(await response.Content.ReadAsStringAsync())["data"]
                       : null;

        }

        #endregion

    }

}
