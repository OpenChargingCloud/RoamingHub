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

using cloud.charging.open.RoamingHub.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// Building hubs to test against.
    /// </summary>
    /// <remarks>
    /// Its own class rather than a few protected methods on the fixture base,
    /// because not every test wants a RoamingHub that is started and taken away
    /// again for it: the peering tests make theirs as they need them, and the
    /// conformance suite starts and stops its own - see RoamingHubConformance.
    /// </remarks>
    internal static class TestRoamingHubs
    {

        #region New(Directory, Configuration = null, Clock = null)

        /// <summary>
        /// A RoamingHub, built and not started.
        /// </summary>
        /// <remarks>
        /// Not started, and that is often the point: it is <c>Start()</c> that
        /// puts a timer on the network to check the clock, so a RoamingHub that
        /// was only built is also one that will not quietly go and ask a time
        /// server in the middle of a test run.
        ///
        /// It is also <c>Start()</c> that makes the accounts, so a RoamingHub
        /// that was only built has none yet and no password to show for them.
        /// </remarks>
        /// <param name="Directory">Where its accounts and its configuration go; created when it does not exist.</param>
        /// <param name="Configuration">What its configuration file says, or null for a RoamingHub nobody has configured.</param>
        /// <param name="Clock">Where it reads the time, for a test that needs to decide what time it is.</param>
        public static RoamingHub New(String         Directory,
                                     JObject?       Configuration   = null,
                                     TimeProvider?  Clock           = null)
        {

            System.IO.Directory.CreateDirectory(Directory);

            var configFile = Path.Combine(Directory, "configuration.json");

            if (Configuration is not null)
                File.WriteAllText(configFile, Configuration.ToString());

            // Its log on neither the console nor the disk: a test run's
            // console is for the test run.
            return new RoamingHub(
                       HTTPPort:         IPPort.Parse(TestPorts.Free()),
                       AccountsPath:     Path.Combine(Directory, "accounts"),
                       ConfigFile:       new WWCPConfigFile(configFile),
                       LogToConsole:     false,
                       BridgeDebugLog:   false,
                       TimeProvider:     Clock
                   );

        }

        #endregion

        #region Offline

        /// <summary>
        /// A configuration with the time client switched off.
        /// </summary>
        /// <remarks>
        /// Written before a RoamingHub is built, because that is when it is
        /// read, and switched off there rather than afterwards because it is
        /// <c>StartCheckingTheClock</c> inside <c>Start()</c> that would
        /// otherwise schedule the first check. A test run has no business
        /// asking a public time server anything.
        /// </remarks>
        public static JObject Offline

            => new (
                   new JProperty("nts", new JObject(
                       new JProperty("enabled", false)
                   ))
               );

        #endregion

        #region TemporaryDirectory(Purpose)

        /// <summary>
        /// A directory of its own for one test, so that no two of them read
        /// each other's accounts or configuration.
        /// </summary>
        public static String TemporaryDirectory(String Purpose)

            => Path.Combine(
                   Path.GetTempPath(),
                   $"hub-{Purpose}-{Guid.NewGuid().ToString("N")[..12]}"
               );

        #endregion

        #region Remove(Directory)

        /// <summary>
        /// Take a test's directory away again.
        /// </summary>
        /// <remarks>
        /// A directory that survives a failed run is untidy and nothing more,
        /// so this never throws: failing a teardown over it would hide the
        /// failure that actually matters.
        /// </remarks>
        public static void Remove(String? Directory)
        {

            try
            {
                if (Directory is not null && System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, true);
            }
            catch (IOException)
            { }
            catch (UnauthorizedAccessException)
            { }

        }

        #endregion

    }

}
