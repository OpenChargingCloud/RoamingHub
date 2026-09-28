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
    /// What the configuration pages of the web interface ask of a hub, beyond
    /// changing its name servers and its time servers and being told no -
    /// which WWCP_Node's conformance suite asks of every node, see
    /// RoamingHubConformance.
    /// </summary>
    [TestFixture]
    public class ConfigurationAPITests : ARoamingHubTests
    {

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

                Assert.That(said,  Has.Some.EqualTo("'root' asked this roaming hub to test the time server 'ptbtime2.ptb.de'."),
                            String.Join(" | ", said));

            });

        }

        #endregion

    }

}
