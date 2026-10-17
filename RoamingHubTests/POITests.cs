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
using ChargingStationOperator  = cloud.charging.open.protocols.WWCP.POI.ChargingStationOperator;
using ChargingPool             = cloud.charging.open.protocols.WWCP.POI.ChargingPool;
using ChargingStation          = cloud.charging.open.protocols.WWCP.POI.ChargingStation;
using EVSE                     = cloud.charging.open.protocols.WWCP.POI.EVSE;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A new roaming network of the points of interest: made by an administrator with the public keys of its
    /// roaming network operator, stored below "poi", served below "/poi" and there after a restart; and filled by
    /// change sets anybody may submit, which only the signatures of their owners let in.
    /// </summary>
    public class POITests : ARoamingHubTests
    {

        #region Data

        private static readonly HttpMethod  COMMIT     = new ("COMMIT");

        private const           String      NewPath    = "api/v1/poi/roamingNetworks";

        private readonly ECPrivateKeyParameters  rnoSigningKey   = NewKey(COSECurve.P256);
        private readonly ECPrivateKeyParameters  rnoIdentityKey  = NewKey(COSECurve.P521);
        private readonly ECPrivateKeyParameters  csoSigningKey   = NewKey(COSECurve.P256);
        private readonly ECPrivateKeyParameters  csoIdentityKey  = NewKey(COSECurve.P521);

        #endregion


        #region (helper) NewKey(Curve)

        private static ECPrivateKeyParameters NewKey(COSECurve Curve)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(Curve.DomainParameters!, new SecureRandom()));
            return (ECPrivateKeyParameters) generator.GenerateKeyPair().Private;
        }

        #endregion

        #region (helper) QA(Id = "QA")

        /// <summary>
        /// A roaming network to be made, with the keys of its RNO: the signing key as POI writes a key, the
        /// identity key as the Base64 of its COSE key alone.
        /// </summary>
        private JObject QA(String Id = "QA")

            => new (
                   new JProperty("@id",           Id),
                   new JProperty("name",          new JObject(new JProperty("en", "Quality assurance"))),
                   new JProperty("signingKeys",   new JArray(POIPublicKey.From(rnoSigningKey).ToJSON())),
                   new JProperty("identityKeys",  new JArray(POIPublicKey.From(rnoIdentityKey).ToJSON()["key"]!))
               );

        #endregion

        #region (helper) Post(HTTP, JSON)

        private static Task<HttpResponseMessage> Post(HttpClient  HTTP,
                                                      JObject     JSON)

            => HTTP.PostAsync(NewPath, new StringContent(JSON.ToString(), Encoding.UTF8, "application/json"));

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


        #region AnAdministratorMakesARoamingNetworkThatIsServedAndStored()

        /// <summary>
        /// Made with the public keys of its RNO, which the hub calculates the identification of where it is not
        /// given; at revision 0, in its own directory, and in the list of the POI API.
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

            Assert.Multiple(() => {

                Assert.That(made.StatusCode,                                         Is.EqualTo(HttpStatusCode.Created), madeJSON.ToString());
                Assert.That(madeJSON["revision"]?.Value<Int32>(),                    Is.EqualTo(0));
                Assert.That(madeJSON["roamingNetwork"]?["@id"]?.Value<String>(),     Is.EqualTo("QA"));

                Assert.That(list.Select(entry => entry["@id"]?.Value<String>()),     Does.Contain("QA"));
                Assert.That(network["name"]?["en"]?.Value<String>(),                 Is.EqualTo("Quality assurance"));
                Assert.That(network["signingKeys"]?[0]?["@id"]?.Value<String>(),     Is.EqualTo(POIPublicKey.From(rnoSigningKey). Id));
                Assert.That(network["identityKeys"]?[0]?["@id"]?.Value<String>(),    Is.EqualTo(POIPublicKey.From(rnoIdentityKey).Id));
                Assert.That(network["chargingStationOperators"],                     Is.Null);

                Assert.That(System.IO.Directory.Exists(Path.Combine(RoamingHub.POIDirectory, "QA")),  Is.True);

            });

        }

        #endregion

        #region NobodySignedInMakesOne()

        [Test]
        public async Task NobodySignedInMakesOne()
        {

            using var anybody  = Anonymous();

            var refused        = await Post(anybody, QA());

            Assert.Multiple(() => {
                Assert.That(refused.StatusCode,                                      Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
                Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out _),  Is.False);
            });

        }

        #endregion

        #region WhatIsNoRoamingNetworkThisHubMakesIsRefused(Case, Said)

        /// <summary>
        /// 400, with why: and nothing made, no directory either.
        /// </summary>
        [TestCase("no signing key",    "at least one signing key")]
        [TestCase("no identity key",   "at least one identity key")]
        [TestCase("a private key",     "this is a private key")]
        [TestCase("a wrong thumbprint", "")]
        [TestCase("an operator",       "not with 'chargingStationOperators'")]
        [TestCase("a path",            "letters, digits")]
        public async Task WhatIsNoRoamingNetworkThisHubMakesIsRefused(String Case, String Said)
        {

            var json = QA(Case == "a path" ? "../QA" : "QA");

            switch (Case)
            {

                case "no signing key":
                    json["signingKeys"] = new JArray();
                    break;

                case "no identity key":
                    json.Remove("identityKeys");
                    break;

                case "a private key":
                    json["signingKeys"] = new JArray(Convert.ToBase64String(COSEKey.From(rnoSigningKey).ToByteArray(CBORWriterOptions.Canonical)));
                    break;

                case "a wrong thumbprint":
                    json["signingKeys"]![0]!["@id"] = POIPublicKey.From(csoSigningKey).Id;
                    break;

                case "an operator":
                    json["chargingStationOperators"] = new JArray(new JObject(new JProperty("@id", "DE*QAC")));
                    break;

            }

            using var admin  = await SignedIn();

            var refused      = await Post(admin, json);
            var text         = await refused.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That(refused.StatusCode,  Is.EqualTo(HttpStatusCode.BadRequest), text);
                Assert.That(text,                Does.Contain(Said));
                Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out _),  Is.False);
                Assert.That(System.IO.Directory.Exists(Path.Combine(RoamingHub.POIDirectory, "QA")),          Is.False);
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

        #region TheOperatorsAndTheirPoolsComeAsChangeSetsTheirOwnersSign()

        /// <summary>
        /// Submitted by somebody not signed in at all: the RNO adds a charging station operator with its keys, the
        /// operator adds a charging pool with a station and two EVSEs - and the same pool signed by the RNO is
        /// refused, as it is not the RNO's.
        /// </summary>
        [Test]
        public async Task TheOperatorsAndTheirPoolsComeAsChangeSetsTheirOwnersSign()
        {

            using (var admin = await SignedIn())
                Assert.That((await Post(admin, QA())).StatusCode, Is.EqualTo(HttpStatusCode.Created));

            using var anybody    = Anonymous();

            var qac              = new ChargingStationOperator(
                                       ChargingStationOperator_Id.Parse("DE*QAC"),
                                       Name:          I18NString.Create(Languages.en, "QA Charging"),
                                       SigningKeys:   [ POIPublicKey.From(csoSigningKey)  ],
                                       IdentityKeys:  [ POIPublicKey.From(csoIdentityKey) ]
                                   );

            var addOperator      = await Commit(anybody, Head().CreateChangeSet([ RoamingNetworkChange.Add(qac) ]).
                                                                 Sign(rnoSigningKey, POIPublicKey.From(rnoSigningKey).Id, COSEAlgorithm.ES256));

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

            var poolByTheRNO     = await Commit(anybody, Head().CreateChangeSet([ RoamingNetworkChange.Add(pool) ]).
                                                                 Sign(rnoSigningKey, POIPublicKey.From(rnoSigningKey).Id, COSEAlgorithm.ES256));

            var addPool          = await Commit(anybody, Head().CreateChangeSet([ RoamingNetworkChange.Add(pool) ]).
                                                                 Sign(csoSigningKey, POIPublicKey.From(csoSigningKey).Id, COSEAlgorithm.ES256));

            var served           = JObject.Parse(await anybody.GetStringAsync("poi/RNs/QA/CSOs/DE*QAC/CPs/DE*QAC*P1"));

            Assert.Multiple(() => {

                Assert.That(addOperator. StatusCode,  Is.EqualTo(HttpStatusCode.Created),              addOperator.Text);
                Assert.That(poolByTheRNO.StatusCode,  Is.EqualTo(HttpStatusCode.UnprocessableEntity),  poolByTheRNO.Text);
                Assert.That(poolByTheRNO.Text,        Does.Contain("needs a signature by a signing key of the ChargingStationOperator 'DE*QAC'"));
                Assert.That(addPool.     StatusCode,  Is.EqualTo(HttpStatusCode.Created),              addPool.Text);

                Assert.That(served["chargingStations"]?[0]?["EVSEs"]?.Count(),  Is.EqualTo(2));
                Assert.That(RoamingHub.Log.Recent(100, Tag: "commit").Select(entry => entry.Message),
                            Has.Some.Contains("The roaming network 'QA' is at revision 2"));

            });

        }

        #endregion

        #region AMadeRoamingNetworkIsThereAfterARestart()

        /// <summary>
        /// Read back from its directory at the next start, with what was committed to it.
        /// </summary>
        [Test]
        public async Task AMadeRoamingNetworkIsThereAfterARestart()
        {

            using (var admin = await SignedIn())
                Assert.That((await Post(admin, QA())).StatusCode, Is.EqualTo(HttpStatusCode.Created));

            var qac = new ChargingStationOperator(
                          ChargingStationOperator_Id.Parse("DE*QAC"),
                          SigningKeys:   [ POIPublicKey.From(csoSigningKey)  ],
                          IdentityKeys:  [ POIPublicKey.From(csoIdentityKey) ]
                      );

            using (var anybody = Anonymous())
                Assert.That((await Commit(anybody, Head().CreateChangeSet([ RoamingNetworkChange.Add(qac) ]).
                                                          Sign(rnoSigningKey, POIPublicKey.From(rnoSigningKey).Id, COSEAlgorithm.ES256))).StatusCode,
                            Is.EqualTo(HttpStatusCode.Created));

            await RoamingHub.DisposeAsync();

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {

                Assert.That(again.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out var history), Is.True);

                Assert.Multiple(() => {
                    Assert.That(history!.Head.Commit.Revision,                                                                         Is.EqualTo(1));
                    Assert.That(history.Head.RoamingNetwork.TryGetChargingStationOperatorById(ChargingStationOperator_Id.Parse("DE*QAC"), out _),  Is.True);
                });

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

    }

}
