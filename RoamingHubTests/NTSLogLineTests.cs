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

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The line a synchronisation writes into the log with numbers in it.
    /// </summary>
    /// <remarks>
    /// Under a German culture - which is what this hub's own machine runs - it
    /// read "clock is +702,4 ms off (round trip 12,3 ms)": a decimal comma in
    /// the middle of an English sentence, in a log whose numbers then change
    /// their punctuation with the machine that wrote them. Every test here runs
    /// under de-DE for that reason.
    /// </remarks>
    [SetCulture("de-DE")]
    public class NTSLogLineTests
    {

        #region TheNumbersAreWrittenWithAPointUnderEveryCulture()

        /// <summary>
        /// The offset and the round trip, with a point - the one culture a log
        /// written in English can be read in.
        /// </summary>
        [Test]
        public void TheNumbersAreWrittenWithAPointUnderEveryCulture()
        {

            Assert.That(RoamingHub.AnsweredLine(DomainName.Parse("ptbtime1.ptb.de"),
                                                631,
                                                TimeSpan.FromMilliseconds(702.4),
                                                TimeSpan.FromMilliseconds(12.3),
                                                7),
                        Does.Contain("clock is +702.4 ms off").And.Contain("(round trip 12.3 ms)"));

        }

        #endregion

        #region TheServerIsNamedAsItIsRead()

        /// <summary>
        /// The server without the root's dot: "ptbtime1.ptb.de." is the name
        /// exactly, and in the middle of a sentence it reads as a typing
        /// mistake. The configuration file keeps it.
        /// </summary>
        [Test]
        public void TheServerIsNamedAsItIsRead()
        {

            Assert.That(RoamingHub.AnsweredLine(DomainName.Parse("ptbtime1.ptb.de"),
                                                631,
                                                TimeSpan.FromMilliseconds(-3),
                                                TimeSpan.FromMilliseconds(12),
                                                7),
                        Is.EqualTo("NTS: ptbtime1.ptb.de answered in 631 ms, this RoamingHub's clock is -3.0 ms off " +
                                   "(round trip 12.0 ms), 7 cookie(s) left."));

        }

        #endregion

        #region AnAnswerWithoutAnOffsetSaysSo()

        /// <summary>
        /// An answer nothing could be taken from still names the server and
        /// the cookies, and leaves out what it does not have.
        /// </summary>
        [Test]
        public void AnAnswerWithoutAnOffsetSaysSo()
        {

            Assert.That(RoamingHub.AnsweredLine(DomainName.Parse("ptbtime1.ptb.de"),
                                                631,
                                                null,
                                                null,
                                                0),
                        Is.EqualTo("NTS: ptbtime1.ptb.de answered in 631 ms, 0 cookie(s) left."));

        }

        #endregion

    }

}
