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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What became of a change of the roaming network operator this hub is, in every roaming network it operates.
    /// </summary>
    /// <param name="Message">What to tell whoever asked for it.</param>
    /// <param name="Changed">The roaming networks that name the operator as it is now.</param>
    /// <param name="Refused">The roaming networks whose history refused the change, and why.</param>
    public sealed record RoamingNetworkOperatorUpdate(String                                          Message,
                                                      IReadOnlyList<RoamingNetwork_Id>                Changed,
                                                      IReadOnlyDictionary<RoamingNetwork_Id, String>  Refused)
    {

        /// <summary>
        /// Whether every roaming network the operator operates names it as it is now.
        /// </summary>
        public Boolean Success
            => Refused.Count == 0;

        /// <summary>
        /// {"message", "changed": [ … ], "refused": { roaming network: why, … }}.
        /// </summary>
        public JObject ToJSON()

            => new (
                   new JProperty("message",  Message),
                   new JProperty("changed",  new JArray(Changed.Select(roamingNetworkId => roamingNetworkId.ToString()))),
                   new JProperty("refused",  new JObject(Refused.Select(refused => new JProperty(refused.Key.ToString(), refused.Value))))
               );

    }


    /// <summary>
    /// The roaming network operator this hub is, changed in every roaming network it operates: a key replaced by a
    /// new one, or its name.
    /// </summary>
    /// <remarks>
    /// Each roaming network gets a change set signed by this hub - a new signing key by the signing key it replaces,
    /// a new identity key by the identity key it replaces - and operator.json the change, so that every roaming
    /// network made afterwards embeds the operator as it is now. A key replaced is forgotten, its file deleted, once
    /// no roaming network this hub serves names it any more: one whose history refused the change keeps it, and so
    /// do all while a directory below "poi" could not be read at the start, whose roaming network may name it.
    /// </remarks>
    public partial class RoamingHub
    {

        #region RotateNetworkOperatorKey(Kind)

        /// <summary>
        /// Replace the current signing key or identity key of the roaming network operator this hub is by a new one,
        /// in every roaming network it operates.
        /// </summary>
        /// <param name="Kind">Which of its keys.</param>
        public RoamingNetworkOperatorUpdate RotateNetworkOperatorKey(POIKeyKind Kind)
        {

            lock (networkOperatorLock)
            {

                var now       = Timestamp.Now;
                var kind      = Kind == POIKeyKind.Signing ? "signing" : "identity";

                // Where several hold the keys of an operator, who holds them is decided together, and an identity key says
                // who: it is not replaced by its holder alone (SHAREHOLDERS.md). One keyring for all this hub holds keys
                // of, so none is made while one roaming network it makes or follows could not name it.
                if (Kind == POIKeyKind.Identity)
                {

                    var held    = NetworkOperator.IdentityKey.Id;
                    var shared  = POI.RoamingNetworks.ToArray().
                                      Where(network => network.Operator is RoamingNetworkOperator networkOperator &&
                                                       networkOperator.MinConsensus > 1                            &&
                                                       networkOperator.IdentityKeys.Any(key => key.Id == held)).
                                      ToArray();

                    if (shared.Length > 0)
                        return new ($"The identity key of the roaming network operator '{NetworkOperator.Id}' is not replaced: this hub holds it with others in " +
                                    $"{String.Join(", ", shared.Select(network => $"'{network.Id}'"))}, where an identity key is replaced by them together, which is not built.",
                                    [],
                                    shared.ToDictionary(network => network.Id,
                                                        network => $"{network.Operator!.MinConsensus} holders of the keys of its operator decide"));

                }

                var oldKey    = Kind == POIKeyKind.Signing ? NetworkOperator.SigningKey : NetworkOperator.IdentityKey;
                var newKey    = NetworkOperator.MakeKey(Kind);
                var changed   = new List<RoamingNetwork_Id>();
                var refused   = new Dictionary<RoamingNetwork_Id, String>();

                foreach (var history in HistoriesOfTheNetworkOperator())
                {

                    var network = history.Head.RoamingNetwork;

                    if (NetworkOperator.HeldKeyIdOf(network, Kind) is not String heldKeyId)
                    {
                        refused[network.Id] = $"it names no {kind} key of the roaming network operator held here";
                        continue;
                    }

                    // Where several hold the keys, who holds them is decided together, and an identity key says who: it
                    // is not replaced by its holder alone (SHAREHOLDERS.md). A signing key is, see below.
                    if (Kind == POIKeyKind.Identity && network.Operator!.MinConsensus > 1)
                    {
                        refused[network.Id] = $"{network.Operator.MinConsensus} holders of the keys of its operator decide: an identity key is replaced by them together, which is not built";
                        continue;
                    }

                    var signersBefore = history.Signers;

                    try
                    {

                        // The new key in the place of the one held here; a key of the operator held elsewhere, e.g. offline, stays.
                        var keys          = (Kind == POIKeyKind.Signing ? network.Operator!.SigningKeys : network.Operator!.IdentityKeys).
                                                Where  (key => key.Id != heldKeyId).
                                                Append (newKey).
                                                OrderBy(key => key.Id, StringComparer.Ordinal).
                                                ToArray();

                        // Its signing keys are those of its certificates: a new signing key certified, or after a new identity key
                        // all of them certified anew by it, with its keys stated anew, in the same change set: a version naming
                        // other identity keys than its key statement, or a certificate by an identity key it does not, is refused.
                        IEnumerable<POIPublicKey> identityKeys  = Kind == POIKeyKind.Identity ? keys : network.Operator.IdentityKeys;

                        var certificates  = Kind == POIKeyKind.Signing
                                                ? network.Operator.SigningKeyCertificates.Where(certificate => certificate.SigningKey.Id != heldKeyId).
                                                                                          Concat(NetworkOperator.CertifySigningKeys([ newKey ], identityKeys, now) ?? []).
                                                                                          ToArray()
                                                : NetworkOperator.CertifySigningKeys(network.Operator.SigningKeys, identityKeys, now)?.ToArray() ?? [];

                        var keyStatement  = Kind == POIKeyKind.Identity
                                                ? NetworkOperator.StateKeys(network.Id, identityKeys, now, KeyStatementValidity)
                                                : null;

                        List<RoamingNetworkChange> changes = [ RoamingNetworkChange.UpdateProperty(network.Operator, RoamingNetworkSigningKeyCertificate.JSONProperty, null, new JArray(certificates.Select(certificate => certificate.ToJSON(Embedded: true)))) ];

                        if (Kind == POIKeyKind.Identity)
                            changes.Insert(0, RoamingNetworkChange.UpdateProperty(network.Operator, POIPartyKeys.IdentityKeysProperty, null, new JArray(keys.Select(key => key.ToJSON(Embedded: true)))));

                        if (keyStatement is not null)
                            changes.Add(RoamingNetworkChange.UpdateProperty(network.Operator, RoamingNetworkKeyStatement.JSONProperty, null, keyStatement.ToJSON(Embedded: true)));

                        var changeSet     = NetworkOperator.Sign(
                                                network,
                                                network.CreateChangeSet(
                                                    changes,
                                                    Description: new ImmutableI18NString(I18NString.Create(Languages.en, $"The {kind} key {heldKeyId[..16]}… of the roaming network operator " +
                                                                                                                         $"'{NetworkOperator.Id}' replaced by {newKey.Id[..16]}…."))
                                                ),
                                                // Where several hold the keys, a holder renews its own signing key with its
                                                // identity key, alone: a change of its certificates only (SPECIFICATION.md of
                                                // WWCP POI, section 4.1). Its signing key is one voice of several there.
                                                Kind == POIKeyKind.Signing && network.Operator!.MinConsensus > 1
                                                    ? POIKeyKind.Identity
                                                    : Kind
                                            );

                        // The key statement and the certificates are data, signed by the signing key as well; and the new
                        // identity key signs it as the proof that this hub holds it.
                        if (Kind == POIKeyKind.Identity)
                            changeSet = NetworkOperator.ProveIdentityKey(NetworkOperator.Sign(network, changeSet, POIKeyKind.Signing), newKey);

                        // Its commits are signed by the new signing key from this one on: the version it leads to certifies
                        // the old one no longer, and a history refuses a commit signed by a key it takes its role from.
                        if (Kind == POIKeyKind.Signing &&
                            NetworkOperator.CommitSignerOf(newKey.Id) is RoamingNetworkCommitSigner signer)
                        {
                            history.Signers = [ signer ];
                        }

                        var result     = history.Commit(changeSet);

                        if (!result.IsCommitted)
                        {
                            // Still the old key's: the roaming network names it as before.
                            history.Signers     = signersBefore;
                            refused[network.Id] = result.ErrorResponse ?? "its history refused the commit";
                            continue;
                        }

                        // And its heads attested by it, the current one now.
                        Attest(history, now);

                        changed.Add(network.Id);

                    }
                    catch (Exception e)
                    {
                        // Signed by what signed it before, unless the commit was made: then its head names the new key.
                        if (history.Head.RoamingNetwork.Operator?.SigningKeys.Any(key => key.Id == newKey.Id) != true)
                            history.Signers = signersBefore;

                        refused[network.Id] = e.Message;
                    }

                }

                // The roaming networks made from now on name the new key.
                NetworkOperator.ReplaceKey(Kind, oldKey.Id, newKey, now);

                var forgotten = NetworkOperator.ForgetKeysNoneNames(Kind,
                                                                    unreadPOIStores > 0
                                                                        ? null
                                                                        : KeysNamedByTheNetworks(Kind));

                var message   = $"The {kind} key of the roaming network operator '{NetworkOperator.Id}' is {newKey.Id[..16]}… now, " +
                                $"in {changed.Count} roaming network(s){(changed.Count > 0 ? ": " + String.Join(", ", changed) : "")}." +
                                (refused.Count > 0
                                     ? $" Not in {String.Join("; ", refused.Select(entry => $"'{entry.Key}': {entry.Value}"))} - which keep(s) the key it names."
                                     : "") +
                                (forgotten.Contains(oldKey.Id)
                                     ? $" The old key {oldKey.Id[..16]}… is forgotten."
                                     : unreadPOIStores > 0
                                           ? $" The old key {oldKey.Id[..16]}… is kept: {unreadPOIStores} directory(s) below '{POIDirectoryName}' could not be read at the start, whose roaming networks may name it."
                                           : $" The old key {oldKey.Id[..16]}… is kept for the roaming networks that still name it.");

                Log.Notice(message, "poi", "security");

                return new (message, changed, refused);

            }

        }

        #endregion

        #region NameNetworkOperatorHolder(Holder)

        /// <summary>
        /// Name the organization this hub is as the holder of the keys of its roaming network operator, in every
        /// roaming network it operates (SHAREHOLDERS.md, phase 1): a new identity key and a new signing key naming it,
        /// in the place of the current ones, as a key keeps its holder. Where the current keys name it already, no
        /// keys are made, and only the roaming networks that still name others are changed: asked again, it goes on
        /// where it was refused.
        /// </summary>
        /// <remarks>
        /// One change set per roaming network, as an identity key certifies the signing keys of its own holder only:
        /// its identity keys, its certificates and its key statement together, signed by the identity key and the
        /// signing key it replaces and by the new identity key, its proof; the commit by the new signing key. A
        /// roaming network naming keys of its operator not held here, or a quorum of several, is not changed from
        /// here: a shareholder is named by itself, at its own hub. As with an identity key replaced, the trust anchor
        /// of a roaming network is another one afterwards.
        /// </remarks>
        /// <param name="Holder">The name of the organization: nonempty, without white space around it.</param>
        /// <exception cref="ArgumentException">The name is none.</exception>
        public RoamingNetworkOperatorUpdate NameNetworkOperatorHolder(String Holder)
        {

            if (!HubRoamingNetworkOperator.IsHolderName(Holder))
                throw new ArgumentException("The holder of the keys of a roaming network operator is named by a nonempty text without white space around it.", nameof(Holder));

            lock (networkOperatorLock)
            {

                var now             = Timestamp.Now;
                var oldSigningKey   = NetworkOperator.SigningKey;
                var oldIdentityKey  = NetworkOperator.IdentityKey;
                var named           = NetworkOperator.Holder == Holder && oldSigningKey.Holder == Holder;

                // Named so already: its current keys are the ones every roaming network is to name.
                var newSigningKey   = named ? oldSigningKey  : NetworkOperator.MakeKey(POIKeyKind.Signing,  Holder);
                var newIdentityKey  = named ? oldIdentityKey : NetworkOperator.MakeKey(POIKeyKind.Identity, Holder);
                var changed         = new List<RoamingNetwork_Id>();
                var refused         = new Dictionary<RoamingNetwork_Id, String>();

                foreach (var history in HistoriesOfTheNetworkOperator())
                {

                    var network          = history.Head.RoamingNetwork;
                    var networkOperator  = network.Operator!;

                    // Names them already: nothing to commit.
                    if (networkOperator.IdentityKeys.Select(key => key.Id).SequenceEqual([ newIdentityKey.Id ]) &&
                        networkOperator.SigningKeys. Select(key => key.Id).SequenceEqual([ newSigningKey. Id ]))
                    {
                        changed.Add(network.Id);
                        continue;
                    }

                    if (NetworkOperator.HeldKeyIdOf(network, POIKeyKind.Signing)  is not String heldSigningKeyId ||
                        NetworkOperator.HeldKeyIdOf(network, POIKeyKind.Identity) is not String heldIdentityKeyId)
                    {
                        refused[network.Id] = "it names no signing key and identity key of the roaming network operator held here";
                        continue;
                    }

                    // Others hold keys of its operator as well: each of them is named by itself, at its own hub.
                    if (networkOperator.MinConsensus > 1          ||
                        networkOperator.IdentityKeys.Length != 1  ||
                        networkOperator.SigningKeys. Length != 1)
                    {
                        refused[network.Id] = "it names keys of the roaming network operator not held here, or a quorum of several holders: a holder is named at its own hub";
                        continue;
                    }

                    var signersBefore = history.Signers;

                    try
                    {

                        var certificates  = NetworkOperator.CertifySigningKeys([ newSigningKey ], [ newIdentityKey ], now);
                        var keyStatement  = NetworkOperator.StateKeys(network.Id, [ newIdentityKey ], now, KeyStatementValidity);

                        if (certificates is null || keyStatement is null)
                        {
                            refused[network.Id] = "the new identity key is not held here";
                            continue;
                        }

                        var changeSet     = network.CreateChangeSet(
                                                [
                                                    RoamingNetworkChange.UpdateProperty(networkOperator, POIPartyKeys.IdentityKeysProperty,                null, new JArray(newIdentityKey.ToJSON(Embedded: true))),
                                                    RoamingNetworkChange.UpdateProperty(networkOperator, RoamingNetworkSigningKeyCertificate.JSONProperty, null, new JArray(certificates.Select(certificate => certificate.ToJSON(Embedded: true)))),
                                                    RoamingNetworkChange.UpdateProperty(networkOperator, RoamingNetworkKeyStatement.JSONProperty,          null, keyStatement.ToJSON(Embedded: true))
                                                ],
                                                Description: new ImmutableI18NString(I18NString.Create(Languages.en, $"The keys of the roaming network operator '{NetworkOperator.Id}' replaced by " +
                                                                                                                     $"ones naming '{Holder}' as their holder."))
                                            );

                        // By the identity key it replaces, as its keys change; by the signing key it replaces, as the key
                        // statement and the certificates are data; and by the new identity key, the proof that it is held.
                        changeSet         = NetworkOperator.ProveIdentityKey(
                                                NetworkOperator.Sign(network,
                                                                     NetworkOperator.Sign(network, changeSet, POIKeyKind.Identity),
                                                                     POIKeyKind.Signing),
                                                newIdentityKey
                                            );

                        // Signed by the new signing key from this commit on: it takes its role from the old one.
                        if (NetworkOperator.CommitSignerOf(newSigningKey.Id) is RoamingNetworkCommitSigner signer)
                            history.Signers = [ signer ];

                        var result        = history.Commit(changeSet);

                        if (!result.IsCommitted)
                        {
                            history.Signers     = signersBefore;
                            refused[network.Id] = result.ErrorResponse ?? "its history refused the commit";
                            continue;
                        }

                        // And its heads attested by it, the current one now.
                        Attest(history, now);

                        changed.Add(network.Id);

                    }
                    catch (Exception e)
                    {
                        // Signed by what signed it before, unless the commit was made: then its head names the new key.
                        if (history.Head.RoamingNetwork.Operator?.SigningKeys.Any(key => key.Id == newSigningKey.Id) != true)
                            history.Signers = signersBefore;

                        refused[network.Id] = e.Message;
                    }

                }

                var forgotten = new List<String>();

                if (!named)
                {

                    // The roaming networks made from now on name the new keys.
                    NetworkOperator.ReplaceKeys(newSigningKey, newIdentityKey, now);

                }

                foreach (var kind in new[] { POIKeyKind.Signing, POIKeyKind.Identity })
                    forgotten.AddRange(NetworkOperator.ForgetKeysNoneNames(kind,
                                                                           unreadPOIStores > 0
                                                                               ? null
                                                                               : KeysNamedByTheNetworks(kind)));

                var message   = $"The keys of the roaming network operator '{NetworkOperator.Id}' name '{Holder}' as their holder" +
                                (named
                                     ? ""
                                     : $", the identity key {newIdentityKey.Id[..16]}… and the signing key {newSigningKey.Id[..16]}… now") +
                                $", in {changed.Count} roaming network(s){(changed.Count > 0 ? ": " + String.Join(", ", changed) : "")}." +
                                (refused.Count > 0
                                     ? $" Not in {String.Join("; ", refused.Select(entry => $"'{entry.Key}': {entry.Value}"))} - which keep(s) the keys it names."
                                     : "") +
                                (forgotten.Count > 0
                                     ? $" {forgotten.Count} old key(s) forgotten."
                                     : "");

                Log.Notice(message, "poi", "security");

                return new (message, changed, refused);

            }

        }

        #endregion

        #region RenameNetworkOperator(Name)

        /// <summary>
        /// Give the roaming network operator this hub is a new name, in every roaming network it operates.
        /// </summary>
        /// <param name="Name">Its new name.</param>
        /// <exception cref="ArgumentException">The name is empty.</exception>
        public RoamingNetworkOperatorUpdate RenameNetworkOperator(I18NString Name)
        {

            if (Name.IsNullOrEmpty())
                throw new ArgumentException("A roaming network operator is renamed to a name, not to none.", nameof(Name));

            lock (networkOperatorLock)
            {

                var now       = Timestamp.Now;
                var nameJSON  = Name.ToJSON();
                var changed   = new List<RoamingNetwork_Id>();
                var refused   = new Dictionary<RoamingNetwork_Id, String>();

                foreach (var history in HistoriesOfTheNetworkOperator())
                {

                    var network = history.Head.RoamingNetwork;

                    // Called so already: nothing to commit.
                    if (JToken.DeepEquals(network.Operator!.Name.ToJSON(), nameJSON))
                    {
                        changed.Add(network.Id);
                        continue;
                    }

                    if (!NetworkOperator.Operates(network))
                    {
                        refused[network.Id] = "it names no signing key of the roaming network operator held here";
                        continue;
                    }

                    try
                    {

                        var result = history.Commit(
                                         NetworkOperator.Sign(
                                             network,
                                             network.CreateChangeSet(
                                                 [ RoamingNetworkChange.UpdateProperty(network.Operator!, "name", null, nameJSON) ],
                                                 Description: new ImmutableI18NString(I18NString.Create(Languages.en, $"The roaming network operator '{NetworkOperator.Id}' renamed to '{Name.FirstText()}'."))
                                             )
                                         )
                                     );

                        if (!result.IsCommitted)
                        {
                            refused[network.Id] = result.ErrorResponse ?? "its history refused the commit";
                            continue;
                        }

                        changed.Add(network.Id);

                    }
                    catch (Exception e)
                    {
                        refused[network.Id] = e.Message;
                    }

                }

                // The roaming networks made from now on are operated by it as it is called now.
                NetworkOperator.ChangeProperty("name", nameJSON, now);

                var message = $"The roaming network operator '{NetworkOperator.Id}' is called '{Name.FirstText()}' now, " +
                              $"in {changed.Count} roaming network(s){(changed.Count > 0 ? ": " + String.Join(", ", changed) : "")}." +
                              (refused.Count > 0
                                   ? $" Not in {String.Join("; ", refused.Select(entry => $"'{entry.Key}': {entry.Value}"))}."
                                   : "");

                Log.Notice(message, "poi");

                return new (message, changed, refused);

            }

        }

        #endregion


        #region (private) HistoriesOfTheNetworkOperator()

        /// <summary>
        /// The histories of the roaming networks this hub serves whose operator is the one this hub is,
        /// in the order of their identifications.
        /// </summary>
        private IEnumerable<RoamingNetworkHistory> HistoriesOfTheNetworkOperator()
        {

            // Not the ones followed at another server, whoever operates them: nothing of them is changed from here.
            foreach (var network in POI.RoamingNetworks.OrderBy(network => network.Id.ToString(), StringComparer.OrdinalIgnoreCase).ToArray())
                if (network.Operator?.Id == NetworkOperator.Id &&
                    !IsFollowed(network.Id)                     &&
                    POI.TryGetRoamingNetworkHistory(network.Id, out var history))
                {
                    yield return history;
                }

        }

        #endregion

        #region (private) KeysNamedByTheNetworks(Kind)

        /// <summary>
        /// The keys of the given kind that the operators of the roaming networks this hub serves name, as their heads
        /// have it: of the ones it operates, and of the ones it follows, whose operator it may hold keys of as one of
        /// several - a key one of them names is not forgotten, whoever that operator is.
        /// </summary>
        private IReadOnlySet<String> KeysNamedByTheNetworks(POIKeyKind Kind)

            => POI.RoamingNetworks.ToArray().
                   Where     (network => network.Operator is not null).
                   SelectMany(network => Kind == POIKeyKind.Signing
                                             ? network.Operator!.SigningKeys
                                             : network.Operator!.IdentityKeys).
                   Select    (key => key.Id).
                   ToHashSet (StringComparer.Ordinal);

        #endregion

    }

}
