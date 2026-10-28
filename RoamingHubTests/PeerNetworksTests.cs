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

using NUnit.Framework;

using cloud.charging.open.RoamingHub.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// Where this hub goes when a peer tells it where to go: to the public internet, and to the networks its
    /// configuration allows, and nowhere else - not to this host, where the stub peers of these tests listen, nor to the
    /// network it runs in.
    /// </summary>
    /// <remarks>
    /// A peer holding a token of this hub hands in the URL its versions are fetched from, and the URLs those lead to.
    /// Without the rules, it had this hub call any host it can reach - 127.0.0.1, 10.0.0.0/8, the metadata service of a
    /// cloud at 169.254.169.254 - with the peer's token in the header, and told the peer by the answer whether
    /// something listened there.
    /// </remarks>
    public class PeerNetworksTests : APeeringTests
    {

        #region APeerCannotHaveThisHubCallAHostOfItsNetwork()

        /// <summary>
        /// A peer registering with credentials whose URL is on this host: this hub does not go there, and answers as it
        /// answers a URL nobody answers at - no word of why.
        /// </summary>
        [Test]
        public async Task APeerCannotHaveThisHubCallAHostOfItsNetwork()
        {

            using var admin = await SignedIn();

            await using var cpo = await StubPeer.Start();

            var (id, ourToken, _) = await AddPeer(admin);

            using var peer = Peer(ourToken);

            var versions        = (await OCPIResponse(await peer.GetAsync("/ext/versions")))["data"] as JArray;
            var details         = versions!.First(version => version.Value<String>("version") == "2.2.1").Value<String>("url");
            var endpoints       = (await OCPIResponse(await peer.GetAsync(details)))["data"]?["endpoints"] as JArray;
            var credentialsURL  = endpoints!.First(endpoint => endpoint.Value<String>("identifier") == "credentials").Value<String>("url")!;

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

            var text   = await posted.Content.ReadAsStringAsync();
            var after  = ((await GetJSON(admin, "/api/v1/ocpi/partners"))["partners"] as JArray)!.
                             First(candidate => candidate.Value<String>("id") == id);

            Assert.Multiple(() => {
                Assert.That(cpo.TokensSeen,                       Is.Empty,  "This hub called a host on itself because a peer said so.");
                Assert.That(JObject.Parse(text).Value<Int32>("status_code"), Is.Not.EqualTo(1000), text);
                Assert.That(text,                                 Does.Contain("Could not fetch VERSIONS information"), "Not the answer to a URL nobody answers at.");
                Assert.That(text,                                 Does.Not.Contain("allow").IgnoreCase, "The answer tells the peer why.");
                Assert.That(after.Value<Boolean>("registered"),   Is.False);
            });

        }

        #endregion

        #region ThisHubDoesNotGoToAPeerOnThisHostOfItsOwnAccord()

        /// <summary>
        /// Registering with a peer whose versions URL is on this host fails without a word to it - unless the
        /// configuration allows the network (see PeeringTests, whose hub allows this host).
        /// </summary>
        [Test]
        public async Task ThisHubDoesNotGoToAPeerOnThisHostOfItsOwnAccord()
        {

            using var admin = await SignedIn();

            await using var cpo = await StubPeer.Start();

            var (id, _, _) = await AddPeer(admin,
                                           TheirToken:   StubPeer.TokenA,
                                           VersionsURL:  cpo.VersionsURL);

            var register = await admin.PostAsync($"/api/v1/ocpi/partners/2.2.1/{id}/register", JSONBody());
            var text     = await register.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That((Int32) register.StatusCode,  Is.EqualTo(502), text);
                Assert.That(cpo.TokensSeen,               Is.Empty, "This hub went to a peer on this host.");
                Assert.That(cpo.ReceivedCredentials,      Is.Null);
            });

        }

        #endregion

        #region TheRulesAreReadAndComeBeforeThePublicInternet()

        /// <summary>
        /// "peerNetworks" in the "ocpi" section: rules in their order, then every network no public server is reached
        /// at denied, then the public internet; a rule that is none stops the start with a sentence saying which.
        /// </summary>
        [Test]
        public void TheRulesAreReadAndComeBeforeThePublicInternet()
        {

            static System.Net.IPAddress IP(String Text) => System.Net.IPAddress.Parse(Text);

            Assert.That(OCPIConfiguration.TryParse(JObject.Parse("""{ "peerNetworks": [ "192.168.7.0/24 => ALLOW", "203.0.113.0/24 => DENY" ] }"""),
                                                   out var configuration,
                                                   out var error),
                        Is.True, error);

            var rules = configuration!.EffectivePeerNetworks;

            Assert.Multiple(() => {

                Assert.That(rules.Allows(IP("192.168.7.10"),    443), Is.True,  "allowed by the file");
                Assert.That(rules.Allows(IP("192.168.8.10"),    443), Is.False, "another private network");
                Assert.That(rules.Allows(IP("127.0.0.1"),       443), Is.False);
                Assert.That(rules.Allows(IP("169.254.169.254"), 80),  Is.False, "the metadata service of a cloud");
                Assert.That(rules.Allows(IP("8.8.8.8"),         443), Is.True,  "the public internet");
                Assert.That(new OCPIConfiguration().EffectivePeerNetworks.Allows(IP("10.1.2.3"), 443), Is.False, "without rules as well");

                Assert.That(configuration.ToJSON()["peerNetworks"]?.Values<String>(),
                            Is.EqualTo(new[] { "192.168.7.0/24 => ALLOW", "203.0.113.0/24 => DENY" }));

                Assert.That(OCPIConfiguration.TryParse(JObject.Parse("""{ "peerNetworks": [ "10.0.0.0/8 => PERHAPS" ] }"""), out _, out var notARule), Is.False);
                Assert.That(notARule, Does.StartWith("'ocpi.peerNetworks':").And.Contain("PERHAPS"));

            });

        }

        #endregion

    }

}
