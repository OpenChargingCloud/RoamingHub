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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Norn.Monitoring;
using org.GraphDefined.Vanaheimr.Norn.TimeSync;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// What a synchronisation writes into the log about what the group
    /// concluded, on a machine that writes its numbers with a comma.
    /// </summary>
    /// <remarks>
    /// The sentences are English and end up in a log that is held against
    /// roaming records, so their numbers keep their decimal point whatever the
    /// culture of the machine that wrote them. Under de-DE they read
    /// "disagree by 2,2 ms", "the agreed deviation of 0 s" for one of a
    /// millisecond, and "+2,1 ms from 2 server(s), spread 2,2 ms".
    ///
    /// Built from a verdict rather than from a synchronisation, so that
    /// nothing here asks a time server anything.
    /// </remarks>
    [TestFixture]
    [SetCulture("de-DE")]
    public class NTSLogLineTests
    {

        #region (private static) Answer(Name, OffsetMilliseconds)

        private static NTSMeasurementResult Answer(String  Name,
                                                   Double  OffsetMilliseconds)

            => new (DomainName.Parse(Name),
                    Guid.Empty) {

                   Success  = true,
                   NTP      = new NTPMeasurementResult {
                                  Success                 = true,
                                  NTSAuthenticationValid  = true,
                                  Offset                  = TimeSpan.FromMilliseconds(OffsetMilliseconds)
                              }

               };

        #endregion

        #region (private static) Disagreeing()

        /// <summary>
        /// Two servers 2.2 ms apart, in a group that agreed on a millisecond.
        /// </summary>
        private static (TimeSourceGroup Group, TimeSyncVerdict Verdict) Disagreeing()
        {

            var deviation  = TimeSpan.FromMilliseconds(1);

            var group      = new TimeSourceGroup(
                                 "legal",
                                 [
                                     new NTSServerEndpoint(DomainName.Parse("a.example")),
                                     new NTSServerEndpoint(DomainName.Parse("b.example"))
                                 ],
                                 MinServers:    2,
                                 MaxDeviation:  deviation
                             );

            var verdict    = TimeSyncVerdict.From(
                                 [ Answer("a.example", 1.0), Answer("b.example", 3.2) ],
                                 MinServers:    2,
                                 MaxDeviation:  deviation
                             );

            return (group, verdict);

        }

        #endregion


        #region TheDisagreementIsWrittenWithAPointAndTheDeviationInFull()

        /// <summary>
        /// The warning that the servers of a group disagree: its spread with a
        /// point, and the agreed deviation with as many places as it has.
        /// </summary>
        [Test]
        public void TheDisagreementIsWrittenWithAPointAndTheDeviationInFull()
        {

            var (group, verdict) = Disagreeing();

            Assert.That(verdict.DeviationExceeded,  Is.True,  "the test's own premise");

            Assert.That(RoamingHub.DeviationWarning(group.Name, verdict.Spread!.Value, group.MaxDeviation),
                        Is.EqualTo("NTS: the time servers of group 'legal' disagree by 2.2 ms, " +
                                   "which reaches the agreed deviation of 0.001 s."));

        }

        #endregion

        #region TheVerdictTheLineEndsWithHasAPointToo()

        /// <summary>
        /// "NTS: group 'legal' answered in ... ms - " ends with the verdict,
        /// which is Norn's to write. Under a German culture it was the one
        /// half of the line with a comma in it.
        /// </summary>
        [Test]
        public void TheVerdictTheLineEndsWithHasAPointToo()
        {

            var (_, verdict) = Disagreeing();

            Assert.That(verdict.ToString(),
                        Is.EqualTo("+2.1 ms from 2 server(s), spread 2.2 ms - beyond the agreed deviation"));

        }

        #endregion

    }

}
