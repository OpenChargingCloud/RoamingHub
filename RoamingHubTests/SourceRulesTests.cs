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

using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.RoamingHub.Tests
{

    /// <summary>
    /// What the kit holds every kind's C# source to, asked of this roaming
    /// hub's - as its pages are held to WWCP_Node's test/pages.ts.
    /// </summary>
    public class SourceRulesTests
    {

        #region NoTextOfThisRoamingHubPutsAnArticleBeforeAName()

        /// <summary>
        /// Nothing this roaming hub says puts an article in front of a name it
        /// interpolates. Which article a name takes goes by how the name is
        /// said, and a text cannot know that of a name it is handed: the kit's
        /// own "A {Node.Kind.Name}" read "A electric vehicle" (found by the
        /// EV). None of the hub's texts did; this keeps it so.
        /// </summary>
        [Test]
        public void NoTextOfThisRoamingHubPutsAnArticleBeforeAName()
        {

            var repository = SourceRules.RepositoryAbove(AppContext.BaseDirectory, "RoamingHub", "RoamingHubTests");

            Assert.That(SourceRules.ArticlesBeforeANameIn(Path.Combine(repository, "RoamingHub")), Is.Empty);

        }

        #endregion

    }

}
