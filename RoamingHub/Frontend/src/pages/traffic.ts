import type { Call } from '../api/client';
import { escapeHTML, html, must, render } from '../html';
import type { Page } from '../router';
import { shell } from '../shell';
import { traffic } from '../traffic/store';
import { formatTime, formatTimestamp } from '../ui';

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
                <button type="button" id="clear" class="btn small" title="Clear what this page shows; the hub keeps its record">Clear view</button>
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
        const clear       = must<HTMLButtonElement> (root,    '#clear');

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


        /** The status as one chip: the HTTP code, and the OCPI one when there is one. */
        function statusHTML(call: Call): string {

            const label = call.ocpiStatusCode === null
                              ? String(call.httpStatusCode)
                              : `${call.httpStatusCode}/${call.ocpiStatusCode}`;

            const title = call.ocpiStatusCode === null
                              ? `HTTP ${call.httpStatusCode}`
                              : `HTTP ${call.httpStatusCode}, OCPI ${call.ocpiStatusCode}`;

            return `<span class="chip status ${call.ok ? 'ok' : 'bad'}" title="${escapeHTML(title)}">${escapeHTML(label)}</span>`;
        }

        function partiesHTML(call: Call): string {

            if (!call.from && !call.to)
                return '<span class="muted">-</span>';

            return `${escapeHTML(call.from ?? '?')}<i class="fa-solid fa-arrow-right-long"></i>${escapeHTML(call.to ?? '?')}`;

        }

        function lineHTML(call: Call): string {

            const open = opened.has(call.id);

            return `<div class="line call ${call.ok ? '' : 'failed'} ${open ? 'open' : ''}" data-id="${call.id}">` +
                       `<div class="call-head" role="button" tabindex="0" aria-expanded="${open}">` +
                           `<time datetime="${escapeHTML(call.timestamp)}" title="${escapeHTML(formatTimestamp(call.timestamp))}">${escapeHTML(formatTime(call.timestamp))}</time>` +
                           `<span class="chip dir ${call.direction}" title="${call.direction === 'in' ? 'A peer called this hub' : 'This hub called a peer'}">` +
                               `<i class="fa-solid ${call.direction === 'in' ? 'fa-arrow-right-to-bracket' : 'fa-arrow-right-from-bracket'}"></i>` +
                           `</span>` +
                           statusHTML(call) +
                           `<span class="peer">${escapeHTML(call.peer ?? '-')}</span>` +
                           `<span class="parties">${partiesHTML(call)}</span>` +
                           `<span class="chip module">${escapeHTML(call.module ?? '-')}${call.version ? ' ' + escapeHTML(call.version) : ''}</span>` +
                           `<span class="message"><b>${escapeHTML(call.method)}</b> ${escapeHTML(call.path)}</span>` +
                           `<span class="duration">${escapeHTML(String(call.durationMs))} ms</span>` +
                       `</div>` +
                       (open ? detailHTML(call) : '') +
                   `</div>`;

        }

        /** Everything the line had no room for, unfolded under it. */
        function detailHTML(call: Call): string {

            const rows: [string, string | null][] = [
                ['Status message',  call.statusMessage],
                ['Error',           call.error],
                ['Request size',    call.requestSize  === null ? null : `${call.requestSize} bytes`],
                ['Response size',   call.responseSize === null ? null : `${call.responseSize} bytes`],
                ['Request ID',      call.requestId],
                ['Correlation ID',  call.correlationId],
                ['Remote socket',   call.remoteSocket],
                ['When',            formatTimestamp(call.timestamp)]
            ];

            const kv = rows.filter(([, value]) => value !== null && value !== '').
                            map(([key, value]) => `<div class="kv"><span class="k">${escapeHTML(key)}</span><span class="v">${escapeHTML(String(value))}</span></div>`).
                            join('');

            const body = (label: string, value: string | null): string =>
                value === null || value === ''
                    ? ''
                    : `<div class="call-body"><h4>${escapeHTML(label)}</h4><pre>${escapeHTML(value)}</pre></div>`;

            return `<div class="call-detail">` +
                       `<div class="kv-list">${kv}</div>` +
                       body('Request',  call.requestBody) +
                       body('Response', call.responseBody) +
                       (traffic.payloads ? '' : `<p class="muted small">The bodies are not kept. Set <code>ocpi.logging.payloads</code> to keep them.</p>`) +
                   `</div>`;

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

            list.innerHTML = traffic.calls.filter(matches).map(lineHTML).join('') ||
                             '<div class="log-empty">Nothing to show. Nothing has gone between the peers yet, or the filters are too narrow.</div>';

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
                list.insertAdjacentHTML('beforeend', wanted.map(lineHTML).join(''));

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

        /** The party filter, redrawn only when the hub has seen a new one. */
        function drawPeers(): void {

            const all = [...traffic.peers].sort();
            const key = all.join('\0');

            if (key === renderedPeers)
                return;

            renderedPeers = key;

            const chosen = peer.value;

            peer.innerHTML = '<option value="">everybody</option>' +
                             all.map(party => `<option value="${escapeHTML(party)}">${escapeHTML(party)}</option>`).join('');

            // A party that has gone out of the window would otherwise silently
            // reset the filter to "everybody" and show more than was asked for.
            peer.value = all.includes(chosen) ? chosen : '';

        }

        function showStream(): void {
            streamState.className   = `stream-state ${traffic.streamConnected ? 'live' : 'down'}`;
            streamState.textContent = traffic.streamConnected ? 'live' : 'reconnecting ...';
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

        /** Unfold a line, or fold it again. Redraws only that line. */
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

            line.outerHTML = lineHTML(call);

        }

        search   .addEventListener('input',  () => redraw());
        direction.addEventListener('change', () => redraw());
        peer     .addEventListener('change', () => redraw());
        failed   .addEventListener('change', () => redraw());
        follow   .addEventListener('change', () => { if (follow.checked) scrollToBottom(); });
        toBottom .addEventListener('click',  () => scrollToBottom());
        clear    .addEventListener('click',  () => traffic.clear());

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
                    break;

                case 'error':
                    list.insertAdjacentHTML('beforeend', `<div class="line error"><span class="message">${escapeHTML(event.text)}</span></div>`);
                    break;

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
