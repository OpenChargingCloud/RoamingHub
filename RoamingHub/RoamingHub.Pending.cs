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
    /// <para>
    /// The change sets waiting are those of the history here, at every holder's hub (SHAREHOLDERS.md, phase 7.2):
    /// where the roaming network was made, and in the copy of a hub following it that holds a key of its operator. A
    /// hub that only follows reads them where it follows. Approving signs with the signing key of this hub, and with
    /// its identity key where a change needs identity keys; refusing withdraws the change set, signed by this hub as
    /// an owner of one of its changes. Neither happens by itself.
    /// </para>
    /// <para>
    /// The holders' hubs exchange what waits, each with each it is connected with (<see cref="ExchangePending"/>):
    /// a change set waiting there is taken here as if it was submitted here, with the signatures it has there, and
    /// one waiting here is sent there where it lacks it or signatures of it; a change set withdrawn is withdrawn at
    /// the other one as well, by its withdrawal, which is signed. A change set that has the signatures it needs is
    /// committed by the hub where it got the last one, and two hubs getting there at once make the same commit.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// How many withdrawals of change sets this hub keeps per roaming network, to pass them on.
        /// </summary>
        public const Int32 MaxWithdrawalsKept = 100;

        /// <summary>
        /// How long a server that did not answer is not asked again, while this hub is not connected with it.
        /// </summary>
        public static readonly TimeSpan  AwayFor  = TimeSpan.FromSeconds(30);

        // The servers that did not answer, by their URLs, and when.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<String, DateTimeOffset>  unreached  = new (StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the server at the given URL is away, as far as this hub knows: it did not answer within the last
        /// <see cref="AwayFor"/>, and its peer is not connected. Connected again, it is asked at once.
        /// </summary>
        private Boolean IsAway(String URL, RoamingNetworkPeer Peer)
        {

            if (Peer.IsConnected)
            {
                unreached.TryRemove(URL, out _);
                return false;
            }

            return unreached.TryGetValue(URL, out var since) && TimeProvider.GetUtcNow() - since < AwayFor;

        }

        /// <summary>
        /// The withdrawals of change sets this hub knows, beside the store of a roaming network.
        /// </summary>
        public const String WithdrawalsFileName = "withdrawals.json";

        // The withdrawals made or taken here, by roaming network: passed on to the other holders' hubs, and kept
        // beside its store, so that a hub that was away gets them also after this one started again.
        private readonly Dictionary<RoamingNetwork_Id, List<RoamingNetworkChangeSetWithdrawal>>  withdrawals  = [];

        #endregion

        #region WithdrawalsOf(RoamingNetworkId) / (private) KeepWithdrawal(Withdrawal)

        /// <summary>
        /// The withdrawals of change sets of the given roaming network this hub knows, the latest
        /// <see cref="MaxWithdrawalsKept"/>: made here by somebody refusing a change set, or taken from another
        /// holder's hub, and kept over a restart. Each is signed by an owner of a change of its change set, so they
        /// are told to everybody.
        /// </summary>
        public IReadOnlyList<RoamingNetworkChangeSetWithdrawal> WithdrawalsOf(RoamingNetwork_Id RoamingNetworkId)
        {
            lock (withdrawals)
                return withdrawals.TryGetValue(RoamingNetworkId, out var kept) ? [ .. kept ] : [];
        }

        private void KeepWithdrawal(RoamingNetworkChangeSetWithdrawal Withdrawal)
        {
            lock (withdrawals)
            {

                if (!withdrawals.TryGetValue(Withdrawal.RoamingNetworkId, out var kept))
                    withdrawals[Withdrawal.RoamingNetworkId] = kept = [];

                if (kept.Any(known => known.ChangeSetId == Withdrawal.ChangeSetId))
                    return;

                kept.Add(Withdrawal);

                if (kept.Count > MaxWithdrawalsKept)
                    kept.RemoveRange(0, kept.Count - MaxWithdrawalsKept);

                WriteWithdrawals(Withdrawal.RoamingNetworkId, kept);

            }
        }

        #endregion

        #region (private) WriteWithdrawals(RoamingNetworkId, Kept) / ReadWithdrawals(RoamingNetworkId, Directory)

        /// <summary>
        /// Write the withdrawals of the given roaming network beside its store. Locked by the caller.
        /// </summary>
        private void WriteWithdrawals(RoamingNetwork_Id                         RoamingNetworkId,
                                      List<RoamingNetworkChangeSetWithdrawal>   Kept)
        {

            try
            {

                var file = Path.Combine(POIDirectory, RoamingNetworkId.ToString(), WithdrawalsFileName);

                File.WriteAllText(file + ".new", new JArray(Kept.Select(withdrawal => withdrawal.ToJSON())).ToString(Newtonsoft.Json.Formatting.Indented));
                File.Move        (file + ".new", file, overwrite: true);

            }
            catch (Exception e)
            {
                Log.Exception(e, $"The withdrawals of change sets of the roaming network '{RoamingNetworkId}' could not be kept: after a restart they are passed on no more.", "poi", "shareholders", "store");
            }

        }

        /// <summary>
        /// Read the withdrawals of the given roaming network as they were kept in its directory. Each is signed, and
        /// verified by whoever takes it: by the history of another hub, where the change set waits still.
        /// </summary>
        private void ReadWithdrawals(RoamingNetwork_Id  RoamingNetworkId,
                                     String             Directory)
        {

            var file = Path.Combine(Directory, WithdrawalsFileName);

            if (!File.Exists(file))
                return;

            try
            {

                var read = new List<RoamingNetworkChangeSetWithdrawal>();

                foreach (var withdrawalJSON in (POIJSON.Read(File.ReadAllText(file)) as JArray)?.OfType<JObject>() ?? [])
                {

                    if (RoamingNetworkChangeSetWithdrawal.TryParse(withdrawalJSON, out var withdrawal, out var error) &&
                        withdrawal.RoamingNetworkId == RoamingNetworkId)
                    {
                        read.Add(withdrawal);
                    }

                    else
                        Log.Error($"A withdrawal of a change set of the roaming network '{RoamingNetworkId}' in '{file}' can not be read: {error ?? "it is one of another roaming network"}", "poi", "shareholders");

                }

                lock (withdrawals)
                    withdrawals[RoamingNetworkId] = [ .. read.TakeLast(MaxWithdrawalsKept) ];

            }
            catch (Exception e)
            {
                Log.Exception(e, $"The withdrawals of change sets kept in '{file}' can not be read.", "poi", "shareholders", "store");
            }

        }

        #endregion

        #region (private) ExchangePending(Network)

        /// <summary>
        /// Exchange the change sets waiting for signatures with every server the given roaming network followed is
        /// connected with, where this hub is one of several making it (SHAREHOLDERS.md, phase 7.2). Asked for when
        /// this hub looks, and after somebody decided something here. A server that is away is asked the next time.
        /// </summary>
        /// <returns>What was committed here by it, said.</returns>
        private async Task<IReadOnlyList<String>> ExchangePending(FollowedRoamingNetwork Network)
        {

            var said = new List<String>();

            if (Network.Copy is not RoamingNetworkHistory history || !IsMadeBySeveral(Network.Id))
                return said;

            // What this hub commits goes to the others: it may not have looked since it holds a key there.
            await KeepPeersOf(Network);

            foreach (var (url, peer) in Network.Peers)
            {

                // A server that did not answer a moment ago, and is not connected: asked when it is back.
                if (IsAway(url, peer))
                    continue;

                var at = url.TrimEnd('/');

                try
                {

                    #region What was withdrawn there waits here no longer

                    foreach (var withdrawal in await GetWithdrawals(at, Network.Id))
                    {

                        if (!history.TryGetPendingChangeSet(withdrawal.ChangeSetId, out _))
                            continue;

                        if (history.Withdraw(withdrawal).Status == RoamingNetworkCoSigningStatus.Withdrawn)
                        {
                            KeepWithdrawal(withdrawal);
                            DecidedHere(Network.Id, withdrawal.ChangeSetId.ToString());
                            Log.Notice($"The change set '{withdrawal.ChangeSetId}' of the roaming network '{Network.Id}' was withdrawn at '{at}': it waits here no longer.", "poi", "shareholders", "security");
                        }

                    }

                    #endregion

                    #region What waits there

                    using var response = await shareholderHTTP.GetAsync($"{at}/history/changeSets/pending");

                    if (!response.IsSuccessStatusCode)
                        continue;

                    var theirs = new Dictionary<ChangeSet_Id, RoamingNetworkChangeSet>();

                    foreach (var entry in (ReadJSON(await response.Content.ReadAsStringAsync())?["pendingChangeSets"] as JArray)?.OfType<JObject>() ?? [])
                        if (entry["changeSet"] is JObject changeSetJSON && RoamingNetworkChangeSet.TryParse(changeSetJSON, out var waitingThere, out _))
                            theirs[waitingThere.Id] = waitingThere;

                    var withdrawn = WithdrawalsOf(Network.Id).ToDictionary(withdrawal => withdrawal.ChangeSetId);

                    foreach (var changeSet in theirs.Values)
                    {

                        // Withdrawn here, or known here to be: withdrawn there as well.
                        if (withdrawn.TryGetValue(changeSet.Id, out var itsWithdrawal))
                        {

                            using var request  = new HttpRequestMessage(HttpMethod.Delete, $"{at}/history/changeSets/pending/{changeSet.Id}") {
                                                     Content = new StringContent(itsWithdrawal.ToJSON().ToString(), Encoding.UTF8, "application/json")
                                                 };

                            using var answer   = await shareholderHTTP.SendAsync(request);

                            continue;

                        }

                        var known = history.TryGetPendingChangeSet(changeSet.Id, out var waitingHere) ? waitingHere.ChangeSet : null;

                        if (known is not null && changeSet.Signatures.All(signature => known.Signatures.Any(has => has.KeyId == signature.KeyId)))
                            continue;

                        var taken = history.CoSign(changeSet);

                        if (taken.Status == RoamingNetworkCoSigningStatus.Committed)
                        {
                            said.Add($"The change set '{changeSet.Id}' of the roaming network '{Network.Id}' has the signatures it needs, with those from '{at}', and is committed: it is at revision {history.Head.Commit.Revision}.");
                            Log.Notice(said[^1], "poi", "shareholders", "security");
                        }

                        else if (taken.Status == RoamingNetworkCoSigningStatus.Pending && known is null)
                            Log.Info($"The change set '{changeSet.Id}' of the roaming network '{Network.Id}' waiting at '{at}' waits here as well.", "poi", "shareholders");

                    }

                    #endregion

                    #region What waits here, and not there, or with signatures it lacks there

                    foreach (var changeSet in history.PendingChangeSets)
                    {

                        if (theirs.TryGetValue(changeSet.Id, out var there) &&
                            changeSet.Signatures.All(signature => there.Signatures.Any(has => has.KeyId == signature.KeyId)))
                        {
                            continue;
                        }

                        await Post($"{at}/history/changeSets/pending", changeSet.ToJSON());

                    }

                    #endregion

                }
                catch (HttpRequestException)
                {
                    // Away: asked again when it is back, or a while later.
                    unreached[url] = TimeProvider.GetUtcNow();
                }
                catch (TaskCanceledException)
                {
                    unreached[url] = TimeProvider.GetUtcNow();
                }
                catch
                {
                    // No hub, or it answered something else: asked again when this hub looks next.
                }

            }

            return said;

        }

        /// <summary>
        /// The withdrawals the hub serving the given copy of a roaming network knows; none where it is no hub, or tells none.
        /// </summary>
        private static async Task<IReadOnlyList<RoamingNetworkChangeSetWithdrawal>> GetWithdrawals(String             At,
                                                                                                   RoamingNetwork_Id  RoamingNetworkId)
        {

            var suffix = $"/{POIPathSegment}/RNs/{RoamingNetworkId}";

            if (!At.EndsWith(suffix, StringComparison.Ordinal))
                return [];

            using var response = await shareholderHTTP.GetAsync($"{At[..^suffix.Length]}/api/v1/{POIPathSegment}/shareholders/{RoamingNetworkId}/withdrawals");

            if (!response.IsSuccessStatusCode)
                return [];

            var found = new List<RoamingNetworkChangeSetWithdrawal>();

            foreach (var withdrawalJSON in (ReadJSON(await response.Content.ReadAsStringAsync())?["withdrawals"] as JArray)?.OfType<JObject>() ?? [])
                if (RoamingNetworkChangeSetWithdrawal.TryParse(withdrawalJSON, out var withdrawal, out _) && withdrawal.RoamingNetworkId == RoamingNetworkId)
                    found.Add(withdrawal);

            return found;

        }

        #endregion

        #region PendingChangeSetsOf(RoamingNetworkId)

        /// <summary>
        /// The change sets of the given roaming network waiting for signatures, [ {"status", "changeSet", "missing"} ]:
        /// those of its history here, where this hub made it or is one of several making it; for one it only follows,
        /// as the server followed tells them. Null where this hub serves no such roaming network or the server
        /// followed could not be asked.
        /// </summary>
        public async Task<JArray?> PendingChangeSetsOf(RoamingNetwork_Id RoamingNetworkId)
        {

            if (!IsFollowed(RoamingNetworkId) || IsMadeBySeveral(RoamingNetworkId))
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

            var result = await DecidedPendingChangeSet(RoamingNetworkId, ChangeSetId, Approve);

            // Decided here, or waiting no longer: it waits for somebody here no longer.
            if (result.Success || result.Status == ShareholderChangeStatus.NotFound)
                DecidedHere(RoamingNetworkId, ChangeSetId.ToString());

            return result;

        }

        private async Task<ShareholderChangeResult> DecidedPendingChangeSet(RoamingNetwork_Id  RoamingNetworkId,
                                                                            ChangeSet_Id       ChangeSetId,
                                                                            Boolean            Approve)
        {

            RoamingNetworkHistory?    history;
            FollowedRoamingNetwork?   network  = null;

            if (IsFollowed(RoamingNetworkId))
            {
                if (!TryGetFollowed(RoamingNetworkId, out network, out history, out var notFollowed))
                    return notFollowed;
            }
            else if (!POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out history))
                return new (ShareholderChangeStatus.NotFound, $"This hub neither makes nor follows a roaming network '{RoamingNetworkId}'.");

            if (NetworkOperator.SigningKeyIdIn(history.Head.RoamingNetwork) is not String signingKeyId)
                return new (ShareholderChangeStatus.NothingToDo, $"This hub holds no key of the operator of the roaming network '{RoamingNetworkId}': it is none of those who decide.");

            // Decided in the history here, at every holder's hub (phase 7.2), and told to the others; a hub that only
            // follows holds no key, and was answered above.
            var isFollowed = network is not null && !IsMadeBySeveral(RoamingNetworkId);

            // One waiting at another holder's hub that did not get here yet.
            if (network is not null && !isFollowed && !history.TryGetPendingChangeSet(ChangeSetId, out _))
                await ExchangePending(network);

            try
            {

                var result = await Decide();

                // What was decided here goes to the other holders' hubs at once.
                if (network is not null && !isFollowed && result.Success)
                    await ExchangePending(network);

                return result;

            }
            catch (Exception e)
            {
                return new (ShareholderChangeStatus.Refused, $"The change set '{ChangeSetId}' could not be {(Approve ? "signed" : "withdrawn")}: {e.Message}");
            }

            async Task<ShareholderChangeResult> Decide()
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

                    var withdrawal = NetworkOperator.WithdrawAsShareholder(changeSet, TimeProvider.GetUtcNow(), history.Head.RoamingNetwork);

                    if (!isFollowed)
                    {

                        var withdrawn = history.Withdraw(withdrawal);

                        if (withdrawn.Status != RoamingNetworkCoSigningStatus.Withdrawn)
                            return new (ShareholderChangeStatus.Invalid, $"The change set '{ChangeSetId}' was not withdrawn: {withdrawn.ErrorResponse}");

                        // Passed on to the other holders' hubs, where it waits as well.
                        KeepWithdrawal(withdrawal);

                        return Withdrawn();

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
                    signed = NetworkOperator.SignAsShareholder(signed, POIKeyKind.Signing, history.Head.RoamingNetwork);

                if (missing.Any(what => what.Contains("identity key", StringComparison.Ordinal))           &&
                    NetworkOperator.IdentityKeyIdIn(history.Head.RoamingNetwork) is String identityKeyId   &&
                    changeSet.Signatures.All(signature => signature.KeyId != identityKeyId))
                {
                    signed = NetworkOperator.SignAsShareholder(signed, POIKeyKind.Identity, history.Head.RoamingNetwork);
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

        }

        #endregion

    }

}
