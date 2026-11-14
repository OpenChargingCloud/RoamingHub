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
        internal RoamingNetworkHistoryFileStore      Store     { get; }
        internal RoamingNetworkHistoryFileArchive?   Archive   { get; }
        internal String?                             LastError { get; set; }

        /// <summary>
        /// Whether this hub is connected to the other server right now.
        /// </summary>
        public Boolean IsConnected
            => Peer?.IsConnected == true;

        /// <summary>
        /// Its copy here, once there is one.
        /// </summary>
        public RoamingNetworkHistory? History
            => Peer?.History;

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
        /// {"@id", "url", "connected", "revision"?, "commit"?, "trustAnchor", "error"?}.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("@id",          Id.ToString()),
                           new JProperty("url",          URL.ToString()),
                           new JProperty("connected",    IsConnected),
                           new JProperty("trustAnchor",  Trust.TrustAnchor.ToQRCode())
                       );

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

            if (network.Peer is RoamingNetworkPeer peer)
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


        #region SignFollowedCommit(RoamingNetworkId, CommitId = null)

        /// <summary>
        /// Sign a commit of a roaming network followed as one of the holders of the keys of its operator
        /// (SHAREHOLDERS.md, phase 3), and send the signature to the server followed: with as many holders signing as
        /// its quorum requires, whoever checks the roaming network takes the commit. Asked for by somebody who looked
        /// at the commit, never done by this hub itself.
        /// </summary>
        /// <param name="RoamingNetworkId">A roaming network this hub follows.</param>
        /// <param name="CommitId">The commit; its head otherwise.</param>
        public async Task<RoamingNetworkCommitSigning> SignFollowedCommit(RoamingNetwork_Id         RoamingNetworkId,
                                                                          RoamingNetworkCommit_Id?  CommitId   = null)
        {

            FollowedRoamingNetwork? network;

            lock (poiLock)
                followed.TryGetValue(RoamingNetworkId, out network);

            if (network?.History is not RoamingNetworkHistory history || network.Peer is not RoamingNetworkPeer peer)
                return new (RoamingNetworkCommitSigningStatus.NotFollowed,
                            network is null
                                ? $"This hub follows no roaming network '{RoamingNetworkId}'."
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

            try
            {

                using var timeout = new CancellationTokenSource(FollowTimeout);

                await peer.SignCommitAsync(commit.Id, [ signature ], timeout.Token);

            }
            catch (Exception e)
            {
                return new (RoamingNetworkCommitSigningStatus.Refused,
                            $"The signature of the commit '{commit.Id}' of the roaming network '{RoamingNetworkId}' was not taken at '{network.URL}': {e.Message}",
                            commit);
            }

            // In the copy here at once: it comes back from there as well, and is known then.
            history.TryAddSignatures(commit.Id, [ signature ], network.Trust.VerifyCommitSignature, out var signed, out _, out _);

            Log.Notice($"The commit '{commit.Id}' of the roaming network '{RoamingNetworkId}', revision {commit.Revision}, was signed as '{NetworkOperator.Holder}', " +
                       $"one of the holders of the keys of its operator '{head.RoamingNetwork.Operator?.Id}'.",
                       "poi", "follow", "security");

            return new (RoamingNetworkCommitSigningStatus.Signed,
                        $"The commit '{commit.Id}' of the roaming network '{RoamingNetworkId}', revision {commit.Revision}, is signed as '{NetworkOperator.Holder}', " +
                        $"one of the holders of the keys of its operator, and the signature was taken at '{network.URL}'.",
                        signed ?? commit);

        }

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
                    Serve(network, history);

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
            network.Trust.OnFollowed += (trust, trustAnchor) => {

                try
                {
                    WriteFollowed(network);
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

        #region (private) WriteFollowed(Network)

        private static void WriteFollowed(FollowedRoamingNetwork Network)
        {

            Directory.CreateDirectory(Network.Directory);

            var file       = Path.Combine(Network.Directory, FollowedFileName);
            var temporary  = file + ".new";

            File.WriteAllText(temporary,
                              new JObject(
                                  new JProperty("@id",          Network.Id.ToString()),
                                  new JProperty("url",          Network.URL.ToString()),
                                  new JProperty("trustAnchor",  Network.Trust.TrustAnchor.ToQRCode())
                              ).ToString());

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

            var trust = Network.Trust;

            RoamingNetwork? Head()
                => Network.History?.Head.RoamingNetwork;

            var peer = new RoamingNetworkPeer(WebSocketURL,
                                              History,
                                              new RoamingNetworkPeerOptions {

                                                  TrustAnchor                = trust.TrustAnchor,
                                                  Store                      = Network.Store,
                                                  Retention                  = poiConfiguration.EffectiveRetention,
                                                  Archive                    = Network.Archive,

                                                  VerifyCommitSignature      = trust.VerifyCommitSignature,
                                                  VerifyCheckpointSignature  = trust.VerifyCheckpointSignature(Head),
                                                  VerifyAttestation          = trust.VerifyAttestation(Head),

                                                  // A copy, changed from here by nobody: nothing of it goes back.
                                                  PushCommits                = false,
                                                  PushStatus                 = false,
                                                  PushCheckpoints            = false,
                                                  PushPriceStatements        = false

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

            peer.OnSynchronized += (_, history) => {
                Network.LastError = null;
                trust.Follow(history.Head.RoamingNetwork);
            };

            peer.OnHistoryReplacementRefused += (_, _, reason) => {
                Network.LastError = reason;
                Log.Warning($"The roaming network '{Network.Id}' followed at '{Network.URL}' is another one there than its copy here, which is kept: {reason}", "poi", "follow");
            };

            peer.OnError += (_, description, exception) => {

                if (Network.LastError != description)
                    Log.Warning($"Following the roaming network '{Network.Id}' at '{Network.URL}': {description}", "poi", "follow");

                Network.LastError = description;

            };

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

        #region (private) Serve(Network, History, OldHistory = null)

        /// <summary>
        /// Serve the given copy of a roaming network followed, in place of the one before if there was one, and follow
        /// its trust anchor with every commit it takes.
        /// </summary>
        private void Serve(FollowedRoamingNetwork  Network,
                           RoamingNetworkHistory   History,
                           RoamingNetworkHistory?  OldHistory   = null)
        {

            Network.Trust.Follow(History.Head.RoamingNetwork);

            // Called while the history is locked, which the same thread may enter again.
            History.OnCommitAnnouncement += announcement => Network.Trust.Follow(History.Head.RoamingNetwork);

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
                if (network.Peer is RoamingNetworkPeer peer)
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
