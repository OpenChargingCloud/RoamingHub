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
    /// What a RoamingHub makes of an "nts" section: the section put into effect
    /// on top of the group of time servers it already has.
    /// </summary>
    /// <remarks>
    /// Beside the tests of the section on its own, because the rule that
    /// matters here - what a section does not mention is left as it is - can
    /// only be seen against something that is already there.
    ///
    /// The RoamingHubs are built and never started. The constructor is what
    /// applies the file, and it is Start() that would put a timer on the
    /// network to ask the servers.
    /// </remarks>
    [TestFixture]
    public class NTSGroupInEffectTests
    {

        #region Data

        private String directory = default!;

        private String ConfigurationPath
            => Path.Combine(directory, "configuration.json");

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestRoamingHubs.TemporaryDirectory("nts-in-effect");
        }

        [TearDown]
        public void RemoveTheDirectory()
        {
            TestRoamingHubs.Remove(directory);
        }

        #endregion


        #region (helper) NewRoamingHub(Configuration = null)

        /// <summary>
        /// A RoamingHub as it stands after reading this configuration file, or
        /// after reading none.
        /// </summary>
        private RoamingHub NewRoamingHub(String? Configuration = null)

            => TestRoamingHubs.New(directory,
                             Configuration is not null
                                 ? JObject.Parse(Configuration)
                                 : null);

        #endregion


        #region AQuorumOnItsOwnHoldsTheServersInEffectToIt()

        /// <summary>
        /// "minServers" without a list is about the servers the RoamingHub has.
        /// </summary>
        /// <remarks>
        /// It used to count only beside a list or a hostname: the file was read,
        /// the start reported NTS configuration, and the default four went on
        /// being held to two.
        /// </remarks>
        [Test]
        public async Task AQuorumOnItsOwnHoldsTheServersInEffectToIt()
        {

            await using var RoamingHub = NewRoamingHub("""{ "nts": { "minServers": 3 } }""");

            Assert.Multiple(() => {
                Assert.That(RoamingHub.TimeSources.Sources.Count(),  Is.EqualTo(4),  "the servers were not mentioned, so they are the default four");
                Assert.That(RoamingHub.TimeSources.MinServers,       Is.EqualTo(3),  "the quorum was read and changed nothing");
            });

        }

        #endregion

        #region ADeviationOnItsOwnAppliesToTheServersInEffect()

        [Test]
        public async Task ADeviationOnItsOwnAppliesToTheServersInEffect()
        {

            await using var RoamingHub = NewRoamingHub("""{ "nts": { "maxDeviationSeconds": 0.5 } }""");

            Assert.Multiple(() => {
                Assert.That(RoamingHub.TimeSources.Sources.Count(),  Is.EqualTo(4));
                Assert.That(RoamingHub.TimeSources.MinServers,       Is.EqualTo(2));
                Assert.That(RoamingHub.TimeSources.MaxDeviation,     Is.EqualTo(TimeSpan.FromSeconds(0.5)),  "the deviation was read and changed nothing");
            });

        }

        #endregion

        #region AQuorumTheServersInEffectCannotReachStopsTheStart()

        /// <summary>
        /// Five of four is refused while the file is read, as five of a list of
        /// four always was.
        /// </summary>
        [Test]
        public void AQuorumTheServersInEffectCannotReachStopsTheStart()
        {

            var problem = Assert.Throws<InvalidOperationException>(() => NewRoamingHub("""{ "nts": { "minServers": 5 } }"""));

            Assert.That(problem?.Message,  Does.Contain("minServers").And.Contain(ConfigurationPath));

        }

        #endregion

        #region AQuorumOnItsOwnIsRefusedBeforeItIsWrittenDown()

        /// <summary>
        /// And the same from the page: refused, and the file left as it was, so
        /// that the next start does not stop over what was refused.
        /// </summary>
        [Test]
        public async Task AQuorumOnItsOwnIsRefusedBeforeItIsWrittenDown()
        {

            await using var RoamingHub = NewRoamingHub();

            Assert.Multiple(() => {

                Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""{ "minServers": 5 }"""), out var error),  Is.False);
                Assert.That(error,                                                                                   Does.Contain("minServers"));

                Assert.That(!File.Exists(ConfigurationPath) || !File.ReadAllText(ConfigurationPath).Contains("minServers"),
                            Is.True,
                            "a refused quorum was written down all the same");

                Assert.That(RoamingHub.TimeSources.MinServers,  Is.EqualTo(2));

            });

            Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""{ "minServers": 3 }"""), out var unexpected),  Is.True,  unexpected);
            Assert.That(RoamingHub.TimeSources.MinServers,                                                                  Is.EqualTo(3));

        }

        #endregion

        #region AListAfterALoneHostnameIsHeldToTheQuorumAgain()

        /// <summary>
        /// The quorum a group of one has to settle for is not carried over to
        /// the four that come after it.
        /// </summary>
        /// <remarks>
        /// Read from the file at the next start, the same section holds the four
        /// to two. A running RoamingHub that held them to one would be a different
        /// RoamingHub from the one that file describes.
        /// </remarks>
        [Test]
        public async Task AListAfterALoneHostnameIsHeldToTheQuorumAgain()
        {

            await using var RoamingHub = NewRoamingHub();

            Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""{ "hostname": "ptbtime1.ptb.de" }"""), out var error),  Is.True,  error);
            Assert.That(RoamingHub.TimeSources.MinServers,                                                                          Is.EqualTo(1),  "one server cannot be held to two");

            Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""
                            {
                                "servers": [ "ptbtime1.ptb.de", "ptbtime2.ptb.de",
                                             "ptbtime3.ptb.de", "ptbtime4.ptb.de" ]
                            }
                            """), out error),  Is.True,  error);

            Assert.That(RoamingHub.TimeSources.MinServers,  Is.EqualTo(2),  "the group of one's quorum was carried over to four");

        }

        #endregion

        #region ASaveOfPartOfTheSectionLeavesTheRestInEffect()

        /// <summary>
        /// The switch on the page sends "enabled" and nothing else, and the
        /// rest of what the file said stays in effect.
        /// </summary>
        /// <remarks>
        /// It used to be replaced by what was sent: how often to check and who
        /// stands behind the time went back to their defaults, and came back
        /// only when the next start read the file again - so that switching NTS
        /// off and on again from the page took the RoamingHub's claim to legal
        /// time away until then.
        /// </remarks>
        [Test]
        public async Task ASaveOfPartOfTheSectionLeavesTheRestInEffect()
        {

            await using var RoamingHub = NewRoamingHub("""{ "nts": { "checkEverySeconds": 600, "legalTimeAuthority": "PTB" } }""");

            Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""{ "enabled": true }"""), out var error),  Is.True,  error);

            Assert.Multiple(() => {
                Assert.That(RoamingHub.TimeCheckEvery,      Is.EqualTo(TimeSpan.FromSeconds(600)),  "the interval went back to its default");
                Assert.That(RoamingHub.LegalTimeAuthority,  Is.EqualTo("PTB"),                      "the authority was forgotten");
            });

        }

        #endregion

        #region AListShorterThanTheQuorumIsRefusedAndNotWritten()

        /// <summary>
        /// A server deleted or switched off below the quorum the file holds is
        /// refused, and the file is left as it was.
        /// </summary>
        /// <remarks>
        /// Each half was fine on its own - the quorum in the file, the list that
        /// was sent - and merged they made a section the next start refuses. A
        /// save that is accepted and then stops the RoamingHub is the one thing
        /// worse than a save that is refused.
        /// </remarks>
        [Test]
        public async Task AListShorterThanTheQuorumIsRefusedAndNotWritten()
        {

            await using var RoamingHub = NewRoamingHub("""
                                          { "nts": { "servers": [ "a.example", "b.example", "c.example" ], "minServers": 3 } }
                                          """);

            var before = File.ReadAllText(ConfigurationPath);

            Assert.Multiple(() => {

                Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""{ "servers": [ "a.example", "b.example" ] }"""), out var deleted),
                            Is.False,
                            "a server was deleted below the quorum");

                Assert.That(deleted,  Does.Contain("minServers"));

                Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""
                                { "servers": [ "a.example", "b.example", { "hostname": "c.example", "enabled": false } ] }
                                """), out _),
                            Is.False,
                            "a server was switched off below the quorum");

                Assert.That(File.ReadAllText(ConfigurationPath),         Is.EqualTo(before),  "a refused save was written down");
                Assert.That(RoamingHub.TimeSources.Sources.Count(),          Is.EqualTo(3));

            });

        }

        #endregion

        #region EveryServerIsListedWithItsPorts()

        /// <summary>
        /// The list the page edits and sends back whole has every server in it,
        /// the switched-off ones included, with the ports each is asked on.
        /// </summary>
        /// <remarks>
        /// It used to list the bands, which have only the servers switched on:
        /// a page sending back what it was shown would have deleted every
        /// server that was switched off.
        /// </remarks>
        [Test]
        public async Task EveryServerIsListedWithItsPorts()
        {

            await using var RoamingHub = NewRoamingHub("""
                                          { "nts": { "servers": [ "a.example",
                                                                  { "hostname": "b.example", "ntsKEPort": 4461, "enabled": false } ] } }
                                          """);

            var listed = RoamingHub.NTSConfigurationJSON()["timeSources"] as JArray;

            Assert.Multiple(() => {
                Assert.That(listed,                                      Has.Count.EqualTo(2),  "the switched-off server is missing");
                Assert.That(listed?[1]?.Value<String>("hostname"),       Is.EqualTo("b.example."));
                Assert.That(listed?[1]?.Value<Boolean>("enabled"),       Is.False);
                Assert.That(listed?[1]?.Value<Int32>("ntsKEPort"),       Is.EqualTo(4461));
                Assert.That(listed?[0]?.Value<Int32>("ntpPort"),         Is.EqualTo(123));

                // There and empty until a key exchange has shown a chain.
                Assert.That(listed?[0]?["rootCA"]?.Type,                 Is.EqualTo(JTokenType.Null));
            });

        }

        #endregion

        #region ARunningRoamingHubPutsANewIntervalIntoItsClockCheckAtOnce()

        /// <summary>
        /// How often the clock is checked, and whether it is, are put into the
        /// check of a running RoamingHub at once - not at its next start.
        /// </summary>
        /// <remarks>
        /// The check runs on a timer set at the start, and a save used to change
        /// only the setting: the page said "in effect" about an interval the
        /// timer did not have until the next start. Seen here in the line the
        /// check writes whenever it is set. Started on a clock whose timers
        /// never fire, so that the checks those lines announce are never made,
        /// as the local controller's test does it (17ab30f there).
        /// </remarks>
        [Test]
        public async Task ARunningRoamingHubPutsANewIntervalIntoItsClockCheckAtOnce()
        {

            await using var RoamingHub = TestRoamingHubs.New(directory, Clock: ClockWithoutTimers.Instance);

            await RoamingHub.Start();

            var before = RoamingHub.Log.LastId;

            Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""{ "checkEverySeconds": 600 }"""), out var error),  Is.True,  error);
            Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""{ "enabled": false }"""),          out error),      Is.True,  error);

            var said = RoamingHub.Log.Recent(50, before, "clock").Select(entry => entry.Message).ToArray();

            Assert.Multiple(() => {
                Assert.That(said,  Has.Some.Contains("will be checked against").And.Contains("every 10 minute(s)"),  String.Join(" | ", said));
                Assert.That(said,  Has.Some.Contains("is not being checked: NTS is switched off"),                   String.Join(" | ", said));
            });

        }

        #endregion

        #region AServerOfTheGroupIsTestedOnItsOwnPorts()

        /// <summary>
        /// The detailed test asks a server of the group on the ports that server
        /// is configured with, and not on those of the single client.
        /// </summary>
        /// <remarks>
        /// It used to take the single client's ports for every server, so a
        /// server with a port of its own was asked on the usual one and reported
        /// as not answering. Name resolution is switched off here, so that
        /// nothing goes out: the first step names the ports before anything is
        /// asked, and the rest fails at once.
        /// </remarks>
        [Test]
        public async Task AServerOfTheGroupIsTestedOnItsOwnPorts()
        {

            await using var RoamingHub = NewRoamingHub("""
                                          { "dns": { "enabled": false },
                                            "nts": { "servers": [ "a.example",
                                                                  { "hostname": "b.example", "ntsKEPort": 4461, "ntpPort": 1234 } ],
                                                     "timeoutSeconds": 1 } }
                                          """);

            var result = await RoamingHub.TestTimeServerAsync("b.example");

            Assert.That(result["steps"]?[0]?.Value<String>("text"),
                        Does.Contain("key exchange on port 4461").And.Contain("time on port 1234"));

        }

        #endregion

        #region TheTestWritesTheNameAsItIsRead()

        /// <summary>
        /// The detailed test names the server the way everything else this
        /// RoamingHub prints does: without the root's dot.
        /// </summary>
        /// <remarks>
        /// "Asking ptbtime2.ptb.de.: key exchange on port 4460" - the name is
        /// exact with the dot, and in the middle of a sentence it reads like a
        /// typing mistake. The steps and the log lines use the name as it is
        /// read; the name the result carries as data is left as it is. Name
        /// resolution is switched off, so that nothing goes out.
        /// </remarks>
        [Test]
        public async Task TheTestWritesTheNameAsItIsRead()
        {

            await using var RoamingHub = NewRoamingHub("""
                                          { "dns": { "enabled": false },
                                            "nts": { "servers": [ "a.example", "b.example" ], "timeoutSeconds": 1 } }
                                          """);

            var before  = RoamingHub.Log.LastId;
            var result  = await RoamingHub.TestTimeServerAsync("b.example");
            var said    = RoamingHub.Log.Recent(50, before, "test").Select(entry => entry.Message).ToArray();

            Assert.Multiple(() => {
                Assert.That(result["steps"]?[0]?.Value<String>("text"),  Does.StartWith("Asking b.example:"));
                Assert.That(said,                                          Has.Some.EqualTo("NTS test: asking b.example ..."),  String.Join(" | ", said));
            });

        }

        #endregion

        #region TheQuorumWantedAndTheQuorumHeldAreBothShown()

        /// <summary>
        /// A lone hostname holds the group to one, and the page shows both that
        /// and the two that is wanted, which the next list is held to.
        /// </summary>
        [Test]
        public async Task TheQuorumWantedAndTheQuorumHeldAreBothShown()
        {

            await using var RoamingHub = NewRoamingHub("""{ "nts": { "hostname": "a.example" } }""");

            var shown = RoamingHub.NTSConfigurationJSON();

            Assert.Multiple(() => {
                Assert.That(shown["settings"]?.Value<Int32>("minServers"),  Is.EqualTo(2),  "the quorum wanted");
                Assert.That(shown["group"]?.   Value<Int32>("minServers"),  Is.EqualTo(1),  "the quorum one server can be held to");
            });

        }

        #endregion

        #region AChangeToOneServerIsWrittenDown()

        /// <summary>
        /// A server given another priority or a port of its own is a change to
        /// the group, and the log book hears of it.
        /// </summary>
        /// <remarks>
        /// The log compared the names of the servers switched on, in the order
        /// they are asked. A priority that put a server into a band of its own
        /// at the end, and a port of its own, left those just as they were -
        /// and so the group changed without a line saying so.
        /// </remarks>
        [Test]
        public async Task AChangeToOneServerIsWrittenDown()
        {

            await using var RoamingHub = NewRoamingHub("""{ "nts": { "servers": [ "a.example", "b.example" ] } }""");

            var before = RoamingHub.Log.LastId;

            Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""
                            { "servers": [ "a.example", { "hostname": "b.example", "priority": 5, "ntsKEPort": 4461 } ] }
                            """), out var error),
                        Is.True,
                        error);

            var said = RoamingHub.Log.Recent(50, before, null).Select(entry => entry.Message).ToArray();

            Assert.That(said,  Has.Some.Contains("time servers = a.example, b.example (priority 5, NTS-KE port 4461)"),
                        String.Join(" | ", said));

        }

        #endregion

        #region ADeviationStaysWhenTheServersChange()

        /// <summary>
        /// What a section does not mention is left as it is - the deviation
        /// too, when the servers are replaced.
        /// </summary>
        [Test]
        public async Task ADeviationStaysWhenTheServersChange()
        {

            await using var RoamingHub = NewRoamingHub("""{ "nts": { "maxDeviationSeconds": 0.5 } }""");

            Assert.That(RoamingHub.TryUpdateNTSConfiguration(JObject.Parse("""{ "servers": [ "a.example", "b.example" ] }"""), out var error),  Is.True,  error);

            Assert.That(RoamingHub.TimeSources.MaxDeviation,  Is.EqualTo(TimeSpan.FromSeconds(0.5)));

        }

        #endregion

        #region TheOverviewNamesTheGroupAndNotTheTestClient()

        /// <summary>
        /// The Configuration page's time card names the servers the clock is
        /// set by - all of them, the one switched off as well - and not the
        /// host of the single client the detailed test starts from.
        /// </summary>
        /// <remarks>
        /// It led with "NTS: ptbtime1.ptb.de." above the servers switched on:
        /// one server, which was the test's, above the group that was asked.
        /// The NTS answer carried the same client as "server", "cookies" and
        /// "keyExchange", and does not any more either.
        /// </remarks>
        [Test]
        public async Task TheOverviewNamesTheGroupAndNotTheTestClient()
        {

            await using var RoamingHub = NewRoamingHub("""
                                          { "nts": { "servers": [ "a.example",
                                                                  { "hostname": "b.example", "priority": 5 },
                                                                  { "hostname": "c.example", "enabled": false } ] } }
                                          """);

            var time = RoamingHub.ConfigurationJSON()["time"] as JObject;
            var nts  = RoamingHub.NTSConfigurationJSON();

            Assert.Multiple(() => {

                Assert.That(nts.ContainsKey("server"),           Is.False);
                Assert.That(nts.ContainsKey("cookies"),          Is.False);
                Assert.That(nts.ContainsKey("keyExchange"),      Is.False);


                Assert.That(time?.Value<String>("timeServers"),  Is.EqualTo("a.example, b.example (priority 5), c.example (switched off)"));
                Assert.That(time?.Value<Boolean>("ntsEnabled"),  Is.True);
                Assert.That(time?.ContainsKey("nts"),            Is.False,  "the test client's host is named again");

                // There and empty while nothing has been synchronised, so that
                // the card says "-" rather than leaving the line out.
                Assert.That(time?["lastSync"]?.      Type,       Is.EqualTo(JTokenType.Null));
                Assert.That(time?["lastSyncResult"]?.Type,       Is.EqualTo(JTokenType.Null));

            });

        }

        #endregion

        #region TheClockIsCheckedAgainstTheGroupAndNotTheTestClient()

        /// <summary>
        /// Against whom the clock is checked, as its JSON says it: the group,
        /// its servers switched on in the order they are asked, and how many of
        /// them have to answer.
        /// </summary>
        /// <remarks>
        /// The servers used to come with their root dots and without the group
        /// or its quorum; the log line at the start, which names the same
        /// servers, has always left the dots out. Both come from one place now.
        /// And it used to say "server" as well, with the host of the single
        /// client that is only there for a server's detailed test.
        /// </remarks>
        [Test]
        public async Task TheClockIsCheckedAgainstTheGroupAndNotTheTestClient()
        {

            await using var RoamingHub = NewRoamingHub("""
                                          { "nts": { "servers": [ { "hostname": "b.example", "priority": 5 },
                                                                  "a.example",
                                                                  { "hostname": "c.example", "enabled": false } ] } }
                                          """);

            var nts = (JObject) RoamingHub.ClockJSON()["nts"]!;

            Assert.Multiple(() => {

                Assert.That(nts.Value<String>("group"),        Is.EqualTo("legal"));

                Assert.That(nts["servers"]!.Values<String>(),  Is.EqualTo(new[] { "a.example", "b.example" }),
                            "switched on, in the order their bands are asked, and without the root's dot");

                Assert.That(nts.Value<Int32>("minServers"),    Is.EqualTo(2));

                Assert.That(nts.ContainsKey("server"),         Is.False,  "the test client's host is named again");

            });

        }

        #endregion

        #region AClockThatIsNotCheckedNamesNobody()

        /// <summary>
        /// Switched off, the clock is checked against nobody, and says so -
        /// rather than naming servers that are not asked.
        /// </summary>
        [Test]
        public async Task AClockThatIsNotCheckedNamesNobody()
        {

            await using var RoamingHub = NewRoamingHub("""{ "nts": { "enabled": false } }""");

            var nts = (JObject) RoamingHub.ClockJSON()["nts"]!;

            Assert.Multiple(() => {
                Assert.That(nts.Value<Boolean>("enabled"),  Is.False);
                Assert.That(nts["group"]?.     Type,        Is.EqualTo(JTokenType.Null));
                Assert.That(nts["servers"]?.   Type,        Is.EqualTo(JTokenType.Null));
                Assert.That(nts["minServers"]?.Type,        Is.EqualTo(JTokenType.Null));
            });

        }

        #endregion

    }

}
