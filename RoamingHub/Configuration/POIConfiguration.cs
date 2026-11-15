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

using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.Web;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Configuration
{

    /// <summary>
    /// The "poi" section: what the histories of the roaming networks below "poi" keep in their stores, whether the
    /// commits they drop when compacting go into an archive beside them, and whose runtime status the accounts of which
    /// roles may set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A history writes every commit and every status update to the log of its store, and compacts the log once it has
    /// CompactAfterLogEntries entries: the status updates go into a new base, and the commits more than RetainCommits
    /// revisions behind the head are dropped from it - into the archive, so that they are served and proved as before
    /// (SPECIFICATION.md of WWCP POI, section 10.6). Without the archive, they are gone.
    /// </para>
    /// <para>
    /// StatusRoles binds roles to charging station operators: an account holding one of them, and with it the
    /// permission "poi-status:edit", may set the runtime status of what these operators own over the WebSocket of a
    /// history - "statusRoles": { "gef-feed": [ "DE*GEF" ] }. Both are written down on purpose, the permission in the
    /// "roles" section and the operators here: a role nobody gave the permission, or one this hub does not have, stops
    /// the start rather than be a binding that quietly lets nobody set anything. See RoamingHub.POIStatus.cs.
    /// </para>
    /// <para>
    /// AllowedOrigins names the web pages of other sites that may open the WebSocket of a history in a browser -
    /// "allowedOrigins": [ "https://map.example.org" ]. A browser says whose page asks, and a page of another site is
    /// refused unless it is named here; the hub's own pages, and programs that are no browsers, always may.
    /// </para>
    /// <para>
    /// URL says where the POI API of this hub is reached, "url": "https://hub-b.example/poi": no hub knows its own
    /// public URL. Follow names the roaming networks of other servers this hub follows, by their trust anchors, and
    /// whether it asks to hold the keys of their operators as well - "follow": [ { "trustAnchor": "...", "hold": true } ].
    /// What it says is done at every start as if somebody had said it then; what it names no longer is not undone.
    /// "signCommits" beside a trust anchor says which commits of that roaming network this hub signs by itself as one
    /// of the holders of the keys of its operator: "none", "approved" - those of the change sets it signed, the
    /// default - or "all" it took. See SHAREHOLDERS.md, phase 6, and RoamingHub.POIConfigured.cs.
    /// </para>
    /// </remarks>
    /// <param name="RetainCommits">How many revisions behind the head the commits are kept in the store when compacting; 1000 by default.</param>
    /// <param name="CompactAfterLogEntries">After how many entries of its log a history compacts it; 100 000 by default.</param>
    /// <param name="VersionCheckpointInterval">The versions of every this many revisions are kept in memory; 100 by default.</param>
    /// <param name="RecentVersions">The versions of this many latest commits are kept in memory; 100 by default.</param>
    /// <param name="Archive">Whether the commits dropped go into the archive beside the store, "archive/"; true by default.</param>
    /// <param name="StatusRoles">The roles whose accounts may set the runtime status, each with the charging station operators whose status it may set; none by default.</param>
    /// <param name="AllowedOrigins">The origins of the web pages of other sites that may open the WebSocket of a history, "https://map.example.org"; none by default.</param>
    /// <param name="URL">Where the POI API of this hub is reached, "https://hub-b.example/poi"; not said by default.</param>
    /// <param name="Follow">The roaming networks of other servers this hub follows; none by default.</param>
    public sealed record POIConfiguration(UInt32?                                                                   RetainCommits               = null,
                                          UInt32?                                                                   CompactAfterLogEntries      = null,
                                          UInt32?                                                                   VersionCheckpointInterval   = null,
                                          UInt32?                                                                   RecentVersions              = null,
                                          Boolean?                                                                  Archive                     = null,
                                          IReadOnlyDictionary<String, IReadOnlyList<ChargingStationOperator_Id>>?  StatusRoles                 = null,
                                          IReadOnlyList<String>?                                                    AllowedOrigins              = null,
                                          org.GraphDefined.Vanaheimr.Hermod.HTTP.URL?                               URL                         = null,
                                          IReadOnlyList<POIFollow>?                                                 Follow                      = null)
    {

        #region Data

        /// <summary>
        /// The name of the section.
        /// </summary>
        public const String  SectionName           = "poi";

        /// <summary>
        /// The name of the directory of the archive, beside the store of a roaming network.
        /// </summary>
        public const String  ArchiveDirectoryName  = "archive";

        /// <summary>
        /// The name of the field binding roles to charging station operators.
        /// </summary>
        public const String  StatusRolesName       = "statusRoles";

        /// <summary>
        /// The most charging station operators one role may be bound to.
        /// </summary>
        public const Int32   MaxStatusOperators    = 1000;

        /// <summary>
        /// The name of the field of the origins of other sites that may open the WebSocket of a history.
        /// </summary>
        public const String  AllowedOriginsName    = "allowedOrigins";

        /// <summary>
        /// The most origins the section may name.
        /// </summary>
        public const Int32   MaxAllowedOrigins     = 100;

        /// <summary>
        /// The name of the field saying where the POI API of this hub is reached.
        /// </summary>
        public const String  URLName               = "url";

        /// <summary>
        /// The name of the field of the roaming networks of other servers this hub follows.
        /// </summary>
        public const String  FollowName            = "follow";

        /// <summary>
        /// The most roaming networks the section may have this hub follow.
        /// </summary>
        public const Int32   MaxFollowed           = 100;

        /// <summary>
        /// The name of the field of a roaming network followed saying which of its commits this hub signs by itself.
        /// </summary>
        public const String  SignCommitsName       = "signCommits";
        public const String  AlsoAtName            = "alsoAt";

        /// <summary>
        /// How many further copies of a roaming network followed an entry may name.
        /// </summary>
        public const Int32   MaxAlsoAt             = 16;

        private static readonly RoamingNetworkHistoryRetention  defaults  = new ();

        #endregion

        #region Properties

        /// <summary>
        /// What the histories keep: the defaults of WWCP POI, where the section says nothing.
        /// </summary>
        public RoamingNetworkHistoryRetention  EffectiveRetention

            => new () {
                   RetainCommits              = RetainCommits             ?? defaults.RetainCommits,
                   CompactAfterLogEntries     = CompactAfterLogEntries    ?? defaults.CompactAfterLogEntries,
                   VersionCheckpointInterval  = VersionCheckpointInterval ?? defaults.VersionCheckpointInterval,
                   RecentVersions             = (Int32?) RecentVersions   ?? defaults.RecentVersions
               };

        /// <summary>
        /// Whether the commits dropped go into the archive.
        /// </summary>
        public Boolean  EffectiveArchive
            => Archive ?? true;

        /// <summary>
        /// The roles whose accounts may set the runtime status, with their operators: none, where the section says nothing.
        /// </summary>
        public IReadOnlyDictionary<String, IReadOnlyList<ChargingStationOperator_Id>>  EffectiveStatusRoles
            => StatusRoles ?? new Dictionary<String, IReadOnlyList<ChargingStationOperator_Id>>();

        /// <summary>
        /// The origins of other sites that may open the WebSocket of a history: none, where the section says nothing.
        /// </summary>
        public IReadOnlyList<String>  EffectiveAllowedOrigins
            => AllowedOrigins ?? [];

        /// <summary>
        /// The roaming networks of other servers this hub follows: none, where the section says nothing.
        /// </summary>
        public IReadOnlyList<POIFollow>  EffectiveFollow
            => Follow ?? [];

        /// <summary>
        /// Where this hub serves the given roaming network, ~/RNs/{RN_Id} below its POI API, where the section says
        /// where that is reached.
        /// </summary>
        public org.GraphDefined.Vanaheimr.Hermod.HTTP.URL? URLOf(RoamingNetwork_Id RoamingNetworkId)

            => URL.HasValue
                   ? org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.Parse($"{URL.Value.ToString().TrimEnd('/')}/RNs/{RoamingNetworkId}")
                   : null;

        /// <summary>
        /// Which commits of the given roaming network followed this hub signs by itself: those of the change sets it
        /// signed, where the section says nothing of it.
        /// </summary>
        public POISignCommits SignCommitsOf(RoamingNetwork_Id RoamingNetworkId)

            => EffectiveFollow.FirstOrDefault(follow => follow.TrustAnchor.Id == RoamingNetworkId)?.SignCommits
                   ?? POISignCommits.Approved;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// The "poi" section, or the one sentence that says what is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                    JSON,
                                       [NotNullWhen(true)]  out POIConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?            Error)
        {

            Configuration = null;

            if (!ConfigurationReader.TryReadUInt32 (JSON, "retainCommits",              SectionName, 0, UInt32.MaxValue,         out var retainCommits,              out Error) ||
                !ConfigurationReader.TryReadUInt32 (JSON, "compactAfterLogEntries",     SectionName, 1, UInt32.MaxValue,         out var compactAfterLogEntries,     out Error) ||
                !ConfigurationReader.TryReadUInt32 (JSON, "versionCheckpointInterval",  SectionName, 1, UInt32.MaxValue,         out var versionCheckpointInterval,  out Error) ||
                !ConfigurationReader.TryReadUInt32 (JSON, "recentVersions",             SectionName, 0, (UInt32) Int32.MaxValue, out var recentVersions,             out Error) ||
                !ConfigurationReader.TryReadBoolean(JSON, "archive",                    SectionName,                             out var archive,                    out Error))
            {
                return false;
            }

            if (!TryParseStatusRoles(JSON, out var statusRoles, out Error))
                return false;

            if (!TryParseAllowedOrigins(JSON, out var allowedOrigins, out Error))
                return false;

            if (!TryParseURL(JSON, URLName, SectionName, out var url, out Error))
                return false;

            if (!TryParseFollow(JSON, out var follow, out Error))
                return false;

            Configuration = new POIConfiguration(retainCommits,
                                                 compactAfterLogEntries,
                                                 versionCheckpointInterval,
                                                 recentVersions,
                                                 archive,
                                                 statusRoles,
                                                 allowedOrigins,
                                                 url,
                                                 follow);
            return true;

        }

        #endregion

        #region (private static) TryParseURL(JSON, Name, Path, out URL, out Error)

        /// <summary>
        /// A URL over http or https without a query, or the one sentence that says what is wrong with it.
        /// </summary>
        private static Boolean TryParseURL(JObject                                           JSON,
                                           String                                            Name,
                                           String                                            Path,
                                           out org.GraphDefined.Vanaheimr.Hermod.HTTP.URL?   URL,
                                           [NotNullWhen(false)] out String?                  Error)
        {

            URL = null;

            if (!ConfigurationReader.TryReadString(JSON, Name, Path, 2048, out var text, out Error))
                return false;

            if (text is null)
                return true;

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)     ||
                uri.Scheme is not ("http" or "https")                   ||
                uri.Host.Length  == 0                                   ||
                uri.UserInfo.Length > 0                                 ||
                uri.Query.Length    > 0                                 ||
                uri.Fragment.Length > 0                                 ||
                !org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.TryParse(text.TrimEnd('/'), out var url))
            {
                Error = $"'{Path}.{Name}' is '{text}', which is no URL over http or https without a query, e.g. \"https://hub.example.org/poi\".";
                return false;
            }

            URL = url;
            return true;

        }

        #endregion

        #region (private static) TryParseFollow(JSON, out Follow, out Error)

        /// <summary>
        /// The roaming networks of other servers this hub follows, [ { "trustAnchor", "url"?, "hold"?, "signCommits"?, "alsoAt"? } ],
        /// or the one sentence that says what is wrong with them.
        /// </summary>
        private static Boolean TryParseFollow(JObject                           JSON,
                                              out IReadOnlyList<POIFollow>?     Follow,
                                              [NotNullWhen(false)] out String?  Error)
        {

            Follow  = null;
            Error   = null;

            if (!JSON.TryGetValue(FollowName, out var token) || token.Type == JTokenType.Null)
                return true;

            var path = $"{SectionName}.{FollowName}";

            if (token is not JArray followJSON || followJSON.Any(entry => entry is not JObject))
            {
                Error = $"'{path}' must be a JSON array of the roaming networks followed, each with its trust anchor: [ {{ \"trustAnchor\": \"...\", \"hold\": true }} ].";
                return false;
            }

            if (followJSON.Count > MaxFollowed)
            {
                Error = $"'{path}' may name at most {MaxFollowed} roaming networks.";
                return false;
            }

            var follow  = new List<POIFollow>();
            var number  = 0;

            foreach (var entry in followJSON.Cast<JObject>())
            {

                var entryPath = $"{path}[{number++}]";

                if (entry.Properties().FirstOrDefault(property => property.Name is not ("trustAnchor" or URLName or "hold" or SignCommitsName or AlsoAtName)) is JProperty other)
                {
                    Error = $"'{entryPath}' says '{other.Name}', which is none of \"trustAnchor\", \"{URLName}\", \"hold\", \"{SignCommitsName}\" and \"{AlsoAtName}\".";
                    return false;
                }

                if (!ConfigurationReader.TryReadString(entry, "trustAnchor", entryPath, 65536, out var anchorText, out Error))
                    return false;

                if (anchorText is null || !RoamingNetworkTrustAnchor.TryParse(anchorText.Trim(), out var trustAnchor, out var anchorError))
                {
                    Error = $"'{entryPath}.trustAnchor' is no trust anchor of a roaming network, the text of its QR code or its URL, as its operator hands it out.";
                    return false;
                }

                if (!TryParseURL(entry, URLName, entryPath, out var url, out Error))
                    return false;

                if (url is null && trustAnchor.URLs.IsDefaultOrEmpty)
                {
                    Error = $"'{entryPath}.trustAnchor' names no URL of the roaming network '{trustAnchor.Id}': say where it is followed, \"{URLName}\".";
                    return false;
                }

                if (!ConfigurationReader.TryReadBoolean(entry, "hold", entryPath, out var hold, out Error))
                    return false;

                if (!ConfigurationReader.TryReadString(entry, SignCommitsName, entryPath, 16, out var signCommitsText, out Error))
                    return false;

                POISignCommits signCommits;

                switch (signCommitsText)
                {

                    case null:        signCommits = POISignCommits.Approved;  break;
                    case "none":      signCommits = POISignCommits.None;      break;
                    case "approved":  signCommits = POISignCommits.Approved;  break;
                    case "all":       signCommits = POISignCommits.All;       break;

                    default:
                        Error = $"'{entryPath}.{SignCommitsName}' is '{signCommitsText}', which is none of \"none\", \"approved\" and \"all\".";
                        return false;

                }

                if (follow.Any(known => known.TrustAnchor.Id == trustAnchor.Id))
                {
                    Error = $"'{path}' names the roaming network '{trustAnchor.Id}' twice.";
                    return false;
                }

                #region The copies of the other holders, where the roaming network does not name them

                var alsoAt = new List<org.GraphDefined.Vanaheimr.Hermod.HTTP.URL>();

                if (entry.TryGetValue(AlsoAtName, out var alsoAtToken) && alsoAtToken.Type != JTokenType.Null)
                {

                    if (alsoAtToken is not JArray alsoAtJSON || alsoAtJSON.Count > MaxAlsoAt || alsoAtJSON.Any(also => also.Type != JTokenType.String))
                    {
                        Error = $"'{entryPath}.{AlsoAtName}' must be a JSON array of at most {MaxAlsoAt} URLs of the roaming network '{trustAnchor.Id}' at other hubs, e.g. [ \"https://hub-c.example.org/poi/RNs/{trustAnchor.Id}\" ].";
                        return false;
                    }

                    foreach (var text in alsoAtJSON.Select(also => also.Value<String>()!))
                    {

                        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)     ||
                            uri.Scheme is not ("http" or "https")                   ||
                            uri.Host.Length  == 0                                   ||
                            uri.UserInfo.Length > 0                                 ||
                            uri.Query.Length    > 0                                 ||
                            uri.Fragment.Length > 0                                 ||
                            !uri.AbsolutePath.TrimEnd('/').EndsWith($"/RNs/{trustAnchor.Id}", StringComparison.Ordinal) ||
                            !org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.TryParse(text.TrimEnd('/'), out var also))
                        {
                            Error = $"'{entryPath}.{AlsoAtName}' names '{text}', which is no URL of the roaming network '{trustAnchor.Id}' at another hub, over http or https, ending in 'RNs/{trustAnchor.Id}'.";
                            return false;
                        }

                        alsoAt.Add(also);

                    }

                }

                #endregion

                follow.Add(new POIFollow(trustAnchor, anchorText.Trim(), url, hold ?? false, signCommits) { AlsoAt = alsoAt });

            }

            Follow = follow;
            return true;

        }

        #endregion

        #region (private static) TryParseAllowedOrigins(JSON, out AllowedOrigins, out Error)

        /// <summary>
        /// The origins of other sites that may open the WebSocket of a history, [ "https://map.example.org" ], or the one
        /// sentence that says what is wrong with them. An origin is what a browser sends: the scheme, the host and the port
        /// where it is not the scheme's own, and nothing after them.
        /// </summary>
        private static Boolean TryParseAllowedOrigins(JObject                           JSON,
                                                      out IReadOnlyList<String>?        AllowedOrigins,
                                                      [NotNullWhen(false)] out String?  Error)
        {

            AllowedOrigins = null;

            var path = $"{SectionName}.{AllowedOriginsName}";

            if (!ConfigurationReader.TryReadStrings(JSON, AllowedOriginsName, SectionName, MaxAllowedOrigins, 256, out var texts, out Error))
                return false;

            if (texts is null)
                return true;

            var origins = new List<String>();

            foreach (var text in texts)
            {

                if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https") ||
                    uri.Host.Length == 0 ||
                    uri.UserInfo.Length > 0 ||
                    text.TrimEnd('/').Length != uri.GetLeftPart(UriPartial.Authority).Length)
                {
                    Error = $"'{path}' names '{text}', which is no origin: the scheme and the host of a web page, and its port where it is not the scheme's own, e.g. \"https://map.example.org\".";
                    return false;
                }

                var origin = uri.GetLeftPart(UriPartial.Authority);

                if (!origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                    origins.Add(origin);

            }

            AllowedOrigins = origins;
            return true;

        }

        #endregion

        #region (private static) TryParseStatusRoles(JSON, out StatusRoles, out Error)

        /// <summary>
        /// The roles bound to charging station operators, { role: [ operator, ... ] }, or the one sentence that says what
        /// is wrong with them. Whether this hub has the roles and gave them the permission is asked when it is made, as
        /// only the node knows its roles: see RoamingHub.POIStatus.cs.
        /// </summary>
        private static Boolean TryParseStatusRoles(JObject                                                                      JSON,
                                                   out IReadOnlyDictionary<String, IReadOnlyList<ChargingStationOperator_Id>>?  StatusRoles,
                                                   [NotNullWhen(false)] out String?                                             Error)
        {

            StatusRoles  = null;
            Error        = null;

            if (!JSON.TryGetValue(StatusRolesName, out var token) || token.Type == JTokenType.Null)
                return true;

            var path = $"{SectionName}.{StatusRolesName}";

            if (token is not JObject rolesJSON)
            {
                Error = $"'{path}' must be a JSON object, a role and the charging station operators whose status it may set: {{ \"gef-feed\": [ \"DE*GEF\" ] }}.";
                return false;
            }

            if (rolesJSON.Count > RolesConfiguration.MaxRoles)
            {
                Error = $"'{path}' may name at most {RolesConfiguration.MaxRoles} roles.";
                return false;
            }

            var statusRoles = new Dictionary<String, IReadOnlyList<ChargingStationOperator_Id>>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in rolesJSON.Properties())
            {

                var role = property.Name.Trim();

                if (!Role.IsRoleName(role))
                {
                    Error = $"'{path}' names '{property.Name}', which is not a role name: a letter, then letters, digits, '-' or '_', {Role.MinNameLength} to {Role.MaxNameLength} characters.";
                    return false;
                }

                if (statusRoles.ContainsKey(role))
                {
                    Error = $"'{path}' names the role '{role}' twice.";
                    return false;
                }

                if (!ConfigurationReader.TryReadStrings(rolesJSON, property.Name, path, MaxStatusOperators, 64, out var texts, out Error))
                    return false;

                if (texts is null || texts.Count == 0)
                {
                    Error = $"'{path}.{role}' names no charging station operator, so the role would set nothing: name at least one, e.g. [ \"DE*GEF\" ], or leave the role out.";
                    return false;
                }

                var operators = new List<ChargingStationOperator_Id>();

                foreach (var text in texts)
                {

                    if (!ChargingStationOperator_Id.TryParse(text, out var operatorId))
                    {
                        Error = $"'{path}.{role}' names '{text}', which is no charging station operator, e.g. \"DE*GEF\".";
                        return false;
                    }

                    if (!operators.Contains(operatorId))
                        operators.Add(operatorId);

                }

                statusRoles.Add(role, operators);

            }

            StatusRoles = statusRoles;
            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section as it is written to the file.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (RetainCommits.HasValue)              json.Add("retainCommits",              RetainCommits.Value);
            if (CompactAfterLogEntries.HasValue)     json.Add("compactAfterLogEntries",     CompactAfterLogEntries.Value);
            if (VersionCheckpointInterval.HasValue)  json.Add("versionCheckpointInterval",  VersionCheckpointInterval.Value);
            if (RecentVersions.HasValue)             json.Add("recentVersions",             RecentVersions.Value);
            if (Archive.HasValue)                    json.Add("archive",                    Archive.Value);

            if (StatusRoles is not null)
                json.Add(StatusRolesName, new JObject(StatusRoles.Select(statusRole => new JProperty(statusRole.Key, new JArray(statusRole.Value.Select(operatorId => operatorId.ToString()))))));

            if (AllowedOrigins is not null)
                json.Add(AllowedOriginsName, new JArray(AllowedOrigins));

            if (URL.HasValue)
                json.Add(URLName, URL.Value.ToString());

            if (Follow is not null)
                json.Add(FollowName, new JArray(Follow.Select(follow => follow.ToJSON())));

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()
        {

            var retention = EffectiveRetention;

            return $"points of interest compacted after {retention.CompactAfterLogEntries} log entries, keeping {retention.RetainCommits} revisions" +
                   (EffectiveArchive ? ", the commits dropped archived" : ", the commits dropped not archived") +
                   (EffectiveStatusRoles.Count > 0
                        ? ", their status set by " + String.Join(", ", EffectiveStatusRoles.Select(statusRole => $"'{statusRole.Key}' for {String.Join(", ", statusRole.Value)}"))
                        : "") +
                   (EffectiveAllowedOrigins.Count > 0
                        ? ", their WebSocket also opened by pages of " + String.Join(", ", EffectiveAllowedOrigins)
                        : "") +
                   (URL.HasValue
                        ? $", reached at {URL.Value}"
                        : "") +
                   (EffectiveFollow.Count > 0
                        ? ", following " + String.Join(", ", EffectiveFollow.Select(follow => $"'{follow.TrustAnchor.Id}'{(follow.Hold ? " to hold the keys of its operator as well" : "")}{(follow.SignCommits switch { POISignCommits.None => ", signing none of its commits by itself", POISignCommits.All => ", signing all of its commits", _ => "" })}"))
                        : "");

        }

        #endregion

    }


    /// <summary>
    /// Which commits of a roaming network followed a hub signs by itself, as one of the holders of the keys of its
    /// operator (SHAREHOLDERS.md, phase 6.4). Its signature says that the history took the commit rightly, which the
    /// hub verified to take it into its copy; not that whoever runs the hub agrees with what it changes.
    /// </summary>
    public enum POISignCommits
    {

        /// <summary>
        /// None: every commit is signed when somebody says so.
        /// </summary>
        None,

        /// <summary>
        /// The commits of the change sets this hub signed; every other when somebody says so.
        /// </summary>
        Approved,

        /// <summary>
        /// Every commit this hub took into its copy.
        /// </summary>
        All

    }


    /// <summary>
    /// A roaming network of another server the "poi" section has this hub follow.
    /// </summary>
    /// <param name="TrustAnchor">Its trust anchor, as its operator hands it out.</param>
    /// <param name="TrustAnchorText">The trust anchor as the file says it: the text of its QR code, or its URL.</param>
    /// <param name="URL">Where it is followed, ~/RNs/{RN_Id} at the other server; the first URL of the trust anchor otherwise.</param>
    /// <param name="Hold">Whether this hub asks to hold the keys of its operator as well, once.</param>
    /// <param name="SignCommits">Which of its commits this hub signs by itself as one of the holders of the keys of its operator.</param>
    public sealed record POIFollow(RoamingNetworkTrustAnchor                    TrustAnchor,
                                   String                                       TrustAnchorText,
                                   org.GraphDefined.Vanaheimr.Hermod.HTTP.URL?  URL,
                                   Boolean                                      Hold,
                                   POISignCommits                               SignCommits   = POISignCommits.Approved)
    {

        /// <summary>
        /// The copies of the other holders of the keys of its operator, ~/RNs/{RN_Id} at their hubs, which this hub
        /// is connected with as one of them: beside those the roaming network names itself, its "urls", which are
        /// https only (SHAREHOLDERS.md, phase 7.1).
        /// </summary>
        public IReadOnlyList<org.GraphDefined.Vanaheimr.Hermod.HTTP.URL> AlsoAt { get; init; } = [];

        /// <summary>
        /// The entry as it is written to the file.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(new JProperty("trustAnchor", TrustAnchorText));

            if (URL.HasValue)
                json.Add(POIConfiguration.URLName, URL.Value.ToString());

            if (Hold)
                json.Add("hold", true);

            if (SignCommits != POISignCommits.Approved)
                json.Add(POIConfiguration.SignCommitsName, SignCommits.ToString().ToLowerInvariant());

            if (AlsoAt.Count > 0)
                json.Add(POIConfiguration.AlsoAtName, new JArray(AlsoAt.Select(also => also.ToString())));

            return json;

        }

    }

}
