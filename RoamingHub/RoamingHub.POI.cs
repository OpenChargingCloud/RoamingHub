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
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.RoamingHub.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What became of a roaming network to be made.
    /// </summary>
    public enum RoamingNetworkCreationStatus
    {

        /// <summary>
        /// Made, stored and served.
        /// </summary>
        Created,

        /// <summary>
        /// Not made: what was asked for is no roaming network this hub makes.
        /// </summary>
        Invalid,

        /// <summary>
        /// Not made: there is one of that identification already, or a directory of its name.
        /// </summary>
        Exists,

        /// <summary>
        /// Not made: its store could not be written.
        /// </summary>
        NotSaved

    }

    /// <summary>
    /// What became of a roaming network to be made, why, and its history when it was made.
    /// </summary>
    /// <param name="Status">What became of it.</param>
    /// <param name="Message">What to tell whoever asked for it.</param>
    /// <param name="History">Its history, when it was made.</param>
    public sealed record RoamingNetworkCreation(RoamingNetworkCreationStatus  Status,
                                                String                        Message,
                                                RoamingNetworkHistory?        History   = null)
    {

        /// <summary>
        /// Whether it was made.
        /// </summary>
        public Boolean Success
            => Status == RoamingNetworkCreationStatus.Created;

    }


    /// <summary>
    /// The points of interest of this hub: the roaming networks it serves with POI 3, their charging station
    /// operators, charging pools and everything else they hold, to query and to synchronize.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Below "/poi", beside the web interface and the JSON API: the POI API is a public one, readable from every
    /// origin and without signing in, as the points of interest are what everybody is meant to find. A browser
    /// opening a list of it, e.g. /poi/RNs/{RN_Id}/CPs, gets the web interface, whose page asks the very same URL
    /// for the data.
    /// Every directory below "poi", beside the configuration file, is the store of the history of one roaming
    /// network, read at the start.
    /// </para>
    /// <para>
    /// This hub is a roaming network operator, see <see cref="HubRoamingNetworkOperator"/>: made at its first
    /// start with keys of its own, and serving no roaming network yet. A new roaming network is made here with
    /// nothing but a name and a description, and this hub as its operator; its commits are signed by the
    /// operator's signing key. This hub adds its parties - charging station operators and the others, with their
    /// public keys - with change sets it signs. Everything else comes as change sets, which this hub takes from
    /// everybody - over HTTP and over the WebSocket of the network's history - because what a change set may
    /// change is decided by the signatures of its owners, not by who sent it: the second commit of a roaming
    /// network is so its first charging station operator, and the third one that operator's charging pools.
    /// </para>
    /// <para>
    /// Who may make a roaming network and add its parties is the permission poi:edit, which only the
    /// administrators have out of the box: it signs with the keys of this hub.
    /// </para>
    /// <para>
    /// The runtime status - whether an EVSE is charging, its power, its price right now - is no change set and
    /// signed by nobody: it is set over the WebSocket of the history by a charging station operator among the peers,
    /// or by an account of a role the "poi" section binds to one, and only for what that operator owns - see
    /// RoamingHub.POIStatus.cs.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// The directory the histories of the roaming networks are stored in, below the directory of the
        /// configuration file; one directory per roaming network.
        /// </summary>
        public const String  POIDirectoryName  = "poi";

        /// <summary>
        /// Where the POI API is, below the base path of this hub.
        /// </summary>
        public const String  POIPathSegment    = "poi";

        /// <summary>
        /// What the identification of a roaming network made here looks like: letters, digits, '-' and '_', at most
        /// 64 of them, beginning with a letter or a digit - as it is the name of its directory below "poi" as well.
        /// </summary>
        public static readonly Regex  NewRoamingNetworkIdPattern  = new ("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// What a roaming network to be made may say of itself; everything else is added by change sets.
        /// </summary>
        public static readonly IReadOnlyList<String>  NewRoamingNetworkProperties  = [ "@id", "@context", "name", "description" ];

        private readonly List<RoamingNetworkHistoryFileStore> poiStores = [];

        private readonly List<RoamingNetworkHistory> poiHistories = [];

        private readonly List<RoamingNetworkHistoryFileArchive> poiArchives = [];

        /// <summary>
        /// What the histories keep, and whether the commits they drop are archived: the "poi" section.
        /// </summary>
        private POIConfiguration poiConfiguration = new ();

        private readonly Lock poiLock = new ();

        /// <summary>
        /// How many directories below "poi" could not be read at the start: the roaming networks in them may name
        /// keys of the roaming network operator this hub is, which are therefore not forgotten when it replaces them.
        /// </summary>
        private Int32 unreadPOIStores;

        #endregion

        #region Properties

        /// <summary>
        /// The POI API: the roaming networks this hub serves, to query and to synchronize.
        /// </summary>
        public POI_API  POI             { get; private set; } = default!;

        /// <summary>
        /// The directory the histories of the roaming networks are stored in.
        /// </summary>
        public String   POIDirectory    { get; private set; } = default!;

        #endregion


        #region (private) BuildPOI()

        /// <summary>
        /// The POI API below "/poi", with the roaming networks of every store below the POI directory, keeping what the
        /// given "poi" section says, and archiving the commits they drop beside their stores, in "archive/".
        /// A store that can not be read is said in the log and left alone, and the others are served.
        /// </summary>
        private void BuildPOI(POIConfiguration? Configuration)
        {

            poiConfiguration = Configuration ?? new ();

            // Who signs what this hub makes of them: see RoamingHub.RoamingNetworkOperator.cs.
            BuildNetworkOperator();

            POIDirectory  = Path.Combine(Path.GetDirectoryName(ConfigFile.Path) ?? ".", POIDirectoryName);

            POI           = new POI_API(HTTPServer,
                                        TimeProvider:  TimeProvider,
                                        RootPath:      BasePath + POIPathSegment,
                                        HTMLView:      WebInterfacePage) {

                                // Everybody may submit: what a change set changes is the business of
                                // the signatures of its owners, which every commit checks.
                                AuthorizeChangeSetSubmission  = (request, changeSet) => true,

                                // Everybody may open the WebSocket of a history and read, and a charging station
                                // operator among the peers, or an account bound to one, set the runtime status of
                                // what it owns: see RoamingHub.POIStatus.cs.
                                AuthenticateWebSocketClient   = AuthenticatePOIClient,
                                AuthorizeStatusUpdate         = AuthorizePOIStatusUpdate,

                                // A browser opens it from the hub's own pages - by the name a trusted proxy in front
                                // says, too - and from the pages of the sites the "poi" section names, and from no
                                // other site's: see POI_API.AllowedOrigins.
                                AllowedOrigins                = poiConfiguration.EffectiveAllowedOrigins,
                                HostAskedFor                  = ProxyHostOf

                            };

            if (!Directory.Exists(POIDirectory))
                return;

            foreach (var directory in Directory.GetDirectories(POIDirectory).Order(StringComparer.Ordinal))
            {

                RoamingNetworkHistoryFileStore?    store    = null;
                RoamingNetworkHistoryFileArchive?  archive  = null;

                try
                {

                    store    = new RoamingNetworkHistoryFileStore(directory);
                    archive  = NewPOIArchive(directory);

                    if (!RoamingNetworkHistory.TryLoad(store, out var history, out var error, poiConfiguration.EffectiveRetention, Archive: archive))
                    {
                        Log.Error($"The roaming network stored in '{directory}' can not be read: {error}", "poi");
                        store.Dispose();
                        archive?.Dispose();
                        unreadPOIStores++;
                        continue;
                    }

                    if (!POI.AddRoamingNetworkHistory(history))
                    {
                        Log.Error($"The roaming network '{history.RoamingNetworkId}' stored in '{directory}' is served already.", "poi");
                        store.Dispose();
                        archive?.Dispose();
                        unreadPOIStores++;
                        continue;
                    }

                    lock (poiLock)
                    {
                        poiStores.Add(store);
                        poiHistories.Add(history);

                        if (archive is not null)
                            poiArchives.Add(archive);
                    }

                    WatchStore(history);

                    // The commits made here of a roaming network this hub operates are signed by its operator,
                    // with the signing key the network names.
                    if (NetworkOperator.CommitSigner(history.Head.RoamingNetwork) is RoamingNetworkCommitSigner signer)
                        history.Signers = [ signer ];

                    // Its heads attested by that key as well, the current one now: see RoamingHub.Attestations.cs.
                    Attest(history, TimeProvider.GetUtcNow());

                    Watch(history);

                    Log.Info($"The roaming network '{history.RoamingNetworkId}' from '{directory}', at revision {history.Head.Commit.Revision}.", "poi");

                }
                catch (Exception e)
                {
                    store?.Dispose();
                    archive?.Dispose();
                    unreadPOIStores++;
                    Log.Exception(e, $"The roaming network stored in '{directory}' can not be read.", "poi");
                }

            }

        }

        #endregion

        #region (private) Watch(History)

        /// <summary>
        /// Say every commit the given history takes in the log: change sets submitted, commits of other
        /// servers, and the merges of tips.
        /// </summary>
        private void Watch(RoamingNetworkHistory History)
        {

            var roamingNetworkId = History.RoamingNetworkId;

            // Called while the history is locked: the log only queues the entry.
            History.OnCommitAnnouncement += announcement => Log.Info(
                $"The roaming network '{roamingNetworkId}' is at revision {announcement.Revision}, commit {announcement.CommitId}" +
                (announcement.Description.FirstText() is { Length: > 0 } description ? $": {description}" : "."),
                "poi", "commit"
            );

        }

        #endregion


        #region (private) NewPOIArchive(Directory)

        /// <summary>
        /// The archive of the commits a history drops when compacting, in "archive/" beside its store in the given
        /// directory, unless the "poi" section says not to archive them.
        /// </summary>
        private RoamingNetworkHistoryFileArchive? NewPOIArchive(String Directory)

            => poiConfiguration.EffectiveArchive
                   ? new RoamingNetworkHistoryFileArchive(Path.Combine(Directory, POIConfiguration.ArchiveDirectoryName))
                   : null;

        #endregion

        #region (private) WatchStore(History)

        /// <summary>
        /// Say in the log what the store of the given history could not do, e.g. keep a status update or compact the
        /// log: the history goes on, in memory.
        /// </summary>
        private void WatchStore(RoamingNetworkHistory History)
        {

            var roamingNetworkId = History.RoamingNetworkId;

            History.OnStoreError += (history, description, exception) => {

                if (exception is not null)
                    Log.Exception(exception, $"The store of the roaming network '{roamingNetworkId}': {description}", "poi", "store");

                else
                    Log.Error($"The store of the roaming network '{roamingNetworkId}': {description}", "poi", "store");

            };

        }

        #endregion


        #region CreateRoamingNetwork(Id, Name = null, Description = null)

        /// <summary>
        /// Make a new roaming network, store it below "poi" and serve it: its genesis commit, revision 0, with
        /// this hub as its roaming network operator, signed by it.
        /// </summary>
        /// <param name="Id">Its identification, which is the name of its directory as well: see <see cref="NewRoamingNetworkIdPattern"/>.</param>
        /// <param name="Name">Its name.</param>
        /// <param name="Description">What it is.</param>
        public RoamingNetworkCreation CreateRoamingNetwork(RoamingNetwork_Id  Id,
                                                           I18NString?        Name          = null,
                                                           I18NString?        Description   = null)
        {

            #region What it must be

            if (!NewRoamingNetworkIdPattern.IsMatch(Id.ToString()))
                return Invalid($"'{Id}' can not be the identification of a roaming network made here: it is the name of its directory as well, " +
                               "so it is letters, digits, '-' and '_', at most 64 of them, beginning with a letter or a digit.");

            RoamingNetwork network;

            try
            {
                network = new RoamingNetwork(Id,
                                             Name:         Name        is not null && !Name.       IsNullOrEmpty() ? new ImmutableI18NString(Name)        : null,
                                             Description:  Description is not null && !Description.IsNullOrEmpty() ? new ImmutableI18NString(Description) : null,
                                             // Stating its keys from the genesis on: see RoamingHub.Attestations.cs.
                                             Operator:     OperatorStatingItsKeys(Id, TimeProvider.GetUtcNow()));
            }
            catch (Exception e)
            {
                return Invalid($"The roaming network '{Id}' can not be made: {e.Message}");
            }

            #endregion

            // Not while the keys of the operator are replaced: the network would name the one replaced.
            lock (networkOperatorLock)
            lock (poiLock)
            {

                #region Not there yet

                if (POI.TryGetRoamingNetworkHistory(Id, out _))
                    return new (RoamingNetworkCreationStatus.Exists, $"This hub serves a roaming network '{Id}' already.");

                var directory = Path.Combine(POIDirectory, Id.ToString());

                // Compared without case, as the identifications are: on a file system that tells them apart,
                // "QA" beside "qa" would be two directories of the same roaming network.
                if (Directory.Exists(POIDirectory) &&
                    Directory.GetDirectories(POIDirectory).Any(existing => String.Equals(Path.GetFileName(existing), Id.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    return new (RoamingNetworkCreationStatus.Exists,
                                $"There is a directory '{Id}' below '{POIDirectory}' already, which this hub did not serve at its start: " +
                                 "the log of that start says why. It is left as it is.");
                }

                #endregion

                RoamingNetworkHistoryFileStore?    store    = null;
                RoamingNetworkHistoryFileArchive?  archive  = null;

                try
                {

                    store        = new RoamingNetworkHistoryFileStore(directory);
                    archive      = NewPOIArchive(directory);

                    var history  = RoamingNetworkHistory.Start(network,
                                                               Signers:    [ NetworkOperator.CommitSigner(network)! ],
                                                               Store:      store,
                                                               Retention:  poiConfiguration.EffectiveRetention,
                                                               Archive:    archive);

                    Attest(history, TimeProvider.GetUtcNow());

                    if (!POI.AddRoamingNetworkHistory(history))
                        throw new InvalidOperationException($"The roaming network '{Id}' could not be served.");

                    poiStores.Add(store);
                    poiHistories.Add(history);

                    if (archive is not null)
                        poiArchives.Add(archive);

                    WatchStore(history);
                    Watch(history);

                    // Published as DATEX II, when the configuration names it.
                    PublishAsDatexII(history);

                    return new (RoamingNetworkCreationStatus.Created,
                                $"The roaming network '{Id}' was made, at revision 0, commit {history.Head.Commit.Id}, operated by this hub, '{NetworkOperator.Id}'. " +
                                $"Its parties are added now, with change sets this hub signs.",
                                history);

                }
                catch (Exception e)
                {

                    store?.Dispose();
                    archive?.Dispose();

                    try
                    {
                        if (Directory.Exists(directory))
                            Directory.Delete(directory, recursive: true);
                    }
                    catch (Exception cleanup)
                    {
                        Log.Exception(cleanup, $"What was begun of the roaming network '{Id}' in '{directory}' could not be taken away again.", "poi");
                    }

                    Log.Exception(e, $"The roaming network '{Id}' could not be stored in '{directory}'.", "poi");

                    return new (RoamingNetworkCreationStatus.NotSaved, $"The roaming network '{Id}' could not be stored: {e.Message}");

                }

            }

        }

        #endregion

        #region CreateRoamingNetwork(JSON)

        /// <summary>
        /// Make a new roaming network as the given JSON says: {"@id", "name"?, "description"?}, as POI 3 writes a
        /// roaming network, and nothing else: its operator is this hub, with the keys of this hub.
        /// </summary>
        /// <param name="JSON">The roaming network to be made.</param>
        public RoamingNetworkCreation CreateRoamingNetwork(JObject JSON)
        {

            if (JSON.Properties().FirstOrDefault(property => !NewRoamingNetworkProperties.Contains(property.Name)) is JProperty other)
                return Invalid(other.Name is "signingKeys" or "identityKeys" or RoamingNetwork.PropertyKeys.Operator
                                   ? $"A roaming network made here is operated by this hub, '{NetworkOperator.Id}', with its keys: '{other.Name}' is not given."
                                   : $"A roaming network is made with {String.Join(", ", NewRoamingNetworkProperties.Where(name => name != "@context"))} only, not with '{other.Name}': " +
                                      "everything else in it is added by change sets its owners sign.");

            if (JSON["@id"]?.Type != JTokenType.String || !RoamingNetwork_Id.TryParse(JSON["@id"]!.Value<String>()!, out var id))
                return Invalid("A roaming network needs an identification, '@id'.");

            // Its name and its description - and its context, where it says one - are read as a roaming
            // network reads them. When it is made is when this hub makes it; given here only as a roaming
            // network is read with one.
            var parsed = new JObject(
                             new JProperty("@id",      id.ToString()),
                             new JProperty("created",  Timestamp.Now.ToISO8601())
                         );

            foreach (var name in new[] { "@context", "name", "description" })
                if (JSON[name] is JToken text && text.Type != JTokenType.Null)
                    parsed[name] = text;

            if (!RoamingNetwork.TryParse(parsed, out var network, out var error))
                return Invalid($"The roaming network '{id}' can not be made: {error}");

            return CreateRoamingNetwork(id,
                                        network.Name.       IsNullOrEmpty() ? null : network.Name.       ToMutable(),
                                        network.Description.IsNullOrEmpty() ? null : network.Description.ToMutable());

        }

        #endregion

        #region (static) TryParsePublicKey(JSON, out Key, out ErrorResponse)

        /// <summary>
        /// A public key of a party: as POI 3 writes it, {"@id"?, "key", "notBefore"?, "notAfter"?}, or the Base64
        /// of its COSE key alone. Its identification is calculated where it is not given; a private key is refused,
        /// as it is no longer private once it was sent here.
        /// </summary>
        /// <param name="JSON">The key.</param>
        /// <param name="Key">The public key.</param>
        /// <param name="ErrorResponse">Why it is none.</param>
        public static Boolean TryParsePublicKey(JToken?                                 JSON,
                                                [NotNullWhen(true)]  out POIPublicKey?  Key,
                                                [NotNullWhen(false)] out String?        ErrorResponse)
        {

            Key = null;

            var json = JSON?.Type == JTokenType.String
                           ? new JObject(new JProperty("key", JSON.Value<String>()))
                           : JSON as JObject;

            if (json is null || json["key"]?.Type != JTokenType.String)
            {
                ErrorResponse = "a key is the Base64 of its COSE key, or {\"key\": ...} as POI writes it";
                return false;
            }

            Byte[] bytes;

            try
            {
                bytes = Convert.FromBase64String(json["key"]!.Value<String>()!.Trim());
            }
            catch (FormatException)
            {
                ErrorResponse = "a key is the Base64 of its COSE key, and this is no Base64";
                return false;
            }

            if (!COSEKey.TryParse(bytes, out var coseKey, out var coseError))
            {
                ErrorResponse = $"this is no COSE key: {coseError}";
                return false;
            }

            if (coseKey.IsPrivate)
            {
                ErrorResponse = "this is a private key, and only its public half is given to anybody: " +
                                "it is no longer private now, so make a new one";
                return false;
            }

            json = (JObject) json.DeepClone();
            json["key"] = Convert.ToBase64String(bytes);

            if (json["@id"] is null)
                json["@id"] = new POIPublicKey(coseKey).Id;

            return POIPublicKey.TryParse(json, out Key, out ErrorResponse);

        }

        #endregion

        #region (private static) TryParsePublicKeys(JSON, Kind, out Keys, out ErrorResponse)

        private static Boolean TryParsePublicKeys(JToken?                                 JSON,
                                                  String                                  Kind,
                                                  out IReadOnlyList<POIPublicKey>         Keys,
                                                  [NotNullWhen(false)] out String?        ErrorResponse)
        {

            var keys  = new List<POIPublicKey>();
            Keys      = keys;

            if (JSON is not JArray array || array.Count == 0)
            {
                ErrorResponse = $"it needs at least one {Kind}, '{Kind.Replace(" key", "")}Keys': [...]";
                return false;
            }

            for (var index = 0; index < array.Count; index++)
            {

                if (!TryParsePublicKey(array[index], out var key, out var error))
                {
                    ErrorResponse = $"{Kind} {index + 1}: {error}";
                    return false;
                }

                keys.Add(key);

            }

            ErrorResponse = null;
            return true;

        }

        #endregion

        #region (private static) Invalid(Message)

        private static RoamingNetworkCreation Invalid(String Message)

            => new (RoamingNetworkCreationStatus.Invalid, Message);

        #endregion


        #region (private) DisposePOI()

        /// <summary>
        /// Let go of the POI API and of the stores of its roaming networks.
        /// </summary>
        private async ValueTask DisposePOI()
        {

            // Before the roaming networks they publish.
            await DisposeDatexIIAsync();

            await POI.DisposeAsync();

            lock (poiLock)
            {

                // Detached first: a compaction running in the background is waited for, and none starts into a store disposed.
                foreach (var history in poiHistories)
                    history.DetachStore();

                poiHistories.Clear();

                foreach (var store in poiStores)
                    store.Dispose();

                poiStores.Clear();

                foreach (var archive in poiArchives)
                    archive.Dispose();

                poiArchives.Clear();

            }

        }

        #endregion

    }

}
