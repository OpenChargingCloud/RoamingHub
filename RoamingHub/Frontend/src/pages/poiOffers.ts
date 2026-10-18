import { poiList, textOf, type Entity } from '../api/poi';
import { errorMessage } from '@node/ui';
import { html, live, nothing, type TemplateResult } from '@node/view';
import { entityURL, offered } from './poi';

/**
 * The charging tariff offers covering a charging pool, a charging station or an EVSE: another price for a time, of its
 * operator and of e-mobility providers. The POI API lists them below it, ~/…/EVSEs/{id}/CTOs, those of a pool or a
 * station covering one of its EVSEs; the operator's first, then the providers', each in the order of their
 * identifications.
 *
 * - Every offer covering it, or those that apply now, or at a time: ?applicable=true, &at=. An offer applies while it
 *   is active and its opening times are open, in the time zone of the charging pool; of an e-mobility provider whose
 *   offers came to overlap there, the one with the later notBefore.
 * - Its status now: planned, active, exhausted or expired, as the POI API tells it, ?status=. Those applying now are
 *   active.
 * - Each leads to its page below its owner, through the roaming network: ~/RNs/{id}/CTOs/{CTO_Id} redirects there, as
 *   the identification of an offer names no owner.
 * - The list is asked again when the status of an offer shown changes by its times, at its notBefore and right after
 *   its notAfter, when the POI API tells of it as well; while those applying now are shown, also at every minute, as
 *   opening times open and close by the minute.
 */


/** Which offers are shown: every one covering it, those applying now, or those applying at a time. */
export type OffersShown = 'all' | 'now' | 'at';

/** The offers shown, how many there are, and the status now of each, by its identification. */
export interface Offers {
    entries:   Entity[];
    count:     number;
    statuses:  Map<string, string>;
}

/** How many offers the table shows: the rest are in the list of the POI API. */
export const offersShown = 100;

/** The statuses the POI API is asked for; an offer of none of them has expired. */
const askedStatuses = [ 'planned', 'active', 'exhausted' ] as const;

/** The longest the list waits to be asked again: then it looks again. */
const maxWait_ms = 60 * 60 * 1000;


/** The given time as a datetime-local field says it, in the time zone of the browser, to the minute. */
export function localTimeOf(time: Date): string {
    return new Date(time.getTime() - time.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

/** The time a datetime-local field says, in the time zone of the browser; null for none. */
export function timeOf(value: string): Date | null {
    const time = value ? new Date(value) : null;
    return time !== null && !Number.isNaN(time.getTime()) ? time : null;
}

/**
 * The offers covering what the given path names, those shown: every one, or those applying now or at the given time;
 * with the status now of each.
 */
export async function loadOffers(path:     string,
                                 shown:    OffersShown,
                                 at:       Date | null,
                                 signal?:  AbortSignal): Promise<Offers> {

    const applicable  = shown === 'all'
                            ? undefined
                            : { applicable: 'true', ...(shown === 'at' && at !== null ? { at: at.toISOString() } : {}) };

    // Those applying now are active: their status needs no asking.
    const [ list, ...byStatus ] = await Promise.all([
                                      poiList<Entity>(`${path}/CTOs`, { limit: offersShown, filters: applicable }, signal),
                                      ...(shown === 'now'
                                              ? []
                                              : askedStatuses.map(status => poiList<Entity>(`${path}/CTOs`, { limit: offersShown, filters: { status } }, signal)))
                                  ]);

    const statuses = new Map<string, string>();

    if (shown === 'now')
        for (const offer of list.entries)
            statuses.set(offer['@id'], 'active');

    byStatus.forEach((part, index) => {
        for (const offer of part.entries)
            statuses.set(offer['@id'], askedStatuses[index]!);
    });

    // Of none of them, when each was asked for all of them: expired.
    if (shown !== 'now' && byStatus.every(part => part.entries.length >= part.filtered))
        for (const offer of list.entries)
            if (!statuses.has(offer['@id']))
                statuses.set(offer['@id'], 'expired');

    return { entries: list.entries, count: list.filtered, statuses };

}

/**
 * When the offers shown are to be asked for again, in milliseconds since the epoch: at the next notBefore of one
 * still planned, right after the next notAfter of one not expired yet; while those applying now are shown, at the next
 * minute at the latest. Null when nothing changes by time.
 */
export function nextRefresh(entries:  Entity[],
                            shown:    OffersShown,
                            now:      number): number | null {

    const changes = entries.flatMap(offer => {

        const notBefore  = typeof offer.notBefore === 'string' ? Date.parse(offer.notBefore) : NaN;
        const notAfter   = typeof offer.notAfter  === 'string' ? Date.parse(offer.notAfter)  : NaN;

        return [ notBefore > now ? notBefore : NaN, notAfter >= now ? notAfter + 1 : NaN ];

    }).filter(Number.isFinite);

    if (shown === 'now')
        changes.push((Math.floor(now / 60_000) + 1) * 60_000);

    return changes.length > 0
               ? Math.min(Math.min(...changes), now + maxWait_ms)
               : null;

}

/** The chip of a status: active in green, exhausted in amber, planned and expired plain. */
function statusChip(status: string | undefined): TemplateResult | string {

    if (status === undefined)
        return '';

    const colour = status === 'active' ? ' ok' : status === 'exhausted' ? ' warn' : '';

    return html`<span class="chip poi-offer-status${colour}">${status}</span>`;

}

/** What can be changed of the offers shown. */
export interface OffersControls {
    show:  (shown: OffersShown) => void;
    at:    (value: string) => void;
}

/**
 * The offers covering a pool, a station or an EVSE: which are shown, every one or those applying now or at a time,
 * and a table of them, each leading to its page through the roaming network at the given path.
 */
export function offersView(offers:    Offers | null,
                           problem:   unknown,
                           shown:     OffersShown,
                           at:        string,
                           network:   string,
                           controls:  OffersControls): TemplateResult {

    const rows = offers?.entries.slice(0, offersShown) ?? [];

    return html`
        <section class="card poi-offers">

            <h2><i class="fa-solid fa-percent"></i> Charging tariff offers
                ${offers !== null ? html`<span class="chip">${offers.count}</span>` : nothing}</h2>

            <p class="hint">
                Of its operator, then of e-mobility providers. An offer applies while it is active and its opening times
                are open, in the time zone of its charging pool; of a provider only the one with the latest notBefore.
            </p>

            <div class="log-filters poi-filters">

                <label class="filter-level">
                    Show
                    <select id="poi-offers-shown" @change=${(event: Event) => controls.show((event.target as HTMLSelectElement).value as OffersShown)}>
                        <option value="all" ?selected=${shown === 'all'}>every offer covering it</option>
                        <option value="now" ?selected=${shown === 'now'}>those applying now</option>
                        <option value="at"  ?selected=${shown === 'at'}>those applying at ...</option>
                    </select>
                </label>

                ${shown === 'at' ? html`
                    <label class="filter-level">
                        At
                        <input type="datetime-local" id="poi-offers-at" .value=${live(at)}
                               @change=${(event: Event) => controls.at((event.target as HTMLInputElement).value)} />
                    </label>` : nothing}

            </div>

            ${problem !== null
                  ? html`<div class="error-box">The offers could not be loaded: ${errorMessage(problem)}</div>`
                  : shown === 'at' && timeOf(at) === null
                        ? html`<p class="muted">Choose a time.</p>`
                        : offers === null
                              ? html`<div class="loading">Loading ...</div>`
                              : rows.length === 0
                                    ? html`<p class="muted">${shown === 'all' ? 'None.' : 'None applies then.'}</p>`
                                    : html`
                                        <div class="table-scroll">
                                            <table class="table poi-table">
                                                <thead><tr><th>Identification</th><th>Name</th><th>Offer</th><th>Status now</th><th>Not before</th><th>Not after</th></tr></thead>
                                                <tbody>
                                                    ${rows.map(offer => html`
                                                        <tr>
                                                            <td><a href=${entityURL(`${network}/CTOs/${encodeURIComponent(offer['@id'])}`)}><code>${offer['@id']}</code></a></td>
                                                            <td>${textOf(offer.name)}</td>
                                                            <td>${offered(offer)}</td>
                                                            <td>${statusChip(offers.statuses.get(offer['@id']))}</td>
                                                            <td>${String(offer.notBefore ?? '')}</td>
                                                            <td>${String(offer.notAfter  ?? '')}</td>
                                                        </tr>`)}
                                                </tbody>
                                            </table>
                                        </div>`}

            ${offers !== null && offers.count > rows.length
                  ? html`<p class="muted small">The first ${rows.length} of ${offers.count}; all of them are in the list of the POI API.</p>`
                  : nothing}

        </section>`;

}
