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

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.RoamingHub.CommandLine
{

    /// <summary>
    /// The roaming network operator this hub is: who it is, the public halves of its keys, where its private keys
    /// are kept, and the roaming networks it operates; a key of it replaced, and its name changed, in all of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// roamingNetworkOperator shows it: public, as every roaming network it operates names all of it; the private
    /// keys are never shown. roamingNetworkOperator rotate signingKey | identityKey replaces that key by a new one in
    /// every roaming network it operates, each change signed by the key replaced; the old key is forgotten once no
    /// roaming network names it any more. roamingNetworkOperator name &lt;name&gt; gives it a new name, in English,
    /// in every roaming network it operates.
    /// </para>
    /// <para>
    /// Replacing a key and renaming it sign with the keys of this hub, and are poi:edit, as on the JSON API.
    /// </para>
    /// </remarks>
    /// <param name="CLI">The command line.</param>
    public class RoamingNetworkOperatorCommand(HubCLI CLI) : ACLICommand<HubCLI>(CLI),
                                                             ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public const String CommandName = "roamingNetworkOperator";

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
                return Task.FromResult(Shown());

            if (rest[0].Equals("rotate", StringComparison.OrdinalIgnoreCase) && rest.Length == 2)
            {

                if (rest[1].Equals("signingKey",  StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Rotated(POIKeyKind.Signing));

                if (rest[1].Equals("identityKey", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(Rotated(POIKeyKind.Identity));

            }

            if (rest[0].Equals("name", StringComparison.OrdinalIgnoreCase) && rest.Length > 1)
                return Task.FromResult(Renamed(String.Join(" ", rest[1..]).Trim()));

            return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [rotate signingKey|identityKey | name <name>] - the roaming network operator this hub is, its public keys and the roaming " +
                "networks it operates; a key of it replaced by a new one, or its name changed, in all of them";

        #endregion


        #region (private) Shown()

        private String[] Shown()
        {

            var networkOperator  = cli.Hub.NetworkOperator;
            var name             = networkOperator.Operator.Name.FirstText();
            var operated         = cli.Hub.POI.RoamingNetworks.Where(networkOperator.Operates).
                                                               Select(network => network.Id.ToString()).
                                                               Order(StringComparer.OrdinalIgnoreCase).
                                                               ToArray();
            var kept             = networkOperator.KeptKeyIds;

            return [
                $"This hub is the roaming network operator '{networkOperator.Id}'{(name.Length > 0 ? $", {name}" : "")}, since {networkOperator.Operator.Created.ToISO8601()}.",
                $"  signing key   {networkOperator.SigningKey. Id}  {networkOperator.SigningKey. ToJSON()["key"]}",
                $"  identity key  {networkOperator.IdentityKey.Id}  {networkOperator.IdentityKey.ToJSON()["key"]}",
                .. kept.Count > 0
                       ? new[] { $"  older keys kept for the roaming networks that still name them: {String.Join(", ", kept.Select(keyId => keyId[..16] + "…"))}" }
                       : [],
                $"  its private keys are kept in '{networkOperator.Directory}'",
                operated.Length > 0
                    ? $"  it operates {String.Join(", ", operated)}"
                    : $"  it operates no roaming network yet: '{RoamingNetworksCommand.CommandName} add <id> [<name>]' makes one"
            ];

        }

        #endregion

        #region (private) Rotated(Kind)

        private String[] Rotated(POIKeyKind Kind)
        {

            var kind = Kind == POIKeyKind.Signing ? "signingKey" : "identityKey";

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} rotate {kind}", out var refused))
                return [ refused ];

            var result = cli.Hub.RotateNetworkOperatorKey(Kind);

            cli.Hub.Log.Notice($"{cli.Caller.Asker} replaced the {(Kind == POIKeyKind.Signing ? "signing" : "identity")} key of the roaming network operator '{cli.Hub.NetworkOperator.Id}'.",
                               cli.Caller.Tags("poi", "security"));

            return [ result.Message ];

        }

        #endregion

        #region (private) Renamed(Name)

        private String[] Renamed(String Name)
        {

            if (!cli.MayDo(Permission.Edit(HubAccess.POI), $"{CommandName} name", out var refused))
                return [ refused ];

            if (Name.Length == 0)
                return [ $"Usage: {Help()}" ];

            var result = cli.Hub.RenameNetworkOperator(I18NString.Create(Languages.en, Name));

            cli.Hub.Log.Notice($"{cli.Caller.Asker} renamed the roaming network operator '{cli.Hub.NetworkOperator.Id}' to '{Name}'.",
                               cli.Caller.Tags("poi"));

            return [ result.Message ];

        }

        #endregion

    }

}
