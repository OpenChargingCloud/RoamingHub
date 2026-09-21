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

using System.Collections.Concurrent;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.OCPI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// Who is on this hub and whether they can be reached right now - the
    /// OCPI HubClientInfo module, from this hub's side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept here rather than only in the OCPI library because it is a fact
    /// about a party and not about a version: whether DE*GEF is answering is
    /// the same question whether it was peered on 2.2.1 or on 2.3.0. This is
    /// the hub's own answer, and each version's Common API is fed from it so
    /// that a peer asking over OCPI gets the same answer as a browser.
    /// </para>
    /// <para>
    /// <b>How a status is decided.</b> Not by asking, most of the time. Every
    /// call a peer makes to this hub already passes the traffic recorder,
    /// which identifies the peer by its token - so an OCPI call <i>is</i> the
    /// liveness signal, and a hub with busy peers never has to check anything.
    /// The keepalive below only catches the quiet ones.
    /// </para>
    /// <para>
    /// The four statuses are the specification's, and the mapping is:
    /// a peer that is not registered yet is PLANNED - the contracts are not
    /// established; one that is registered and has been heard from inside the
    /// window is CONNECTED; one that is registered and has gone quiet is
    /// OFFLINE; and one an operator has switched off is SUSPENDED, which
    /// sticks until somebody switches it back on.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// How long a peer may be silent before this hub stops calling it
        /// connected.
        /// </summary>
        /// <remarks>
        /// The specification recommends five minutes before a hub goes and
        /// checks, and this is the same five minutes seen from the other side:
        /// what has not been heard from within it is not known to be there.
        /// </remarks>
        public static readonly TimeSpan  SilenceBeforeOffline  = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How often the quiet peers are looked at.
        /// </summary>
        /// <remarks>
        /// Well below the silence above, so that a peer which has just gone
        /// quiet is noticed within a fraction of that window rather than up to
        /// a whole one late.
        /// </remarks>
        public static readonly TimeSpan  PresenceCheckEvery    = TimeSpan.FromMinutes(1);

        /// <summary>
        /// How long one peer may take to accept a push before the next one is
        /// got on with.
        /// </summary>
        /// <remarks>
        /// Short, and deliberately so: the thing being delivered is that
        /// somebody is unreachable, and a hub that spent a minute per peer
        /// discovering that would deliver the news long after it mattered.
        /// </remarks>
        public static readonly TimeSpan  PushTimeout           = TimeSpan.FromSeconds(10);

        private readonly ConcurrentDictionary<Party_Idv3, PeerPresence>  presence = [];

        private          ITimer?                                         presenceTimer;

        /// <summary>
        /// One push at a time, so that a burst of changes does not open a
        /// connection to every peer several times over.
        /// </summary>
        private readonly SemaphoreSlim                                   pushLock = new (1, 1);

        /// <summary>
        /// Cancelled when this hub shuts down, so that a push in flight does
        /// not keep it up.
        /// </summary>
        private readonly CancellationTokenSource                         pushShutdown = new ();

        #endregion

        #region Properties

        /// <summary>
        /// Everyone this hub knows about, and how they are doing.
        /// </summary>
        public IEnumerable<PeerPresence>  Peers
            => presence.Values.OrderBy(peer => peer.PartyId.ToString()).ToArray();

        /// <summary>
        /// How many of them are answering.
        /// </summary>
        public Int32                      ConnectedPeerCount
            => presence.Values.Count(peer => peer.Status == PeerStatus.CONNECTED);

        #endregion

        #region Events

        /// <summary>
        /// A peer appeared, or changed how it is doing.
        /// </summary>
        /// <remarks>
        /// The one thing an operator actually waits for. Raised only on a
        /// change, never on a keepalive that found everything as it was.
        /// </remarks>
        public event Action<PeerPresence>? OnPeerPresenceChanged;

        #endregion


        #region SeenFrom     (RemotePartyId)

        /// <summary>
        /// A peer just called this hub, so it is there.
        /// </summary>
        /// <remarks>
        /// Called from the traffic recorder, which has already worked out who
        /// the caller is from its access token. Cheap on purpose: it runs on
        /// every single OCPI call, and on the overwhelming majority of them it
        /// only moves a timestamp.
        /// </remarks>
        internal void SeenFrom(String RemotePartyId)
        {

            var peer = FindPeer(RemotePartyId);

            if (peer is null)
                return;

            var now = TimeProvider.GetUtcNow();

            // A peer an operator has switched off does not switch itself back
            // on by calling: SUSPENDED is a decision, not an observation.
            if (peer.Status == PeerStatus.SUSPENDED)
            {
                presence[peer.PartyId] = peer with { LastSeen = now };
                return;
            }

            SetPresence(
                peer.PartyId,
                peer.Role,
                peer.Registered
                    ? PeerStatus.CONNECTED
                    : PeerStatus.PLANNED,
                Registered:  peer.Registered,
                LastSeen:    now
            );

        }

        #endregion

        #region Suspend/Resume(PartyId)

        /// <summary>
        /// Stop talking to a party, without forgetting it.
        /// </summary>
        /// <remarks>
        /// The specification has no deletion here: a ClientInfo object that
        /// vanished would leave every peer holding a party the hub no longer
        /// knows, and nothing to tell that apart from one that is merely
        /// quiet. So it is switched off instead, and every peer is told.
        /// </remarks>
        public Boolean SuspendPeer(Party_Idv3 PartyId)
        {

            if (!presence.TryGetValue(PartyId, out var peer))
                return false;

            SetPresence(PartyId, peer.Role, PeerStatus.SUSPENDED, peer.Registered, peer.LastSeen);

            Log.Notice($"'{PartyId}' was suspended and will not be talked to until it is resumed.", "ocpi", "hubclientinfo");

            return true;

        }

        /// <summary>
        /// Talk to it again: back to whatever it actually is.
        /// </summary>
        public Boolean ResumePeer(Party_Idv3 PartyId)
        {

            if (!presence.TryGetValue(PartyId, out var peer))
                return false;

            SetPresence(PartyId, peer.Role, StatusOf(peer.Registered, peer.LastSeen), peer.Registered, peer.LastSeen);

            Log.Notice($"'{PartyId}' was resumed.", "ocpi", "hubclientinfo");

            return true;

        }

        #endregion

        #region PeersJSON()

        /// <summary>
        /// Who is on this hub, as its page reads them.
        /// </summary>
        public JObject PeersJSON()

            => new (
                   new JProperty("peers",      new JArray(Peers.Select(peer => peer.ToJSON()))),
                   new JProperty("connected",  ConnectedPeerCount),
                   new JProperty("total",      presence.Count),
                   new JProperty("statuses",   new JArray(Enum.GetNames<PeerStatus>())),
                   new JProperty("silenceBeforeOfflineSeconds",  (Int32) SilenceBeforeOffline.TotalSeconds),
                   new JProperty("checkEverySeconds",            (Int32) PresenceCheckEvery.  TotalSeconds)
               );

        #endregion


        #region RefreshPresence(Quiet = false)

        /// <summary>
        /// Bring the list in line with the peers this hub actually has.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called at a start, and again after every change to the peers -
        /// one added, one registered, one removed. One method rather than a
        /// hook on each of those, because all three ask the same question:
        /// who is peered now, and what does that make them.
        /// </para>
        /// <para>
        /// What it does not touch is what it has no business deciding again:
        /// when a peer was last heard from stays, and a peer somebody
        /// suspended stays suspended. At a start nobody is CONNECTED, however
        /// completely they are registered - this hub has just come up and has
        /// heard from none of them.
        /// </para>
        /// </remarks>
        public void RefreshPresence(Boolean Quiet = false)
        {

            var seen = new HashSet<Party_Idv3>();

            foreach (var version in ocpiVersions)
            {
                foreach (var party in version.RemoteParties)
                {

                    var partyId = Party_Idv3.From(party.CountryCode, party.PartyId);

                    seen.Add(partyId);

                    var known   = presence.TryGetValue(partyId, out var previous) ? previous : null;

                    SetPresence(
                        partyId,
                        party.Role,
                        known?.Status == PeerStatus.SUSPENDED
                            ? PeerStatus.SUSPENDED
                            : StatusOf(party.Registered, known?.LastSeen),
                        Registered:  party.Registered,
                        LastSeen:    known?.LastSeen,
                        Quiet:       Quiet
                    );

                }
            }

            // A peer that was removed altogether is a different thing from one
            // that was suspended, and this is the only place it is forgotten:
            // it is no longer peered, so there is nothing left to report about
            // it - see SuspendPeer for the case where there is.
            foreach (var gone in presence.Keys.Where(partyId => !seen.Contains(partyId)).ToArray())
                presence.TryRemove(gone, out _);

            if (!presence.IsEmpty && !Quiet)
                Log.Info($"HubClientInfo: {presence.Count} peer(s), {ConnectedPeerCount} of them connected.", "ocpi", "hubclientinfo");

        }

        #endregion

        #region (private) SetPresence(...)

        /// <summary>
        /// Write a peer's state down, tell everybody when it moved, and hand
        /// it to every OCPI version so that a peer asking gets the same answer.
        /// </summary>
        private void SetPresence(Party_Idv3       PartyId,
                                 Role             Role,
                                 PeerStatus       Status,
                                 Boolean          Registered,
                                 DateTimeOffset?  LastSeen,
                                 Boolean          Quiet        = false)
        {

            var now       = TimeProvider.GetUtcNow();
            var existed   = presence.TryGetValue(PartyId, out var previous);
            var changed   = !existed || previous!.Status != Status;

            var updated   = new PeerPresence(
                                PartyId,
                                Role,
                                Status,
                                Registered,
                                // As with the OCPI object it feeds: the moment
                                // only moves when something about the party
                                // did, or every peer's "date_from" would match
                                // everything forever.
                                changed ? now : previous!.LastUpdated,
                                LastSeen
                            );

            presence[PartyId] = updated;

            // Into the OCPI store of every version, so that GET
            // ~/hubclientinfo answers what this page shows.
            foreach (var version in ocpiVersions)
                version.PublishClientInfo(PartyId, Role, Status, updated.LastUpdated);

            if (!changed || Quiet)
                return;

            Log.Notice($"HubClientInfo: '{PartyId}' is {Status}.", "ocpi", "hubclientinfo");

            // The other half of the module: the peers are told rather than
            // left to ask. Off this thread - see PushToPeers.
            PushToPeers(updated);

            try
            {
                OnPeerPresenceChanged?.Invoke(updated);
            }
            catch (Exception e)
            {
                Log.Exception(e, "A peer presence listener failed.", "ocpi", "hubclientinfo");
            }

        }

        #endregion

        #region (private) PushToPeers(Subject)

        /// <summary>
        /// Tell everybody else what became of one peer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Off the caller's thread, and that is not an optimisation. A status
        /// changes inside <c>SetPresence</c>, which is reached from the
        /// traffic recorder - so pushing synchronously would make one peer's
        /// OCPI call wait on an HTTP round trip to every other peer, and a
        /// peer that had gone offline would be the slowest of them.
        /// </para>
        /// <para>
        /// Not told: the subject itself, which already knows; a peer that has
        /// handed out nothing to talk to it with; and a peer that is itself
        /// OFFLINE or SUSPENDED, because the specification asks that nothing
        /// be queued for one - and because news of a third party is not worth
        /// waiting out a timeout for.
        /// </para>
        /// </remarks>
        private void PushToPeers(PeerPresence Subject)
        {

            if (pushShutdown.IsCancellationRequested)
                return;

            _ = Task.Run(async () => {

                try
                {

                    await pushLock.WaitAsync(pushShutdown.Token);

                    try
                    {
                        foreach (var version in ocpiVersions)
                        {
                            foreach (var target in version.RemoteParties)
                            {

                                var targetId = Party_Idv3.From(target.CountryCode, target.PartyId);

                                if (targetId == Subject.PartyId)
                                    continue;

                                if (presence.TryGetValue(targetId, out var targetPeer) &&
                                    targetPeer.Status is PeerStatus.OFFLINE or PeerStatus.SUSPENDED)
                                {
                                    continue;
                                }

                                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(pushShutdown.Token);

                                deadline.CancelAfter(PushTimeout);

                                var problem = await version.PushClientInfo(target.Id, Subject, deadline.Token);

                                if (problem is null)
                                    Log.Debug($"HubClientInfo: '{target.Id}' was told that '{Subject.PartyId}' is {Subject.Status}.", "ocpi", "hubclientinfo");

                                else
                                    // Debug and not a warning: a peer that
                                    // offers no receiver endpoint, or that is
                                    // simply down, is the ordinary state of a
                                    // hub - and this is a hub talking about
                                    // somebody else's choice, once per status
                                    // change, for every peer it has.
                                    Log.Debug($"HubClientInfo: '{target.Id}' could not be told about '{Subject.PartyId}': {problem}", "ocpi", "hubclientinfo");

                            }
                        }
                    }
                    finally
                    {
                        pushLock.Release();
                    }

                }
                catch (OperationCanceledException)
                { }
                catch (Exception e)
                {
                    Log.Exception(e, "The peers could not be told about a change.", "ocpi", "hubclientinfo");
                }

            });

        }

        #endregion

        #region (private) StatusOf(Registered, LastSeen)

        /// <summary>
        /// What a peer is, going by what is known about it.
        /// </summary>
        private PeerStatus StatusOf(Boolean          Registered,
                                          DateTimeOffset?  LastSeen)
        {

            if (!Registered)
                return PeerStatus.PLANNED;

            return LastSeen.HasValue &&
                   TimeProvider.GetUtcNow() - LastSeen.Value <= SilenceBeforeOffline
                       ? PeerStatus.CONNECTED
                       : PeerStatus.OFFLINE;

        }

        #endregion

        #region (private) FindPeer(RemotePartyId)

        /// <summary>
        /// The peer behind a remote party identification, over every version.
        /// </summary>
        private PeerPresence? FindPeer(String RemotePartyId)
        {

            foreach (var version in ocpiVersions)
            {
                foreach (var party in version.RemoteParties)
                {
                    if (party.Id.ToString() == RemotePartyId)
                    {

                        var partyId = Party_Idv3.From(party.CountryCode, party.PartyId);

                        return presence.TryGetValue(partyId, out var known)
                                   ? known with { Registered = party.Registered }
                                   : new PeerPresence(
                                         partyId,
                                         party.Role,
                                         PeerStatus.PLANNED,
                                         party.Registered,
                                         TimeProvider.GetUtcNow(),
                                         null
                                     );

                    }
                }
            }

            return null;

        }

        #endregion

        #region (private) StartWatchingThePeers() / CheckThePeers()

        /// <summary>
        /// Start noticing the peers that have gone quiet.
        /// </summary>
        private void StartWatchingThePeers()
        {

            presenceTimer?.Dispose();

            RefreshPresence(Quiet: true);

            presenceTimer = TimeProvider.CreateTimer(
                                _ => CheckThePeers(),
                                null,
                                PresenceCheckEvery,
                                PresenceCheckEvery
                            );

            Log.Info(
                $"HubClientInfo: the peers are looked at every {PresenceCheckEvery.TotalMinutes:F0} minute(s); " +
                $"one that has not been heard from for {SilenceBeforeOffline.TotalMinutes:F0} is taken to be offline.",
                "ocpi", "hubclientinfo"
            );

        }

        /// <summary>
        /// Anyone who has gone quiet for too long is no longer connected.
        /// </summary>
        /// <remarks>
        /// This is the passive half of the specification's keepalive: it does
        /// not go and ask, it notices that nobody asked <i>it</i>. A hub that
        /// polled every peer every five minutes would generate more traffic
        /// than the peering it is watching, and on a hub the peers call all
        /// day anyway.
        /// </remarks>
        private void CheckThePeers()
        {

            try
            {
                foreach (var peer in presence.Values.ToArray())
                {

                    // A decision, not an observation - see SuspendPeer.
                    if (peer.Status == PeerStatus.SUSPENDED)
                        continue;

                    var should = StatusOf(peer.Registered, peer.LastSeen);

                    if (should != peer.Status)
                        SetPresence(peer.PartyId, peer.Role, should, peer.Registered, peer.LastSeen);

                }
            }
            catch (Exception e)
            {
                Log.Exception(e, "The peers could not be looked at.", "ocpi", "hubclientinfo");
            }

        }

        #endregion

    }


    /// <summary>
    /// Whether a peer can be reached, as OCPI writes it.
    /// </summary>
    /// <remarks>
    /// The hub's own, with the four values of the specification, because the
    /// OCPI library declares one of these per version and a peer being
    /// reachable is not a fact about a version. Each version maps it back on
    /// its way out - see OCPIVersion.PublishClientInfo.
    /// </remarks>
    public enum PeerStatus
    {

        /// <summary>
        /// Online and can be sent requests.
        /// </summary>
        CONNECTED,

        /// <summary>
        /// Not reachable at the moment; nothing should be queued for it.
        /// </summary>
        OFFLINE,

        /// <summary>
        /// Peered, but the contracts are not established yet.
        /// </summary>
        PLANNED,

        /// <summary>
        /// Switched off by an operator, and to be treated like OFFLINE.
        /// </summary>
        SUSPENDED

    }


    /// <summary>
    /// How one peer of this hub is doing.
    /// </summary>
    /// <param name="PartyId">Who it is.</param>
    /// <param name="Role">What it is: a CPO, an EMSP, or another hub.</param>
    /// <param name="Status">Whether it can be reached, as OCPI writes it.</param>
    /// <param name="Registered">Whether the peering is complete in both directions.</param>
    /// <param name="LastUpdated">When the status last moved.</param>
    /// <param name="LastSeen">When this hub last heard from it, or null when it never has.</param>
    public sealed record PeerPresence(Party_Idv3       PartyId,
                                      Role             Role,
                                      PeerStatus       Status,
                                      Boolean          Registered,
                                      DateTimeOffset   LastUpdated,
                                      DateTimeOffset?  LastSeen)
    {

        public JObject ToJSON()

            => new (
                   new JProperty("partyId",      PartyId.ToString()),
                   new JProperty("countryCode",  PartyId.CountryCode.ToString()),
                   new JProperty("party",        PartyId.PartyId.    ToString()),
                   new JProperty("role",         Role.  ToString()),
                   new JProperty("status",       Status.ToString()),
                   new JProperty("registered",   Registered),
                   new JProperty("lastUpdated",  LastUpdated.ToString("o")),
                   new JProperty("lastSeen",     LastSeen?.  ToString("o"))
               );

    }

}
