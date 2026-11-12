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

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.Mail;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.SSH;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;

using cloud.charging.open.RoamingHub.CommandLine;

using RoamingNetwork = cloud.charging.open.protocols.WWCP.POI.RoamingNetwork;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// The command line of a roaming hub: the hub's own, whichever program
    /// runs it, with roamingNetworks and roamingNetworkOperator beside the
    /// commands every node has.
    /// </summary>
    /// <remarks>
    /// The commands are the RoamingHub library's, not the program
    /// RoamingHubCLI's, so they are tested here, typed at the console of a
    /// hub of the tests, and over SSH by an account that may only look.
    /// </remarks>
    [TestFixture]
    public class HubCLITests : ARoamingHubTests
    {

        #region (private) Run(Command)

        /// <summary>
        /// The command, typed at the console of this hub, which may do
        /// everything.
        /// </summary>
        private async Task<String[]> Run(String Command)
        {

            using var cli    = new HubCLI(RoamingHub);
            var       lines  = await cli.Execute(Command);

            TestContext.Out.WriteLine($"{HubCLI.Prompt}{Command}");
            TestContext.Out.WriteLine(String.Join(Environment.NewLine, lines));

            return lines;

        }

        #endregion

        #region (private) RunOverSSHAs(Login, Role, Command)

        /// <summary>
        /// The command, typed over SSH by an account of the given name, made
        /// for the purpose and put in the group of the given role.
        /// </summary>
        private async Task<String[]> RunOverSSHAs(String  Login,
                                                  String  Role,
                                                  String  Command)
        {

            Assert.That(RoamingHub.ExtAPI.TryGetOrganization(Organization_Id.Parse(RoamingHub.DefaultOrganization), out var organization) &&
                        organization is Organization, Is.True, "the hub's organization is not there");

            await RoamingHub.ExtAPI.CreateUser(
                      User_Id.Parse(Login),
                      I18NString.Create(Languages.en, Login),
                      SimpleEMailAddress.Parse($"{Login}@localhost"),
                      User2OrganizationEdgeLabel.IsMember,
                      (Organization) organization!,
                      Password:                  "correct-horse-battery-" + Guid.NewGuid().ToString("N")[..8],
                      SkipDefaultNotifications:  true,
                      SkipNewUserEMail:          true,
                      SkipNewUserNotifications:  true,
                      AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                      IsAuthenticated:           true
                  );

            Assert.That(RoamingHub.ExtAPI.TryGetUser     (User_Id.     Parse(Login), out var account),  Is.True, $"the account '{Login}' was not made");
            Assert.That(RoamingHub.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role),  out var group),    Is.True, $"the hub has no group '{Role}'");
            Assert.That((await RoamingHub.ExtAPI.AddUserToUserGroup((User) account!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!)).IsSuccess,
                        Is.True, $"'{Login}' could not be put in '{Role}'");

            var session  = new SshSessionInfo("42", Login, "password", null, null, null, null, SshSessionRestrictions.None);

            await using var terminal  = new VT100Terminal((bytes, cancellationToken) => ValueTask.CompletedTask);
            using       var cli       = new HubCLI(RoamingHub, terminal, new CLICaller(account!, session));

            var lines = await cli.Execute(Command);

            TestContext.Out.WriteLine($"{Login}@{HubCLI.Prompt}{Command}");
            TestContext.Out.WriteLine(String.Join(Environment.NewLine, lines));

            return lines;

        }

        #endregion

        #region (private) Network(Id)

        /// <summary>
        /// The roaming network of the given identification as this hub serves it now.
        /// </summary>
        private RoamingNetwork Network(String Id)
        {

            Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse(Id), out var history), Is.True, $"this hub serves no roaming network '{Id}'");

            return history!.Head.RoamingNetwork;

        }

        #endregion


        #region OverSSHTheHubHasItsOwnCommands()

        /// <summary>
        /// Whoever signs in over SSH gets the command line of the hub, with its
        /// commands, without the program that runs it having to say so: the
        /// tests run a hub without the program RoamingHubCLI.
        /// </summary>
        [Test]
        public async Task OverSSHTheHubHasItsOwnCommands()
        {

            Assert.That(RoamingHub.CommandLines, Is.Not.Null, "a session over SSH would get the node's command line only");

            await using var terminal  = new VT100Terminal((bytes, cancellationToken) => ValueTask.CompletedTask);
            using       var cli       = RoamingHub.CommandLines!(terminal, CLICaller.Console);

            Assert.That(cli, Is.InstanceOf<HubCLI>());

            foreach (var command in new[] { "roamingNetworks", "roamingNetworkOperator" })
                Assert.That(String.Join(" | ", await cli.Execute(command)), Does.Not.StartWith("Unknown command"), command);

            // And those of every node beside them.
            Assert.That(String.Join(" | ", await cli.Execute("syncNTS help")), Does.Not.StartWith("Unknown command"), "syncNTS");

        }

        #endregion

        #region ACommandIsNamedAsItIsTypedAndAsksForWhatItNeeds(Command)

        /// <summary>
        /// What is typed wrongly is answered by how it is typed rightly.
        /// </summary>
        [TestCase("roamingNetworks add")]
        [TestCase("roamingNetworks QA remove chargingStationOperator DE*QAC")]
        [TestCase("roamingNetworks QA add chargingStationOperator")]
        [TestCase("roamingNetworkOperator rotate")]
        [TestCase("roamingNetworkOperator rotate everything")]
        [TestCase("roamingNetworkOperator name")]
        [TestCase("roamingNetworkOperator whatever")]
        public async Task ACommandIsNamedAsItIsTypedAndAsksForWhatItNeeds(String Command)
        {

            var lines = await Run(Command);

            Assert.That(lines,         Has.Length.EqualTo(1));
            Assert.That(lines.Single(), Does.StartWith($"Usage: {Command.Split(' ')[0]} "));

        }

        #endregion

        #region AHubWithoutRoamingNetworksSaysHowOneIsMade()

        [Test]
        public async Task AHubWithoutRoamingNetworksSaysHowOneIsMade()
        {

            var listed    = await Run("roamingNetworks");
            var shown     = await Run("roamingNetworks QA");
            var operating = await Run("roamingNetworkOperator");

            Assert.Multiple(() => {

                Assert.That(listed.Single(),  Is.EqualTo($"This hub serves no roaming network yet: 'roamingNetworks add <id> [<name>]' makes one, operated by this hub, '{RoamingHub.NetworkOperator.Id}'."));
                Assert.That(shown. Single(),  Is.EqualTo("This hub serves no roaming network 'QA'."));

                Assert.That(operating[0],     Does.StartWith($"This hub is the roaming network operator '{RoamingHub.NetworkOperator.Id}'"));
                Assert.That(operating,        Has.Some.EqualTo($"  signing key   {RoamingHub.NetworkOperator.SigningKey. Id}  {RoamingHub.NetworkOperator.SigningKey. ToJSON()["key"]}"));
                Assert.That(operating,        Has.Some.EqualTo($"  identity key  {RoamingHub.NetworkOperator.IdentityKey.Id}  {RoamingHub.NetworkOperator.IdentityKey.ToJSON()["key"]}"));
                Assert.That(operating,        Has.Some.EqualTo($"  its private keys are kept in '{RoamingHub.NetworkOperator.Directory}'"));
                Assert.That(operating[^1],    Is.EqualTo("  it operates no roaming network yet: 'roamingNetworks add <id> [<name>]' makes one"));

            });

        }

        #endregion

        #region ARoamingNetworkMadeAtTheConsoleIsOperatedByThisHub()

        [Test]
        public async Task ARoamingNetworkMadeAtTheConsoleIsOperatedByThisHub()
        {

            var made       = await Run("roamingNetworks add QA Quality Assurance");
            var again      = await Run("roamingNetworks add QA Quality Assurance");
            var listed     = await Run("roamingNetworks");
            var shown      = await Run("roamingNetworks qa");
            var operating  = await Run("roamingNetworkOperator");

            var network    = Network("QA");
            var url        = $"{RoamingHub.WebInterfaceURL.ToString().TrimEnd('/')}/{RoamingHub.POIPathSegment}/RNs/QA";

            Assert.Multiple(() => {

                Assert.That(made,                    Has.Length.EqualTo(3));
                Assert.That(made[1],                 Does.StartWith("Next: 'roamingNetworks QA add chargingStationOperator <id>"));
                Assert.That(made[2],                 Is.EqualTo(url));

                Assert.That(network.Name.FirstText(),                       Is.EqualTo("Quality Assurance"));
                Assert.That(RoamingHub.NetworkOperator.Operates(network),   Is.True);

                Assert.That(again.Single(),          Does.StartWith("No roaming network was made: "));

                Assert.That(listed.Single(),         Does.StartWith("QA  revision ").
                                                     And.Contain("Quality Assurance").
                                                     And.EndWith($"(operated by '{RoamingHub.NetworkOperator.Id}', this hub, 0 charging station operator(s), 0 charging pool(s))"));

                Assert.That(shown[0],                Does.StartWith("QA  revision ").And.EndWith("Quality Assurance"));
                Assert.That(shown[1],                Is.EqualTo($"  operated by '{RoamingHub.NetworkOperator.Id}', this hub"));
                Assert.That(shown[^1],               Is.EqualTo(url));

                Assert.That(operating[^1],           Is.EqualTo("  it operates QA"));

            });

        }

        #endregion

        #region APartyWithKeysWaitsForTheProofThatItHoldsThem()

        /// <summary>
        /// A charging station operator added at the console waits for the
        /// signature of its identity key, and is not in the network before it.
        /// </summary>
        [Test]
        public async Task APartyWithKeysWaitsForTheProofThatItHoldsThem()
        {

            await Run("roamingNetworks add QA");

            var signingKey   = POIPublicKey.From(POITests.NewKey(COSECurve.P256)).ToJSON()["key"];
            var identityKey  = POIPublicKey.From(POITests.NewKey(COSECurve.P521)).ToJSON()["key"];

            var asked        = await Run($"roamingNetworks QA add chargingStationOperator DE*QAC signingKey {signingKey} identityKey {identityKey} QA Charging");

            Assert.Multiple(() => {

                Assert.That(asked,                       Is.Not.Empty);
                Assert.That(asked[0],                    Does.Not.StartWith("Nothing was added"));
                Assert.That(Network("QA").ChargingStationOperators, Is.Empty, "added before the proof that it holds its identity key");

                Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("QA"), out var history) &&
                            history!.PendingChangeSets.Count > 0, Is.True, "nothing waits for the signature of its identity key");

            });

        }

        #endregion

        #region AGridOperatorWithoutKeysIsAddedAtOnce()

        /// <summary>
        /// A grid operator not taking part comes without keys, and is in the
        /// network at once: this hub signs in its place.
        /// </summary>
        [Test]
        public async Task AGridOperatorWithoutKeysIsAddedAtOnce()
        {

            await Run("roamingNetworks add QA");

            var added  = await Run("roamingNetworks QA add gridOperator DE*GRD Grid of QA");
            var shown  = await Run("roamingNetworks QA");

            Assert.Multiple(() => {

                Assert.That(added.Single(),  Does.Not.StartWith("Nothing was added"));
                Assert.That(Network("QA").Parties.Select(party => party.Id.EntityId.ToString()), Has.Member("DE*GRD"));
                Assert.That(shown,           Has.Some.EqualTo("  GridOperator DE*GRD  without keys: its roaming network operator signs for it"));

            });

        }

        #endregion

        #region APartyIsOneOfTheKnownKinds()

        [Test]
        public async Task APartyIsOneOfTheKnownKinds()
        {

            await Run("roamingNetworks add QA");

            var parties = Network("QA").Parties.Count();
            var refused = await Run("roamingNetworks QA add chargingStation DE*QAC*S1");

            Assert.That(refused.Single(),               Does.StartWith("A party is one of chargingStationOperator, ").And.EndWith(", not 'chargingStation'."));
            Assert.That(Network("QA").Parties.Count(),  Is.EqualTo(parties), "a party was added all the same");

        }

        #endregion

        #region ARotatedKeyAndANewNameReachEveryRoamingNetworkOperated()

        [Test]
        public async Task ARotatedKeyAndANewNameReachEveryRoamingNetworkOperated()
        {

            await Run("roamingNetworks add QA");

            var signingKeyBefore   = RoamingHub.NetworkOperator.SigningKey. Id;
            var identityKeyBefore  = RoamingHub.NetworkOperator.IdentityKey.Id;

            var signing            = await Run("roamingNetworkOperator rotate signingKey");
            var identity           = await Run("roamingNetworkOperator rotate identityKey");
            var renamed            = await Run("roamingNetworkOperator name QA Operator");

            Assert.Multiple(() => {

                Assert.That(signing. Single(),  Is.Not.Empty);
                Assert.That(identity.Single(),  Is.Not.Empty);
                Assert.That(renamed. Single(),  Is.Not.Empty);

                Assert.That(RoamingHub.NetworkOperator.SigningKey. Id,  Is.Not.EqualTo(signingKeyBefore));
                Assert.That(RoamingHub.NetworkOperator.IdentityKey.Id,  Is.Not.EqualTo(identityKeyBefore));

                Assert.That(RoamingHub.NetworkOperator.Operator.Name.FirstText(),  Is.EqualTo("QA Operator"));
                Assert.That(Network("QA").Operator?.Name.FirstText(),              Is.EqualTo("QA Operator"));

            });

        }

        #endregion

        #region AViewerOverSSHMayLookButNotChange()

        /// <summary>
        /// What signs with the keys of this hub is poi:edit, as on the web
        /// interface: an account that may only look is told so, and nothing
        /// changes.
        /// </summary>
        [Test]
        public async Task AViewerOverSSHMayLookButNotChange()
        {

            await Run("roamingNetworks add QA");

            var signingKeyBefore  = RoamingHub.NetworkOperator.SigningKey.Id;

            var listed            = await RunOverSSHAs("looker", "viewer", "roamingNetworks");
            var made              = await RunOverSSHAs("maker",  "viewer", "roamingNetworks add OTHER");
            var added             = await RunOverSSHAs("adder",  "viewer", "roamingNetworks QA add gridOperator DE*GRD");
            var rotated           = await RunOverSSHAs("rotor",  "viewer", "roamingNetworkOperator rotate signingKey");
            var renamed           = await RunOverSSHAs("namer",  "viewer", "roamingNetworkOperator name Somebody Else");

            Assert.Multiple(() => {

                Assert.That(listed.Single(),   Does.StartWith("QA  revision "));

                foreach (var (login, refused) in new[] { ("maker", made), ("adder", added), ("rotor", rotated), ("namer", renamed) })
                    Assert.That(refused.Single(), Does.StartWith($"'{login}' may not do that here: it needs the "), login);

                Assert.That(RoamingHub.POI.TryGetRoamingNetworkHistory(RoamingNetwork_Id.Parse("OTHER"), out _),  Is.False);
                Assert.That(Network("QA").Parties.Select(party => party.Id.EntityId.ToString()),                   Has.No.Member("DE*GRD"));
                Assert.That(RoamingHub.NetworkOperator.SigningKey.Id,                                              Is.EqualTo(signingKeyBefore));
                Assert.That(RoamingHub.NetworkOperator.Operator.Name.FirstText(),                                   Is.Not.EqualTo("Somebody Else"));

            });

        }

        #endregion

    }

}
