import { poiList, poiURL, textOf, type Address, type ChargingPool, type ListPart, type Query, type Summary } from '../api/poi';
import { fromURL, toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage } from '@node/ui';
import { html, live, nothing, render, repeat, type TemplateResult } from '@node/view';

/**
 * The points of interest of this hub - the QUERY interface of its POI API, the roaming networks and what they
 * hold, searched and paged. Every page stands at the URL of its list, /poi/RNs/rn1/CPs say: a browser opening
 * it gets this page, and the page asks the same URL for the data, as JSON. So the URL of the page is the one of
 * the data, and a search or a page of the list is a URL to keep or to pass on.
 *
 * Public: the POI API is readable by everybody, and so are these pages; nobody signs in for them.
 * The search is the API's: words separated by spaces, each one found in a text of an entry, whatever its case;
 * a word starting with "!" must not be found.
 */

/** Where the lists start: the roaming networks. */
export const poiHome = '/poi/RNs';

/** The sizes of a page of a list. */
const pageSizes = [ 10, 25, 50, 100, 250 ];

/** The size of a page unless the URL says another. */
const defaultPageSize = 25;

/** How long the search waits for the next key before it asks. */
const typingPause_ms = 300;


/** What a roaming network holds, in the order of its sections: the charging pools first, as what is looked for. */
const sections = [
    { segment: 'CPs',   label: 'Charging pools'                  },
    { segment: 'CSOs',  label: 'Charging station operators'      },
    { segment: 'GOs',   label: 'Grid operators'                  },
    { segment: 'EMPs',  label: 'E-mobility providers'            },
    { segment: 'CSMs',  label: 'Charging station manufacturers'  },
    { segment: 'POs',   label: 'Parking operators'               }
] as const;

type Segment = typeof sections[number]['segment'];


/** The roaming network a page of it is of, from its URL: /poi/RNs/{RN_Id}/... */
export function roamingNetworkOf(url: URL): string {
    const found = /^\/poi\/RNs\/([^/]+)/.exec(fromURL(url.pathname));
    return found ? decodeURIComponent(found[1]!) : '';
}

/** The query of a page from its URL: the words to search, the part of the list, and an operator. */
export function queryOf(url: URL): Required<Pick<Query, 'match' | 'offset' | 'limit'>> & Pick<Query, 'operatorId'> {

    const offset  = Number(url.searchParams.get('offset') ?? 0);
    const limit   = Number(url.searchParams.get('limit')  ?? defaultPageSize);

    return {
        match:       url.searchParams.get('match') ?? '',
        offset:      Number.isSafeInteger(offset) && offset > 0 ? offset : 0,
        limit:       Number.isSafeInteger(limit)  && limit  > 0 ? limit  : defaultPageSize,
        operatorId:  url.searchParams.get('operatorId') ?? undefined
    };

}


/** How a list of the POI API is shown. */
interface ListOptions<T> {
    /** The heading. */
    title:        (roamingNetworkId: string) => string;
    /** One line under it. */
    subtitle:     string;
    /** The path of the list below /poi, e.g. /RNs/rn1/CPs. */
    path:         (roamingNetworkId: string) => string;
    /** The section of the roaming network the list is, unless it is the list of the roaming networks. */
    section?:     Segment;
    /** What the search field says while it is empty. */
    placeholder:  string;
    /** The head of the table. */
    head:         TemplateResult;
    /** A row of the table. */
    row:          (entry: T, roamingNetworkId: string) => TemplateResult;
    /** What makes an entry the one it is. */
    key:          (entry: T) => string;
    /** Whether the list can be narrowed to one charging station operator. */
    byOperator?:  boolean;
}


/** A page with a list of the POI API: searched as one types, paged, and its URL kept in the address bar. */
function listPage<T>(Options: ListOptions<T>): Page {

    return {

        title: params => Options.title(params['rn'] ?? ''),

        render({ root, url }) {

            const roamingNetworkId  = roamingNetworkOf(url);
            const query             = queryOf(url);
            const path              = Options.path(roamingNetworkId);

            const content = shell(root, {
                active:    poiHome,
                title:     Options.title(roamingNetworkId),
                subtitle:  Options.subtitle,
                actions:   reloadButton(() => load())
            });

            let part:        ListPart<T> | null       = null;
            let problem:     unknown                  = null;
            let operators:   Summary[]                = [];
            let asking:      AbortController | null   = null;
            let typing:      ReturnType<typeof setTimeout> | undefined;
            let cancelled                             = false;

            function draw(): void {

                const first  = part && part.entries.length > 0 ? query.offset + 1 : 0;
                const last   = part ? query.offset + part.entries.length : 0;

                render(content, html`

                    ${Options.section ? sectionsView(roamingNetworkId, Options.section) : nothing}

                    <div class="log-filters poi-filters">

                        <label class="filter-search">
                            <i class="fa-solid fa-magnifying-glass"></i>
                            <input type="search" id="poi-search" placeholder=${Options.placeholder} autocomplete="off"
                                   .value=${live(query.match)}
                                   @input=${(event: Event) => searchAfterTyping((event.target as HTMLInputElement).value)}
                                   @search=${(event: Event) => searchNow((event.target as HTMLInputElement).value)} />
                        </label>

                        ${Options.byOperator ? html`
                            <label class="filter-level">
                                Operator
                                <select id="poi-operator" @change=${(event: Event) => narrowTo((event.target as HTMLSelectElement).value)}>
                                    <option value="" ?selected=${!query.operatorId}>every operator</option>
                                    ${repeat(operators, operator => operator['@id'], operator => html`
                                        <option value=${operator['@id']} ?selected=${operator['@id'] === query.operatorId}>
                                            ${textOf(operator.name) || operator['@id']}
                                        </option>`)}
                                </select>
                            </label>` : nothing}

                        <label class="filter-level">
                            Per page
                            <select id="poi-limit" @change=${(event: Event) => pageSize(Number((event.target as HTMLSelectElement).value))}>
                                ${pageSizes.map(size => html`<option value=${size} ?selected=${size === query.limit}>${size}</option>`)}
                            </select>
                        </label>

                    </div>

                    <div class="poi-pager">
                        <span id="poi-count" class="muted">${part === null ? '' : countOf(first, last, part)}</span>
                        <button type="button" id="poi-previous" class="btn small" ?disabled=${part === null || query.offset === 0}
                                @click=${() => turnTo(Math.max(0, query.offset - query.limit))}>Previous</button>
                        <button type="button" id="poi-next" class="btn small" ?disabled=${part === null || last >= part.filtered}
                                @click=${() => turnTo(query.offset + query.limit)}>Next</button>
                    </div>

                    ${problem !== null
                        ? html`<div class="error-box">The list could not be loaded: ${errorMessage(problem)}</div>`
                        : part === null
                              ? html`<div class="loading">Loading ...</div>`
                              : part.entries.length === 0
                                    ? html`<div class="hint">${part.total === 0 ? 'There is nothing in this list.' : 'Nothing matches the search.'}</div>`
                                    : html`
                                        <div class="table-scroll">
                                            <table class="table poi-table">
                                                <thead>${Options.head}</thead>
                                                <tbody>${repeat(part.entries, Options.key, entry => Options.row(entry, roamingNetworkId))}</tbody>
                                            </table>
                                        </div>`}

                `);

            }

            async function load(): Promise<void> {

                asking?.abort();
                const ask = asking = new AbortController();

                // The address bar says what is shown: a search or a page of the list is a URL to keep.
                history.replaceState(history.state, '', poiURL(path, { ...query, limit: query.limit === defaultPageSize ? undefined : query.limit }));

                problem = null;
                draw();

                try
                {

                    const [ loaded, operatorList ] = await Promise.all([
                        poiList<T>(path, query, ask.signal),
                        Options.byOperator && operators.length === 0
                            ? poiList<Summary>(`/RNs/${encodeURIComponent(roamingNetworkId)}/CSOs`, {}, ask.signal).then(list => list.entries)
                            : Promise.resolve(operators)
                    ]);

                    if (cancelled || ask !== asking)
                        return;

                    part       = loaded;
                    operators  = operatorList;

                }
                catch (failure)
                {

                    if (cancelled || ask !== asking)
                        return;

                    part     = null;
                    problem  = failure;

                }

                draw();

            }

            function searchAfterTyping(match: string): void {
                clearTimeout(typing);
                typing = setTimeout(() => searchNow(match), typingPause_ms);
            }

            function searchNow(match: string): void {

                clearTimeout(typing);

                if (match === query.match)
                    return;

                query.match   = match;
                query.offset  = 0;
                void load();

            }

            function narrowTo(operatorId: string): void {
                query.operatorId  = operatorId || undefined;
                query.offset      = 0;
                void load();
            }

            function pageSize(limit: number): void {
                query.limit   = limit;
                query.offset  = 0;
                void load();
            }

            function turnTo(offset: number): void {
                query.offset = offset;
                void load();
            }

            void load();

            return () => {
                cancelled = true;
                clearTimeout(typing);
                asking?.abort();
            };

        }

    };

}


/** "1 - 25 of 120", and how many there are at all when a search narrowed them. */
function countOf<T>(first: number, last: number, part: ListPart<T>): string {

    const shown = part.filtered === 0
                      ? 'None'
                      : `${first} - ${last} of ${part.filtered}`;

    return part.filtered === part.total
               ? shown
               : `${shown}, found among ${part.total}`;

}

/** The sections of a roaming network, the one shown marked. */
function sectionsView(roamingNetworkId: string, active: Segment): TemplateResult {

    return html`
        <nav class="poi-sections">
            <a href=${toURL(poiHome)}>Roaming networks</a>
            <span class="muted">/</span>
            <strong>${roamingNetworkId}</strong>
            <span class="muted">/</span>
            ${sections.map(section => html`
                <a href=${toURL(`${poiHome}/${encodeURIComponent(roamingNetworkId)}/${section.segment}`)}
                   class=${section.segment === active ? 'active' : ''}>${section.label}</a>`)}
        </nav>`;

}

/** The address of a charging pool in one line. */
export function addressLine(address: Address | undefined): string {

    if (!address)
        return '';

    const street  = [ address.street, address.houseNumber ].filter(Boolean).join(' ');
    const city    = [ address.postalCode, textOf(address.city) ].filter(Boolean).join(' ');

    return [ street, city, address.country ].filter(Boolean).join(', ');

}

/** The rows of a list in short: its identification, name and description. */
function summaryRow(entry: Summary): TemplateResult {
    return html`
        <tr>
            <td><code>${entry['@id']}</code></td>
            <td>${textOf(entry.name)}</td>
            <td class="muted">${textOf(entry.description)}</td>
        </tr>`;
}

const summaryHead = html`<tr><th>Identification</th><th>Name</th><th>Description</th></tr>`;


/** The roaming networks this hub serves, each leading to its charging pools. */
export const poiNetworksPage: Page = listPage<Summary>({
    title:        () => 'Roaming networks',
    subtitle:     'The points of interest of this hub, by roaming network.',
    path:         () => '/RNs',
    placeholder:  'Search the roaming networks ...',
    head:         summaryHead,
    key:          entry => entry['@id'],
    row:          entry => html`
        <tr>
            <td><a href=${toURL(`${poiHome}/${encodeURIComponent(entry['@id'])}/CPs`)}><code>${entry['@id']}</code></a></td>
            <td>${textOf(entry.name)}</td>
            <td class="muted">${textOf(entry.description)}</td>
        </tr>`
});

/** The charging pools of every operator of a roaming network: what the points of interest are about. */
export const poiChargingPoolsPage: Page = listPage<ChargingPool>({
    title:        roamingNetworkId => `Charging pools${roamingNetworkId ? ` of ${roamingNetworkId}` : ''}`,
    subtitle:     'Every charging pool of every charging station operator.',
    path:         roamingNetworkId => `/RNs/${encodeURIComponent(roamingNetworkId)}/CPs`,
    section:      'CPs',
    placeholder:  'Search names, addresses, operators and identifications ...',
    byOperator:   true,
    head:         html`<tr><th>Identification</th><th>Name</th><th>Operator</th><th>Address</th><th>Stations</th><th>EVSEs</th></tr>`,
    key:          pool => pool['@id'],
    row:          pool => html`
        <tr>
            <td><code>${pool['@id']}</code></td>
            <td>${textOf(pool.name)}</td>
            <td>${textOf(pool.chargingStationOperator.name) || pool.chargingStationOperator['@id']}
                <span class="muted">${pool.chargingStationOperator['@id']}</span></td>
            <td>${addressLine(pool.address)}</td>
            <td class="poi-number">${pool.chargingStations?.length ?? 0}</td>
            <td class="poi-number">${(pool.chargingStations ?? []).reduce((evses, station) => evses + (station.EVSEs?.length ?? 0), 0)}</td>
        </tr>`
});

/** A list in short of a section of a roaming network beside its charging pools. */
function summaryPage(section: Exclude<Segment, 'CPs'>, subtitle: string): Page {

    const label = sections.find(entry => entry.segment === section)!.label;

    return listPage<Summary>({
        title:        roamingNetworkId => `${label}${roamingNetworkId ? ` of ${roamingNetworkId}` : ''}`,
        subtitle,
        path:         roamingNetworkId => `/RNs/${encodeURIComponent(roamingNetworkId)}/${section}`,
        section,
        placeholder:  `Search the ${label.toLowerCase()} ...`,
        head:         summaryHead,
        key:          entry => entry['@id'],
        row:          summaryRow
    });

}

export const poiOperatorsPage:      Page = summaryPage('CSOs', 'Who runs the charging pools.');
export const poiGridOperatorsPage:  Page = summaryPage('GOs',  'Who runs the grids the charging pools are connected to.');
export const poiProvidersPage:      Page = summaryPage('EMPs', 'Who drivers charge with.');
export const poiManufacturersPage:  Page = summaryPage('CSMs', 'Who makes the charging stations.');
export const poiParkingPage:        Page = summaryPage('POs',  'Who runs the parking garages and spaces.');

/** The pages of the points of interest, by their paths: public, as the POI API is. */
export const poiPages: Record<string, Page> = {
    [poiHome]:                  poiNetworksPage,
    [`${poiHome}/:rn/CPs`]:     poiChargingPoolsPage,
    [`${poiHome}/:rn/CSOs`]:    poiOperatorsPage,
    [`${poiHome}/:rn/GOs`]:     poiGridOperatorsPage,
    [`${poiHome}/:rn/EMPs`]:    poiProvidersPage,
    [`${poiHome}/:rn/CSMs`]:    poiManufacturersPage,
    [`${poiHome}/:rn/POs`]:     poiParkingPage
};
