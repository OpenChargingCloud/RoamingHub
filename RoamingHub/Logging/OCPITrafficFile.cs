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

using System.Text;

#endregion

namespace cloud.charging.open.RoamingHub.Logging
{

    /// <summary>
    /// What went between the peers, written down where a restart cannot take
    /// it away: one call per line, appended and never rewritten.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Beside <see cref="OCPITrafficLog"/> rather than inside it. The log is a
    /// ring buffer that answers "what has been going on" quickly and forgets
    /// the oldest call to do it; this is the part that forgets nothing. They
    /// answer different questions and fail in different ways, and a reader of
    /// either should not have to think about the other.
    /// </para>
    /// <para>
    /// It needs no hook of its own: the log already raises
    /// <see cref="OCPITrafficLog.OnCall"/> for every call it takes, once, in
    /// the order it took them. Subscribing to that is the whole wiring.
    /// </para>
    /// <para>
    /// <b>One file per day</b>, named after the day in UTC. Not one file
    /// forever - a hub that has been up for a year would otherwise have a
    /// single file nobody can open - and not one per hour, which would turn a
    /// quiet week into a hundred and sixty-eight empty files.
    /// </para>
    /// <para>
    /// <b>Flushed line by line.</b> A buffered write is faster and loses the
    /// last seconds of a hub that was killed, which is exactly the moment
    /// somebody will want to read. What travels through here is one short
    /// line per OCPI call; the writing is not what a hub spends its time on.
    /// </para>
    /// <para>
    /// <b>The "id" of a line is a cursor, not a key.</b> It belongs to the
    /// traffic log, which hands it out so that a browser can tell the stream
    /// apart from the snapshot it loaded and catch up with "after" - all of
    /// which happens inside one run of this hub. It therefore starts at one
    /// again after a restart, and a file that spans one will hold the same id
    /// twice. That is not a defect to be repaired by counting on across
    /// restarts: it would give a cursor a second job it was never meant to do,
    /// and make the log read the file it writes.
    /// </para>
    /// <para>
    /// <b>Reading the open file needs FileShare.ReadWrite.</b> The handle
    /// here permits readers, but a reader must permit this writer in return,
    /// and the usual ways of reading a file do not: <c>File.ReadAllLines</c>,
    /// PowerShell's <c>Get-Content</c> and <c>type</c> all ask for
    /// <c>FileShare.Read</c> and are refused while a hub is running. Nothing
    /// is wrong with the file when that happens - the day's file is readable
    /// by anything once the day turns or the hub stops, and a reader that
    /// says <c>FileShare.ReadWrite</c> can read it at any time.
    /// </para>
    /// <para>
    /// A line is identified by when it was written and where it sits. Across
    /// systems it is the OCPI identifications that carry - "X-Request-ID" and
    /// "X-Correlation-ID", both of them on the record already - though a peer
    /// that sends neither leaves them empty, which is worth knowing before
    /// anybody leans on them.
    /// </para>
    /// </remarks>
    public sealed class OCPITrafficFile : IDisposable
    {

        #region Data

        /// <summary>
        /// The directory the files live in, unless another is given.
        /// </summary>
        public const String DefaultDirectoryName = "traffic";

        private readonly  Object       padlock = new ();

        private           StreamWriter?  writer;

        /// <summary>
        /// The day the open file belongs to, so that the first call after
        /// midnight opens the next one.
        /// </summary>
        private           DateOnly?      openDay;

        private           Boolean        disposed;

        #endregion

        #region Properties

        /// <summary>
        /// Where the files are written.
        /// </summary>
        public String  Directory    { get; }

        /// <summary>
        /// How many calls have been written since this hub started.
        /// </summary>
        public UInt64  Written      { get; private set; }

        /// <summary>
        /// The file being appended to, or null before the first call.
        /// </summary>
        public String?  CurrentFile  { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// A call could not be written down.
        /// </summary>
        /// <remarks>
        /// Raised rather than thrown, and this is the one place it matters:
        /// the log invokes its listeners inside a try/catch that swallows
        /// whatever they throw, so that a listener cannot hold up the request
        /// being answered. A write that failed would therefore be lost in
        /// silence - which is the one thing a durable record must not do. So
        /// it is reported here, and the hub puts it in the event log.
        /// </remarks>
        public event Action<OCPICall, Exception>? OnWriteFailed;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create a writer appending to the given directory, which is made
        /// when it does not exist.
        /// </summary>
        /// <param name="Directory">Where the files live.</param>
        /// <param name="Complain">Told where the directory could not be made its owner's alone.</param>
        public OCPITrafficFile(String           Directory,
                               Action<String>?  Complain   = null)
        {

            this.Directory = Directory;

            // What the peers sent each other - sessions, CDRs, the uids of
            // their tokens - is for the account the hub runs under alone.
            protocols.WWCP.Node.OwnerOnly.Directory(Directory, Complain);

        }

        #endregion


        #region Append(Call)

        /// <summary>
        /// Write one call down.
        /// </summary>
        /// <remarks>
        /// As one line of JSON - the same shape the JSON API and the event
        /// stream hand out, so that a file, a page and a stream do not each
        /// describe a call differently. One object per line rather than one
        /// array for the file, because an array has to be closed: a hub that
        /// was killed would leave a document nothing can parse, and appending
        /// to it would mean seeking past the end.
        /// </remarks>
        public void Append(OCPICall Call)
        {

            if (disposed)
                return;

            try
            {

                lock (padlock)
                {

                    var day = DateOnly.FromDateTime(Call.Timestamp.UtcDateTime);

                    if (writer is null || openDay != day)
                        Open(day);

                    // Formatting.None: one call, one line. A line that is
                    // indented is a line somebody has to re-join before they
                    // can count or grep it.
                    writer!.WriteLine(Call.ToJSON().ToString(Newtonsoft.Json.Formatting.None));
                    writer!.Flush();

                    Written++;

                }

            }
            catch (Exception e)
            {

                // Closed rather than left half-open: the next call opens it
                // again, which also recovers from a directory that came back
                // after being away.
                try
                {
                    lock (padlock)
                    {
                        writer?.Dispose();
                        writer   = null;
                        openDay  = null;
                    }
                }
                catch
                { }

                try
                {
                    OnWriteFailed?.Invoke(Call, e);
                }
                catch
                { }

            }

        }

        #endregion

        #region (private) Open(Day)

        /// <summary>
        /// Open the file of the given day for appending.
        /// </summary>
        private void Open(DateOnly Day)
        {

            writer?.Dispose();

            var path = Path.Combine(Directory, $"traffic-{Day:yyyy-MM-dd}.jsonl");

            // Append, and UTF-8 without a byte order mark: a mark in the
            // middle of a file that is appended to every day would be a
            // stray three bytes on whatever line happened to follow a
            // restart.
            //
            // FileShare.Read lets the file be read while this handle is
            // open - but only by a reader that in turn allows this writer,
            // which on Windows means FileShare.ReadWrite. A reader asking
            // for the default FileShare.Read is refused, because its own
            // sharing would have to permit the write already in progress
            // and does not. That is most tools: File.ReadAllLines,
            // Get-Content, type. See the remarks on this class.
            writer       = new StreamWriter(
                               new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
                               new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                           );

            openDay      = Day;
            CurrentFile  = path;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// Where the calls are being appended, which file is open, and how
        /// many have been written since this hub started.
        /// </summary>
        /// <remarks>
        /// So that a page which says what is kept can say it truthfully. The
        /// ring buffer alone would have every page tell its reader that a
        /// restart forgets the traffic, which stopped being true the moment
        /// the operator asked for the file.
        /// </remarks>
        public JObject ToJSON()
        {

            // Under the lock, because the three belong together: a reader
            // that caught "written" of one call and the file of the next
            // would be handed a state that never existed.
            lock (padlock)
                return new JObject(
                           new JProperty("directory",  Directory),
                           new JProperty("current",    CurrentFile),
                           new JProperty("written",    Written)
                       );

        }

        #endregion

        #region Dispose()

        public void Dispose()
        {

            if (disposed)
                return;

            disposed = true;

            lock (padlock)
            {
                try
                {
                    writer?.Dispose();
                }
                catch
                { }

                writer = null;
            }

        }

        #endregion

    }

}
