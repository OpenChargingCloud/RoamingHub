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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The JSON API the browser talks to, registered at "/api": what every
    /// node has - see <see cref="NodeHTTPAPI"/> - and what only a roaming hub
    /// has, its peers and what goes between them.
    /// </summary>
    /// <remarks>
    /// The sign-in, the status, the clock, the configuration, name resolution
    /// and the time servers, the certificate store, the log and the event
    /// stream are the node's, as they are the vehicle's and the local
    /// controller's; this class used to have its own copy of all of them. What
    /// is left here is registered on top: the peering in
    /// RoamingHubHTTPAPI.OCPI.cs, and the traffic with a stream of its own in
    /// RoamingHubHTTPAPI.Traffic.cs.
    /// </remarks>
    public partial class RoamingHubHTTPAPI : NodeHTTPAPI
    {

        #region Data

        /// <summary>
        /// A peer appeared on this hub, or changed how it is doing.
        /// </summary>
        /// <remarks>
        /// On the same stream as the log rather than on the traffic's: it is
        /// something this hub decided, not something that went between two
        /// peers, and whoever has a page of this hub open wants it wherever
        /// they are.
        /// </remarks>
        public const String PeerEventName = "peer";

        #endregion

        #region Properties

        /// <summary>
        /// The RoamingHub this API speaks for.
        /// </summary>
        public RoamingHub  RoamingHub  { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create and register the JSON API within the given HTTP server.
        /// </summary>
        /// <param name="HTTPServer">The HTTP server.</param>
        /// <param name="RoamingHub">The RoamingHub this API speaks for.</param>
        /// <param name="ExtAPI">The accounts and the groups they are in.</param>
        /// <param name="Log">Everything that happens inside this RoamingHub.</param>
        /// <param name="APIPath">The root path of the API, "/api" by default.</param>
        /// <param name="Version">The version reported by the status resource.</param>
        public RoamingHubHTTPAPI(HTTPServer  HTTPServer,
                                 RoamingHub  RoamingHub,
                                 HTTPExtAPI  ExtAPI,
                                 EventLog    Log,
                                 HTTPPath?   APIPath   = null,
                                 String?     Version   = null)

            : base(HTTPServer,
                   RoamingHub,
                   ExtAPI,
                   Log,
                   APIPath,
                   Version ?? typeof(RoamingHubHTTPAPI).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")

        {

            this.RoamingHub = RoamingHub;

            RoamingHub.OnPeerPresenceChanged += peer => Publish(PeerEventName, peer.ToJSON());

            // The OCPI side: who the peers are and the peering itself; see
            // RoamingHubHTTPAPI.OCPI.cs. And what went between them; see
            // RoamingHubHTTPAPI.Traffic.cs.
            RegisterOCPIRoutes();
            RegisterTrafficRoutes();

        }

        #endregion


        #region (protected override) ProductStatus()

        /// <summary>
        /// Who this hub is in OCPI - the one thing every peer wrote into its
        /// credentials.
        /// </summary>
        protected override IEnumerable<JProperty> ProductStatus()
        {
            yield return new JProperty("partyId", RoamingHub.PartyIdText);
        }

        #endregion

        #region (protected override) ToReadTheClock

        /// <summary>
        /// The clock is read with the time servers' permission on a hub, as it
        /// was while it sat below their configuration: what the clock is worth
        /// is what the last check against those servers found. Every role a hub
        /// brings has it; a role of the file without it has no NTS page to show
        /// the clock on either.
        /// </summary>
        protected override Permission? ToReadTheClock
            => Permission.Read(NodeResources.NTS);

        #endregion

    }

}
