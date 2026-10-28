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

using System.Net.Http.Headers;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using static cloud.charging.open.protocols.WWCP.Node.TestKit.OwnerOnlyChecks;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A hub asked to keep its traffic beyond this run: the file it appends
    /// to, and the JSON API saying so.
    /// </summary>
    /// <remarks>
    /// A fixture of its own because the file is off everywhere else, which is
    /// the default and what <see cref="TrafficTests"/> covers. The two
    /// questions here are the ones a deployment leans on: that the calls
    /// really are on disk, and that a page can tell a reader they are instead
    /// of promising that a restart forgets them.
    /// </remarks>
    public class TrafficFileTests : ARoamingHubTests
    {

        #region Configuration

        /// <summary>
        /// The traffic appended as well, which nothing else here switches on.
        /// </summary>
        protected override JObject Configuration

            => new (
                   new JProperty("nts",   new JObject(
                       new JProperty("groups", new JObject(
                           new JProperty("legal", new JObject(
                               new JProperty("enabled",  false)
                           ))
                       ))
                   )),
                   new JProperty("ocpi",  new JObject(
                       new JProperty("logging",  new JObject(
                           new JProperty("file",  true)
                       ))
                   ))
               );

        #endregion

        #region (private) Stranger()

        /// <summary>
        /// Somebody calling with a token this hub never gave out, which is
        /// refused and written down all the same - the shortest way to one
        /// line of traffic.
        /// </summary>
        private HttpClient Stranger()
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                           "Token",
                                                           Convert.ToBase64String(Encoding.UTF8.GetBytes("nobody-gave-me-this"))
                                                       );

            return http;

        }

        #endregion


        #region ACallIsAppendedToTheFileOfItsDay()

        /// <summary>
        /// The whole point: a call that happened is a line on disk, in the
        /// file of the day it happened on, and that line is the call.
        /// </summary>
        [Test]
        public async Task ACallIsAppendedToTheFileOfItsDay()
        {

            using (var stranger = Stranger())
                await stranger.GetAsync("/ext/versions");

            var directory = Path.Combine(Directory, "traffic");

            Assert.That(System.IO.Directory.Exists(directory), Is.True,
                        $"The configuration asked for the traffic on disk and '{directory}' was not made.");

            var files = System.IO.Directory.GetFiles(directory, "traffic-*.jsonl");

            Assert.That(files, Has.Length.EqualTo(1),
                        "One day, one file - and this run was one day.");

            // Read while the hub still holds the handle open, which is the
            // case somebody looking at a running hub is in - and it has to
            // be done with FileShare.ReadWrite. File.ReadAllLines asks for
            // FileShare.Read, whose own sharing would have to permit the
            // write in progress, and is refused. See OCPITrafficFile.
            var lines = new List<String>();

            using (var reading = new StreamReader(
                                     new FileStream(files[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                                 ))
            {

                while (await reading.ReadLineAsync() is String line)
                    lines.Add(line);

            }

            Assert.That(lines, Is.Not.Empty, "A call was refused and nothing was written down.");

            var call = JObject.Parse(lines[^1]);

            Assert.Multiple(() => {
                Assert.That(call.Value<Int32>("httpStatusCode"),  Is.EqualTo(401));
                Assert.That(call.Value<String>("module"),         Is.EqualTo("versions"));
                Assert.That(call.Value<String>("timestamp"),      Is.Not.Null);
            });

            // One object per line rather than one array for the file: a line
            // that had been indented would be a line nobody can count.
            Assert.That(lines[0].StartsWith('{') && lines[0].EndsWith('}'), Is.True,
                        "A line of the file is not one whole JSON object.");

        }

        #endregion

        #region TheTrafficSaysWhereItIsAppended()

        /// <summary>
        /// What the pages read: with the file on, the traffic says where its
        /// calls go. They used to say "in memory only" whatever was true,
        /// because nothing told them otherwise.
        /// </summary>
        [Test]
        public async Task TheTrafficSaysWhereItIsAppended()
        {

            using (var stranger = Stranger())
                await stranger.GetAsync("/ext/versions");

            using var admin = await SignedIn();

            var appendedTo = (await GetJSON(admin, "/api/v1/traffic"))["appendedTo"] as JObject;

            Assert.That(appendedTo, Is.Not.Null, "The file is on and the traffic does not say so.");

            Assert.Multiple(() => {

                Assert.That(appendedTo.Value<String>("directory"),
                            Is.EqualTo(Path.Combine(Directory, "traffic")));

                Assert.That(appendedTo.Value<String>("current"),
                            Does.EndWith(".jsonl"));

                Assert.That(appendedTo.Value<UInt64>("written"),
                            Is.GreaterThan(0UL),
                            "A call was written down and the count did not move.");

            });

        }

        #endregion

        #region WhatTheHubKeepsIsItsOwnersAlone()

        /// <summary>
        /// The traffic, the peers and their tokens, and the private keys of the
        /// roaming network operator: each directory for the account the hub
        /// runs under alone, and the keys as well.
        /// </summary>
        [Test]
        public async Task WhatTheHubKeepsIsItsOwnersAlone()
        {

            using (var stranger = Stranger())
                await stranger.GetAsync("/ext/versions");

            var networkOperator = Path.Combine(Directory, HubRoamingNetworkOperator.DirectoryName);

            var kept = new[] {
                           Path.Combine(Directory, "traffic"),
                           Path.Combine(Directory, RoamingHub.OCPIDirectoryName),
                           networkOperator,
                           Path.Combine(networkOperator, HubRoamingNetworkOperator.SigningKeyFileName),
                           Path.Combine(networkOperator, HubRoamingNetworkOperator.IdentityKeyFileName)
                       };

            Assert.Multiple(() => {
                foreach (var path in kept)
                {
                    Assert.That(Path.Exists(path),      Is.True,  path);
                    Assert.That(OthersMayGetIn(path),   Is.Empty, path);
                }
            });

        }

        #endregion

    }

}
