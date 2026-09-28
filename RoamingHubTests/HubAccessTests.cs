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
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Web;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// Who may do what on a hub: its two resources beside the node's, its own
    /// viewer and its operator beside the node's administrators - and a role
    /// from the configuration file, heard by the API like every other.
    /// </summary>
    /// <remarks>
    /// EV's VehicleAccessTests, for a hub. The one thing a hub has that a
    /// vehicle does not is something its viewer must not read: the traffic.
    /// </remarks>
    public class HubAccessTests
    {

        #region Data

        private const String  NoTimeServers  = """{ "nts": { "enabled": false } }""";

        private String       directory  = "";
        private RoamingHub?  hub;
        private Uri?         address;

        #endregion

        #region Setup / TearDown

        [SetUp]
        public void Setup()
        {

            directory = Path.Combine(Path.GetTempPath(), "hub-access-" + Guid.NewGuid().ToString("N")[..12]);

            Directory.CreateDirectory(directory);

        }

        [TearDown]
        public async Task TearDown()
        {

            if (hub is not null)
                await hub.DisposeAsync();

            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // A temporary directory that outlives one test run is not worth
                // failing the run over.
            }

        }

        #endregion


        #region (helper) Hub(Configuration)

        /// <summary>
        /// A hub with the given configuration file, on a free port of the
        /// loopback - made, and not yet started.
        /// </summary>
        private RoamingHub Hub(String Configuration = NoTimeServers)
        {

            var probe  = new TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            var port   = ((IPEndPoint) probe.LocalEndpoint).Port;
            probe.Stop();

            var file   = Path.Combine(directory, WWCPConfigFile.DefaultFileName);
            File.WriteAllText(file, Configuration);

            hub        = new RoamingHub(
                             HTTPPort:          IPPort.Parse(port),
                             AccountsPath:      Path.Combine(directory, "accounts"),
                             ConfigFile:        new WWCPConfigFile(file),
                             CertificatesPath:  Path.Combine(directory, "certificates"),
                             LogToConsole:      false,
                             BridgeDebugLog:    false
                         );

            address    = new Uri($"http://127.0.0.1:{port}/");

            return hub;

        }

        #endregion

        #region (helper) SignedInAs(Name, Role)

        /// <summary>
        /// A client signed in with a password as an account of the given name,
        /// made for the purpose and put in the group of the given role - made
        /// the way the hub makes its first one, so that it may sign in.
        /// </summary>
        private async Task<HttpClient> SignedInAs(String  Name,
                                                  String  Role)
        {

            var password = "correct-horse-battery-" + Guid.NewGuid().ToString("N")[..8];

            Assert.That(hub!.ExtAPI.TryGetOrganization(Organization_Id.Parse(RoamingHub.DefaultOrganization), out var organization) &&
                        organization is Organization, Is.True, "the hub's organization is not there");

            var account = await hub.ExtAPI.CreateUser(
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

            Assert.That(account,                                                              Is.Not.Null, $"the account '{Name}' was not made");
            Assert.That(hub.ExtAPI.TryGetUser(User_Id.Parse(Name), out var stored),           Is.True);
            Assert.That(hub.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role), out var group),  Is.True, $"the hub has no group '{Role}'");

            var joined = await hub.ExtAPI.AddUserToUserGroup((User) stored!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!);

            Assert.That(joined.IsSuccess, Is.True, $"'{Name}' could not be put in '{Role}'");

            var client = new HttpClient {
                             BaseAddress  = address,
                             Timeout      = TimeSpan.FromSeconds(30)
                         };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                             "Basic",
                                                             Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Name}:{password}"))
                                                         );

            return client;

        }

        #endregion

        #region (helper) Put(Client, Path, JSON)

        private static Task<HttpResponseMessage> Put(HttpClient  Client,
                                                     String      Path,
                                                     String      JSON)

            => Client.PutAsync(Path, new StringContent(JSON, Encoding.UTF8, "application/json"));

        #endregion

        #region (helper) HubAccessControl()

        /// <summary>
        /// The hub's resources and roles, as a node told nothing else puts
        /// them together.
        /// </summary>
        private static AccessControl HubAccessControl()
        {

            Assert.That(AccessControl.TryCombine(HubAccess.Resources, HubAccess.Roles, null, null,
                                                 "roaming hub", out var access, out _, out var error),
                        Is.True, error);

            return access!;

        }

        #endregion


        #region AHubKnowsItsResourcesAndItsThreeRoles()

        /// <summary>
        /// The node brings the administrators, and the hub its own viewer and
        /// its operator - the viewer in the node's place, so that the one
        /// every node calls "viewer" is the one that cannot read the traffic.
        /// </summary>
        [Test]
        public void AHubKnowsItsResourcesAndItsThreeRoles()
        {

            var roamingHub = Hub();

            Assert.Multiple(() => {
                Assert.That(roamingHub.Roles,             Is.EqualTo(new[] { "viewer", "hub", WWCPNode.AdminRole }));
                Assert.That(roamingHub.Access.Resources,  Is.EqualTo(new[] { "configuration", "dns", "nts", "certificates", "peers", "traffic" }));
            });

        }

        #endregion

        #region EachRoleMayDoWhatItAlwaysMayDo(Role, Permission, Allowed)

        /// <summary>
        /// What each role could do before roles were data, permission by
        /// permission: the viewer looks at everything but the traffic, the
        /// operator also repoints and asks and reads the traffic, and only the
        /// administrators touch the peers and the certificates.
        /// </summary>
        [TestCase("viewer",       "configuration:read", true)]
        [TestCase("viewer",       "dns:read",           true)]
        [TestCase("viewer",       "nts:read",           true)]
        [TestCase("viewer",       "certificates:read",  true)]
        [TestCase("viewer",       "peers:read",         true)]
        [TestCase("viewer",       "traffic:read",       false)]
        [TestCase("viewer",       "dns:edit",           false)]
        [TestCase("viewer",       "dns:run",            false)]
        [TestCase("viewer",       "nts:run",            false)]

        [TestCase("hub",          "dns:edit",           true)]
        [TestCase("hub",          "dns:run",            true)]
        [TestCase("hub",          "nts:edit",           true)]
        [TestCase("hub",          "nts:run",            true)]
        [TestCase("hub",          "traffic:read",       true)]
        [TestCase("hub",          "peers:read",         true)]
        [TestCase("hub",          "peers:edit",         false)]
        [TestCase("hub",          "peers:run",          false)]
        [TestCase("hub",          "certificates:edit",  false)]

        [TestCase("systemadmin",  "peers:edit",         true)]
        [TestCase("systemadmin",  "peers:run",          true)]
        [TestCase("systemadmin",  "certificates:edit",  true)]
        [TestCase("systemadmin",  "traffic:read",       true)]
        public void EachRoleMayDoWhatItAlwaysMayDo(String Role, String Permission, Boolean Allowed)
        {

            Assert.That(protocols.WWCP.Node.Web.Permission.TryParse(Permission, out var permission, out var error), Is.True, error);

            Assert.That(HubAccessControl().RoleNamed(Role)!.Allows(permission.Resource, permission.Operation), Is.EqualTo(Allowed));

        }

        #endregion


        #region AViewerMayLookAtTheDNSSettingsAndIsToldWhoMayChangeThem()

        /// <summary>
        /// Over the wire, as a browser signed in as a viewer sees it: the page
        /// opens, the save is refused with the roles to ask for, the traffic
        /// is refused too, and what the browser is told it may do says the same
        /// beforehand.
        /// </summary>
        [Test]
        public async Task AViewerMayLookAtTheDNSSettingsAndIsToldWhoMayChangeThem()
        {

            await Hub().Start();

            using var viewer  = await SignedInAs("viewer1", "viewer");

            var looked        = await viewer.GetAsync("api/v1/configuration/dns");
            var changed       = await Put(viewer, "api/v1/configuration/dns", "{}");
            var refusal       = await changed.Content.ReadAsStringAsync();
            var traffic       = await viewer.GetAsync("api/v1/traffic");
            var me            = JObject.Parse(await (await viewer.GetAsync("api/v1/auth/me")).Content.ReadAsStringAsync());
            var permissions   = me["permissions"]!.Values<String>().OfType<String>().ToArray();

            Assert.Multiple(() => {
                Assert.That(looked.StatusCode,               Is.EqualTo(HttpStatusCode.OK));
                Assert.That(changed.StatusCode,              Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(refusal,                         Does.Contain("This needs the hub or systemadmin role."));
                Assert.That(traffic.StatusCode,              Is.EqualTo(HttpStatusCode.Forbidden),
                            "what the peers say to each other is not the viewer's to read");
                Assert.That(me["roles"]!.Values<String>(),   Is.EqualTo(new[] { "viewer" }));
                Assert.That(permissions,                     Does.Contain("dns:read").And.Contain("peers:read").And.Contain("certificates:read"));
                Assert.That(permissions,                     Does.Not.Contain("dns:edit").And.Not.Contain("traffic:read").And.Not.Contain("peers:edit"));
                Assert.That(permissions.Any(permission => permission.StartsWith('*')),
                            Is.False,
                            "spelt out resource by resource, so that a page asking \"dns:read\" need not know what \"*\" is");
            });

        }

        #endregion

        #region AnOperatorMayRepointTheHubAndReadTheTrafficButNotLetInAPeer()

        /// <summary>
        /// The operator's day: the traffic opens, and adding a peer is refused
        /// with the one role that may - which is the line between running a hub
        /// and deciding who is let into it.
        /// </summary>
        [Test]
        public async Task AnOperatorMayRepointTheHubAndReadTheTrafficButNotLetInAPeer()
        {

            await Hub().Start();

            using var operatorClient  = await SignedInAs("operator1", "hub");

            var traffic   = await operatorClient.GetAsync("api/v1/traffic");
            var addPeer   = await operatorClient.PostAsync("api/v1/ocpi/partners",
                                                           new StringContent("""{ "countryCode": "DE", "partyId": "ABC", "role": "CPO" }""",
                                                                             Encoding.UTF8, "application/json"));
            var refusal   = await addPeer.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That(traffic.StatusCode,  Is.EqualTo(HttpStatusCode.OK));
                Assert.That(addPeer.StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(refusal,             Does.Contain("This needs the systemadmin role."));
            });

        }

        #endregion

        #region ARoleFromTheConfigurationFileIsHeardByTheAPI()

        /// <summary>
        /// A role nobody compiled in: the file names it, the start makes its
        /// group, and a route asking for a permission lets it in or not by what
        /// the file says it carries.
        /// </summary>
        [Test]
        public async Task ARoleFromTheConfigurationFileIsHeardByTheAPI()
        {

            await Hub("""
                      {
                        "nts":   { "enabled": false },
                        "roles": { "support": [ "dns:read", "traffic:read" ] }
                      }
                      """).Start();

            using var support  = await SignedInAs("supporter", "support");

            var dns            = await support.GetAsync("api/v1/configuration/dns");
            var traffic        = await support.GetAsync("api/v1/traffic");
            var nts            = await support.GetAsync("api/v1/configuration/nts");
            var refusal        = await nts.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That(hub!.Roles,          Is.EqualTo(new[] { "viewer", "hub", "support", WWCPNode.AdminRole }));
                Assert.That(dns.StatusCode,      Is.EqualTo(HttpStatusCode.OK));
                Assert.That(traffic.StatusCode,  Is.EqualTo(HttpStatusCode.OK));
                Assert.That(nts.StatusCode,      Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(refusal,             Does.Contain("This needs the viewer or hub or systemadmin role."),
                            "the file's role carries dns:read and traffic:read and nothing else, so it is not among the ones to ask for");
            });

        }

        #endregion


        #region TheStatusSaysWhoTheHubIsInOCPIAfterItsVersion()

        /// <summary>
        /// The node writes every node's status; the hub adds who it is in OCPI,
        /// right after the version - the one thing every peer wrote into its
        /// credentials.
        /// </summary>
        [Test]
        public async Task TheStatusSaysWhoTheHubIsInOCPIAfterItsVersion()
        {

            await Hub().Start();

            using var viewer  = await SignedInAs("viewer2", "viewer");

            var answer        = await viewer.GetAsync("api/v1/status");
            var status        = JObject.Parse(await answer.Content.ReadAsStringAsync());
            var names         = status.Properties().Select(property => property.Name).ToList();

            Assert.Multiple(() => {
                Assert.That(answer.StatusCode,                 Is.EqualTo(HttpStatusCode.OK));
                Assert.That(status.Value<String>("service"),   Is.EqualTo("RoamingHub"));
                Assert.That(status.Value<String>("partyId"),   Is.EqualTo(hub!.PartyIdText));
                Assert.That(names.IndexOf("partyId"),          Is.EqualTo(names.IndexOf("version") + 1), String.Join(", ", names));
                Assert.That(names,                             Does.Contain("hermod").And.Contain("uptime").And.Contain("log"),
                            "and everything else every node's status says");
            });

        }

        #endregion

        #region TheClockIsWhereEveryNodeHasItAndNeedsTheTimeServers()

        /// <summary>
        /// The clock at /api/v1/clock, where every node has it, and read with
        /// the time servers' permission on a hub: a viewer may, a role of the
        /// file without nts:read is told who may, and nobody signed in is
        /// asked to sign in. Its old path is the JSON API's 404 now.
        /// </summary>
        [Test]
        public async Task TheClockIsWhereEveryNodeHasItAndNeedsTheTimeServers()
        {

            await Hub("""
                      {
                        "nts":   { "enabled": false },
                        "roles": { "support": [ "dns:read", "traffic:read" ] }
                      }
                      """).Start();

            using var viewer     = await SignedInAs("viewer3",    "viewer");
            using var support    = await SignedInAs("supporter2", "support");
            using var anonymous  = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };

            var read             = await viewer.   GetAsync("api/v1/clock");
            var refused          = await support.  GetAsync("api/v1/clock");
            var refusal          = await refused.Content.ReadAsStringAsync();
            var nobody           = await anonymous.GetAsync("api/v1/clock");
            var oldPath          = await viewer.   GetAsync("api/v1/configuration/time");
            var notThere         = await oldPath.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That(read.StatusCode,     Is.EqualTo(HttpStatusCode.OK));
                Assert.That(refused.StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(refusal,             Does.Contain("This needs the viewer or hub or systemadmin role."),
                            "the clock asks for nts:read on a hub, which the file's role does not carry");
                Assert.That(nobody.StatusCode,   Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(oldPath.StatusCode,  Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(notThere,            Does.Contain("Unknown API path"),
                            "the JSON API's own 404, and not the web interface's page");
            });

        }

        #endregion

    }

}
