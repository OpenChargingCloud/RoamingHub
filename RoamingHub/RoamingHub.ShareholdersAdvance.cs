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

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What a hub goes on with by itself where several hold the keys of the operator of a roaming network
    /// (SHAREHOLDERS.md, phase 6): somebody decides once per change at each hub, and what follows from that is done
    /// by the hub, when it looks - whenever it is told that something waits or was done, after it connected to a hub
    /// it follows, and every <see cref="AttestationCheckEvery"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No decision, and so done by itself: the change set of a key statement all signed who must is made where the
    /// roaming network is made, by the hub that signed that key statement; a hub that signed a key statement signs
    /// the change set of it; the hub that asked to hold the keys signs the change set admitting it; and a hub signs
    /// the commit of a change set it signed, so that its head is attested by it from then on.
    /// </para>
    /// <para>
    /// A decision, and never done by itself: signing a key statement - admitting, dismissing, agreeing to another
    /// identity key, stating the keys anew - resigning, approving a change set of the operator, and signing a commit
    /// of a change set this hub did not sign. What waits for somebody here is said in the log, once.
    /// </para>
    /// <para>
    /// The changes waiting where a roaming network is made are kept beside its store, in "shareholderChanges.json",
    /// and read at the start.
    /// </para>
    /// <para>
    /// Told is a notice, not the content: the hub where a roaming network is made pushes one to those following its
    /// history whenever a change of the holders waits anew, otherwise or no longer - the topic
    /// <see cref="ShareholderChangesTopic"/> - beside the change sets waiting, which the POI API pushes. A hub told
    /// reads what waits as it does when it looks by the timer, and verifies it the same: a notice forged or lost
    /// does no harm.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// The changes of the holders of the keys of the operator waiting, beside the store of a roaming network made here.
        /// </summary>
        public const String  ShareholderChangesFileName   = "shareholderChanges.json";

        /// <summary>
        /// How many commits behind the head of a roaming network followed are looked at for a signature of this hub.
        /// </summary>
        public const Int32   MaxCommitsSignedByItself      = 8;

        private readonly Dictionary<RoamingNetwork_Id, String>  shareholderChangesKept   = [];

        private readonly SemaphoreSlim                          shareholdersAdvancing    = new (1, 1);

        private readonly HashSet<String>                        undecidedSaid            = [];

        /// <summary>
        /// The topic of the WebSocket of a history made here: a notice of every change of the holders of the keys of
        /// its operator that waits anew, otherwise or no longer, { holder, kind, waits }.
        /// </summary>
        public const String  ShareholderChangesTopic      = "shareholderChanges";

        private readonly Dictionary<(RoamingNetwork_Id RoamingNetworkId, String Holder), (ShareholderChangeKind Kind, String Text)>  shareholderChangesTold  = [];

        private readonly System.Collections.Concurrent.ConcurrentDictionary<RoamingNetwork_Id, Boolean>  shareholdersAsked  = [];

        private Int32  shareholdersLookQueued;

        /// <summary>
        /// Whether this hub looks at what waits as soon as it is told, connected, or took a commit: yes. Otherwise
        /// only every <see cref="AttestationCheckEvery"/>, and when asked, <see cref="AdvanceShareholders"/>.
        /// </summary>
        public Boolean  LooksWhenTold    { get; set; } = true;

        #endregion


        #region (private) KeepShareholderChanges(RoamingNetworkId)

        /// <summary>
        /// Write the changes of the holders waiting in the given roaming network made here beside its store, where
        /// they are not what was written last; none of them, no file. Locked by the caller.
        /// </summary>
        private void KeepShareholderChanges(RoamingNetwork_Id RoamingNetworkId)
        {

            var changes  = shareholderChanges.
                               Where  (change => change.Key.RoamingNetworkId == RoamingNetworkId).
                               OrderBy(change => change.Key.Holder, StringComparer.Ordinal).
                               Select (change => change.Value.ToJSON()).
                               ToArray();

            TellShareholderChanges(RoamingNetworkId);

            var text     = changes.Length > 0
                               ? new JArray(changes).ToString(Newtonsoft.Json.Formatting.Indented)
                               : "";

            if (shareholderChangesKept.TryGetValue(RoamingNetworkId, out var kept)
                    ? kept == text
                    : text.Length == 0)
            {
                return;
            }

            try
            {

                var file = Path.Combine(POIDirectory, RoamingNetworkId.ToString(), ShareholderChangesFileName);

                if (text.Length == 0)
                    File.Delete(file);

                else
                {
                    File.WriteAllText(file + ".new", text);
                    File.Move        (file + ".new", file, overwrite: true);
                }

                shareholderChangesKept[RoamingNetworkId] = text;

            }
            catch (Exception e)
            {
                Log.Exception(e, $"The changes of the holders of the keys of the operator waiting in the roaming network '{RoamingNetworkId}' could not be kept: they are forgotten at a restart.", "poi", "shareholders", "store");
            }

        }

        #endregion

        #region (private) TellShareholderChanges(RoamingNetworkId)

        /// <summary>
        /// Tell those following the history of the given roaming network made here which changes of the holders wait
        /// anew, otherwise than told last, or no longer: a notice each, queued. Locked by the caller.
        /// </summary>
        private void TellShareholderChanges(RoamingNetwork_Id RoamingNetworkId)
        {

            void Tell(String Holder, ShareholderChangeKind Kind, Boolean Waits)
            {
                try
                {
                    POI.PushToHistorySubscribers(RoamingNetworkId,
                                                 ShareholderChangesTopic,
                                                 new JObject(
                                                     new JProperty("holder",  Holder),
                                                     new JProperty("kind",    Kind.ToString().ToLowerInvariant()),
                                                     new JProperty("waits",   Waits)
                                                 ));
                }
                catch (Exception e)
                {
                    Log.Exception(e, $"The roaming network '{RoamingNetworkId}': those following it could not be told of a change of the holders of the keys of its operator.", "poi", "shareholders");
                }
            }

            foreach (var change in shareholderChanges.Where(change => change.Key.RoamingNetworkId == RoamingNetworkId).ToArray())
            {

                var text = change.Value.ToJSON().ToString(Newtonsoft.Json.Formatting.None);

                if (shareholderChangesTold.TryGetValue(change.Key, out var told) && told.Text == text)
                    continue;

                shareholderChangesTold[change.Key] = (change.Value.Kind, text);
                Tell(change.Key.Holder, change.Value.Kind, true);

            }

            foreach (var told in shareholderChangesTold.Where(told => told.Key.RoamingNetworkId == RoamingNetworkId &&
                                                                      !shareholderChanges.ContainsKey(told.Key)).ToArray())
            {
                shareholderChangesTold.Remove(told.Key);
                Tell(told.Key.Holder, told.Value.Kind, false);
            }

        }

        #endregion

        #region (private) ReadShareholderChanges(History, Directory)

        /// <summary>
        /// Read the changes of the holders waiting in the given roaming network made here, as they were kept in its
        /// directory: those that still fit its head. Everything in them is signed, and verified again when it is used.
        /// </summary>
        private void ReadShareholderChanges(RoamingNetworkHistory  History,
                                            String                 Directory)
        {

            var file = Path.Combine(Directory, ShareholderChangesFileName);

            if (!File.Exists(file))
                return;

            try
            {

                var text  = File.ReadAllText(file);
                var read  = 0;

                lock (poiLock)
                {

                    foreach (var changeJSON in (POIJSON.Read(text) as JArray)?.OfType<JObject>() ?? [])
                    {

                        if (!ShareholderChange.TryParse(changeJSON, History.RoamingNetworkId, out var change, out var error))
                        {
                            Log.Error($"A change of the holders of the keys of the operator of the roaming network '{History.RoamingNetworkId}' in '{file}' can not be read: {error}", "poi", "shareholders");
                            continue;
                        }

                        if (ChangeError(change, History.Head.RoamingNetwork) is String changeError)
                        {
                            Log.Notice($"A change of the holders of the keys of the operator of the roaming network '{History.RoamingNetworkId}' waits no longer: {changeError}", "poi", "shareholders");
                            continue;
                        }

                        shareholderChanges[(History.RoamingNetworkId, change.Holder)] = change;
                        read++;

                    }

                    shareholderChangesKept[History.RoamingNetworkId] = text;

                    ForgetWhatIsDone(History);

                }

                if (read > 0)
                    Log.Info($"{read} change(s) of the holders of the keys of the operator of the roaming network '{History.RoamingNetworkId}' wait still.", "poi", "shareholders");

            }
            catch (Exception e)
            {
                Log.Exception(e, $"The changes of the holders of the keys of the operator waiting in '{file}' can not be read.", "poi", "shareholders", "store");
            }

        }

        #endregion


        #region (private) AdvanceShareholdersSoon(RoamingNetworkId)

        /// <summary>
        /// Look at the given roaming network soon, without waiting for it: this hub was told that something of it
        /// waits or was done, connected to where it is followed, or took a commit of it. Whatever is asked for while
        /// a look is at it is looked at by one more, after it.
        /// </summary>
        private void AdvanceShareholdersSoon(RoamingNetwork_Id RoamingNetworkId)
        {

            if (!LooksWhenTold)
                return;

            shareholdersAsked[RoamingNetworkId] = true;

            if (Interlocked.Exchange(ref shareholdersLookQueued, 1) == 1)
                return;

            _ = Task.Run(async () => {

                var locked = false;

                try
                {

                    await shareholdersAdvancing.WaitAsync();
                    locked = true;

                    // From here on, whatever is asked for queues the next look.
                    Interlocked.Exchange(ref shareholdersLookQueued, 0);

                    var asked = shareholdersAsked.Keys.ToArray();

                    foreach (var id in asked)
                        shareholdersAsked.TryRemove(id, out _);

                    await LookAtShareholders(asked);

                }
                catch (ObjectDisposedException)
                {
                    // The hub was stopped.
                }
                catch (Exception e)
                {
                    Log.Exception(e, "The changes of the holders of the keys of an operator could not be gone on with.", "poi", "shareholders");
                }
                finally
                {
                    if (locked)
                        shareholdersAdvancing.Release();
                }

            });

        }

        #endregion

        #region AdvanceShareholders()

        /// <summary>
        /// Go on with what follows from what was decided at this hub, in every roaming network it makes and in every
        /// one it follows: see the remarks of this class. What the timer does every
        /// <see cref="AttestationCheckEvery"/>; nothing while it is at it already.
        /// </summary>
        /// <returns>What was done, said.</returns>
        public async Task<IReadOnlyList<String>> AdvanceShareholders()
        {

            if (!await shareholdersAdvancing.WaitAsync(0))
                return [];

            try
            {
                return await LookAtShareholders(null);
            }
            finally
            {
                shareholdersAdvancing.Release();
            }

        }

        #endregion

        #region (private) LookAtShareholders(Only)

        /// <summary>
        /// What <see cref="AdvanceShareholders"/> does, in the given roaming networks only, or in all of them. One at
        /// a time: its callers hold <see cref="shareholdersAdvancing"/>.
        /// </summary>
        private async Task<IReadOnlyList<String>> LookAtShareholders(IReadOnlyCollection<RoamingNetwork_Id>? Only)
        {

            var done = new List<String>();

            {

                void Take(ShareholderChangeResult Result)
                {

                    if (Result.Status is ShareholderChangeStatus.Waiting or ShareholderChangeStatus.Committed)
                    {
                        done.Add(Result.Message);
                        Log.Notice("By itself: " + Result.Message, "poi", "shareholders", "security");
                    }

                    else if (Result.Status == ShareholderChangeStatus.Undecided)
                    {

                        Boolean said;

                        lock (undecidedSaid)
                        {

                            if (undecidedSaid.Count > 1000)
                                undecidedSaid.Clear();

                            said = !undecidedSaid.Add(Result.Message);

                        }

                        if (!said)
                            Log.Notice(Result.Message, "poi", "shareholders", "security");

                    }

                }

                #region Where the roaming network is made

                foreach (var history in HistoriesOfTheNetworkOperator().ToArray())
                {

                    if (Only?.Contains(history.RoamingNetworkId) == false)
                        continue;

                    (ShareholderChangeKind Kind, String Holder)[] waiting;

                    lock (poiLock)
                        waiting = [ .. shareholderChanges.Where (change => change.Key.RoamingNetworkId == history.RoamingNetworkId).
                                                          Select(change => (change.Value.Kind, change.Value.Holder)) ];

                    foreach (var (kind, holder) in waiting)
                    {
                        try
                        {
                            Take(await ChangeShareholders(kind, history.RoamingNetworkId, holder, ByItself: true));
                        }
                        catch (Exception e)
                        {
                            Log.Exception(e, $"The roaming network '{history.RoamingNetworkId}': a change of the holders of the keys of its operator could not be gone on with.", "poi", "shareholders");
                        }
                    }

                }

                #endregion

                #region Where it is followed

                if (NetworkOperator.Holder is String self)
                {
                    foreach (var network in FollowedRoamingNetworks)
                    {

                        if (network.History is not RoamingNetworkHistory copy || Only?.Contains(network.Id) == false)
                            continue;

                        var holds = NetworkOperator.SigningKeyIdIn(copy.Head.RoamingNetwork) is not null;

                        IReadOnlyList<ShareholderChange> changes;

                        try
                        {
                            changes = await GetChanges(network);
                        }
                        catch
                        {
                            // No hub, or not reached right now: asked again next time.
                            changes = [];
                        }

                        foreach (var change in changes)
                        {
                            try
                            {

                                if (holds)
                                    Take(await ChangeShareholders(change.Kind, network.Id, change.Holder, ByItself: true));

                                else if (change.Kind == ShareholderChangeKind.Admit && change.Holder == self)
                                    Take(await Join(network.Id, null, ByItself: true));

                            }
                            catch (Exception e)
                            {
                                Log.Exception(e, $"The roaming network '{network.Id}': a change of the holders of the keys of its operator could not be gone on with.", "poi", "shareholders");
                            }
                        }

                        try
                        {
                            done.AddRange(await SignWhatThisHubApproved(network));
                        }
                        catch (Exception e)
                        {
                            Log.Exception(e, $"The roaming network '{network.Id}': its commits could not be signed.", "poi", "shareholders");
                        }

                    }
                }

                #endregion

            }

            return done;

        }

        #endregion

        #region (private) SignWhatThisHubApproved(Network)

        /// <summary>
        /// Sign the commits of the given roaming network followed whose change set this hub signed, as one of the
        /// holders of the keys of its operator: the head and up to <see cref="MaxCommitsSignedByItself"/> commits
        /// behind it, as far as they lack its signature. It approved what they change; a commit of a change set it
        /// did not sign is signed when somebody says so.
        /// </summary>
        private async Task<IReadOnlyList<String>> SignWhatThisHubApproved(FollowedRoamingNetwork Network)
        {

            var signed = new List<String>();

            if (Network.History is not RoamingNetworkHistory history)
                return signed;

            var head = history.Head;

            if (NetworkOperator.CommitSignerIn(head.RoamingNetwork) is not RoamingNetworkCommitSigner signer)
                return signed;

            var mine = new HashSet<String>(StringComparer.Ordinal) {
                           NetworkOperator.IdentityKey.Id,
                           NetworkOperator.SigningKey. Id
                       };

            foreach (var keyId in new[] {
                                      NetworkOperator.NextIdentityKey?.Id,
                                      NetworkOperator.IdentityKeyIdIn(head.RoamingNetwork),
                                      NetworkOperator.SigningKeyIdIn (head.RoamingNetwork)
                                  })
            {
                if (keyId is not null)
                    mine.Add(keyId);
            }

            var commit = head.Commit;

            for (var behind = 0; behind <= MaxCommitsSignedByItself; behind++)
            {

                // Signed from here on backwards.
                if (commit.Signatures.Any(signature => signature.KeyId == signer.KeyId && signature.Role == signer.Role))
                    break;

                if (commit.ChangeSet?.Signatures.Any(signature => mine.Contains(signature.KeyId)) == true)
                {

                    var signing = await SignFollowedCommit(Network.Id, commit.Id);

                    if (signing.Status == RoamingNetworkCommitSigningStatus.Signed)
                        signed.Add(signing.Message);

                }

                if (commit.ParentIds.IsDefaultOrEmpty || !history.TryGetCommit(commit.ParentIds[0], out var parent))
                    break;

                commit = parent;

            }

            return signed;

        }

        #endregion

    }

}
