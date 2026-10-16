import { poiBase, poiEntity, poiList, textOf, type Address, type ChargingPool, type Entity, type I18N, type Loaded, type Query, type Summary } from '../api/poi';
import { fromURL, toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage, formatValue, humanizeKey } from '@node/ui';
import { html, nothing, render, type TemplateResult } from '@node/view';
import { addressLine, entityURL, evseCountOf, gridConnectionPoints, networkPath, parkingCollections, poiHome, roamingNetworkOf, sections, sectionsView, type EmbeddedCollection } from './poi';

/**
 * The single things of the points of interest: a roaming network with what it holds, and every entity below it -
 * a charging station operator down to an EVSE, a grid operator and its grid connection points, an e-mobility
 * provider, a charging station manufacturer and a parking operator. Each page stands at the URL of its entity,
 * /poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1 say, and asks the same URL for it, as JSON.
 *
 * What an entity has is drawn as the API sends it, field by field, so a field the API learns shows up here without
 * a change; only what reads better otherwise - a multi-language text, an address, a place on the map, a power in
 * kilowatts, a link to another entity - is drawn so. Its children are a table, each leading to its page.
 *
 * A charging station operator is asked for without its charging pools, which may be thousands, each with its
 * stations and EVSEs: its table holds the first of them, from the charging pools of the roaming network. So is a
 * grid operator without its grid connection points, the first of them from its list of them, and a parking operator
 * without its garages, spaces, groups of spaces, sensors and products, the first of each from their lists. Each of
 * them has a page of its own, below its parking operator, and a parking space leads to its garage, sensors and
 * products, as a group to its spaces.
 */


/** How many children a table shows: the rest are found by searching, or in the JSON. */
const childrenShown = 100;

/** A step of the path of an entity: the segment of its kind, its identification, and its path below /poi. */
export interface Step {
    segment:  string;
    id:       string;
    path:     string;
}

/** The children of a kind of entity: the field the API sends them in, and what of each is shown in a table. */
interface Children {
    field:    string;
    label:    string;
    segment:  string;
    /** The icon of their table; the one of their kind by default. */
    icon?:    string;
    head:     string[];
    cells:    (child: Entity) => (string | number | TemplateResult)[];
    /** Where all of them are, searched and paged, when there are more than a table shows. */
    all?:     (steps: Step[]) => string;
    /** The list they are asked from instead, when the entity is asked for without them. */
    listed?:  (steps: Step[]) => { path: string; query: Query };
}

/** The children a table shows, and how many there are. */
interface ChildrenPart {
    entries:  Entity[];
    count:    number;
}

/** What an operator embeds, asked from its list below the operator: the first of them, and where all of them are. */
function listedChildren(collection: EmbeddedCollection): Children {
    return {
        field:    collection.field,
        label:    collection.label,
        segment:  collection.segment,
        icon:     collection.icon,
        head:     collection.head,
        cells:    collection.cells,
        all:      steps => entityURL(`${steps[1]!.path}/${collection.segment}`),
        listed:   steps => ({ path: `${steps[1]!.path}/${collection.segment}`, query: { limit: childrenShown } })
    };
}

/** A kind of entity, by the segment of its path. */
interface Kind {
    label:        string;
    icon:         string;
    children?:    Children[];
    /** What the API is asked beyond the path, e.g. to embed what the entity only names. */
    parameters?:  Record<string, string>;
}

const kinds: Record<string, Kind> = {

    CSOs:   { label: 'Charging station operator',  icon: 'fa-building',
              children: [ { field: 'chargingPools',  label: 'Charging pools',  segment: 'CPs',
                            head:  [ 'Address', 'Stations', 'EVSEs' ],
                            cells: pool => [ addressLine(pool.address as Address | undefined), (pool.chargingStations as unknown[] | undefined)?.length ?? 0, evseCountOf(pool as Pick<ChargingPool, 'chargingStations'>) ],
                            all:     steps => `${entityURL(`${steps[0]!.path}/CPs`)}?operatorId=${encodeURIComponent(steps[1]!.id)}`,
                            listed:  steps => ({ path: `${steps[0]!.path}/CPs`, query: { operatorId: steps[1]!.id, limit: childrenShown } }) } ],
              parameters: { without: 'chargingPools' } },

    CPs:    { label: 'Charging pool',  icon: 'fa-charging-station',
              parameters: { expand: 'gridConnectionPoint' },
              children: [ { field: 'chargingStations',  label: 'Charging stations',  segment: 'CSs',
                            head:  [ 'EVSEs', 'Max power' ],
                            cells: station => [ (station.EVSEs as unknown[] | undefined)?.length ?? 0, withUnit('maxPower', station.maxPower) ] } ] },

    CSs:    { label: 'Charging station',  icon: 'fa-charging-station',
              children: [ { field: 'EVSEs',  label: 'EVSEs',  segment: 'EVSEs',
                            head:  [ 'Max power', 'Connectors' ],
                            cells: evse => [ withUnit('maxPower', evse.maxPower),
                                             ((evse.chargingConnectors as { type?: string }[] | undefined) ?? []).map(connector => connector.type ?? '?').join(', ') ] } ] },

    EVSEs:  { label: 'EVSE',  icon: 'fa-plug' },

    GOs:    { label: 'Grid operator',  icon: 'fa-bolt',
              children:   [ listedChildren(gridConnectionPoints) ],
              parameters: { without: gridConnectionPoints.field } },

    GCPs:   { label: 'Grid connection point',          icon: 'fa-plug-circle-bolt' },
    EMPs:   { label: 'E-mobility provider',            icon: 'fa-id-card'          },
    CSMs:   { label: 'Charging station manufacturer',  icon: 'fa-industry'         },
    POs:    { label: 'Parking operator',               icon: 'fa-square-parking',
              children:   parkingCollections.map(collection => listedChildren(collection)),
              parameters: { without: parkingCollections.map(collection => collection.field).join(',') } },

    // What a parking operator embeds, each below it.
    ...Object.fromEntries(parkingCollections.map(collection => [ collection.segment, { label: collection.one, icon: collection.icon } ]))

};

/** The fields of what a parking operator embeds naming other things of it, by the segment of their list. */
const parkingReferences: Record<string, string> = {
    parkingGarageId:    'PGs',
    parkingSpaceIds:    'PSs',
    parkingSensorIds:   'PSNs',
    parkingProductIds:  'PPs'
};

/** Not drawn as fields: what says how to read the JSON, not what the entity is. */
const notAField = new Set([ '@context' ]);

/** The units of the numbers the API sends without one, and what reads better: kilowatts rather than watts. */
const units: Record<string, { unit: string; factor?: number }> = {
    maxPower:                       { unit: 'kW',   factor: 1000 },
    contractedImportPower:          { unit: 'kW',   factor: 1000 },
    contractedExportPower:          { unit: 'kW',   factor: 1000 },
    contractedImportApparentPower:  { unit: 'kVA',  factor: 1000 },
    contractedExportApparentPower:  { unit: 'kVA',  factor: 1000 },
    maxVoltage:                     { unit: 'V' },
    nominalVoltage:                 { unit: 'V' },
    maxCurrent:                     { unit: 'A' },
    nominalFrequency:               { unit: 'Hz' }
};


/** The steps of the path of a page below /poi: /poi/RNs/rn1/CSOs/DE*GEF is [ RNs rn1, CSOs DE*GEF ]. */
export function stepsOf(url: URL): Step[] {

    const parts  = fromURL(url.pathname).slice(poiBase.length).replace(/\/$/, '').split('/').slice(1);
    const steps  = [] as Step[];
    let   path   = '';

    for (let at = 0; at + 1 < parts.length; at += 2)
    {
        path += `/${parts[at]}/${parts[at + 1]}`;
        steps.push({ segment: parts[at]!, id: decodeURIComponent(parts[at + 1]!), path });
    }

    return steps;

}

/** A number of a field with its unit, if it has a known one. */
export function withUnit(key: string, value: unknown): string {

    const unit = units[key];

    if (typeof value !== 'number' || unit === undefined)
        return formatValue(value);

    return `${(value / (unit.factor ?? 1)).toLocaleString([], { maximumFractionDigits: 2 })} ${unit.unit}`;

}

/** Whether a value is a multi-language text: { "de": "...", "en": "..." }. */
function isI18N(value: unknown): value is I18N {
    return typeof value === 'object' && value !== null && !Array.isArray(value) &&
           Object.keys(value).length > 0 &&
           Object.entries(value).every(([ language, text ]) => /^[a-z]{2,3}(-[A-Za-z0-9]{2,8})?$/.test(language) && typeof text === 'string');
}

/** Whether a value is a thing of its own, or a list of things, to be drawn as a block below its name. */
function thingsIn(value: unknown): Record<string, unknown>[] | null {

    if (Array.isArray(value))
        return value.some(item => typeof item === 'object' && item !== null)
                   ? value as Record<string, unknown>[]
                   : null;

    if (typeof value === 'object' && value !== null && Object.keys(value).length > 0)
        return [ value as Record<string, unknown> ];

    return null;

}

/** A link away from this hub, opened beside it. */
function away(href: string, text: string): TemplateResult {
    return html`<a href=${href} target="_blank" rel="noopener noreferrer">${text}</a>`;
}


/** The value of a field, drawn as it reads best; null for one that is a block of fields of its own. */
function valueView(key: string, value: unknown, steps: Step[]): TemplateResult | string | null {

    const network = steps[0]?.path ?? '';

    if (key === '@id' && typeof value === 'string')
        return html`<code>${value}</code>`;

    if (key === 'address' && typeof value === 'object' && value !== null && !Array.isArray(value))
        return addressLine(value as Address) || formatValue(value);

    if (key === 'geoLocation' && typeof value === 'object' && value !== null &&
        typeof (value as { lat?: unknown }).lat === 'number' && typeof (value as { lng?: unknown }).lng === 'number')
    {
        const { lat, lng } = value as { lat: number; lng: number };
        return away(`https://www.openstreetmap.org/?mlat=${lat}&mlon=${lng}#map=18/${lat}/${lng}`, `${lat}, ${lng}`);
    }

    // Embedded where the charging pool only names it: with its grid operator, so it has a page to lead to.
    if (key === 'gridConnectionPoint' && typeof value === 'object' && value !== null)
    {
        const point = value as Entity & { gridOperatorId?: string };
        if (typeof point['@id'] === 'string' && typeof point.gridOperatorId === 'string')
            return html`<a href=${entityURL(`${network}/GOs/${encodeURIComponent(point.gridOperatorId)}/GCPs/${encodeURIComponent(point['@id'])}`)}>${textOf(point.name) || point['@id']}</a>
                        <span class="muted"><code>${point['@id']}</code> of <code>${point.gridOperatorId}</code></span>`;
    }

    if (key === 'manufacturerId' && typeof value === 'string')
        return html`<a href=${entityURL(`${network}/CSMs/${encodeURIComponent(value)}`)}><code>${value}</code></a>`;

    // What a thing of a parking operator names of it: its garage, sensors, products, or the spaces of a group.
    if (steps[1]?.segment === 'POs' && parkingReferences[key] !== undefined &&
        (typeof value === 'string' || (Array.isArray(value) && value.every(item => typeof item === 'string'))))
    {
        const below = `${steps[1].path}/${parkingReferences[key]}`;
        return html`${([] as string[]).concat(value as string | string[]).map((id, index) => html`${index > 0 ? ', ' : ''}<a href=${entityURL(`${below}/${encodeURIComponent(id)}`)}><code>${id}</code></a>`)}`;
    }

    if (isI18N(value))
        return html`${Object.entries(value).map(([ language, text ]) => html`<div>${text} <span class="muted small">${language}</span></div>`)}`;

    // The keys of a party: by their identifications, the keys themselves are in the JSON.
    if (Array.isArray(value) && value.length > 0 && value.every(item => typeof item === 'object' && item !== null && '@id' in item && 'key' in item))
        return html`${value.map(item => html`<div><code class="poi-key">${(item as { '@id': string })['@id']}</code></div>`)}`;

    if (typeof value === 'string' && /^https?:\/\/\S+$/.test(value))
        return away(value, value);

    if ((key.endsWith('Id') || key.endsWith('Ids')) && (typeof value === 'string' || (Array.isArray(value) && value.every(item => typeof item === 'string'))))
        return html`${([] as string[]).concat(value as string | string[]).map((id, index) => html`${index > 0 ? ', ' : ''}<code>${id}</code>`)}`;

    if (units[key] !== undefined)
        return withUnit(key, value);

    return thingsIn(value) === null
               ? formatValue(value)
               : null;

}

/** The fields first, and last, of an entity: the API sends them in the order of their names. */
const firstFields  = [ '@id', 'name', 'description' ];
const lastFields   = [ 'created', 'lastChange' ];

/** The fields in the order they are read: what it is first, when it changed last, and the rest as sent between. */
export function inReadingOrder(fields: [ string, unknown ][]): [ string, unknown ][] {

    const rank = (key: string) => firstFields.includes(key)
                                      ? firstFields.indexOf(key) - firstFields.length
                                      : lastFields.includes(key)
                                            ? lastFields.indexOf(key) + 1
                                            : 0;

    // Stable: the fields between keep the order they were sent in.
    return [ ...fields ].sort(([ a ], [ b ]) => rank(a) - rank(b));

}

/** The name of a field: "@id" is the identification, of a thing inside an entity as well. */
function labelOf(key: string): string {
    return key === '@id' ? 'Identification' : humanizeKey(key);
}

/** The fields of an entity, or of a thing in it, a line each, and a thing of its own as a block below its name. */
function fieldsView(fields: [ string, unknown ][], steps: Step[]): TemplateResult[] {

    return inReadingOrder(fields).map(([ key, value ]) => {

        const shown = valueView(key, value, steps);

        return shown !== null
            ? html`
                <div class="kv">
                    <span class="k">${labelOf(key)}</span>
                    <span class="poi-value">${shown}</span>
                </div>`
            : html`
                <div class="kv-nested">
                    <span class="k">${labelOf(key)}</span>
                    <div class="nested">
                        ${thingsIn(value)!.map(thing => html`
                            <div class="nested-item">
                                ${fieldsView(Object.entries(thing), steps)}
                            </div>`)}
                    </div>
                </div>`;

    });

}

/**
 * Where an entity is: the roaming networks, its roaming network, the list it is in, and every entity above it,
 * each leading to its page.
 */
function pathView(steps: Step[]): TemplateResult {

    const network  = steps[0];
    const list     = steps[1] ? sections.find(section => section.segment === steps[1]!.segment) : undefined;

    return html`
        <nav class="poi-sections poi-path">
            <a href=${toURL(poiHome)}>Roaming networks</a>
            ${network ? html`<span class="muted">/</span><a href=${entityURL(network.path)}><strong>${network.id}</strong></a>` : nothing}
            ${list    ? html`<span class="muted">/</span><a href=${entityURL(`${network!.path}/${list.segment}`)}>${list.label}</a>` : nothing}
            ${steps.slice(1).map((step, index) => html`
                <span class="muted">/</span>
                ${index === steps.length - 2
                      ? html`<code class="active">${step.id}</code>`
                      : html`<a href=${entityURL(step.path)} title=${kinds[step.segment]?.label ?? ''}><code>${step.id}</code></a>`}`)}
        </nav>`;

}

/** The children of an entity, each leading to its page: as many as a table shows, and where the others are. */
function childrenView(children: Children, part: ChildrenPart, steps: Step[]): TemplateResult {

    const shown  = part.entries.slice(0, childrenShown);
    const here   = steps.at(-1)!.path;

    return html`
        <section class="card poi-children">

            <h2><i class="fa-solid ${children.icon ?? kinds[children.segment]?.icon ?? 'fa-list'}"></i> ${children.label}
                <span class="chip">${part.count}</span></h2>

            ${part.count === 0
                  ? html`<p class="muted">None.</p>`
                  : html`
                      <div class="table-scroll">
                          <table class="table poi-table">
                              <thead><tr><th>Identification</th><th>Name</th>${children.head.map(label => html`<th>${label}</th>`)}</tr></thead>
                              <tbody>
                                  ${shown.map(child => html`
                                      <tr>
                                          <td><a href=${entityURL(`${here}/${children.segment}/${encodeURIComponent(child['@id'])}`)}><code>${child['@id']}</code></a></td>
                                          <td>${textOf(child.name)}</td>
                                          ${children.cells(child).map(cell => html`<td class=${typeof cell === 'number' ? 'poi-number' : ''}>${cell}</td>`)}
                                      </tr>`)}
                              </tbody>
                          </table>
                      </div>`}

            ${part.count > shown.length
                  ? html`<p class="muted small">The first ${shown.length} of ${part.count}${children.all
                        ? html` - <a href=${children.all(steps)}>search all of them</a>`
                        : html`; all of them are in the JSON below`}.</p>`
                  : nothing}

        </section>`;

}

/** The children an entity embeds. */
function embedded(children: Children, entity: Entity): ChildrenPart {

    const entries = Array.isArray(entity[children.field]) ? entity[children.field] as Entity[] : [];

    return { entries, count: entries.length };

}

/** The entity as the API sent it, and the ETag it came with. */
function jsonView({ entity, eTag }: Loaded): TemplateResult {

    return html`
        <section class="card">
            <details class="poi-json">
                <summary>JSON${eTag ? html` <span class="muted small">ETag <code>${eTag}</code></span>` : nothing}</summary>
                <pre>${JSON.stringify(entity, null, 2)}</pre>
            </details>
        </section>`;

}


/** The page of an entity of the given kind. */
function entityPage(segment: string): Page {

    const kind = kinds[segment]!;

    return {

        title: () => kind.label,

        render({ root, url }) {

            const steps  = stepsOf(url);
            const self   = steps.at(-1)!;

            let loaded:     Loaded | null            = null;
            let listed:     (ChildrenPart | null)[]  = [];
            let problem:    unknown                  = null;
            let asking:     AbortController | null   = null;
            let cancelled                            = false;

            function draw(): void {

                const content  = shell(root, {
                                     active:    poiHome,
                                     title:     textOf(loaded?.entity.name) || self.id,
                                     subtitle:  `${kind.label} ${self.id}`,
                                     actions:   reloadButton(() => load())
                                 });

                const fields   = loaded === null
                                     ? []
                                     : Object.entries(loaded.entity).filter(([ key ]) => !notAField.has(key) && !kind.children?.some(children => children.field === key));

                render(content, html`

                    ${pathView(steps)}

                    ${problem !== null
                        ? html`<div class="error-box">The ${kind.label.toLowerCase()} could not be loaded: ${errorMessage(problem)}</div>`
                        : loaded === null
                              ? html`<div class="loading">Loading ...</div>`
                              : html`
                                  <div class="cards stacked">

                                      <section class="card poi-entity">
                                          <h2><i class="fa-solid ${kind.icon}"></i> ${kind.label}</h2>
                                          <div class="kv-list">
                                              ${fieldsView(fields, steps)}
                                          </div>
                                      </section>

                                      ${(kind.children ?? []).map((children, index) => childrenView(children, listed[index] ?? embedded(children, loaded!.entity), steps))}

                                      ${jsonView(loaded)}

                                  </div>`}

                `);

            }

            async function load(): Promise<void> {

                asking?.abort();
                const ask = asking = new AbortController();

                problem = null;
                draw();

                try
                {

                    const lists                = (kind.children ?? []).map(children => children.listed?.(steps));
                    const [ entity, ...parts ] = await Promise.all([
                                                     poiEntity(self.path, kind.parameters, ask.signal),
                                                     ...lists.map(list => list ? poiList<Entity>(list.path, list.query, ask.signal) : Promise.resolve(null))
                                                 ]);

                    if (cancelled || ask !== asking)
                        return;

                    loaded  = entity;
                    listed  = parts.map(part => part === null ? null : { entries: part.entries, count: part.filtered });

                }
                catch (failure)
                {

                    if (cancelled || ask !== asking)
                        return;

                    loaded   = null;
                    problem  = failure;

                }

                draw();

            }

            void load();

            return () => {
                cancelled = true;
                asking?.abort();
            };

        }

    };

}


/**
 * A roaming network: its name, and how many it holds of each kind, each leading to its list. Not the roaming
 * network as its URL answers with JSON - that is everything it holds, all at once - but its entry in the list of
 * the roaming networks, and the counts of its lists.
 */
export const poiNetworkPage: Page = {

    title: () => 'Roaming network',

    render({ root, url }) {

        const roamingNetworkId  = roamingNetworkOf(url);
        const path              = networkPath(roamingNetworkId);

        let network:    Summary | null           = null;
        let counts:     number[] | null          = null;
        let problem:    unknown                  = null;
        let asking:     AbortController | null   = null;
        let cancelled                            = false;

        function draw(): void {

            const content = shell(root, {
                                active:    poiHome,
                                title:     textOf(network?.name) || roamingNetworkId,
                                subtitle:  `Roaming network ${roamingNetworkId}`,
                                actions:   reloadButton(() => load())
                            });

            render(content, html`

                ${sectionsView(roamingNetworkId)}

                ${problem !== null
                    ? html`<div class="error-box">The roaming network could not be loaded: ${errorMessage(problem)}</div>`
                    : counts === null
                          ? html`<div class="loading">Loading ...</div>`
                          : html`
                              <div class="cards stacked">

                                  <section class="card poi-entity">
                                      <h2><i class="fa-solid fa-network-wired"></i> Roaming network</h2>
                                      <div class="kv-list">
                                          ${fieldsView(Object.entries(network ?? { '@id': roamingNetworkId }), [])}
                                      </div>
                                  </section>

                                  <section class="card poi-holds">
                                      <h2><i class="fa-solid fa-layer-group"></i> What it holds</h2>
                                      <div class="kv-list">
                                          ${sections.map((section, index) => html`
                                              <div class="kv">
                                                  <span class="k"><i class="fa-solid ${section.icon}"></i>
                                                      <a href=${entityURL(`${path}/${section.segment}`)}>${section.label}</a></span>
                                                  <span class="poi-value poi-number" id=${`poi-count-${section.segment}`}>${counts![index]!.toLocaleString()}</span>
                                              </div>`)}
                                      </div>
                                  </section>

                              </div>`}

            `);

        }

        async function load(): Promise<void> {

            asking?.abort();
            const ask = asking = new AbortController();

            problem = null;
            draw();

            try
            {

                // A part of no entry of each list says how many it has.
                const [ networks, ...lists ] = await Promise.all([
                                                   poiList<Summary>('/RNs', {}, ask.signal),
                                                   ...sections.map(section => poiList<Summary>(`${path}/${section.segment}`, { limit: 0 }, ask.signal))
                                               ]);

                if (cancelled || ask !== asking)
                    return;

                network  = networks.entries.find(entry => entry['@id'] === roamingNetworkId) ?? null;
                counts   = lists.map(list => list.total);

            }
            catch (failure)
            {

                if (cancelled || ask !== asking)
                    return;

                counts   = null;
                problem  = failure;

            }

            draw();

        }

        void load();

        return () => {
            cancelled = true;
            asking?.abort();
        };

    }

};


const network          = `${poiHome}/:rn`;
const operator         = `${network}/CSOs/:cso`;
const pool             = `${operator}/CPs/:cp`;
const station          = `${pool}/CSs/:cs`;
const gridOperator     = `${network}/GOs/:go`;
const parkingOperator  = `${network}/POs/:po`;

/** The pages of the single things of the points of interest, by their paths: public, as the POI API is. */
export const poiEntityPages: Record<string, Page> = {
    [network]:                       poiNetworkPage,
    [operator]:                      entityPage('CSOs'),
    [pool]:                          entityPage('CPs'),
    [station]:                       entityPage('CSs'),
    [`${station}/EVSEs/:evse`]:      entityPage('EVSEs'),
    [gridOperator]:                  entityPage('GOs'),
    [`${gridOperator}/GCPs/:gcp`]:   entityPage('GCPs'),
    [`${network}/EMPs/:emp`]:        entityPage('EMPs'),
    [`${network}/CSMs/:csm`]:        entityPage('CSMs'),
    [parkingOperator]:               entityPage('POs'),
    ...Object.fromEntries(parkingCollections.map(collection => [ `${parkingOperator}/${collection.segment}/:id`, entityPage(collection.segment) ]))
};
