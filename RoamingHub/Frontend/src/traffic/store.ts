import { api, ApiError, type Call } from '../api/client';

// The browser's copy of what went between the peers, fed by the same two
// things as the event log beside it: a snapshot from the JSON API and a
// Server-Sent Events stream. Both carry ids from the same counter on the hub,
// and the counter only ever grows - so a call is taken only when it is newer
// than the snapshot, and a replayed stream (Hermod repeats its cached events
// to every new client) does no harm.
//
// The stream is not filtered: the snapshot and the events have to be about the
// same thing, or a filter changed at the browser would leave holes that only
// another round trip could fill. Filtering by peer, by direction and by
// outcome happens on this side, over what is already here, and is therefore
// instant.
//
// What this does NOT share with the event log is when it runs. The log follows
// along from the sign-in, whichever page is open, so that opening the Logs
// page shows what happened while somebody was reading the configuration. This
// one starts and stops with its page: it is behind a permission of its own,
// and a busy hub puts far more through here than it writes to its log. A
// stream nobody is looking at is a stream worth closing - and a page the
// browser keeps for the way back is a page nobody is looking at: see pause().

export type StoreEvent =
    | { type: 'calls';  added: Call[] }
    | { type: 'reloaded' }
    | { type: 'stream' }
    | { type: 'error';  text: string };

type Listener = (event: StoreEvent) => void;

/**
 * How many calls the page keeps. The hub keeps its own number and answers with
 * at most that many; this is the ceiling for a page that has been open all day
 * beside a busy hub.
 */
const MAX_CALLS = 5_000;

/** How much of the traffic a fresh page loads before it starts following along. */
const SNAPSHOT_SIZE = 1_000;

/**
 * How long after the stream has given up before the hub is asked why, and how
 * long before asking again where it could not answer either - the event log's
 * two numbers, for the same reasons: see WWCP_Node's logs/store.ts, imported
 * here as @node/logs/store.
 */
const ASK_WHY_AFTER   =  3_000;
const ASK_AGAIN_AFTER = 10_000;


export class TrafficStore {

    /** What is known, oldest first. */
    readonly calls: Call[] = [];

    /** Every party seen at either end of a call, for the filter. */
    readonly peers = new Set<string>();

    /** Whether the event stream is up. */
    streamConnected = false;

    /** The newest id this page knows about. */
    lastId = 0;

    /** How many calls the hub keeps. */
    capacity = 0;

    /** Whether the hub is keeping the bodies at all. */
    payloads = false;

    /**
     * Whether the hub stopped sending the traffic to this account, because
     * it may no longer read it - which the stream cannot say, and asking the
     * hub who is signed in can.
     */
    refused = false;

    private source:              EventSource | null = null;
    private readonly listeners = new Set<Listener>();

    /** Set while the hub is being asked why the stream stopped. */
    private askingWhy = false;

    /** The next attempt to find out, so that stopping cancels it. */
    private askAgain: ReturnType<typeof setTimeout> | null = null;

    /** Whether pause() closed a stream that resume() is to open again. */
    private paused = false;


    onChange(listener: Listener): () => void {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }


    /** Open the stream; every 'open' - the first and every reconnect - reloads. */
    start(): void {

        if (this.source !== null)
            return;

        this.refused = false;

        const source = new EventSource(api.trafficEventsURL);
        this.source  = source;

        source.addEventListener('open', () => {
            this.streamConnected = true;
            this.emit({ type: 'stream' });
            void this.reload();
        });

        source.addEventListener('error', () => {

            if (this.streamConnected) {
                this.streamConnected = false;
                this.emit({ type: 'stream' });
            }

            // CLOSED is the browser having given up, as on the event log's
            // stream - and here it is also what a stream looks like that the
            // hub ended because its reader was taken out of the hub role.
            if (source.readyState === EventSource.CLOSED)
                this.findOutWhy(source, ASK_WHY_AFTER);

        });

        source.addEventListener('call', event => {

            try
            {
                this.apply([JSON.parse((event as MessageEvent<string>).data) as Call]);
            }
            catch (error)
            {
                console.warn('A call could not be read:', error);
            }

        });

    }

    /**
     * Why the stream stopped, asked of the hub rather than guessed.
     *
     * Three answers, where the event log has two. A 401 is a session that is
     * gone, and the sign-in page is where this person belongs. An account
     * that is still signed in but may no longer read the traffic has been
     * taken out of the role that let it - opening the stream again would only
     * be refused again, every few seconds, for as long as the page stayed
     * open, so it is said instead. Anything else is a stream that was cut,
     * and a new one is opened.
     */
    private findOutWhy(Source: EventSource, In: number): void {

        if (this.askingWhy || this.source !== Source || this.askAgain !== null)
            return;

        this.askAgain = setTimeout(() => {

            this.askAgain = null;

            if (this.source !== Source)
                return;

            this.askingWhy = true;

            api.auth.me().then(
                me => {

                    this.askingWhy = false;

                    if (this.source !== Source)
                        return;

                    Source.close();
                    this.source = null;

                    if (me.permissions.includes('traffic:read')) {
                        this.start();
                        return;
                    }

                    this.refused = true;
                    this.emit({ type: 'stream' });
                    this.emit({
                        type: 'error',
                        text: 'This account may no longer read the traffic, so the hub has stopped sending it. ' +
                              'What is shown is what came before.'
                    });

                },
                (problem: unknown) => {

                    this.askingWhy = false;

                    // Signed out: onUnauthorized has already been told, and
                    // what happens next is the router's business.
                    if (problem instanceof ApiError && problem.isUnauthorized)
                        return;

                    this.findOutWhy(Source, ASK_AGAIN_AFTER);

                }
            );

        }, In);

    }


    /** Close the stream and forget everything: at sign-out, and when the page goes. */
    stop(): void {

        if (this.askAgain !== null) {
            clearTimeout(this.askAgain);
            this.askAgain = null;
        }

        this.source?.close();
        this.source = null;

        this.streamConnected = false;
        this.paused          = false;
        this.lastId          = 0;

        this.calls.length = 0;
        this.peers.clear();

    }

    /**
     * Close the stream while the page waits in the browser's back/forward
     * cache, and keep what is known - resume() opens it again when the page
     * is shown.
     *
     * The traffic page is where "/" opens for whoever may read the traffic,
     * so it is where a hub's address typed or bookmarked lands. Left for
     * another page in the same tab it is kept whole for the way back, and its
     * stream with it: with the log's stream let go of (WWCP_Node's
     * LogStore.pause()), this one alone held one of the six connections a
     * browser gives a host for every such page, and in a headless Chrome the
     * fifth page in a row waited three seconds, the ninth 15 and said the hub
     * had not answered.
     *
     * The page is told its stream is down before it goes: unsaid, it came out
     * of the cache saying "live", and went on saying it where the hub had gone
     * meanwhile - a stream that never opens has nothing to say (found by the
     * meter, on the log's).
     */
    pause(): void {

        this.paused = this.source !== null;

        if (this.askAgain !== null) {
            clearTimeout(this.askAgain);
            this.askAgain = null;
        }

        this.source?.close();
        this.source = null;

        if (this.streamConnected) {
            this.streamConnected = false;
            this.emit({ type: 'stream' });
        }

    }

    /** Open the stream again where pause() closed one; the 'open' reloads what was missed. */
    resume(): void {

        if (!this.paused)
            return;

        this.paused = false;

        this.start();

    }

    /** Throw away what this page shows, without touching the hub's record. */
    clear(): void {
        this.calls.length = 0;
        this.emit({ type: 'reloaded' });
    }


    /** The snapshot: what went between the peers up to now, as far back as the hub keeps. */
    async reload(): Promise<void> {

        try
        {

            const page = await api.traffic(SNAPSHOT_SIZE);

            this.calls.length = 0;
            this.calls.push(...page.calls);

            this.lastId    = Math.max(page.lastId, ...page.calls.map(call => call.id), 0);
            this.capacity  = page.capacity;
            this.payloads  = page.payloads;

            for (const peer of page.peers)
                this.peers.add(peer);

            for (const call of page.calls)
                this.remember(call);

            this.emit({ type: 'reloaded' });

        }
        catch (error)
        {
            this.emit({
                type: 'error',
                text: `Could not load the traffic: ${error instanceof Error ? error.message : String(error)}`
            });
        }

    }


    /** Everything the stream delivered that the snapshot did not already hold. */
    private apply(incoming: Call[]): void {

        const added = incoming.filter(call => call.id > this.lastId).
                               sort((a, b) => a.id - b.id);

        if (added.length === 0)
            return;

        for (const call of added) {
            this.calls.push(call);
            this.lastId = call.id;
            this.remember(call);
        }

        if (this.calls.length > MAX_CALLS)
            this.calls.splice(0, this.calls.length - MAX_CALLS);

        this.emit({ type: 'calls', added });

    }

    /**
     * The parties of a call, for the filter.
     *
     * All three of them: the peer this hub was talking to, and the two out of
     * the OCPI `from` and `to` headers - which on a hub are often somebody
     * else entirely, because a call that arrives from one peer is about
     * another. That is the whole question the filter is there to answer.
     */
    private remember(call: Call): void {
        for (const party of [call.peer, call.from, call.to])
            if (party)
                this.peers.add(party);
    }

    private emit(event: StoreEvent): void {

        for (const listener of this.listeners)
        {
            try
            {
                listener(event);
            }
            catch (error)
            {
                console.error('A traffic listener failed:', error);
            }
        }

    }

}


export const traffic = new TrafficStore();
