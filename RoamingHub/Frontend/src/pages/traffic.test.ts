/**
 * The traffic page, drawn into a document of happy-dom against a stand-in for
 * the hub, and a stream that does what a test tells it to - see test/hub.ts
 * and traffic/store.test.ts.
 *
 * A line is made as elements, once, and made anew in its place when it is
 * unfolded: what a call says - a path, a party, a message a peer wrote - goes
 * in as text, never as markup, and somebody who unfolds a line from the
 * keyboard is still on it afterwards.
 */

import { asked, change, leave, open, refused, settled, until } from '../../test/hub.ts';

import { strict as assert }       from 'node:assert';
import { after, describe, it }    from 'node:test';

import type { Call, CallPage } from '../api/client.ts';


/** A stream that does what a test tells it to. */
class Stream {

    static readonly CONNECTING = 0;
    static readonly OPEN       = 1;
    static readonly CLOSED     = 2;

    static latest: Stream | null = null;

    readyState = Stream.CONNECTING;

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
        this.readyState = Stream.CLOSED;
    }

    /** What the browser does to it, from the outside. */
    fire(name: string, event: unknown = {}): void {
        for (const listener of this.listeners.get(name) ?? [])
            listener(event);
    }

}

(globalThis as unknown as { EventSource: unknown }).EventSource = Stream;

const { trafficPage } = await import('./traffic.ts');


/** A call, as the hub says it, with whatever a test wants different. */
function aCall(id: number, more: Partial<Call> = {}): Call {
    return {
        id,
        timestamp:       '2026-10-04T12:00:00Z',
        direction:       'in',
        peer:            'DE*AAA',
        from:            'DE*AAA',
        to:              'DE*GDH',
        version:         '2.2.1',
        module:          'locations',
        method:          'PUT',
        path:            `/ocpi/2.2.1/emsp/locations/DE/AAA/LOC${id}`,
        httpStatusCode:  200,
        ocpiStatusCode:  1000,
        statusMessage:   null,
        ok:              true,
        error:           null,
        durationMs:      12,
        requestSize:     100,
        responseSize:    50,
        requestId:       null,
        correlationId:   null,
        remoteSocket:    null,
        requestBody:     null,
        responseBody:    null,
        ...more
    };
}

/** The traffic page opened, its stream open, and the snapshot of these calls drawn. */
async function opened(calls: Call[], payloads = true): Promise<HTMLElement> {

    const page: CallPage = {
        calls,
        lastId:    Math.max(0, ...calls.map(call => call.id)),
        count:     calls.length,
        capacity:  5000,
        payloads,
        peers:     [...new Set(calls.flatMap(call => [call.peer, call.from, call.to]).filter((party): party is string => party !== null))]
    };

    const root = await open(trafficPage, '/traffic', [ 'traffic:read' ],
                            asked => asked.method === 'GET' && asked.path === '/traffic' ? page : undefined,
                            root => root.querySelector('#calls') !== null);

    Stream.latest!.fire('open');

    await until(() => lines(root).length === calls.length, 'the snapshot was not drawn');

    return root;

}

function lines(root: HTMLElement): HTMLElement[] {
    return [...root.querySelectorAll<HTMLElement>('#calls > .line.call')];
}

/** A call the stream brings. */
function streamed(call: Call): void {
    Stream.latest!.fire('call', { data: JSON.stringify(call) });
}


describe('the traffic', () => {

    after(() => leave());

    it('says what a call says as text, not as markup', async () => {

        const root = await opened([
            aCall(1, { path: '/ocpi/2.2.1/emsp/locations/<b>DE</b>', peer: '<i>DE*AAA</i>', module: '<em>locations</em>' })
        ]);

        const line = lines(root)[0]!;

        assert.equal(line.querySelector('.message')!.textContent, 'PUT /ocpi/2.2.1/emsp/locations/<b>DE</b>');
        assert.equal(line.querySelector('.peer')!.textContent,    '<i>DE*AAA</i>');
        assert.equal(line.querySelector('.module')!.textContent,  '<em>locations</em> 2.2.1');

        assert.equal(line.querySelectorAll('.message b').length, 1, 'a path went in as markup');
        assert.equal(line.querySelectorAll('.peer i, .module em').length, 0, 'a party or a module went in as markup');

    });

    it('adds what the stream brings at the end, and the party it brings to the chooser', async () => {

        const root = await opened([ aCall(1) ]);

        streamed(aCall(2, { peer: 'DE*BBB', from: 'DE*BBB' }));

        await until(() => lines(root).length === 2, 'the call streamed was not added');

        assert.deepEqual(lines(root).map(line => line.dataset.id), [ '1', '2' ]);
        assert.deepEqual([...root.querySelectorAll<HTMLOptionElement>('#peer option')].map(option => option.value),
                         [ '', 'DE*AAA', 'DE*BBB', 'DE*GDH' ]);

    });

    it('keeps the party chosen, and its filter, when the hub sees a new one', async () => {

        const root   = await opened([ aCall(1), aCall(2, { peer: 'DE*BBB', from: 'DE*BBB' }) ]);
        const chosen = root.querySelector<HTMLSelectElement>('#peer')!;

        change(chosen, 'DE*BBB');

        assert.deepEqual(lines(root).map(line => line.dataset.id), [ '2' ]);

        streamed(aCall(3, { peer: 'DE*CCC', from: 'DE*CCC' }));

        await until(() => [...chosen.options].some(option => option.value === 'DE*CCC'), 'the new party was not offered');

        assert.equal(chosen.value, 'DE*BBB', 'the party chosen was reset by a new one');
        assert.deepEqual(lines(root).map(line => line.dataset.id), [ '2' ], 'the call of another party was shown');

    });

    it('unfolds a line from the keyboard, and is still on it afterwards', async () => {

        const root = await opened([ aCall(1, { statusMessage: 'Fine.' }), aCall(2) ]);

        const head = lines(root)[0]!.querySelector<HTMLElement>('.call-head')!;

        head.focus();
        head.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));

        const line = lines(root)[0]!;

        assert.ok(line.classList.contains('open'), 'the line was not unfolded');
        assert.match(line.querySelector('.call-detail')!.textContent!, /Status message\s*Fine\./);
        // As a yes or no: two elements told apart by assert.equal are printed
        // whole, with the window of happy-dom they hang from, and a failure
        // would print until the memory ran out.
        assert.ok(document.activeElement === line.querySelector('.call-head'), 'the focus went with the line that was there before');
        assert.equal(lines(root).length, 2);

    });

    it('says in an unfolded line that the bodies are not kept, where they are not', async () => {

        const root = await opened([ aCall(1) ], false);

        lines(root)[0]!.querySelector<HTMLElement>('.call-head')!.click();

        const note = lines(root)[0]!.querySelector('.call-detail p')!;

        assert.equal(note.textContent, 'The bodies are not kept. Set ocpi.logging.payloads to keep them.');
        assert.equal(note.querySelector('code')?.textContent, 'ocpi.logging.payloads');

    });

    it('says why the traffic could not be loaded, as text', async () => {

        await open(trafficPage, '/traffic', [ 'traffic:read' ],
                   () => refused(500, '<b>broken</b>'),
                   root => root.querySelector('#calls') !== null);

        Stream.latest!.fire('open');

        await until(() => document.querySelector('#calls .line.error') !== null, 'the error was not said');

        const error = document.querySelector('#calls .line.error')!;

        assert.match(error.textContent!, /<b>broken<\/b>/);
        assert.equal(error.querySelectorAll('b').length, 0, 'the error went in as markup');
        assert.ok(asked.some(one => one.path === '/traffic'));

    });

    it('clears the view on Clear view', async () => {

        const root = await opened([ aCall(1), aCall(2) ]);

        root.querySelector<HTMLButtonElement>('#clear')!.click();

        await settled();

        assert.equal(lines(root).length, 0);
        assert.ok(root.querySelector('#calls .log-empty'), 'the empty list did not say so');

    });

});
