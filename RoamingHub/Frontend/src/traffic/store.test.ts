/**
 * What the browser's copy of the traffic does when its stream stops.
 *
 * Run with `npm test`, which is Node's own runner reading the TypeScript as it
 * stands - no bundler, no browser, no dependency that is not already here.
 *
 * The event log's store asks the hub why its stream stopped, and so does this
 * one - see logs/store.test.ts. What is pinned here is the answer the log
 * never gets: still signed in, and no longer allowed to read the traffic,
 * because the account was taken out of the role that let it. The hub ends
 * such a stream by itself, and a store that simply opened it again would be
 * refused again, every few seconds, for as long as the page stayed open.
 */

import { strict as assert }       from 'node:assert';
import { readFileSync }           from 'node:fs';
import { registerHooks }          from 'node:module';
import { describe, it, mock }     from 'node:test';

// The pages are written for webpack, which does not want the extension in a
// relative import; Node does.
registerHooks({
    resolve(specifier, context, next) {
        return specifier.startsWith('.') && !specifier.endsWith('.ts')
                   ? next(`${specifier}.ts`, context)
                   : next(specifier, context);
    }
});

// The client reads config.ts, which reads <meta> tags when it is loaded.
(globalThis as unknown as { document: unknown }).document = { querySelector: () => null };


/** A stream that does what a test tells it to, and remembers being closed. */
class Stream {

    static readonly CONNECTING = 0;
    static readonly OPEN       = 1;
    static readonly CLOSED     = 2;

    static latest: Stream | null = null;

    readyState = Stream.CONNECTING;
    closed     = false;

    private readonly listeners = new Map<string, ((event: unknown) => void)[]>();

    readonly url: string;

    constructor(url: string) {
        this.url      = url;
        Stream.latest = this;
    }

    addEventListener(name: string, listener: (event: unknown) => void): void {
        this.listeners.set(name, [...(this.listeners.get(name) ?? []), listener]);
    }

    close(): void {
        this.closed     = true;
        this.readyState = Stream.CLOSED;
    }

    /** What the browser does to it, from the outside. */
    fire(name: string, event: unknown = {}): void {
        for (const listener of this.listeners.get(name) ?? [])
            listener(event);
    }

}

(globalThis as unknown as { EventSource: unknown }).EventSource = Stream;

const { TrafficStore } = await import('./store.ts');


/** What the hub was asked. */
let askedFor: string[] = [];

/**
 * What the hub answers who is signed in: somebody who may read the traffic,
 * somebody who may not any more, or nobody.
 */
const hubAnswers = (How: 'may read it' | 'may not any more' | 'signed out') => {

    askedFor = [];

    (globalThis as unknown as { fetch: unknown }).fetch = (url: string) => {

        askedFor.push(url);

        const signedIn = How !== 'signed out';

        return Promise.resolve({
            ok:          signedIn,
            status:      signedIn ? 200 : 401,
            statusText:  '',
            text:        () => Promise.resolve(signedIn
                                                   ? JSON.stringify({
                                                         username:     'operator',
                                                         permissions:  How === 'may read it'
                                                                           ? [ 'configuration:read', 'traffic:read' ]
                                                                           : [ 'configuration:read' ]
                                                     })
                                                   : JSON.stringify({ error: 'Not signed in.' }))
        } as unknown as Response);

    };

};

/** A hub with no traffic yet, answering the snapshot a stream's 'open' asks for. */
const hubHasNoTraffic = () => {

    askedFor = [];

    (globalThis as unknown as { fetch: unknown }).fetch = (url: string) => {

        askedFor.push(url);

        return Promise.resolve({
            ok:          true,
            status:      200,
            statusText:  '',
            text:        () => Promise.resolve(JSON.stringify({ calls: [], lastId: 0, capacity: 1_000, payloads: false, peers: [] }))
        } as unknown as Response);

    };

};

/** A store with its stream open and one that has just given up on it. */
const aStreamThatGaveUp = () => {

    const store = new TrafficStore();

    store.start();

    const stream = Stream.latest!;

    stream.readyState = Stream.CLOSED;
    stream.fire('error');

    return { store, stream };

};

/** Let the answer arrive and everything it sets off run. */
const settle = async () => {
    for (let turn = 0; turn < 8; turn++)
        await Promise.resolve();
};


describe('a traffic stream that stops', () => {

    it('is opened again where the account may still read the traffic', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            hubAnswers('may read it');

            const { store, stream } = aStreamThatGaveUp();

            mock.timers.tick(5_000);
            await settle();

            assert.equal(askedFor.length, 1, 'nobody asked the hub anything');
            assert.match(askedFor[0]!, /auth\/me/);
            assert.equal(stream.closed, true, 'the stream that had stopped was left open');
            assert.notEqual(Stream.latest, stream, 'no new stream was opened');
            assert.equal(store.refused, false);

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('is not opened again, and says why, where the account may not read it any more', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            hubAnswers('may not any more');

            const { store, stream } = aStreamThatGaveUp();

            const said: string[] = [];
            store.onChange(event => {
                if (event.type === 'error')
                    said.push(event.text);
            });

            mock.timers.tick(5_000);
            await settle();

            assert.equal(Stream.latest,     stream, 'a new stream was opened for an account the hub had stopped sending the traffic to');
            assert.equal(store.refused,     true,   'the page would go on saying "reconnecting ..."');
            assert.equal(said.length,       1,      'nobody was told why the traffic stopped');
            assert.match(said[0]!,          /may no longer read the traffic/);

            // And it stops there rather than asking again and again.
            const askedOnce = askedFor.length;

            mock.timers.tick(60_000);
            await settle();

            assert.equal(askedFor.length, askedOnce, 'it went on asking after the hub had answered');

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('is left to the sign-in where nobody is signed in any more', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            hubAnswers('signed out');

            const { store, stream } = aStreamThatGaveUp();

            mock.timers.tick(5_000);
            await settle();

            // Signing out is what happens next, through the same handler every
            // other request uses - it is not this store's to say.
            assert.equal(Stream.latest, stream, 'a new stream was opened at a hub that had refused');
            assert.equal(store.refused, false,  'a signed-out page was told about a role rather than sent to the sign-in');

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

    it('opens again with a clean slate after the page has been left and come back to', async () => {

        mock.timers.enable({ apis: ['setTimeout'] });

        try
        {
            hubAnswers('may not any more');

            const { store } = aStreamThatGaveUp();

            mock.timers.tick(5_000);
            await settle();

            assert.equal(store.refused, true);

            // The page goes, and the role is given back before it is opened
            // again: what the last visit was refused says nothing about this one.
            store.stop();
            store.start();

            assert.equal(store.refused, false, 'a page opened again still says the traffic was refused');

            store.stop();
        }
        finally
        {
            mock.timers.reset();
        }

    });

});


describe('a traffic page the browser keeps for the way back', () => {

    it('lets go of its stream while it waits there, and keeps what it knows', async () => {

        hubHasNoTraffic();

        const store = new TrafficStore();
        store.start();

        const stream   = Stream.latest!;
        const reloaded = new Promise<void>(resolve => store.onChange(event => {
                             if (event.type === 'reloaded')
                                 resolve();
                         }));

        stream.fire('open');
        await reloaded;

        stream.fire('call', { data: JSON.stringify({ id: 7, peer: 'DE*CPO', from: 'DE*CPO', to: 'DE*EMP' }) });

        store.pause();

        assert.equal(stream.closed,          true,  'a stream held open for a page nobody sees - one of the six connections a browser gives a host');
        assert.equal(store.calls.length,     1,     'what the page knew was thrown away');
        assert.equal(store.streamConnected,  false, 'a stream let go of is still said to be up');

        store.stop();

    });

    it('opens it again when it is shown, and a store whose page was not open opens nothing', () => {

        const store = new TrafficStore();
        store.start();

        const first = Stream.latest!;

        store.pause();
        store.resume();

        assert.notEqual(Stream.latest,          first, 'shown again, the page followed nothing');
        assert.equal   (Stream.latest!.closed,  false);

        // Kept while another page was open: the traffic's stream opens and
        // closes with its own page, so there was nothing to let go of.
        const idle    = new TrafficStore();
        const before  = Stream.latest;

        idle.pause();
        idle.resume();

        assert.equal(Stream.latest, before, 'a stream was opened for a page that followed none');

        // Stopped in between: what was paused is not the store's to open.
        store.pause();
        store.stop();

        const stopped = Stream.latest;

        store.resume();

        assert.equal(Stream.latest, stopped, 'a stream was opened again for a page that had been left');

    });

    it('says it is no longer live as it lets go, so that the page shown again claims nothing', async () => {

        hubHasNoTraffic();

        const store = new TrafficStore();
        store.start();

        const reloaded = new Promise<void>(resolve => store.onChange(event => {
                             if (event.type === 'reloaded')
                                 resolve();
                         }));

        Stream.latest!.fire('open');
        await reloaded;

        const told: string[] = [];
        store.onChange(event => told.push(`${event.type}, ${store.streamConnected ? 'live' : 'down'}`));

        store.pause();

        // Unsaid, the traffic page came out of the cache saying "live", and
        // went on saying it where the hub had gone meanwhile: a stream that
        // never opens has nothing to say (found by the meter, on the log's).
        assert.deepEqual(told, [ 'stream, down' ], 'the page was not told its stream is down');

        store.stop();

    });

    it('is looked after by main.ts, as the log is by startNode', () => {

        // main.ts starts the web interface, which needs a page Node has not
        // got: what is asked here is that it asks for the above, with the
        // traffic's store.
        const main = readFileSync(new URL('../main.ts', import.meta.url), 'utf-8');

        assert.match(main, /^followAcrossTheCache\(window, traffic\);/m);

    });

});
