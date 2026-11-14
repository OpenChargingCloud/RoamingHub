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

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.RoamingHub.CommandLine
{

    /// <summary>
    /// The roaming networks of the points of interest: listed, a new one made, one shown, and a party added to one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// roamingNetworks lists them, each with its revision, its operator and what it holds; roamingNetworks add
    /// &lt;id&gt; [&lt;name&gt;] makes one, operated by this hub - see roamingNetworkOperator. roamingNetworks
    /// &lt;id&gt; shows one with its parties and their keys; roamingNetworks &lt;id&gt; add chargingStationOperator
    /// &lt;operatorId&gt; signingKey &lt;key&gt; identityKey &lt;key&gt; [signingKey &lt;key&gt; | identityKey
    /// &lt;key&gt; ...] [&lt;name&gt;] adds one of its parties - as well eMobilityProvider, gridOperator,
    /// parkingOperator, chargingStationManufacturer, energyMeterManufacturer, transparencySoftwareManufacturer or
    /// conformityAssessmentBody - with the public keys of that party: each the Base64 of its COSE key, as the "key" of a
    /// public key in POI 3 says it; a grid operator and a conformity assessment body not taking part come without. A
    /// charging station or energy meter manufacturer takes homepage &lt;url&gt; after its keys as well. The words after
    /// the identification, or after the keys, are the name, in English.
    /// </para>
    /// <para>
    /// Making one and adding a party sign with the keys of this hub, and are poi:edit, as on the web interface: the
    /// administrators, and the console, which may do everything. Everything else in a roaming network comes as
    /// change sets its owners sign, submitted to the POI API.
    /// </para>
    /// </remarks>
    /// <param name="CLI">The command line.</param>
    public class RoamingNetworksCommand(HubCLI CLI) : ACLICommand<HubCLI>(CLI),
                                                      ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public const String CommandName = "roamingNetworks";

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
                return CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                           ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                           : [];

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            var rest = Arguments.Skip(1).ToArray();

            if (rest.Length == 0)
                return Task.FromResult(Listed());

            if (rest[0].Equals("add", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(Add(rest[1..]));

            if (rest[0].Equals("follow", StringComparison.OrdinalIgnoreCase) && rest.Length > 1)
                return Follow(rest[1..]);

            if (rest[0].Equals("unfollow", StringComparison.OrdinalIgnoreCase) && rest.Length == 2)
                return Unfollow(rest[1]);

            if (!RoamingNetwork_Id.TryParse(rest[0], out var roamingNetworkId))
                return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

            if (rest.Length == 1)
                return Task.FromResult(Shown(roamingNetworkId));

            if (rest[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(AddParty(roamingNetworkId, rest[2..]));

            if (rest[1].Equals("sign", StringComparison.OrdinalIgnoreCase) && rest.Length <= 3)
                return Sign(roamingNetworkId, rest.Length == 3 ? rest[2] : null);

            if (rest[1].Equals("shareholders", StringComparison.OrdinalIgnoreCase) && rest.Length == 2)
                return Task.FromResult(Shareholders(roamingNetworkId));

            if (rest[1].Equals("join", StringComparison.OrdinalIgnoreCase) && (rest.Length == 2 || (rest.Length == 4 && rest[2].Equals("at", StringComparison.OrdinalIgnoreCase))))
                return Join(roamingNetworkId, rest.Length == 4 ? rest[3] : null);

            if (rest[1].Equals("admit", StringComparison.OrdinalIgnoreCase) && rest.Length > 2)
                return ChangeShareholders(roamingNetworkId, "admit", String.Join(" ", rest[2..]));

            if (rest[1].Equals("dismiss", StringComparison.OrdinalIgnoreCase) && rest.Length > 2)
                return ChangeShareholders(roamingNetworkId, "dismiss", String.Join(" ", rest[2..]));

            if (rest[1].Equals("resign", StringComparison.OrdinalIgnoreCase) && rest.Length == 2 && cli.Hub.NetworkOperator.Holder is String self)
                return ChangeShareholders(roamingNetworkId, "dismiss", self);

            return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [add <id> [<name>] | follow <trust anchor> [at <url>] | unfollow <id> | <id> [sign [<commit>] | shareholders | join [at <url>] | admit <holder> | dismiss <holder> | resign | add chargingStationOperator|eMobilityProvider|gridOperator|parkingOperator|" +
                "chargingStationManufacturer|energyMeterManufacturer|transparencySoftwareManufacturer|conformityAssessmentBody <id> " +
                "signingKey <key> identityKey <key> [signingKey <key> | identityKey <key> ...] [homepage <url>] [<name>]]] - the roaming networks of the points of interest, " +
                "a new one operated by this hub, one of another server followed by its trust anchor or followed no longer, one of them, a commit of one followed signed as a holder of the keys of its operator, who holds those keys, asking to hold them as well, admitting who asked, dismissing a holder or resigning, and a new party of one";

        #endregion


        #region (private) Listed()

        private String[] Listed()
        {

            var lines = new List<String>();

            foreach (var network in cli.Hub.POI.RoamingNetworks.OrderBy(network => network.Id.ToString(), StringComparer.OrdinalIgnoreCase))
            {

                var revision = cli.Hub.POI.TryGetRoamingNetworkHistory(network.Id, out var history)
                                   ? $"revision {history.Head.Commit.Revision}"
                                   : "no history";

                var operators = network.ChargingStationOperators.Count();
                var pools     = network.ChargingStationOperators.Sum(chargingStationOperator => chargingStationOperator.ChargingPools.Count());

                lines.Add($"{network.Id}  {revision}  {network.Name.FirstText()}".TrimEnd() +
                          $"  ({OperatedBy(network)}, {operators} charging station operator(s), {pools} charging pool(s))");

            }

            // The ones of other servers, with those that have no copy here yet.
            foreach (var followed in cli.Hub.FollowedRoamingNetworks)
                lines.Add($"{followed.Id}  followed at {followed.URL}, {(followed.IsConnected ? "connected" : "not connected")}" +
                          (followed.History is null     ? ", no copy here yet"  : "") +
                          (followed.Error is String why ? $": {why}"            : ""));

            return lines.Count > 0
                       ? [ .. lines ]
                       : [ $"This hub serves no roaming network yet: '{CommandName} add <id> [<name>]' makes one, operated by this hub, '{cli.Hub.NetworkOperator.Id}'." ];

        }

        #endregion

        #region (private) Shareholders(RoamingNetworkId) / Join(RoamingNetworkId, URL) / ChangeShareholders(RoamingNetworkId, What, Holder)

        private String[] Shareholders(RoamingNetwork_Id RoamingNetworkId)
        {

            if (!cli.Hub.POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out var history) ||
                history.Head.RoamingNetwork.Operator is not RoamingNetworkOperator networkOperator)
            {
                return [ $"This hub serves no roaming network '{RoamingNetworkId}'." ];
            }

            var holders  = POIPartyKeys.Holders(networkOperator.IdentityKeys).Order(StringComparer.Ordinal).ToArray();
            var lines    = new List<String> {
                               $"The keys of the operator '{networkOperator.Id}' of the roaming network '{RoamingNetworkId}' are held by {holders.Length}, {networkOperator.MinConsensus} of them deciding:"
                           };

            foreach (var holder in holders)
                lines.Add($"  {(holder.Length > 0 ? holder : "(unnamed)")}{(holder == cli.Hub.NetworkOperator.Holder && cli.Hub.NetworkOperator.SigningKeyIdIn(history.Head.RoamingNetwork) is not null ? ", this hub" : "")}  " +
                          $"identity key(s) {String.Join(", ", networkOperator.IdentityKeys.Where(key => key.HolderOrUnnamed == holder).Select(key => key.Id[..16] + "…"))}, " +
                          $"signing key(s) {String.Join(", ", networkOperator.SigningKeys.Where(key => key.HolderOrUnnamed == holder).Select(key => key.Id[..16] + "…"))}");

            foreach (var change in cli.Hub.ShareholdersOf(RoamingNetworkId)?["changes"]?.OfType<JObject>() ?? [])
                lines.Add($"  waiting: {change["kind"]} '{change["holder"]}', its key statement signed by {change["keyStatement"]?["signatures"]?.Count() ?? 0}" +
                          (change["changeSet"] is JToken changeSet ? $", change set {changeSet}" : ", no change set yet"));

            if (cli.Hub.IsFollowed(RoamingNetworkId))
                lines.Add("  what waits is told where it is followed.");

            return [ .. lines ];

        }

        private async Task<String[]> Join(RoamingNetwork_Id  RoamingNetworkId,
                                          String?            URLText)
        {

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} {RoamingNetworkId} join", out var refused))
                return [ refused ];

            URL? url = null;

            if (URLText is not null)
            {

                if (!URL.TryParse(URLText, out var parsed))
                    return [ $"'{URLText}' is no URL. Usage: {Help()}" ];

                url = parsed;

            }

            var result = await cli.Hub.JoinRoamingNetworkOperator(RoamingNetworkId, url);

            if (result.Success)
                cli.Hub.Log.Notice($"{cli.Caller.Asker} had this hub ask to hold, or sign for holding, the keys of the operator of the roaming network '{RoamingNetworkId}'.",
                                   cli.Caller.Tags("poi", "shareholders", "security"));

            return [ result.Message ];

        }

        private async Task<String[]> ChangeShareholders(RoamingNetwork_Id  RoamingNetworkId,
                                                        String             What,
                                                        String             Holder)
        {

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} {RoamingNetworkId} {What}", out var refused))
                return [ refused ];

            var result = What == "admit"
                             ? await cli.Hub.AdmitShareholder  (RoamingNetworkId, Holder.Trim())
                             : await cli.Hub.DismissShareholder(RoamingNetworkId, Holder.Trim());

            if (result.Success)
                cli.Hub.Log.Notice($"{cli.Caller.Asker} had this hub sign for {(What == "admit" ? "admitting" : "dismissing")} '{Holder.Trim()}' as a holder of the keys of the operator of the roaming network '{RoamingNetworkId}'.",
                                   cli.Caller.Tags("poi", "shareholders", "security"));

            return [ result.Message ];

        }

        #endregion

        #region (private) Sign(RoamingNetworkId, CommitId)

        private async Task<String[]> Sign(RoamingNetwork_Id  RoamingNetworkId,
                                          String?            CommitId)
        {

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} {RoamingNetworkId} sign", out var refused))
                return [ refused ];

            RoamingNetworkCommit_Id? commitId = null;

            if (CommitId is not null)
            {

                if (!RoamingNetworkCommit_Id.TryParse(CommitId, out var parsed))
                    return [ $"'{CommitId}' is no identification of a commit. Usage: {Help()}" ];

                commitId = parsed;

            }

            var result = await cli.Hub.SignFollowedCommit(RoamingNetworkId, commitId);

            if (result.Status == RoamingNetworkCommitSigningStatus.Signed)
                cli.Hub.Log.Notice($"{cli.Caller.Asker} had the commit '{result.Commit?.Id}' of the roaming network '{RoamingNetworkId}' signed.",
                                   cli.Caller.Tags("poi", "follow", "security"));

            return [ result.Message ];

        }

        #endregion

        #region (private) Follow(Arguments) / Unfollow(Id)

        /// <summary>
        /// roamingNetworks follow &lt;trust anchor&gt; [at &lt;url&gt;]: the trust anchor as its URL, or as the text of its QR
        /// code, which may have spaces in it.
        /// </summary>
        private async Task<String[]> Follow(String[] Arguments)
        {

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} follow", out var refused))
                return [ refused ];

            URL? url = null;

            if (Arguments.Length > 2 && Arguments[^2].Equals("at", StringComparison.OrdinalIgnoreCase))
            {

                if (!URL.TryParse(Arguments[^1], out var parsed))
                    return [ $"'{Arguments[^1]}' is no URL. Usage: {Help()}" ];

                url        = parsed;
                Arguments  = Arguments[..^2];

            }

            var result = await cli.Hub.FollowRoamingNetwork(String.Join(" ", Arguments), url);

            if (result.Success)
                cli.Hub.Log.Notice($"{cli.Caller.Asker} had the roaming network '{result.RoamingNetworkId}' followed.",
                                   cli.Caller.Tags("poi", "follow", "security"));

            return [ result.Message ];

        }

        private async Task<String[]> Unfollow(String Id)
        {

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} unfollow", out var refused))
                return [ refused ];

            if (!RoamingNetwork_Id.TryParse(Id, out var roamingNetworkId))
                return [ $"Usage: {Help()}" ];

            var result = await cli.Hub.UnfollowRoamingNetwork(roamingNetworkId);

            if (result.Success)
                cli.Hub.Log.Notice($"{cli.Caller.Asker} had the roaming network '{roamingNetworkId}' followed no longer.",
                                   cli.Caller.Tags("poi", "follow", "security"));

            return [ result.Message ];

        }

        #endregion

        #region (private) Shown(RoamingNetworkId)

        private String[] Shown(RoamingNetwork_Id RoamingNetworkId)
        {

            if (!cli.Hub.POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out var history))
                return [ $"This hub serves no roaming network '{RoamingNetworkId}'." ];

            var network  = history.Head.RoamingNetwork;
            var signedBy = RoamingHub.OperatorSignersOf(history.Head.Commit, network);
            var lines    = new List<String> {
                               $"{network.Id}  revision {history.Head.Commit.Revision}, commit {history.Head.Commit.Id}  {network.Name.FirstText()}".TrimEnd(),
                               $"  {OperatedBy(network)}",
                               $"  its head is signed by {(signedBy.Count > 0 ? String.Join(", ", signedBy) : "nobody")} as its operator; {network.Operator?.MinConsensus ?? 1} of " +
                               $"{(network.Operator is not null ? POIPartyKeys.Holders(network.Operator.IdentityKeys).Count : 0)} holder(s) of its keys decide"
                           };

            if (cli.Hub.FollowedRoamingNetworks.FirstOrDefault(followed => followed.Id == RoamingNetworkId) is FollowedRoamingNetwork followedAt)
                lines.Add($"  followed at {followedAt.URL}, {(followedAt.IsConnected ? "connected" : "not connected")}" +
                          (cli.Hub.NetworkOperator.SigningKeyIdIn(network) is not null
                               ? $"; this hub holds keys of its operator: '{CommandName} {network.Id} sign' signs its head"
                               : "; this hub holds no key of its operator"));

            foreach (var (partyId, party) in network.Parties)
                lines.Add($"  {partyId.EntityType} {partyId.EntityId}  " +
                          (party.SigningKeys.IsDefaultOrEmpty
                               ? "without keys: its roaming network operator signs for it"
                               : $"signing key(s) {String.Join(", ", party.SigningKeys.Select(key => key.Id[..16] + "…"))}, " +
                                 $"identity key(s) {String.Join(", ", party.IdentityKeys.Select(key => key.Id[..16] + "…"))}"));

            lines.Add($"{cli.Hub.WebInterfaceURL.ToString().TrimEnd('/')}/{RoamingHub.POIPathSegment}/RNs/{network.Id}");

            return [ .. lines ];

        }

        #endregion

        #region (private) Add(Words)

        private String[] Add(String[] Words)
        {

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} add", out var refused))
                return [ refused ];

            if (Words.Length == 0)
                return [ $"Usage: {Help()}" ];

            var name = String.Join(" ", Words[1..]).Trim();
            var json = new JObject(new JProperty("@id", Words[0]));

            if (name.Length > 0)
                json["name"] = new JObject(new JProperty("en", name));

            var result = cli.Hub.CreateRoamingNetwork(json);

            if (!result.Success || result.History is null)
                return [ $"No roaming network was made: {result.Message}" ];

            var network = result.History.Head.RoamingNetwork;

            cli.Hub.Log.Notice(
                $"{cli.Caller.Asker} made the roaming network '{network.Id}', operated by this hub, '{cli.Hub.NetworkOperator.Id}'.",
                cli.Caller.Tags("poi", "security")
            );

            return [
                result.Message,
                $"Next: '{CommandName} {network.Id} add chargingStationOperator <id> signingKey <key> identityKey <key> [<name>]'.",
                $"{cli.Hub.WebInterfaceURL.ToString().TrimEnd('/')}/{RoamingHub.POIPathSegment}/RNs/{network.Id}"
            ];

        }

        #endregion

        #region (private) AddParty(RoamingNetworkId, Words)

        private String[] AddParty(RoamingNetwork_Id  RoamingNetworkId,
                                  String[]           Words)
        {

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} {RoamingNetworkId} add", out var refused))
                return [ refused ];

            if (Words.Length < 2)
                return [ $"Usage: {Help()}" ];

            // Its entity type, in the spelling of a JSON property: chargingStationOperator, or chargingStationManufacturer.
            var entityType      = RoamingHub.PartyTypes.Values.FirstOrDefault(type => PartyWord(type).Equals(Words[0], StringComparison.OrdinalIgnoreCase));

            if (entityType is null)
                return [ $"A party is one of {String.Join(", ", RoamingHub.PartyTypes.Values.Select(PartyWord))}, not '{Words[0]}'." ];

            var manufacturer    = entityType is nameof(ChargingStationManufacturer) or nameof(EnergyMeterManufacturer);

            var signingKeys     = new JArray();
            var identityKeys    = new JArray();
            String? homepage    = null;
            var index           = 2;

            // The keys, each after the word that says which kind it is, and the homepage of a manufacturer; the words after
            // them are the name.
            while (index + 1 < Words.Length)
            {

                if      (Words[index].Equals("signingKey",  StringComparison.OrdinalIgnoreCase))
                    signingKeys. Add(Words[index + 1]);

                else if (Words[index].Equals("identityKey", StringComparison.OrdinalIgnoreCase))
                    identityKeys.Add(Words[index + 1]);

                else if (Words[index].Equals("homepage",    StringComparison.OrdinalIgnoreCase) && manufacturer)
                    homepage = Words[index + 1];

                else
                    break;

                index += 2;

            }

            var name = String.Join(" ", Words[index..]).Trim();
            var json = new JObject(new JProperty("@id", Words[1]));

            // A party comes with its keys; a grid operator or a conformity assessment body not taking part may come without.
            var keysOptional = entityType is nameof(GridOperator) or nameof(ConformityAssessmentBody);

            if (signingKeys. Count > 0 || !keysOptional)
                json[POIPartyKeys.SigningKeysProperty]   = signingKeys;

            if (identityKeys.Count > 0 || !keysOptional)
                json[POIPartyKeys.IdentityKeysProperty]  = identityKeys;

            if (name.Length > 0)
                json["name"] = new JObject(new JProperty("en", name));

            if (homepage is not null)
                json["homepage"] = homepage;

            var result = cli.Hub.AddParty(RoamingNetworkId, entityType, json);

            // Waiting for the signatures of its identity keys, as the proof that it holds them.
            if (result.Status == RoamingNetworkPartyStatus.Pending)
            {

                cli.Hub.Log.Notice(
                    $"{cli.Caller.Asker} asked to add the {entityType} '{Words[1]}' to the roaming network '{RoamingNetworkId}', signed by this hub, '{cli.Hub.NetworkOperator.Id}': " +
                    $"the change set '{result.ChangeSet?.Id}' waits for the signatures of its identity keys.",
                    cli.Caller.Tags("poi", "security")
                );

                return [ result.Message, .. result.Missing ?? [] ];

            }

            if (!result.Success)
                return [ $"Nothing was added: {result.Message}" ];

            cli.Hub.Log.Notice(
                $"{cli.Caller.Asker} added the {entityType} '{Words[1]}' to the roaming network '{RoamingNetworkId}', signed by this hub, '{cli.Hub.NetworkOperator.Id}'.",
                cli.Caller.Tags("poi", "security")
            );

            return [ result.Message ];

        }

        #endregion

        #region (private static) PartyWord(EntityType)

        /// <summary>
        /// The word a party of the given entity type is added by: its type, spelled as a JSON property is.
        /// </summary>
        private static String PartyWord(String EntityType)

            => Char.ToLowerInvariant(EntityType[0]) + EntityType[1..];

        #endregion

        #region (private) OperatedBy(Network)

        private String OperatedBy(RoamingNetwork Network)

            => Network.Operator is null
                   ? "no roaming network operator"
                   : $"operated by '{Network.Operator.Id}'" + (cli.Hub.NetworkOperator.Operates(Network) ? ", this hub" : "");

        #endregion

    }

}
