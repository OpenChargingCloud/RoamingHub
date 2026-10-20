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
using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Configuration
{

    /// <summary>
    /// The "poi" section: what the histories of the roaming networks below "poi" keep in their stores, and whether the
    /// commits they drop when compacting go into an archive beside them.
    /// </summary>
    /// <remarks>
    /// A history writes every commit and every status update to the log of its store, and compacts the log once it has
    /// CompactAfterLogEntries entries: the status updates go into a new base, and the commits more than RetainCommits
    /// revisions behind the head are dropped from it - into the archive, so that they are served and proved as before
    /// (SPECIFICATION.md of WWCP POI, section 10.6). Without the archive, they are gone.
    /// </remarks>
    /// <param name="RetainCommits">How many revisions behind the head the commits are kept in the store when compacting; 1000 by default.</param>
    /// <param name="CompactAfterLogEntries">After how many entries of its log a history compacts it; 100 000 by default.</param>
    /// <param name="VersionCheckpointInterval">The versions of every this many revisions are kept in memory; 100 by default.</param>
    /// <param name="RecentVersions">The versions of this many latest commits are kept in memory; 100 by default.</param>
    /// <param name="Archive">Whether the commits dropped go into the archive beside the store, "archive/"; true by default.</param>
    public sealed record POIConfiguration(UInt32?   RetainCommits               = null,
                                          UInt32?   CompactAfterLogEntries      = null,
                                          UInt32?   VersionCheckpointInterval   = null,
                                          UInt32?   RecentVersions              = null,
                                          Boolean?  Archive                     = null)
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

            Configuration = new POIConfiguration(retainCommits,
                                                 compactAfterLogEntries,
                                                 versionCheckpointInterval,
                                                 recentVersions,
                                                 archive);
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

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()
        {

            var retention = EffectiveRetention;

            return $"points of interest compacted after {retention.CompactAfterLogEntries} log entries, keeping {retention.RetainCommits} revisions" +
                   (EffectiveArchive ? ", the commits dropped archived" : ", the commits dropped not archived");

        }

        #endregion

    }

}
