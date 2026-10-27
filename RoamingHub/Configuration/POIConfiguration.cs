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
    /// </remarks>
    /// <param name="RetainCommits">How many revisions behind the head the commits are kept in the store when compacting; 1000 by default.</param>
    /// <param name="CompactAfterLogEntries">After how many entries of its log a history compacts it; 100 000 by default.</param>
    /// <param name="VersionCheckpointInterval">The versions of every this many revisions are kept in memory; 100 by default.</param>
    /// <param name="RecentVersions">The versions of this many latest commits are kept in memory; 100 by default.</param>
    /// <param name="Archive">Whether the commits dropped go into the archive beside the store, "archive/"; true by default.</param>
    /// <param name="StatusRoles">The roles whose accounts may set the runtime status, each with the charging station operators whose status it may set; none by default.</param>
    /// <param name="AllowedOrigins">The origins of the web pages of other sites that may open the WebSocket of a history, "https://map.example.org"; none by default.</param>
    public sealed record POIConfiguration(UInt32?                                                                   RetainCommits               = null,
                                          UInt32?                                                                   CompactAfterLogEntries      = null,
                                          UInt32?                                                                   VersionCheckpointInterval   = null,
                                          UInt32?                                                                   RecentVersions              = null,
                                          Boolean?                                                                  Archive                     = null,
                                          IReadOnlyDictionary<String, IReadOnlyList<ChargingStationOperator_Id>>?  StatusRoles                 = null,
                                          IReadOnlyList<String>?                                                    AllowedOrigins              = null)
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

            Configuration = new POIConfiguration(retainCommits,
                                                 compactAfterLogEntries,
                                                 versionCheckpointInterval,
                                                 recentVersions,
                                                 archive,
                                                 statusRoles,
                                                 allowedOrigins);
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
                        : "");

        }

        #endregion

    }

}
