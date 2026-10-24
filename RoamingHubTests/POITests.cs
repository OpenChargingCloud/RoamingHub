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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

using org.GraphDefined.Vanaheimr.Aegir;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP;
using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

// Those of POI 3, not those of WWCP_Core the OCPI side knows.
using RoamingNetwork           = cloud.charging.open.protocols.WWCP.POI.RoamingNetwork;
using RoamingNetworkOperator   = cloud.charging.open.protocols.WWCP.POI.RoamingNetworkOperator;
using ChargingStationOperator  = cloud.charging.open.protocols.WWCP.POI.ChargingStationOperator;
using ChargingPool             = cloud.charging.open.protocols.WWCP.POI.ChargingPool;
using ChargingStation          = cloud.charging.open.protocols.WWCP.POI.ChargingStation;
using EVSE                     = cloud.charging.open.protocols.WWCP.POI.EVSE;
using POIEnergyMeter           = cloud.charging.open.protocols.WWCP.POI.EnergyMeter;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The hub is a roaming network operator from its first start, with keys of its own, and serves no roaming
    /// network yet. A new one is made by an administrator, operated by the hub, stored below "poi", served below
    /// "/poi" and there after a restart; the hub adds its parties with change sets it signs, and the parties fill
    /// it with change sets anybody may submit, which only the signatures of their owners let in.
    /// </summary>
    public class POITests : ARoamingHubTests
    {

        #region Data

        private static readonly HttpMethod  COMMIT     = new ("COMMIT");

        private const           String      NewPath    = "api/v1/poi/RNs";

        private readonly ECPrivateKeyParameters  csoSigningKey   = NewKey(COSECurve.P256);
        private readonly ECPrivateKeyParameters  csoIdentityKey  = NewKey(COSECurve.P521);

        #endregion


        #region (helper) NewKey(Curve)

        internal static ECPrivateKeyParameters NewKey(COSECurve Curve)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(Curve.DomainParameters!, new SecureRandom()));
            return (ECPrivateKeyParameters) generator.GenerateKeyPair().Private;
        }

        #endregion

        #region (helper) AddedParty(Hub, RoamingNetworkId, Id)

        /// <summary>
        /// The charging station operator of the given identification, with keys of its own, added to the given roaming
        /// network of the given hub: the hub signs it as the roaming network operator, and its change set waits until the
        /// operator signed it with its identity key, as the proof that it holds it (SPECIFICATION.md of WWCP POI, section
        /// 4.1); then it is committed.
        /// </summary>
        internal static RoamingNetworkPartyAddition AddedParty(RoamingHub  Hub,
                                                               String      RoamingNetworkId,
                                                               String      Id)
        {

            var identityKey  = NewKey(COSECurve.P521);
            var asked        = Hub.AddParty(RoamingNetwork_Id.Parse(RoamingNetworkId),
                                            nameof(ChargingStationOperator),
                                            new JObject(new JProperty("@id",           Id),
                                                        new JProperty("signingKeys",   new JArray(POIPublicKey.From(NewKey(COSECurve.P256)).ToJSON())),
                                                        new JProperty("identityKeys",  new JArray(POIPublicKey.From(identityKey).ToJSON()))));

            if (asked.Status != RoamingNetworkPartyStatus.Pending || asked.ChangeSet is null)
                return asked;

            Assert.That(Hub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse(RoamingNetworkId), out var history), Is.True);

            var proof        = asked.ChangeSet.Sign(identityKey, POIPublicKey.From(identityKey).Id, COSEAlgorithm.ES512).Signatures[^1];
            var result       = history!.AddPendingSignatures(asked.ChangeSet.Id, [ proof ]);

            return result.IsCommitted && result.Commit is not null
                       ? new (RoamingNetworkPartyStatus.Added,   asked.Message, result.Commit)
                       : new (RoamingNetworkPartyStatus.Refused, result.ErrorResponse ?? result.Status.ToString());

        }

        #endregion

        #region (helper) QA(Id = "QA")

        /// <summary>
        /// A roaming network to be made: nothing but its identification and its name.
        /// </summary>
        private static JObject QA(String Id = "QA")

            => new (
                   new JProperty("@id",   Id),
                   new JProperty("name",  new JObject(new JProperty("en", "Quality assurance")))
               );

        #endregion

        #region (helper) QAC()

        /// <summary>
        /// The charging station operator DE*QAC with its public keys: the signing key as POI writes a key,
        /// the identity key as the Base64 of its COSE key alone.
        /// </summary>
        private JObject QAC()

            => new (
                   new JProperty("@id",           "DE*QAC"),
                   new JProperty("name",          new JObject(new JProperty("en", "QA Charging"))),
                   new JProperty("signingKeys",   new JArray(POIPublicKey.From(csoSigningKey).ToJSON())),
                   new JProperty("identityKeys",  new JArray(POIPublicKey.From(csoIdentityKey).ToJSON()["key"]!))
               );

        #endregion

        #region (helper) AddQAC(Admin)

        /// <summary>
        /// DE*QAC added to QA over HTTP: asked for by the administrator, its change set waits, signed by the hub as the
        /// roaming network operator, until DE*QAC signed it with its identity key, as the proof that it holds it; the
        /// answer to that signature, the change set committed.
        /// </summary>
        private async Task<JObject> AddQAC(HttpClient Admin)
        {

            var asked      = await Post(Admin, QAC(), $"{NewPath}/QA/CSOs");
            var askedText  = await asked.Content.ReadAsStringAsync();

            Assert.That(asked.StatusCode, Is.EqualTo(HttpStatusCode.Accepted), askedText);

            // Its timestamps the text they are signed as.
            var askedJSON  = JObject.Load(new Newtonsoft.Json.JsonTextReader(new StringReader(askedText)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None });

            Assert.That(askedJSON["status"]?.Value<String>(),  Is.EqualTo("pending"));
            Assert.That(RoamingNetworkChangeSet.TryParse((JObject) askedJSON["changeSet"]!, out var changeSet, out var error), Is.True, error);

            var proof      = changeSet!.Sign(csoIdentityKey, POIPublicKey.From(csoIdentityKey).Id, COSEAlgorithm.ES512).Signatures[^1];

            using var anybody = Anonymous();

            var signed     = await Post(anybody,
                                        new JObject(new JProperty("signatures", new JArray(proof.ToJSON()))),
                                        $"poi/RNs/QA/history/changeSets/pending/{changeSet.Id}/signatures");

            var signedJSON = JObject.Parse(await signed.Content.ReadAsStringAsync());

            Assert.That(signedJSON["status"]?.Value<String>(), Is.EqualTo("committed"), signedJSON.ToString());

            return signedJSON;

        }

        #endregion

        #region (helper) Post(HTTP, JSON, Path = NewPath)

        private static Task<HttpResponseMessage> Post(HttpClient  HTTP,
                                                      JObject     JSON,
                                                      String      Path   = NewPath)

            => HTTP.PostAsync(Path, new StringContent(JSON.ToString(), Encoding.UTF8, "application/json"));

        #endregion

        #region (helper) Commit(HTTP, ChangeSet)

        /// <summary>
        /// Submit the given change set as canonical JSON, as a client of the POI API does.
        /// </summary>
        private static async Task<(HttpStatusCode StatusCode, String Text)> Commit(HttpClient               HTTP,
                                                                                  RoamingNetworkChangeSet  ChangeSet)
        {

            using var request   = new HttpRequestMessage(COMMIT, $"poi/RNs/{ChangeSet.RoamingNetworkId}/history/changeSets") {
                                      Content = new StringContent(CanonicalJSON.Serialize(ChangeSet.ToJSON()), Encoding.UTF8, "application/json")
                                  };

            using var response  = await HTTP.SendAsync(request);

            return (response.StatusCode, await response.Content.ReadAsStringAsync());

        }

        #endregion

        #region (helper) Head(Id)

        private RoamingNetwork Head(String Id = "QA")
        {

            Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse(Id), out var history), Is.True, $"'{Id}' is not served");

            return history!.Head.RoamingNetwork;

        }

        #endregion

        #region (helper) MadeWithQAC()

        /// <summary>
        /// QA made, and DE*QAC added to it by the hub.
        /// </summary>
        private async Task MadeWithQAC()
        {

            using var admin = await SignedIn();

            Assert.That((await Post(admin, QA())).StatusCode,                                           Is.EqualTo(HttpStatusCode.Created));

            await AddQAC(admin);

        }

        #endregion


        #region TheHubIsARoamingNetworkOperatorFromItsFirstStart()

        /// <summary>
        /// With a signing and an identity key made at the first start, kept beside the configuration, and public
        /// as every roaming network it operates names them - of which there is none yet.
        /// </summary>
        [Test]
        public async Task TheHubIsARoamingNetworkOperatorFromItsFirstStart()
        {

            var networkOperator  = RoamingHub.NetworkOperator;
            var directory        = Path.Combine(Directory, HubRoamingNetworkOperator.DirectoryName);

            using var anybody    = Anonymous();

            var served           = JObject.Parse(await anybody.GetStringAsync("api/v1/poi/roamingNetworkOperator"));
            var stored           = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(directory, HubRoamingNetworkOperator.OperatorFileName)));

            Assert.Multiple(() => {

                Assert.That(networkOperator.IsNew,                                                    Is.True);
                Assert.That(networkOperator.Id.ToString(),                                            Does.StartWith("RNO-"));
                Assert.That(networkOperator.Id.ToString(),                                            Is.EqualTo("RNO-" + networkOperator.IdentityKey.Id[..8].ToUpperInvariant()));
                Assert.That(networkOperator.SigningKey. Key.Curve,                                    Is.EqualTo(COSECurve.P256));
                Assert.That(networkOperator.IdentityKey.Key.Curve,                                    Is.EqualTo(COSECurve.P521));

                Assert.That(File.Exists(Path.Combine(directory, HubRoamingNetworkOperator.SigningKeyFileName)),   Is.True);
                Assert.That(File.Exists(Path.Combine(directory, HubRoamingNetworkOperator.IdentityKeyFileName)),  Is.True);
                Assert.That(File.ReadAllText(Path.Combine(directory, HubRoamingNetworkOperator.SigningKeyFileName)), Does.StartWith("-----BEGIN PRIVATE KEY-----"));

                // Only the public halves leave the directory.
                Assert.That(served["@id"]?.Value<String>(),                                           Is.EqualTo(networkOperator.Id.ToString()));
                // Its signing key named by its certificate, by its identity key; no signing keys of its own.
                Assert.That(served["signingKeys"],                                                    Is.Null);
                Assert.That(served["signingKeyCertificates"]?[0]?["signingKey"]?["@id"]?.Value<String>(),  Is.EqualTo(networkOperator.SigningKey.Id));
                Assert.That(served["signingKeyCertificates"]?[0]?["keyId"]?.Value<String>(),          Is.EqualTo(networkOperator.IdentityKey.Id));
                Assert.That(COSEKey.TryParse(Convert.FromBase64String(served["signingKeyCertificates"]![0]!["signingKey"]!["key"]!.Value<String>()!), out var key, out _) && !key.IsPrivate, Is.True);
                Assert.That(stored.ToString(),                                                        Is.EqualTo(served.ToString()));

                Assert.That(RoamingHub.POI.RoamingNetworks,                                           Is.Empty);
                Assert.That(RoamingHub.Log.Recent(200, Tag: "poi").Select(entry => entry.Message),
                            Has.Some.Contains($"This hub is the roaming network operator '{networkOperator.Id}' now"));

            });

        }

        #endregion

        #region TheSameOperatorAfterARestart()

        /// <summary>
        /// Read back at the next start, keys and all; and a missing private key stops the start rather than
        /// making another operator.
        /// </summary>
        [Test]
        public async Task TheSameOperatorAfterARestart()
        {

            var first       = RoamingHub.NetworkOperator;
            var signingKey  = Path.Combine(Directory, HubRoamingNetworkOperator.DirectoryName, HubRoamingNetworkOperator.SigningKeyFileName);
            var pem         = await File.ReadAllTextAsync(signingKey);

            await RoamingHub.DisposeAsync();

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {
                Assert.Multiple(() => {
                    Assert.That(again.NetworkOperator.IsNew,           Is.False);
                    Assert.That(again.NetworkOperator.Id,              Is.EqualTo(first.Id));
                    Assert.That(again.NetworkOperator.SigningKey.Id,   Is.EqualTo(first.SigningKey.Id));
                    Assert.That(again.NetworkOperator.IdentityKey.Id,  Is.EqualTo(first.IdentityKey.Id));
                    Assert.That(File.ReadAllText(signingKey),          Is.EqualTo(pem));
                });
            }
            finally
            {
                await again.DisposeAsync();
            }

            File.Delete(signingKey);

            Assert.That(() => HubRoamingNetworkOperator.OpenOrCreate(Path.GetDirectoryName(signingKey)!),
                        Throws.InvalidOperationException.With.Message.Contains("is missing"));

        }

        #endregion

        #region AnAdministratorMakesARoamingNetworkThatIsServedAndStored()

        /// <summary>
        /// Made with its name only, operated by the hub, whose signing key signs its genesis commit; at revision 0,
        /// in its own directory, and in the list of the POI API.
        /// </summary>
        [Test]
        public async Task AnAdministratorMakesARoamingNetworkThatIsServedAndStored()
        {

            using var admin      = await SignedIn();

            var made             = await Post(admin, QA());
            var madeJSON         = JObject.Parse(await made.Content.ReadAsStringAsync());

            using var anybody    = Anonymous();

            var list             = JArray. Parse(await anybody.GetStringAsync("poi/RNs"));
            var network          = JObject.Parse(await anybody.GetStringAsync("poi/RNs/QA"));
            var networkOperator  = network[RoamingNetwork.PropertyKeys.Operator];

            Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out var history), Is.True);

            Assert.Multiple(() => {

                Assert.That(made.StatusCode,                                                          Is.EqualTo(HttpStatusCode.Created), madeJSON.ToString());
                Assert.That(madeJSON["revision"]?.Value<Int32>(),                                     Is.EqualTo(0));
                Assert.That(madeJSON["roamingNetwork"]?["@id"]?.Value<String>(),                      Is.EqualTo("QA"));
                Assert.That(madeJSON["roamingNetwork"]?[RoamingNetwork.PropertyKeys.Operator]?["@id"]?.Value<String>(),  Is.EqualTo(RoamingHub.NetworkOperator.Id.ToString()));

                Assert.That(list.Select(entry => entry["@id"]?.Value<String>()),                      Does.Contain("QA"));
                Assert.That(network["name"]?["en"]?.Value<String>(),                                  Is.EqualTo("Quality assurance"));
                Assert.That(network["signingKeys"],                                                   Is.Null);
                Assert.That(networkOperator?["@id"]?.Value<String>(),                                 Is.EqualTo(RoamingHub.NetworkOperator.Id.ToString()));
                Assert.That(networkOperator?["signingKeyCertificates"]?[0]?["signingKey"]?["@id"]?.Value<String>(),  Is.EqualTo(RoamingHub.NetworkOperator.SigningKey. Id));
                Assert.That(networkOperator?["identityKeys"]?[0]?["@id"]?.Value<String>(),            Is.EqualTo(RoamingHub.NetworkOperator.IdentityKey.Id));
                Assert.That(network["chargingStationOperators"],                                      Is.Null);

                var signature = history!.Head.Commit.Signatures.Single();
                Assert.That(signature.Role,                                                           Is.EqualTo(RoamingNetworkCommitSignatureRoles.RoamingNetworkOperator));
                Assert.That(signature.KeyId,                                                          Is.EqualTo(RoamingHub.NetworkOperator.SigningKey.Id));
                Assert.That(signature.Signer,                                                         Is.EqualTo(RoamingHub.NetworkOperator.Id.ToString()));

                Assert.That(System.IO.Directory.Exists(Path.Combine(RoamingHub.POIDirectory, "QA")),  Is.True);

            });

        }

        #endregion

        #region NobodySignedInMakesOneOrAddsAParty()

        [Test]
        public async Task NobodySignedInMakesOneOrAddsAParty()
        {

            using var anybody  = Anonymous();

            var refused        = await Post(anybody, QA());

            Assert.Multiple(() => {
                Assert.That(refused.StatusCode,                                                       Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
                Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out _),  Is.False);
            });

            using (var admin = await SignedIn())
                Assert.That((await Post(admin, QA())).StatusCode,                                     Is.EqualTo(HttpStatusCode.Created));

            var party          = await Post(anybody, QAC(), $"{NewPath}/QA/CSOs");

            Assert.Multiple(() => {
                Assert.That(party.StatusCode,                                                         Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
                Assert.That(Head().ChargingStationOperators,                                          Is.Empty);
            });

        }

        #endregion

        #region WhatIsNoRoamingNetworkThisHubMakesIsRefused(Case, Said)

        /// <summary>
        /// 400, with why: and nothing made, no directory either.
        /// </summary>
        [TestCase("keys",          "operated by this hub")]
        [TestCase("an operator",   "operated by this hub")]
        [TestCase("a CSO",         "not with 'chargingStationOperators'")]
        [TestCase("a path",        "letters, digits")]
        public async Task WhatIsNoRoamingNetworkThisHubMakesIsRefused(String Case, String Said)
        {

            var json = QA(Case == "a path" ? "../QA" : "QA");

            switch (Case)
            {

                case "keys":
                    json["signingKeys"] = new JArray(POIPublicKey.From(NewKey(COSECurve.P256)).ToJSON());
                    break;

                case "an operator":
                    json[RoamingNetwork.PropertyKeys.Operator] = new JObject(new JProperty("@id", "Somebody"));
                    break;

                case "a CSO":
                    json["chargingStationOperators"] = new JArray(new JObject(new JProperty("@id", "DE*QAC")));
                    break;

            }

            using var admin  = await SignedIn();

            var refused      = await Post(admin, json);
            var text         = await refused.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That(refused.StatusCode,                                                       Is.EqualTo(HttpStatusCode.BadRequest), text);
                Assert.That(text,                                                                     Does.Contain(Said));
                Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out _),  Is.False);
                Assert.That(System.IO.Directory.Exists(Path.Combine(RoamingHub.POIDirectory, "QA")),  Is.False);
            });

        }

        #endregion

        #region ASecondOneOfTheSameIdentificationIsAConflict()

        /// <summary>
        /// Also where it differs in case only, as the identifications of roaming networks do not tell case apart.
        /// </summary>
        [Test]
        public async Task ASecondOneOfTheSameIdentificationIsAConflict()
        {

            using var admin  = await SignedIn();

            var first        = await Post(admin, QA("QA"));
            var second       = await Post(admin, QA("qa"));

            Assert.Multiple(() => {
                Assert.That(first. StatusCode,  Is.EqualTo(HttpStatusCode.Created));
                Assert.That(second.StatusCode,  Is.EqualTo(HttpStatusCode.Conflict));
            });

        }

        #endregion

        #region TheHubAddsAnOperatorThatAddsItsPools()

        /// <summary>
        /// The hub adds DE*QAC with its public keys, signed by the hub; DE*QAC adds a charging pool with a station
        /// and two EVSEs, submitted by somebody not signed in at all - and the same pool signed by the hub is
        /// refused, as it is not the hub's.
        /// </summary>
        [Test]
        public async Task TheHubAddsAnOperatorThatAddsItsPools()
        {

            using (var admin = await SignedIn())
                Assert.That((await Post(admin, QA())).StatusCode, Is.EqualTo(HttpStatusCode.Created));

            JObject addedJSON;

            using (var admin = await SignedIn())
                addedJSON        = await AddQAC(admin);

            using var anybody    = Anonymous();

            var pool             = new ChargingPool(
                                       ChargingPool_Id.Parse("DE*QAC*P1"),
                                       Name:              I18NString.Create(Languages.en, "QA Pool 1"),
                                       GeoLocation:       GeoCoordinate.Parse(50.9271, 11.5892),
                                       ChargingStations:  [
                                           new ChargingStation(
                                               ChargingStation_Id.Parse("DE*QAC*S1"),
                                               EVSEs: [
                                                   new EVSE(EVSE_Id.Parse("DE*QAC*E1*1"), MaxPower: Watt.FromW(11000)),
                                                   new EVSE(EVSE_Id.Parse("DE*QAC*E1*2"), MaxPower: Watt.FromW(11000))
                                               ]
                                           )
                                       ]
                                   );

            var someoneElse      = NewKey(COSECurve.P256);

            var poolByAnother    = await Commit(anybody, Head().CreateChangeSet([ RoamingNetworkChange.Add(pool) ]).
                                                                 Sign(someoneElse, POIPublicKey.From(someoneElse).Id, COSEAlgorithm.ES256));

            var addPool          = await Commit(anybody, Head().CreateChangeSet([ RoamingNetworkChange.Add(pool) ]).
                                                                 Sign(csoSigningKey, POIPublicKey.From(csoSigningKey).Id, COSEAlgorithm.ES256));

            var served           = JObject.Parse(await anybody.GetStringAsync("poi/RNs/QA/CSOs/DE*QAC/CPs/DE*QAC*P1"));
            var qac              = Head().GetChargingStationOperatorById(ChargingStationOperator_Id.Parse("DE*QAC"))!;

            Assert.Multiple(() => {

                Assert.That(addedJSON["commitId"]?.Value<String>(),                                   Is.Not.Null.And.Not.Empty, addedJSON.ToString());
                Assert.That(qac.SigningKeys. Single().Id,                                             Is.EqualTo(POIPublicKey.From(csoSigningKey). Id));
                Assert.That(qac.IdentityKeys.Single().Id,                                             Is.EqualTo(POIPublicKey.From(csoIdentityKey).Id));

                Assert.That(poolByAnother.StatusCode,                                                 Is.EqualTo(HttpStatusCode.UnprocessableEntity),  poolByAnother.Text);
                Assert.That(poolByAnother.Text,                                                       Does.Contain("is no key of a party"));
                Assert.That(addPool.StatusCode,                                                       Is.EqualTo(HttpStatusCode.Created),              addPool.Text);

                Assert.That(served["chargingStations"]?[0]?["EVSEs"]?.Count(),                        Is.EqualTo(2));
                Assert.That(RoamingHub.Log.Recent(100, Tag: "commit").Select(entry => entry.Message),
                            Has.Some.Contains("The roaming network 'QA' is at revision 2"));

            });

        }

        #endregion

        #region TheHubAddsManufacturersTheStationsAndMetersOfAnOperatorName()

        /// <summary>
        /// A charging station naming a manufacturer and an energy meter naming its own are refused until the hub added both
        /// to the registries of the roaming network, committed at once, signed by the hub; then DE*QAC adds them. The same
        /// manufacturer again is a conflict, one naming an identity key waits for its signature, and nobody signed in adds
        /// none.
        /// </summary>
        [Test]
        public async Task TheHubAddsManufacturersTheStationsAndMetersOfAnOperatorName()
        {

            await MadeWithQAC();

            using var anybody     = Anonymous();
            using var admin       = await SignedIn();

            var pool              = new ChargingPool(
                                        ChargingPool_Id.Parse("DE*QAC*P1"),
                                        ChargingStations:  [
                                            new ChargingStation(
                                                ChargingStation_Id.Parse("DE*QAC*S1"),
                                                ManufacturerId:  ChargingStationManufacturer_Id.Parse("ACME"),
                                                EVSEs: [
                                                    new EVSE(EVSE_Id.Parse("DE*QAC*E1*1"),
                                                             MaxPower:     Watt.FromW(11000),
                                                             EnergyMeter:  new POIEnergyMeter(EnergyMeter_Id.Parse("DE*QAC*M1*1"),
                                                                                              ManufacturerId:  EnergyMeterManufacturer_Id.Parse("METERS")))
                                                ]
                                            )
                                        ]
                                    );

            RoamingNetworkChangeSet AddPool()
                => Head().CreateChangeSet([ RoamingNetworkChange.Add(pool) ]).
                          Sign(csoSigningKey, POIPublicKey.From(csoSigningKey).Id, COSEAlgorithm.ES256);

            // Neither manufacturer is one of QA yet: no change set naming them is made.
            var poolBefore        = Assert.Throws<ArgumentException>(() => AddPool());

            var byAnybody         = await Post(anybody, new JObject(new JProperty("@id", "ACME")), $"{NewPath}/QA/CSMs");

            var stationMaker      = await Post(admin, new JObject(new JProperty("@id",       "ACME"),
                                                                  new JProperty("name",      new JObject(new JProperty("en", "ACME Chargers"))),
                                                                  new JProperty("homepage",  "https://acme.example")),
                                           $"{NewPath}/QA/CSMs");

            var meterMaker        = await Post(admin, new JObject(new JProperty("@id",       "METERS"),
                                                                  new JProperty("name",      new JObject(new JProperty("en", "Example Meters")))),
                                           $"{NewPath}/QA/EMMs");

            var again             = await Post(admin, new JObject(new JProperty("@id", "acme")), $"{NewPath}/QA/CSMs");
            var unknownKind       = await Post(admin, new JObject(new JProperty("@id", "ACME")), $"{NewPath}/QA/XYZs");

            // A manufacturer naming an identity key signs its change set as the proof that it holds it, as a party does.
            var meterIdentityKey  = NewKey(COSECurve.P521);
            var withKey           = await Post(admin, new JObject(new JProperty("@id",           "KEYED"),
                                                                  new JProperty("identityKeys",  new JArray(POIPublicKey.From(meterIdentityKey).ToJSON()))),
                                           $"{NewPath}/QA/EMMs");

            var poolAfter         = await Commit(anybody, AddPool());

            var stationMakerText  = await stationMaker.Content.ReadAsStringAsync();
            var meterMakerText    = await meterMaker.  Content.ReadAsStringAsync();
            var unknownKindText   = await unknownKind. Content.ReadAsStringAsync();
            var withKeyText       = await withKey.     Content.ReadAsStringAsync();
            var meterMakers       = JArray. Parse(await anybody.GetStringAsync("poi/RNs/QA/EMMs"));
            var served            = JObject.Parse(await anybody.GetStringAsync("poi/RNs/QA/CSMs/ACME"));

            Assert.Multiple(() => {

                Assert.That(poolBefore?.Message,                                                      Does.Contain("The manufacturer 'ACME' of the charging station 'DE*QAC*S1' is unknown"));
                Assert.That(byAnybody.StatusCode,                                                     Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));

                Assert.That(stationMaker.StatusCode,                                                  Is.EqualTo(HttpStatusCode.Created), stationMakerText);
                Assert.That(JObject.Parse(stationMakerText)["commitId"]?.Value<String>(),             Is.Not.Null.And.Not.Empty);
                Assert.That(meterMaker.StatusCode,                                                    Is.EqualTo(HttpStatusCode.Created), meterMakerText);
                Assert.That(again.StatusCode,                                                         Is.EqualTo(HttpStatusCode.Conflict));
                Assert.That(unknownKind.StatusCode,                                                   Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(unknownKindText,                                                          Does.Contain("CSMs | EMMs"));
                Assert.That(withKey.StatusCode,                                                       Is.EqualTo(HttpStatusCode.Accepted), withKeyText);
                Assert.That(JObject.Parse(withKeyText)["status"]?.Value<String>(),                    Is.EqualTo("pending"));

                Assert.That(poolAfter.StatusCode,                                                     Is.EqualTo(HttpStatusCode.Created), poolAfter.Text);
                Assert.That(served["homepage"]?.Value<String>(),                                      Is.EqualTo("https://acme.example"));
                Assert.That(meterMakers.Select(entry => entry["@id"]?.Value<String>()),               Is.EqualTo(new[] { "METERS" }));

            });

        }

        #endregion

        #region AnAppScansTheTrustAnchorAndChecksAStationProof()

        /// <summary>
        /// The roaming network made here states the keys of its operator from its genesis on, and every head is attested
        /// by its signing key: an app that scanned the trust anchor - the identity key of the operator this hub is - gets
        /// the proof of a charging station and checks it offline.
        /// </summary>
        [Test]
        public async Task AnAppScansTheTrustAnchorAndChecksAStationProof()
        {

            await MadeWithQAC();

            using var anybody  = Anonymous();

            var pool           = new ChargingPool(
                                     ChargingPool_Id.Parse("DE*QAC*P1"),
                                     ChargingStations:  [
                                         new ChargingStation(
                                             ChargingStation_Id.Parse("DE*QAC*S1"),
                                             EVSEs: [ new EVSE(EVSE_Id.Parse("DE*QAC*E1*1"), MaxPower: Watt.FromW(11000)) ]
                                         )
                                     ]
                                 );

            var addPool        = await Commit(anybody, Head().CreateChangeSet([ RoamingNetworkChange.Add(pool) ]).
                                                                 Sign(csoSigningKey, POIPublicKey.From(csoSigningKey).Id, COSEAlgorithm.ES256));

            // The operator shows the QR code of its trust anchor; the app scans it.
            var trustAnchor    = JObject.Parse(await anybody.GetStringAsync("poi/RNs/QA/trustAnchor"));

            Assert.That(RoamingNetworkTrustAnchor.TryParse(trustAnchor["uri"]!.Value<String>()!, out var anchor, out var error), Is.True, error);

            // Below the roaming network the anchor names, the proof of the charging station.
            var proofText      = await anybody.GetStringAsync($"{anchor!.URLs[0]}/CSs/DE*QAC*S1/proof");

            Assert.That(RoamingNetworkProof.TryParse(proofText, out var proof, out error), Is.True, error);

            Assert.Multiple(() => {

                Assert.That(addPool.StatusCode,                                                                     Is.EqualTo(HttpStatusCode.Created), addPool.Text);

                Assert.That(anchor.Keys,                                                                            Is.EqualTo(new[] { RoamingHub.NetworkOperator.IdentityKey.Id }));
                Assert.That(Head().Operator!.SigningKeyCertificates.Select(certificate => certificate.SigningKey.Id),  Is.EqualTo(new[] { RoamingHub.NetworkOperator.SigningKey.Id }));
                Assert.That(Head().Operator!.SigningKeyCertificates.Single().KeyId,                                 Is.EqualTo(RoamingHub.NetworkOperator.IdentityKey.Id));
                Assert.That(Head().Operator!.KeyStatement!.Signatures.Single().KeyId,                               Is.EqualTo(RoamingHub.NetworkOperator.IdentityKey.Id));

                Assert.That(proof!.Verify(anchor.Keys, out var attestation, out var verifyError),                   Is.True, verifyError);
                Assert.That(attestation!.CommitId,                                                                  Is.EqualTo(proof.CommitId));
                Assert.That(attestation.KeyId,                                                                      Is.EqualTo(RoamingHub.NetworkOperator.SigningKey.Id));
                Assert.That(proof.Given("chargingStationOperators", "chargingPools", "chargingStations").Single()["@id"]!.Value<String>(),
                            Is.EqualTo("DE*QAC*S1"));

                // A key nobody scanned proves nothing.
                Assert.That(proof.Verify([ RoamingHub.NetworkOperator.SigningKey.Id ], out _, out _),               Is.False);

            });

        }

        #endregion

        #region WhatIsNoPartyThisHubAddsIsRefused(Case, Status, Said)

        /// <summary>
        /// With why, and nothing committed.
        /// </summary>
        [TestCase("no network",       HttpStatusCode.NotFound,             "serves no roaming network 'XX'")]
        [TestCase("no party type",    HttpStatusCode.NotFound,             "CSOs | EMPs | GOs | POs")]
        [TestCase("no signing key",   HttpStatusCode.BadRequest,           "at least one signing key")]
        [TestCase("a private key",    HttpStatusCode.BadRequest,           "this is a private key")]
        [TestCase("its pools",        HttpStatusCode.UnprocessableEntity,  "must be added without its chargingPools")]
        [TestCase("twice",            HttpStatusCode.Conflict,             "has a ChargingStationOperator 'DE*QAC' already")]
        public async Task WhatIsNoPartyThisHubAddsIsRefused(String Case, HttpStatusCode Status, String Said)
        {

            using var admin  = await SignedIn();

            Assert.That((await Post(admin, QA())).StatusCode, Is.EqualTo(HttpStatusCode.Created));

            var json         = QAC();
            var path         = $"{NewPath}/QA/CSOs";

            switch (Case)
            {

                case "no network":
                    path = $"{NewPath}/XX/CSOs";
                    break;

                case "no party type":
                    path = $"{NewPath}/QA/CPs";
                    break;

                case "no signing key":
                    json.Remove("signingKeys");
                    break;

                case "a private key":
                    json["signingKeys"] = new JArray(Convert.ToBase64String(COSEKey.From(csoSigningKey).ToByteArray(CBORWriterOptions.Canonical)));
                    break;

                case "its pools":
                    json["chargingPools"] = new JArray(new JObject(new JProperty("@id", "DE*QAC*P1"), new JProperty("created", "2026-10-17T00:00:00Z")));
                    break;

                case "twice":
                    await AddQAC(admin);
                    break;

            }

            var refused      = await Post(admin, json, path);
            var text         = await refused.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That(refused.StatusCode,                                                       Is.EqualTo(Status), text);
                Assert.That(text,                                                                     Does.Contain(Said));
                Assert.That(Head().ChargingStationOperators.Count(),                                  Is.EqualTo(Case == "twice" ? 1 : 0));
            });

        }

        #endregion

        #region ARoamingNetworkOfAnotherOperatorIsServedButNotAddedTo()

        /// <summary>
        /// A roaming network found below "poi" whose operator is another one - e.g. one this hub follows - is
        /// served, but the hub neither signs its commits nor adds its parties: its operator does.
        /// </summary>
        [Test]
        public async Task ARoamingNetworkOfAnotherOperatorIsServedButNotAddedTo()
        {

            await RoamingHub.DisposeAsync();

            var otherIdentity = NewKey(COSECurve.P521);
            var otherOperator = new RoamingNetworkOperator(RoamingNetworkOperator_Id.Parse("Somebody"),
                                                           IdentityKeys:            [ POIPublicKey.From(otherIdentity) ],
                                                           SigningKeyCertificates:  [ RoamingNetworkSigningKeyCertificate.Create(otherIdentity,
                                                                                                                                 COSEAlgorithm.ES512,
                                                                                                                                 RoamingNetworkOperator_Id.Parse("Somebody"),
                                                                                                                                 POIPublicKey.From(NewKey(COSECurve.P256)),
                                                                                                                                 Timestamp.Now) ]);

            using (var store = new RoamingNetworkHistoryFileStore(Path.Combine(Directory, RoamingHub.POIDirectoryName, "OTHER")))
                RoamingNetworkHistory.Start(new RoamingNetwork(RoamingNetwork_Id.Parse("OTHER"), Operator: otherOperator), Store: store);

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {

                Assert.That(again.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("OTHER"), out var history), Is.True);

                var refused = again.AddParty(RoamingNetwork_Id.Parse("OTHER"), nameof(ChargingStationOperator), QAC());

                Assert.Multiple(() => {
                    Assert.That(history!.Signers,                                                     Is.Empty);
                    Assert.That(refused.Status,                                                       Is.EqualTo(RoamingNetworkPartyStatus.NotOperatedHere));
                    Assert.That(refused.Message,                                                      Does.Contain("is operated by 'Somebody', not by this hub"));
                });

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

        #region AMadeRoamingNetworkIsThereAfterARestart()

        /// <summary>
        /// Read back from its directory at the next start, with what was committed to it, and its commits signed
        /// by the hub again.
        /// </summary>
        [Test]
        public async Task AMadeRoamingNetworkIsThereAfterARestart()
        {

            await MadeWithQAC();

            await RoamingHub.DisposeAsync();

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {

                Assert.That(again.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out var history), Is.True);

                var added = AddedParty(again, "QA", "DE*QAD");

                Assert.Multiple(() => {
                    Assert.That(added.Status,                                                         Is.EqualTo(RoamingNetworkPartyStatus.Added), added.Message);
                    Assert.That(history!.Head.Commit.Revision,                                        Is.EqualTo(2));
                    Assert.That(history.Head.RoamingNetwork.TryGetChargingStationOperatorById(ChargingStationOperator_Id.Parse("DE*QAC"), out _),  Is.True);
                    Assert.That(history.Head.Commit.Signatures.Single().KeyId,                       Is.EqualTo(again.NetworkOperator.SigningKey.Id));
                });

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

    }


    /// <summary>
    /// The configuration file says who the roaming network operator of a hub is, at its first start.
    /// </summary>
    public class ConfiguredRoamingNetworkOperatorTests : ARoamingHubTests
    {

        #region Configuration

        protected override JObject Configuration
        {
            get
            {
                var configuration = TestRoamingHubs.Offline;
                configuration[HubRoamingNetworkOperator.ConfigurationSection] = new JObject(
                                                                                    new JProperty("@id",   "GraphDefined"),
                                                                                    new JProperty("name",  "GraphDefined GmbH")
                                                                                );
                return configuration;
            }
        }

        #endregion

        #region TheConfigurationNamesTheOperator()

        [Test]
        public void TheConfigurationNamesTheOperator()
        {

            Assert.Multiple(() => {
                Assert.That(RoamingHub.NetworkOperator.Id.ToString(),                Is.EqualTo("GraphDefined"));
                Assert.That(RoamingHub.NetworkOperator.Operator.Name.FirstText(),    Is.EqualTo("GraphDefined GmbH"));
            });

        }

        #endregion

    }

}
