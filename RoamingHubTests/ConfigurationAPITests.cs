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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// What the configuration pages of the web interface may change, and how
    /// they are told no.
    /// </summary>
    [TestFixture]
    public class ConfigurationAPITests : ARoamingHubTests
    {

        #region ANameServerInTheFormTheLogUsesIsRefusedAndNothingIsWritten()

        /// <summary>
        /// "udp://…:53", as the log and the banner write a name server, typed
        /// into the DNS page: refused with the sentence that says why, and
        /// neither applied nor written down. It was an internal server error,
        /// out of an exception in the address parser.
        /// </summary>
        [Test]
        public async Task ANameServerInTheFormTheLogUsesIsRefusedAndNothingIsWritten()
        {

            using var http = await SignedIn();

            var response = await http.PutAsync(
                                     "/api/v1/configuration/dns",
                                     JSONBody(
                                         new JProperty("servers", new JArray("udp://213.133.98.98:53"))
                                     )
                                 );

            var answered = await response.Content.ReadAsStringAsync();
            var onDisk   = File.Exists(RoamingHub.ConfigFile.Path)
                               ? File.ReadAllText(RoamingHub.ConfigFile.Path)
                               : "";

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,  Is.EqualTo(HttpStatusCode.BadRequest),  answered);
                Assert.That(answered,             Does.Contain("'dns.servers'").And.Contain("udp://213.133.98.98:53"));
                Assert.That(onDisk,               Does.Not.Contain("213.133.98.98"));
                Assert.That(RoamingHub.DNSClient.DNSServers.Select(server => server.ToString()),
                            Has.None.Contains("213.133.98.98"));
            });

        }

        #endregion


        #region TheNTSConfigurationIsReadable()

        [Test]
        public async Task TheNTSConfigurationIsReadable()
        {

            using var http = await SignedIn();

            var nts = await GetJSON(http, "/api/v1/configuration/nts");

            Assert.Multiple(() => {
                // Switched off by the fixture, so that no test reaches the
                // network - the servers it would ask are still named, below,
                // and so is what they are held to.
                Assert.That(nts.Value<Boolean>("enabled"),                Is.False);
                Assert.That(nts["settings"]?.Value<Int32>("minServers"),  Is.EqualTo(2));
                Assert.That(nts.Value<String>("file"),                    Is.EqualTo(RoamingHub.ConfigFile.Path));

                // What the page draws its "Time servers" card from. A RoamingHub
                // nobody has configured asks the PTB's four, so the card has
                // four to draw rather than nothing.
                Assert.That(nts["timeSources"],                           Is.Not.Null.And.Count.EqualTo(4));
                Assert.That(nts["timeSources"]?[0]?.Value<String>("hostname"),
                                                                          Is.EqualTo(RoamingHub.NTSClient.Hostname.ToString()));
                Assert.That(nts["group"]?.Value<String>("name"),          Is.EqualTo("legal"));
                Assert.That(nts["group"]?.Value<Byte>  ("minServers"),    Is.EqualTo(2));
            });

        }

        #endregion

        #region AServerIsTestedFromThePage()

        /// <summary>
        /// POST /api/v1/configuration/nts/test: what each server's Test button
        /// on the NTS page asks, at the diagnostics permission - and the log
        /// says who asked, with the name as it was sent.
        /// </summary>
        /// <remarks>
        /// Time synchronisation is switched off in these hubs, so the answer is
        /// the refusal to ask anybody - which is still an answer from the test,
        /// and nothing goes out.
        /// </remarks>
        [Test]
        public async Task AServerIsTestedFromThePage()
        {

            using var anonymous  = Anonymous();
            var       refused    = await anonymous.PostAsync("/api/v1/configuration/nts/test",
                                                             JSONBody(new JProperty("host", "ptbtime2.ptb.de")));

            using var http       = await SignedIn();

            var before           = RoamingHub.Log.LastId;

            var response         = await http.PostAsync("/api/v1/configuration/nts/test",
                                                        JSONBody(new JProperty("host", "ptbtime2.ptb.de")));

            var answered         = await response.Content.ReadAsStringAsync();
            var said             = RoamingHub.Log.Recent(50, before, "nts").Select(entry => entry.Message).ToArray();

            Assert.Multiple(() => {

                Assert.That(refused.StatusCode,   Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(response.StatusCode,  Is.EqualTo(HttpStatusCode.OK),  answered);

                var test = JObject.Parse(answered);

                Assert.That(test.Value<Boolean>("ok"),                      Is.False);
                Assert.That(test["steps"]?[0]?.Value<String>("text"),       Does.Contain("switched off"));

                Assert.That(said,  Has.Some.EqualTo("'root' asked this RoamingHub to test the time server 'ptbtime2.ptb.de'."),
                            String.Join(" | ", said));

            });

        }

        #endregion

        #region PointingTheHubAtAnotherTimeServerReplacesTheClient()

        /// <summary>
        /// An NTS client is bound to its host at construction, and the cookies
        /// and keys it holds belong to that host and to no other - so being
        /// pointed elsewhere builds a new one rather than reconfiguring this
        /// one.
        /// </summary>
        [Test]
        public async Task PointingTheHubAtAnotherTimeServerReplacesTheClient()
        {

            using var http = await SignedIn();

            var before = RoamingHub.NTSClient;

            var response = await http.PutAsync(
                                     "/api/v1/configuration/nts",
                                     JSONBody(
                                         new JProperty("hostname",   "ptbtime2.ptb.de"),
                                         new JProperty("ntsKEPort",  4460),
                                         new JProperty("ntpPort",    123)
                                     )
                                 );

            var onDisk = JObject.Parse(File.ReadAllText(RoamingHub.ConfigFile.Path));

            Assert.Multiple(() => {

                Assert.That(response.IsSuccessStatusCode,              Is.True);
                Assert.That(RoamingHub.NTSClient.Hostname.ToString(),  Does.StartWith("ptbtime2.ptb.de"));

                Assert.That(RoamingHub.NTSClient,                      Is.Not.SameAs(before),
                            "The host changed and the client did not, so it still holds the cookies of the old one.");

                // Written as the domain name it was parsed into, which is the
                // absolute form with the root label on the end - so this is
                // what a name looks like on its way back out of the file, and
                // not a stray character.
                Assert.That(onDisk["nts"]?.Value<String>("hostname"),
                            Is.EqualTo(RoamingHub.NTSClient.Hostname.ToString()));

            });

        }

        #endregion

    }

}
