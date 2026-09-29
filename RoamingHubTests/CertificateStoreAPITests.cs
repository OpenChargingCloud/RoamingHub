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

using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The hub's certificate store over the wire: what it keeps, and what each
    /// of it may be told it is for.
    /// </summary>
    /// <remarks>
    /// What every node's store does - a root uploaded for what it is for,
    /// changed and taken back to every use, identities, what is refused where
    /// it is typed, and nothing of it for anybody not signed in - is asked by
    /// WWCP_Node's conformance suite, see RoamingHubConformance, which holds a
    /// store to its own word. What that word is on a hub is asked here: TLS's
    /// four kinds, and none of a vehicle's.
    /// </remarks>
    public class CertificateStoreAPITests
    {

        #region Data

        private String       directory  = "";
        private RoamingHub?  hub;
        private HttpClient?  client;

        #endregion

        #region Setup / TearDown

        [SetUp]
        public async Task Setup()
        {

            directory = Path.Combine(Path.GetTempPath(), "hub-certificates-" + Guid.NewGuid().ToString("N")[..12]);

            Directory.CreateDirectory(directory);

            var file   = Path.Combine(directory, WWCPConfigFile.DefaultFileName);
            File.WriteAllText(file, """{ "nts": { "enabled": false } }""");

            hub        = await TestPorts.StartedOnFreshPorts(() => new RoamingHub(
                             HTTPPort:          IPPort.Parse(TestPorts.Free()),
                             AccountsPath:      Path.Combine(directory, "accounts"),
                             ConfigFile:        new WWCPConfigFile(file),
                             CertificatesPath:  Path.Combine(directory, "certificates"),
                             LogToConsole:      false,
                             BridgeDebugLog:    false
                         ));

            // The port it was started on - not the one it was first handed,
            // where another test run on this machine took that one.
            client     = new HttpClient {
                             BaseAddress  = new Uri($"http://127.0.0.1:{hub.HTTPPort}/"),
                             Timeout      = TimeSpan.FromSeconds(30)
                         };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                             "Basic",
                                                             Convert.ToBase64String(Encoding.UTF8.GetBytes($"root:{hub.GeneratedPassword}"))
                                                         );

        }

        [TearDown]
        public async Task TearDown()
        {

            client?.Dispose();

            if (hub is not null)
                await hub.DisposeAsync();

            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // A temporary directory that outlives one test run is not worth
                // failing the run over.
            }

        }

        #endregion


        #region (helper) Send(Method, Path, JSON)

        private async Task<(HttpStatusCode Status, JObject JSON)> Send(HttpMethod  Method,
                                                                      String      Path,
                                                                      JObject?    JSON = null)
        {

            using var request   = new HttpRequestMessage(Method, Path);

            if (JSON is not null)
                request.Content = new StringContent(JSON.ToString(), Encoding.UTF8, "application/json");

            using var response  = await client!.SendAsync(request);
            var text            = await response.Content.ReadAsStringAsync();

            return (response.StatusCode, text.Length > 0 ? JObject.Parse(text) : new JObject());

        }

        #endregion


        #region TheStoreKeepsTLSsFourKindsAndNoneOfAVehicles()

        /// <summary>
        /// What the page is told the store keeps: the four kinds TLS is made of,
        /// sorted into what the hub believes, presents and recognises, and what
        /// each of them may be told it is for.
        /// </summary>
        [Test]
        public async Task TheStoreKeepsTLSsFourKindsAndNoneOfAVehicles()
        {

            var (_, store) = await Send(HttpMethod.Get, "api/v1/certificates");

            Assert.Multiple(() => {

                Assert.That(((JObject) store["kinds"]!).Properties().Select(kind => kind.Name),
                            Is.EquivalentTo(new[] { "tlsRoot", "clientRoot", "tlsServer", "tlsIdentity" }));

                Assert.That(store["trustAnchors"]!.Values<String>(),  Is.EquivalentTo(new[] { "tlsRoot", "clientRoot" }));
                Assert.That(store["credentials"]!.Values<String>(),   Is.EqualTo(new[] { "tlsIdentity" }));
                Assert.That(store["recognised"]!.Values<String>(),    Is.EqualTo(new[] { "tlsServer" }),
                            "a server certificate is recognised, neither believed nor presented");
                Assert.That(store["usages"]!.Values<String>(),        Is.EqualTo(new[] { "dns", "nts" }), "what a page may offer");
                Assert.That(store["chosen"],                          Is.Null, "a hub has no session to choose certificates for");

                // What a certificate of each kind may be told it is for, as the
                // store says it for this hub rather than as the kind says it.
                Assert.That(store["kinds"]!["tlsRoot"]!["usages"]?.Values<String>(),         Is.EqualTo(new[] { "dns", "nts" }), "what a page may offer a root");
                Assert.That(store["kinds"]!["tlsServer"]!["usages"]?.Values<String>(),       Is.EqualTo(new[] { "dns", "nts" }));
                Assert.That(store["kinds"]!["tlsIdentity"]!["hasUsages"]!.Value<Boolean>(),  Is.False,
                            "a hub names no listener an identity could be told of, so a page offers it nothing - not the services a root vouches for");
                Assert.That(store["kinds"]!["tlsIdentity"]!["usages"]?.Children().Any(),     Is.False);
                Assert.That(store["kinds"]!["clientRoot"]!["hasUsages"]!.Value<Boolean>(),   Is.False);

            });

        }

        #endregion

    }

}
