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

using NUnit.Framework;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The time server, wherever somebody reads its name, and what the
    /// overview says about the last synchronisation.
    /// </summary>
    /// <remarks>
    /// A DomainName prints itself absolutely, with the root label on the end.
    /// That is right for a name on its way back into the configuration file
    /// and wrong in the middle of a sentence, where "ptbtime1.ptb.de." reads as
    /// a typing mistake. None of these tests asks a time server anything: the
    /// hub is only built, or started on a clock whose timers never fire.
    /// </remarks>
    [TestFixture]
    public class TimeServerNameTests
    {

        #region Data

        private String       directory   = default!;
        private RoamingHub?  hub;

        /// <summary>
        /// A hub whose time client is on, and whose file says nothing else.
        /// </summary>
        private static JObject TimeClientOn

            => new (
                   new JProperty("nts", new JObject(
                       new JProperty("enabled", true)
                   ))
               );

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestRoamingHubs.TemporaryDirectory("time-server-names");
        }

        [TearDown]
        public async Task TakeItAwayAgain()
        {

            if (hub is not null)
                await hub.DisposeAsync();

            hub = null;

            TestRoamingHubs.Remove(directory);

        }

        #endregion


        #region TheStartNamesTheServerAsItIsRead()

        /// <summary>
        /// The line a start writes about the clock check.
        /// </summary>
        /// <remarks>
        /// Started on a clock whose timers never fire, so that the check this
        /// line announces never asks the PTB for anything.
        /// </remarks>
        [Test]
        public async Task TheStartNamesTheServerAsItIsRead()
        {

            hub = TestRoamingHubs.New(directory, TimeClientOn, ClockWithoutTimers.Instance);

            await hub.Start();

            var said = hub.Log.Recent(500).Select(entry => entry.Message).Where(message => message.Contains("will be checked against")).ToArray();

            Assert.That(said, Has.Some.Contains("will be checked against ptbtime1.ptb.de every"));

        }

        #endregion

        #region TheClockNamesTheServerAsItIsRead()

        /// <summary>
        /// The clock's JSON, which the NTS page shows as the server the clock
        /// is checked against.
        /// </summary>
        [Test]
        public void TheClockNamesTheServerAsItIsRead()
        {

            hub = TestRoamingHubs.New(directory, TimeClientOn);

            Assert.That(hub.ClockJSON()["nts"]?.Value<String>("server"),  Is.EqualTo("ptbtime1.ptb.de"));

        }

        #endregion

        #region TheOverviewNamesTheServerAndSaysWhenItWasLastSynchronised()

        /// <summary>
        /// The overview's time card: the server as it is read, and the last
        /// synchronisation - there and empty while there has been none, so that
        /// the card says "-" rather than leaving the line out.
        /// </summary>
        [Test]
        public void TheOverviewNamesTheServerAndSaysWhenItWasLastSynchronised()
        {

            hub = TestRoamingHubs.New(directory, TimeClientOn);

            var time = hub.ConfigurationJSON()["time"] as JObject;

            Assert.Multiple(() => {
                Assert.That(time?.Value<String>("nts"),          Is.EqualTo("ptbtime1.ptb.de"));
                Assert.That(time?["lastSync"]?.      Type,       Is.EqualTo(JTokenType.Null));
                Assert.That(time?["lastSyncResult"]?.Type,       Is.EqualTo(JTokenType.Null));
            });

        }

        #endregion

        #region AnotherServerIsWrittenIntoTheLogAsItIsRead()

        /// <summary>
        /// The line a change of server writes. What goes into the file keeps
        /// its root dot; only the sentence drops it.
        /// </summary>
        [Test]
        public void AnotherServerIsWrittenIntoTheLogAsItIsRead()
        {

            hub = TestRoamingHubs.New(directory, TestRoamingHubs.Offline);

            Assert.That(hub.TryUpdateNTSConfiguration(new JObject(new JProperty("hostname", "time.example.org")), out var error),
                        Is.True,
                        error);

            var said = hub.Log.Recent(500).Select(entry => entry.Message).Where(message => message.StartsWith("NTS configuration changed")).ToArray();

            Assert.That(said, Has.Some.Contains("server = time.example.org:4460 (NTS-KE)"));

        }

        #endregion

    }

}
