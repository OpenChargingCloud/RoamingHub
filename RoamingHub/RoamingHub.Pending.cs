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

using System.Text;

using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// Operating a roaming network under a quorum (SHAREHOLDERS.md, phase 5): what its operator owns - a party
    /// added, a certificate, its name - is changed by as many holders of its keys as decide. A change set one of them
    /// made waits for the others; each of them looks at it, and approves it with its keys or refuses it.
    /// </summary>
    /// <remarks>
    /// At the hub where the roaming network is made, the change sets waiting are those of its history; at another
    /// holder's hub, which follows it, they are read there and the signatures sent there. Approving signs with the
    /// signing key of this hub, and with its identity key where a change needs identity keys; refusing withdraws the
    /// change set, signed by this hub as an owner of one of its changes. Neither happens by itself.
    /// </remarks>
    public partial class RoamingHub
    {

        #region PendingChangeSetsOf(RoamingNetworkId)

        /// <summary>
        /// The change sets of the given roaming network waiting for signatures, [ {"status", "changeSet", "missing"} ],
        /// where it is made or, for one followed, as the server followed tells them; null where this hub serves no such
        /// roaming network or the server followed could not be asked.
        /// </summary>
        public async Task<JArray?> PendingChangeSetsOf(RoamingNetwork_Id RoamingNetworkId)
        {

            if (!IsFollowed(RoamingNetworkId))
                return POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out var history)
                           ? new JArray(history.PendingChangeSets.
                                                Select(changeSet => history.TryGetPendingChangeSet(changeSet.Id, out var waiting) ? waiting.ToJSON() : null).
                                                OfType<JObject>())
                           : null;

            if (!TryGetFollowed(RoamingNetworkId, out var network, out _, out _))
                return null;

            try
            {

                using var response = await shareholderHTTP.GetAsync($"{network.URL.ToString().TrimEnd('/')}/history/changeSets/pending");

                return response.IsSuccessStatusCode
                           ? ReadJSON(await response.Content.ReadAsStringAsync())?["pendingChangeSets"] as JArray ?? []
                           : null;

            }
            catch
            {
                return null;
            }

        }

        #endregion

        #region ApprovePendingChangeSet(RoamingNetworkId, ChangeSetId) / RefusePendingChangeSet(RoamingNetworkId, ChangeSetId)

        /// <summary>
        /// Sign the given change set waiting for signatures as one of the holders of the keys of the operator of its
        /// roaming network: with the signing key of this hub, and with its identity key where a change needs identity
        /// keys. Committed once as many holders signed as decide.
        /// </summary>
        public Task<ShareholderChangeResult> ApprovePendingChangeSet(RoamingNetwork_Id  RoamingNetworkId,
                                                                     ChangeSet_Id       ChangeSetId)

            => DecidePendingChangeSet(RoamingNetworkId, ChangeSetId, true);

        /// <summary>
        /// Withdraw the given change set waiting for signatures, as one of the holders of the keys of the operator of
        /// its roaming network, which owns one of its changes: it waits no longer.
        /// </summary>
        public Task<ShareholderChangeResult> RefusePendingChangeSet(RoamingNetwork_Id  RoamingNetworkId,
                                                                    ChangeSet_Id       ChangeSetId)

            => DecidePendingChangeSet(RoamingNetworkId, ChangeSetId, false);

        #endregion

        #region (private) DecidePendingChangeSet(RoamingNetworkId, ChangeSetId, Approve)

        private async Task<ShareholderChangeResult> DecidePendingChangeSet(RoamingNetwork_Id  RoamingNetworkId,
                                                                           ChangeSet_Id       ChangeSetId,
                                                                           Boolean            Approve)
        {

            var isFollowed = IsFollowed(RoamingNetworkId);

            RoamingNetworkHistory?    history;
            FollowedRoamingNetwork?   network  = null;

            if (isFollowed)
            {
                if (!TryGetFollowed(RoamingNetworkId, out network, out history, out var notFollowed))
                    return notFollowed;
            }
            else if (!POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out history))
                return new (ShareholderChangeStatus.NotFound, $"This hub neither makes nor follows a roaming network '{RoamingNetworkId}'.");

            if (NetworkOperator.SigningKeyIdIn(history.Head.RoamingNetwork) is not String signingKeyId)
                return new (ShareholderChangeStatus.NothingToDo, $"This hub holds no key of the operator of the roaming network '{RoamingNetworkId}': it is none of those who decide.");

            try
            {

                #region The change set, and what it waits for

                RoamingNetworkChangeSet?  changeSet  = null;
                IReadOnlyList<String>     missing    = [];

                if (!isFollowed)
                {
                    if (history.TryGetPendingChangeSet(ChangeSetId, out var waiting) && waiting.Status == RoamingNetworkCoSigningStatus.Pending)
                    {
                        changeSet  = waiting.ChangeSet;
                        missing    = waiting.Missing ?? [];
                    }
                }
                else
                {

                    using var response = await shareholderHTTP.GetAsync($"{network!.URL.ToString().TrimEnd('/')}/history/changeSets/pending/{ChangeSetId}");

                    if (response.IsSuccessStatusCode &&
                        ReadJSON(await response.Content.ReadAsStringAsync()) is JObject json &&
                        json["changeSet"] is JObject changeSetJSON &&
                        RoamingNetworkChangeSet.TryParse(changeSetJSON, out changeSet, out _))
                    {
                        missing = [ .. json["missing"]?.Values<String>().OfType<String>() ?? [] ];
                    }

                }

                if (changeSet is null)
                    return new (ShareholderChangeStatus.NotFound, $"No change set '{ChangeSetId}' waits for signatures in the roaming network '{RoamingNetworkId}'.");

                #endregion

                #region Refused: withdrawn, signed by this hub

                if (!Approve)
                {

                    var withdrawal = NetworkOperator.WithdrawAsShareholder(changeSet, TimeProvider.GetUtcNow());

                    if (!isFollowed)
                    {

                        var withdrawn = history.Withdraw(withdrawal);

                        return withdrawn.Status == RoamingNetworkCoSigningStatus.Withdrawn
                                   ? Withdrawn()
                                   : new (ShareholderChangeStatus.Invalid, $"The change set '{ChangeSetId}' was not withdrawn: {withdrawn.ErrorResponse}");

                    }

                    using var request  = new HttpRequestMessage(HttpMethod.Delete, $"{network!.URL.ToString().TrimEnd('/')}/history/changeSets/pending/{ChangeSetId}") {
                                             Content = new StringContent(withdrawal.ToJSON().ToString(), Encoding.UTF8, "application/json")
                                         };

                    using var answer   = await shareholderHTTP.SendAsync(request);

                    return answer.IsSuccessStatusCode
                               ? Withdrawn()
                               : new (ShareholderChangeStatus.Refused, $"'{network.URL}' did not take the withdrawal of the change set '{ChangeSetId}': {Description(ReadJSON(await answer.Content.ReadAsStringAsync()))}");

                    ShareholderChangeResult Withdrawn()
                    {

                        Log.Notice($"The change set '{ChangeSetId}' of the roaming network '{RoamingNetworkId}' was withdrawn as '{NetworkOperator.Holder}'.", "poi", "shareholders", "security");

                        return new (ShareholderChangeStatus.Committed, $"The change set '{ChangeSetId}' of the roaming network '{RoamingNetworkId}' is withdrawn: it waits no longer.");

                    }

                }

                #endregion

                #region Approved: signed with the keys it waits for

                var signed = changeSet;

                if (changeSet.Signatures.All(signature => signature.KeyId != signingKeyId))
                    signed = NetworkOperator.SignAsShareholder(signed, POIKeyKind.Signing);

                if (missing.Any(what => what.Contains("identity key", StringComparison.Ordinal)) &&
                    changeSet.Signatures.All(signature => signature.KeyId != NetworkOperator.IdentityKey.Id))
                {
                    signed = NetworkOperator.SignAsShareholder(signed, POIKeyKind.Identity);
                }

                if (signed.Signatures.Length == changeSet.Signatures.Length)
                    return new (ShareholderChangeStatus.NothingToDo, $"The change set '{ChangeSetId}' is signed by this hub already. It waits for signatures: {String.Join(" ", missing)}");

                if (isFollowed)
                    return await SendChangeSetSignatures(network!, changeSet, signed, "of its operator");

                var result = history.AddPendingSignatures(ChangeSetId, signed.Signatures.Where(signature => changeSet.Signatures.All(known => known.KeyId != signature.KeyId)));

                if (result.IsCommitted)
                    Log.Notice($"The change set '{ChangeSetId}' of the roaming network '{RoamingNetworkId}' was signed as '{NetworkOperator.Holder}', and is committed.", "poi", "shareholders", "security");

                return result.Status switch {
                           RoamingNetworkCoSigningStatus.Committed or
                           RoamingNetworkCoSigningStatus.AlreadyCommitted  => new (ShareholderChangeStatus.Committed, $"The change set '{ChangeSetId}' is signed by this hub, and committed: the roaming network '{RoamingNetworkId}' is at revision {history.Head.Commit.Revision}."),
                           RoamingNetworkCoSigningStatus.Pending           => new (ShareholderChangeStatus.Waiting,   $"The change set '{ChangeSetId}' is signed by this hub. It waits for signatures: {String.Join(" ", result.Missing ?? [])}"),
                           _                                               => new (ShareholderChangeStatus.Invalid,   $"The signature of the change set '{ChangeSetId}' was not taken: {result.ErrorResponse}")
                       };

                #endregion

            }
            catch (Exception e)
            {
                return new (ShareholderChangeStatus.Refused, $"The change set '{ChangeSetId}' could not be {(Approve ? "signed" : "withdrawn")}: {e.Message}");
            }

        }

        #endregion

    }

}
