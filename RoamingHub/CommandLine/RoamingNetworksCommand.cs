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

            if (!RoamingNetwork_Id.TryParse(rest[0], out var roamingNetworkId))
                return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

            if (rest.Length == 1)
                return Task.FromResult(Shown(roamingNetworkId));

            if (rest[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(AddParty(roamingNetworkId, rest[2..]));

            return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [add <id> [<name>] | <id> [add chargingStationOperator|eMobilityProvider|gridOperator|parkingOperator|" +
                "chargingStationManufacturer|energyMeterManufacturer|transparencySoftwareManufacturer|conformityAssessmentBody <id> " +
                "signingKey <key> identityKey <key> [signingKey <key> | identityKey <key> ...] [homepage <url>] [<name>]]] - the roaming networks of the points of interest, " +
                "a new one operated by this hub, one of them, and a new party of one";

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

            return lines.Count > 0
                       ? [ .. lines ]
                       : [ $"This hub serves no roaming network yet: '{CommandName} add <id> [<name>]' makes one, operated by this hub, '{cli.Hub.NetworkOperator.Id}'." ];

        }

        #endregion

        #region (private) Shown(RoamingNetworkId)

        private String[] Shown(RoamingNetwork_Id RoamingNetworkId)
        {

            if (!cli.Hub.POI.TryGetRoamingNetworkHistory(RoamingNetworkId, out var history))
                return [ $"This hub serves no roaming network '{RoamingNetworkId}'." ];

            var network  = history.Head.RoamingNetwork;
            var lines    = new List<String> {
                               $"{network.Id}  revision {history.Head.Commit.Revision}, commit {history.Head.Commit.Id}  {network.Name.FirstText()}".TrimEnd(),
                               $"  {OperatedBy(network)}"
                           };

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
