import type { Call } from '../api/client';
import { auth } from '../auth';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { shell } from '@node/shell';
import { traffic } from '../traffic/store';
import { formatTime, formatTimestamp } from '@node/ui';
import { html, render } from '@node/view';

/** An element of the given kind and class, with the given text - as text, which needs no escaping. */
function element<K extends keyof HTMLElementTagNameMap>(Tag: K, Class = '', Text?: string): HTMLElementTagNameMap[K] {
    const made = document.createElement(Tag);
    if (Class !== '')
        made.className = Class;
    if (Text !== undefined)
        made.textContent = Text;
    return made;
}

/** A Font Awesome icon. */
function icon(Name: string): HTMLElement {
    return element('i', `fa-solid ${Name}`);
}

/** The status as one chip: the HTTP code, and the OCPI one when there is one. */
function statusElement(Call: Call): HTMLSpanElement {

    const chip = element('span', `chip status ${Call.ok ? 'ok' : 'bad'}`,
                         Call.ocpiStatusCode === null
                             ? String(Call.httpStatusCode)
                             : `${Call.httpStatusCode}/${Call.ocpiStatusCode}`);

    chip.title = Call.ocpiStatusCode === null
                     ? `HTTP ${Call.httpStatusCode}`
                     : `HTTP ${Call.httpStatusCode}, OCPI ${Call.ocpiStatusCode}`;

    return chip;

}

/** The two parties out of the OCPI headers, from the one to the other. */
function partiesElement(Call: Call): HTMLSpanElement {

    const parties = element('span', 'parties');

    if (!Call.from && !Call.to)
        parties.append(element('span', 'muted', '-'));
    else
        parties.append(Call.from ?? '?', icon('fa-arrow-right-long'), Call.to ?? '?');

    return parties;

}

/**
 * One line, made once - and made anew only when it is unfolded or folded
 * again, in place of the one it was.
 *
 * Made as elements, not drawn by view.ts: a line is never drawn again, and
 * comparing a list of the thousands the hub keeps with itself, for every call
 * that goes through it, would put back the cost that making a line once takes
 * away. What a call says - a path, a party, a message somebody else wrote -
 * goes in as text, which needs no escaping: it was markup once, escaped by
 * hand at every place it went in, and one place that forgot would have let a
 * peer write into this page.
 */
export function lineElement(Call: Call, Open = false, Payloads = true): HTMLElement {

    const line = element('div', `line call ${Call.ok ? '' : 'failed'} ${Open ? 'open' : ''}`);
    line.dataset.id = String(Call.id);

    const head = element('div', 'call-head');
    head.setAttribute('role', 'button');
    head.tabIndex = 0;
    head.setAttribute('aria-expanded', String(Open));

    const time = element('time', '', formatTime(Call.timestamp));
    time.dateTime = Call.timestamp;
    time.title    = formatTimestamp(Call.timestamp);

    const direction = element('span', `chip dir ${Call.direction}`);
    direction.title = Call.direction === 'in' ? 'A peer called this hub' : 'This hub called a peer';
    direction.append(icon(Call.direction === 'in' ? 'fa-arrow-right-to-bracket' : 'fa-arrow-right-from-bracket'));

    const message = element('span', 'message');
    message.append(element('b', '', Call.method), ` ${Call.path}`);

    head.append(time,
                direction,
                statusElement(Call),
                element('span', 'peer', Call.peer ?? '-'),
                partiesElement(Call),
                element('span', 'chip module', `${Call.module ?? '-'}${Call.version ? ' ' + Call.version : ''}`),
                message,
                element('span', 'duration', `${Call.durationMs} ms`));

    line.append(head);

    if (Open)
        line.append(detailElement(Call, Payloads));

    return line;

}

/** Everything the line had no room for, unfolded under it. */
function detailElement(Call: Call, Payloads: boolean): HTMLElement {

    const rows: [string, string | null][] = [
        ['Status message',  Call.statusMessage],
        ['Error',           Call.error],
        ['Request size',    Call.requestSize  === null ? null : `${Call.requestSize} bytes`],
        ['Response size',   Call.responseSize === null ? null : `${Call.responseSize} bytes`],
        ['Request ID',      Call.requestId],
        ['Correlation ID',  Call.correlationId],
        ['Remote socket',   Call.remoteSocket],
        ['When',            formatTimestamp(Call.timestamp)]
    ];

    const detail = element('div', 'call-detail');
    const kvList = element('div', 'kv-list');

    for (const [key, value] of rows) {
        if (value !== null && value !== '') {
            const kv = element('div', 'kv');
            kv.append(element('span', 'k', key), element('span', 'v', value));
            kvList.append(kv);
        }
    }

    detail.append(kvList);

    for (const [label, value] of [['Request', Call.requestBody], ['Response', Call.responseBody]] as const) {
        if (value !== null && value !== '') {
            const body = element('div', 'call-body');
            body.append(element('h4', '', label), element('pre', '', value));
            detail.append(body);
        }
    }

    if (!Payloads) {
        const note = element('p', 'muted small', 'The bodies are not kept. Set ');
        note.append(element('code', '', 'ocpi.logging.payloads'), ' to keep them.');
        detail.append(note);
    }

    return detail;

}

/** An option of a chooser. */
function option(Value: string, Text: string): HTMLOptionElement {
    const made = element('option', '', Text);
    made.value = Value;
    return made;
}

/**
 * What went between the peers, as it happens.
 *
 * This is the page a hub exists for. Two peers that talk directly can each
 * read their own log and compare them; the moment a hub is between them,
 * neither can say what the other actually sent, and "it works for us" is an
 * answer nobody can check. So every call is written down here, both ways, with
 * the two parties it was between.
 *
 * Built like the Logs page beside it - one stream, filters that work on what
 * is already in the browser, a list that follows along until somebody scrolls
 * up - with two differences that come from what it shows:
 *
 *  - The stream opens and closes with this page rather than with the sign-in.
 *    It is behind a permission of its own, and a busy hub puts far more
 *    through here than it writes to its log.
 *
 *  - A line carries two statuses. OCPI answers a refusal with `200` and a
 *    status code inside the envelope as readily as with a `4xx`, so a page
 *    that showed only the HTTP status would call half the failures a success.
 *    "Only what failed" means either of them said so.
 */
export const trafficPage: Page = {

    title: 'Traffic',

    render({ root }) {

        const content = shell(root, {
            active:    '/traffic',
            title:     'Traffic',
            subtitle:  'Every OCPI call that touched this hub, and the two parties it was between.',
            actions:   html`
                <span id="stream-state" class="stream-state"></span>
                <button type="button" id="clear" class="btn small" title="Clear what this page shows; the hub keeps its record"
                        @click=${() => traffic.clear()}>Clear view</button>
            `
        });

        render(content, html`

            <div class="log-filters">

                <label class="filter-search">
                    <i class="fa-solid fa-magnifying-glass"></i>
                    <input type="search" id="search" placeholder="Search the paths and modules ..." autocomplete="off" />
                </label>

                <label class="filter-level">
                    Direction
                    <select id="direction">
                        <option value="">both ways</option>
                        <option value="in">a peer called this hub</option>
                        <option value="out">this hub called a peer</option>
                    </select>
                </label>

                <label class="filter-level">
                    Party
                    <select id="peer"><option value="">everybody</option></select>
                </label>

                <label class="filter-follow">
                    <input type="checkbox" id="failed" />
                    Only what failed
                </label>

                <label class="filter-follow">
                    <input type="checkbox" id="follow" checked />
                    Follow
                </label>

            </div>

            <div id="bodies-hint" class="hint traffic-hint" hidden></div>

            <div id="calls" class="log traffic" role="log" aria-live="polite" tabindex="0"></div>

            <div class="log-foot small muted">
                <span id="counts"></span>
                <button type="button" id="to-bottom" class="btn small" hidden>Jump to the newest</button>
            </div>

        `);

        const list        = must<HTMLElement>       (content, '#calls');
        const counts      = must<HTMLElement>       (content, '#counts');
        const bodiesHint  = must<HTMLElement>       (content, '#bodies-hint');
        const toBottom    = must<HTMLButtonElement> (content, '#to-bottom');
        const search      = must<HTMLInputElement>  (content, '#search');
        const direction   = must<HTMLSelectElement> (content, '#direction');
        const peer        = must<HTMLSelectElement> (content, '#peer');
        const failed      = must<HTMLInputElement>  (content, '#failed');
        const follow      = must<HTMLInputElement>  (content, '#follow');
        const streamState = must<HTMLElement>       (root,    '#stream-state');

        /** The ids whose details are unfolded. */
        const opened = new Set<number>();

        let renderedPeers = '';


        function matches(call: Call): boolean {

            if (direction.value !== '' && call.direction !== direction.value)
                return false;

            if (failed.checked && call.ok)
                return false;

            // Any of the three, and that is the point: a call that arrives
            // from one peer is often about another, and somebody asking "what
            // has DE*GEF been doing here?" means either end of it.
            if (peer.value !== '' &&
                call.peer !== peer.value && call.from !== peer.value && call.to !== peer.value)
                return false;

            const needle = search.value.trim().toLowerCase();

            return needle.length === 0 ||
                   call.path.toLowerCase().includes(needle) ||
                   (call.module ?? '').toLowerCase().includes(needle) ||
                   (call.statusMessage ?? '').toLowerCase().includes(needle) ||
                   (call.error ?? '').toLowerCase().includes(needle);

        }

        /** Lines for the calls given, in their order, to go into the list at once. */
        function linesOf(calls: readonly Call[]): DocumentFragment {
            const lines = document.createDocumentFragment();
            for (const call of calls)
                lines.append(lineElement(call, opened.has(call.id), traffic.payloads));
            return lines;
        }

        function atBottom(): boolean {
            // A few pixels of slack: a list that is one rounding error short
            // of the bottom is, to the person reading it, at the bottom.
            return list.scrollTop + list.clientHeight >= list.scrollHeight - 24;
        }

        function scrollToBottom(): void {
            list.scrollTop  = list.scrollHeight;
            toBottom.hidden = true;
        }

        /** Everything again: after a reload, or when a filter changed. */
        function redraw(): void {

            const stick = follow.checked && atBottom();
            const shown = traffic.calls.filter(matches);

            list.replaceChildren(shown.length > 0
                                     ? linesOf(shown)
                                     : element('div', 'log-empty', 'Nothing to show. Nothing has gone between the peers yet, or the filters are too narrow.'));

            if (stick || follow.checked)
                scrollToBottom();

            updateCounts();
            drawPeers();

        }

        /** Only what is new: the usual case, and the cheap one. */
        function append(added: Call[]): void {

            const wanted = added.filter(matches);

            if (wanted.length > 0) {

                const stick = follow.checked && atBottom();

                list.querySelector('.log-empty')?.remove();
                list.append(linesOf(wanted));

                // The hub keeps a bounded record and so does this page; what
                // fell out of the store has to leave the list as well.
                while (list.childElementCount > traffic.calls.length)
                    list.firstElementChild?.remove();

                if (stick)
                    scrollToBottom();
                else
                    toBottom.hidden = false;

            }

            updateCounts();
            drawPeers();

        }

        function updateCounts(): void {

            const shown = list.querySelectorAll('.line').length;

            counts.textContent = `${shown} of ${traffic.calls.length} calls` +
                                 (traffic.capacity > 0 ? ` (the hub keeps the last ${traffic.capacity}, in memory only)` : '');

            bodiesHint.hidden      = traffic.payloads;
            bodiesHint.textContent = traffic.payloads
                                         ? ''
                                         : 'The request and response bodies are not being kept: what travels through a hub is a location ' +
                                           'somebody operates, a session somebody is having and a card somebody is holding. Set ' +
                                           'ocpi.logging.payloads in the configuration file to keep them anyway.';

        }

        /** The party filter, made anew only when the hub has seen a new one. */
        function drawPeers(): void {

            const all = [...traffic.peers].sort();
            const key = all.join('\0');

            if (key === renderedPeers)
                return;

            renderedPeers = key;

            const chosen = peer.value;

            peer.replaceChildren(option('', 'everybody'),
                                 ...all.map(party => option(party, party)));

            // A party that has gone out of the window would otherwise silently
            // reset the filter to "everybody" and show more than was asked for.
            peer.value = all.includes(chosen) ? chosen : '';

        }

        function showStream(): void {
            streamState.className   = `stream-state ${traffic.streamConnected ? 'live' : 'down'}`;
            streamState.textContent = traffic.streamConnected ? 'live'
                                    : traffic.refused         ? 'ended'
                                    :                           'reconnecting ...';
        }


        // Events

        list.addEventListener('click', event => toggle(event.target as Element | null));

        list.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') {
                const head = (event.target as Element | null)?.closest('.call-head');
                if (head) {
                    event.preventDefault();
                    toggle(head);
                }
            }
        });

        /**
         * Unfold a line, or fold it again: that line made anew, in its place.
         * Somebody who did it from the keyboard is still on it afterwards -
         * the line that had the focus is gone, and the focus would have gone
         * with it, to the top of the page.
         */
        function toggle(target: Element | null): void {

            const line = target?.closest<HTMLElement>('.line.call');

            if (!line)
                return;

            const id   = Number(line.dataset.id);
            const call = traffic.calls.find(one => one.id === id);

            if (call === undefined)
                return;

            if (opened.has(id))
                opened.delete(id);
            else
                opened.add(id);

            const hadFocus = line.contains(document.activeElement);
            const made     = lineElement(call, opened.has(id), traffic.payloads);

            line.replaceWith(made);

            if (hadFocus)
                made.querySelector<HTMLElement>('.call-head')?.focus();

        }

        search   .addEventListener('input',  () => redraw());
        direction.addEventListener('change', () => redraw());
        peer     .addEventListener('change', () => redraw());
        failed   .addEventListener('change', () => redraw());
        follow   .addEventListener('change', () => { if (follow.checked) scrollToBottom(); });
        toBottom .addEventListener('click',  () => scrollToBottom());

        list.addEventListener('scroll', () => {
            if (atBottom())
                toBottom.hidden = true;
        });

        const stopListening = traffic.onChange(event => {

            switch (event.type) {

                case 'calls':
                    append(event.added);
                    break;

                case 'reloaded':
                    redraw();
                    break;

                case 'stream':
                    showStream();

                    // Taken out of the role that let it read this: what the
                    // menu offers is asked again, so that the next page drawn
                    // no longer offers the traffic.
                    if (traffic.refused)
                        void auth.refresh();

                    break;

                case 'error': {
                    const line = element('div', 'line error');
                    line.append(element('span', 'message', event.text));
                    list.append(line);
                    break;
                }

            }

        });

        traffic.start();

        showStream();
        redraw();

        // Unlike the event log, this one goes when the page goes: the stream
        // is behind its own permission, it can run hot, and nothing outside
        // this page shows it.
        return () => {
            stopListening();
            traffic.stop();
        };

    }

};
