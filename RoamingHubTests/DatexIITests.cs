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
using System.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.TestKit;
using cloud.charging.open.RoamingHub.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    #region DatexIIConfigurationTests

    /// <summary>
    /// The "datex2" section of the configuration file: what it says, and the one sentence naming the field when it
    /// says something wrong.
    /// </summary>
    public class DatexIIConfigurationTests
    {

        #region (helper) Section(Publication)

        private static JObject Section(JObject Publication,
                                       Boolean? Pull = null)
        {

            var section = new JObject(new JProperty("publications", new JArray(Publication)));

            if (Pull.HasValue)
                section.Add("pull", Pull.Value);

            return new (new JProperty(DatexIIConfiguration.SectionName, section));

        }

        private static JObject Publication(params JProperty[] More)
        {

            var publication = new JObject(
                                  new JProperty("roamingNetworkId",    "QA"),
                                  new JProperty("publicationCreator",  new JObject(new JProperty("country",             "de"),
                                                                                   new JProperty("nationalIdentifier",  "DE-NAP-QA")))
                              );

            foreach (var property in More)
                publication[property.Name] = property.Value;

            return publication;

        }

        #endregion


        #region AFullSectionIsReadAndWrittenAgain()

        [Test]
        public void AFullSectionIsReadAndWrittenAgain()
        {

            var json = Section(Publication(new JProperty("operatorIds",                new JArray("DE*GEF")),
                                           new JProperty("language",                   "de"),
                                           new JProperty("energyStatusDeltaInterval",  120),
                                           new JProperty("statusDeltaInterval",        1),
                                           new JProperty("snapshotInterval",           21600),
                                           new JProperty("maxDeltasPerSnapshot",       3500),
                                           new JProperty("mobilithek",                 new JObject(
                                                                                           new JProperty("staticPublicationId",          123),
                                                                                           new JProperty("dynamicPublicationId",         "18446744073709551615"),
                                                                                           new JProperty("clientCertificate",            "certs/mobilithek.p12"),
                                                                                           new JProperty("clientCertificatePassword",    "secret"),
                                                                                           new JProperty("trustedCertificates",          "certs/mobilithek-ca.pem"),
                                                                                           new JProperty("compress",                     false)
                                                                                       ))),
                               Pull: false);

            Assert.That(RoamingHubConfiguration.TryParse(json, out var configuration, out var error), Is.True, error);

            var datexII      = configuration!.DatexII!;
            var publication  = datexII.Publications!.Single();
            var mobilithek   = publication.Mobilithek!;

            Assert.Multiple(() => {

                Assert.That(configuration.IsEmpty,                         Is.False);
                Assert.That(datexII.EffectivePull,                         Is.False);

                Assert.That(publication.RoamingNetworkId,                  Is.EqualTo("QA"));
                Assert.That(publication.Country,                           Is.EqualTo("DE"));
                Assert.That(publication.NationalIdentifier,                Is.EqualTo("DE-NAP-QA"));
                Assert.That(publication.OperatorIds,                       Is.EqualTo(new[] { "DE*GEF" }));
                Assert.That(publication.Language,                          Is.EqualTo("de"));
                Assert.That(publication.EnergyStatusDeltaInterval,         Is.EqualTo(TimeSpan.FromMinutes(2)));
                Assert.That(publication.StatusDeltaInterval,               Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(publication.SnapshotInterval,                  Is.EqualTo(TimeSpan.FromHours(6)));
                Assert.That(publication.MaxDeltasPerSnapshot,              Is.EqualTo(3500));

                Assert.That(mobilithek.StaticPublicationId,                Is.EqualTo(123UL));
                Assert.That(mobilithek.DynamicPublicationId,               Is.EqualTo(UInt64.MaxValue));
                Assert.That(mobilithek.URL,                                Is.Null);
                Assert.That(mobilithek.Compress,                           Is.False);

                // The password stays out of what is logged.
                Assert.That(configuration.ToString(),                      Does.Not.Contain("secret"));

            });

            // Written as read.
            Assert.That(RoamingHubConfiguration.TryParse(configuration.ToJSON(), out var again, out error), Is.True, error);
            Assert.That(again!.ToJSON().ToString(), Is.EqualTo(configuration.ToJSON().ToString()));

        }

        #endregion

        #region PullIsOnByDefault()

        [Test]
        public void PullIsOnByDefault()
        {

            Assert.That(RoamingHubConfiguration.TryParse(Section(Publication()), out var configuration, out var error), Is.True, error);

            Assert.Multiple(() => {
                Assert.That(configuration!.DatexII!.EffectivePull,                Is.True);
                Assert.That(configuration.DatexII.Publications!.Single().Mobilithek, Is.Null);
            });

        }

        #endregion

        #region WhatIsWrongIsNamed(...)

        [TestCase("roamingNetworkId",    null,                                   "datex2.publications[0].roamingNetworkId")]
        [TestCase("publicationCreator",  null,                                   "datex2.publications[0].publicationCreator")]
        [TestCase("language",            "\"klingon\"",                          "datex2.publications[0].language")]
        [TestCase("snapshotInterval",    "0",                                    "datex2.publications[0].snapshotInterval")]
        [TestCase("mobilithek",          "{ \"dynamicPublicationId\": 1 }",      "datex2.publications[0].mobilithek.staticPublicationId")]
        [TestCase("mobilithek",          "{ \"staticPublicationId\": \"x\", \"dynamicPublicationId\": 1 }",
                                                                                 "datex2.publications[0].mobilithek.staticPublicationId")]
        [TestCase("mobilithek",          "{ \"staticPublicationId\": 1, \"dynamicPublicationId\": 2, \"url\": \"ftp://mobilithek.info\" }",
                                                                                 "datex2.publications[0].mobilithek.url")]
        public void WhatIsWrongIsNamed(String   Field,
                                       String?  Value,
                                       String   Named)
        {

            var publication = Publication();

            if (Value is null)
                publication.Remove(Field);
            else
                publication[Field] = JToken.Parse(Value);

            Assert.That(RoamingHubConfiguration.TryParse(Section(publication), out _, out var error), Is.False);
            Assert.That(error, Does.Contain(Named));

        }

        #endregion

        #region ANetworkIsPublishedOnlyOnce()

        [Test]
        public void ANetworkIsPublishedOnlyOnce()
        {

            var json = Section(Publication());
            ((JArray) json[DatexIIConfiguration.SectionName]!["publications"]!).Add(Publication());

            Assert.That(RoamingHubConfiguration.TryParse(json, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("only once"));

        }

        #endregion

    }

    #endregion


    #region DatexIITests

    /// <summary>
    /// A roaming network the "datex2" section names, published as DATEX II below "/datex2": made at runtime, or read
    /// back at a start; a roaming network it does not name is not.
    /// </summary>
    public class DatexIITests : ARoamingHubTests
    {

        #region Data

        private const String NewPath = "api/v1/poi/RNs";

        private readonly ECPrivateKeyParameters  rnoSigningKey   = NewKey(COSECurve.P256);
        private readonly ECPrivateKeyParameters  rnoIdentityKey  = NewKey(COSECurve.P521);

        #endregion

        #region Configuration - "QA" is published, to be pulled

        protected override JObject Configuration

            => new (
                   new JProperty("nts",     new JObject(
                       new JProperty("groups", new JObject(
                           new JProperty("legal", new JObject(
                               new JProperty("enabled",  false)
                           ))
                       ))
                   )),
                   new JProperty("datex2",  new JObject(
                       new JProperty("publications",  new JArray(
                           new JObject(
                               new JProperty("roamingNetworkId",    "QA"),
                               new JProperty("publicationCreator",  new JObject(new JProperty("country",             "DE"),
                                                                                 new JProperty("nationalIdentifier",  "DE-NAP-QA")))
                           )
                       ))
                   ))
               );

        #endregion


        #region (helper) NewKey(Curve) / Make(HTTP, Id)

        private static ECPrivateKeyParameters NewKey(COSECurve Curve)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(Curve.DomainParameters!, new SecureRandom()));
            return (ECPrivateKeyParameters) generator.GenerateKeyPair().Private;
        }

        /// <summary>
        /// Make a roaming network with the keys of its RNO, as an administrator does.
        /// </summary>
        private async Task Make(String Id)
        {

            using var admin   = await SignedIn();

            var response      = await admin.PostAsync(NewPath,
                                                      new StringContent(new JObject(
                                                                            new JProperty("@id",           Id),
                                                                            new JProperty("name",          new JObject(new JProperty("en", "Quality assurance")))
                                                                        ).ToString(),
                                                                        Encoding.UTF8,
                                                                        "application/json"));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());

        }

        #endregion

        #region (helper) Pulled(HTTP, Path)

        /// <summary>
        /// The answer to a GET of the given publication, once there is one: the publisher publishes the head of the
        /// roaming network right after it started, which is just after the roaming network was made.
        /// </summary>
        private static async Task<HttpResponseMessage> Pulled(HttpClient  HTTP,
                                                              String      Path)
        {

            var until = DateTime.UtcNow.AddSeconds(10);

            while (true)
            {

                var response = await HTTP.GetAsync(Path);

                if (response.StatusCode != HttpStatusCode.ServiceUnavailable || DateTime.UtcNow > until)
                    return response;

                await Task.Delay(50);

            }

        }

        #endregion


        #region ANetworkMadeAtRuntimeIsPublished()

        /// <summary>
        /// The table and the full status, as DATEX II 3.7 XML and JSON and as the JSON of the Mobilithek.
        /// </summary>
        [Test]
        public async Task ANetworkMadeAtRuntimeIsPublished()
        {

            using var anybody  = Anonymous();

            Assert.That((await anybody.GetAsync("datex2/RNs/QA/table.xml")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(RoamingHub.DatexIIBannerLines(), Is.EqualTo(new[] { ("DATEX II", "QA: not served yet, published once it is made") }));

            await Make("QA");

            // What the banner says of it, after it was made.
            Assert.That(RoamingHub.DatexIIBannerLines(), Is.EqualTo(new[] { ("DATEX II", $"QA: {BaseURL.TrimEnd('/')}/datex2/RNs/QA/") }));

            var tableXML       = await Pulled(anybody, "datex2/RNs/QA/table.xml");
            var statusXML      = await Pulled(anybody, "datex2/RNs/QA/status.xml");
            var tableJSON      = await Pulled(anybody, "datex2/RNs/QA/table.json");
            var statusJSON     = await Pulled(anybody, "datex2/RNs/QA/mobilithek/status.json");

            var tableText      = await tableXML.  Content.ReadAsStringAsync();
            var statusText     = await statusXML. Content.ReadAsStringAsync();
            var statusJSONText = await statusJSON.Content.ReadAsStringAsync();

            Assert.Multiple(() => {

                Assert.That(tableXML.  StatusCode,                         Is.EqualTo(HttpStatusCode.OK), tableText);
                Assert.That(tableXML.  Content.Headers.ContentType?.MediaType,  Does.Contain("xml"));
                Assert.That(tableText,                                     Does.Contain("DE-NAP-QA"));

                Assert.That(statusXML. StatusCode,                         Is.EqualTo(HttpStatusCode.OK), statusText);
                Assert.That(tableJSON. StatusCode,                         Is.EqualTo(HttpStatusCode.OK));

                Assert.That(statusJSON.StatusCode,                         Is.EqualTo(HttpStatusCode.OK), statusJSONText);
                Assert.That(statusJSONText,                                Does.Contain("messageContainer"));

            });

        }

        #endregion

        #region ANetworkTheSectionDoesNotNameIsNot()

        [Test]
        public async Task ANetworkTheSectionDoesNotNameIsNot()
        {

            await Make("QB");

            using var anybody = Anonymous();

            Assert.That((await anybody.GetAsync("datex2/RNs/QB/table.xml")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        }

        #endregion

        #region ANetworkReadBackAtAStartIsPublished()

        [Test]
        public async Task ANetworkReadBackAtAStartIsPublished()
        {

            await Make("QA");

            await RoamingHub.DisposeAsync();

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {

                using var anybody = new HttpClient { BaseAddress = new Uri(again.WebInterfaceURL.ToString()) };

                var table = await Pulled(anybody, "datex2/RNs/QA/table.xml");

                Assert.That(table.StatusCode, Is.EqualTo(HttpStatusCode.OK), await table.Content.ReadAsStringAsync());

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

    }

    #endregion


    #region DatexIIWithoutPullTests

    /// <summary>
    /// With "pull": false a roaming network is still published - to the Mobilithek, if the section says so - but
    /// nothing is served below "/datex2".
    /// </summary>
    public class DatexIIWithoutPullTests : ARoamingHubTests
    {

        protected override JObject Configuration

            => new (
                   new JProperty("nts",     new JObject(
                       new JProperty("groups", new JObject(
                           new JProperty("legal", new JObject(
                               new JProperty("enabled",  false)
                           ))
                       ))
                   )),
                   new JProperty("datex2",  new JObject(
                       new JProperty("pull",          false),
                       new JProperty("publications",  new JArray(
                           new JObject(
                               new JProperty("roamingNetworkId",    "QA"),
                               new JProperty("publicationCreator",  new JObject(new JProperty("country",             "DE"),
                                                                                 new JProperty("nationalIdentifier",  "DE-NAP-QA")))
                           )
                       ))
                   ))
               );

        [Test]
        public async Task NothingIsServed()
        {

            var rnoSigningKey   = new ECKeyPairGenerator();
            rnoSigningKey.Init(new ECKeyGenerationParameters(COSECurve.P256.DomainParameters!, new SecureRandom()));
            var rnoIdentityKey  = new ECKeyPairGenerator();
            rnoIdentityKey.Init(new ECKeyGenerationParameters(COSECurve.P521.DomainParameters!, new SecureRandom()));

            using var admin     = await SignedIn();

            var made            = await admin.PostAsync("api/v1/poi/RNs",
                                                        JSONBody(new JProperty("@id",           "QA")));

            Assert.That(made.StatusCode, Is.EqualTo(HttpStatusCode.Created), await made.Content.ReadAsStringAsync());

            await Task.Delay(500);

            using var anybody   = Anonymous();

            Assert.That((await anybody.GetAsync("datex2/RNs/QA/table.xml")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(RoamingHub.DatexIIBannerLines(),                                   Is.EqualTo(new[] { ("DATEX II", "QA: neither served nor pushed") }));

        }

    }

    #endregion


    #region DatexIIMobilithekTests

    /// <summary>
    /// A roaming network the "datex2" section names with a "mobilithek" block, pushed to a Mobilithek on this machine
    /// as the real one takes it: over mutual TLS, with a server certificate of a certification authority of its own,
    /// which only the "trustedCertificates" of the section make trusted, and the machine certificate of the data
    /// provider, a PKCS #12 file whose password is in an environment variable.
    /// </summary>
    public class DatexIIMobilithekTests
    {

        #region Data

        private const    String            CertificatePassword  = "mobilithek-test";

        private          String            directory            = default!;
        private          String            passwordVariable     = default!;
        private          X509Certificate2  machineCertificate   = default!;
        private          HTTPServer        mobilithek           = default!;
        private          RoamingHub        roamingHub           = default!;

        private readonly List<(String PublicationId, String? ClientCertificate, JObject JSON)>  pushes = [];

        #endregion


        #region (static) Certificates()

        /// <summary>
        /// A certification authority of the Mobilithek, its server certificate for 127.0.0.1, and a machine certificate,
        /// also as the PKCS #12 file the Mobilithek hands out.
        /// </summary>
        private static (X509Certificate2 CA, X509Certificate2 Server, X509Certificate2 Machine, Byte[] MachinePKCS12) Certificates()
        {

            var notBefore  = DateTimeOffset.UtcNow.AddDays(-1);
            var notAfter   = DateTimeOffset.UtcNow.AddDays( 1);

            using var caKey       = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var caRequest         = new CertificateRequest("CN=Mobilithek Test CA", caKey, HashAlgorithmName.SHA256);
            caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));
            using var ca          = caRequest.CreateSelfSigned(notBefore, notAfter);

            using var serverKey   = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var serverRequest     = new CertificateRequest("CN=127.0.0.1", serverKey, HashAlgorithmName.SHA256);
            var names             = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(System.Net.IPAddress.Loopback);
            serverRequest.CertificateExtensions.Add(names.Build());
            serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ new Oid("1.3.6.1.5.5.7.3.1") ], false));
            serverRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));
            using var serverOnly  = serverRequest.Create(ca, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));
            using var server      = serverOnly.CopyWithPrivateKey(serverKey);

            using var machineKey  = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var machineRequest    = new CertificateRequest("CN=DE-NAP-QA machine", machineKey, HashAlgorithmName.SHA256);
            machineRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            machineRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([ new Oid("1.3.6.1.5.5.7.3.2") ], false));
            using var machine     = machineRequest.CreateSelfSigned(notBefore, notAfter);

            var machinePKCS12     = machine.Export(X509ContentType.Pkcs12, CertificatePassword);

            // Through PKCS #12, as the TLS of Windows wants a key it signs with.
            return (X509CertificateLoader.LoadCertificate(ca.Export(X509ContentType.Cert)),
                    X509CertificateLoader.LoadPkcs12(server.Export(X509ContentType.Pkcs12, CertificatePassword), CertificatePassword),
                    X509CertificateLoader.LoadPkcs12(machinePKCS12, CertificatePassword),
                    machinePKCS12);

        }

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public async Task StartTheMobilithekAndTheHub()
        {

            directory         = TestRoamingHubs.TemporaryDirectory("datex2");
            passwordVariable  = $"ROAMINGHUB_TEST_MOBILITHEK_{Guid.NewGuid():N}";

            var (ca, server, machine, machinePKCS12) = Certificates();
            machineCertificate = machine;

            // The files the section names, relative to the configuration file.
            Directory.CreateDirectory(Path.Combine(directory, "certs"));
            File.WriteAllBytes(Path.Combine(directory, "certs", "mobilithek.p12"),     machinePKCS12);
            File.WriteAllText (Path.Combine(directory, "certs", "mobilithek-ca.pem"),  ca.ExportCertificatePem());

            Environment.SetEnvironmentVariable(passwordVariable, CertificatePassword);

            #region The Mobilithek, as far as a data provider sees it

            mobilithek = new HTTPServer(IPAddress:                   IPv4Address.Localhost,
                                        TCPPort:                     IPPort.Zero,
                                        ServerCertificateSelector:   (tcpServer, tcpClient) => server,
                                        ClientCertificateValidator:  (sender, certificate, chain, httpServer, policyErrors) =>
                                                                         certificate?.Thumbprint == machineCertificate.Thumbprint
                                                                             ? TLSValidationResult.Success()
                                                                             : TLSValidationResult.Failed("Not the machine certificate the Mobilithek issued!"),
                                        ClientCertificateRequired:   true,
                                        AutoStart:                   true);

            var api = new HTTPAPI(mobilithek);

            api.AddHandler(HTTPPath.Parse("/mobilithek/api/v1.0/publication/{publicationId}"),
                           HTTPMethod:    HTTPMethod.POST,
                           HTTPDelegate:  request => {

                               var body = request.HTTPBody ?? [];

                               // gzip, unless the server decoded it already.
                               if (body.Length > 2 && body[0] == 0x1f && body[1] == 0x8b)
                               {
                                   using var gzip     = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
                                   using var decoded  = new MemoryStream();
                                   gzip.CopyTo(decoded);
                                   body = decoded.ToArray();
                               }

                               lock (pushes)
                                   pushes.Add((request.ParsedURLParameters[0], request.ClientCertificate?.Thumbprint, JObject.Parse(Encoding.UTF8.GetString(body))));

                               return Task.FromResult(new HTTPResponse.Builder(request) { HTTPStatusCode = HTTPStatusCode.OK }.AsImmutable);

                           });

            #endregion

            var configuration = new JObject(
                                    new JProperty("nts",     new JObject(new JProperty("groups", new JObject(new JProperty("legal", new JObject(new JProperty("enabled", false))))))),
                                    new JProperty("datex2",  new JObject(
                                        new JProperty("publications", new JArray(
                                            new JObject(
                                                new JProperty("roamingNetworkId",    "QA"),
                                                new JProperty("publicationCreator",  new JObject(new JProperty("country",             "DE"),
                                                                                                  new JProperty("nationalIdentifier",  "DE-NAP-QA"))),
                                                new JProperty("mobilithek",          new JObject(
                                                    new JProperty("staticPublicationId",                           11),
                                                    new JProperty("dynamicPublicationId",                          "22"),
                                                    new JProperty("url",                                           $"https://127.0.0.1:{mobilithek.TCPPort}/mobilithek/api/v1.0/publication/"),
                                                    new JProperty("clientCertificate",                             "certs/mobilithek.p12"),
                                                    new JProperty("clientCertificatePasswordEnvironmentVariable",  passwordVariable),
                                                    new JProperty("trustedCertificates",                           "certs/mobilithek-ca.pem")
                                                ))
                                            )
                                        ))
                                    ))
                                );

            roamingHub = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(directory, configuration));

        }

        [TearDown]
        public async Task StopTheHubAndTheMobilithek()
        {

            if (roamingHub is not null)
                await roamingHub.DisposeAsync();

            if (mobilithek is not null)
                await mobilithek.DisposeAsync();

            Environment.SetEnvironmentVariable(passwordVariable, null);

            TestRoamingHubs.Remove(directory);

        }

        #endregion


        #region (static) NewKey(Curve) / Eventually(Condition)

        private static ECPrivateKeyParameters NewKey(COSECurve Curve)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(Curve.DomainParameters!, new SecureRandom()));
            return (ECPrivateKeyParameters) generator.GenerateKeyPair().Private;
        }

        private static async Task<Boolean> Eventually(Func<Boolean> Condition)
        {

            var until = DateTime.UtcNow.AddSeconds(15);

            while (DateTime.UtcNow < until)
            {

                if (Condition())
                    return true;

                await Task.Delay(50);

            }

            return Condition();

        }

        #endregion


        #region ANetworkMadeAtRuntimeIsPushedOverMutualTLS()

        /// <summary>
        /// Its table to the static publication, as the bare payload, and its full status to the dynamic one, in a
        /// message container as a snapshot; each over a connection the machine certificate authenticated.
        /// </summary>
        [Test]
        public async Task ANetworkMadeAtRuntimeIsPushedOverMutualTLS()
        {

            Assert.That(roamingHub.CreateRoamingNetwork(RoamingNetwork_Id.Parse("QA")).Success,
                        Is.True);

            Assert.That(await Eventually(() => { lock (pushes) return pushes.Count >= 2; }), Is.True, "The table and the full status were not pushed.");

            lock (pushes)
            {
                Assert.Multiple(() => {

                    Assert.That(pushes[0].PublicationId,                                                        Is.EqualTo("11"));
                    Assert.That(pushes[0].JSON["payload"],                                                      Is.Not.Null);
                    Assert.That(pushes[0].ClientCertificate,                                                    Is.EqualTo(machineCertificate.Thumbprint));

                    Assert.That(pushes[1].PublicationId,                                                        Is.EqualTo("22"));
                    Assert.That(pushes[1].JSON["messageContainer"],                                             Is.Not.Null);
                    Assert.That(pushes[1].JSON.SelectToken("$..codedExchangeProtocol.value")?.Value<String>(),  Is.EqualTo("snapshotPush"));
                    Assert.That(pushes[1].ClientCertificate,                                                    Is.EqualTo(machineCertificate.Thumbprint));

                });
            }

            Assert.That(roamingHub.DatexIIBannerLines().Single().Value,
                        Does.EndWith("pushed to the Mobilithek publications 11 (static) and 22 (dynamic)"));

        }

        #endregion

    }

    #endregion

}
