import { api, type PartySegment, type PublicKey } from '../api/client';
import { poiBase, poiList, poiURL, textOf, type Address, type ChargingPool, type Entity, type ListPart, type Query, type Summary } from '../api/poi';
import { auth } from '../auth';
import { fromURL, toURL } from '@node/basePath';
import { must } from '@node/html';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage, field } from '@node/ui';
import { anyFormTypedSinceDrawn, unsaved } from '@node/unsaved';
import { html, live, nothing, render, repeat, type TemplateResult } from '@node/view';

/**
 * The points of interest of this hub - the QUERY interface of its POI API, the roaming networks and what they
 * hold, searched and paged. Every page stands at the URL of its list, /poi/RNs/rn1/CPs say: a browser opening
 * it gets this page, and the page asks the same URL for the data, as JSON. So the URL of the page is the one of
 * the data, and a search or a page of the list is a URL to keep or to pass on.
 *
 * Public: the POI API is readable by everybody, and so are these pages; nobody signs in for them. Only a new
 * roaming network is made, and a party added to one, by somebody signed in who may edit the roaming networks,
 * poi:edit: below their list.
 * The search is the API's: words separated by spaces or commas, each one found in a text of an entry, whatever its case;
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
export const sections = [
    { segment: 'CPs',   label: 'Charging pools',                  icon: 'fa-charging-station'  },
    { segment: 'CSOs',  label: 'Charging station operators',      icon: 'fa-building'          },
    { segment: 'GOs',   label: 'Grid operators',                  icon: 'fa-bolt'              },
    { segment: 'EMPs',  label: 'E-mobility providers',            icon: 'fa-id-card'           },
    { segment: 'CSMs',  label: 'Charging station manufacturers',  icon: 'fa-industry'          },
    { segment: 'EMMs',  label: 'Energy meter manufacturers',      icon: 'fa-gauge-high'        },
    { segment: 'TSMs',  label: 'Transparency software manufacturers',  icon: 'fa-laptop-code'  },
    { segment: 'CABs',  label: 'Conformity assessment bodies',    icon: 'fa-stamp'             },
    { segment: 'POs',   label: 'Parking operators',               icon: 'fa-square-parking'    }
] as const;

export type Segment = typeof sections[number]['segment'];

/** The page of a roaming network, or of an entity below it, by its path below /poi: /RNs/rn1/CSOs/DE*GEF. */
export function entityURL(path: string): string {
    return toURL(`${poiBase}${path}`);
}

/** The path of a list of a roaming network, as the router takes it: /poi/RNs/rn1/CSOs. */
export function listPathOf(roamingNetworkId: string, segment: string): string {
    return `${poiHome}/${encodeURIComponent(roamingNetworkId)}/${segment}`;
}

/** The path of a roaming network below /poi. */
export function networkPath(roamingNetworkId: string): string {
    return `/RNs/${encodeURIComponent(roamingNetworkId)}`;
}


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
    /** The heading; the URL of the page once it is drawn. */
    title:        (roamingNetworkId: string, url?: URL) => string;
    /** One line under it. */
    subtitle:     string;
    /** The path of the list below /poi, e.g. /RNs/rn1/CPs. */
    path:         (roamingNetworkId: string, url: URL) => string;
    /** The section of the roaming network the list is, unless it is the list of the roaming networks. */
    section?:     Segment;
    /** What the search field says while it is empty. */
    placeholder:  string;
    /** The head of the table. */
    head:         TemplateResult;
    /** A row of the table; the path of the list, to lead to the page of an entry below it. */
    row:          (entry: T, roamingNetworkId: string, path: string) => TemplateResult;
    /** What makes an entry the one it is. */
    key:          (entry: T) => string;
    /** Whether the list can be narrowed to one charging station operator. */
    byOperator?:  boolean;
    /** What stands below the list, e.g. a form; given how the page goes to another one. */
    below?:       (navigate: (path: string) => void) => TemplateResult | typeof nothing;
}


/** A page with a list of the POI API: searched as one types, paged, and its URL kept in the address bar. */
function listPage<T>(Options: ListOptions<T>): Page {

    return {

        title: params => Options.title(params['rn'] ?? ''),

        render({ root, url, navigate }) {

            const roamingNetworkId  = roamingNetworkOf(url);
            const query             = queryOf(url);
            const path              = Options.path(roamingNetworkId, url);

            const content = shell(root, {
                active:    poiHome,
                title:     Options.title(roamingNetworkId, url),
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
                                                <tbody>${repeat(part.entries, Options.key, entry => Options.row(entry, roamingNetworkId, path))}</tbody>
                                            </table>
                                        </div>`}

                    ${Options.below ? Options.below(navigate) : nothing}

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

            // What is typed into a form below the list is a draft like any other page's: leaving the page asks
            // first. The search is in no form, and nothing to lose.
            const release = Options.below
                                ? unsaved.heldBy(() => anyFormTypedSinceDrawn(content))
                                : () => undefined;

            void load();

            return () => {
                cancelled = true;
                clearTimeout(typing);
                asking?.abort();
                release();
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

/** The sections of a roaming network, the one shown marked; none on the page of the roaming network. */
export function sectionsView(roamingNetworkId: string, active?: Segment): TemplateResult {

    return html`
        <nav class="poi-sections">
            <a href=${toURL(poiHome)}>Roaming networks</a>
            <span class="muted">/</span>
            <a href=${entityURL(networkPath(roamingNetworkId))} class=${active === undefined ? 'active' : ''}><strong>${roamingNetworkId}</strong></a>
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

/** The rows of a list in short: its identification, leading to its page, name and description. */
function summaryRow(section: Segment, entry: Summary, roamingNetworkId: string): TemplateResult {
    return html`
        <tr>
            <td><a href=${entityURL(`${networkPath(roamingNetworkId)}/${section}/${encodeURIComponent(entry['@id'])}`)}><code>${entry['@id']}</code></a></td>
            <td>${textOf(entry.name)}</td>
            <td class="muted">${textOf(entry.description)}</td>
        </tr>`;
}

const summaryHead = html`<tr><th>Identification</th><th>Name</th><th>Description</th></tr>`;


/**
 * The public keys typed into a field, one per line: the Base64 of a COSE key, or a key as POI writes it,
 * {"key": ...}. Empty lines are passed over.
 */
export function keysOf(text: string, kind: string): PublicKey[] {

    return text.split('\n').
                map(line => line.trim()).
                filter(line => line.length > 0).
                map((line, index) => {

                    if (!line.startsWith('{'))
                        return line;

                    try
                    {
                        return JSON.parse(line) as PublicKey;
                    }
                    catch
                    {
                        throw new Error(`The ${kind} ${index + 1} begins like JSON and is none.`);
                    }

                });

}

/**
 * A new roaming network, for whoever may make one: its identification and its name. Its operator is this hub,
 * with the keys it made at its first start. Made, the page goes to it.
 */
function newNetworkCard(navigate: (path: string) => void): TemplateResult | typeof nothing {

    if (!auth.can('poi', 'edit'))
        return nothing;

    async function make(form: HTMLFormElement): Promise<void> {

        const error = must<HTMLElement>(form, '#network-error');
        error.textContent = '';

        try
        {

            const name  = field(form, 'name');

            const made  = await api.poi.create({
                              '@id':  field(form, 'id'),
                              name:   name ? { en: name } : undefined
                          });

            // Made: what was typed is no draft any more, and the page of the network is where to go on.
            form.reset();
            navigate(`${poiHome}/${encodeURIComponent(made.roamingNetwork['@id'])}`);

        }
        catch (problem)
        {
            error.textContent = errorMessage(problem);
        }

    }

    return html`
        <section class="card wide poi-new-network">

            <h2><i class="fa-solid fa-plus"></i> New roaming network</h2>

            <p class="hint">
                Operated by this hub, which signs its commits with the keys it made at its first start. This hub adds
                its parties below; everything else in the network comes as change sets its owners sign, submitted to
                its history.
            </p>

            <form id="network-form" class="form-stack"
                  @submit=${(event: SubmitEvent) => { event.preventDefault(); void make(event.currentTarget as HTMLFormElement); }}>

                <div class="form-grid">

                    <label>Identification
                        <input type="text" name="id" placeholder="QA" maxlength="64" required autocomplete="off"
                               pattern="[A-Za-z0-9][A-Za-z0-9_\\-]{0,63}" />
                    </label>

                    <label>Name
                        <input type="text" name="name" placeholder="Quality assurance" maxlength="100" />
                    </label>

                </div>

                <div class="form-actions">
                    <button type="submit" class="btn primary">Make the roaming network</button>
                    <span id="network-error" class="form-error" role="alert"></span>
                </div>

            </form>

        </section>`;

}

/** The parties this hub adds: the segment the POI API lists them at, which is where they are added, and what they are called. */
export const partyKinds: ReadonlyArray<{ segment: PartySegment; label: string }> = [
    { segment: 'CSOs',  label: 'Charging station operator' },
    { segment: 'EMPs',  label: 'E-mobility provider'       },
    { segment: 'GOs',   label: 'Grid operator'             },
    { segment: 'POs',   label: 'Parking operator'          },
    { segment: 'CSMs',  label: 'Charging station manufacturer' },
    { segment: 'EMMs',  label: 'Energy meter manufacturer'     },
    { segment: 'TSMs',  label: 'Transparency software manufacturer' },
    { segment: 'CABs',  label: 'Conformity assessment body'         }
];

/** The parties that may come without keys, not taking part (yet): the hub signs in their place. */
const keylessKinds: ReadonlySet<PartySegment> = new Set<PartySegment>([ 'GOs', 'CABs' ]);

/**
 * A new party of a roaming network this hub operates, for whoever may add one: a change set this hub signs. The
 * party keeps its private keys: this hub gets their public halves. Added, the page goes to the list of its kind.
 */
function newPartyCard(navigate: (path: string) => void): TemplateResult | typeof nothing {

    if (!auth.can('poi', 'edit'))
        return nothing;

    async function add(form: HTMLFormElement): Promise<void> {

        const error    = must<HTMLElement>(form, '#party-error');
        const pending  = must<HTMLElement>(form, '#party-pending');
        error.textContent  = '';
        pending.hidden     = true;

        try
        {

            const roamingNetworkId  = field(form, 'roamingNetwork');
            const kind              = partyKinds.find(entry => entry.segment === field(form, 'kind')) ?? partyKinds[0];
            const name              = field(form, 'name');
            const signingKeys       = keysOf(field(form, 'signingKeys'),  'signing key');
            const identityKeys      = keysOf(field(form, 'identityKeys'), 'identity key');

            const added = await api.poi.addParty(roamingNetworkId, kind.segment, {
                '@id':         field(form, 'id'),
                name:          name ? { en: name } : undefined,
                // A grid operator or a conformity assessment body not taking part comes without keys: the hub signs in its place.
                signingKeys:   signingKeys. length > 0 || !keylessKinds.has(kind.segment) ? signingKeys  : undefined,
                identityKeys:  identityKeys.length > 0 || !keylessKinds.has(kind.segment) ? identityKeys : undefined
            });

            form.reset();

            // Added once the party signed the change set with each of its identity keys: until then it waits.
            if (added.status === 'pending')
            {
                pending.textContent  = added.message;
                pending.hidden       = false;
                return;
            }

            navigate(listPathOf(roamingNetworkId, kind.segment));

        }
        catch (problem)
        {
            error.textContent = errorMessage(problem);
        }

    }

    return html`
        <section class="card wide poi-new-party">

            <h2><i class="fa-solid fa-user-plus"></i> New party of a roaming network</h2>

            <p class="hint">
                Added by this hub as the operator of the roaming network, with a change set it signs. The party keeps
                its private keys: this hub gets their public halves, one per line, each the Base64 of its COSE key or
                a key as POI writes it, <code>{"key": ...}</code>. It is added once it signed the change set with each
                of its identity keys, as the proof that it holds them; until then the change set waits. Its data is its
                own to add afterwards.
            </p>

            <form id="party-form" class="form-stack"
                  @submit=${(event: SubmitEvent) => { event.preventDefault(); void add(event.currentTarget as HTMLFormElement); }}>

                <div class="form-grid">

                    <label>Roaming network
                        <input type="text" name="roamingNetwork" placeholder="QA" maxlength="64" required autocomplete="off" />
                    </label>

                    <label>Party
                        <select name="kind">
                            ${partyKinds.map(kind => html`<option value=${kind.segment}>${kind.label}</option>`)}
                        </select>
                    </label>

                    <label>Identification
                        <input type="text" name="id" placeholder="DE*QAC" maxlength="64" required autocomplete="off" />
                    </label>

                    <label>Name
                        <input type="text" name="name" placeholder="QA Charging" maxlength="100" />
                    </label>

                </div>

                <label>Signing keys
                    <textarea name="signingKeys" rows="2" spellcheck="false" placeholder="pAECIAEhWCD..."></textarea>
                </label>

                <label>Identity keys
                    <textarea name="identityKeys" rows="3" spellcheck="false" placeholder="pAECIAMhWEIB..."></textarea>
                    <span class="hint">
                        At least one of each; for an identity key, two with different validities, one of them kept
                        offline. A grid operator not taking part comes without keys.
                    </span>
                </label>

                <div class="form-actions">
                    <button type="submit" class="btn primary">Add the party</button>
                    <span id="party-error" class="form-error" role="alert"></span>
                </div>

                <p id="party-pending" class="notice" role="status" hidden></p>

            </form>

        </section>`;

}

/** The roaming networks this hub serves, each leading to its page; and a new one, for whoever may make it. */
export const poiNetworksPage: Page = listPage<Summary>({
    title:        () => 'Roaming networks',
    subtitle:     'The points of interest of this hub, by roaming network.',
    path:         () => '/RNs',
    placeholder:  'Search the roaming networks ...',
    head:         summaryHead,
    key:          entry => entry['@id'],
    row:          entry => html`
        <tr>
            <td><a href=${entityURL(networkPath(entry['@id']))}><code>${entry['@id']}</code></a></td>
            <td>${textOf(entry.name)}</td>
            <td class="muted">${textOf(entry.description)}</td>
        </tr>`,
    below:        navigate => html`${newNetworkCard(navigate)}${newPartyCard(navigate)}`
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
    row:          (pool, roamingNetworkId) => {

        const operatorPath = `${networkPath(roamingNetworkId)}/CSOs/${encodeURIComponent(pool.chargingStationOperator['@id'])}`;

        return html`
        <tr>
            <td><a href=${entityURL(`${operatorPath}/CPs/${encodeURIComponent(pool['@id'])}`)}><code>${pool['@id']}</code></a></td>
            <td>${textOf(pool.name)}</td>
            <td><a href=${entityURL(operatorPath)}>${textOf(pool.chargingStationOperator.name) || pool.chargingStationOperator['@id']}</a>
                <span class="muted">${pool.chargingStationOperator['@id']}</span></td>
            <td>${addressLine(pool.address)}</td>
            <td class="poi-number">${pool.chargingStations?.length ?? 0}</td>
            <td class="poi-number">${evseCountOf(pool)}</td>
        </tr>`;

    }
});

/** How many EVSEs the stations of a charging pool have. */
export function evseCountOf(pool: Pick<ChargingPool, 'chargingStations'>): number {
    return (pool.chargingStations ?? []).reduce((evses, station) => evses + (station.EVSEs?.length ?? 0), 0);
}

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
        row:          (entry, roamingNetworkId) => summaryRow(section, entry, roamingNetworkId)
    });

}

export const poiOperatorsPage:      Page = summaryPage('CSOs', 'Who runs the charging pools.');
export const poiGridOperatorsPage:  Page = summaryPage('GOs',  'Who runs the grids the charging pools are connected to.');
export const poiProvidersPage:      Page = summaryPage('EMPs', 'Who drivers charge with.');
export const poiManufacturersPage:  Page = summaryPage('CSMs', 'Who makes the charging stations, with their models.');
export const poiMeterMakersPage:    Page = summaryPage('EMMs', 'Who makes the energy meters, with their models.');
export const poiParkingPage:        Page = summaryPage('POs',  'Who runs the parking garages and spaces.');
export const poiSoftwareMakersPage: Page = summaryPage('TSMs', 'Who makes the transparency software checking the meter values, with its releases.');
export const poiAssessmentPage:     Page = summaryPage('CABs', 'Who certifies which transparency software checks the meters of which charging stations.');

/** What an operator embeds and the API lists below it, e.g. /poi/RNs/rn1/GOs/DE*NET/GCPs: what of each a table shows. */
export interface EmbeddedCollection {
    /** The segment of its list below the operator. */
    segment:  string;
    /** The field the operator embeds them in. */
    field:    string;
    label:    string;
    /** What one of them is called. */
    one:      string;
    icon:     string;
    head:     string[];
    cells:    (entry: Entity) => (string | number)[];
}

/** How many identifications a list of them has. */
const idCount = (ids: unknown): number => Array.isArray(ids) ? ids.length : 0;

/** The grid connection points of a grid operator. */
export const gridConnectionPoints: EmbeddedCollection = {
    segment: 'GCPs',  field: 'gridConnectionPoints',  label: 'Grid connection points',  one: 'Grid connection point',  icon: 'fa-plug-circle-bolt',
    head:    [ 'Voltage level', 'Address' ],
    cells:   point => [ String(point.voltageLevel ?? ''), addressLine(point.address as Address | undefined) ]
};

/**
 * The templates of the energy meters of a charging station operator, of its pools, stations and EVSEs, or of a grid
 * operator, of its grid connection points: giving the meters naming them what a series of meters has in common.
 */
export const energyMeterTemplates: EmbeddedCollection = {
    segment: 'EMTs',  field: 'energyMeterTemplates',  label: 'Energy meter templates',  one: 'Energy meter template',  icon: 'fa-gauge',
    head:    [ 'Manufacturer', 'Model' ],
    cells:   template => [ String(template.manufacturerId ?? ''), String(template.modelId ?? '') ]
};

/** The groups of energy meters of a charging station operator or of a grid operator: collections, with a status of their own. */
export const energyMeterGroups: EmbeddedCollection = {
    segment: 'EMGs',  field: 'energyMeterGroups',  label: 'Energy meter groups',  one: 'Energy meter group',  icon: 'fa-layer-group',
    head:    [ 'Meters' ],
    cells:   group => [ idCount(group.energyMeterIds) ]
};

/** What a grid operator embeds, each in a list of its own. */
export const gridCollections: EmbeddedCollection[] = [ gridConnectionPoints, energyMeterTemplates, energyMeterGroups ];

/** The models of a charging station manufacturer: what its charging stations are. */
export const chargingStationModels: EmbeddedCollection = {
    segment: 'models',  field: 'chargingStationModels',  label: 'Models',  one: 'Charging station model',  icon: 'fa-cube',
    head:    [ 'Versions', 'Max. power' ],
    cells:   model => [ ((model.versions as string[] | undefined) ?? []).join(', '), model.maxPower !== undefined ? `${Number(model.maxPower) / 1000} kW` : '' ]
};

/** The models of an energy meter manufacturer: what its energy meters are. */
export const energyMeterModels: EmbeddedCollection = {
    segment: 'models',  field: 'energyMeterModels',  label: 'Models',  one: 'Energy meter model',  icon: 'fa-cube',
    head:    [ 'Versions', 'Accuracy class' ],
    cells:   model => [ ((model.versions as string[] | undefined) ?? []).join(', '), String(model.accuracyClass ?? '') ]
};

/** The releases of a transparency software manufacturer: one per version of a software. */
export const transparencySoftwareReleases: EmbeddedCollection = {
    segment: 'TSs',  field: 'transparencySoftware',  label: 'Releases',  one: 'Transparency software release',  icon: 'fa-code-branch',
    head:    [ 'Version', 'Supported until' ],
    cells:   release => [ String(release.version ?? ''), String(release.supportUntil ?? '') ]
};

/** The transparency software certificates a conformity assessment body issued: for a charging station model each. */
export const transparencySoftwareCertificates: EmbeddedCollection = {
    segment: 'TSCs',  field: 'transparencySoftwareCertificates',  label: 'Certificates',  one: 'Transparency software certificate',  icon: 'fa-certificate',
    head:    [ 'Charging station model', 'Legislation' ],
    cells:   certificate => [ `${String(certificate.chargingStationModel ?? '')} ${String(certificate.chargingStationModelVersion ?? '')}`.trim(), String(certificate.legislation ?? '') ]
};

/** What a parking operator embeds, each in a list of its own. */
export const parkingCollections: EmbeddedCollection[] = [
    { segment: 'PGs',   field: 'parkingGarages',      label: 'Parking garages',       one: 'Parking garage',       icon: 'fa-warehouse',
      head:    [ 'Address', 'Charging stations' ],
      cells:   garage => [ addressLine(garage.address as Address | undefined), idCount(garage.chargingStationIds) ] },
    { segment: 'PSs',   field: 'parkingSpaces',       label: 'Parking spaces',        one: 'Parking space',        icon: 'fa-square-parking',
      head:    [ 'Garage', 'Sensors', 'Charging stations' ],
      cells:   space => [ String(space.parkingGarageId ?? ''), idCount(space.parkingSensorIds), idCount(space.chargingStationIds) ] },
    { segment: 'PSTs',  field: 'parkingSpaceTemplates',  label: 'Parking space templates',  one: 'Parking space template',  icon: 'fa-clone',
      head:    [ 'Garage', 'Sensors' ],
      cells:   template => [ String(template.parkingGarageId ?? ''), idCount(template.parkingSensorIds) ] },
    { segment: 'PSGs',  field: 'parkingSpaceGroups',  label: 'Parking space groups',  one: 'Parking space group',  icon: 'fa-layer-group',
      head:    [ 'Spaces' ],
      cells:   group => [ idCount(group.parkingSpaceIds) ] },
    { segment: 'PSNs',  field: 'parkingSensors',      label: 'Parking sensors',       one: 'Parking sensor',       icon: 'fa-satellite-dish',
      head:    [ 'Charging stations' ],
      cells:   sensor => [ idCount(sensor.chargingStationIds) ] },
    { segment: 'PPs',   field: 'parkingProducts',     label: 'Parking products',      one: 'Parking product',      icon: 'fa-ticket',
      head:    [ 'Description' ],
      cells:   product => [ textOf(product.description as Summary['description']) ] }
];

/** The groups of EVSEs, charging stations and charging pools, of a charging station operator or an e-mobility provider: collections. */
export const evseGroups: EmbeddedCollection = {
    segment: 'EVSEGs',  field: 'EVSEGroups',             label: 'EVSE groups',              one: 'EVSE group',              icon: 'fa-object-group',
    head:    [ 'EVSEs' ],
    cells:   group => [ idCount(group.EVSEIds) ]
};

export const chargingStationGroups: EmbeddedCollection = {
    segment: 'CSGs',    field: 'chargingStationGroups',  label: 'Charging station groups',  one: 'Charging station group',  icon: 'fa-object-group',
    head:    [ 'Stations' ],
    cells:   group => [ idCount(group.chargingStationIds) ]
};

export const chargingPoolGroups: EmbeddedCollection = {
    segment: 'CPGs',    field: 'chargingPoolGroups',     label: 'Charging pool groups',     one: 'Charging pool group',     icon: 'fa-object-group',
    head:    [ 'Pools' ],
    cells:   group => [ idCount(group.chargingPoolIds) ]
};

/** What a charging tariff offer gives: a price of its own, or a discount. */
export function offered(offer: Entity): string {
    const discount = offer.discount as { percent?: number } | undefined;
    const price    = offer.chargingTariff as { currency?: string } | undefined;
    return discount?.percent !== undefined ? `${discount.percent} % less` : price?.currency !== undefined ? `a price in ${price.currency}` : '';
}

/** The charging tariff offers of a charging station operator or an e-mobility provider: another price for a time. */
export const chargingTariffOffers: EmbeddedCollection = {
    segment: 'CTOs',    field: 'chargingTariffOffers',   label: 'Charging tariff offers',   one: 'Charging tariff offer',   icon: 'fa-percent',
    head:    [ 'Offer', 'Not before', 'Not after' ],
    cells:   offer => [ offered(offer), String(offer.notBefore ?? ''), String(offer.notAfter ?? '') ]
};

/** The charging tariffs, the templates, the groups and the offers of a charging station operator, each in a list of its own. */
export const operatorCollections: EmbeddedCollection[] = [
    { segment: 'CTs',     field: 'chargingTariffs',          label: 'Charging tariffs',           one: 'Charging tariff',           icon: 'fa-tag',
      head:    [ 'Currency', 'Type', 'Elements' ],
      cells:   tariff => [ String(tariff.currency ?? ''), String(tariff.tariffType ?? ''), idCount(tariff.elements) ] },
    { segment: 'EVSETs',  field: 'EVSETemplates',            label: 'EVSE templates',             one: 'EVSE template',             icon: 'fa-clone',
      head:    [ 'Max power', 'Tariff' ],
      cells:   template => [ String(template.maxPower ?? ''), String(template.chargingTariffId ?? '') ] },
    { segment: 'CSTs',    field: 'chargingStationTemplates', label: 'Charging station templates', one: 'Charging station template', icon: 'fa-clone',
      head:    [ 'Tariff' ],
      cells:   template => [ String(template.chargingTariffId ?? '') ] },
    { segment: 'CPTs',    field: 'chargingPoolTemplates',    label: 'Charging pool templates',    one: 'Charging pool template',    icon: 'fa-clone',
      head:    [ 'Tariff' ],
      cells:   template => [ String(template.chargingTariffId ?? '') ] },
    { segment: 'CTTs',    field: 'chargingTariffTemplates',  label: 'Charging tariff templates',  one: 'Charging tariff template',  icon: 'fa-clone',
      head:    [ 'Currency', 'Elements' ],
      cells:   template => [ String(template.currency ?? ''), idCount(template.elements) ] },
    energyMeterTemplates,
    { segment: 'CTGs',    field: 'chargingTariffGroups',     label: 'Charging tariff groups',     one: 'Charging tariff group',     icon: 'fa-tags',
      head:    [ 'Tariffs' ],
      cells:   group => [ idCount(group.chargingTariffIds) ] },
    chargingPoolGroups,
    chargingStationGroups,
    evseGroups,
    energyMeterGroups,
    chargingTariffOffers
];

/** The groups and the offers of an e-mobility provider, each in a list of its own: of members of any operator. */
export const providerCollections: EmbeddedCollection[] = [ evseGroups, chargingStationGroups, chargingPoolGroups, chargingTariffOffers ];

/** The party a page below it is of, from its URL: /poi/RNs/{RN_Id}/{CSOs|GOs|POs|EMPs|CSMs|EMMs|TSMs|CABs}/{Id}/... */
function operatorOf(url: URL, segment: 'CSOs' | 'GOs' | 'POs' | 'EMPs' | 'CSMs' | 'EMMs' | 'TSMs' | 'CABs'): string {
    const found = new RegExp(`^/poi/RNs/[^/]+/${segment}/([^/]+)`).exec(fromURL(url.pathname));
    return found ? decodeURIComponent(found[1]!) : '';
}

/** The grid operator a page of it is of, from its URL: /poi/RNs/{RN_Id}/GOs/{GO_Id}/... */
export function gridOperatorOf(url: URL): string {
    return operatorOf(url, 'GOs');
}

/** All of what an operator embeds, beyond the first its page shows, searched and paged: each leading to its page. */
function embeddedListPage(parent:      'CSOs' | 'GOs' | 'POs' | 'EMPs' | 'CSMs' | 'EMMs' | 'TSMs' | 'CABs',
                          collection:  EmbeddedCollection,
                          subtitle:    string): Page {

    return listPage<Entity>({
        title:        (_, url) => `${collection.label}${url ? ` of ${operatorOf(url, parent)}` : ''}`,
        subtitle,
        path:         (roamingNetworkId, url) => `${networkPath(roamingNetworkId)}/${parent}/${encodeURIComponent(operatorOf(url, parent))}/${collection.segment}`,
        section:      parent,
        placeholder:  `Search the ${collection.label.toLowerCase()} ...`,
        head:         html`<tr><th>Identification</th><th>Name</th>${collection.head.map(label => html`<th>${label}</th>`)}</tr>`,
        key:          entry => entry['@id'],
        row:          (entry, _, path) => html`
            <tr>
                <td><a href=${entityURL(`${path}/${encodeURIComponent(entry['@id'])}`)}><code>${entry['@id']}</code></a></td>
                <td>${textOf(entry.name)}</td>
                ${collection.cells(entry).map(cell => html`<td class=${typeof cell === 'number' ? 'poi-number' : ''}>${cell}</td>`)}
            </tr>`
    });

}

/** The grid connection points of a grid operator, as they are at runtime: all of them, beyond the first its page shows. */
export const poiGridConnectionPointsPage: Page = embeddedListPage('GOs', gridConnectionPoints, 'Where the charging pools are connected to the grid of a grid operator.');

/** The templates of the energy meters of the grid connection points of a grid operator. */
export const poiGridEnergyMeterTemplatesPage: Page = embeddedListPage('GOs', energyMeterTemplates, 'The templates of the energy meters of the grid connection points of a grid operator.');

/** The groups of the energy meters of the grid connection points of a grid operator. */
export const poiGridEnergyMeterGroupsPage: Page = embeddedListPage('GOs', energyMeterGroups, 'The groups of the energy meters of the grid connection points of a grid operator.');

/** The models of a charging station manufacturer, all of them. */
export const poiChargingStationModelsPage: Page = embeddedListPage('CSMs', chargingStationModels, 'The models of a charging station manufacturer, which its charging stations name.');

/** The models of an energy meter manufacturer, all of them. */
export const poiEnergyMeterModelsPage: Page = embeddedListPage('EMMs', energyMeterModels, 'The models of an energy meter manufacturer, which its energy meters name.');

/** The releases of a transparency software manufacturer, all of them. */
export const poiReleasesPage: Page = embeddedListPage('TSMs', transparencySoftwareReleases, 'The releases of a transparency software manufacturer.');

/** The certificates of a conformity assessment body, all of them. */
export const poiCertificatesPage: Page = embeddedListPage('CABs', transparencySoftwareCertificates, 'The transparency software certificates a conformity assessment body issued.');

/** The groups and the offers of an e-mobility provider, a list each. */
export const poiProviderListPages: Record<string, Page> = Object.fromEntries(
    providerCollections.map(collection => [ `${poiHome}/:rn/EMPs/:emp/${collection.segment}`,
                                            embeddedListPage('EMPs', collection, `The ${collection.label.toLowerCase()} of an e-mobility provider.`) ])
);

/** The charging tariffs, the templates, the groups and the offers of a charging station operator, a list each. */
export const poiOperatorListPages: Record<string, Page> = Object.fromEntries(
    operatorCollections.map(collection => [ `${poiHome}/:rn/CSOs/:cso/${collection.segment}`,
                                            embeddedListPage('CSOs', collection, `The ${collection.label.toLowerCase()} of a charging station operator.`) ])
);

/** What a parking operator embeds, a list each. */
export const poiParkingListPages: Record<string, Page> = Object.fromEntries(
    parkingCollections.map(collection => [ `${poiHome}/:rn/POs/:po/${collection.segment}`,
                                           embeddedListPage('POs', collection, `The ${collection.label.toLowerCase()} of a parking operator.`) ])
);

/** The pages of the lists of the points of interest, by their paths: public, as the POI API is. */
export const poiListPages: Record<string, Page> = {
    [poiHome]:                          poiNetworksPage,
    [`${poiHome}/:rn/CPs`]:             poiChargingPoolsPage,
    [`${poiHome}/:rn/CSOs`]:            poiOperatorsPage,
    [`${poiHome}/:rn/GOs`]:             poiGridOperatorsPage,
    [`${poiHome}/:rn/EMPs`]:            poiProvidersPage,
    [`${poiHome}/:rn/CSMs`]:            poiManufacturersPage,
    [`${poiHome}/:rn/EMMs`]:            poiMeterMakersPage,
    [`${poiHome}/:rn/POs`]:             poiParkingPage,
    [`${poiHome}/:rn/TSMs`]:            poiSoftwareMakersPage,
    [`${poiHome}/:rn/CABs`]:            poiAssessmentPage,
    [`${poiHome}/:rn/CSMs/:csm/models`]:  poiChargingStationModelsPage,
    [`${poiHome}/:rn/EMMs/:emm/models`]:  poiEnergyMeterModelsPage,
    [`${poiHome}/:rn/TSMs/:tsm/TSs`]:   poiReleasesPage,
    [`${poiHome}/:rn/CABs/:cab/TSCs`]:  poiCertificatesPage,
    [`${poiHome}/:rn/GOs/:go/GCPs`]:    poiGridConnectionPointsPage,
    [`${poiHome}/:rn/GOs/:go/EMTs`]:    poiGridEnergyMeterTemplatesPage,
    [`${poiHome}/:rn/GOs/:go/EMGs`]:    poiGridEnergyMeterGroupsPage,
    ...poiOperatorListPages,
    ...poiProviderListPages,
    ...poiParkingListPages
};
