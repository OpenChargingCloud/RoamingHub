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
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The hub's certificate store over the wire: what it keeps, a root
    /// uploaded for what it is for, changed afterwards and taken back to every
    /// use, switched off, deleted - and what the store refuses, refused where
    /// it is typed.
    /// </summary>
    /// <remarks>
    /// EV's CertificateUsagesTests, for a hub - whose store keeps TLS's four
    /// kinds and none of a vehicle's, which is the one thing the vehicle's
    /// version could not have asked.
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

            var probe  = new TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            var port   = ((IPEndPoint) probe.LocalEndpoint).Port;
            probe.Stop();

            var file   = Path.Combine(directory, WWCPConfigFile.DefaultFileName);
            File.WriteAllText(file, """{ "nts": { "enabled": false } }""");

            hub        = new RoamingHub(
                             HTTPPort:          IPPort.Parse(port),
                             AccountsPath:      Path.Combine(directory, "accounts"),
                             ConfigFile:        new WWCPConfigFile(file),
                             CertificatesPath:  Path.Combine(directory, "certificates"),
                             LogToConsole:      false,
                             BridgeDebugLog:    false
                         );

            await hub.Start();

            client     = new HttpClient {
                             BaseAddress  = new Uri($"http://127.0.0.1:{port}/"),
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


        #region (helpers) RootPem(Name) / IdentityPem(Name) / Send(Method, Path, JSON)

        /// <summary>
        /// A self-signed certificate, as the text of a PEM file base64-encoded -
        /// which is what an upload from the browser turns into.
        /// </summary>
        private static String RootPem(String Name)
        {

            using var key  = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request    = new CertificateRequest($"CN={Name}", key, HashAlgorithmName.SHA256);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));

            using var root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

            return Convert.ToBase64String(Encoding.ASCII.GetBytes(root.ExportCertificatePem()));

        }

        /// <summary>
        /// A certificate this hub could present, with its private key beside it
        /// in the one PEM - as the text of the file, base64-encoded.
        /// </summary>
        private static String IdentityPem(String Name)
        {

            using var key       = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request         = new CertificateRequest($"CN={Name}", key, HashAlgorithmName.SHA256);

            using var identity  = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

            return Convert.ToBase64String(Encoding.ASCII.GetBytes(identity.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem()));

        }

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
        /// sorted into what the hub believes, presents and recognises - and a
        /// vehicle's kind refused with the four named, before anything is read.
        /// </summary>
        [Test]
        public async Task TheStoreKeepsTLSsFourKindsAndNoneOfAVehicles()
        {

            var (_, store)          = await Send(HttpMethod.Get, "api/v1/certificates");

            var (refused, refusal)  = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                     new JProperty("kind",     "v2gRoot"),
                                                     new JProperty("content",  RootPem("A V2G Root"))
                                                 ));

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

                Assert.That(refused,                                  Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(refusal.Value<String>("error"),           Does.Contain("'kind' has to be one of").And.Contain("tlsRoot").And.Not.Contain("v2gRoot"));

            });

        }

        #endregion

        #region ARootIsUploadedForTheUsesItIsFor()

        [Test]
        public async Task ARootIsUploadedForTheUsesItIsFor()
        {

            var (created, entry)  = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                   new JProperty("kind",     "tlsRoot"),
                                                   new JProperty("content",  RootPem("Our Clocks' Root")),
                                                   new JProperty("usages",   new JArray("nts"))
                                               ));

            var (again, sameOne)  = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                   new JProperty("kind",     "tlsRoot"),
                                                   new JProperty("content",  RootPem("Another Root")),
                                                   new JProperty("label",    "Another")
                                               ));

            var (_, store)        = await Send(HttpMethod.Get, "api/v1/certificates");

            Assert.Multiple(() => {
                Assert.That(created,                                                              Is.EqualTo(HttpStatusCode.Created), entry.ToString());
                Assert.That(entry["usages"]!.Values<String>(),                                    Is.EqualTo(new[] { "nts" }));
                Assert.That(again,                                                                Is.EqualTo(HttpStatusCode.Created), sameOne.ToString());
                Assert.That(sameOne["usages"]!.Type,                                              Is.EqualTo(JTokenType.Null), "told nothing, a root is for every use");
                Assert.That(store["certificates"]!["tlsRoot"]!.Children().Count(),                Is.EqualTo(2));
                Assert.That(store["certificates"]!["tlsRoot"]!.Any(root => root["usages"]!.Type == JTokenType.Array &&
                                                                           root["usages"]!.Values<String>().SequenceEqual(new[] { "nts" })),
                            Is.True);
            });

        }

        #endregion

        #region WhatARootIsForIsChangedAndTakenBackToEveryUse()

        [Test]
        public async Task WhatARootIsForIsChangedAndTakenBackToEveryUse()
        {

            var (_, entry)        = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                   new JProperty("kind",     "tlsRoot"),
                                                   new JProperty("content",  RootPem("Our Resolvers' Root")),
                                                   new JProperty("usages",   new JArray("dns"))
                                               ));

            var path              = $"api/v1/certificates/{entry["id"]}";

            var (both,  forBoth)  = await Send(HttpMethod.Patch, path, new JObject(new JProperty("usages", new JArray("nts", "dns"))));
            var (label, relabel)  = await Send(HttpMethod.Patch, path, new JObject(new JProperty("label",  "Our Root")));
            var (every, forAll)   = await Send(HttpMethod.Patch, path, new JObject(new JProperty("usages", JValue.CreateNull())));

            Assert.Multiple(() => {
                Assert.That(both,                                      Is.EqualTo(HttpStatusCode.OK), forBoth.ToString());
                Assert.That(forBoth["usages"]!.Values<String>(),       Is.EqualTo(new[] { "dns", "nts" }));
                Assert.That(label,                                     Is.EqualTo(HttpStatusCode.OK), relabel.ToString());
                Assert.That(relabel.Value<String>("label"),            Is.EqualTo("Our Root"));
                Assert.That(relabel["usages"]!.Values<String>(),       Is.EqualTo(new[] { "dns", "nts" }), "a PATCH without them leaves them alone");
                Assert.That(every,                                     Is.EqualTo(HttpStatusCode.OK), forAll.ToString());
                Assert.That(forAll["usages"]!.Type,                    Is.EqualTo(JTokenType.Null),    "null is every use again");
                Assert.That(hub!.Log.Recent(200, Tag: "security").Any(line => line.Message.Contains("is now for every use")),
                            Is.True,
                            "a change of what a root vouches for is a matter of security, and said as one");
            });

        }

        #endregion

        #region ARootIsSwitchedOffAndThenDeleted()

        /// <summary>
        /// The two ways a certificate leaves service: switched off, which can
        /// be undone and keeps the file, and deleted, which takes the file with
        /// it - and a certificate that is not there is a 404 for both.
        /// </summary>
        [Test]
        public async Task ARootIsSwitchedOffAndThenDeleted()
        {

            var (_, entry)         = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                    new JProperty("kind",     "tlsRoot"),
                                                    new JProperty("content",  RootPem("A Root For A While"))
                                                ));

            var path               = $"api/v1/certificates/{entry["id"]}";
            var file               = hub!.Certificates.FullPath(hub.Certificates.Get(entry.Value<String>("id"))!);

            var (off,  switched)   = await Send(HttpMethod.Patch,  path, new JObject(new JProperty("active", false)));
            var (bad,  notABool)   = await Send(HttpMethod.Patch,  path, new JObject(new JProperty("active", "no")));
            var fileAfterOff       = File.Exists(file);
            var (gone, afterwards) = await Send(HttpMethod.Delete, path);
            var (none, notThere)   = await Send(HttpMethod.Get,    path);

            Assert.Multiple(() => {
                Assert.That(off,                                                    Is.EqualTo(HttpStatusCode.OK), switched.ToString());
                Assert.That(switched.Value<Boolean>("active"),                      Is.False);
                Assert.That(bad,                                                    Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(notABool.Value<String>("error"),                        Does.Contain("'active' has to be true or false."));
                Assert.That(fileAfterOff,                                           Is.True, "switched off keeps the file");
                Assert.That(gone,                                                   Is.EqualTo(HttpStatusCode.OK), afterwards.ToString());
                Assert.That(afterwards["certificates"]!["tlsRoot"]!.Children().Any(),  Is.False);
                Assert.That(File.Exists(file),                                      Is.False, "deleted takes the file with it");
                Assert.That(none,                                                   Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(notThere.Value<String>("error"),                        Is.EqualTo("There is no such certificate in this store."));
            });

        }

        #endregion

        #region WhatIsNotAUsageIsRefusedWhereItIsTyped()

        [Test]
        public async Task WhatIsNotAUsageIsRefusedWhereItIsTyped()
        {

            var (unknown, said)       = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                       new JProperty("kind",     "tlsRoot"),
                                                       new JProperty("content",  RootPem("Some Root")),
                                                       new JProperty("usages",   new JArray("ntp"))
                                                   ));

            var (onClient, clientSaid) = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                       new JProperty("kind",     "clientRoot"),
                                                       new JProperty("content",  RootPem("A Client Root")),
                                                       new JProperty("usages",   new JArray("nts"))
                                                   ));

            var (notAList, listSaid)  = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                       new JProperty("kind",     "tlsRoot"),
                                                       new JProperty("content",  RootPem("Another Root")),
                                                       new JProperty("usages",   "dns")
                                                   ));

            var (identity, idSaid)    = await Send(HttpMethod.Post, "api/v1/certificates", new JObject(
                                                       new JProperty("kind",     "tlsIdentity"),
                                                       new JProperty("content",  IdentityPem("Not For The Name Servers")),
                                                       new JProperty("usages",   new JArray("dns"))
                                                   ));

            var (_, store)            = await Send(HttpMethod.Get, "api/v1/certificates");

            Assert.Multiple(() => {
                Assert.That(unknown,                       Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(said.ToString(),               Does.Contain("'ntp' is not a usage this roaming hub knows").And.Contain("dns, nts"));
                Assert.That(onClient,                      Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(clientSaid.ToString(),         Does.Contain("only a TLS root and a server certificate"));
                Assert.That(notAList,                      Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(listSaid.ToString(),           Does.Contain("has to be a list of usages"));
                Assert.That(identity,                      Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(idSaid.ToString(),             Does.Contain("names none"), "an identity is told listeners, and a hub has none");
                Assert.That(store["certificates"]!.Values().SelectMany(kind => kind.Children()).Any(),
                            Is.False,
                            "nothing refused was half-imported");
            });

        }

        #endregion

        #region AnonymouslyTheStoreIsNotThere()

        /// <summary>
        /// The store is nobody's business anonymously: reading it and uploading
        /// to it are both a 401, the upload before its body is looked at.
        /// </summary>
        [Test]
        public async Task AnonymouslyTheStoreIsNotThere()
        {

            using var anonymous = new HttpClient { BaseAddress = client!.BaseAddress, Timeout = TimeSpan.FromSeconds(30) };

            var read    = await anonymous.GetAsync("api/v1/certificates");
            var upload  = await anonymous.PostAsync("api/v1/certificates",
                                                    new StringContent("""{ "kind": "tlsRoot", "content": "" }""", Encoding.UTF8, "application/json"));

            Assert.Multiple(() => {
                Assert.That(read.StatusCode,    Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(upload.StatusCode,  Is.EqualTo(HttpStatusCode.Unauthorized));
            });

        }

        #endregion

    }

}
