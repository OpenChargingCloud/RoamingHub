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

namespace cloud.charging.open.RoamingHub.Logging
{

    /// <summary>
    /// What went between the peers of this hub, in memory, newest last.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shape as the event log beside it - a ring of a fixed size,
    /// every entry numbered from one, and an event whenever something is
    /// added - because the browser reads both the same way: fetch what is
    /// there, then follow the Server-Sent Events stream from the last
    /// identification it saw. That is also what makes a reader that was
    /// disconnected for a minute able to catch up without reading the whole
    /// ring again.
    /// </para>
    /// <para>
    /// In memory and nowhere else. A hub that wrote every call to disk would
    /// be a hub that keeps its peers' business for them, and how long that
    /// may be kept is a question with a different answer in every
    /// jurisdiction. What is here is what a person watching can see; a
    /// deployment that has to keep more should read the stream and put it
    /// where it has decided to keep it.
    /// </para>
    /// </remarks>
    public sealed class OCPITrafficLog
    {

        #region Data

        /// <summary>
        /// How many calls are kept when nothing says otherwise.
        /// </summary>
        /// <remarks>
        /// Bigger than the event log's, because a single peering that goes
        /// wrong is a dozen calls and somebody will be looking at it after
        /// the fact.
        /// </remarks>
        public const Int32 DefaultCapacity = 5_000;

        private readonly OCPICall[]  ring;
        private readonly Lock        padlock = new ();

        private Int32   next;
        private Int32   count;
        private UInt64  lastId;

        #endregion

        #region Properties

        /// <summary>
        /// How many calls this log keeps before the oldest falls out.
        /// </summary>
        public Int32         Capacity      { get; }

        /// <summary>
        /// The clock every call is stamped against.
        /// </summary>
        public TimeProvider  TimeProvider  { get; }

        /// <summary>
        /// The identification of the newest call, or zero when there is none.
        /// </summary>
        public UInt64  LastId
        {
            get
            {
                lock (padlock)
                {
                    return lastId;
                }
            }
        }

        /// <summary>
        /// How many calls are in the log right now.
        /// </summary>
        public Int32   Count
        {
            get
            {
                lock (padlock)
                {
                    return count;
                }
            }
        }

        /// <summary>
        /// Every party that has been at either end of a call, as they would
        /// be written in a filter.
        /// </summary>
        public IEnumerable<String> KnownParties
        {
            get
            {

                var parties = new SortedSet<String>(StringComparer.OrdinalIgnoreCase);

                foreach (var call in Recent(Capacity))
                {
                    if (call.Peer is not null)  parties.Add(call.Peer);
                    if (call.From is not null)  parties.Add(call.From);
                    if (call.To   is not null)  parties.Add(call.To);
                }

                return parties;

            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Raised for every call added, so that whoever is holding an open
        /// event stream hears about it without asking again.
        /// </summary>
        public event Action<OCPICall>? OnCall;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a traffic log.
        /// </summary>
        /// <param name="Capacity">How many calls to keep.</param>
        /// <param name="TimeProvider">The clock; the system one by default.</param>
        public OCPITrafficLog(Int32          Capacity       = DefaultCapacity,
                              TimeProvider?  TimeProvider   = null)
        {

            this.Capacity      = Capacity > 0 ? Capacity : DefaultCapacity;
            this.TimeProvider  = TimeProvider ?? System.TimeProvider.System;
            this.ring          = new OCPICall[this.Capacity];

        }

        #endregion

        #region Add(...)

        /// <summary>
        /// Write down one call, and hand it to whoever is listening.
        /// </summary>
        /// <remarks>
        /// The identification and the timestamp are given here rather than by
        /// the caller, so that the order of the log is the order things were
        /// written down and not the order somebody happened to stamp them.
        /// </remarks>
        public OCPICall Add(CallDirection  Direction,
                            String?        Peer,
                            String?        From,
                            String?        To,
                            String?        Version,
                            String         Module,
                            String         Method,
                            String         Path,
                            UInt16         HTTPStatusCode,
                            Int32?         OCPIStatusCode      = null,
                            String?        OCPIStatusMessage   = null,
                            TimeSpan?      Duration            = null,
                            Int64          RequestSize         = 0,
                            Int64          ResponseSize        = 0,
                            String?        RequestId           = null,
                            String?        CorrelationId       = null,
                            String?        RemoteSocket        = null,
                            String?        Error               = null,
                            String?        RequestBody         = null,
                            String?        ResponseBody        = null)
        {

            OCPICall call;

            lock (padlock)
            {

                call = new OCPICall(
                           ++lastId,
                           TimeProvider.GetUtcNow(),
                           Direction,
                           Peer,
                           From,
                           To,
                           Version,
                           Module,
                           Method,
                           Path,
                           HTTPStatusCode,
                           OCPIStatusCode,
                           OCPIStatusMessage,
                           Duration ?? TimeSpan.Zero,
                           RequestSize,
                           ResponseSize,
                           RequestId,
                           CorrelationId,
                           RemoteSocket,
                           Error,
                           RequestBody,
                           ResponseBody
                       );

                ring[next] = call;
                next       = (next + 1) % Capacity;

                if (count < Capacity)
                    count++;

            }

            // Outside the lock: a listener that is slow, or that throws,
            // must not hold up the request that is being answered.
            try
            {
                OnCall?.Invoke(call);
            }
            catch
            { }

            return call;

        }

        #endregion

        #region Recent(Limit, After = null, Party = null)

        /// <summary>
        /// The most recent calls, oldest of the returned ones first.
        /// </summary>
        /// <param name="Limit">At most this many.</param>
        /// <param name="After">Only calls newer than this identification, for a reader catching up.</param>
        /// <param name="Party">Only calls one party was at either end of.</param>
        public IEnumerable<OCPICall> Recent(Int32    Limit,
                                            UInt64?  After   = null,
                                            String?  Party   = null)
        {

            if (Limit < 1)
                return [];

            OCPICall[] snapshot;

            lock (padlock)
            {

                snapshot = new OCPICall[count];

                // The ring, unrolled oldest first: the oldest entry is the
                // one 'next' points at once the ring has wrapped, and index
                // zero before it has.
                var start = count == Capacity ? next : 0;

                for (var i = 0; i < count; i++)
                    snapshot[i] = ring[(start + i) % Capacity];

            }

            IEnumerable<OCPICall> calls = snapshot;

            if (After.HasValue)
                calls = calls.Where(call => call.Id > After.Value);

            if (Party is { Length: > 0 })
                calls = calls.Where(call => call.Matches(Party));

            // Take from the end, then put it back in order: a reader wants
            // the newest calls, and wants to read them downwards.
            return calls.TakeLast(Limit);

        }

        #endregion

        #region Clear()

        /// <summary>
        /// Forget everything. The numbering carries on, so that a reader
        /// holding an identification is not handed an older call with the
        /// same one.
        /// </summary>
        public void Clear()
        {
            lock (padlock)
            {
                Array.Clear(ring);
                next   = 0;
                count  = 0;
            }
        }

        #endregion

    }

}
