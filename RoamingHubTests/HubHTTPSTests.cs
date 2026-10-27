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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.TestKit;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Certificates;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The hub served safely, as the node's "web" section and its switches say
    /// it (POI3-ISSUES.md S13): over HTTPS of its own, with the identity the
    /// command line put into its store - its web interface, its JSON API, its
    /// sign-in and its POI API alike - and behind a trusted proxy, where the
    /// sign-in's ration of guesses is counted per client the proxy names.
    /// </summary>
    public class HubHTTPSTests
    {

        #region Data

        private          String            directory  = "";
        private readonly List<RoamingHub>  hubs       = [];

        #endregion

        #region Setup / TearDown

        [SetUp]
        public void Setup()
        {
            directory = TestRoamingHubs.TemporaryDirectory("https");
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public async Task TearDown()
        {

            foreach (var hub in hubs)
            {
                try
                {
                    await hub.DisposeAsync();
                }
                catch (Exception)
                { }
            }

            hubs.Clear();

            TestRoamingHubs.Remove(directory);

        }

        #endregion


        #region (helpers) AnIdentityFile() / Started(Arguments, Configuration) / SignIn(...)

        /// <summary>
        /// A TLS server identity for 127.0.0.1 in a PKCS#12 file without a
        /// password, as --import-certificate takes it, and its thumbprint.
        /// </summary>
        private (String File, String Thumbprint) AnIdentityFile()
        {

            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var request   = new CertificateRequest("CN=roaming hub web interface", key, HashAlgorithmName.SHA256);
            var names     = new SubjectAlternativeNameBuilder();

            names.AddIpAddress(System.Net.IPAddress.Loopback);
            names.AddDnsName("localhost");

            request.CertificateExtensions.Add(names.Build());
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ new Oid("1.3.6.1.5.5.7.3.1") ], false));

            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

            var file = Path.Combine(directory, "hub-web.p12");

            File.WriteAllBytes(file, certificate.Export(X509ContentType.Pkcs12));

            return (file, CertificateEntry.ThumbprintOf(certificate));

        }

        /// <summary>
        /// A hub made as RoamingHubCLI makes it - from the command line given,
        /// what goes into its store put in - and started on fresh ports.
        /// </summary>
        private async Task<RoamingHub> Started(NodeArguments  Arguments,
                                               JObject?       Configuration  = null)
        {

            var hub = await TestPorts.StartedOnFreshPorts(() => {

                var home  = Path.Combine(directory, "hub" + hubs.Count);
                var made  = new RoamingHub(HTTPPort:        IPPort.Parse(TestPorts.Free()),
                                           AccountsPath:    Path.Combine(home, "accounts"),
                                           ConfigFile:      TestRoamingHubs.ConfigFile(home, Configuration ?? TestRoamingHubs.Offline),
                                           LogToConsole:    false,
                                           BridgeDebugLog:  false,
                                           Web:             Arguments.Web);

                Assert.That(made.ImportCertificates(Arguments, out _, new StringWriter(), new StringWriter()), Is.Null);

                return made;

            });

            hubs.Add(hub);

            return hub;

        }

        /// <summary>
        /// A sign-in at the HTTPExt API's form, with the given header fields
        /// written as given and without a cookie jar.
        /// </summary>
        private static async Task<HttpResponseMessage> SignIn(RoamingHub                 Hub,
                                                              String                     Login,
                                                              String                     Password,
                                                              params (String, String)[]  Headers)
        {

            using var handler  = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client   = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            using var request  = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Hub.HTTPPort}/ext/login") {
                                     Content = new FormUrlEncodedContent([
                                                   new KeyValuePair<String, String>("login",     Login),
                                                   new KeyValuePair<String, String>("password",  Password)
                                               ])
                                 };

            foreach (var (name, value) in Headers)
                Assert.That(request.Headers.TryAddWithoutValidation(name, value), Is.True, $"{name}: {value}");

            var response = await client.SendAsync(request);

            await response.Content.LoadIntoBufferAsync();

            return response;

        }

        #endregion


        #region TheHubServesItsWebInterfaceAPIAndPOIOverHTTPS()

        /// <summary>
        /// --https with --import-certificate tlsServerIdentity=&lt;file&gt;, as
        /// RoamingHubCLI passes them on: the hub keeps the identity, shows it,
        /// and serves its web interface with HSTS, its sign-in with a secure
        /// cookie, its JSON API and its POI API - all over HTTPS - and says
        /// nothing about plain HTTP.
        /// </summary>
        [Test]
        public async Task TheHubServesItsWebInterfaceAPIAndPOIOverHTTPS()
        {

            var (file, thumbprint)  = AnIdentityFile();
            var arguments           = NodeArguments.Parse([ "--https", "--import-certificate", $"tlsServerIdentity={file}" ]);

            Assert.That(arguments.Problem, Is.Null);

            var hub                 = await Started(arguments);

            String? shown           = null;

            using var http          = new HttpClient(new HttpClientHandler {
                                                         CookieContainer                            = new CookieContainer(),
                                                         UseCookies                                 = true,
                                                         ServerCertificateCustomValidationCallback  = (message, served, chain, errors) => {
                                                                                                          shown = served is null ? null : CertificateEntry.ThumbprintOf(served);
                                                                                                          return true;
                                                                                                      }
                                                     }) {
                                          BaseAddress  = new Uri(hub.WebInterfaceURL.ToString()),
                                          Timeout      = TimeSpan.FromSeconds(30)
                                      };

            using var page          = await http.GetAsync("");
            using var signIn        = await http.PostAsync("ext/login", new FormUrlEncodedContent([
                                                                            new KeyValuePair<String, String>("login",     RoamingHub.DefaultAdminUser),
                                                                            new KeyValuePair<String, String>("password",  hub.GeneratedPassword!)
                                                                        ]));
            using var status        = await http.GetAsync("api/v1/status");
            using var networks      = await http.GetAsync("poi/RNs");

            var session             = signIn.Headers.TryGetValues("Set-Cookie", out var cookies)
                                          ? cookies.FirstOrDefault(cookie => cookie.StartsWith($"{hub.ExtAPI.SessionCookieName}=", StringComparison.Ordinal))
                                          : null;

            Assert.Multiple(() => {

                Assert.That(RoamingHub.CertificateKinds,                             Does.Contain(CertificateKind.TLSServerIdentity));
                Assert.That(hub.Certificates.ByKind(CertificateKind.TLSServerIdentity).Select(entry => entry.Thumbprint),
                                                                                     Is.EqualTo(new[] { thumbprint }));

                Assert.That(hub.HTTPS,                                               Is.True);
                Assert.That(hub.WebInterfaceURL.ToString(),                          Does.StartWith("https://"));
                Assert.That(shown,                                                   Is.EqualTo(thumbprint));
                Assert.That(hub.PlainHTTPWarning,                                    Is.Null);

                Assert.That(page.StatusCode,                                         Is.EqualTo(HttpStatusCode.OK));
                Assert.That(page.Headers.TryGetValues("Strict-Transport-Security", out var hsts) ? hsts.Single() : null,
                                                                                     Is.EqualTo("max-age=31536000"));

                Assert.That(signIn.IsSuccessStatusCode,                              Is.True, $"{(Int32) signIn.StatusCode}");
                Assert.That(session,                                                 Does.Contain("secure").IgnoreCase);

                Assert.That(status.StatusCode,                                       Is.EqualTo(HttpStatusCode.OK), "the JSON API, with the session over HTTPS");
                Assert.That(networks.StatusCode,                                     Is.EqualTo(HttpStatusCode.OK), "the POI API");

            });

            var configuration = JObject.Parse(await http.GetStringAsync("api/v1/configuration"));

            Assert.That(configuration["http"]?["tls"]?.Value<String>("thumbprint"),  Is.EqualTo(thumbprint));

        }

        #endregion

        #region BehindATrustedProxyTheSignInsRationIsCountedPerClient()

        /// <summary>
        /// Behind nginx, named in "trustedProxies": ten guesses from one client
        /// make that client wait, and another behind the same proxy signs in at
        /// once - with a secure cookie, as the proxy says its leg was HTTPS. And
        /// the hub, listening on the loopback only behind it, says nothing about
        /// plain HTTP.
        /// </summary>
        [Test]
        public async Task BehindATrustedProxyTheSignInsRationIsCountedPerClient()
        {

            var configuration = TestRoamingHubs.Offline;

            configuration.Add("web", JObject.Parse("""{ "trustedProxies": [ "127.0.0.1", "::1" ] }"""));

            var hub = await Started(NodeArguments.Parse([]), configuration);

            for (var guess = 1; guess <= 10; guess++)
                using (await SignIn(hub, "nobody", $"Not-The-Password-{guess}", ("X-Forwarded-For", "192.0.2.7"), ("X-Forwarded-Proto", "https"))) { }

            using var guesser = await SignIn(hub, RoamingHub.DefaultAdminUser, hub.GeneratedPassword!, ("X-Forwarded-For", "192.0.2.7"),    ("X-Forwarded-Proto", "https"));
            using var other   = await SignIn(hub, RoamingHub.DefaultAdminUser, hub.GeneratedPassword!, ("X-Forwarded-For", "198.51.100.4"), ("X-Forwarded-Proto", "https"));

            var session       = other.Headers.TryGetValues("Set-Cookie", out var cookies)
                                    ? cookies.FirstOrDefault(cookie => cookie.StartsWith($"{hub.ExtAPI.SessionCookieName}=", StringComparison.Ordinal))
                                    : null;

            Assert.Multiple(() => {
                Assert.That(guesser.StatusCode,        Is.EqualTo(HttpStatusCode.TooManyRequests), "the client who guessed waits");
                Assert.That((Int32) other.StatusCode,  Is.InRange(200, 399),                       "another client behind the same proxy signs in");
                Assert.That(session,                   Does.Contain("secure").IgnoreCase,           "its leg was HTTPS, as the proxy says");
                Assert.That(hub.PlainHTTPWarning,      Is.Null);
                Assert.That(hub.HTTPS,                 Is.False);
            });

        }

        #endregion

    }

}
