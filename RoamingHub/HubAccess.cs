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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What a hub adds to the resources every node has, and the roles of the
    /// people who run it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node brings the administrators, who may do everything - the peers
    /// and the certificates included, which nobody else may change: somebody
    /// who can add a peer lets a foreign system into a room with all the
    /// others, and somebody who can add a root makes this hub believe a
    /// server nobody else would.
    /// </para>
    /// <para>
    /// The viewer is the hub's own rather than the node's. The node's may read
    /// everything, and on a hub that would include the traffic - what the
    /// peers say to each other, which is their business passing through and
    /// not something everybody who may look at the configuration should read.
    /// </para>
    /// <para>
    /// The configuration file may add roles to these and say differently what
    /// one of them may do - see the node's "roles" section. What is written
    /// here is what a hub is when its file says nothing.
    /// </para>
    /// </remarks>
    public static class HubAccess
    {

        #region Resources

        /// <summary>
        /// The roaming partners: who is on this hub - and, edited, a peer
        /// added, suspended, resumed or removed and the token it signs in with;
        /// run, the OCPI credentials handshake with one.
        /// </summary>
        public const String  Peers    = "peers";

        /// <summary>
        /// What went between the peers: which of them called which, on what,
        /// and what came back - read, and nothing else.
        /// </summary>
        public const String  Traffic  = "traffic";

        /// <summary>
        /// Both.
        /// </summary>
        public static readonly IReadOnlyList<String>  Resources = [ Peers, Traffic ];

        #endregion

        #region Roles

        /// <summary>
        /// May look at this hub and at who is on it, and do nothing to it -
        /// and may not read what the peers say to each other.
        /// </summary>
        public static readonly Role  Viewer  = new ("viewer",
                                                    [ Permission.Read(NodeResources.Configuration),
                                                      Permission.Read(NodeResources.DNS),
                                                      Permission.Read(NodeResources.NTS),
                                                      Permission.Read(NodeResources.Certificates),
                                                      Permission.Read(Peers) ],
                                                    "looks at the hub and its peers, and not at their traffic");

        /// <summary>
        /// Whoever runs this hub: everything the viewer may do, and on top of it
        /// where it resolves names and reads the time, asking those servers
        /// whether they work, and what went between the peers.
        /// </summary>
        /// <remarks>
        /// Day-to-day operation. A hub is run by whoever is asked why a CPO and
        /// an EMSP are not seeing each other, long before it is run by whoever
        /// installed it, and the answer to that question is in the traffic.
        /// Which peers are let in, and which certificates are believed, stays
        /// with the administrators.
        /// </remarks>
        public static readonly Role  Hub     = new ("hub",
                                                    [ Permission.Read(NodeResources.Configuration),
                                                      Permission.Read(NodeResources.DNS),
                                                      Permission.Edit(NodeResources.DNS),
                                                      Permission.Run (NodeResources.DNS),
                                                      Permission.Read(NodeResources.NTS),
                                                      Permission.Edit(NodeResources.NTS),
                                                      Permission.Run (NodeResources.NTS),
                                                      Permission.Read(NodeResources.Certificates),
                                                      Permission.Read(Peers),
                                                      Permission.Read(Traffic) ],
                                                    "runs the hub: its name and time servers, and what goes between the peers");

        /// <summary>
        /// Both, in the order a sentence naming them reads best.
        /// </summary>
        public static readonly IReadOnlyList<Role>  Roles = [ Viewer, Hub ];

        #endregion

    }

}
