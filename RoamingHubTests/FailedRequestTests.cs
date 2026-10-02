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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.OCPI;
using cloud.charging.open.protocols.WWCP.Node.Logging;

using cloud.charging.open.RoamingHub.OCPI;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// A request to this hub's OCPI side whose handling throws: its caller is
    /// told nothing but the ids to quote, and what was thrown is in the log,
    /// with the request's line and those ids - and not its token.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The OCPI library answered such a request with what was thrown - its
    /// message, and its stack trace with the paths this hub was built at - to
    /// whoever asked, at the versions list without a token as well. Since
    /// WWCP_OCPI be3fafe1 it answers OCPI 3000 and the ids only, and says the
    /// rest to OnRequestFailed: where nobody listens, a Release build says it
    /// nowhere.
    /// </para>
    /// <para>
    /// A peer whose TOTP secret is too short to make a password with is what
    /// makes reading a request throw, as in the library's own tests: the
    /// versions list cannot read a request that carries its token.
    /// </para>
    /// </remarks>
    public class FailedRequestTests : ARoamingHubTests
    {

        #region Data

        private const String BrokenToken    = "a-token-with-a-broken-totp";
        private const String RequestId      = "a-request-id-of-the-caller";
        private const String CorrelationId  = "a-correlation-id-of-the-caller";

        #endregion


        #region AFailedRequestTellsItsCallerNoMoreThanItsIds()

        /// <summary>
        /// The caller is answered OCPI 3000 with HTTP 500, the message for it
        /// and the ids it came with - and nothing of what was thrown.
        /// </summary>
        [Test]
        public async Task AFailedRequestTellsItsCallerNoMoreThanItsIds()
        {

            await AddAPeerWithABrokenTOTP();

            var answer  = await TheVersionsList();
            var json    = answer.Text.StartsWith('{') ? JObject.Parse(answer.Text) : null;

            Assert.Multiple(() => {

                Assert.That(answer.Status,                         Is.EqualTo(500),                                $"The failed request is not answered 500: {answer.Text}");
                Assert.That(json?.Value<Int32?>("status_code"),    Is.EqualTo(3000),                               $"The failed request is not answered OCPI 3000: {answer.Text}");
                Assert.That(json?.Value<String>("status_message"), Is.EqualTo(CommonHTTPAPI.FailedRequestMessage), "The failed request is not answered with the message for it.");
                Assert.That(json?.Value<String>("requestId"),      Is.EqualTo(RequestId),                          "The failed request does not say the request id it came with.");
                Assert.That(answer.RequestIdHeader,                Is.EqualTo(RequestId),                          "The failed request is not answered with the request id it came with.");

                Assert.That(answer.Text, Does.Not.Contain("stacktrace").IgnoreCase,  "The caller is told a stack trace.");
                Assert.That(answer.Text, Does.Not.Contain(".cs"),                    "The caller is told a source file.");
                Assert.That(answer.Text, Does.Not.Contain("Exception"),              "The caller is told what was thrown.");

            });

        }

        #endregion

        #region AFailedRequestIsInTheLogWithItsIdsAndNotItsToken()

        /// <summary>
        /// What the caller is not told is an error in the log: the request's
        /// line and the ids the caller can quote, and not its token.
        /// </summary>
        [Test]
        public async Task AFailedRequestIsInTheLogWithItsIdsAndNotItsToken()
        {

            await AddAPeerWithABrokenTOTP();

            await TheVersionsList();

            var said = RoamingHub.Log.Recent(100, Tag: "http").
                                      Where (entry => entry.Message.Contains(RequestId)).
                                      ToArray();

            Assert.That(said, Has.Length.EqualTo(1), "The failed request is not in the log, or more than once.");

            Assert.Multiple(() => {
                Assert.That(said[0].Level,    Is.EqualTo(LogLevel.Error));
                Assert.That(said[0].Tags,     Does.Contain("ocpi"));
                Assert.That(said[0].Message,  Does.Contain("GET /ext/versions"),   "The log does not say which request failed.");
                Assert.That(said[0].Message,  Does.Contain(CorrelationId),         "The log does not say the correlation id the caller was answered with.");
                Assert.That(said[0].Message,  Does.Contain("shared secret"),       "The log does not say why the request failed.");
                Assert.That(said[0].Message,  Does.Not.Contain(BrokenToken),       "The log holds the token of the request.");
            });

        }

        #endregion


        #region (private) AddAPeerWithABrokenTOTP()

        /// <summary>
        /// A peer whose TOTP secret is too short to make a password with:
        /// reading a request with its token throws.
        /// </summary>
        private async Task AddAPeerWithABrokenTOTP()
        {

            var added = await RoamingHub.OCPIVersions.OfType<OCPIv2_2_1>().Single().CommonAPI.AddRemoteParty(
                                  RemoteParty_Id.Parse("DE-CCC_EMSP"),
                                  [
                                      new CredentialsRole(
                                          CountryCode.Parse("DE"),
                                          Party_Id.   Parse("CCC"),
                                          protocols.OCPI.Role.EMSP,
                                          new BusinessDetails("An EMSP with a broken TOTP")
                                      )
                                  ],
                                  AccessToken.Parse(BrokenToken),
                                  LocalAccessTokenBase64Encoding:  false,
                                  LocalTOTPConfig:                 new TOTPConfig("too-short")
                              );

            Assert.That(added.IsSuccess, Is.True, $"The peer with a broken TOTP could not be added: {added.ErrorResponse}");

        }

        #endregion

        #region (private) TheVersionsList()

        /// <summary>
        /// The versions list, asked with the token of the peer with a broken
        /// TOTP and with ids of the caller's own.
        /// </summary>
        private async Task<Answer> TheVersionsList()
        {

            using var http     = new HttpClient();
            using var request  = new HttpRequestMessage(HttpMethod.Get, $"{BaseURL}ext/versions");

            request.Headers.TryAddWithoutValidation("Authorization",     $"Token {BrokenToken}");
            request.Headers.TryAddWithoutValidation("X-Request-ID",      RequestId);
            request.Headers.TryAddWithoutValidation("X-Correlation-ID",  CorrelationId);

            using var response = await http.SendAsync(request);

            return new Answer(
                       (Int32) response.StatusCode,
                       await response.Content.ReadAsStringAsync(),
                       response.Headers.TryGetValues("X-Request-ID", out var requestIds) ? requestIds.FirstOrDefault() : null
                   );

        }

        /// <summary>
        /// What the caller was answered.
        /// </summary>
        private sealed record Answer(Int32    Status,
                                     String   Text,
                                     String?  RequestIdHeader);

        #endregion

    }

}
