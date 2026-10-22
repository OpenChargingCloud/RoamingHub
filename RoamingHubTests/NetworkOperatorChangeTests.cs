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

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

// Those of POI 3, not those of WWCP_Core the OCPI side knows.
using RoamingNetwork           = cloud.charging.open.protocols.WWCP.POI.RoamingNetwork;
using ChargingStationOperator  = cloud.charging.open.protocols.WWCP.POI.ChargingStationOperator;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The roaming network operator this hub is, changed in every roaming network it operates: its signing key or its
    /// identity key replaced by a new one, each change signed by the key it replaces, the old key forgotten once no
    /// roaming network names it; and its name.
    /// </summary>
    public class NetworkOperatorChangeTests : ARoamingHubTests
    {

        #region (helper) Made(Hub, Ids)

        private static void Made(RoamingHub Hub, params String[] Ids)
        {
            foreach (var id in Ids)
                Assert.That(Hub.CreateRoamingNetwork(RoamingNetwork_Id.Parse(id)).Success, Is.True, $"'{id}' was not made");
        }

        #endregion

        #region (helper) Party(Id)

        /// <summary>
        /// A charging station operator with public keys of its own.
        /// </summary>
        private static JObject Party(String Id)

            => new (
                   new JProperty("@id",           Id),
                   new JProperty("signingKeys",   new JArray(POIPublicKey.From(POITests.NewKey(COSECurve.P256)).ToJSON())),
                   new JProperty("identityKeys",  new JArray(POIPublicKey.From(POITests.NewKey(COSECurve.P521)).ToJSON()))
               );

        #endregion

        #region (helper) HistoryOf(Hub, Id)

        private static RoamingNetworkHistory HistoryOf(RoamingHub Hub, String Id)
        {

            Assert.That(Hub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse(Id), out var history), Is.True, $"'{Id}' is not served");

            return history!;

        }

        #endregion

        #region (helper) KeyDirectory / KeyFiles(Prefix) / Stored()

        private String KeyDirectory
            => Path.Combine(Directory, HubRoamingNetworkOperator.DirectoryName);

        private String[] KeyFiles(String Prefix)
            => [ .. System.IO.Directory.GetFiles(KeyDirectory, Prefix + "*.pem").Select(Path.GetFileName).OfType<String>().Order(StringComparer.Ordinal) ];

        /// <summary>
        /// The operator as its directory has it, read again.
        /// </summary>
        private HubRoamingNetworkOperator Stored()
            => HubRoamingNetworkOperator.OpenOrCreate(KeyDirectory);

        #endregion


        #region TheSigningKeyIsReplacedInEveryRoamingNetwork()

        /// <summary>
        /// Each roaming network gets the new signing key by a change set the old one signs, and its commits are
        /// signed by the new one from then on; the old one is forgotten, as no roaming network names it any more.
        /// </summary>
        [Test]
        public async Task TheSigningKeyIsReplacedInEveryRoamingNetwork()
        {

            Made(RoamingHub, "QA", "PROD");

            Assert.That(POITests.AddedParty(RoamingHub, "QA", "DE*QAC").Success, Is.True);

            var old       = RoamingHub.NetworkOperator.SigningKey;
            var result    = RoamingHub.RotateNetworkOperatorKey(POIKeyKind.Signing);
            var current   = RoamingHub.NetworkOperator.SigningKey;

            var qa        = HistoryOf(RoamingHub, "QA");
            var prod      = HistoryOf(RoamingHub, "PROD");
            var rotation  = qa.Head.Commit;

            var added     = POITests.AddedParty(RoamingHub, "QA", "DE*QAD");

            using var anybody = Anonymous();

            var served    = JObject.Parse(await anybody.GetStringAsync("api/v1/poi/roamingNetworkOperator"));

            Assert.Multiple(() => {

                Assert.That(result.Success,                                                           Is.True, result.Message);
                Assert.That(result.Changed.Select(id => id.ToString()),                              Is.EqualTo(new[] { "PROD", "QA" }));
                Assert.That(current.Id,                                                               Is.Not.EqualTo(old.Id));
                Assert.That(current.Key.Curve,                                                        Is.EqualTo(COSECurve.P256));

                foreach (var history in new[] { qa, prod })
                    Assert.That(history.Head.RoamingNetwork.Operator!.SigningKeys.Select(key => key.Id),  Is.EqualTo(new[] { current.Id }), history.RoamingNetworkId.ToString());

                // Certified anew, and its heads attested by it.
                foreach (var history in new[] { qa, prod })
                {
                    Assert.That(history.Head.RoamingNetwork.Operator!.SigningKeyCertificates.Select(certificate => certificate.SigningKey.Id),  Is.EqualTo(new[] { current.Id }), history.RoamingNetworkId.ToString());
                    Assert.That(history.CurrentAttestations().Select(attestation => attestation.KeyId),                Does.Contain(current.Id),           history.RoamingNetworkId.ToString());
                }


                Assert.That(prod.Head.Commit.Revision,                                                Is.EqualTo(1));
                Assert.That(rotation.Revision,                                                        Is.EqualTo(2));

                // The change set is signed by the key it replaces; so is the commit, made before the new key was named.
                Assert.That(rotation.ChangeSet!.Signatures.Select(signature => signature.KeyId),      Is.EqualTo(new[] { old.Id }));
                Assert.That(rotation.Signatures.Single().KeyId,                                       Is.EqualTo(old.Id));

                // From then on, the new one signs.
                Assert.That(added.Success,                                                            Is.True, added.Message);
                // Its change set signed by the new signing key, then by the identity key of the party, as its proof.
                Assert.That(added.Commit!.ChangeSet!.Signatures.First().KeyId,                        Is.EqualTo(current.Id));
                Assert.That(added.Commit.ChangeSet.Signatures,                                        Has.Length.EqualTo(2));
                Assert.That(added.Commit.Signatures.Single().KeyId,                                   Is.EqualTo(current.Id));

                // Kept as the current one, and the old one forgotten.
                Assert.That(KeyFiles("signing-key"),                                                  Is.EqualTo(new[] { HubRoamingNetworkOperator.SigningKeyFileName }));
                Assert.That(Stored().SigningKey.Id,                                                   Is.EqualTo(current.Id));
                Assert.That(RoamingHub.NetworkOperator.KeptKeyIds,                                    Is.Empty);
                Assert.That(result.Message,                                                           Does.Contain($"The old key {old.Id[..16]}… is forgotten"));

                Assert.That(served["signingKeyCertificates"]?[0]?["signingKey"]?["@id"]?.Value<String>(),  Is.EqualTo(current.Id));
                Assert.That(RoamingHub.Log.Recent(100, Tag: "security").Select(entry => entry.Message),
                            Has.Some.Contains($"The signing key of the roaming network operator '{RoamingHub.NetworkOperator.Id}' is {current.Id[..16]}… now"));

            });

        }

        #endregion

        #region TheIdentityKeyIsReplacedInEveryRoamingNetwork()

        /// <summary>
        /// By a change set the old identity key signs; the signing key and the identification stay.
        /// </summary>
        [Test]
        public void TheIdentityKeyIsReplacedInEveryRoamingNetwork()
        {

            Made(RoamingHub, "QA");

            var id          = RoamingHub.NetworkOperator.Id;
            var old         = RoamingHub.NetworkOperator.IdentityKey;
            var signingKey  = RoamingHub.NetworkOperator.SigningKey;

            var result      = RoamingHub.RotateNetworkOperatorKey(POIKeyKind.Identity);
            var current     = RoamingHub.NetworkOperator.IdentityKey;
            var rotation    = HistoryOf(RoamingHub, "QA").Head.Commit;

            Assert.Multiple(() => {

                Assert.That(result.Success,                                                           Is.True, result.Message);
                Assert.That(current.Id,                                                               Is.Not.EqualTo(old.Id));
                Assert.That(current.Key.Curve,                                                        Is.EqualTo(COSECurve.P521));
                Assert.That(HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.Operator!.IdentityKeys.Select(key => key.Id),  Is.EqualTo(new[] { current.Id }));

                // Signed by the identity key it replaces, by the signing key for the key statement made anew, which the
                // new identity key signs, and by the new identity key, as the proof that the hub holds it.
                Assert.That(rotation.ChangeSet!.Signatures.Select(signature => signature.KeyId),      Is.EqualTo(new[] { old.Id, signingKey.Id, current.Id }));
                Assert.That(HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.Operator!.KeyStatement?.Signatures.Single().KeyId,  Is.EqualTo(current.Id));
                Assert.That(HistoryOf(RoamingHub, "QA").Head.RoamingNetwork.Operator!.KeyStatement?.IdentityKeys.Select(key => key.Id),  Is.EqualTo(new[] { current.Id }));


                Assert.That(RoamingHub.NetworkOperator.Id,                                            Is.EqualTo(id));
                Assert.That(RoamingHub.NetworkOperator.SigningKey.Id,                                 Is.EqualTo(signingKey.Id));
                Assert.That(KeyFiles("identity-key"),                                                 Is.EqualTo(new[] { HubRoamingNetworkOperator.IdentityKeyFileName }));
                Assert.That(Stored().IdentityKey.Id,                                                  Is.EqualTo(current.Id));

            });

        }

        #endregion

        #region TheKeysAreStatedAnewBeforeTheirStatementEnds()

        /// <summary>
        /// A roaming network made here states the keys of its operator from its genesis on; a week before the statement
        /// ends, it is stated anew by a commit, and the head is attested again once its attestation is older than its
        /// interval.
        /// </summary>
        [Test]
        public void TheKeysAreStatedAnewBeforeTheirStatementEnds()
        {

            Made(RoamingHub, "QA");

            var history   = HistoryOf(RoamingHub, "QA");
            var first     = history.Head.RoamingNetwork.Operator!.KeyStatement;
            var revision  = history.Head.Commit.Revision;

            Assert.That(first, Is.Not.Null, "no key statement in the genesis");

            var soon      = first!.SignedAt.AddDays(1);
            var late      = first.ValidUntil - RoamingHub.KeyStatementRenewal + TimeSpan.FromMinutes(1);

            var atFirst   = RoamingHub.KeepAttesting(soon);
            var atLast    = RoamingHub.KeepAttesting(late);
            var second    = history.Head.RoamingNetwork.Operator!.KeyStatement!;

            Assert.Multiple(() => {

                Assert.That(revision,                                                                Is.Zero);
                Assert.That(first.ValidUntil - first.SignedAt,                                       Is.EqualTo(RoamingHub.KeyStatementValidity));
                Assert.That(first.Signatures.Single().KeyId,                                         Is.EqualTo(RoamingHub.NetworkOperator.IdentityKey.Id));

                Assert.That(atFirst,                                                                 Is.Empty);
                Assert.That(atLast.Select(id => id.ToString()),                                      Is.EqualTo(new[] { "QA" }));
                Assert.That(history.Head.Commit.Revision,                                            Is.EqualTo(1));
                Assert.That(second.ValidUntil,                                                       Is.GreaterThan(first.ValidUntil));

                // Attested again at that time, by the signing key it states.
                Assert.That(history.CurrentAttestations(late).Select(attestation => attestation.KeyId),
                            Has.Some.EqualTo(RoamingHub.NetworkOperator.SigningKey.Id));

            });

        }

        #endregion

        #region TheNewKeysAreThoseOfTheHubAfterARestart()

        [Test]
        public async Task TheNewKeysAreThoseOfTheHubAfterARestart()
        {

            Made(RoamingHub, "QA");

            Assert.That(RoamingHub.RotateNetworkOperatorKey(POIKeyKind.Signing). Success, Is.True);
            Assert.That(RoamingHub.RotateNetworkOperatorKey(POIKeyKind.Identity).Success, Is.True);

            var signingKey   = RoamingHub.NetworkOperator.SigningKey.Id;
            var identityKey  = RoamingHub.NetworkOperator.IdentityKey.Id;

            await RoamingHub.DisposeAsync();

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {

                var added = POITests.AddedParty(again, "QA", "DE*QAC");

                Assert.Multiple(() => {
                    Assert.That(again.NetworkOperator.SigningKey. Id,                                 Is.EqualTo(signingKey));
                    Assert.That(again.NetworkOperator.IdentityKey.Id,                                 Is.EqualTo(identityKey));
                    Assert.That(again.NetworkOperator.KeptKeyIds,                                     Is.Empty);
                    Assert.That(added.Success,                                                        Is.True, added.Message);
                    Assert.That(added.Commit!.Signatures.Single().KeyId,                              Is.EqualTo(signingKey));
                });

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

        #region AnOldKeyIsKeptWhileARoamingNetworkMayNameIt()

        /// <summary>
        /// A directory below "poi" that could not be read at the start may hold a roaming network naming the old key:
        /// it is kept beside the new one, after a restart as well, and signs for whoever still names it.
        /// </summary>
        [Test]
        public async Task AnOldKeyIsKeptWhileARoamingNetworkMayNameIt()
        {

            Made(RoamingHub, "QA");

            await RoamingHub.DisposeAsync();

            var broken = Path.Combine(Directory, RoamingHub.POIDirectoryName, "BROKEN");
            System.IO.Directory.CreateDirectory(broken);
            await File.WriteAllTextAsync(Path.Combine(broken, "CURRENT"), "no generation of a history");

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            String old;
            String current;

            try
            {

                old          = again.NetworkOperator.SigningKey.Id;
                var result   = again.RotateNetworkOperatorKey(POIKeyKind.Signing);
                current      = again.NetworkOperator.SigningKey.Id;

                Assert.Multiple(() => {
                    Assert.That(result.Success,                                                       Is.True, result.Message);
                    Assert.That(result.Message,                                                       Does.Contain($"The old key {old[..16]}… is kept: 1 directory(s) below 'poi' could not be read"));
                    Assert.That(again.NetworkOperator.KeptKeyIds,                                     Is.EqualTo(new[] { old }));
                    Assert.That(KeyFiles("signing-key"),                                              Is.EqualTo(new[] { $"signing-key-{old[..16]}.pem", HubRoamingNetworkOperator.SigningKeyFileName }));
                    Assert.That(HistoryOf(again, "QA").Head.RoamingNetwork.Operator!.SigningKeys.Single().Id,  Is.EqualTo(current));
                });

            }
            finally
            {
                await again.DisposeAsync();
            }

            var third = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {
                Assert.Multiple(() => {
                    Assert.That(third.NetworkOperator.SigningKey.Id,                                  Is.EqualTo(current));
                    Assert.That(third.NetworkOperator.KeptKeyIds,                                     Is.EqualTo(new[] { old }));
                    Assert.That(third.NetworkOperator.Operates(HistoryOf(third, "QA").Head.RoamingNetwork),  Is.True);
                });
            }
            finally
            {
                await third.DisposeAsync();
            }

        }

        #endregion

        #region TheOperatorIsRenamedInEveryRoamingNetwork()

        /// <summary>
        /// In every roaming network it operates, in operator.json, and so in every roaming network made afterwards;
        /// a network that has the name already gets no commit.
        /// </summary>
        [Test]
        public void TheOperatorIsRenamedInEveryRoamingNetwork()
        {

            Made(RoamingHub, "QA", "PROD");

            var result     = RoamingHub.RenameNetworkOperator(I18NString.Create(Languages.en, "GraphDefined GmbH"));
            var revision   = HistoryOf(RoamingHub, "QA").Head.Commit.Revision;
            var again      = RoamingHub.RenameNetworkOperator(I18NString.Create(Languages.en, "GraphDefined GmbH"));

            Made(RoamingHub, "DEV");

            Assert.Multiple(() => {

                Assert.That(result.Success,                                                           Is.True, result.Message);
                Assert.That(result.Changed.Select(id => id.ToString()),                              Is.EqualTo(new[] { "PROD", "QA" }));

                foreach (var id in new[] { "QA", "PROD", "DEV" })
                    Assert.That(HistoryOf(RoamingHub, id).Head.RoamingNetwork.Operator!.Name.FirstText(),  Is.EqualTo("GraphDefined GmbH"), id);

                Assert.That(revision,                                                                 Is.EqualTo(1));
                Assert.That(again.Success,                                                            Is.True);
                Assert.That(HistoryOf(RoamingHub, "QA").Head.Commit.Revision,                         Is.EqualTo(1));

                Assert.That(Stored().Operator.Name.FirstText(),                                       Is.EqualTo("GraphDefined GmbH"));
                Assert.That(() => RoamingHub.RenameNetworkOperator(I18NString.Empty),                 Throws.ArgumentException);

            });

        }

        #endregion

        #region OverHTTPOnlyWhoMayEditThePointsOfInterestChangesIt()

        [Test]
        public async Task OverHTTPOnlyWhoMayEditThePointsOfInterestChangesIt()
        {

            Made(RoamingHub, "QA");

            var old            = RoamingHub.NetworkOperator.SigningKey.Id;

            using var anybody  = Anonymous();
            using var admin    = await SignedIn();

            var refused        = await anybody.PostAsync("api/v1/poi/roamingNetworkOperator/signingKey/rotate", null);
            var unchanged      = RoamingHub.NetworkOperator.SigningKey.Id;

            var rotated        = await admin.  PostAsync("api/v1/poi/roamingNetworkOperator/signingKey/rotate", null);
            var rotatedJSON    = JObject.Parse(await rotated.Content.ReadAsStringAsync());

            var renamed        = await admin.  PutAsync ("api/v1/poi/roamingNetworkOperator/name",
                                                         new StringContent("""{ "name": "GraphDefined GmbH" }""", Encoding.UTF8, "application/json"));
            var renamedJSON    = JObject.Parse(await renamed.Content.ReadAsStringAsync());

            var nameless       = await admin.  PutAsync ("api/v1/poi/roamingNetworkOperator/name",
                                                         new StringContent("""{ "name": "" }""", Encoding.UTF8, "application/json"));

            Assert.Multiple(() => {

                Assert.That(refused.StatusCode,                                                       Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
                Assert.That(unchanged,                                                                Is.EqualTo(old));

                Assert.That(rotated.StatusCode,                                                       Is.EqualTo(HttpStatusCode.OK), rotatedJSON.ToString());
                Assert.That(rotatedJSON["changed"]?.Values<String>(),                                 Is.EqualTo(new[] { "QA" }));
                Assert.That(rotatedJSON[RoamingNetwork.PropertyKeys.Operator]?["signingKeyCertificates"]?[0]?["signingKey"]?["@id"]?.Value<String>(),  Is.EqualTo(RoamingHub.NetworkOperator.SigningKey.Id));
                Assert.That(RoamingHub.NetworkOperator.SigningKey.Id,                                 Is.Not.EqualTo(old));

                Assert.That(renamed.StatusCode,                                                       Is.EqualTo(HttpStatusCode.OK), renamedJSON.ToString());
                Assert.That(renamedJSON[RoamingNetwork.PropertyKeys.Operator]?["name"]?["en"]?.Value<String>(),   Is.EqualTo("GraphDefined GmbH"));

                Assert.That(nameless.StatusCode,                                                      Is.EqualTo(HttpStatusCode.BadRequest));

            });

        }

        #endregion

    }

}
