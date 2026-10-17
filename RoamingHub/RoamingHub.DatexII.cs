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

using System.Security.Cryptography.X509Certificates;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.DatexII.v3.Common;
using cloud.charging.open.protocols.DatexII.v3.POI3;
using cloud.charging.open.protocols.DatexII.v3.WebAPI;
using cloud.charging.open.RoamingHub.Configuration;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// The roaming networks this hub publishes as DATEX II 3.7 AFIR Energy Infrastructure publications: served below
    /// "/datex2" to be pulled, and pushed to the Mobilithek, the German national access point, as the "datex2" section
    /// of the configuration file says.
    /// </summary>
    public partial class RoamingHub
    {

        #region (private class) DatexIIPublication

        /// <summary>
        /// One roaming network published: its publisher, its pull endpoints, and its push to the Mobilithek.
        /// </summary>
        private sealed class DatexIIPublication(RoamingNetwork_Id       RoamingNetworkId,
                                                POI3ToDatexIIPublisher  Publisher,
                                                DatexIIHTTPAPI?         API,
                                                MobilithekPushClient?   PushClient)
        {
            public RoamingNetwork_Id       RoamingNetworkId  { get; } = RoamingNetworkId;
            public POI3ToDatexIIPublisher  Publisher         { get; } = Publisher;
            public DatexIIHTTPAPI?         API               { get; } = API;
            public MobilithekPushClient?   PushClient        { get; } = PushClient;
        }

        #endregion

        #region Data

        /// <summary>
        /// Where the DATEX II publications are served, below the base path.
        /// </summary>
        public const String  DatexIIPathSegment  = "datex2";

        private          DatexIIConfiguration?                                datexIIConfiguration;
        private readonly Dictionary<RoamingNetwork_Id, DatexIIPublication>    datexIIPublications   = [];
        private readonly Lock                                                 datexIILock           = new ();
        private          Boolean                                              datexIIStarted;

        #endregion


        #region (private) BuildDatexII(Configuration)

        /// <summary>
        /// A publisher for every roaming network the "datex2" section names and this hub serves; one it does not serve
        /// yet is published once it is made.
        /// </summary>
        private void BuildDatexII(DatexIIConfiguration? Configuration)
        {

            datexIIConfiguration = Configuration;

            foreach (var publication in Configuration?.Publications ?? [])
            {

                if (!RoamingNetwork_Id.TryParse(publication.RoamingNetworkId, out var roamingNetworkId))
                {
                    Log.Error($"DATEX II: '{publication.RoamingNetworkId}' is no roaming network identification.", "datex2");
                    continue;
                }

                if (POI.TryGetRoamingNetworkHistory(roamingNetworkId, out var history))
                    AddDatexIIPublication(publication, history);

                else
                    Log.Warning($"DATEX II: the roaming network '{roamingNetworkId}' is not served (yet); it is published once it is made.", "datex2");

            }

        }

        #endregion

        #region (private) PublishAsDatexII(History)

        /// <summary>
        /// Publish a roaming network made at runtime, when the "datex2" section names it.
        /// </summary>
        private void PublishAsDatexII(RoamingNetworkHistory History)
        {

            var configuration = datexIIConfiguration?.Publications?.FirstOrDefault(publication => RoamingNetwork_Id.TryParse(publication.RoamingNetworkId, out var id) &&
                                                                                                   id == History.RoamingNetworkId);

            if (configuration is null)
                return;

            var publication = AddDatexIIPublication(configuration, History);

            if (publication is not null && datexIIStarted)
                _ = StartDatexIIPublicationAsync(publication);

        }

        #endregion

        #region (private) AddDatexIIPublication(Configuration, History)

        /// <summary>
        /// The publisher of the given roaming network, its pull endpoints when they are on, and its push to the
        /// Mobilithek when the configuration has one; null, with the reason in the log, when one of them can not be made.
        /// </summary>
        private DatexIIPublication? AddDatexIIPublication(DatexIIPublicationConfiguration  Configuration,
                                                          RoamingNetworkHistory            History)
        {

            var roamingNetworkId = History.RoamingNetworkId;

            lock (datexIILock)
                if (datexIIPublications.ContainsKey(roamingNetworkId))
                    return null;

            try
            {

                #region Who publishes, what, and how often

                if (!Country.TryParseAlpha2Code(Configuration.Country, out var country))
                    throw new ArgumentException($"'{Configuration.Country}' is no country.");

                var operatorIds = new List<ChargingStationOperator_Id>();

                foreach (var operatorId in Configuration.OperatorIds ?? [])
                    operatorIds.Add(ChargingStationOperator_Id.TryParse(operatorId) ?? throw new ArgumentException($"'{operatorId}' is no charging station operator identification."));

                var mobilithek  = Configuration.Mobilithek;

                var publisher   = new POI3ToDatexIIPublisher(
                                      History,
                                      new InternationalIdentifier(country, Configuration.NationalIdentifier),
                                      Configuration.Language is not null
                                          ? Enum.Parse<Languages>(Configuration.Language, ignoreCase: true)
                                          : null,
                                      OperatorIds:                operatorIds.Count > 0 ? operatorIds : null,
                                      EnergyStatusDeltaInterval:  Configuration.EnergyStatusDeltaInterval,
                                      // What the Mobilithek profile wants, when the publications go there.
                                      StatusDeltaInterval:        Configuration.StatusDeltaInterval  ?? (mobilithek is not null ? POI3ToDatexIIPublisher.MobilithekStatusDeltaInterval  : null),
                                      SnapshotInterval:           Configuration.SnapshotInterval     ?? (mobilithek is not null ? POI3ToDatexIIPublisher.MobilithekSnapshotInterval     : null),
                                      MaxDeltasPerSnapshot:       Configuration.MaxDeltasPerSnapshot ?? (mobilithek is not null ? POI3ToDatexIIPublisher.MobilithekMaxDeltasPerSnapshot : null)
                                  );

                publisher.OnError += (sender, description, warnings) =>
                    Log.Warning($"DATEX II '{roamingNetworkId}': {description}{(warnings.Any() ? " " + String.Join(" ", warnings) : "")}", "datex2");

                #endregion

                #region To be pulled

                var api         = datexIIConfiguration?.EffectivePull != false
                                      ? new DatexIIHTTPAPI(HTTPServer,
                                                           () => publisher.CurrentTablePublication,
                                                           () => publisher.CurrentStatusPublication,
                                                           BasePath + DatexIIPathSegment + "/RNs/" + roamingNetworkId.ToString())
                                      : null;

                #endregion

                #region Pushed to the Mobilithek

                MobilithekPushClient? pushClient = null;

                if (mobilithek is not null)
                {

                    pushClient = new MobilithekPushClient(mobilithek.StaticPublicationId,
                                                          mobilithek.DynamicPublicationId,
                                                          MachineCertificateOf (mobilithek),
                                                          TrustedCertificatesOf(mobilithek),
                                                          mobilithek.URL is not null ? URL.Parse(mobilithek.URL) : null,
                                                          mobilithek.Compress ?? true);

                    pushClient.OnPush += (sender, publicationId, publication, result) => {
                        if (!result.IsSuccess)
                            Log.Error($"DATEX II '{roamingNetworkId}': the push to the Mobilithek publication {publicationId} failed" +
                                      $"{(result.StatusCode.HasValue ? $" ({result.StatusCode})" : "")}: {result.Description}", "datex2", "mobilithek");
                    };

                    // Before the publisher starts, to push its first table and full status as well.
                    pushClient.Attach(publisher);

                }

                #endregion

                var datexIIPublication = new DatexIIPublication(roamingNetworkId, publisher, api, pushClient);

                lock (datexIILock)
                    datexIIPublications[roamingNetworkId] = datexIIPublication;

                Log.Info($"DATEX II: the roaming network '{roamingNetworkId}' is published" +
                         $"{(api is not null ? $" below {api.RootPath}" : "")}" +
                         $"{(mobilithek is not null ? $", and pushed to the {mobilithek}" : "")}.", "datex2");

                return datexIIPublication;

            }
            catch (Exception e)
            {
                Log.Exception(e, $"DATEX II: the roaming network '{roamingNetworkId}' can not be published.", "datex2");
                return null;
            }

        }

        #endregion

        #region (private) MachineCertificateOf(Mobilithek) / TrustedCertificatesOf(Mobilithek)

        /// <summary>
        /// The machine certificate of the configuration, with its private key; its password from the file or, better,
        /// from an environment variable.
        /// </summary>
        private X509Certificate2? MachineCertificateOf(MobilithekConfiguration Mobilithek)
        {

            if (Mobilithek.ClientCertificate is null)
            {
                Log.Warning("DATEX II: the Mobilithek wants the machine certificate it issued, and the configuration names none.", "datex2", "mobilithek");
                return null;
            }

            var password = Mobilithek.ClientCertificatePasswordEnvironmentVariable is not null
                               ? Environment.GetEnvironmentVariable(Mobilithek.ClientCertificatePasswordEnvironmentVariable)
                               : Mobilithek.ClientCertificatePassword;

            return X509CertificateLoader.LoadPkcs12FromFile(ConfigurationPath(Mobilithek.ClientCertificate), password);

        }

        /// <summary>
        /// The certificate chain of the Mobilithek of the configuration, if it names one.
        /// </summary>
        private IEnumerable<X509Certificate2>? TrustedCertificatesOf(MobilithekConfiguration Mobilithek)
        {

            if (Mobilithek.TrustedCertificates is null)
                return null;

            var certificates = new X509Certificate2Collection();
            certificates.ImportFromPemFile(ConfigurationPath(Mobilithek.TrustedCertificates));

            return certificates;

        }

        /// <summary>
        /// The given path, relative to the configuration file unless it is absolute.
        /// </summary>
        private String ConfigurationPath(String FilePath)

            => Path.Combine(Path.GetDirectoryName(ConfigFile.Path) ?? ".", FilePath);

        #endregion


        #region DatexIIBannerLines()

        /// <summary>
        /// What the banner tells of the DATEX II publications: one line for every roaming network the "datex2" section
        /// names, with where it is to be pulled and where it is pushed to.
        /// </summary>
        public IEnumerable<(String Label, String Value)> DatexIIBannerLines()
        {

            foreach (var configuration in datexIIConfiguration?.Publications ?? [])
            {

                if (!RoamingNetwork_Id.TryParse(configuration.RoamingNetworkId, out var roamingNetworkId))
                {
                    yield return ("DATEX II", $"{configuration.RoamingNetworkId}: no roaming network identification");
                    continue;
                }

                DatexIIPublication? publication;

                lock (datexIILock)
                    datexIIPublications.TryGetValue(roamingNetworkId, out publication);

                if (publication is null)
                {
                    yield return ("DATEX II", POI.TryGetRoamingNetworkHistory(roamingNetworkId, out _)
                                                  ? $"{roamingNetworkId}: not published, see the log"
                                                  : $"{roamingNetworkId}: not served yet, published once it is made");
                    continue;
                }

                var where = new List<String>();

                if (publication.API is not null)
                    where.Add($"{WebInterfaceURL.ToString().TrimEnd('/')}/{DatexIIPathSegment}/RNs/{roamingNetworkId}/");

                if (publication.PushClient is not null)
                    where.Add($"pushed to the {configuration.Mobilithek}");

                yield return ("DATEX II", $"{roamingNetworkId}: {(where.Count > 0 ? String.Join(", ", where) : "neither served nor pushed")}");

            }

        }

        #endregion


        #region (private) StartDatexIIAsync()

        /// <summary>
        /// Publish the head of every published roaming network, and every change from now on.
        /// </summary>
        private async Task StartDatexIIAsync()
        {

            DatexIIPublication[] publications;

            lock (datexIILock)
            {
                datexIIStarted  = true;
                publications    = [ .. datexIIPublications.Values ];
            }

            foreach (var publication in publications)
                await StartDatexIIPublicationAsync(publication);

        }

        private async Task StartDatexIIPublicationAsync(DatexIIPublication Publication)
        {
            try
            {

                await Publication.Publisher.StartAsync();

                if (Publication.API is not null)
                    Log.Info($"DATEX II '{Publication.RoamingNetworkId}' is at {WebInterfaceURL.ToString().TrimEnd('/')}/{DatexIIPathSegment}/RNs/{Publication.RoamingNetworkId}/" +
                             "table.xml, status.xml, table.json, status.json, mobilithek/table.json and mobilithek/status.json.", "datex2");

            }
            catch (Exception e)
            {
                Log.Exception(e, $"DATEX II: the roaming network '{Publication.RoamingNetworkId}' could not be published.", "datex2");
            }
        }

        #endregion

        #region (private) DisposeDatexIIAsync()

        /// <summary>
        /// Stop publishing: the pushes first, after what they are still pushing, then the publishers.
        /// </summary>
        private async ValueTask DisposeDatexIIAsync()
        {

            DatexIIPublication[] publications;

            lock (datexIILock)
            {
                publications = [ .. datexIIPublications.Values ];
                datexIIPublications.Clear();
            }

            foreach (var publication in publications)
            {
                try
                {

                    if (publication.PushClient is not null)
                        await publication.PushClient.DisposeAsync();

                    await publication.Publisher.DisposeAsync();

                }
                catch (Exception e)
                {
                    Log.Exception(e, $"DATEX II: the publication of the roaming network '{publication.RoamingNetworkId}' could not be stopped cleanly.", "datex2");
                }
            }

        }

        #endregion

    }

}
