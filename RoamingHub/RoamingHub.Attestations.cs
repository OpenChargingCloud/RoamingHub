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

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// What a driver's app checks of the roaming networks this hub operates (SPECIFICATION.md of WWCP POI, sections 8.4 to
    /// 8.6): the head of every one of them attested by the signing key of the roaming network operator this hub is, and
    /// that key stated by its identity key, so that an app pinning the identity key - from the QR code of
    /// ~/poi/RNs/{id}/trustAnchor - checks the proof of a charging station, ~/poi/RNs/{id}/CSs/{CS_Id}/proof, offline.
    /// </summary>
    /// <remarks>
    /// A roaming network made here names the key statement in its genesis; every head is attested when it changes, and
    /// again every <see cref="AttestationCheckEvery"/> once the attestation is older than its interval; a key statement
    /// valid for less than <see cref="KeyStatementRenewal"/>, or one naming other keys than the network, is stated anew
    /// by a commit. Replacing a key of the operator states its keys anew in the same change set.
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// How long a key statement made here is valid.
        /// </summary>
        public static readonly TimeSpan KeyStatementValidity    = TimeSpan.FromDays(30);

        /// <summary>
        /// How long a key statement must still be valid not to be stated anew.
        /// </summary>
        public static readonly TimeSpan KeyStatementRenewal     = TimeSpan.FromDays(7);

        /// <summary>
        /// How often the heads and the key statements are looked at.
        /// </summary>
        public static readonly TimeSpan AttestationCheckEvery   = TimeSpan.FromMinutes(1);

        private ITimer? attestationTimer;

        #endregion


        #region (private) OperatorStatingItsKeys(RoamingNetworkId, Now)

        /// <summary>
        /// The roaming network operator this hub is, as a roaming network made now names it: with the key statement of
        /// its identity keys in that network; the certificates of its signing keys it has already.
        /// </summary>
        private RoamingNetworkOperator OperatorStatingItsKeys(RoamingNetwork_Id  RoamingNetworkId,
                                                              DateTimeOffset     Now)
        {

            var networkOperator = NetworkOperator.Operator;

            if (NetworkOperator.StateKeys(RoamingNetworkId,
                                          networkOperator.IdentityKeys,
                                          Now,
                                          KeyStatementValidity) is not RoamingNetworkKeyStatement keyStatement)
            {
                return networkOperator;
            }

            var json = networkOperator.ToJSON(Embedded: true);

            json[RoamingNetworkKeyStatement.JSONProperty] = keyStatement.ToJSON(Embedded: true);

            return RoamingNetworkOperator.TryParse(json, out var stating, out var error)
                       ? stating
                       : throw new InvalidOperationException("The roaming network operator can not state its keys: " + error);

        }

        #endregion

        #region (private) Attest(History, Now)

        /// <summary>
        /// Let the signing key of the roaming network operator held here that the head of the given history names attest
        /// its heads from now on, and the current head now, unless it is attested recently.
        /// </summary>
        private void Attest(RoamingNetworkHistory  History,
                            DateTimeOffset         Now)
        {

            if (NetworkOperator.CommitSigner(History.Head.RoamingNetwork) is not RoamingNetworkCommitSigner signer)
                return;

            if (History.AttestationSigners.IsDefaultOrEmpty || History.AttestationSigners[0].KeyId != signer.KeyId)
                History.AttestationSigners = [ signer ];

            History.RefreshAttestations(Now);

        }

        #endregion

        #region (private) StateKeysAnew(History, Now)

        /// <summary>
        /// Commit a new key statement of the roaming network operator this hub is to the given history, where its head
        /// states none, one valid for less than <see cref="KeyStatementRenewal"/>, or one naming other identity keys than
        /// the network names. Null where nothing was to be done or could be.
        /// </summary>
        private RoamingNetworkCommitResult? StateKeysAnew(RoamingNetworkHistory  History,
                                                          DateTimeOffset         Now)
        {

            var network          = History.Head.RoamingNetwork;

            if (!NetworkOperator.Operates(network))
                return null;

            var networkOperator  = network.Operator!;
            var current          = networkOperator.KeyStatement;

            // With a quorum of several holders, its key statement needs the signatures of the others as well: not stated
            // anew here alone (SPECIFICATION.md of WWCP POI, section 4.1).
            if (networkOperator.MinConsensus > 1)
                return null;

            if (current is not null                                         &&
                current.ValidUntil - Now >= KeyStatementRenewal             &&
                current.IdentityKeys.SequenceEqual(networkOperator.IdentityKeys))
            {
                return null;
            }

            if (NetworkOperator.StateKeys(network.Id,
                                          networkOperator.IdentityKeys,
                                          Now,
                                          KeyStatementValidity) is not RoamingNetworkKeyStatement keyStatement)
            {
                return null;
            }

            var result = History.Commit(
                             NetworkOperator.Sign(
                                 network,
                                 network.CreateChangeSet(
                                     [ RoamingNetworkChange.UpdateProperty(networkOperator, RoamingNetworkKeyStatement.JSONProperty, null, keyStatement.ToJSON(Embedded: true)) ],
                                     Description: new ImmutableI18NString(I18NString.Create(Languages.en, $"The keys of the roaming network operator '{NetworkOperator.Id}' stated until {keyStatement.ValidUntil.ToISO8601()}.")),
                                     CreatedAt:   Now
                                 )
                             ),
                             At: Now
                         );

            if (result.IsCommitted)
                Log.Info ($"The roaming network '{network.Id}' states the keys of its operator until {keyStatement.ValidUntil.ToISO8601()}.", "poi", "security");
            else
                Log.Error($"The roaming network '{network.Id}' refused the key statement of its operator: {result.ErrorResponse}", "poi", "security");

            return result;

        }

        #endregion

        #region StartAttesting() / KeepAttesting(Now = null) / StopAttesting()

        /// <summary>
        /// Look at the heads and the key statements of the roaming networks this hub operates every
        /// <see cref="AttestationCheckEvery"/>.
        /// </summary>
        private void StartAttesting()
        {

            attestationTimer?.Dispose();

            attestationTimer = TimeProvider.CreateTimer(
                                   _ => KeepAttesting(),
                                   null,
                                   AttestationCheckEvery,
                                   AttestationCheckEvery
                               );

        }

        /// <summary>
        /// State the keys of the operator anew where they are about to end, and attest every head again whose newest
        /// attestation is older than its interval: what the timer does every <see cref="AttestationCheckEvery"/>.
        /// </summary>
        /// <param name="Now">The time, the one of the hub by default.</param>
        /// <returns>The roaming networks whose keys were stated anew.</returns>
        public IReadOnlyList<RoamingNetwork_Id> KeepAttesting(DateTimeOffset? Now = null)
        {

            var now     = Now ?? TimeProvider.GetUtcNow();
            var stated  = new List<RoamingNetwork_Id>();

            // Not while the keys of the operator are replaced: a statement made meanwhile would name the one replaced.
            lock (networkOperatorLock)
            {
                foreach (var history in HistoriesOfTheNetworkOperator())
                {
                    try
                    {
                        if (StateKeysAnew(history, now)?.IsCommitted == true)
                            stated.Add(history.RoamingNetworkId);

                        Attest(history, now);
                    }
                    catch (Exception e)
                    {
                        Log.Exception(e, $"The roaming network '{history.RoamingNetworkId}' could not be attested.", "poi", "security");
                    }
                }
            }

            return stated;

        }

        /// <summary>
        /// Stop looking at the heads.
        /// </summary>
        private void StopAttesting()
        {
            attestationTimer?.Dispose();
            attestationTimer = null;
        }

        #endregion

    }

}
