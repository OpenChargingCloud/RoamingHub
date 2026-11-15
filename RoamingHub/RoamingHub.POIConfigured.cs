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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;

using cloud.charging.open.RoamingHub.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What the "poi" section of the configuration has this hub take part in (SHAREHOLDERS.md, phases 6.3, 6.4): where it
    /// is reached, which roaming networks of other servers it follows, and of which it asks to hold the keys of the
    /// operator as well.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read at every start, and what it says is done as if somebody had said it then. What it names no longer is not
    /// undone: following no longer deletes a copy, which somebody says. The commands and the pages stay, for what
    /// is said once.
    /// </para>
    /// <para>
    /// "url": where the POI API of this hub is reached. No hub knows its own public URL, so without it the trust
    /// anchor of a roaming network names the URL it was asked at, and a roaming network names no copies. With it, a
    /// roaming network made here names it from its genesis on, every trust anchor served here names it first, and
    /// this hub says it as the URL of its copy when it asks to hold the keys of an operator, over https.
    /// </para>
    /// <para>
    /// "follow": a roaming network followed already goes on from the trust anchor it has got to. "hold" asks once,
    /// which is kept beside the copy: a hub dismissed is not asking again by itself.
    /// "signCommits" says which commits of it this hub signs by itself as one of the holders of the keys of its
    /// operator: see SignCommitsByItself in RoamingHub.ShareholdersAdvance.cs.
    /// </para>
    /// </remarks>
    public partial class RoamingHub
    {

        #region (private) CopyURLOf(RoamingNetworkId)

        /// <summary>
        /// Where this hub serves its copy of the given roaming network, as the configuration says, to be named by
        /// the roaming network: over https only, as everybody following it would be sent there.
        /// </summary>
        private URL? CopyURLOf(RoamingNetwork_Id RoamingNetworkId)

            => poiConfiguration.URLOf(RoamingNetworkId) is URL url &&
               url.ToString().StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                   ? url
                   : null;

        #endregion

        #region FollowAsConfigured()

        /// <summary>
        /// Follow the roaming networks the configuration names that are not followed yet. After the start, in the
        /// background: another server may be away, and this hub starts all the same.
        /// </summary>
        /// <returns>What became of each, said.</returns>
        public async Task<IReadOnlyList<String>> FollowAsConfigured()
        {

            var said = new List<String>();

            foreach (var follow in poiConfiguration.EffectiveFollow)
            {

                var id = follow.TrustAnchor.Id;

                try
                {

                    if (IsFollowed(id))
                        continue;

                    if (POI.TryGetRoamingNetworkHistory(id, out _))
                    {
                        said.Add($"The configuration has this hub follow the roaming network '{id}', which it makes itself: it is not followed.");
                        Log.Error(said[^1], "poi", "follow", "config");
                        continue;
                    }

                    var following = await FollowRoamingNetwork(follow.TrustAnchorText, follow.URL);

                    said.Add("As the configuration says: " + following.Message);

                    if (following.Status is RoamingNetworkFollowingStatus.Synchronized or RoamingNetworkFollowingStatus.Following)
                        Log.Notice(said[^1], "poi", "follow", "config");
                    else
                        Log.Error (said[^1], "poi", "follow", "config");

                }
                catch (Exception e)
                {
                    Log.Exception(e, $"The roaming network '{id}' could not be followed as the configuration says.", "poi", "follow", "config");
                }

            }

            return said;

        }

        #endregion

        #region (private) HoldAsConfigured(Network, Changes)

        /// <summary>
        /// Ask to hold the keys of the operator of the given roaming network followed, where the configuration says
        /// so and this hub did not ask yet: the decision of somebody, written down before. Once, kept beside the
        /// copy. Called when this hub looks, where it holds no key there.
        /// </summary>
        /// <returns>What was done, said; nothing otherwise.</returns>
        private async Task<String?> HoldAsConfigured(FollowedRoamingNetwork            Network,
                                                     IReadOnlyList<ShareholderChange>  Changes)
        {

            if (Network.AskedToHold ||
                !poiConfiguration.EffectiveFollow.Any(follow => follow.Hold && follow.TrustAnchor.Id == Network.Id))
            {
                return null;
            }

            void Asked()
            {
                Network.AskedToHold = true;
                WriteFollowed(Network);
            }

            // Asked already, by somebody.
            if (Changes.Any(change => change.Kind == ShareholderChangeKind.Admit && change.Holder == NetworkOperator.Holder))
            {
                Asked();
                return null;
            }

            var result = await Join(Network.Id, null, ByItself: false);

            if (result.Status == ShareholderChangeStatus.Waiting)
            {

                Asked();

                var said = "As the configuration says: " + result.Message;

                Log.Notice(said, "poi", "shareholders", "security", "config");

                return said;

            }

            // Not asked: tried again when this hub looks next, and said once.
            Boolean saidBefore;

            lock (undecidedSaid)
                saidBefore = !undecidedSaid.Add("hold " + Network.Id + " " + result.Message);

            if (!saidBefore)
                Log.Warning($"The configuration has this hub ask to hold the keys of the operator of the roaming network '{Network.Id}', which it could not: {result.Message}", "poi", "shareholders", "config");

            return null;

        }

        #endregion

    }

}
