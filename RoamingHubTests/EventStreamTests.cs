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

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The traffic's event stream, as a proxy in front of the hub sees it.
    /// </summary>
    /// <remarks>
    /// Against a hub that is started, because what is tested is what goes over
    /// the wire: a header, what the stream says while the peers are quiet,
    /// that it ends when whoever opened it would no longer be let in, and that
    /// a hub told to stop stops with it open. The log's stream is every
    /// node's, and asked by WWCP_Node's conformance suite - see
    /// RoamingHubConformance.
    /// </remarks>
    [TestFixture]
    public class EventStreamTests : ARoamingHubTests
    {

        #region (private) OpenStream(Client, Lines)

        /// <summary>
        /// Open the log's stream and wait until an entry logged after it was
        /// opened has come down it, keeping every line that came.
        /// </summary>
        private async Task<StreamReader> OpenStream(HttpClient Client, List<String> Lines)
        {

            var response = await Client.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.That(response.IsSuccessStatusCode, Is.True, "the event stream opened");

            var reader   = new StreamReader(await response.Content.ReadAsStreamAsync());
            var marker   = "The stream is live " + Guid.NewGuid().ToString("N")[..8];

            RoamingHub.Log.Info(marker, "test");

            Assert.That(await ReadUntil(reader, line => { Lines.Add(line); return line.Contains(marker); }, TimeSpan.FromSeconds(10)),
                        Is.True, "an entry logged after the stream opened came down it");

            return reader;

        }

        #endregion

        #region (private static) EndsWithin(Reader, Lines, Within)

        /// <summary>
        /// Whether the hub ends the stream within the given time, keeping every
        /// line that came before it did.
        /// </summary>
        /// <remarks>
        /// ReadUntil() cannot tell the two apart: it answers false both for a
        /// stream that ended and for one that merely went on without the line,
        /// and a stream that goes on is exactly what a test of a stream that
        /// should have ended is looking for.
        /// </remarks>
        private static async Task<Boolean> EndsWithin(StreamReader  Reader,
                                                      List<String>  Lines,
                                                      TimeSpan      Within)
        {

            using var timeout = new CancellationTokenSource(Within);

            try
            {
                while (await Reader.ReadLineAsync(timeout.Token) is String line)
                    Lines.Add(line);

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (IOException)
            {
                // Cut rather than closed is ended, too.
                return true;
            }

        }

        #endregion

        #region (private) AnAccountIn(Name, Role)

        /// <summary>
        /// An account of the given name, made for the purpose and put in the
        /// group of the given role - made the way the hub makes its first one,
        /// so that it may sign in - and its password.
        /// </summary>
        private async Task<(User Account, String Password)> AnAccountIn(String  Name,
                                                                        String  Role)
        {

            var password = "correct-horse-battery-" + Guid.NewGuid().ToString("N")[..8];

            Assert.That(RoamingHub.ExtAPI.TryGetOrganization(Organization_Id.Parse(RoamingHub.DefaultOrganization), out var organization) &&
                        organization is Organization, Is.True, "the hub's organization is not there");

            await RoamingHub.ExtAPI.CreateUser(
                      User_Id.Parse(Name),
                      I18NString.Create(Languages.en, Name),
                      SimpleEMailAddress.Parse($"{Name}@localhost"),
                      User2OrganizationEdgeLabel.IsMember,
                      (Organization) organization!,
                      Password:                  password,
                      SkipDefaultNotifications:  true,
                      SkipNewUserEMail:          true,
                      SkipNewUserNotifications:  true,
                      AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                      IsAuthenticated:           true
                  );

            Assert.That(RoamingHub.ExtAPI.TryGetUser(User_Id.Parse(Name), out var account),        Is.True, $"the account '{Name}' was not made");
            Assert.That(RoamingHub.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role), out var group), Is.True, $"the hub has no group '{Role}'");

            var joined = await RoamingHub.ExtAPI.AddUserToUserGroup((User) account!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!);

            Assert.That(joined.IsSuccess, Is.True, $"'{Name}' could not be put in '{Role}'");

            return ((User) account!, password);

        }

        #endregion

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


        #region TheTrafficStreamAsksAProxyNotToBufferIt()

        /// <summary>
        /// "X-Accel-Buffering: no" on the traffic's stream, as on the log's.
        /// </summary>
        /// <remarks>
        /// nginx buffers what it passes on unless it is told otherwise, and a
        /// buffered event stream reaches the browser as nothing at all - not even
        /// its header - until nginx gives up on it after 60 silent seconds. The
        /// vehicle's Logs page said "reconnecting ..." all the while behind
        /// nginx, and never asked for its snapshot, which it does when the
        /// stream opens; the hub's Traffic page hangs on the same kind of
        /// stream.
        /// </remarks>
        [Test]
        public async Task TheTrafficStreamAsksAProxyNotToBufferIt()
        {

            using var http      = await SignedIn();
            using var response  = await http.GetAsync("/api/v1/traffic/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,                                                 Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType?.MediaType,                     Is.EqualTo("text/event-stream"));
                Assert.That(response.Headers.TryGetValues("X-Accel-Buffering", out var values),  Is.True, "the header is there");
                Assert.That(values,                                                              Is.EqualTo(new[] { "no" }));
            });

        }

        #endregion

        #region ASilentTrafficStreamSaysSoToo()

        /// <summary>
        /// The traffic's stream, which is silent for as long as the peers are:
        /// a comment after the heartbeat, as on the log's.
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

        #region TheTrafficEndsWithTheRoleThatLetItIn()

        /// <summary>
        /// Taken out of the hub role, an account's stream of the traffic ends at
        /// its next heartbeat - while the log's stream of the same session goes
        /// on, because the log is for anybody signed in.
        /// </summary>
        /// <remarks>
        /// What an account may do is asked of the node on every request, so
        /// that taking somebody out of a group takes effect on their next one.
        /// A stream is one request that is answered for hours, and on a hub
        /// whose peers are quiet nothing happens on it for hours either: it is
        /// the heartbeat that asks, and the session is still there, so it is
        /// the permission that has to be asked.
        /// </remarks>
        [Test]
        public async Task TheTrafficEndsWithTheRoleThatLetItIn()
        {

            RoamingHub.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            var (account, password) = await AnAccountIn("operator", "hub");

            using var http          = await SignedInAs("operator", password);

            using var response      = await http.GetAsync("/api/v1/traffic/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the hub role may read the traffic");

            using var traffic       = new StreamReader(await response.Content.ReadAsStreamAsync());

            Assert.That(await ReadUntil(traffic, line => line == ": keep-alive", TimeSpan.FromSeconds(10)), Is.True,
                        "the traffic's stream is open and saying so");

            var logLines            = new List<String>();
            using var log           = await OpenStream(http, logLines);

            Assert.That(RoamingHub.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse("hub"), out var group), Is.True);

            var left                = await RoamingHub.ExtAPI.RemoveUserFromUserGroup(account, (UserGroup) group!);

            Assert.That(left.IsSuccess, Is.True, "'operator' could not be taken out of 'hub'");

            var trafficLines        = new List<String>();
            var ended               = await EndsWithin(traffic, trafficLines, TimeSpan.FromSeconds(3));

            var afterwards          = "Logged after the role was taken " + Guid.NewGuid().ToString("N")[..8];
            RoamingHub.Log.Info(afterwards, "test");

            var logGoesOn           = await ReadUntil(log, line => { logLines.Add(line); return line.Contains(afterwards); }, TimeSpan.FromSeconds(10));

            var again               = await http.GetAsync("/api/v1/traffic/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.Multiple(() => {
                Assert.That(ended,             Is.True,                             "the traffic's stream went on after its reader had been taken out of the hub role");
                Assert.That(logGoesOn,         Is.True,                             "the log's stream of the same session ended as well, and it may be read by anybody signed in");
                Assert.That(again.StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden), "opening the traffic's stream again was not refused");
            });

        }

        #endregion

        #region StopsWithABrowserOnTheTrafficPage()

        /// <summary>
        /// A hub with a browser on its Traffic page stops, as one with a browser
        /// on its Logs page does: the node ends every stream of its JSON API
        /// before it stops the server, the traffic's among them, where the hub
        /// used to end its streams itself.
        /// </summary>
        /// <remarks>
        /// Without a heartbeat, so that a stream nobody ends waits for ever
        /// rather than until its next heartbeat finds the socket gone. And
        /// settled, not merely opened, as the conformance suite settles the
        /// log's (see EventStream.OpenAndSettle): a stream still writing out
        /// what it had is ended by its socket closing, and this test would then
        /// pass against a hub that cannot stop. A stranger's call is what comes
        /// down this stream - calls until one arrives, a quiet moment, and one
        /// more, whose arrival means the stream was waiting for it.
        /// </remarks>
        [Test]
        public async Task StopsWithABrowserOnTheTrafficPage()
        {

            RoamingHub.API.EventStreamHeartbeat = TimeSpan.Zero;

            using var http      = await SignedIn();
            using var response  = await http.GetAsync("/api/v1/traffic/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the traffic's stream opened");

            using var traffic   = new StreamReader(await response.Content.ReadAsStreamAsync());
            using var stranger  = Anonymous();
            using var settled   = new CancellationTokenSource();

            var knocking        = Task.Run(async () => {
                                      try
                                      {
                                          while (!settled.IsCancellationRequested)
                                          {
                                              await stranger.GetAsync("/ext/versions/knocking", settled.Token);
                                              await Task.Delay(TimeSpan.FromMilliseconds(100), settled.Token);
                                          }
                                      }
                                      catch (OperationCanceledException)
                                      { }
                                  });

            var carried         = await ReadUntil(traffic, line => line.StartsWith("data:"), TimeSpan.FromSeconds(10));

            settled.Cancel();
            await knocking;

            Assert.That(carried, Is.True, "a stranger's call never came down the traffic's stream");

            await Task.Delay(TimeSpan.FromMilliseconds(500));

            var waitedFor       = "/ext/versions/waited-for-" + Guid.NewGuid().ToString("N")[..8];

            await stranger.GetAsync(waitedFor);

            Assert.That(await ReadUntil(traffic, line => line.StartsWith("data:") && line.Contains(waitedFor), TimeSpan.FromSeconds(10)),
                        Is.True,
                        "the traffic's stream never started waiting for the next call");

            var stopping        = RoamingHub.Stop();
            var stopped         = await Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds(30))) == stopping;

            // Where it does not stop, the streams are ended here, as the hub
            // used to end them - so that this test fails rather than hanging
            // the teardown on the same stop.
            if (!stopped)
                RoamingHub.API.CloseEventStreams();

            await stopping;

            Assert.That(stopped, Is.True,
                        "A hub with a browser on its Traffic page did not stop within 30 seconds: " +
                        "the traffic's stream is not ended before the server is.");

        }

        #endregion

    }

}
