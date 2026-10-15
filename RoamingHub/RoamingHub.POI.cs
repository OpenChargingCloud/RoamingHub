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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The points of interest of this hub: the roaming networks it serves with POI 3, their charging station
    /// operators, charging pools and everything else they hold, to query and to synchronize.
    /// </summary>
    /// <remarks>
    /// Below "/poi", beside the web interface and the JSON API: the POI API is a public one, readable from every
    /// origin and without signing in, as the points of interest are what everybody is meant to find. A browser
    /// opening a list of it, e.g. /poi/RNs/{RN_Id}/CPs, gets the web interface, whose page asks the very same URL
    /// for the data.
    /// Every directory below "poi", beside the configuration file, is the store of the history of one roaming
    /// network, read at the start.
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

        private readonly List<RoamingNetworkHistoryFileStore> poiStores = [];

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
        /// The POI API below "/poi", with the roaming networks of every store below the POI directory.
        /// A store that can not be read is said in the log and left alone, and the others are served.
        /// </summary>
        private void BuildPOI()
        {

            POIDirectory  = Path.Combine(Path.GetDirectoryName(ConfigFile.Path) ?? ".", POIDirectoryName);

            POI           = new POI_API(HTTPServer,
                                        TimeProvider:  TimeProvider,
                                        RootPath:      BasePath + POIPathSegment,
                                        HTMLView:      WebInterfacePage);

            if (!Directory.Exists(POIDirectory))
                return;

            foreach (var directory in Directory.GetDirectories(POIDirectory).Order(StringComparer.Ordinal))
            {

                RoamingNetworkHistoryFileStore? store = null;

                try
                {

                    store = new RoamingNetworkHistoryFileStore(directory);

                    if (!RoamingNetworkHistory.TryLoad(store, out var history, out var error))
                    {
                        Log.Error($"The roaming network stored in '{directory}' can not be read: {error}", "poi");
                        store.Dispose();
                        continue;
                    }

                    if (!POI.AddRoamingNetworkHistory(history))
                    {
                        Log.Error($"The roaming network '{history.RoamingNetworkId}' stored in '{directory}' is served already.", "poi");
                        store.Dispose();
                        continue;
                    }

                    poiStores.Add(store);

                    Log.Info($"The roaming network '{history.RoamingNetworkId}' from '{directory}', at revision {history.Head.Commit.Revision}.", "poi");

                }
                catch (Exception e)
                {
                    store?.Dispose();
                    Log.Exception(e, $"The roaming network stored in '{directory}' can not be read.", "poi");
                }

            }

        }

        #endregion

        #region (private) DisposePOI()

        /// <summary>
        /// Let go of the POI API and of the stores of its roaming networks.
        /// </summary>
        private async ValueTask DisposePOI()
        {

            await POI.DisposeAsync();

            foreach (var store in poiStores)
                store.Dispose();

            poiStores.Clear();

        }

        #endregion

    }

}
