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
    /// <para>
    /// Its keys are replaced in every roaming network it operates, see <see cref="RoamingHub.RotateNetworkOperatorKey"/>.
    /// A key a roaming network still names after that - one the replacement did not reach - is kept beside the
    /// current one, as "signing-key-" or "identity-key-" and the first 16 hex digits of its identification, and
    /// signs for that roaming network until it names the new one as well: every private key of the directory is
    /// held, and a roaming network is signed for with the one it names. The current keys are those operator.json
    /// names.
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
        /// The current private P-256 key signing its change sets and commits.
        /// </summary>
        public const String  SigningKeyFileName     = "signing-key.pem";

        /// <summary>
        /// The current private P-521 key changing keys.
        /// </summary>
        public const String  IdentityKeyFileName    = "identity-key.pem";

        /// <summary>
        /// The section of the configuration file that says who it is, at the first start.
        /// </summary>
        public const String  ConfigurationSection   = "roamingNetworkOperator";

        private readonly Lock  keysLock = new ();

        /// <summary>
        /// Every private key held, by the identification of its public half, with the file it is kept in.
        /// </summary>
        private readonly Dictionary<String, (ECPrivateKeyParameters Key, String Path)>  signingKeys   = [];
        private readonly Dictionary<String, (ECPrivateKeyParameters Key, String Path)>  identityKeys  = [];

        private RoamingNetworkOperator networkOperator;

        #endregion

        #region Properties

        /// <summary>
        /// The operator, as every roaming network made from now on embeds it: with the public halves of its current keys.
        /// </summary>
        public RoamingNetworkOperator  Operator
        {
            get
            {
                lock (keysLock)
                    return networkOperator;
            }
        }

        /// <summary>
        /// Where it is kept.
        /// </summary>
        public String                  Directory      { get; }

        /// <summary>
        /// Whether it was made now, at this start, rather than read.
        /// </summary>
        public Boolean                 IsNew          { get; }

        /// <summary>
        /// The public half of its current signing key.
        /// </summary>
        public POIPublicKey            SigningKey
            => CurrentKey(POIKeyKind.Signing);

        /// <summary>
        /// The public half of its current identity key.
        /// </summary>
        public POIPublicKey            IdentityKey
            => CurrentKey(POIKeyKind.Identity);

        /// <summary>
        /// The identifications of the keys held besides the current ones: those a roaming network still names.
        /// </summary>
        public IReadOnlyList<String>   KeptKeyIds
        {
            get
            {
                lock (keysLock)
                {
                    var current = new[] { CurrentKeyId(POIKeyKind.Signing), CurrentKeyId(POIKeyKind.Identity) };
                    return [ .. signingKeys.Keys.Concat(identityKeys.Keys).Except(current).Order(StringComparer.Ordinal) ];
                }
            }
        }

        /// <summary>
        /// Its identification, which never changes.
        /// </summary>
        public RoamingNetworkOperator_Id  Id
            => Operator.Id;

        #endregion

        #region Constructor(s)

        private HubRoamingNetworkOperator(RoamingNetworkOperator  Operator,
                                          String                  Directory,
                                          Boolean                 IsNew)
        {

            this.networkOperator  = Operator;
            this.Directory        = Directory;
            this.IsNew            = IsNew;

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

                if (!RoamingNetworkOperator.TryParse(JObject.Parse(File.ReadAllText(operatorPath)), out var stored, out var error))
                    throw new InvalidOperationException($"'{operatorPath}' is no roaming network operator: {error}");

                var read = new HubRoamingNetworkOperator(stored, Directory, false);

                read.ReadKeys();

                // Without its private keys, it can sign nothing any more; made again, it would be somebody else
                // than the one its roaming networks name.
                foreach (var kind in new[] { POIKeyKind.Signing, POIKeyKind.Identity })
                    if (read.CurrentKeyIdOrNull(kind) is null)
                        throw new InvalidOperationException($"The {KindText(kind)} key of the roaming network operator '{stored.Id}' its '{OperatorFileName}' names is missing " +
                                                            $"from '{Directory}': this hub can not sign as that operator without it. Restore it from a copy; " +
                                                             "a new key would be none its roaming networks know.");

                return read;

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
            WriteOperator(operatorPath, made);

            var madeNow = new HubRoamingNetworkOperator(made, Directory, true);

            madeNow.signingKeys. Add(POIPublicKey.From(newSigningKey).Id, (newSigningKey,  signingKeyPath));
            madeNow.identityKeys.Add(identityKey.Id,                      (newIdentityKey, identityKeyPath));

            return madeNow;

            #endregion

        }

        #endregion

        #region Operates(RoamingNetwork)

        /// <summary>
        /// Whether this is the operator of the given roaming network: the one it names, with a signing key it names held here.
        /// </summary>
        public Boolean Operates(RoamingNetwork RoamingNetwork)

            => HeldKeyOf(RoamingNetwork, POIKeyKind.Signing) is not null;

        #endregion

        #region HeldKeyIdOf(RoamingNetwork, Kind)

        /// <summary>
        /// The identification of the key of the given kind held here that the operator of the given roaming network
        /// names, the current one where it names that; null where it names none, or is another operator.
        /// </summary>
        public String? HeldKeyIdOf(RoamingNetwork  RoamingNetwork,
                                   POIKeyKind      Kind)

            => HeldKeyOf(RoamingNetwork, Kind)?.KeyId;

        #endregion

        #region Sign(RoamingNetwork, ChangeSet, Kind = Signing)

        /// <summary>
        /// The given change set of the given roaming network, signed with the key of the given kind held here that the
        /// network names: the signing key for data, the identity key for changes of identity keys.
        /// </summary>
        /// <exception cref="InvalidOperationException">The roaming network names no such key held here.</exception>
        public RoamingNetworkChangeSet Sign(RoamingNetwork           RoamingNetwork,
                                            RoamingNetworkChangeSet  ChangeSet,
                                            POIKeyKind               Kind   = POIKeyKind.Signing)
        {

            var (key, keyId) = HeldKeyOf(RoamingNetwork, Kind)
                                   ?? throw new InvalidOperationException($"The roaming network '{RoamingNetwork.Id}' names no {KindText(Kind)} key of the roaming network operator '{Id}' held here.");

            return ChangeSet.Sign(key, keyId, Kind == POIKeyKind.Signing ? COSEAlgorithm.ES256 : COSEAlgorithm.ES512);

        }

        #endregion

        #region CommitSigner(RoamingNetwork)

        /// <summary>
        /// What signs every commit this hub makes of the given roaming network: the signing key held here it names,
        /// in the role of the roaming network operator; null where it names none.
        /// </summary>
        public RoamingNetworkCommitSigner? CommitSigner(RoamingNetwork RoamingNetwork)

            => HeldKeyOf(RoamingNetwork, POIKeyKind.Signing) is (ECPrivateKeyParameters key, String keyId)
                   ? new RoamingNetworkCommitSigner(key,
                                                    keyId,
                                                    COSEAlgorithm.ES256,
                                                    RoamingNetworkCommitSignatureRoles.RoamingNetworkOperator,
                                                    Id.ToString())
                   : null;

        #endregion


        #region StateKeys(RoamingNetworkId, SigningKeys, IdentityKeys, Now, Validity)

        /// <summary>
        /// The key statement of this operator in the given roaming network (SPECIFICATION.md of WWCP POI, section 8.6):
        /// the given signing keys and identity keys, as the network names them or will, signed with one of these identity
        /// keys held here, the current one where they name it, valid from now for the given time. Null where they name
        /// none held here.
        /// </summary>
        /// <param name="RoamingNetworkId">The roaming network.</param>
        /// <param name="SigningKeys">The signing keys of this operator the network names.</param>
        /// <param name="IdentityKeys">The identity keys of this operator the network names.</param>
        /// <param name="Now">When it is signed.</param>
        /// <param name="Validity">How long it is valid.</param>
        public RoamingNetworkKeyStatement? StateKeys(RoamingNetwork_Id          RoamingNetworkId,
                                                     IEnumerable<POIPublicKey>  SigningKeys,
                                                     IEnumerable<POIPublicKey>  IdentityKeys,
                                                     DateTimeOffset             Now,
                                                     TimeSpan                   Validity)
        {

            var identityKeys = IdentityKeys.ToArray();

            ECPrivateKeyParameters key;

            lock (keysLock)
            {

                var held   = HeldKeys(POIKeyKind.Identity);
                var named  = identityKeys.Select(identityKey => identityKey.Id).Where(held.ContainsKey).ToArray();

                if (named.Length == 0)
                    return null;

                key = held[named.Contains(CurrentKeyIdOrNull(POIKeyKind.Identity)) ? CurrentKeyIdOrNull(POIKeyKind.Identity)! : named[0]].Key;

            }

            return RoamingNetworkKeyStatement.Create(key,
                                                     COSEAlgorithm.ES512,
                                                     1,
                                                     RoamingNetworkId,
                                                     Id,
                                                     SigningKeys,
                                                     identityKeys,
                                                     Now,
                                                     Now + Validity);

        }

        #endregion


        #region (internal) MakeKey(Kind)

        /// <summary>
        /// A new key of the given kind, kept and held from now on beside the others, but not yet named by anybody.
        /// Written before any roaming network may name it.
        /// </summary>
        internal POIPublicKey MakeKey(POIKeyKind Kind)
        {

            var key        = NewKey(Kind == POIKeyKind.Signing ? COSECurve.P256 : COSECurve.P521);
            var publicKey  = POIPublicKey.From(key);
            var path       = KeptKeyPath(Kind, publicKey.Id);

            WriteKey(path, key);

            lock (keysLock)
                HeldKeys(Kind)[publicKey.Id] = (key, path);

            return publicKey;

        }

        #endregion

        #region (internal) ReplaceKey(Kind, OldKeyId, NewKey, Now)

        /// <summary>
        /// The given new key the current one of its kind in the place of the old one: named by operator.json, and so by
        /// every roaming network made from now on.
        /// </summary>
        internal void ReplaceKey(POIKeyKind    Kind,
                                 String        OldKeyId,
                                 POIPublicKey  NewKey,
                                 DateTimeOffset Now)
        {

            lock (keysLock)
            {

                var property  = Kind == POIKeyKind.Signing ? POIPartyKeys.SigningKeysProperty : POIPartyKeys.IdentityKeysProperty;
                var keys      = (Kind == POIKeyKind.Signing ? networkOperator.SigningKeys : networkOperator.IdentityKeys).
                                    Where (key => key.Id != OldKeyId).
                                    Append(NewKey).
                                    OrderBy(key => key.Id, StringComparer.Ordinal);

                Change(property, new JArray(keys.Select(key => key.ToJSON(Embedded: true))), Now);

            }

        }

        #endregion

        #region (internal) ChangeProperty(PropertyName, Value, Now)

        /// <summary>
        /// Change a property of the operator in operator.json, as every roaming network made from now on embeds it.
        /// </summary>
        internal void ChangeProperty(String          PropertyName,
                                     JToken          Value,
                                     DateTimeOffset  Now)
        {
            lock (keysLock)
                Change(PropertyName, Value, Now);
        }

        #endregion

        #region (internal) ForgetKeysNoneNames(Kind, StillNamed)

        /// <summary>
        /// Forget every key of the given kind but the current one that none of the given key identifications names -
        /// those the roaming networks this operator operates still name: delete its file; none without them. The
        /// current key is kept in "signing-key.pem" or "identity-key.pem" afterwards, the others beside it.
        /// </summary>
        /// <returns>The identifications of the keys forgotten.</returns>
        internal IReadOnlyList<String> ForgetKeysNoneNames(POIKeyKind             Kind,
                                                           IReadOnlySet<String>?  StillNamed)
        {

            lock (keysLock)
            {

                var held       = HeldKeys(Kind);
                var currentId  = CurrentKeyId(Kind);
                var forgotten  = new List<String>();

                foreach (var (keyId, (_, path)) in held.ToArray())
                {
                    if (keyId != currentId && StillNamed is not null && !StillNamed.Contains(keyId))
                    {
                        File.Delete(path);
                        held.Remove(keyId);
                        forgotten.Add(keyId);
                    }
                }

                // The current one in the file of the current one, every other one beside it.
                var canonical = Path.Combine(Directory, Kind == POIKeyKind.Signing ? SigningKeyFileName : IdentityKeyFileName);

                foreach (var (keyId, (key, path)) in held.ToArray())
                {
                    if (keyId != currentId && SamePath(path, canonical))
                    {
                        var aside = KeptKeyPath(Kind, keyId);
                        File.Move(path, aside, overwrite: true);
                        held[keyId] = (key, aside);
                    }
                }

                var (currentKey, currentPath) = held[currentId];

                if (!SamePath(currentPath, canonical))
                {
                    File.Move(currentPath, canonical, overwrite: true);
                    held[currentId] = (currentKey, canonical);
                }

                return forgotten;

            }

        }

        #endregion


        #region (private) HeldKeyOf(RoamingNetwork, Kind)

        private (ECPrivateKeyParameters Key, String KeyId)? HeldKeyOf(RoamingNetwork  RoamingNetwork,
                                                                      POIKeyKind      Kind)
        {

            if (RoamingNetwork.Operator is not RoamingNetworkOperator networkOperatorOfIt ||
                networkOperatorOfIt.Id != Id)
            {
                return null;
            }

            lock (keysLock)
            {

                var held   = HeldKeys(Kind);
                var named  = (Kind == POIKeyKind.Signing ? networkOperatorOfIt.SigningKeys : networkOperatorOfIt.IdentityKeys).
                                 Select(key => key.Id).
                                 Where (held.ContainsKey).
                                 ToArray();

                if (named.Length == 0)
                    return null;

                var keyId  = named.Contains(CurrentKeyIdOrNull(Kind)) ? CurrentKeyIdOrNull(Kind)! : named[0];

                return (held[keyId].Key, keyId);

            }

        }

        #endregion

        #region (private) HeldKeys(Kind) / CurrentKeyId(Kind) / CurrentKey(Kind)

        private Dictionary<String, (ECPrivateKeyParameters Key, String Path)> HeldKeys(POIKeyKind Kind)

            => Kind == POIKeyKind.Signing ? signingKeys : identityKeys;

        /// <summary>
        /// The identification of the current key of the given kind: the first one operator.json names that is held here.
        /// </summary>
        private String? CurrentKeyIdOrNull(POIKeyKind Kind)
        {
            lock (keysLock)
                return (Kind == POIKeyKind.Signing ? networkOperator.SigningKeys : networkOperator.IdentityKeys).
                           Select(key => key.Id).
                           FirstOrDefault(HeldKeys(Kind).ContainsKey);
        }

        private String CurrentKeyId(POIKeyKind Kind)

            => CurrentKeyIdOrNull(Kind)
                   ?? throw new InvalidOperationException($"The roaming network operator '{Id}' holds none of the {KindText(Kind)} keys it names.");

        private POIPublicKey CurrentKey(POIKeyKind Kind)
        {
            lock (keysLock)
            {
                var keyId = CurrentKeyId(Kind);
                return (Kind == POIKeyKind.Signing ? networkOperator.SigningKeys : networkOperator.IdentityKeys).First(key => key.Id == keyId);
            }
        }

        #endregion

        #region (private) Change(PropertyName, Value, Now)

        /// <summary>
        /// Change a property in operator.json, written in its place at once. Locked by the caller.
        /// </summary>
        private void Change(String          PropertyName,
                            JToken          Value,
                            DateTimeOffset  Now)
        {

            var json = networkOperator.ToJSON(Embedded: true);

            json[PropertyName]  = Value;
            json["lastChange"]  = Now.ToISO8601();

            if (!RoamingNetworkOperator.TryParse(json, out var changed, out var error))
                throw new InvalidOperationException($"The roaming network operator '{Id}' can not be changed so: {error}");

            WriteOperator(Path.Combine(Directory, OperatorFileName), changed);

            networkOperator = changed;

        }

        #endregion

        #region (private) ReadKeys()

        /// <summary>
        /// Hold every private key of the directory: the current ones, and those kept beside them.
        /// </summary>
        private void ReadKeys()
        {

            foreach (var (kind, pattern) in new[] { (POIKeyKind.Signing, "signing-key*.pem"), (POIKeyKind.Identity, "identity-key*.pem") })
                foreach (var path in System.IO.Directory.GetFiles(Directory, pattern).Order(StringComparer.Ordinal))
                {
                    var key = ReadKey(path);
                    HeldKeys(kind)[POIPublicKey.From(key).Id] = (key, path);
                }

        }

        #endregion

        #region (private) KeptKeyPath(Kind, KeyId) / KindText(Kind) / SamePath(Path1, Path2)

        private String KeptKeyPath(POIKeyKind  Kind,
                                   String      KeyId)

            => Path.Combine(Directory, $"{(Kind == POIKeyKind.Signing ? "signing-key" : "identity-key")}-{KeyId[..16]}.pem");

        private static String KindText(POIKeyKind Kind)

            => Kind == POIKeyKind.Signing ? "signing" : "identity";

        private static Boolean SamePath(String Path1, String Path2)

            => String.Equals(Path.GetFullPath(Path1), Path.GetFullPath(Path2), StringComparison.OrdinalIgnoreCase);

        #endregion

        #region (private static) NewKey(Curve)

        private static ECPrivateKeyParameters NewKey(COSECurve Curve)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(Curve.DomainParameters!, new SecureRandom()));
            return (ECPrivateKeyParameters) generator.GenerateKeyPair().Private;
        }

        #endregion

        #region (private static) WriteOperator(Path, Operator)

        /// <summary>
        /// Write the operator in the place of the file there: complete, or not at all.
        /// </summary>
        private static void WriteOperator(String                  Path,
                                          RoamingNetworkOperator  Operator)
        {
            var written = Path + ".tmp";
            File.WriteAllText(written, Operator.ToJSON(Embedded: true).ToString());
            File.Move(written, Path, overwrite: true);
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

            => $"the roaming network operator '{Id}', signing key {SigningKey.Id[..16]}…, identity key {IdentityKey.Id[..16]}…";

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
        /// The parties this hub adds to the roaming networks it operates: their entity type, by the segment the POI API
        /// lists them at below a roaming network, ~/RNs/{id}/CSOs and the others, which is the last segment of the path
        /// they are added at below /api/v1/poi as well.
        /// </summary>
        public static readonly IReadOnlyDictionary<String, String> PartyTypes = new Dictionary<String, String>(StringComparer.Ordinal) {
            { "CSOs",  nameof(ChargingStationOperator) },
            { "EMPs",  nameof(EMobilityProvider)       },
            { "GOs",   nameof(GridOperator)            },
            { "POs",   nameof(ParkingOperator)         }
        };

        /// <summary>
        /// One change signed by this hub as the roaming network operator at a time: a party added, a key replaced,
        /// the operator renamed.
        /// </summary>
        private readonly Lock networkOperatorLock = new ();

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
                Log.Info($"This hub is the roaming network operator '{NetworkOperator.Id}', its keys in '{directory}'" +
                         (NetworkOperator.KeptKeyIds.Count > 0 ? $", {NetworkOperator.KeptKeyIds.Count} older key(s) kept for the roaming networks that still name them." : "."),
                         "poi");

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

            lock (networkOperatorLock)
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

                    party[POIPartyKeys.SigningKeysProperty]   = new JArray(signingKeys. Select(key => key.ToJSON(Embedded: true)));
                    party[POIPartyKeys.IdentityKeysProperty]  = new JArray(identityKeys.Select(key => key.ToJSON(Embedded: true)));

                }

                #endregion

                if (network.TryGetParty(new POIPartyId(EntityType, id), out _))
                    return new (RoamingNetworkPartyStatus.Exists, $"The roaming network '{RoamingNetworkId}' has a {EntityType} '{id}' already.");

                RoamingNetworkChangeSet changeSet;

                try
                {
                    changeSet = NetworkOperator.Sign(
                                    network,
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

            }

            static RoamingNetworkPartyAddition Invalid(String Message)
                => new (RoamingNetworkPartyStatus.Invalid, Message);

        }

        #endregion

    }

}
