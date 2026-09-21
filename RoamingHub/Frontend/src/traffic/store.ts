import { api, type Call } from '../api/client';

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
// stream nobody is looking at is a stream worth closing.

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

    private source:              EventSource | null = null;
    private readonly listeners = new Set<Listener>();


    onChange(listener: Listener): () => void {
        this.listeners.add(listener);
        return () => this.listeners.delete(listener);
    }


    /** Open the stream; every 'open' - the first and every reconnect - reloads. */
    start(): void {

        if (this.source !== null)
            return;

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

    /** Close the stream and forget everything: at sign-out, and when the page goes. */
    stop(): void {

        this.source?.close();
        this.source = null;

        this.streamConnected = false;
        this.lastId          = 0;

        this.calls.length = 0;
        this.peers.clear();

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
