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

using System.Net;
using System.Net.Sockets;

using NUnit.Framework;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A hub whose stopping fails every time: it says so, and all the same
    /// its port is closed, and letting go of it does not stop it again.
    /// </summary>
    /// <remarks>
    /// The node below sees to both since WWCP_Node 68aeac6. Before, letting
    /// go of the hub stopped it a second time, that stop failed as the first
    /// had, and the port stayed open: a peer or a browser that came to it was
    /// answered by a hub that had said it was stopping. That what the OCPI
    /// library holds is written out where stopping fails is in PeerFileTests.
    /// </remarks>
    public class StopThatFailsTests : ARoamingHubTests
    {

        #region NewRoamingHub() - a hub whose stopping can be made to fail

        /// <summary>
        /// A hub as any other, whose stopping can be made to fail - see
        /// HubWhoseStopCanFail.
        /// </summary>
        protected override RoamingHub NewRoamingHub()
            => HubWhoseStopCanFail.In(Directory, Configuration, Clock);

        #endregion


        #region AHubThatFailsToStopEveryTimeSaysSoAndStillClosesItsPort()

        /// <summary>
        /// The failure reaches whoever let go of the hub, the hub is stopped
        /// once and not again, and its port is closed: whoever comes to it is
        /// refused.
        /// </summary>
        [Test]
        public void AHubThatFailsToStopEveryTimeSaysSoAndStillClosesItsPort()
        {

            var hub   = (HubWhoseStopCanFail) RoamingHub;
            var port  = hub.HTTPPort.ToUInt16();

            hub.EveryStopFails = true;

            Assert.That(async () => await hub.DisposeAsync(),
                        Throws.InstanceOf<InvalidOperationException>().With.Message.EqualTo(HubWhoseStopCanFail.Why),
                        "The stop that was made to fail is not said to have failed.");

            Assert.That(hub.Stoppings, Is.EqualTo(1), "Letting go of the hub stopped it a second time.");

            using var client  = new TcpClient();

            var refused = Assert.CatchAsync<SocketException>(async () => await client.ConnectAsync(IPAddress.Loopback, port),
                                                              "The hub still listens on its port.");

            Assert.That(refused?.SocketErrorCode, Is.EqualTo(SocketError.ConnectionRefused));

        }

        #endregion

    }

}
