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
using System.Text;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What a change of who holds the keys of a roaming network operator is about.
    /// </summary>
    public enum ShareholderChangeKind
    {

        /// <summary>
        /// One more shareholder: it asked, with its keys.
        /// </summary>
        Admit,

        /// <summary>
        /// One shareholder less: dismissed by the others, or resigning.
        /// </summary>
        Dismiss,

        /// <summary>
        /// The same shareholders: their key statement stated anew, before it ends.
        /// </summary>
        Restate

    }


    /// <summary>
    /// What became of asking, admitting or dismissing.
    /// </summary>
    public enum ShareholderChangeStatus
    {

        /// <summary>
        /// Asked, or signed as far as this hub signs: it waits for the others.
        /// </summary>
        Waiting,

        /// <summary>
        /// Committed: the roaming network names its shareholders so.
        /// </summary>
        Committed,

        /// <summary>
        /// Nothing for this hub to do right now: it signed already, or it is none of those who decide.
        /// </summary>
        NothingToDo,

        /// <summary>
        /// There is no such roaming network, shareholder or request here.
        /// </summary>
        NotFound,

        /// <summary>
        /// It is not what the roaming network needs: other keys, a holder that is one already, a quorum that does not fit.
        /// </summary>
        Invalid,

        /// <summary>
        /// The other hub did not take it, or could not be asked.
        /// </summary>
        Refused

    }


    /// <summary>
    /// What became of asking, admitting or dismissing.
    /// </summary>
    /// <param name="Status">What became of it.</param>
    /// <param name="Message">The same, said.</param>
    public sealed record ShareholderChangeResult(ShareholderChangeStatus  Status,
                                                 String                   Message)
    {

        /// <summary>
        /// Whether it went on, or is done.
        /// </summary>
        public Boolean Success
            => Status is ShareholderChangeStatus.Waiting or ShareholderChangeStatus.Committed or ShareholderChangeStatus.NothingToDo;

    }


    /// <summary>
    /// A change of who holds the keys of the operator of a roaming network, waiting at the hub where the roaming
    /// network is made: one more shareholder, as it asked with its public identity key, the certificate of its signing
    /// key and the URL of its copy, or one less; with the key statement of the holders afterwards, signed as far as
    /// they signed it, and the change set once it was made of it.
    /// </summary>
    public sealed class ShareholderChange
    {

        public ShareholderChangeKind                 Kind                     { get; }
        public RoamingNetwork_Id                     RoamingNetworkId         { get; }
        public String                                Holder                   { get; }
        public POIPublicKey?                         IdentityKey              { get; }
        public RoamingNetworkSigningKeyCertificate?  SigningKeyCertificate    { get; }
        public URL?                                  URL                      { get; }
        public RoamingNetworkKeyStatement            KeyStatement             { get; internal set; }
        public ChangeSet_Id?                         ChangeSetId              { get; internal set; }

        internal ShareholderChange(ShareholderChangeKind                 Kind,
                                   RoamingNetwork_Id                     RoamingNetworkId,
                                   String                                Holder,
                                   RoamingNetworkKeyStatement            KeyStatement,
                                   POIPublicKey?                         IdentityKey             = null,
                                   RoamingNetworkSigningKeyCertificate?  SigningKeyCertificate   = null,
                                   URL?                                  URL                     = null,
                                   ChangeSet_Id?                         ChangeSetId             = null)
        {

            this.Kind                   = Kind;
            this.RoamingNetworkId       = RoamingNetworkId;
            this.Holder                 = Holder;
            this.KeyStatement           = KeyStatement;
            this.IdentityKey            = IdentityKey;
            this.SigningKeyCertificate  = SigningKeyCertificate;
            this.URL                    = URL;
            this.ChangeSetId            = ChangeSetId;

        }

        /// <summary>
        /// {"kind", "holder", "keyStatement", "identityKey"?, "signingKeyCertificate"?, "url"?, "changeSet"?}.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("kind",          Kind switch {
                                                              ShareholderChangeKind.Admit    => "admit",
                                                              ShareholderChangeKind.Dismiss  => "dismiss",
                                                              _                              => "restate"
                                                          }),
                           new JProperty("holder",        Holder),
                           new JProperty("keyStatement",  KeyStatement.ToJSON(Embedded: true))
                       );

            if (IdentityKey is not null)
                json["identityKey"]            = IdentityKey.ToJSON(Embedded: true);

            if (SigningKeyCertificate is not null)
                json["signingKeyCertificate"]  = SigningKeyCertificate.ToJSON(Embedded: true);

            if (URL.HasValue)
                json["url"]                    = URL.Value.ToString();

            if (ChangeSetId.HasValue)
                json["changeSet"]              = ChangeSetId.Value.ToString();

            return json;

        }

        /// <summary>
        /// Try to read a change as ToJSON() writes it.
        /// </summary>
        public static Boolean TryParse(JObject                                      JSON,
                                       RoamingNetwork_Id                            RoamingNetworkId,
                                       [NotNullWhen(true)]  out ShareholderChange?  Change,
                                       [NotNullWhen(false)] out String?             ErrorResponse)
        {

            Change = null;

            if (JSON["keyStatement"] is not JObject statementJSON)
            {
                ErrorResponse = "It names no key statement of the holders afterwards.";
                return false;
            }

            if (!RoamingNetworkKeyStatement.TryParse(statementJSON, out var keyStatement, out ErrorResponse))
                return false;

            var kind = JSON["kind"]?.Value<String>() switch {
                           "dismiss"  => ShareholderChangeKind.Dismiss,
                           "restate"  => ShareholderChangeKind.Restate,
                           _          => ShareholderChangeKind.Admit
                       };

            POIPublicKey?                         identityKey  = null;
            RoamingNetworkSigningKeyCertificate?  certificate  = null;
            URL?                                  url          = null;
            ChangeSet_Id?                         changeSetId  = null;

            if (kind == ShareholderChangeKind.Admit)
            {

                if (JSON["identityKey"] is not JObject keyJSON || JSON["signingKeyCertificate"] is not JObject certificateJSON)
                {
                    ErrorResponse = "It names no public identity key and no certificate of the signing key of the one asking.";
                    return false;
                }

                if (!POIPublicKey.TryParse(keyJSON, out identityKey, out ErrorResponse) ||
                    !RoamingNetworkSigningKeyCertificate.TryParse(certificateJSON, out certificate, out ErrorResponse))
                {
                    return false;
                }

            }

            if (JSON["url"] is JToken urlJSON)
            {

                if (urlJSON.Type != JTokenType.String || !org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.TryParse(urlJSON.Value<String>()!, out var parsed))
                {
                    ErrorResponse = "Its URL is none.";
                    return false;
                }

                url = parsed;

            }

            if (JSON["changeSet"]?.Value<String>() is String changeSetText && ChangeSet_Id.TryParse(changeSetText, out var parsedId))
                changeSetId = parsedId;

            var holder = kind == ShareholderChangeKind.Admit
                             ? identityKey!.Holder
                             : JSON["holder"]?.Value<String>();

            // Stating the keys anew is of all holders, and names none.
            if (kind == ShareholderChangeKind.Restate)
                holder = "";

            else if (!HubRoamingNetworkOperator.IsHolderName(holder))
            {
                ErrorResponse = "It names no holder.";
                return false;
            }

            Change         = new ShareholderChange(kind, RoamingNetworkId, holder!, keyStatement, identityKey, certificate, url, changeSetId);
            ErrorResponse  = null;
            return true;

        }

    }


    /// <summary>
    /// Who holds the keys of the operator of a roaming network changes (SHAREHOLDERS.md, phase 4): a hub following it
    /// asks to hold them as well, the holders admit it, or dismiss one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A change waits at the hub where the roaming network is made, below its JSON API, read and written by
    /// everybody: everything in it is signed. It is the key statement of the holders afterwards first, which each of
    /// them signs at its own hub until the quorum before and its own have; then the hub where it waits makes the one
    /// change set of it, which waits for the signatures of the holders before, and of the new identity key, as every
    /// change set of the operator does.
    /// </para>
    /// <para>
    /// Nothing of it happens by itself. Each command does what its hub can do right then, asked for by somebody with
    /// poi:edit, and says what is missing: "join" asks, and later signs the change set admitting this hub; "admit"
    /// and "dismiss" sign the key statement, and later the change set, at the hub where the roaming network is made
    /// and at every other holder's.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// How many changes of the shareholders of one roaming network wait here at most.
        /// </summary>
        public const Int32  MaxShareholderChanges  = 16;

        private readonly Dictionary<(RoamingNetwork_Id RoamingNetworkId, String Holder), ShareholderChange>  shareholderChanges  = [];

        private static readonly HttpClient  shareholderHTTP  = new () { Timeout = TimeSpan.FromSeconds(15) };

        #endregion


        #region ShareholdersOf(RoamingNetworkId)

        /// <summary>
        /// Who holds the keys of the operator of the given roaming network made here, how many of them decide, and the
        /// changes of that waiting: {"roamingNetworkOperator", "minConsensus", "holders": [ {"holder", "identityKeys",
        /// "signingKeys"} ], "changes": [ … ]}; null where this hub makes no such roaming network.
        /// </summary>
        public JObject? ShareholdersOf(RoamingNetwork_Id RoamingNetworkId)
        {

            if (IsFollowed(RoamingNetworkId) ||
                !POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out var history) ||
                history.Head.RoamingNetwork.Operator is not RoamingNetworkOperator networkOperator)
            {
                return null;
            }

            ShareholderChange[] changes;

            lock (poiLock)
            {
                ForgetWhatIsDone(history);
                changes = [ .. shareholderChanges.Where(change => change.Key.RoamingNetworkId == RoamingNetworkId).Select(change => change.Value) ];
            }

            return new JObject(
                       new JProperty("roamingNetworkOperator",  networkOperator.Id.ToString()),
                       new JProperty("minConsensus",            networkOperator.MinConsensus),
                       new JProperty("holders",                 new JArray(POIPartyKeys.Holders(networkOperator.IdentityKeys).Order(StringComparer.Ordinal).Select(holder => new JObject(
                                                                               new JProperty("holder",        holder),
                                                                               new JProperty("identityKeys",  new JArray(networkOperator.IdentityKeys.Where(key => key.HolderOrUnnamed == holder).Select(key => key.Id))),
                                                                               new JProperty("signingKeys",   new JArray(networkOperator.SigningKeys. Where(key => key.HolderOrUnnamed == holder).Select(key => key.Id)))
                                                                           )))),
                       new JProperty("changes",                 new JArray(changes.OrderBy(change => change.Holder, StringComparer.Ordinal).Select(change => change.ToJSON())))
                   );

        }

        #endregion

        #region TakeShareholderRequest(RoamingNetworkId, JSON)

        /// <summary>
        /// Take what a hub following the given roaming network made here sends to hold the keys of its operator as well:
        /// its public identity key, the certificate of its signing key, the URL of its copy if it says one, and the key
        /// statement of the holders so far and itself, which it signed. Sent again by the same holder, it replaces what
        /// waits, unless its change set was made already.
        /// </summary>
        public ShareholderChangeResult TakeShareholderRequest(RoamingNetwork_Id  RoamingNetworkId,
                                                              JObject            JSON)
        {

            if (!TryGetOperated(RoamingNetworkId, out var history, out var notOperated))
                return notOperated;

            JSON["kind"] = "admit";
            JSON.Remove("changeSet");

            if (!ShareholderChange.TryParse(JSON, RoamingNetworkId, out var request, out var error))
                return new (ShareholderChangeStatus.Invalid, $"It is no request to hold the keys of the operator of the roaming network '{RoamingNetworkId}': {error}");

            if (ChangeError(request, history.Head.RoamingNetwork) is String changeError)
                return new (ShareholderChangeStatus.Invalid, changeError);

            lock (poiLock)
            {

                ForgetWhatIsDone(history);

                var key = (RoamingNetworkId, request.Holder);

                if (shareholderChanges.TryGetValue(key, out var waiting) && waiting.ChangeSetId.HasValue)
                    return new (ShareholderChangeStatus.Waiting, $"The change set admitting '{request.Holder}' waits for signatures already: '{waiting.ChangeSetId}'.");

                if (!shareholderChanges.ContainsKey(key) &&
                    shareholderChanges.Keys.Count(waitingKey => waitingKey.RoamingNetworkId == RoamingNetworkId) >= MaxShareholderChanges)
                {
                    return new (ShareholderChangeStatus.Refused, $"{MaxShareholderChanges} changes of the shareholders of the roaming network '{RoamingNetworkId}' wait already.");
                }

                shareholderChanges[key] = request;

            }

            Log.Notice($"'{request.Holder}' asks to hold the keys of the operator of the roaming network '{RoamingNetworkId}' as well, with the identity key {request.IdentityKey!.Id[..16]}….",
                       "poi", "shareholders", "security");

            return new (ShareholderChangeStatus.Waiting,
                        $"'{request.Holder}' asked to hold the keys of the operator of the roaming network '{RoamingNetworkId}': it waits for those who hold them to admit it.");

        }

        #endregion

        #region TakeKeyStatementSignatures(RoamingNetworkId, Holder, KeyStatementJSON)

        /// <summary>
        /// Take the signatures another holder added to the key statement of a change waiting here: the same statement,
        /// with signatures of identity keys it names, or of the holders before it.
        /// </summary>
        public ShareholderChangeResult TakeKeyStatementSignatures(RoamingNetwork_Id  RoamingNetworkId,
                                                                  String             Holder,
                                                                  JObject            KeyStatementJSON)
        {

            if (!TryGetOperated(RoamingNetworkId, out var history, out var notOperated))
                return notOperated;

            if (!RoamingNetworkKeyStatement.TryParse(KeyStatementJSON, out var signed, out var error))
                return new (ShareholderChangeStatus.Invalid, $"It is no key statement: {error}");

            lock (poiLock)
            {

                if (!shareholderChanges.TryGetValue((RoamingNetworkId, Holder), out var change))
                    return new (ShareholderChangeStatus.NotFound, $"No change of '{Holder}' waits in the roaming network '{RoamingNetworkId}'.");

                if (change.ChangeSetId.HasValue)
                    return new (ShareholderChangeStatus.NothingToDo, $"The change set of it was made already: '{change.ChangeSetId}'.");

                if (!signed.GetContentBytes().SequenceEqual(change.KeyStatement.GetContentBytes()))
                    return new (ShareholderChangeStatus.Invalid, "It is another key statement than the one waiting.");

                // Signed by identity keys the statement names, or the holders before name: each verified.
                var keys   = signed.IdentityKeys.Concat(history.Head.RoamingNetwork.Operator!.IdentityKeys).ToArray();
                var added  = 0;
                var all    = change.KeyStatement.Signatures.ToList();

                foreach (var signature in signed.Signatures.Where(signature => all.All(known => known.KeyId != signature.KeyId)))
                {

                    if (keys.FirstOrDefault(key => key.Id == signature.KeyId) is not POIPublicKey identityKey ||
                        !signed.VerifySignature(identityKey, out _))
                    {
                        return new (ShareholderChangeStatus.Invalid, $"The signature by '{signature.KeyId}' is none of an identity key of a holder.");
                    }

                    all.Add(signature);
                    added++;

                }

                if (added == 0)
                    return new (ShareholderChangeStatus.NothingToDo, "It has no signature the key statement waiting has not.");

                change.KeyStatement = WithSignatures(change.KeyStatement, all);

                return new (ShareholderChangeStatus.Waiting, $"{added} signature(s) of the key statement taken.");

            }

        }

        #endregion


        #region JoinRoamingNetworkOperator(RoamingNetworkId, URL = null)

        /// <summary>
        /// Ask to hold the keys of the operator of a roaming network this hub follows, or go on with it: sign the
        /// change set admitting this hub, once those who hold them made it.
        /// </summary>
        /// <param name="RoamingNetworkId">A roaming network this hub follows.</param>
        /// <param name="URL">Where this hub serves its copy, ~/RNs/{RN_Id} over https, to be named by the roaming network; none otherwise.</param>
        public async Task<ShareholderChangeResult> JoinRoamingNetworkOperator(RoamingNetwork_Id  RoamingNetworkId,
                                                                              URL?               URL   = null)
        {

            if (!TryGetFollowed(RoamingNetworkId, out var network, out var history, out var notFollowed))
                return notFollowed;

            var head    = history.Head.RoamingNetwork;
            var holder  = NetworkOperator.Holder;

            if (holder is null)
                return new (ShareholderChangeStatus.Invalid, $"The keys of this hub name no holder: '{CommandLine.RoamingNetworkOperatorCommand.CommandName} holder <name>' names one.");

            if (NetworkOperator.SigningKeyIdIn(head) is not null)
                return new (ShareholderChangeStatus.NothingToDo, $"This hub, '{holder}', holds keys of the operator of the roaming network '{RoamingNetworkId}' already.");

            try
            {

                var waiting = await GetChange(network, holder);

                #region Its change set waits: signed with the identity key, the proof that this hub holds it and asked

                if (waiting?.ChangeSetId is ChangeSet_Id changeSetId)
                {

                    if (await GetPendingChangeSet(network, changeSetId) is not RoamingNetworkChangeSet changeSet)
                        return new (ShareholderChangeStatus.NotFound, $"The change set '{changeSetId}' admitting this hub waits no longer at '{network.URL}'.");

                    // What it admits is what this hub asked for, and nothing else.
                    if (waiting.IdentityKey?.Id != NetworkOperator.IdentityKey.Id ||
                        ChangeSetOf(waiting, head, changeSet)?.Id != changeSet.Id)
                    {
                        return new (ShareholderChangeStatus.Invalid, $"The change set '{changeSetId}' at '{network.URL}' is not the one admitting this hub as it asked: it is not signed.");
                    }

                    return await SendChangeSetSignatures(network, changeSet, NetworkOperator.SignAsShareholder(changeSet, POIKeyKind.Identity), $"admitting '{holder}'");

                }

                #endregion

                if (waiting is not null)
                    return new (ShareholderChangeStatus.Waiting, $"'{holder}' asked to hold the keys of the operator of the roaming network '{RoamingNetworkId}' already: it waits for those who hold them to admit it.");

                #region Ask

                if (head.Operator?.KeyStatement is null)
                    return new (ShareholderChangeStatus.Invalid, $"The operator of the roaming network '{RoamingNetworkId}' states no keys.");

                var now      = TimeProvider.GetUtcNow();
                var holders  = POIPartyKeys.Holders(head.Operator.IdentityKeys).Count + 1;

                var request  = new ShareholderChange(ShareholderChangeKind.Admit,
                                                     RoamingNetworkId,
                                                     holder,
                                                     NetworkOperator.StateKeysOf(RoamingNetworkId,
                                                                                 head.Operator.Id,
                                                                                 head.Operator.IdentityKeys.Append(NetworkOperator.IdentityKey),
                                                                                 (Byte) (holders / 2 + 1),
                                                                                 now,
                                                                                 KeyStatementValidity),
                                                     NetworkOperator.IdentityKey,
                                                     NetworkOperator.CertifySigningKeyFor(head.Operator.Id, now),
                                                     URL);

                if (ChangeError(request, head) is String changeError)
                    return new (ShareholderChangeStatus.Invalid, changeError);

                var (status, answer) = await Post(ShareholdersURL(network) + "/requests", request.ToJSON());

                return status is >= 200 and < 300
                           ? new (ShareholderChangeStatus.Waiting,
                                  $"This hub, '{holder}', asked to hold the keys of the operator '{head.Operator.Id}' of the roaming network '{RoamingNetworkId}' as well, " +
                                  $"{holders / 2 + 1} of {holders} deciding. It waits for those who hold them to admit it; '{CommandLine.RoamingNetworksCommand.CommandName} {RoamingNetworkId} join' goes on then.")
                           : new (ShareholderChangeStatus.Refused, $"'{network.URL}' did not take it: {Description(answer)}");

                #endregion

            }
            catch (Exception e)
            {
                return new (ShareholderChangeStatus.Refused, $"'{network.URL}' could not be asked: {e.Message}");
            }

        }

        #endregion

        #region AdmitShareholder(RoamingNetworkId, Holder) / DismissShareholder(RoamingNetworkId, Holder)

        /// <summary>
        /// Admit the given holder, which asked to hold the keys of the operator of the given roaming network: as far as
        /// this hub can right now - sign the key statement of the holders afterwards, make the change set of it once it
        /// is signed, at the hub where the roaming network is made, or sign that change set.
        /// </summary>
        public Task<ShareholderChangeResult> AdmitShareholder(RoamingNetwork_Id  RoamingNetworkId,
                                                              String             Holder)

            => ChangeShareholders(ShareholderChangeKind.Admit, RoamingNetworkId, Holder);

        /// <summary>
        /// Dismiss the given holder of the keys of the operator of the given roaming network, or resign as it: as far
        /// as this hub can right now. Begun at the hub where the roaming network is made, by one who stays.
        /// </summary>
        public Task<ShareholderChangeResult> DismissShareholder(RoamingNetwork_Id  RoamingNetworkId,
                                                                String             Holder)

            => ChangeShareholders(ShareholderChangeKind.Dismiss, RoamingNetworkId, Holder);

        /// <summary>
        /// State the keys of the operator of the given roaming network anew, together with the other holders
        /// (SHAREHOLDERS.md, phase 5): the same identity keys and the same quorum, valid from now, as far as this hub can
        /// right now. Begun at the hub where the roaming network is made; where one holder decides, that hub does it by
        /// itself before the key statement ends.
        /// </summary>
        public Task<ShareholderChangeResult> RestateOperatorKeys(RoamingNetwork_Id RoamingNetworkId)

            => ChangeShareholders(ShareholderChangeKind.Restate, RoamingNetworkId, "");

        #endregion


        #region RotateNetworkOperatorKeyEverywhere(Kind)

        /// <summary>
        /// Replace the current signing key or identity key of this hub by a new one: in every roaming network it makes
        /// (RotateNetworkOperatorKey), and, a signing key, in every roaming network it follows whose operator it holds
        /// keys of as well - there a holder renews its own signing key with its identity key, alone, by a change set
        /// of its certificate sent where the roaming network is followed. An identity key is not replaced where
        /// several decide.
        /// </summary>
        /// <param name="Kind">Which of its keys.</param>
        public async Task<RoamingNetworkOperatorUpdate> RotateNetworkOperatorKeyEverywhere(POIKeyKind Kind)
        {

            var oldKeyId  = (Kind == POIKeyKind.Signing ? NetworkOperator.SigningKey : NetworkOperator.IdentityKey).Id;
            var update    = RotateNetworkOperatorKey(Kind);
            var changed   = update.Changed.ToList();
            var refused   = update.Refused.ToDictionary(entry => entry.Key, entry => entry.Value);
            var now       = TimeProvider.GetUtcNow();

            foreach (var network in FollowedRoamingNetworks)
            {

                if (network.History?.Head.RoamingNetwork is not RoamingNetwork head || head.Operator is not RoamingNetworkOperator networkOperator)
                    continue;

                // Only where it names the key replaced: this hub holds it of that operator.
                if (!(Kind == POIKeyKind.Signing ? networkOperator.SigningKeys : networkOperator.IdentityKeys).Any(key => key.Id == oldKeyId))
                    continue;

                if (Kind == POIKeyKind.Identity)
                {
                    refused[network.Id] = "this hub holds the keys of its operator with others: an identity key is replaced by them together, which is not built";
                    continue;
                }

                try
                {

                    var changeSet = NetworkOperator.SignAsShareholder(
                                        head.CreateChangeSet(
                                            [
                                                RoamingNetworkChange.UpdateProperty(networkOperator,
                                                                                    RoamingNetworkSigningKeyCertificate.JSONProperty,
                                                                                    null,
                                                                                    new JArray(networkOperator.SigningKeyCertificates.
                                                                                                   Where (certificate => certificate.SigningKey.Id != oldKeyId).
                                                                                                   Append(NetworkOperator.CertifySigningKeyFor(networkOperator.Id, now)).
                                                                                                   Select(certificate => certificate.ToJSON(Embedded: true))))
                                            ],
                                            Description: ImmutableI18NString.Create(Languages.en, $"'{NetworkOperator.Holder}' renews its signing key as a holder of the keys of the roaming network operator")
                                        ),
                                        POIKeyKind.Identity
                                    );

                    var (status, answer) = await Post($"{network.URL.ToString().TrimEnd('/')}/history/changeSets/pending", changeSet.ToJSON());

                    if (status is >= 200 and < 300 && answer?["status"]?.Value<String>() is "committed" or "alreadyCommitted")
                    {
                        changed.Add(network.Id);
                        Log.Notice($"The signing key of this hub, '{NetworkOperator.Holder}', as a holder of the keys of the operator of the roaming network '{network.Id}' is {NetworkOperator.SigningKey.Id[..16]}… now.", "poi", "shareholders", "security");
                    }
                    else
                        refused[network.Id] = $"'{network.URL}' did not take it: {Description(answer)}";

                }
                catch (Exception e)
                {
                    refused[network.Id] = e.Message;
                }

            }

            return changed.Count == update.Changed.Count && refused.Count == update.Refused.Count
                       ? update
                       : new (update.Message +
                              (changed.Count > update.Changed.Count
                                   ? $" Also in the roaming network(s) followed {String.Join(", ", changed.Skip(update.Changed.Count))}, where this hub holds keys of the operator."
                                   : "") +
                              (refused.Count > update.Refused.Count
                                   ? $" Not in the roaming network(s) followed {String.Join("; ", refused.Where(entry => !update.Refused.ContainsKey(entry.Key)).Select(entry => $"'{entry.Key}': {entry.Value}"))}."
                                   : ""),
                              changed,
                              refused);

        }

        #endregion

        #region (private) ChangeShareholders(Kind, RoamingNetworkId, Holder)

        private async Task<ShareholderChangeResult> ChangeShareholders(ShareholderChangeKind  Kind,
                                                                       RoamingNetwork_Id      RoamingNetworkId,
                                                                       String                 Holder)
        {

            var what     = Kind switch {
                               ShareholderChangeKind.Admit    => $"admitting '{Holder}'",
                               ShareholderChangeKind.Dismiss  => $"dismissing '{Holder}'",
                               _                              => "stating the keys of the operator anew"
                           };

            var command  = Kind switch {
                               ShareholderChangeKind.Admit    => $"admit {Holder}",
                               ShareholderChangeKind.Dismiss  => $"dismiss {Holder}",
                               _                              => "restate"
                           };

            #region At the hub where the roaming network is made

            if (!IsFollowed(RoamingNetworkId))
            {

                if (!TryGetOperated(RoamingNetworkId, out var history, out var notOperated))
                    return notOperated;

                var head = history.Head.RoamingNetwork;

                lock (networkOperatorLock)
                lock (poiLock)
                {

                    ForgetWhatIsDone(history);

                    shareholderChanges.TryGetValue((RoamingNetworkId, Holder), out var change);

                    #region A dismissal begins here: the key statement of those who stay

                    if (change is null && Kind == ShareholderChangeKind.Dismiss)
                    {

                        var staying = head.Operator!.IdentityKeys.Where(key => key.HolderOrUnnamed != Holder).ToArray();

                        if (staying.Length == head.Operator.IdentityKeys.Length)
                            return new (ShareholderChangeStatus.NotFound, $"'{Holder}' holds no keys of the operator of the roaming network '{RoamingNetworkId}'.");

                        if (Holder == NetworkOperator.Holder)
                            return new (ShareholderChangeStatus.Invalid, $"This hub makes the roaming network '{RoamingNetworkId}': it does not resign as a holder of the keys of its operator.");

                        var holders = POIPartyKeys.Holders(staying).Count;

                        change = new ShareholderChange(ShareholderChangeKind.Dismiss,
                                                       RoamingNetworkId,
                                                       Holder,
                                                       NetworkOperator.StateKeysOf(RoamingNetworkId,
                                                                                   head.Operator.Id,
                                                                                   staying,
                                                                                   (Byte) (holders / 2 + 1),
                                                                                   TimeProvider.GetUtcNow(),
                                                                                   KeyStatementValidity));

                        shareholderChanges[(RoamingNetworkId, Holder)] = change;

                    }

                    #endregion

                    #region Stating the keys anew begins here as well: the same keys, the same quorum, from now

                    if (change is null && Kind == ShareholderChangeKind.Restate)
                    {

                        change = new ShareholderChange(ShareholderChangeKind.Restate,
                                                       RoamingNetworkId,
                                                       "",
                                                       NetworkOperator.StateKeysOf(RoamingNetworkId,
                                                                                   head.Operator!.Id,
                                                                                   head.Operator.IdentityKeys,
                                                                                   head.Operator.MinConsensus,
                                                                                   TimeProvider.GetUtcNow(),
                                                                                   KeyStatementValidity));

                        shareholderChanges[(RoamingNetworkId, "")] = change;

                    }

                    #endregion

                    if (change is null || change.Kind != Kind)
                        return new (ShareholderChangeStatus.NotFound,
                                    Kind == ShareholderChangeKind.Admit
                                        ? $"'{Holder}' did not ask to hold the keys of the operator of the roaming network '{RoamingNetworkId}'."
                                        : $"'{Holder}' asked to be admitted to the roaming network '{RoamingNetworkId}': it is not dismissed.");

                    if (ChangeError(change, head) is String changeError)
                    {
                        shareholderChanges.Remove((RoamingNetworkId, Holder));
                        return new (ShareholderChangeStatus.Invalid, changeError + " It waits no longer.");
                    }

                    #region Its change set waits, or was committed

                    if (change.ChangeSetId is ChangeSet_Id changeSetId)
                    {

                        if (history.TryGetPendingChangeSet(changeSetId, out var pending) && pending.Status == RoamingNetworkCoSigningStatus.Pending)
                            return new (ShareholderChangeStatus.Waiting, $"The change set '{changeSetId}' {what} waits for signatures: {String.Join(" ", pending.Missing ?? [])}");

                        shareholderChanges.Remove((RoamingNetworkId, Holder));

                        return new (ShareholderChangeStatus.NothingToDo, $"The change set '{changeSetId}' {what} waits no longer.");

                    }

                    #endregion

                    // Signed by this hub, as one of those who decide.
                    if (change.KeyStatement.IdentityKeys.Any(key => key.Id == NetworkOperator.IdentityKey.Id) &&
                        change.KeyStatement.Signatures.  All(signature => signature.KeyId != NetworkOperator.IdentityKey.Id))
                    {
                        change.KeyStatement = NetworkOperator.CoSign(change.KeyStatement);
                    }

                    if (!IsSignedByItsQuorums(change, head, out var missing))
                        return new (ShareholderChangeStatus.Waiting,
                                    $"The key statement {what} is signed by this hub. It waits for the other holders: {missing} " +
                                    $"Each signs it with '{CommandLine.RoamingNetworksCommand.CommandName} {RoamingNetworkId} {command}' at its hub.");

                    #region The key statement is signed: the change set of it, signed by this hub, waits for the others

                    try
                    {

                        var changeSet  = SignedAsAHolder(ChangeSetOf(change, head)!, Kind);

                        var result     = history.CoSign(changeSet);

                        if (result.Status == RoamingNetworkCoSigningStatus.Committed || result.Status == RoamingNetworkCoSigningStatus.AlreadyCommitted)
                        {
                            shareholderChanges.Remove((RoamingNetworkId, Holder));
                            Log.Notice($"The roaming network '{RoamingNetworkId}': {what} is committed, revision {history.Head.Commit.Revision}.", "poi", "shareholders", "security");
                            return new (ShareholderChangeStatus.Committed, $"The roaming network '{RoamingNetworkId}' is at revision {history.Head.Commit.Revision}: {what} is committed.");
                        }

                        if (result.Status != RoamingNetworkCoSigningStatus.Pending)
                            return new (ShareholderChangeStatus.Invalid, $"The change set {what} was refused: {result.ErrorResponse}");

                        change.ChangeSetId = changeSet.Id;

                        Log.Notice($"The roaming network '{RoamingNetworkId}': the change set '{changeSet.Id}' {what} waits for signatures.", "poi", "shareholders", "security");

                        return new (ShareholderChangeStatus.Waiting,
                                    $"The change set '{changeSet.Id}' {what} is made and signed by this hub. It waits for signatures: {String.Join(" ", result.Missing ?? [])}");

                    }
                    catch (Exception e)
                    {
                        return new (ShareholderChangeStatus.Invalid, $"The change set {what} can not be made: {e.Message}");
                    }

                    #endregion

                }

            }

            #endregion

            #region At another holder's hub, which follows it

            if (!TryGetFollowed(RoamingNetworkId, out var network, out var copy, out var notFollowed))
                return notFollowed;

            var version = copy.Head.RoamingNetwork;

            if (NetworkOperator.SigningKeyIdIn(version) is null)
                return new (ShareholderChangeStatus.NothingToDo, $"This hub holds no key of the operator of the roaming network '{RoamingNetworkId}': it is none of those who decide.");

            try
            {

                if (await GetChange(network, Holder) is not ShareholderChange waiting || waiting.Kind != Kind)
                    return new (ShareholderChangeStatus.NotFound, $"Nothing {what} waits at '{network.URL}'.");

                if (ChangeError(waiting, version) is String changeError)
                    return new (ShareholderChangeStatus.Invalid, changeError);

                #region The key statement lacks the signature of this hub

                if (waiting.ChangeSetId is null)
                {

                    if (waiting.KeyStatement.Signatures.Any(signature => signature.KeyId == NetworkOperator.IdentityKey.Id))
                        return new (ShareholderChangeStatus.Waiting, $"The key statement {what} is signed by this hub already: it waits for the others, and for its change set to be made at '{network.URL}'.");

                    // The one leaving signs no key statement of those who stay: it names it no longer.
                    if (waiting.KeyStatement.IdentityKeys.All(key => key.Id != NetworkOperator.IdentityKey.Id))
                        return new (ShareholderChangeStatus.Waiting, $"The key statement {what} is one of those who stay: this hub signs the change set of it, once it is made at '{network.URL}'.");

                    var (status, answer) = await Post($"{ShareholdersURL(network)}/changes/{(Holder.Length > 0 ? Uri.EscapeDataString(Holder) : "-")}/keyStatement",
                                                      NetworkOperator.CoSign(waiting.KeyStatement).ToJSON(Embedded: true));

                    return status is >= 200 and < 300
                               ? new (ShareholderChangeStatus.Waiting, $"The key statement {what} is signed by this hub, and its signature was taken at '{network.URL}'. Its change set is made there next.")
                               : new (ShareholderChangeStatus.Refused, $"'{network.URL}' did not take the signature of the key statement: {Description(answer)}");

                }

                #endregion

                #region Its change set waits: signed as a holder before

                if (await GetPendingChangeSet(network, waiting.ChangeSetId.Value) is not RoamingNetworkChangeSet changeSet)
                    return new (ShareholderChangeStatus.NotFound, $"The change set '{waiting.ChangeSetId}' {what} waits no longer at '{network.URL}'.");

                if (ChangeSetOf(waiting, version, changeSet)?.Id != changeSet.Id)
                    return new (ShareholderChangeStatus.Invalid, $"The change set '{changeSet.Id}' at '{network.URL}' is not the one {what} with the key statement waiting: it is not signed.");

                if (changeSet.Signatures.Any(signature => signature.KeyId == NetworkOperator.IdentityKey.Id))
                    return new (ShareholderChangeStatus.Waiting, $"The change set '{changeSet.Id}' {what} is signed by this hub already: it waits for the others.");

                return await SendChangeSetSignatures(network,
                                                     changeSet,
                                                     SignedAsAHolder(changeSet, Kind),
                                                     what);

                #endregion

            }
            catch (Exception e)
            {
                return new (ShareholderChangeStatus.Refused, $"'{network.URL}' could not be asked: {e.Message}");
            }

            #endregion

        }

        #endregion


        #region (private) ForgetWhatIsDone(History)

        /// <summary>
        /// Forget the changes of the shareholders of the given roaming network whose change set waits no longer:
        /// committed by the last signature it needed, sent by another hub, or dropped. Locked by the caller.
        /// </summary>
        private void ForgetWhatIsDone(RoamingNetworkHistory History)
        {

            foreach (var (key, change) in shareholderChanges.ToArray())
                if (key.RoamingNetworkId == History.RoamingNetworkId   &&
                    change.ChangeSetId is ChangeSet_Id changeSetId      &&
                    !(History.TryGetPendingChangeSet(changeSetId, out var pending) && pending.Status == RoamingNetworkCoSigningStatus.Pending))
                {
                    shareholderChanges.Remove(key);
                }

        }

        #endregion

        #region (private static) ChangeError(Change, Head)

        /// <summary>
        /// Why the given change does not fit the given version of its roaming network, or null: the key statement is one
        /// of its operator, naming the identity keys so far with the new one, or without those of the one leaving, and a
        /// quorum of more than half of the holders afterwards; who asks is no holder yet, proved its identity key by
        /// signing the key statement, and certified its signing key with it.
        /// </summary>
        private static String? ChangeError(ShareholderChange  Change,
                                           RoamingNetwork     Head)
        {

            if (Head.Operator is not RoamingNetworkOperator networkOperator || networkOperator.KeyStatement is null)
                return $"The operator of the roaming network '{Head.Id}' states no keys.";

            var statement = Change.KeyStatement;

            if (statement.RoamingNetworkId != Head.Id || statement.RoamingNetworkOperatorId != networkOperator.Id)
                return $"The key statement is not one of the operator '{networkOperator.Id}' of the roaming network '{Head.Id}'.";

            var holdersBefore = POIPartyKeys.Holders(networkOperator.IdentityKeys);

            POIPublicKey[] expected;

            if (Change.Kind == ShareholderChangeKind.Admit)
            {

                if (Change.IdentityKey is not POIPublicKey identityKey || Change.SigningKeyCertificate is not RoamingNetworkSigningKeyCertificate certificate)
                    return "It names no identity key and no certificate of a signing key.";

                if (holdersBefore.Contains(Change.Holder))
                    return $"'{Change.Holder}' holds keys of the operator of the roaming network '{Head.Id}' already.";

                if (certificate.Holder != Change.Holder || certificate.RoamingNetworkOperatorId != networkOperator.Id ||
                    !certificate.TryVerify([ identityKey ], out _, out _))
                {
                    return $"The certificate of the signing key is not one of '{Change.Holder}' as a holder of the keys of '{networkOperator.Id}', by its identity key.";
                }

                // Its proof: it holds the identity key, and asked.
                if (!statement.VerifySignature(identityKey, out _))
                    return $"The key statement is not signed by the identity key of '{Change.Holder}': it did not ask.";

                expected = [ .. networkOperator.IdentityKeys, identityKey ];

            }
            else
            {

                if (Change.Kind == ShareholderChangeKind.Restate)
                {

                    // The same keys and the same quorum, lasting longer than the one it follows.
                    if (statement.MinConsensus != networkOperator.MinConsensus)
                        return $"The key statement states the minConsensus {statement.MinConsensus}, not the {networkOperator.MinConsensus} of the operator: stating its keys anew changes nothing.";

                    if (statement.ValidUntil <= networkOperator.KeyStatement.ValidUntil)
                        return "The key statement does not last longer than the one of the roaming network.";

                    return statement.IdentityKeys.Length == networkOperator.IdentityKeys.Length && networkOperator.IdentityKeys.All(key => statement.IdentityKeys.Contains(key))
                               ? null
                               : $"The key statement does not name the identity keys of the operator: the roaming network '{Head.Id}' names others by now.";

                }

                if (!holdersBefore.Contains(Change.Holder))
                    return $"'{Change.Holder}' holds no keys of the operator of the roaming network '{Head.Id}'.";

                expected = [ .. networkOperator.IdentityKeys.Where(key => key.HolderOrUnnamed != Change.Holder) ];

                if (expected.Length == 0)
                    return $"'{Change.Holder}' is the last one holding keys of the operator of the roaming network '{Head.Id}'.";

            }

            if (statement.IdentityKeys.Length != expected.Length || expected.Any(key => !statement.IdentityKeys.Contains(key)))
                return $"The key statement does not name the identity keys of the holders afterwards: the roaming network '{Head.Id}' names others by now.";

            var holdersAfter = POIPartyKeys.Holders(expected).Count;

            if (statement.MinConsensus > holdersAfter || 2 * statement.MinConsensus <= holdersAfter)
                return $"The key statement states the minConsensus {statement.MinConsensus}, which is not more than half of the {holdersAfter} holders afterwards.";

            return null;

        }

        #endregion

        #region (private static) IsSignedByItsQuorums(Change, Head, out Missing)

        /// <summary>
        /// Whether the key statement of the given change is signed as the one following the one of the given version:
        /// by identity keys of as many holders before as their quorum requires, and by its own quorum
        /// (RoamingNetworkKeyStatement.TryVerifyChain). One holder staying alone states its keys alone: the one leaving
        /// signs no statement that names it no longer, and whoever follows the roaming network needs its new trust
        /// anchor then.
        /// </summary>
        private static Boolean IsSignedByItsQuorums(ShareholderChange  Change,
                                                    RoamingNetwork     Head,
                                                    out String?        Missing)
        {

            var before = Head.Operator!.KeyStatement!;

            if (RoamingNetworkKeyStatement.TryVerifyChain([ before, Change.KeyStatement ],
                                                          before.Holders,
                                                          out _,
                                                          out _,
                                                          out Missing,
                                                          before.MinConsensus,
                                                          OtherIdentityKeys: Head.Operator.IdentityKeys))
            {
                return true;
            }

            if (Change.Kind == ShareholderChangeKind.Dismiss                         &&
                POIPartyKeys.Holders(Change.KeyStatement.IdentityKeys).Count == 1     &&
                Change.KeyStatement.Signatures.Any(signature => Change.KeyStatement.IdentityKeys.Any(key => key.Id == signature.KeyId)))
            {
                Missing = null;
                return true;
            }

            return false;

        }

        #endregion

        #region (private static) ChangeSetOf(Change, Head, Like = null)

        /// <summary>
        /// The one change set of the given change against the given version: made now, or as the given one was made, to
        /// see whether that one is it.
        /// </summary>
        private static RoamingNetworkChangeSet? ChangeSetOf(ShareholderChange         Change,
                                                            RoamingNetwork            Head,
                                                            RoamingNetworkChangeSet?  Like   = null)
        {

            try
            {

                var description = Like?.Description ?? ImmutableI18NString.Create(Languages.en,
                                                           Change.Kind switch {
                                                               ShareholderChangeKind.Admit    => $"'{Change.Holder}' holds the keys of the roaming network operator as well",
                                                               ShareholderChangeKind.Dismiss  => $"'{Change.Holder}' holds the keys of the roaming network operator no longer",
                                                               _                              => "The keys of the roaming network operator stated anew by their holders"
                                                           });

                return Change.Kind switch {

                           ShareholderChangeKind.Admit    => Head.Admit  (Change.IdentityKey!, Change.SigningKeyCertificate!, Change.KeyStatement, Change.URL, description, Like?.Metadata, Like?.CreatedAt),
                           ShareholderChangeKind.Dismiss  => Head.Dismiss(Change.Holder,                                      Change.KeyStatement, null,       description, Like?.Metadata, Like?.CreatedAt),

                           _                              => Head.CreateChangeSet([ RoamingNetworkChange.UpdateProperty(Head.Operator!,
                                                                                                                        RoamingNetworkKeyStatement.JSONProperty,
                                                                                                                        null,
                                                                                                                        Change.KeyStatement.ToJSON(Embedded: true)) ],
                                                                                  description,
                                                                                  Like?.Metadata,
                                                                                  Like?.CreatedAt)

                       };

            }
            catch when (Like is not null)
            {
                return null;
            }

        }

        #endregion

        #region (private) SignedAsAHolder(ChangeSet, Kind)

        /// <summary>
        /// The given change set signed by this hub as one of the holders before: with its identity key and its signing
        /// key where who holds the keys changes, with its signing key where they are stated anew, which is data.
        /// </summary>
        private RoamingNetworkChangeSet SignedAsAHolder(RoamingNetworkChangeSet  ChangeSet,
                                                        ShareholderChangeKind    Kind)

            => Kind == ShareholderChangeKind.Restate
                   ? NetworkOperator.SignAsShareholder(ChangeSet, POIKeyKind.Signing)
                   : NetworkOperator.SignAsShareholder(NetworkOperator.SignAsShareholder(ChangeSet, POIKeyKind.Identity), POIKeyKind.Signing);

        #endregion

        #region (private static) WithSignatures(KeyStatement, Signatures)

        private static RoamingNetworkKeyStatement WithSignatures(RoamingNetworkKeyStatement                        KeyStatement,
                                                                 IEnumerable<RoamingNetworkKeyStatementSignature>  Signatures)

            => new (KeyStatement.RoamingNetworkId,
                    KeyStatement.RoamingNetworkOperatorId,
                    KeyStatement.IdentityKeys,
                    KeyStatement.MinConsensus,
                    KeyStatement.SignedAt,
                    KeyStatement.ValidUntil,
                    Signatures);

        #endregion

        #region (private) TryGetOperated(RoamingNetworkId, out History, out Result) / TryGetFollowed(RoamingNetworkId, out Network, out History, out Result)

        private Boolean TryGetOperated(RoamingNetwork_Id                                 RoamingNetworkId,
                                       [NotNullWhen(true)]  out RoamingNetworkHistory?    History,
                                       [NotNullWhen(false)] out ShareholderChangeResult?  Result)
        {

            Result = null;

            if (IsFollowed(RoamingNetworkId) ||
                !POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out History) ||
                !NetworkOperator.Operates(History.Head.RoamingNetwork))
            {
                History  = null;
                Result   = new (ShareholderChangeStatus.NotFound, $"This hub makes no roaming network '{RoamingNetworkId}' whose operator it holds keys of.");
                return false;
            }

            return true;

        }

        private Boolean TryGetFollowed(RoamingNetwork_Id                                 RoamingNetworkId,
                                       [NotNullWhen(true)]  out FollowedRoamingNetwork?   Network,
                                       [NotNullWhen(true)]  out RoamingNetworkHistory?    History,
                                       [NotNullWhen(false)] out ShareholderChangeResult?  Result)
        {

            lock (poiLock)
                followed.TryGetValue(RoamingNetworkId, out Network);

            History  = Network?.History;
            Result   = null;

            if (Network is null || History is null)
            {
                Result = new (ShareholderChangeStatus.NotFound,
                              Network is null
                                  ? $"This hub neither makes nor follows a roaming network '{RoamingNetworkId}'."
                                  : $"The roaming network '{RoamingNetworkId}' has no copy here yet.");
                return false;
            }

            return true;

        }

        #endregion

        #region (private) Between hubs: ShareholdersURL / GetChange / GetPendingChangeSet / SendChangeSetSignatures / Post / Description

        /// <summary>
        /// Where the hub a roaming network is followed at tells and takes the changes of its shareholders: below its
        /// JSON API, beside its POI API.
        /// </summary>
        private static String ShareholdersURL(FollowedRoamingNetwork Network)
        {

            var url     = Network.URL.ToString().TrimEnd('/');
            var suffix  = $"/{POIPathSegment}/RNs/{Network.Id}";

            return url.EndsWith(suffix, StringComparison.Ordinal)
                       ? $"{url[..^suffix.Length]}/api/v1/{POIPathSegment}/shareholders/{Network.Id}"
                       : throw new InvalidOperationException($"'{url}' is no roaming network at a hub: its shareholders are not told there.");

        }

        private static async Task<ShareholderChange?> GetChange(FollowedRoamingNetwork  Network,
                                                                String                  Holder)
        {

            using var response = await shareholderHTTP.GetAsync(ShareholdersURL(Network));

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"it does not tell the shareholders of the roaming network '{Network.Id}' ({(Int32) response.StatusCode})");

            foreach (var changeJSON in ReadJSON(await response.Content.ReadAsStringAsync())?["changes"]?.OfType<JObject>() ?? [])
                if (changeJSON["holder"]?.Value<String>() == Holder &&
                    ShareholderChange.TryParse(changeJSON, Network.Id, out var change, out _))
                {
                    return change;
                }

            return null;

        }

        private static async Task<RoamingNetworkChangeSet?> GetPendingChangeSet(FollowedRoamingNetwork  Network,
                                                                                ChangeSet_Id            ChangeSetId)
        {

            using var response = await shareholderHTTP.GetAsync($"{Network.URL.ToString().TrimEnd('/')}/history/changeSets/pending/{ChangeSetId}");

            return response.IsSuccessStatusCode &&
                   ReadJSON(await response.Content.ReadAsStringAsync())?["changeSet"] is JObject changeSetJSON &&
                   RoamingNetworkChangeSet.TryParse(changeSetJSON, out var changeSet, out _)
                       ? changeSet
                       : null;

        }

        /// <summary>
        /// Send the signatures the given signed change set has more than the given one to where it waits.
        /// </summary>
        private async Task<ShareholderChangeResult> SendChangeSetSignatures(FollowedRoamingNetwork   Network,
                                                                            RoamingNetworkChangeSet  ChangeSet,
                                                                            RoamingNetworkChangeSet  Signed,
                                                                            String                   What)
        {

            var signatures       = Signed.Signatures.Where(signature => ChangeSet.Signatures.All(known => known.KeyId != signature.KeyId)).ToArray();

            var (status, answer) = await Post($"{Network.URL.ToString().TrimEnd('/')}/history/changeSets/pending/{ChangeSet.Id}/signatures",
                                              new JObject(new JProperty("signatures", new JArray(signatures.Select(signature => signature.ToJSON(Embedded: true))))));

            if (status is < 200 or >= 300)
                return new (ShareholderChangeStatus.Refused, $"'{Network.URL}' did not take the signature of the change set '{ChangeSet.Id}' {What}: {Description(answer)}");

            Log.Notice($"The change set '{ChangeSet.Id}' {What} in the roaming network '{Network.Id}' was signed as '{NetworkOperator.Holder}'.", "poi", "shareholders", "security");

            return answer?["status"]?.Value<String>() is "committed" or "alreadyCommitted"
                       ? new (ShareholderChangeStatus.Committed, $"The change set '{ChangeSet.Id}' {What} is signed by this hub, and committed at '{Network.URL}'.")
                       : new (ShareholderChangeStatus.Waiting,   $"The change set '{ChangeSet.Id}' {What} is signed by this hub. It waits for signatures: {String.Join(" ", answer?["missing"]?.Values<String>() ?? [])}");

        }

        private static async Task<(Int32 Status, JObject? Answer)> Post(String URL, JObject Body)
        {

            using var response = await shareholderHTTP.PostAsync(URL, new StringContent(Body.ToString(), Encoding.UTF8, "application/json"));

            var text = await response.Content.ReadAsStringAsync();

            return ((Int32) response.StatusCode, ReadJSON(text) ?? new JObject(new JProperty("description", text)));

        }

        /// <summary>
        /// Read what another hub sent as the text it is: a timestamp is signed as it is written, and stays a text.
        /// </summary>
        internal static JObject? ReadJSON(String Text)
        {

            try
            {
                return Text.Length > 0 ? POIJSON.Read(Text) as JObject : null;
            }
            catch
            {
                return null;
            }

        }

        private static String Description(JObject? Answer)

            => Answer?["description"]?.Value<String>() ?? Answer?["message"]?.Value<String>() ?? Answer?["error"]?.Value<String>() ?? Answer?.ToString(Newtonsoft.Json.Formatting.None) ?? "it did not say why";

        #endregion

    }

}
