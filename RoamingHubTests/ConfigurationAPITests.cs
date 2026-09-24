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

    }

}
