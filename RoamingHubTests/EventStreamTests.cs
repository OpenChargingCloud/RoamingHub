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

using NUnit.Framework;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The two event streams - the log's and the traffic's - as a proxy in
    /// front of the hub sees them.
    /// </summary>
    /// <remarks>
    /// Against a hub that is started, because what is tested is what goes over
    /// the wire: a header, and what a stream says while nothing happens.
    /// </remarks>
    [TestFixture]
    public class EventStreamTests : ARoamingHubTests
    {

        #region (private static) ReadUntil(Reader, Wanted, Within)

        /// <summary>
        /// Read the stream line by line until a line satisfies the condition,
        /// and say whether one did in time.
        /// </summary>
        private static async Task<Boolean> ReadUntil(StreamReader           Reader,
                                                     Func<String, Boolean>  Wanted,
                                                     TimeSpan               Within)
        {

            using var timeout = new CancellationTokenSource(Within);

            try
            {
                while (await Reader.ReadLineAsync(timeout.Token) is String line)
                {
                    if (Wanted(line))
                        return true;
                }
            }
            catch (OperationCanceledException)
            { }

            return false;

        }

        #endregion


        #region TheStreamsAskAProxyNotToBufferThem(Path)

        /// <summary>
        /// "X-Accel-Buffering: no" on both event streams.
        /// </summary>
        /// <remarks>
        /// nginx buffers what it passes on unless it is told otherwise, and a
        /// buffered event stream reaches the browser as nothing at all - not even
        /// its header - until nginx gives up on it after 60 silent seconds. The
        /// vehicle's Logs page said "reconnecting ..." all the while behind
        /// nginx, and never asked for its snapshot, which it does when the
        /// stream opens; the hub's Logs and Traffic pages hang on the same kind
        /// of stream.
        /// </remarks>
        [TestCase("/api/v1/events")]
        [TestCase("/api/v1/traffic/events")]
        public async Task TheStreamsAskAProxyNotToBufferThem(String Path)
        {

            using var http      = await SignedIn();
            using var response  = await http.GetAsync(Path, HttpCompletionOption.ResponseHeadersRead);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,                                                 Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType?.MediaType,                     Is.EqualTo("text/event-stream"));
                Assert.That(response.Headers.TryGetValues("X-Accel-Buffering", out var values),  Is.True, "the header is there");
                Assert.That(values,                                                              Is.EqualTo(new[] { "no" }));
            });

        }

        #endregion

        #region ASilentStreamSaysSoAndThenCarriesOn()

        /// <summary>
        /// A comment whenever the log's stream has been silent for the
        /// heartbeat, and the next entry after it as if nothing had happened.
        /// </summary>
        /// <remarks>
        /// nginx gives up on an upstream that has sent nothing for 60 seconds,
        /// and a hub nobody is using says nothing for longer than that. The
        /// second half is the one that could go wrong: the stream waits for the
        /// next entry across the heartbeat instead of asking for it again, and
        /// an entry that arrived during one must neither be lost nor come twice.
        /// </remarks>
        [Test]
        public async Task ASilentStreamSaysSoAndThenCarriesOn()
        {

            RoamingHub.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            using var http      = await SignedIn();
            using var response  = await http.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead);
            using var reader    = new StreamReader(await response.Content.ReadAsStreamAsync());

            var heartbeat       = await ReadUntil(reader, line => line == ": keep-alive", TimeSpan.FromSeconds(10));

            var marker          = "A line for the event stream " + Guid.NewGuid().ToString("N")[..8];
            RoamingHub.Log.Info(marker, "test");

            var lines           = new List<String>();
            var entry           = await ReadUntil(reader, line => { lines.Add(line); return line.Contains(marker); }, TimeSpan.FromSeconds(10));

            // And the one after it, to be sure the stream is still waiting for
            // entries and not only for the heartbeat.
            var second          = marker + " (second)";
            RoamingHub.Log.Info(second, "test");

            var secondEntry     = await ReadUntil(reader, line => { lines.Add(line); return line.Contains(second); }, TimeSpan.FromSeconds(10));

            Assert.Multiple(() => {
                Assert.That(heartbeat,                                           Is.True,   "a comment came while nothing was logged");
                Assert.That(entry,                                               Is.True,   "the entry logged after the heartbeat arrived");
                Assert.That(secondEntry,                                         Is.True,   "and so did the one after it");
                Assert.That(lines.Count(line => line.Contains($"\"{marker}\"")), Is.EqualTo(1), "once");
            });

        }

        #endregion

        #region ASilentTrafficStreamSaysSoToo()

        /// <summary>
        /// The traffic's stream, which is silent for as long as the peers are:
        /// a comment after the heartbeat there as well.
        /// </summary>
        [Test]
        public async Task ASilentTrafficStreamSaysSoToo()
        {

            RoamingHub.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            using var http      = await SignedIn();
            using var response  = await http.GetAsync("/api/v1/traffic/events", HttpCompletionOption.ResponseHeadersRead);
            using var reader    = new StreamReader(await response.Content.ReadAsStreamAsync());

            Assert.That(await ReadUntil(reader, line => line == ": keep-alive", TimeSpan.FromSeconds(10)),
                        Is.True,
                        "a comment came while no call went through the hub");

        }

        #endregion

    }

}
