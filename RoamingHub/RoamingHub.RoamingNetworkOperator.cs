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

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using Newtonsoft.Json.Linq;

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The roaming network operator a hub is: made at its first start, with a signing key and an identity key
    /// of its own, and read back at every start after it. It operates every roaming network made here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Below the directory of the configuration file, in "roamingNetworkOperator": "operator.json", the operator
    /// as every roaming network it operates embeds it, with the public halves of its keys; "signing-key.pem",
    /// the P-256 key signing its change sets and the commits of its roaming networks; "identity-key.pem", the
    /// P-521 key changing its keys and, for recovery, those of the other parties. The private keys never leave
    /// this directory, and only the account the hub runs as may read them.
    /// </para>
    /// <para>
    /// Who it is is said once, at the first start, by the section "roamingNetworkOperator" of the configuration
    /// file, {"@id", "name"}; without it, it is "RNO-" and the first eight hex digits of its identity key. What it
    /// says later is not read: every roaming network it operates names it already.
    /// </para>
    /// </remarks>
    public sealed class HubRoamingNetworkOperator
    {

        #region Data

        /// <summary>
        /// The directory of the operator, below the directory of the configuration file.
        /// </summary>
        public const String  DirectoryName          = "roamingNetworkOperator";

        /// <summary>
        /// The operator, as its roaming networks embed it.
        /// </summary>
        public const String  OperatorFileName       = "operator.json";

        /// <summary>
        /// The private P-256 key signing its change sets and commits.
        /// </summary>
        public const String  SigningKeyFileName     = "signing-key.pem";

        /// <summary>
        /// The private P-521 key changing keys.
        /// </summary>
        public const String  IdentityKeyFileName    = "identity-key.pem";

        /// <summary>
        /// The section of the configuration file that says who it is, at the first start.
        /// </summary>
        public const String  ConfigurationSection   = "roamingNetworkOperator";

        private readonly ECPrivateKeyParameters  signingKey;
        private readonly ECPrivateKeyParameters  identityKey;

        #endregion

        #region Properties

        /// <summary>
        /// The operator, as every roaming network it operates embeds it: with the public halves of its keys.
        /// </summary>
        public RoamingNetworkOperator  Operator       { get; }

        /// <summary>
        /// Where it is kept.
        /// </summary>
        public String                  Directory      { get; }

        /// <summary>
        /// Whether it was made now, at this start, rather than read.
        /// </summary>
        public Boolean                 IsNew          { get; }

        /// <summary>
        /// The public half of its signing key.
        /// </summary>
        public POIPublicKey            SigningKey     { get; }

        /// <summary>
        /// The public half of its identity key.
        /// </summary>
        public POIPublicKey            IdentityKey    { get; }

        /// <summary>
        /// Its identification.
        /// </summary>
        public RoamingNetworkOperator_Id  Id
            => Operator.Id;

        #endregion

        #region Constructor(s)

        private HubRoamingNetworkOperator(RoamingNetworkOperator  Operator,
                                          String                  Directory,
                                          Boolean                 IsNew,
                                          ECPrivateKeyParameters  SigningKey,
                                          ECPrivateKeyParameters  IdentityKey)
        {

            this.Operator     = Operator;
            this.Directory    = Directory;
            this.IsNew        = IsNew;
            this.signingKey   = SigningKey;
            this.identityKey  = IdentityKey;
            this.SigningKey   = POIPublicKey.From(SigningKey);
            this.IdentityKey  = POIPublicKey.From(IdentityKey);

        }

        #endregion


        #region (static) OpenOrCreate(Directory, Configuration = null, Now = null)

        /// <summary>
        /// The operator kept in the given directory, or, at the first start, a new one with new keys, kept there
        /// from now on.
        /// </summary>
        /// <param name="Directory">Where it is kept.</param>
        /// <param name="Configuration">What the configuration file says of it, read only when it is made.</param>
        /// <param name="Now">When it is made.</param>
        /// <exception cref="InvalidOperationException">What is kept is incomplete or does not fit together.</exception>
        public static HubRoamingNetworkOperator OpenOrCreate(String           Directory,
                                                             JObject?         Configuration   = null,
                                                             DateTimeOffset?  Now             = null)
        {

            var operatorPath     = Path.Combine(Directory, OperatorFileName);
            var signingKeyPath   = Path.Combine(Directory, SigningKeyFileName);
            var identityKeyPath  = Path.Combine(Directory, IdentityKeyFileName);

            #region Read back

            if (File.Exists(operatorPath))
            {

                // Without its private keys, it can sign nothing any more; made again, it would be somebody else
                // than the one its roaming networks name.
                foreach (var keyPath in new[] { signingKeyPath, identityKeyPath })
                    if (!File.Exists(keyPath))
                        throw new InvalidOperationException($"'{keyPath}' is missing: this hub can not sign as the roaming network operator of '{operatorPath}' without it. " +
                                                             "Restore it from a copy; a new key would be none its roaming networks know.");

                if (!RoamingNetworkOperator.TryParse(JObject.Parse(File.ReadAllText(operatorPath)), out var stored, out var error))
                    throw new InvalidOperationException($"'{operatorPath}' is no roaming network operator: {error}");

                var storedSigningKey   = ReadKey(signingKeyPath);
                var storedIdentityKey  = ReadKey(identityKeyPath);

                if (!stored.SigningKeys. Any(key => key.Id == POIPublicKey.From(storedSigningKey). Id) ||
                    !stored.IdentityKeys.Any(key => key.Id == POIPublicKey.From(storedIdentityKey).Id))
                {
                    throw new InvalidOperationException($"The keys in '{Directory}' are not the keys of the roaming network operator '{stored.Id}' its '{OperatorFileName}' names.");
                }

                return new HubRoamingNetworkOperator(stored, Directory, false, storedSigningKey, storedIdentityKey);

            }

            #endregion

            #region The first start: made now

            System.IO.Directory.CreateDirectory(Directory);

            // Keys left by a first start that ended before it wrote the operator are taken; none are replaced.
            var newSigningKey   = File.Exists(signingKeyPath)  ? ReadKey(signingKeyPath)  : WriteKey(signingKeyPath,  NewKey(COSECurve.P256));
            var newIdentityKey  = File.Exists(identityKeyPath) ? ReadKey(identityKeyPath) : WriteKey(identityKeyPath, NewKey(COSECurve.P521));
            var identityKey     = POIPublicKey.From(newIdentityKey);

            var id              = Configuration?["@id"] is JValue { Type: JTokenType.String } configuredId &&
                                  RoamingNetworkOperator_Id.TryParse(configuredId.Value<String>()!, out var parsedId)
                                      ? parsedId
                                      : RoamingNetworkOperator_Id.Parse("RNO-" + identityKey.Id[..8].ToUpperInvariant());

            var name            = Configuration?["name"] switch {
                                      JValue  { Type: JTokenType.String } text  => I18NString.Create(Languages.en, text.Value<String>()!),
                                      JObject                             texts => I18NString.Parse(texts),
                                      _                                         => null
                                  };

            var made            = new RoamingNetworkOperator(id,
                                                             name,
                                                             SigningKeys:   [ POIPublicKey.From(newSigningKey) ],
                                                             IdentityKeys:  [ identityKey ],
                                                             Created:       Now ?? Timestamp.Now);

            // Written last: once it is there, the operator is complete.
            File.WriteAllText(operatorPath, made.ToJSON().ToString());

            return new HubRoamingNetworkOperator(made, Directory, true, newSigningKey, newIdentityKey);

            #endregion

        }

        #endregion

        #region Operates(RoamingNetwork)

        /// <summary>
        /// Whether this is the operator of the given roaming network: the one it names, with the signing key kept here.
        /// </summary>
        public Boolean Operates(RoamingNetwork RoamingNetwork)

            => RoamingNetwork.Operator is RoamingNetworkOperator networkOperator &&
               networkOperator.Id == Operator.Id &&
               networkOperator.SigningKeys.Any(key => key.Id == SigningKey.Id);

        #endregion

        #region Sign(ChangeSet)

        /// <summary>
        /// The given change set, signed with the signing key of this operator.
        /// </summary>
        public RoamingNetworkChangeSet Sign(RoamingNetworkChangeSet ChangeSet)

            => ChangeSet.Sign(signingKey, SigningKey.Id, COSEAlgorithm.ES256);

        #endregion

        #region CommitSigner()

        /// <summary>
        /// What signs every commit this hub makes of a roaming network this operator operates: its signing key,
        /// in the role of the roaming network operator.
        /// </summary>
        public RoamingNetworkCommitSigner CommitSigner()

            => new (signingKey,
                    SigningKey.Id,
                    COSEAlgorithm.ES256,
                    RoamingNetworkCommitSignatureRoles.RoamingNetworkOperator,
                    Operator.Id.ToString());

        #endregion


        #region (private static) NewKey(Curve)

        private static ECPrivateKeyParameters NewKey(COSECurve Curve)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(Curve.DomainParameters!, new SecureRandom()));
            return (ECPrivateKeyParameters) generator.GenerateKeyPair().Private;
        }

        #endregion

        #region (private static) WriteKey(Path, Key) / ReadKey(Path)

        /// <summary>
        /// Write the given private key as PKCS #8 PEM, readable only by its owner.
        /// </summary>
        private static ECPrivateKeyParameters WriteKey(String                  Path,
                                                       ECPrivateKeyParameters  Key)
        {

            File.WriteAllText(Path, new String(PemEncoding.Write("PRIVATE KEY", PrivateKeyInfoFactory.CreatePrivateKeyInfo(Key).GetEncoded())) + "\n");

            // A private key that the whole machine may read is one that the whole machine may sign with.
            // Unix only: Windows inherits the directory's ACL.
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // A file system with no notion of modes: worth trying, not worth failing a start over.
                }
            }

            return Key;

        }

        /// <summary>
        /// Read a private key written by WriteKey().
        /// </summary>
        private static ECPrivateKeyParameters ReadKey(String Path)
        {

            var text = File.ReadAllText(Path);

            if (!PemEncoding.TryFind(text, out var fields))
                throw new InvalidOperationException($"'{Path}' holds no PEM.");

            return PrivateKeyFactory.CreateKey(Convert.FromBase64String(text[fields.Base64Data])) as ECPrivateKeyParameters
                       ?? throw new InvalidOperationException($"'{Path}' holds no elliptic curve private key.");

        }

        #endregion


        #region (override) ToString()

        public override String ToString()

            => $"the roaming network operator '{Operator.Id}', signing key {SigningKey.Id[..16]}…, identity key {IdentityKey.Id[..16]}…";

        #endregion

    }


    /// <summary>
    /// What became of a party to be added to a roaming network.
    /// </summary>
    public enum RoamingNetworkPartyStatus
    {

        /// <summary>
        /// Added: committed, signed by this hub as the roaming network operator.
        /// </summary>
        Added,

        /// <summary>
        /// Not added: what was asked for is no party this hub adds.
        /// </summary>
        Invalid,

        /// <summary>
        /// Not added: this hub serves no roaming network of that identification.
        /// </summary>
        UnknownRoamingNetwork,

        /// <summary>
        /// Not added: the roaming network is operated by another roaming network operator, who adds its parties.
        /// </summary>
        NotOperatedHere,

        /// <summary>
        /// Not added: the roaming network has a party of that type and identification already.
        /// </summary>
        Exists,

        /// <summary>
        /// Not added: the history of the roaming network refused the commit.
        /// </summary>
        Refused

    }

    /// <summary>
    /// What became of a party to be added, why, and the commit that added it.
    /// </summary>
    /// <param name="Status">What became of it.</param>
    /// <param name="Message">What to tell whoever asked for it.</param>
    /// <param name="Commit">The commit that added it.</param>
    public sealed record RoamingNetworkPartyAddition(RoamingNetworkPartyStatus  Status,
                                                     String                     Message,
                                                     RoamingNetworkCommit?      Commit   = null)
    {

        /// <summary>
        /// Whether it was added.
        /// </summary>
        public Boolean Success
            => Status == RoamingNetworkPartyStatus.Added;

    }


    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// The parties this hub adds to the roaming networks it operates: their entity type, by the name of their list
        /// in a roaming network, which is the last segment of the path they are added at as well.
        /// </summary>
        public static readonly IReadOnlyDictionary<String, String> PartyTypes = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase) {
            { "chargingStationOperators",  nameof(ChargingStationOperator) },
            { "eMobilityProviders",        nameof(EMobilityProvider)       },
            { "gridOperators",             nameof(GridOperator)            },
            { "parkingOperators",          nameof(ParkingOperator)         }
        };

        #endregion

        #region Properties

        /// <summary>
        /// The roaming network operator this hub is, with its keys: the operator of every roaming network made here.
        /// </summary>
        public HubRoamingNetworkOperator  NetworkOperator    { get; private set; } = default!;

        #endregion


        #region (private) BuildNetworkOperator()

        /// <summary>
        /// The roaming network operator this hub is: read back, or made at the first start.
        /// </summary>
        private void BuildNetworkOperator()
        {

            var directory = Path.Combine(Path.GetDirectoryName(ConfigFile.Path) ?? ".", HubRoamingNetworkOperator.DirectoryName);

            NetworkOperator = HubRoamingNetworkOperator.OpenOrCreate(directory,
                                                                     ConfigurationDocument[HubRoamingNetworkOperator.ConfigurationSection] as JObject);

            if (NetworkOperator.IsNew)
                Log.Notice($"This hub is the roaming network operator '{NetworkOperator.Id}' now: its signing key {NetworkOperator.SigningKey.Id[..16]}… " +
                           $"and its identity key {NetworkOperator.IdentityKey.Id[..16]}… were made in '{directory}'. Keep a copy of them where nobody else " +
                            "can read it: without its identity key, the keys of the roaming networks it operates can never be replaced.",
                           "poi", "security");

            else
                Log.Info($"This hub is the roaming network operator '{NetworkOperator.Id}', its keys in '{directory}'.", "poi");

        }

        #endregion

        #region AddParty(RoamingNetworkId, EntityType, JSON)

        /// <summary>
        /// Add a party - a charging station operator, an e-mobility provider, a grid operator or a parking operator - to
        /// a roaming network this hub operates: a change set this hub signs as its roaming network operator, committed.
        /// </summary>
        /// <remarks>
        /// The party is given as POI 3 writes it, without anything it holds: {"@id", "name"?, ..., "signingKeys",
        /// "identityKeys"}, its keys the public ones, each as POI 3 writes it or the Base64 of its COSE key alone.
        /// A grid operator may come without keys: it does not take part, and this hub signs in its place.
        /// Its data is its own to add afterwards, with change sets it signs.
        /// </remarks>
        /// <param name="RoamingNetworkId">The roaming network.</param>
        /// <param name="EntityType">The entity type of the party, e.g. ChargingStationOperator.</param>
        /// <param name="JSON">The party.</param>
        public RoamingNetworkPartyAddition AddParty(RoamingNetwork_Id  RoamingNetworkId,
                                                    String             EntityType,
                                                    JObject            JSON)
        {

            #region What it must be

            if (!PartyTypes.Values.Contains(EntityType))
                return Invalid($"A party is one of {String.Join(", ", PartyTypes.Values)}, not '{EntityType}'.");

            if (!POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out var history))
                return new (RoamingNetworkPartyStatus.UnknownRoamingNetwork, $"This hub serves no roaming network '{RoamingNetworkId}'.");

            var network = history.Head.RoamingNetwork;

            if (!NetworkOperator.Operates(network))
                return new (RoamingNetworkPartyStatus.NotOperatedHere,
                            $"The roaming network '{RoamingNetworkId}' is operated by " +
                            (network.Operator is not null ? $"'{network.Operator.Id}'" : "nobody") +
                            $", not by this hub, '{NetworkOperator.Id}': its operator adds its parties.");

            if (JSON["@id"] is not JValue { Type: JTokenType.String } idJSON || String.IsNullOrWhiteSpace(idJSON.Value<String>()))
                return Invalid($"A {EntityType} needs an identification, '@id'.");

            var id        = idJSON.Value<String>()!.Trim();
            var party     = (JObject) JSON.DeepClone();
            party["@id"]  = id;

            #endregion

            #region Its keys: public ones only

            var withoutKeys = EntityType == nameof(GridOperator) &&
                              party[POIPartyKeys.SigningKeysProperty]  is null &&
                              party[POIPartyKeys.IdentityKeysProperty] is null;

            if (!withoutKeys)
            {

                if (!TryParsePublicKeys(party[POIPartyKeys.SigningKeysProperty],  "signing key",  out var signingKeys,  out var keysError) ||
                    !TryParsePublicKeys(party[POIPartyKeys.IdentityKeysProperty], "identity key", out var identityKeys, out keysError))
                {
                    return Invalid($"The {EntityType} '{id}' can not be added: {keysError}");
                }

                party[POIPartyKeys.SigningKeysProperty]   = new JArray(signingKeys. Select(key => key.ToJSON()));
                party[POIPartyKeys.IdentityKeysProperty]  = new JArray(identityKeys.Select(key => key.ToJSON()));

            }

            #endregion

            if (network.TryGetParty(new POIPartyId(EntityType, id), out _))
                return new (RoamingNetworkPartyStatus.Exists, $"The roaming network '{RoamingNetworkId}' has a {EntityType} '{id}' already.");

            RoamingNetworkChangeSet changeSet;

            try
            {
                changeSet = NetworkOperator.Sign(
                                network.CreateChangeSet(
                                    [ RoamingNetworkChange.Add(EntityType, id, party) ],
                                    Description: new ImmutableI18NString(I18NString.Create(Languages.en, $"The {EntityType} '{id}' added by its roaming network operator '{NetworkOperator.Id}'."))
                                )
                            );
            }
            catch (Exception e)
            {
                return Invalid($"The {EntityType} '{id}' can not be added: {e.Message}");
            }

            var result = history.Commit(changeSet);

            if (!result.IsCommitted)
                return new (RoamingNetworkPartyStatus.Refused, $"The {EntityType} '{id}' was not added: {result.ErrorResponse}");

            return new (RoamingNetworkPartyStatus.Added,
                        $"The {EntityType} '{id}' was added to the roaming network '{RoamingNetworkId}', at revision {result.Head.Commit.Revision}, commit {result.Head.Commit.Id}. " +
                        $"It adds its data now, with change sets it signs.",
                        result.Commit ?? result.Head.Commit);

            static RoamingNetworkPartyAddition Invalid(String Message)
                => new (RoamingNetworkPartyStatus.Invalid, Message);

        }

        #endregion

    }

}
