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
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What became of following a roaming network of another server.
    /// </summary>
    public enum RoamingNetworkFollowingStatus
    {

        /// <summary>
        /// Followed and caught up: its copy is served here.
        /// </summary>
        Synchronized,

        /// <summary>
        /// Followed, but not caught up yet: the other server did not answer in time, or what it gave was not taken.
        /// This hub keeps trying.
        /// </summary>
        Following,

        /// <summary>
        /// It is no roaming network this hub can follow: no trust anchor, no URL, or an identification that can be no
        /// directory.
        /// </summary>
        Invalid,

        /// <summary>
        /// This hub serves a roaming network of that identification already.
        /// </summary>
        Exists,

        /// <summary>
        /// This hub does not follow a roaming network of that identification.
        /// </summary>
        NotFollowed,

        /// <summary>
        /// Followed no longer: its copy is gone.
        /// </summary>
        Unfollowed

    }


    /// <summary>
    /// What became of following a roaming network of another server, or of ending it.
    /// </summary>
    /// <param name="Status">What became of it.</param>
    /// <param name="Message">The same, said.</param>
    /// <param name="RoamingNetworkId">The roaming network, where there is one.</param>
    public sealed record RoamingNetworkFollowing(RoamingNetworkFollowingStatus  Status,
                                                 String                         Message,
                                                 RoamingNetwork_Id?             RoamingNetworkId   = null)
    {

        /// <summary>
        /// Whether it is followed, caught up or not yet, or followed no longer as asked.
        /// </summary>
        public Boolean Success
            => Status is RoamingNetworkFollowingStatus.Synchronized or
                         RoamingNetworkFollowingStatus.Following    or
                         RoamingNetworkFollowingStatus.Unfollowed;

    }


    /// <summary>
    /// What became of signing a commit of a roaming network followed.
    /// </summary>
    public enum RoamingNetworkCommitSigningStatus
    {

        /// <summary>
        /// Signed, and the signature taken by the server followed.
        /// </summary>
        Signed,

        /// <summary>
        /// The commit has a signature of this hub already.
        /// </summary>
        AlreadySigned,

        /// <summary>
        /// This hub follows no such roaming network, or has no copy of it yet.
        /// </summary>
        NotFollowed,

        /// <summary>
        /// The copy here has no such commit.
        /// </summary>
        UnknownCommit,

        /// <summary>
        /// This hub holds no key of the operator of the roaming network: it is none of its shareholders.
        /// </summary>
        NoShareholder,

        /// <summary>
        /// The server followed did not take the signature, or could not be asked.
        /// </summary>
        Refused

    }


    /// <summary>
    /// What became of signing a commit of a roaming network followed.
    /// </summary>
    /// <param name="Status">What became of it.</param>
    /// <param name="Message">The same, said.</param>
    /// <param name="Commit">The commit, with the signatures it has here.</param>
    public sealed record RoamingNetworkCommitSigning(RoamingNetworkCommitSigningStatus  Status,
                                                     String                             Message,
                                                     RoamingNetworkCommit?              Commit   = null)
    {

        /// <summary>
        /// Whether the commit has a signature of this hub.
        /// </summary>
        public Boolean Success
            => Status is RoamingNetworkCommitSigningStatus.Signed or RoamingNetworkCommitSigningStatus.AlreadySigned;

    }


    /// <summary>
    /// A roaming network of another server this hub follows: where, whom it trusts to sign as its operator, and how
    /// far it got.
    /// </summary>
    public sealed class FollowedRoamingNetwork
    {

        /// <summary>
        /// The roaming network.
        /// </summary>
        public RoamingNetwork_Id            Id            { get; }

        /// <summary>
        /// Where it is followed: ~/RNs/{RN_Id} at the other server.
        /// </summary>
        public URL                          URL           { get; }

        /// <summary>
        /// Whom this hub trusts to sign as its operator: the trust anchor it was given, followed since.
        /// </summary>
        public RoamingNetworkFollowerTrust  Trust         { get; }

        /// <summary>
        /// Its directory below "poi".
        /// </summary>
        public String                       Directory     { get; }

        internal RoamingNetworkPeer?                 Peer      { get; set; }

        /// <summary>
        /// The copy served here, once there is one: whichever peer brought it.
        /// </summary>
        internal RoamingNetworkHistory?              Copy      { get; set; }

        /// <summary>
        /// Whether the commits made or taken here go to the other servers: where this hub holds a key of its operator
        /// (SHAREHOLDERS.md, phase 7.1).
        /// </summary>
        internal Boolean                             Pushes    { get; set; }

        /// <summary>
        /// One at a time keeps its peers: when this hub looks, and before it does something there as a holder.
        /// </summary>
        internal SemaphoreSlim                       KeepingPeers  { get; } = new (1, 1);

        /// <summary>
        /// The peers to the copies of the other holders of the keys of its operator, by their URLs, ~/RNs/{RN_Id}:
        /// none while this hub holds no key of it.
        /// </summary>
        internal Dictionary<String, RoamingNetworkPeer>  Others  { get; } = new (StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Further copies of it this hub was told of, ~/RNs/{RN_Id} at other hubs: connected with as those the
        /// roaming network names, where this hub holds a key of its operator. Kept beside the copy.
        /// </summary>
        internal HashSet<String>                     AlsoAt    { get; } = new (StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Every peer of it, with where it leads: the one it is followed at first.
        /// </summary>
        internal IReadOnlyList<(String URL, RoamingNetworkPeer Peer)> Peers
        {
            get
            {

                var peers = new List<(String, RoamingNetworkPeer)>();

                if (Peer is RoamingNetworkPeer peer)
                    peers.Add((URL.ToString(), peer));

                lock (Others)
                    peers.AddRange(Others.Select(other => (other.Key, other.Value)));

                return peers;

            }
        }

        internal RoamingNetworkHistoryFileStore      Store     { get; }
        internal RoamingNetworkHistoryFileArchive?   Archive   { get; }
        internal String?                             LastError { get; set; }

        /// <summary>
        /// Whether this hub asked to hold the keys of its operator as the configuration says: once, kept beside the copy.
        /// </summary>
        internal Boolean                             AskedToHold { get; set; }

        /// <summary>
        /// Whether this hub is connected to the other server right now.
        /// </summary>
        public Boolean IsConnected
            => Peer?.IsConnected == true;

        /// <summary>
        /// Its copy here, once there is one.
        /// </summary>
        public RoamingNetworkHistory? History
            => Copy ?? Peer?.History;

        /// <summary>
        /// What went wrong last, if anything did since it was last caught up.
        /// </summary>
        public String? Error
            => LastError;

        internal FollowedRoamingNetwork(RoamingNetwork_Id                  Id,
                                        URL                                URL,
                                        RoamingNetworkFollowerTrust        Trust,
                                        String                             Directory,
                                        RoamingNetworkHistoryFileStore     Store,
                                        RoamingNetworkHistoryFileArchive?  Archive)
        {

            this.Id         = Id;
            this.URL        = URL;
            this.Trust      = Trust;
            this.Directory  = Directory;
            this.Store      = Store;
            this.Archive    = Archive;

        }

        /// <summary>
        /// {"@id", "url", "connected", "alsoAt"?, "revision"?, "commit"?, "trustAnchor", "error"?}: "alsoAt" the
        /// copies of the other holders this hub is connected with as one of them, [ { "url", "connected" } ].
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("@id",          Id.ToString()),
                           new JProperty("url",          URL.ToString()),
                           new JProperty("connected",    IsConnected),
                           new JProperty("trustAnchor",  Trust.TrustAnchor.ToQRCode())
                       );

            lock (Others)
            {
                if (Others.Count > 0)
                    json["alsoAt"] = new JArray(Others.OrderBy(other => other.Key, StringComparer.OrdinalIgnoreCase).
                                                       Select (other => new JObject(
                                                                            new JProperty("url",        other.Key),
                                                                            new JProperty("connected",  other.Value.IsConnected)
                                                                        )));
            }

            if (History is RoamingNetworkHistory history)
            {
                json["revision"]  = history.Head.Commit.Revision;
                json["commit"]    = history.Head.Commit.Id.ToString();
            }

            if (Error is String error)
                json["error"] = error;

            return json;

        }

    }


    /// <summary>
    /// The roaming networks of other servers this hub follows (SHAREHOLDERS.md, phase 2): a copy of each, kept
    /// current over the WebSocket of its history there and served below "/poi" like the ones made here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A roaming network is followed by its trust anchor, as its operator hands it out: what this hub takes from the
    /// other server is taken only where it is signed by the keys the trust anchor leads to. The trust anchor is
    /// followed with it - a version stating the keys of its operator anew, signed by the ones trusted so far - and
    /// kept beside the copy, in "followed.json" in its directory below "poi", with the URL: read at every start,
    /// which goes on where the copy is.
    /// </para>
    /// <para>
    /// This hub is no operator of a roaming network it follows, and changes nothing of it: a change set or a
    /// runtime status sent here for it is refused, as it would be a commit of this copy alone. They go to the server
    /// followed.
    /// </para>
    /// <para>
    /// Where this hub holds a key of its operator, it is one of several making it (SHAREHOLDERS.md, phase 7.1): its
    /// copy is connected with the copies of the other holders as well, the commits made or taken here go to all of
    /// them, and theirs are taken here, merged as everywhere. So what one of them commits gets to everybody while any
    /// way between them is open, whichever hub is away.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// Where and with which trust anchor a roaming network is followed, in its directory below "poi".
        /// </summary>
        public const String  FollowedFileName  = "followed.json";

        /// <summary>
        /// How long following a roaming network waits for its first copy, before it says that it keeps trying.
        /// </summary>
        public static readonly TimeSpan  FollowTimeout  = TimeSpan.FromSeconds(15);

        private readonly Dictionary<RoamingNetwork_Id, FollowedRoamingNetwork>  followed  = [];

        #endregion

        #region Properties

        /// <summary>
        /// The roaming networks of other servers this hub follows, in the order of their identifications.
        /// </summary>
        public IReadOnlyList<FollowedRoamingNetwork> FollowedRoamingNetworks
        {
            get
            {
                lock (poiLock)
                    return [ .. followed.Values.OrderBy(network => network.Id.ToString(), StringComparer.OrdinalIgnoreCase) ];
            }
        }

        #endregion


        #region IsFollowed(RoamingNetworkId)

        /// <summary>
        /// Whether the given roaming network is one of another server this hub follows.
        /// </summary>
        public Boolean IsFollowed(RoamingNetwork_Id RoamingNetworkId)
        {
            lock (poiLock)
                return followed.ContainsKey(RoamingNetworkId);
        }

        #endregion

        #region FollowRoamingNetwork(TrustAnchor, URL = null, Timeout = null)

        /// <summary>
        /// Follow a roaming network of another server: a copy of it, checked against the given trust anchor, kept
        /// current from now on, stored below "poi" and served.
        /// </summary>
        /// <param name="TrustAnchor">Its trust anchor, as its operator hands it out: the text of its QR code, or its URL.</param>
        /// <param name="URL">Where it is followed, ~/RNs/{RN_Id} at the other server; the first URL of the trust anchor otherwise.</param>
        /// <param name="Timeout">How long to wait for its first copy; <see cref="FollowTimeout"/> otherwise.</param>
        public async Task<RoamingNetworkFollowing> FollowRoamingNetwork(String     TrustAnchor,
                                                                        URL?       URL       = null,
                                                                        TimeSpan?  Timeout   = null)
        {

            #region What it must be

            if (!RoamingNetworkTrustAnchor.TryParse(TrustAnchor?.Trim() ?? "", out var trustAnchor, out var anchorError))
                return new (RoamingNetworkFollowingStatus.Invalid,
                            $"A roaming network is followed by its trust anchor, the text of its QR code or its URL: {anchorError}");

            var id = trustAnchor.Id;

            if (!NewRoamingNetworkIdPattern.IsMatch(id.ToString()))
                return new (RoamingNetworkFollowingStatus.Invalid,
                            $"'{id}' can not be the identification of a roaming network followed here: it is the name of its directory as well, " +
                             "so it is letters, digits, '-' and '_', at most 64 of them, beginning with a letter or a digit.",
                            id);

            if (URL is null && trustAnchor.URLs.IsDefaultOrEmpty)
                return new (RoamingNetworkFollowingStatus.Invalid,
                            $"The trust anchor of the roaming network '{id}' names no URL: say where it is followed.",
                            id);

            var url = URL ?? trustAnchor.URLs[0];

            if (!TryGetHistoryWebSocketURL(url, out var webSocketURL))
                return new (RoamingNetworkFollowingStatus.Invalid,
                            $"'{url}' is no URL of a roaming network at another server: it is http or https, and ends in 'RNs/{id}'.",
                            id);

            #endregion

            FollowedRoamingNetwork network;

            lock (poiLock)
            {

                if (followed.ContainsKey(id) || POI.TryGetRoamingNetworkHistory(id, out _))
                    return new (RoamingNetworkFollowingStatus.Exists, $"This hub serves a roaming network '{id}' already.", id);

                var directory = Path.Combine(POIDirectory, id.ToString());

                if (Directory.Exists(POIDirectory) &&
                    Directory.GetDirectories(POIDirectory).Any(existing => String.Equals(Path.GetFileName(existing), id.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    return new (RoamingNetworkFollowingStatus.Exists,
                                $"There is a directory '{id}' below '{POIDirectory}' already, which this hub did not serve at its start: " +
                                 "the log of that start says why. It is left as it is.",
                                id);
                }

                network = NewFollowed(id, url, trustAnchor, directory);

                WriteFollowed(network);

                followed.Add(id, network);

            }

            Log.Notice($"The roaming network '{id}' is followed at '{url}' from now on.", "poi", "follow");

            return await StartFollowing(network, webSocketURL, null, Timeout ?? FollowTimeout);

        }

        #endregion

        #region FollowAlsoAt(RoamingNetworkId, URL)

        /// <summary>
        /// Tell this hub of a further copy of a roaming network it follows, at another hub: where it holds a key of
        /// its operator, it is connected with that copy as well when it looks next, as with those the roaming network
        /// names itself (SHAREHOLDERS.md, phase 7.1). Kept beside the copy here; the "alsoAt" of the configuration says
        /// the same.
        /// </summary>
        /// <param name="RoamingNetworkId">A roaming network this hub follows.</param>
        /// <param name="URL">The copy, ~/RNs/{RN_Id} at the other hub.</param>
        public RoamingNetworkFollowing FollowAlsoAt(RoamingNetwork_Id  RoamingNetworkId,
                                                    URL                URL)
        {

            FollowedRoamingNetwork? network;

            lock (poiLock)
                followed.TryGetValue(RoamingNetworkId, out network);

            if (network is null)
                return new (RoamingNetworkFollowingStatus.NotFollowed,
                            $"This hub follows no roaming network '{RoamingNetworkId}'.",
                            RoamingNetworkId);

            var text = URL.ToString().TrimEnd('/');

            if (!TryGetHistoryWebSocketURL(URL, out _) || !text.EndsWith($"/RNs/{RoamingNetworkId}", StringComparison.Ordinal))
                return new (RoamingNetworkFollowingStatus.Invalid,
                            $"'{URL}' is no URL of the roaming network '{RoamingNetworkId}' at another hub: it is http or https, and ends in 'RNs/{RoamingNetworkId}'.",
                            RoamingNetworkId);

            Boolean added;

            lock (network.Others)
                added = network.AlsoAt.Add(text);

            if (added)
            {
                WriteFollowed(network);
                Log.Notice($"The roaming network '{RoamingNetworkId}' has a copy at '{text}' as well, this hub was told.", "poi", "follow");
                AdvanceShareholdersSoon(RoamingNetworkId);
            }

            return new (RoamingNetworkFollowingStatus.Following,
                        $"The roaming network '{RoamingNetworkId}' has a copy at '{text}' as well: this hub is connected with it while it holds a key of its operator.",
                        RoamingNetworkId);

        }

        #endregion

        #region UnfollowRoamingNetwork(RoamingNetworkId)

        /// <summary>
        /// Follow the given roaming network of another server no longer: its copy is served no longer and deleted,
        /// as the roaming network itself is where it was followed.
        /// </summary>
        /// <param name="RoamingNetworkId">A roaming network this hub follows.</param>
        public async Task<RoamingNetworkFollowing> UnfollowRoamingNetwork(RoamingNetwork_Id RoamingNetworkId)
        {

            FollowedRoamingNetwork? network;

            lock (poiLock)
            {

                if (!followed.Remove(RoamingNetworkId, out network))
                    return new (RoamingNetworkFollowingStatus.NotFollowed,
                                $"This hub follows no roaming network '{RoamingNetworkId}'.",
                                RoamingNetworkId);

            }

            foreach (var (_, peer) in network.Peers)
                await peer.DisposeAsync();

            POI.RemoveRoamingNetworkHistory(RoamingNetworkId);

            lock (poiLock)
            {

                if (network.History is RoamingNetworkHistory history)
                {
                    history.DetachStore();
                    poiHistories.Remove(history);
                }

                poiStores.Remove(network.Store);
                network.Store.Dispose();

                if (network.Archive is not null)
                {
                    poiArchives.Remove(network.Archive);
                    network.Archive.Dispose();
                }

            }

            lock (waitingHere)
                waitingHere.Remove(RoamingNetworkId);

            lock (poiLock)
            {

                foreach (var key in shareholderChanges.Keys.Where(key => key.RoamingNetworkId == RoamingNetworkId).ToArray())
                    shareholderChanges.Remove(key);

                shareholderChangesKept.Remove(RoamingNetworkId);

            }

            try
            {
                if (Directory.Exists(network.Directory))
                    Directory.Delete(network.Directory, recursive: true);
            }
            catch (Exception e)
            {
                Log.Exception(e, $"The copy of the roaming network '{RoamingNetworkId}' in '{network.Directory}' could not be deleted.", "poi", "follow");

                return new (RoamingNetworkFollowingStatus.Unfollowed,
                            $"The roaming network '{RoamingNetworkId}' is followed no longer, but its copy in '{network.Directory}' could not be deleted: {e.Message}",
                            RoamingNetworkId);
            }

            Log.Notice($"The roaming network '{RoamingNetworkId}' is followed no longer; its copy is deleted.", "poi", "follow");

            return new (RoamingNetworkFollowingStatus.Unfollowed,
                        $"The roaming network '{RoamingNetworkId}' is followed no longer, and its copy here is deleted.",
                        RoamingNetworkId);

        }

        #endregion


        #region SignCommitAsHolder(RoamingNetworkId, CommitId = null)

        /// <summary>
        /// Sign a commit of a roaming network as one of the holders of the keys of its operator (SHAREHOLDERS.md,
        /// phases 3 and 7.1): with as many holders signing as its quorum requires, whoever checks the roaming network
        /// takes the commit. Of a roaming network followed the signature is sent to every server this hub is connected
        /// with there; of one made here it is taken here, and told to whoever follows it. Asked for by somebody who
        /// looked at the commit; by this hub itself only what RoamingHub.ShareholdersAdvance.cs says.
        /// </summary>
        /// <param name="RoamingNetworkId">A roaming network this hub makes or follows.</param>
        /// <param name="CommitId">The commit; its head otherwise.</param>
        public async Task<RoamingNetworkCommitSigning> SignCommitAsHolder(RoamingNetwork_Id         RoamingNetworkId,
                                                                          RoamingNetworkCommit_Id?  CommitId   = null)
        {

            FollowedRoamingNetwork? network;

            lock (poiLock)
                followed.TryGetValue(RoamingNetworkId, out network);

            var history = network?.History;

            if (network is null && !POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out history))
                history = null;

            if (history is null)
                return new (RoamingNetworkCommitSigningStatus.NotFollowed,
                            network is null
                                ? $"This hub neither makes nor follows a roaming network '{RoamingNetworkId}'."
                                : $"The roaming network '{RoamingNetworkId}' has no copy here yet.");

            var head    = history.Head;
            var commit  = head.Commit;

            if (CommitId is RoamingNetworkCommit_Id commitId && commitId != commit.Id && !history.TryGetCommit(commitId, out commit))
                return new (RoamingNetworkCommitSigningStatus.UnknownCommit,
                            $"The copy of the roaming network '{RoamingNetworkId}' here has no commit '{commitId}'.");

            // Its key is one the version it signs in certifies: the head, as the keys of the operator are what they are now.
            if (NetworkOperator.CommitSignerIn(head.RoamingNetwork) is not RoamingNetworkCommitSigner signer)
                return new (RoamingNetworkCommitSigningStatus.NoShareholder,
                            $"The operator '{head.RoamingNetwork.Operator?.Id}' of the roaming network '{RoamingNetworkId}' certifies no signing key held here: " +
                             "this hub is none of its shareholders.",
                            commit);

            if (commit.Signatures.Any(signature => signature.KeyId == signer.KeyId && signature.Role == signer.Role))
                return new (RoamingNetworkCommitSigningStatus.AlreadySigned,
                            $"The commit '{commit.Id}' of the roaming network '{RoamingNetworkId}' has a signature of this hub already.",
                            commit);

            var signature = signer.Sign(commit, TimeProvider.GetUtcNow());

            RoamingNetworkCommit?  signed;
            String                 where;

            #region One made here: taken here, and told to whoever follows it

            if (network is null)
            {

                if (!history.TryAddSignatures(commit.Id, [ signature ], VerifyOperatorCommitSignature, out signed, out _, out var addError))
                    return new (RoamingNetworkCommitSigningStatus.Refused,
                                $"The signature of the commit '{commit.Id}' of the roaming network '{RoamingNetworkId}' was not taken: {addError}",
                                commit);

                // Signed, so attested from now on while it is the head.
                Attest(history, TimeProvider.GetUtcNow());

                where = "and the signature was taken here";

            }

            #endregion

            #region One followed: sent to every server this hub is connected with there

            else
            {

                var took     = new List<String>();
                var refused  = new List<String>();

                foreach (var (url, peer) in network.Peers)
                {
                    try
                    {

                        using var timeout = new CancellationTokenSource(FollowTimeout);

                        await peer.SignCommitAsync(commit.Id, [ signature ], timeout.Token);

                        took.Add(url);

                    }
                    catch (Exception e)
                    {
                        refused.Add($"'{url}': {e.Message}");
                    }
                }

                if (took.Count == 0)
                    return new (RoamingNetworkCommitSigningStatus.Refused,
                                $"The signature of the commit '{commit.Id}' of the roaming network '{RoamingNetworkId}' was not taken at {String.Join("; ", refused)}",
                                commit);

                // In the copy here at once: it comes back from there as well, and is known then.
                if (!history.TryAddSignatures(commit.Id, [ signature ], network.Trust.VerifyCommitSignature, out signed, out _, out var copyError))
                    Log.Warning($"The signature of this hub of the commit '{commit.Id}' of the roaming network '{RoamingNetworkId}' was not taken by its copy here: {copyError}", "poi", "follow", "security");

                // Signed, so attested from now on while it is the head: see AttestFollowed.
                AttestFollowed(network, TimeProvider.GetUtcNow());

                where = $"and the signature was taken at {String.Join(", ", took.Select(url => $"'{url}'"))}";

            }

            #endregion

            Log.Notice($"The commit '{commit.Id}' of the roaming network '{RoamingNetworkId}', revision {commit.Revision}, was signed as '{NetworkOperator.Holder}', " +
                       $"one of the holders of the keys of its operator '{head.RoamingNetwork.Operator?.Id}'.",
                       "poi", "follow", "security");

            return new (RoamingNetworkCommitSigningStatus.Signed,
                        $"The commit '{commit.Id}' of the roaming network '{RoamingNetworkId}', revision {commit.Revision}, is signed as '{NetworkOperator.Holder}', " +
                        $"one of the holders of the keys of its operator, {where}.",
                        signed ?? commit);

        }

        #endregion

        #region (private) AttestFollowed(Now) / AttestFollowed(Network, Now) / VerifyOperatorAttestation(Attestation)

        /// <summary>
        /// Attest the heads of the roaming networks followed that this hub signed (AttestFollowed(Network, Now)).
        /// </summary>
        private void AttestFollowed(DateTimeOffset Now)
        {

            foreach (var network in FollowedRoamingNetworks)
            {
                try
                {
                    AttestFollowed(network, Now);
                }
                catch (Exception e)
                {
                    Log.Exception(e, $"The roaming network '{network.Id}' followed could not be attested.", "poi", "follow", "security");
                }
            }

        }

        /// <summary>
        /// Attest the head of the given roaming network followed as one of the holders of the keys of its operator, and
        /// send the attestation where it is followed: only a head whose commit this hub signed, when it was told to,
        /// and only once its newest attestation of it is older than the interval of the history. An attestation says
        /// that a head is the current one, for a quarter of an hour: what was decided is the commit, by somebody; saying
        /// again that it still is the head is left to the hub. A head nobody signed here is not attested.
        /// </summary>
        /// <returns>The attestations made.</returns>
        private IReadOnlyList<RoamingNetworkAttestation> AttestFollowed(FollowedRoamingNetwork  Network,
                                                                        DateTimeOffset          Now)
        {

            if (Network.History is not RoamingNetworkHistory history)
                return [];

            var head = history.Head;

            if (NetworkOperator.CommitSignerIn(head.RoamingNetwork) is not RoamingNetworkCommitSigner signer)
                return [];

            if (!head.Commit.Signatures.Any(signature => signature.KeyId == signer.KeyId && signature.Role == signer.Role))
                return [];

            if (history.CurrentAttestations(Now).Any(attestation => attestation.CommitId == head.Commit.Id  &&
                                                                    attestation.KeyId    == signer.KeyId    &&
                                                                    attestation.Role     == signer.Role     &&
                                                                    Now - attestation.SignedAt < history.AttestationInterval))
            {
                return [];
            }

            var made = new List<RoamingNetworkAttestation>();

            foreach (var eTag in head.Commit.ETags.Where(eTag => eTag.Format == ETagFormat.EffectiveTree))
            {

                var attestation = signer.Attest(Network.Id,
                                                head.Commit.Id,
                                                head.Commit.Revision,
                                                eTag,
                                                Now,
                                                Now + history.AttestationValidity);

                if (history.TryAddAttestation(attestation, Network.Trust.VerifyAttestation(() => head.RoamingNetwork), out var error, Now))
                    made.Add(attestation);
                else
                    Log.Warning($"The attestation of the head of the roaming network '{Network.Id}' followed was not taken by its copy here: {error}", "poi", "follow", "security");

            }

            if (made.Count > 0)
                foreach (var (url, peer) in Network.Peers)
                    _ = SendAttestations(Network, url, peer, made);

            return made;

        }

        private async Task SendAttestations(FollowedRoamingNetwork                    Network,
                                            String                                    URL,
                                            RoamingNetworkPeer                        Peer,
                                            IReadOnlyList<RoamingNetworkAttestation>  Attestations)
        {

            // A server that is away is said to be so where it is followed: it is attested to at the next turn.
            if (!Peer.IsConnected)
                return;

            for (var attempt = 1; ; attempt++)
            {
                try
                {

                    using var timeout = new CancellationTokenSource(FollowTimeout);

                    await Peer.AddAttestationsAsync(Attestations, timeout.Token);

                    return;

                }
                catch (Exception e)
                {

                    // Once more, a moment later: the attestation of a head made here may get there before its commit,
                    // which names the key it is signed with.
                    if (attempt == 1 && Peer.IsConnected)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        continue;
                    }

                    // Said of the server it is followed at: the copy of another one may be told of before its hub
                    // holds a key, and takes no attestation until then.
                    if (Peer.IsConnected && Peer == Network.Peer)
                        Log.Warning($"The attestation of the head of the roaming network '{Network.Id}' was not taken at '{URL}': {e.Message}", "poi", "follow", "security");

                    return;

                }
            }

        }

        /// <summary>
        /// Whether the given attestation another server sends is one of the operator of a roaming network made here: by a
        /// signing key its head certifies, valid when it signed and one that may sign for the party.
        /// </summary>
        private Boolean VerifyOperatorAttestation(RoamingNetworkAttestation Attestation)

            => Attestation.Role == RoamingNetworkCommitSignatureRoles.RoamingNetworkOperator       &&
               (!IsFollowed(Attestation.RoamingNetworkId) || IsMadeBySeveral(Attestation.RoamingNetworkId)) &&
               POI.TryGetRoamingNetworkHistory(Attestation.RoamingNetworkId, out var history)      &&
               history.Head.RoamingNetwork.Operator?.SigningKeyCertificates.
                   FirstOrDefault(certificate => certificate.SigningKey.Id == Attestation.KeyId          &&
                                                 certificate.SigningKey.IsValidAt(Attestation.SignedAt)  &&
                                                 certificate.SigningKey.MaySign(POIKeyUsages.Party)) is RoamingNetworkSigningKeyCertificate certified &&
               Attestation.VerifySignature(certified.SigningKey.Key.ToPublicKey(), certified.SigningKey.Id, out _);

        #endregion

        #region (static) OperatorSignersOf(Commit, Version) / (private static) VerifyOperatorCommitSignature(Commit, Version, Signature)

        /// <summary>
        /// The holders of the keys of the roaming network operator that signed the given commit, as the given version
        /// names them: each once, however many of its keys signed.
        /// </summary>
        /// <param name="Commit">A commit.</param>
        /// <param name="Version">The version to read the holders in, e.g. the head.</param>
        public static IReadOnlyList<String> OperatorSignersOf(RoamingNetworkCommit  Commit,
                                                              RoamingNetwork        Version)

            => [ .. Commit.Signatures.
                        Where   (signature => signature.Role == RoamingNetworkCommitSignatureRoles.RoamingNetworkOperator).
                        Select  (signature => Version.Operator?.SigningKeyCertificates.FirstOrDefault(certificate => certificate.SigningKey.Id == signature.KeyId)?.Holder).
                        OfType<String>().
                        Distinct(StringComparer.Ordinal).
                        Order   (StringComparer.Ordinal) ];

        /// <summary>
        /// Whether the given signature of a commit of a roaming network served here is one of its operator: by a
        /// signing key the version the commit leads to certifies, valid when it signed and one that may sign for the
        /// party. The version was checked when its commit was taken, its certificates with it.
        /// </summary>
        private static Boolean VerifyOperatorCommitSignature(RoamingNetworkCommit           Commit,
                                                             RoamingNetwork                 Version,
                                                             RoamingNetworkCommitSignature  Signature)

            => Signature.Role == RoamingNetworkCommitSignatureRoles.RoamingNetworkOperator &&
               Version.Operator?.SigningKeyCertificates.
                   FirstOrDefault(certificate => certificate.SigningKey.Id == Signature.KeyId            &&
                                                 certificate.SigningKey.IsValidAt(Signature.SignedAt)    &&
                                                 certificate.SigningKey.MaySign(POIKeyUsages.Party)) is RoamingNetworkSigningKeyCertificate certified &&
               Commit.VerifySignature(Signature, certified.SigningKey.Key, certified.SigningKey.Id, out _);

        #endregion

        #region (private) TryFollowStored(Directory)

        /// <summary>
        /// Go on following the roaming network stored in the given directory below "poi", if it is one followed: with
        /// the copy it holds, or without one where it never got one. Called at the start, in the background.
        /// </summary>
        /// <returns>Whether the directory is one of a roaming network followed, read or not.</returns>
        private Boolean TryFollowStored(String Directory)
        {

            var file = Path.Combine(Directory, FollowedFileName);

            if (!File.Exists(file))
                return false;

            try
            {

                var json = JObject.Parse(File.ReadAllText(file));

                if (json["trustAnchor"]?.Value<String>() is not String anchorText                                ||
                    !RoamingNetworkTrustAnchor.TryParse(anchorText, out var trustAnchor, out var anchorError)   ||
                    json["url"]?.Value<String>() is not String urlText                                           ||
                    !org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.TryParse(urlText, out var url)                   ||
                    !TryGetHistoryWebSocketURL(url, out var webSocketURL))
                {
                    Log.Error($"'{file}' does not say where and with which trust anchor a roaming network is followed.", "poi", "follow");
                    unreadPOIStores++;
                    return true;
                }

                var network = NewFollowed(trustAnchor.Id, url, trustAnchor, Directory);

                network.AskedToHold = json["askedToHold"]?.Type == JTokenType.Boolean && json["askedToHold"]!.Value<Boolean>();

                foreach (var also in (json["alsoAt"] as JArray)?.Where(also => also.Type == JTokenType.String).Select(also => also.Value<String>()!) ?? [])
                    network.AlsoAt.Add(also.TrimEnd('/'));

                // The copy it holds, if it got one: the peer goes on from there.
                if (!RoamingNetworkHistory.TryLoad(network.Store, out var history, out var loadError, poiConfiguration.EffectiveRetention, Archive: network.Archive))
                    history = null;

                if (history is not null && history.RoamingNetworkId != trustAnchor.Id)
                {
                    Log.Error($"The roaming network stored in '{Directory}' is '{history.RoamingNetworkId}', not '{trustAnchor.Id}', which '{FollowedFileName}' there follows.", "poi", "follow");
                    network.Store.Dispose();
                    network.Archive?.Dispose();
                    unreadPOIStores++;
                    return true;
                }

                lock (poiLock)
                    followed.Add(network.Id, network);

                if (history is not null)
                {

                    Serve(network, history);

                    // What waited of the holders of the keys of its operator, where this hub is one of them.
                    ReadShareholderChanges(history, Directory);

                }

                Log.Info(history is not null
                             ? $"The roaming network '{network.Id}' from '{Directory}', at revision {history.Head.Commit.Revision}, followed at '{url}'."
                             : $"The roaming network '{network.Id}' is followed at '{url}', and has no copy here yet{(loadError is not null ? $": {loadError}" : "")}.",
                         "poi", "follow");

                // Not waited for: the other server may be away, and this hub starts all the same.
                _ = StartFollowing(network, webSocketURL, history, TimeSpan.Zero);

            }
            catch (Exception e)
            {
                unreadPOIStores++;
                Log.Exception(e, $"The roaming network followed in '{Directory}' can not be read.", "poi", "follow");
            }

            return true;

        }

        #endregion

        #region (private) NewFollowed(Id, URL, TrustAnchor, Directory)

        private FollowedRoamingNetwork NewFollowed(RoamingNetwork_Id          Id,
                                                   URL                        URL,
                                                   RoamingNetworkTrustAnchor  TrustAnchor,
                                                   String                     Directory)
        {

            var network = new FollowedRoamingNetwork(Id,
                                                     URL,
                                                     new RoamingNetworkFollowerTrust(TrustAnchor),
                                                     Directory,
                                                     new RoamingNetworkHistoryFileStore(Directory),
                                                     NewPOIArchive(Directory));

            // Kept beside the copy as far as it was followed: the next start trusts whom this one came to trust.
            // Said where it names other keys, or another quorum: a trust anchor handed out names the roaming network
            // and where it is found as it was then, and is followed in that with the first copy, by the same keys.
            var keys = KeysOf(TrustAnchor);

            network.Trust.OnFollowed += (trust, trustAnchor) => {

                try
                {

                    WriteFollowed(network);

                    var now = KeysOf(trustAnchor);

                    if (Interlocked.Exchange(ref keys, now) != now)
                        Log.Notice($"The operator of the roaming network '{Id}' stated its keys anew: its trust anchor here names {trustAnchor.Keys.Length} identity key(s), {trustAnchor.MinConsensus} of {trustAnchor.Holders.Count} holder(s) deciding.", "poi", "follow", "security");

                }
                catch (Exception e)
                {
                    Log.Exception(e, $"The trust anchor of the roaming network '{Id}' could not be kept in '{Directory}'.", "poi", "follow");
                }

            };

            lock (poiLock)
            {

                poiStores.Add(network.Store);

                if (network.Archive is not null)
                    poiArchives.Add(network.Archive);

            }

            return network;

        }

        #endregion

        #region (private static) KeysOf(TrustAnchor)

        /// <summary>
        /// The identity keys the given trust anchor names, per holder, and how many of the holders decide: what a key
        /// statement states, without what the roaming network is called and where it is found.
        /// </summary>
        private static String KeysOf(RoamingNetworkTrustAnchor TrustAnchor)

            => $"{TrustAnchor.MinConsensus}:{String.Join("|", TrustAnchor.Holders.Select(holder => String.Join(",", holder)))}";

        #endregion

        #region (private) WriteFollowed(Network)

        private static void WriteFollowed(FollowedRoamingNetwork Network)
        {

            // One at a time: written when its trust anchor was followed, and when this hub was told of a copy.
            lock (Network)
                WriteFollowedLocked(Network);

        }

        private static void WriteFollowedLocked(FollowedRoamingNetwork Network)
        {

            Directory.CreateDirectory(Network.Directory);

            var file       = Path.Combine(Network.Directory, FollowedFileName);
            var temporary  = file + ".new";

            var json = new JObject(
                           new JProperty("@id",          Network.Id.ToString()),
                           new JProperty("url",          Network.URL.ToString()),
                           new JProperty("trustAnchor",  Network.Trust.TrustAnchor.ToQRCode())
                       );

            // Asked as the configuration says: not asked again, also after it was dismissed.
            if (Network.AskedToHold)
                json.Add("askedToHold", true);

            lock (Network.Others)
            {
                if (Network.AlsoAt.Count > 0)
                    json.Add("alsoAt", new JArray(Network.AlsoAt.Order(StringComparer.OrdinalIgnoreCase)));
            }

            File.WriteAllText(temporary, json.ToString());

            File.Move(temporary, file, overwrite: true);

        }

        #endregion

        #region (private) StartFollowing(Network, WebSocketURL, History, Timeout)

        /// <summary>
        /// Start the peer of the given roaming network followed, and wait for it to catch up for the given time.
        /// </summary>
        private async Task<RoamingNetworkFollowing> StartFollowing(FollowedRoamingNetwork  Network,
                                                                   Uri                     WebSocketURL,
                                                                   RoamingNetworkHistory?  History,
                                                                   TimeSpan                Timeout)
        {

            var peer = NewPeer(Network, WebSocketURL, History, Network.Pushes, Network.URL.ToString());

            Network.Peer = peer;

            using var waiting = new CancellationTokenSource(Timeout);

            try
            {

                await peer.StartAsync(waiting.Token);

                return new (RoamingNetworkFollowingStatus.Synchronized,
                            $"The roaming network '{Network.Id}' is followed at '{Network.URL}', at revision {peer.History?.Head.Commit.Revision}, " +
                            $"commit {peer.History?.Head.Commit.Id}. Its copy is served here, and changed from here by nobody.",
                            Network.Id);

            }
            catch (OperationCanceledException)
            {

                return new (RoamingNetworkFollowingStatus.Following,
                            $"The roaming network '{Network.Id}' is followed at '{Network.URL}', but has no current copy here yet" +
                            (Network.LastError is String error ? $": {error}" : ".") +
                             " This hub keeps trying.",
                            Network.Id);

            }

        }

        #endregion

        #region (private) NewPeer(Network, WebSocketURL, History, Pushes, At, Other = false)

        /// <summary>
        /// A peer of the given roaming network followed, not started: to the server it is followed at, or to the copy
        /// of another holder of the keys of its operator. It takes what is signed by the keys the trust anchor leads
        /// to, and pushes the commits made or taken here where this hub is a holder itself.
        /// </summary>
        /// <param name="Network">A roaming network followed.</param>
        /// <param name="WebSocketURL">The WebSocket of the history of the other server.</param>
        /// <param name="History">The copy here, if there is one.</param>
        /// <param name="Pushes">Whether the commits made or taken here go there.</param>
        /// <param name="At">Where it leads, ~/RNs/{RN_Id}, for what is said of it.</param>
        /// <param name="Other">Whether it leads to the copy of another holder, not to where the roaming network is followed.</param>
        private RoamingNetworkPeer NewPeer(FollowedRoamingNetwork  Network,
                                           Uri                     WebSocketURL,
                                           RoamingNetworkHistory?  History,
                                           Boolean                 Pushes,
                                           String                  At,
                                           Boolean                 Other   = false)
        {

            var     trust      = Network.Trust;
            String? lastError  = null;

            RoamingNetwork? Head()
                => Network.History?.Head.RoamingNetwork;

            var peer = new RoamingNetworkPeer(WebSocketURL,
                                              History,
                                              new RoamingNetworkPeerOptions {

                                                  TrustAnchor                = trust.TrustAnchor,

                                                  // A trust anchor from before the holders of the keys changed is
                                                  // led on by the key statements in between, before the first copy.
                                                  FollowKeyStatements        = statements => {
                                                                                   trust.FollowChain(statements);
                                                                                   return trust.TrustAnchor;
                                                                               },
                                                  Store                      = Network.Store,
                                                  Retention                  = poiConfiguration.EffectiveRetention,
                                                  Archive                    = Network.Archive,

                                                  VerifyCommitSignature      = trust.VerifyCommitSignature,
                                                  VerifyCheckpointSignature  = trust.VerifyCheckpointSignature(Head),
                                                  VerifyAttestation          = trust.VerifyAttestation(Head),

                                                  // A copy, changed from here by nobody: nothing of it goes back. Where
                                                  // this hub holds a key of its operator, its commits do, and those it
                                                  // took from the other holders (SHAREHOLDERS.md, phase 7.1).
                                                  PushCommits                = Pushes,
                                                  PushStatus                 = false,
                                                  PushCheckpoints            = false,
                                                  PushPriceStatements        = false,

                                                  // Notices of what waits there, where several hold the keys of its
                                                  // operator: see RoamingHub.ShareholdersAdvance.cs.
                                                  FurtherTopics              = [ POI_API.PendingChangeSetsTopic, ShareholderChangesTopic ]

                                              });

            peer.OnHistoryReplaced += (_, oldHistory, newHistory) => {

                try
                {
                    Serve(Network, newHistory, oldHistory);
                }
                catch (Exception e)
                {
                    Log.Exception(e, $"The copy of the roaming network '{Network.Id}' could not be served.", "poi", "follow");
                }

            };

            peer.OnSynchronized += (synchronized, history) => {

                lastError = null;

                if (!Other)
                    Network.LastError = null;

                trust.Follow(history.Head.RoamingNetwork);

                // A notice is missed while not connected.
                AdvanceShareholdersSoon(Network.Id);

                // And a signature of this hub the other server may have missed.
                _ = SendOwnSignatures(Network, synchronized);

            };

            // Something waits there anew, otherwise or no longer: read, and verified, when this hub looks.
            peer.OnNotice += (_, _, _) => AdvanceShareholdersSoon(Network.Id);

            peer.OnHistoryReplacementRefused += (_, _, reason) => {

                if (!Other)
                    Network.LastError = reason;

                Log.Warning($"The roaming network '{Network.Id}' followed at '{At}' is another one there than its copy here, which is kept: {reason}", "poi", "follow");

            };

            peer.OnError += (_, description, exception) => {

                // The copy of another holder: one way of several, said once.
                if (Other)
                {

                    if (lastError != description)
                        Log.Warning($"The roaming network '{Network.Id}', the copy of another holder at '{At}': {description}", "poi", "follow");

                    lastError = description;

                    return;

                }

                if (Network.LastError != description)
                    Log.Warning($"Following the roaming network '{Network.Id}' at '{At}': {description}", "poi", "follow");

                Network.LastError = description;

            };

            return peer;

        }

        #endregion

        #region (private) KeepPeersOf(Network)

        /// <summary>
        /// What this hub is in the given roaming network followed, as its head says (SHAREHOLDERS.md, phase 7.1), and
        /// the peers that follow from it. Where the head names a signing key held here for its operator, this hub is
        /// one of several making it: the commits it makes there are signed with that key, they and those it took go to
        /// every server it is connected with, and it is connected with the copies of the other holders as well, as the
        /// "urls" of the roaming network name them, and as this hub was told ("alsoAt"). Where it names none, it is a
        /// copy nobody changes from here, at the one server it is followed at. Called whenever this hub looks; nothing
        /// is done where nothing changed.
        /// </summary>
        private async Task KeepPeersOf(FollowedRoamingNetwork Network)
        {

            await Network.KeepingPeers.WaitAsync();

            try
            {
                await KeepPeersOfOneAtATime(Network);
            }
            finally
            {
                Network.KeepingPeers.Release();
            }

        }

        private async Task KeepPeersOfOneAtATime(FollowedRoamingNetwork Network)
        {

            if (Network.Copy is not RoamingNetworkHistory history)
                return;

            var head     = history.Head.RoamingNetwork;
            var signer   = NetworkOperator.CommitSignerIn(head);
            var holds    = signer is not null;
            var signers  = history.Signers;

            // A merge is signed where both its commits are, when this hub looks: see SignCommitsByItself.
            history.SignMerges = false;

            if (signer is null
                    ? !signers.IsDefaultOrEmpty
                    :  signers.IsDefaultOrEmpty || signers[0].KeyId != signer.KeyId)
            {
                history.Signers = signer is null ? [] : [ signer ];
            }

            var gone = new List<RoamingNetworkPeer>();

            #region Where it is followed: pushing or not, and with the copy served here

            if (Network.Peer is RoamingNetworkPeer primary              &&
                (Network.Pushes != holds || (primary.History is not null && primary.History != history)) &&
                TryGetHistoryWebSocketURL(Network.URL, out var primaryURL))
            {

                Network.Pushes  = holds;

                var next        = NewPeer(Network, primaryURL, history, holds, Network.URL.ToString());

                Network.Peer    = next;

                gone.Add(primary);
                StartSoon(next);

                Log.Notice(holds
                               ? $"The roaming network '{Network.Id}': this hub holds a key of its operator, so what is committed here goes to '{Network.URL}' from now on."
                               : $"The roaming network '{Network.Id}': this hub holds no key of its operator any more, so nothing goes from here to '{Network.URL}' any longer.",
                           "poi", "follow", "security");

            }

            #endregion

            #region The copies of the other holders

            var own      = OwnURLsOf(Network.Id);
            var desired  = new Dictionary<String, Uri>(StringComparer.OrdinalIgnoreCase);

            if (holds)
            {

                // As the roaming network names them, which is https only, and as this hub was told.
                var copies = new List<URL>();

                if (!head.URLs.IsDefaultOrEmpty)
                    copies.AddRange(head.URLs);

                copies.AddRange(poiConfiguration.EffectiveFollow.FirstOrDefault(follow => follow.TrustAnchor.Id == Network.Id)?.AlsoAt ?? []);

                lock (Network.Others)
                    foreach (var also in Network.AlsoAt)
                        if (URL.TryParse(also, out var told))
                            copies.Add(told);

                foreach (var url in copies)
                {

                    var text = url.ToString().TrimEnd('/');

                    if (!own.Contains(text)                                                                                &&
                        !String.Equals(text, Network.URL.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase)      &&
                        TryGetHistoryWebSocketURL(url, out var webSocketURL))
                    {
                        desired[text] = webSocketURL;
                    }

                }

            }

            lock (Network.Others)
            {

                foreach (var (text, peer) in Network.Others.ToArray())
                    if (!desired.ContainsKey(text) || (peer.History is not null && peer.History != history))
                    {
                        Network.Others.Remove(text);
                        gone.Add(peer);
                        Log.Info($"The roaming network '{Network.Id}': the copy at '{text}' is followed no longer.", "poi", "follow");
                    }

                foreach (var (text, webSocketURL) in desired)
                    if (!Network.Others.ContainsKey(text))
                    {

                        var peer = NewPeer(Network, webSocketURL, history, true, text, Other: true);

                        Network.Others.Add(text, peer);
                        StartSoon(peer);

                        Log.Info($"The roaming network '{Network.Id}': the copy of another holder of the keys of its operator at '{text}' is followed as well.", "poi", "follow");

                    }

            }

            #endregion

            foreach (var peer in gone)
            {
                try
                {
                    await peer.DisposeAsync();
                }
                catch (Exception e)
                {
                    Log.Exception(e, $"A peer of the roaming network '{Network.Id}' could not be stopped.", "poi", "follow");
                }
            }

        }

        /// <summary>
        /// Start the given peer, not waiting for the other server: it keeps trying.
        /// </summary>
        private static void StartSoon(RoamingNetworkPeer Peer)

            => _ = Task.Run(async () => {

                   try
                   {

                       using var waiting = new CancellationTokenSource(FollowTimeout);

                       await Peer.StartAsync(waiting.Token);

                   }
                   catch
                   { }

               });

        /// <summary>
        /// Where this hub itself serves the given roaming network, as far as it knows: as its configuration says, and at
        /// its own web interface.
        /// </summary>
        private HashSet<String> OwnURLsOf(RoamingNetwork_Id RoamingNetworkId)
        {

            var own = new HashSet<String>(StringComparer.OrdinalIgnoreCase) {
                          $"{WebInterfaceURL.ToString().TrimEnd('/')}/{POIPathSegment}/RNs/{RoamingNetworkId}"
                      };

            if (poiConfiguration.URLOf(RoamingNetworkId) is URL configured)
                own.Add(configured.ToString().TrimEnd('/'));

            return own;

        }

        #endregion

        #region IsMadeBySeveral(RoamingNetworkId) / (private static) RecentCommits(History, Max) / (private) SendOwnSignatures(Network, Peer)

        /// <summary>
        /// Whether the given roaming network served here is one several make, this hub among them (SHAREHOLDERS.md,
        /// phase 7.1): its head names more than one holder of the keys of its operator, and a signing key held here.
        /// Such a roaming network takes the commits of the other holders' hubs, whether it was made here or is followed.
        /// </summary>
        public Boolean IsMadeBySeveral(RoamingNetwork_Id RoamingNetworkId)

            => POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out var history)  &&
               history.Head.RoamingNetwork is RoamingNetwork head                  &&
               IsHeldBySeveral(head)                                               &&
               NetworkOperator.SigningKeyIdIn(head) is not null;

        /// <summary>
        /// Whether the given version of a roaming network names more than one holder of the keys of its operator.
        /// </summary>
        private static Boolean IsHeldBySeveral(RoamingNetwork Version)

            => Version.Operator is RoamingNetworkOperator networkOperator &&
               POIPartyKeys.Holders(networkOperator.IdentityKeys).Distinct(StringComparer.Ordinal).Count() > 1;

        /// <summary>
        /// The head of the given history and the commits behind it, the nearest first, at most as many as given: both
        /// parents of a merge.
        /// </summary>
        private static IReadOnlyList<RoamingNetworkCommit> RecentCommits(RoamingNetworkHistory  History,
                                                                         Int32                  Max)
        {

            var found  = new List<RoamingNetworkCommit>();
            var seen   = new HashSet<RoamingNetworkCommit_Id>();
            var next   = new Queue<RoamingNetworkCommit>();
            var head   = History.Head.Commit;

            seen.Add(head.Id);
            next.Enqueue(head);

            while (next.Count > 0 && found.Count < Max)
            {

                var commit = next.Dequeue();

                found.Add(commit);

                if (!commit.ParentIds.IsDefaultOrEmpty)
                    foreach (var parentId in commit.ParentIds)
                        if (seen.Add(parentId) && History.TryGetCommit(parentId, out var parent))
                            next.Enqueue(parent);

            }

            return found;

        }

        /// <summary>
        /// Send the signatures of this hub on the latest commits of the given roaming network to the given server once
        /// more, after it connected: one made while that server was away is known there only so. Taken again without
        /// harm; what fails is sent at the next connect.
        /// </summary>
        private async Task SendOwnSignatures(FollowedRoamingNetwork  Network,
                                             RoamingNetworkPeer      Peer)
        {

            try
            {

                if (Network.Copy is not RoamingNetworkHistory history ||
                    NetworkOperator.SigningKeyIdIn(history.Head.RoamingNetwork) is not String keyId)
                {
                    return;
                }

                foreach (var commit in RecentCommits(history, MaxCommitsSignedByItself + 1))
                {

                    var own = commit.Signatures.Where(signature => signature.KeyId == keyId &&
                                                                   signature.Role  == RoamingNetworkCommitSignatureRoles.RoamingNetworkOperator).ToArray();

                    if (own.Length == 0)
                        continue;

                    using var timeout = new CancellationTokenSource(FollowTimeout);

                    await Peer.SignCommitAsync(commit.Id, own, timeout.Token);

                }

            }
            catch
            { }

        }

        #endregion

        #region (private) Serve(Network, History, OldHistory = null)

        /// <summary>
        /// Serve the given copy of a roaming network followed, in place of the one before if there was one, and follow
        /// its trust anchor with every commit it takes.
        /// </summary>
        private void Serve(FollowedRoamingNetwork  Network,
                           RoamingNetworkHistory   History,
                           RoamingNetworkHistory?  OldHistory   = null)
        {

            Network.Copy        = History;

            // A merge is signed where both its commits are, when this hub looks: see SignCommitsByItself.
            History.SignMerges  = false;

            Network.Trust.Follow(History.Head.RoamingNetwork);

            // Called while the history is locked, which the same thread may enter again.
            History.OnCommitAnnouncement += announcement => Network.Trust.Follow(History.Head.RoamingNetwork);

            // And a commit of a change set this hub signed is signed by it, soon: only queued here.
            History.OnCommitAnnouncement += announcement => AdvanceShareholdersSoon(Network.Id);

            // And a change set waiting here anew, otherwise or no longer (SHAREHOLDERS.md, phase 7.2).
            History.OnPendingChangeSet   += result       => AdvanceShareholdersSoon(Network.Id);

            if (!POI.ReplaceRoamingNetworkHistory(History))
                throw new InvalidOperationException($"The roaming network '{Network.Id}' could not be served.");

            lock (poiLock)
            {

                if (OldHistory is not null)
                    poiHistories.Remove(OldHistory);

                poiHistories.Add(History);

            }

            WatchStore(History);
            Watch(History);

        }

        #endregion

        #region (private) StopFollowing()

        /// <summary>
        /// Stop following, before the stores of the copies are let go of.
        /// </summary>
        private async ValueTask StopFollowing()
        {

            FollowedRoamingNetwork[] networks;

            lock (poiLock)
                networks = [ .. followed.Values ];

            foreach (var network in networks)
                foreach (var (_, peer) in network.Peers)
                {
                    try
                    {
                        await peer.DisposeAsync();
                    }
                    catch (Exception e)
                    {
                        Log.Exception(e, $"Following the roaming network '{network.Id}' could not be stopped.", "poi", "follow");
                    }
                }

        }

        #endregion

        #region (private static) TryGetHistoryWebSocketURL(URL, out WebSocketURL)

        /// <summary>
        /// The URL of the WebSocket of the history of the roaming network at the given URL, ~/RNs/{RN_Id}: over TLS
        /// where that is https.
        /// </summary>
        private static Boolean TryGetHistoryWebSocketURL(URL      URL,
                                                         out Uri  WebSocketURL)
        {

            WebSocketURL = default!;

            var text = URL.ToString().TrimEnd('/');

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)                  ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            {
                return false;
            }

            var segments = uri.AbsolutePath.Split('/');

            if (segments.Length < 3 || segments[^2] != "RNs" || segments[^1].Length == 0)
                return false;

            WebSocketURL = new UriBuilder(uri) {
                               Scheme  = uri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
                               Port    = uri.Port,
                               Path    = uri.AbsolutePath + "/history/ws"
                           }.Uri;

            return true;

        }

        #endregion

    }

}
