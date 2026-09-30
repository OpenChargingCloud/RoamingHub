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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A peer added or removed while the file the OCPI library keeps the peers
    /// of a version in cannot be written: 500 and why, and nothing changed,
    /// neither now nor at the next start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both were answered as done. The library wrote the line into a queue
    /// that told the debug log alone that it could not: an added peer was
    /// listed, its token opening this hub, and gone at the next start; a
    /// removed one was back at the next start, its token opening this hub
    /// again.
    /// </para>
    /// <para>
    /// The file is made unwritable the way that stops root as well: a
    /// directory where it would be. Both versions are offered, because each
    /// keeps its peers in a file of its own and is bound to this hub by a
    /// binding of its own.
    /// </para>
    /// </remarks>
    public class PeerFileTests : ARoamingHubTests
    {

        #region Configuration - a hub that offers both versions

        /// <summary>
        /// The default is 2.2.1 alone.
        /// </summary>
        protected override JObject Configuration

            => new (
                   new JProperty("nts",   new JObject(
                       new JProperty("enabled",  false)
                   )),
                   new JProperty("ocpi",  new JObject(
                       new JProperty("versions",  new JArray("2.2.1", "2.3.0"))
                   ))
               );

        #endregion


        #region APeerIsNotAddedWhereItsFileCannotTakeIt(Version)

        /// <summary>
        /// A peer the file of its version cannot take is not added: 500 and
        /// why, not in the list, and not there at the next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APeerIsNotAddedWhereItsFileCannotTakeIt(String Version)
        {

            using var admin = await SignedIn();

            var file      = BlockPeersFile(Version);

            var response  = await admin.PostAsync("/api/v1/ocpi/partners", Peer(Version));
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPeers(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(text,   Does.Contain(Path.GetFileName(file)),  "The answer does not say which file refused.");
                Assert.That(listed, Does.Not.Contain(PeerId),              "The peer the file refused is listed all the same.");
            });

            Assert.That(await PeersAfterARestart(file), Does.Not.Contain(PeerId));

        }

        #endregion

        #region APeerIsNotRemovedWhereItsFileCannotTakeIt(Version)

        /// <summary>
        /// A peer whose removal the file of its version cannot take is not
        /// removed: 500 and why, still in the list, and still there at the
        /// next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APeerIsNotRemovedWhereItsFileCannotTakeIt(String Version)
        {

            using var admin = await SignedIn();

            await AddPeer(admin, Version);

            // What the next start below reads the peer back from.
            Assert.That(await File.ReadAllTextAsync(PeersFile(Version)), Does.Contain(protocols.OCPI.CommonHTTPAPI.addRemoteParty),
                        "The peer that was added is not in its file.");

            var file      = BlockPeersFile(Version);

            var response  = await admin.DeleteAsync($"/api/v1/ocpi/partners/{Version}/{PeerId}");
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPeers(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(text,   Does.Contain(Path.GetFileName(file)),  "The answer does not say which file refused.");
                Assert.That(listed, Does.Contain(PeerId),                  "The peer the file would not let go of is gone from the list.");
            });

            Assert.That(await PeersAfterARestart(file), Does.Contain(PeerId));

        }

        #endregion

        #region APeerIsRemovedForGood(Version)

        /// <summary>
        /// A peer removed while its file can be written is gone: 200, not in
        /// the list, and not there at the next start.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APeerIsRemovedForGood(String Version)
        {

            using var admin = await SignedIn();

            await AddPeer(admin, Version);

            var response  = await admin.DeleteAsync($"/api/v1/ocpi/partners/{Version}/{PeerId}");
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPeers(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
                Assert.That(listed, Does.Not.Contain(PeerId), "The peer that was removed is still listed.");
            });

            Assert.That(await PeersAfterARestart(), Does.Not.Contain(PeerId));

        }

        #endregion


        #region (private static) PeerId / Peer(Version)

        /// <summary>
        /// The peer every test here adds.
        /// </summary>
        private const String PeerId = "DE-GEF_CPO";

        /// <summary>
        /// Its request body, on the given version.
        /// </summary>
        private static StringContent Peer(String Version)

            => JSONBody(
                   new JProperty("version",      Version),
                   new JProperty("countryCode",  "DE"),
                   new JProperty("partyId",      "GEF"),
                   new JProperty("role",         "CPO"),
                   new JProperty("name",         "Test CPO")
               );

        #endregion

        #region (private static) AddPeer(Admin, Version)

        /// <summary>
        /// Add the peer through the JSON API, on the given version.
        /// </summary>
        private static async Task AddPeer(HttpClient  Admin,
                                          String      Version)
        {

            var response = await Admin.PostAsync("/api/v1/ocpi/partners", Peer(Version));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Adding the peer answered {(Int32) response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        }

        #endregion

        #region (private static) ListedPeers(HTTP)

        /// <summary>
        /// The peers the JSON API lists, by their identification.
        /// </summary>
        private static async Task<String[]> ListedPeers(HttpClient HTTP)

            => [.. ((await GetJSON(HTTP, "/api/v1/ocpi/partners"))["partners"] as JArray ?? new JArray()).
                   Select(peer => peer.Value<String>("id") ?? "")];

        #endregion

        #region (private) PeersFile(Version) / BlockPeersFile(Version)

        /// <summary>
        /// The file the library keeps the peers of a version in.
        /// </summary>
        private String PeersFile(String Version)

            => Path.Combine(
                   RoamingHub.OCPIDirectory,
                   Version switch {
                       "2.2.1"  => protocols.OCPIv2_2_1.CommonAPI.DefaultRemotePartyDBFileName,
                       "2.3.0"  => protocols.OCPIv2_3_0.CommonAPI.DefaultRemotePartyDBFileName,
                       _        => throw new ArgumentException($"This hub offers no OCPI {Version}.", nameof(Version))
                   }
               );

        /// <summary>
        /// Make the file the library keeps the peers of a version in
        /// unwritable: a directory where it is, which stops root as well. What
        /// it held is put aside, for the next start to find again.
        /// </summary>
        private String BlockPeersFile(String Version)
        {

            var file = PeersFile(Version);

            if (File.Exists(file))
                File.Move(file, file + ".aside");

            System.IO.Directory.CreateDirectory(file);

            return file;

        }

        #endregion

        #region (private) PeersAfterARestart(BlockedPeersFile = null)

        /// <summary>
        /// Stop this hub, give a peers' file that was blocked back what it
        /// held, and ask the next start in the same directory which peers it
        /// knows.
        /// </summary>
        private async Task<String[]> PeersAfterARestart(String? BlockedPeersFile = null)
        {

            await RoamingHub.Stop();

            if (BlockedPeersFile is not null)
            {

                System.IO.Directory.Delete(BlockedPeersFile);

                if (File.Exists(BlockedPeersFile + ".aside"))
                    File.Move(BlockedPeersFile + ".aside", BlockedPeersFile);

            }

            var again = await TestPorts.StartedOnFreshPorts(() => TestRoamingHubs.New(Directory, Configuration, Clock));

            try
            {
                return [.. again.OCPIVersions.SelectMany(version => version.RemoteParties).Select(peer => peer.Id.ToString())];
            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

    }

}
