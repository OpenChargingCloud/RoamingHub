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
                var property  = Kind == POIKeyKind.Signing ? POIPartyKeys.SigningKeysProperty : POIPartyKeys.IdentityKeysProperty;
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

                    try
                    {

                        // The new key in the place of the one held here; a key of the operator held elsewhere, e.g. offline, stays.
                        var keys       = (Kind == POIKeyKind.Signing ? network.Operator!.SigningKeys : network.Operator!.IdentityKeys).
                                             Where  (key => key.Id != heldKeyId).
                                             Append (newKey).
                                             OrderBy(key => key.Id, StringComparer.Ordinal);

                        var changeSet  = NetworkOperator.Sign(
                                             network,
                                             network.CreateChangeSet(
                                                 [ RoamingNetworkChange.UpdateProperty(network.Operator!, property, null, new JArray(keys.Select(key => key.ToJSON()))) ],
                                                 Description: new ImmutableI18NString(I18NString.Create(Languages.en, $"The {kind} key {heldKeyId[..16]}… of the roaming network operator " +
                                                                                                                      $"'{NetworkOperator.Id}' replaced by {newKey.Id[..16]}…."))
                                             ),
                                             Kind
                                         );

                        var result     = history.Commit(changeSet);

                        if (!result.IsCommitted)
                        {
                            refused[network.Id] = result.ErrorResponse ?? "its history refused the commit";
                            continue;
                        }

                        // Its commits are signed by the new signing key from now on.
                        if (Kind == POIKeyKind.Signing &&
                            NetworkOperator.CommitSigner(history.Head.RoamingNetwork) is RoamingNetworkCommitSigner signer)
                        {
                            history.Signers = [ signer ];
                        }

                        changed.Add(network.Id);

                    }
                    catch (Exception e)
                    {
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

            foreach (var network in POI.RoamingNetworks.OrderBy(network => network.Id.ToString(), StringComparer.OrdinalIgnoreCase).ToArray())
                if (network.Operator?.Id == NetworkOperator.Id &&
                    POI.TryGetRoamingNetworkHistory(network.Id, out var history))
                {
                    yield return history;
                }

        }

        #endregion

        #region (private) KeysNamedByTheNetworks(Kind)

        /// <summary>
        /// The keys of the given kind of the roaming network operator this hub is that the roaming networks it
        /// serves name, as their heads have it.
        /// </summary>
        private IReadOnlySet<String> KeysNamedByTheNetworks(POIKeyKind Kind)

            => HistoriesOfTheNetworkOperator().
                   SelectMany(history => Kind == POIKeyKind.Signing
                                             ? history.Head.RoamingNetwork.Operator!.SigningKeys
                                             : history.Head.RoamingNetwork.Operator!.IdentityKeys).
                   Select(key => key.Id).
                   ToHashSet(StringComparer.Ordinal);

        #endregion

    }

}
