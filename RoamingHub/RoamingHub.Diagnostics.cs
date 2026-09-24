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

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.DNS;

#endregion

namespace cloud.charging.open.RoamingHub
{

    /// <summary>
    /// Making this RoamingHub try something, to find out whether it can:
    /// resolving a name, and asking its time server what time it is.
    /// </summary>
    /// <remarks>
    /// Both of these write to the event log as they go, and not only at the
    /// end. That is the point of them: somebody who cannot reach their time
    /// server wants to know which step failed - the name, the TLS handshake,
    /// the key exchange, the NTP packet - and a single line saying "it did not
    /// work" tells them to go and find out somewhere else.
    ///
    /// Because they are in the log, they are also in the live stream every open
    /// browser is hanging on. So the Logs page of somebody watching shows what
    /// somebody else pressed, which is the right way round for a machine that
    /// several people look after.
    /// </remarks>
    public partial class RoamingHub
    {

        #region Data

        /// <summary>
        /// The most record types one test query may ask for at a time.
        /// </summary>
        public const Int32  MaxQueryRecordTypes  = 8;

        /// <summary>
        /// The most answers one test query reports. A zone transfer is not a
        /// test, and a browser is not a place to render one.
        /// </summary>
        public const Int32  MaxQueryAnswers      = 200;

        #endregion


        #region ResolveAsync(Name, RecordTypes, CancellationToken = default)

        /// <summary>
        /// Look a name up, and say what came back.
        /// </summary>
        /// <param name="Name">The name to resolve.</param>
        /// <param name="RecordTypes">What to ask for; A and AAAA when nothing is said.</param>
        /// <param name="CancellationToken">A cancellation token.</param>
        public async Task<JObject> ResolveAsync(String                                Name,
                                                IEnumerable<DNSResourceRecordTypes>?  RecordTypes         = null,
                                                CancellationToken                     CancellationToken   = default)
        {

            var name         = Name?.Trim() ?? "";
            var recordTypes  = RecordTypes?.Distinct().ToArray() is { Length: > 0 } given
                                   ? given
                                   : [ DNSResourceRecordTypes.A, DNSResourceRecordTypes.AAAA ];

            var asked        = String.Join(", ", recordTypes);

            if (!DNSEnabled)
            {
                Log.Warning($"DNS test for '{name}' was not run: name resolution is switched off.", "dns", "test");
                return Failed(name, asked, "Name resolution is switched off on this RoamingHub.");
            }

            if (!DNSServiceName.TryParse(name, out var serviceName, out var problem))
            {
                Log.Warning($"DNS test: \"{name}\" is not a name that can be looked up: {problem}", "dns", "test");
                return Failed(name, asked, $"\"{name}\" is not a name that can be looked up: {problem}");
            }

            Log.Info(
                $"DNS test: asking {configuredDNSServers.Count} server(s) for {asked} of '{serviceName}' ...",
                "dns", "test"
            );

            var stopwatch = Stopwatch.StartNew();

            try
            {

                var answer   = await dnsClient.Query(
                                         serviceName,
                                         recordTypes,
                                         CancellationToken: CancellationToken
                                     );

                stopwatch.Stop();

                var records  = answer.Answers.Take(MaxQueryAnswers).ToArray();

                Log.Notice(
                    $"DNS test: '{serviceName}' {asked} -> {answer.ResponseCode} from {answer.Origin}, " +
                    $"{answer.Answers.Count()} answer(s) in {stopwatch.ElapsedMilliseconds} ms" +
                    (records.Length > 0
                         ? $": {String.Join("; ", records.Select(record => record.ToString()))}"
                         : "."),
                    "dns", "test"
                );

                return new JObject(

                           new JProperty("name",          serviceName.ToString()),
                           new JProperty("recordTypes",   new JArray(recordTypes.Select(recordType => recordType.ToString()))),
                           new JProperty("ok",            answer.ResponseCode == DNSResponseCodes.NoError),
                           new JProperty("responseCode",  answer.ResponseCode.ToString()),
                           new JProperty("server",        answer.Origin.ToString()),
                           new JProperty("runtime_ms",    stopwatch.ElapsedMilliseconds),
                           new JProperty("authoritative", answer.AuthoritativeAnswer),
                           new JProperty("truncated",     answer.IsTruncated),
                           new JProperty("dnssec",        answer.DNSSECStatus?.ToString()),
                           new JProperty("timedOut",      answer.IsTimeout),

                           new JProperty("answers",       new JArray(
                               records.Select(record => new JObject(
                                   new JProperty("name",         record.DomainName.ToString()),
                                   new JProperty("type",         record.Type.ToString()),
                                   new JProperty("timeToLive",   (Int64) record.TimeToLive.TotalSeconds),
                                   new JProperty("value",        record.RText ?? record.ToString())
                               ))
                           )),

                           new JProperty("more",          Math.Max(0, answer.Answers.Count() - records.Length))

                       );

            }
            catch (Exception e)
            {

                stopwatch.Stop();

                Log.Error($"DNS test for '{serviceName}' failed after {stopwatch.ElapsedMilliseconds} ms: {e.Message}", "dns", "test");

                return Failed(serviceName.ToString(), asked, e.Message);

            }


            static JObject Failed(String Name, String RecordTypes, String Error)

                => new (
                       new JProperty("name",         Name),
                       new JProperty("recordTypes",  new JArray(RecordTypes.Split(", "))),
                       new JProperty("ok",           false),
                       new JProperty("error",        Error),
                       new JProperty("answers",      new JArray())
                   );

        }

        #endregion

        #region TryParseRecordTypes(JSON, out RecordTypes, out Error)

        /// <summary>
        /// The record types of a test query, as the web interface names them.
        /// </summary>
        /// <remarks>
        /// By name or by number, because the several hundred that exist are not
        /// all in this enumeration and somebody testing a resolver may well want
        /// one that is not.
        /// </remarks>
        public static Boolean TryParseRecordTypes(JToken?                                                 JSON,
                                                  out IEnumerable<DNSResourceRecordTypes>?                RecordTypes,
                                                  [NotNullWhen(false)] out String?                        Error)
        {

            RecordTypes  = null;
            Error        = null;

            if (JSON is null || JSON.Type == JTokenType.Null)
                return true;

            if (JSON is not JArray array)
            {
                Error = "'recordTypes' must be an array.";
                return false;
            }

            if (array.Count > MaxQueryRecordTypes)
            {
                Error = $"One query may ask for at most {MaxQueryRecordTypes} record types.";
                return false;
            }

            var parsed = new List<DNSResourceRecordTypes>();

            foreach (var token in array)
            {

                var text = token.Value<String>()?.Trim() ?? "";

                if (text.Length == 0)
                    continue;

                if (Enum.TryParse<DNSResourceRecordTypes>(text, ignoreCase: true, out var recordType))
                    parsed.Add(recordType);

                else if (UInt16.TryParse(text, out var number))
                    parsed.Add((DNSResourceRecordTypes) number);

                else
                {
                    Error = $"'{text}' is not a DNS resource record type.";
                    return false;
                }

            }

            RecordTypes = parsed;
            return true;

        }

        #endregion


        #region SyncTimeAsync(CancellationToken = default)

        /// <summary>
        /// Ask the time server what time it is: the key exchange first, then
        /// one authenticated NTP request, with every step in the log.
        /// </summary>
        /// <remarks>
        /// The clock of this RoamingHub is not set from the answer, and
        /// that is deliberate: this says whether the time source can be reached
        /// and what it thinks of the local clock, which is what somebody
        /// pressing a button called "Sync now" in a web interface actually
        /// wants to know. Stepping the clock of a running RoamingHub is a
        /// different thing - everything below it reads the time from here - and
        /// it is not something a button does by surprise.
        /// </remarks>
        public async Task<JObject> SyncTimeAsync(CancellationToken CancellationToken = default)
        {

            if (!NTSEnabled)
            {
                Log.Warning("Time synchronisation was not run: NTS is switched off on this RoamingHub.", "nts", "test");
                return Failed("NTS is switched off on this RoamingHub.");
            }

            var client     = ntsClient;
            var stopwatch  = Stopwatch.StartNew();

            // The name as people read it, for every sentence below and for
            // what the page shows: without the root's dot. "ptbtime1.ptb.de."
            // is the name exactly, and in the middle of a line it reads like a
            // typing mistake. The configuration file keeps it.
            var name       = client.Hostname.Trimmed;

            Log.Info($"NTS: key exchange with {name}:{client.NTSKE_Port} ...", "nts", "ntske", "test");

            try
            {

                #region NTS-KE

                var keyExchange = await client.GetNTSKERecords(CancellationToken: CancellationToken);

                if (!keyExchange.Success || keyExchange.Response is null)
                {

                    Log.Error(
                        $"NTS: the key exchange with {name} failed after {stopwatch.ElapsedMilliseconds} ms " +
                        $"({keyExchange.ErrorCategory}): {keyExchange.ErrorMessage}",
                        "nts", "ntske", "test"
                    );

                    return Remember(Failed($"The key exchange failed: {keyExchange.ErrorMessage}",
                                           new JProperty("step",           "ntske"),
                                           new JProperty("errorCategory",  keyExchange.ErrorCategory.ToString())));

                }

                var response = keyExchange.Response;

                foreach (var warning in response.WarningMessages)
                    Log.Warning($"NTS: the key exchange with {name} warned: {warning}", "nts", "ntske", "test");

                Log.Info(
                    $"NTS: the key exchange with {name} succeeded in {stopwatch.ElapsedMilliseconds} ms - " +
                    $"{response.AEADAlgorithm}, {response.Cookies.Count()} cookie(s)" +
                    (response.NTPv4ServerNames.Any()
                         ? $", NTP server(s): {String.Join(", ", response.NTPv4ServerNames)}"
                         : "") + ".",
                    "nts", "ntske", "test"
                );

                // The cookies are what the NTP request below spends, so they go
                // into the pool before it is sent and not after.
                client.SeedCookies(response);

                #endregion

                #region NTP over NTS

                var afterKeyExchange = stopwatch.ElapsedMilliseconds;

                Log.Info($"NTS: authenticated NTP request to {name}:{client.NTP_Port} ...", "nts", "ntp", "test");

                var query = await client.QueryTime(CancellationToken: CancellationToken);

                stopwatch.Stop();

                if (!query.Success || query.Response is null)
                {

                    Log.Error(
                        $"NTS: the NTP request to {name} failed after {stopwatch.ElapsedMilliseconds} ms " +
                        $"({query.ErrorCategory}): {query.ErrorMessage}",
                        "nts", "ntp", "test"
                    );

                    return Remember(Failed($"The NTP request failed: {query.ErrorMessage}",
                                           new JProperty("step",           "ntp"),
                                           new JProperty("errorCategory",  query.ErrorCategory.ToString()),
                                           new JProperty("ntske",          new JObject(
                                               new JProperty("runtime_ms",     afterKeyExchange),
                                               new JProperty("aeadAlgorithm",  response.AEADAlgorithm.ToString()),
                                               new JProperty("cookies",        response.Cookies.Count())
                                           ))));

                }

                #endregion

                var roundTrip = query.StopwatchRoundTripTime;

                // What the exchange was actually for. The clock of this RoamingHub
                // is not stepped by it - see the remarks on this method - so
                // the offset is the whole of the result: it is the difference
                // between what this RoamingHub believes and what a server that
                // knows was saying at the same moment.
                var offset    = query.Response?.ClockOffset;

                lastTimeCheck        = TimeProvider.GetUtcNow();
                lastTimeCheckOffset  = offset;
                lastTimeCheckServer  = name;

                Log.Notice(
                    AnsweredLine(client.Hostname, stopwatch.ElapsedMilliseconds, offset, roundTrip, query.RemainingCookiesAfterQuery),
                    "nts", "ntp", "test"
                );

                return Remember(new JObject(

                           new JProperty("ok",             true),
                           new JProperty("server",         name),
                           new JProperty("remote",         query.RemoteDescription),
                           new JProperty("at",             TimeProvider.GetUtcNow().ToString("o")),
                           new JProperty("runtime_ms",     stopwatch.ElapsedMilliseconds),

                           new JProperty("ntske",          new JObject(
                               new JProperty("runtime_ms",         afterKeyExchange),
                               new JProperty("aeadAlgorithm",      response.AEADAlgorithm.ToString()),
                               new JProperty("cookies",            response.Cookies.Count()),
                               new JProperty("ntpServers",         new JArray(response.NTPv4ServerNames)),
                               new JProperty("warnings",           new JArray(response.WarningMessages))
                           )),

                           new JProperty("offset_ms",      offset?.TotalMilliseconds),

                           new JProperty("ntp",            new JObject(
                               new JProperty("attempts",           query.Attempts),
                               new JProperty("roundTrip_ms",       roundTrip?.TotalMilliseconds),
                               new JProperty("newCookieReceived",  query.NewCookieReceived),
                               new JProperty("cookiesLeft",        query.RemainingCookiesAfterQuery),
                               new JProperty("kissOfDeath",        query.KissOfDeath?.ToString())
                           ))

                       ));

            }
            catch (Exception e)
            {

                stopwatch.Stop();

                Log.Error($"NTS: the exchange with {name} failed after {stopwatch.ElapsedMilliseconds} ms: {e.Message}", "nts", "test");

                return Remember(Failed(e.Message));

            }


            JObject Failed(String Error, params JProperty[] More)
            {

                var json = new JObject(
                               new JProperty("ok",      false),
                               new JProperty("server",  ntsClient.Hostname.Trimmed),
                               new JProperty("at",      TimeProvider.GetUtcNow().ToString("o")),
                               new JProperty("error",   Error)
                           );

                foreach (var property in More)
                    json.Add(property);

                return json;

            }

            JObject Remember(JObject Result)
            {
                lastTimeSync = Result;
                return Result;
            }

        }

        #endregion


        #region (static) AnsweredLine(Hostname, Milliseconds, Offset, RoundTrip, CookiesLeft)

        /// <summary>
        /// The line a synchronisation that produced a time is logged with.
        /// </summary>
        /// <remarks>
        /// Invariant, so that a decimal point stays a point: this sentence is
        /// English, and under a German culture it read "clock is +702,4 ms off
        /// (round trip 12,3 ms)" - in a log whose numbers then changed their
        /// punctuation with the machine that wrote them. And the server by the
        /// name it is read by, without the root's dot, which in the middle of a
        /// sentence reads as a typing mistake.
        /// </remarks>
        /// <param name="Hostname">The time server that was asked.</param>
        /// <param name="Milliseconds">How long asking it took.</param>
        /// <param name="Offset">How far this hub's clock is from what the server said, when it said something to take that from.</param>
        /// <param name="RoundTrip">How long the NTP request took on the wire.</param>
        /// <param name="CookiesLeft">How many cookies the next requests can still spend.</param>
        public static String AnsweredLine(DomainName  Hostname,
                                          Int64       Milliseconds,
                                          TimeSpan?   Offset,
                                          TimeSpan?   RoundTrip,
                                          Int32       CookiesLeft)

            => String.Concat(
                   String.Create(CultureInfo.InvariantCulture, $"NTS: {Hostname.Trimmed} answered in {Milliseconds} ms"),
                   Offset.   HasValue ? String.Create(CultureInfo.InvariantCulture, $", this RoamingHub's clock is {Offset.Value.TotalMilliseconds:+0.0;-0.0;0} ms off") : "",
                   RoundTrip.HasValue ? String.Create(CultureInfo.InvariantCulture, $" (round trip {RoundTrip.Value.TotalMilliseconds:F1} ms)")                    : "",
                   String.Create(CultureInfo.InvariantCulture, $", {CookiesLeft} cookie(s) left.")
               );

        #endregion

    }

}
