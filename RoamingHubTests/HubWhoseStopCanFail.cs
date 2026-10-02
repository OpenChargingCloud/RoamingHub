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

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A hub as TestRoamingHubs builds any other, whose stopping can be made
    /// to fail - the way stopping a server that had not begun to listen yet
    /// once failed.
    /// </summary>
    /// <remarks>
    /// What fails is what this hub ends before its server stops: its
    /// OnStopping. The node below closes the server all the same, and a stop
    /// after that does nothing.
    /// </remarks>
    internal sealed class HubWhoseStopCanFail(String          AccountsPath,
                                              WWCPConfigFile  ConfigFile,
                                              TimeProvider?   Clock)

        : RoamingHub(HTTPPort:        IPPort.Parse(TestPorts.Free()),
                     AccountsPath:    AccountsPath,
                     ConfigFile:      ConfigFile,
                     LogToConsole:    false,
                     BridgeDebugLog:  false,
                     TimeProvider:    Clock)

    {

        #region Data

        /// <summary>
        /// What its stopping says where it fails.
        /// </summary>
        public const String Why = "This hub was made to fail to stop.";

        #endregion

        #region Properties

        /// <summary>
        /// Whether the next stop fails, once this hub has ended what it ends
        /// before its server stops.
        /// </summary>
        public Boolean  NextStopFails     { get; set; }

        /// <summary>
        /// Whether every stop fails - as a hub fails that cannot end what it
        /// holds open, however often it is asked.
        /// </summary>
        public Boolean  EveryStopFails    { get; set; }

        /// <summary>
        /// How often it was asked to end what it holds open.
        /// </summary>
        public Int32    Stoppings         { get; private set; }

        #endregion


        #region In(Directory, Configuration, Clock)

        /// <summary>
        /// One in the given directory, as a fixture's NewRoamingHub builds it.
        /// </summary>
        /// <param name="Directory">Where its accounts and its configuration go.</param>
        /// <param name="Configuration">What its configuration file says.</param>
        /// <param name="Clock">Where it reads the time, or null for the system clock.</param>
        public static HubWhoseStopCanFail In(String         Directory,
                                             JObject?       Configuration,
                                             TimeProvider?  Clock)

            => new (
                   AccountsPath:  Path.Combine(Directory, "accounts"),
                   ConfigFile:    TestRoamingHubs.ConfigFile(Directory, Configuration),
                   Clock:         Clock
               );

        #endregion

        #region (protected override) OnStopping()

        protected override async Task OnStopping()
        {

            Stoppings++;

            await base.OnStopping();

            if (EveryStopFails || NextStopFails)
            {
                NextStopFails = false;
                throw new InvalidOperationException(Why);
            }

        }

        #endregion

    }

}
