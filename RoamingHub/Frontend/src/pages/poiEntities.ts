import { poiBase, poiEntity, poiList, textOf, type Address, type ChargingPool, type Entity, type I18N, type Loaded, type Query, type Summary } from '../api/poi';
import { fromURL, toURL } from '@node/basePath';
import type { Page } from '@node/router';
import { reloadButton, shell } from '@node/shell';
import { errorMessage, formatValue, humanizeKey } from '@node/ui';
import { html, nothing, render, type TemplateResult } from '@node/view';
import { addressLine, entityURL, evseCountOf, gridCollections, networkPath, operatorCollections, parkingCollections, providerCollections, poiHome, roamingNetworkOf, sections, sectionsView, type EmbeddedCollection } from './poi';
import { loadOffers, localTimeOf, nextRefresh, offersView, timeOf, type Offers, type OffersShown } from './poiOffers';
import { loadOrigins, templateSegments, type Origin, type Origins } from './poiOrigins';
import { loadTrustAnchor, trustAnchorCard, type TrustAnchor } from './poiTrustAnchor';
import { copyText } from '@node/ui';

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
 * grid operator without its grid connection points and its energy meter templates and groups, a parking operator without
 * its garages, spaces, templates and groups of spaces, sensors and products, and an e-mobility provider without its groups
 * and offers, the first of each from their lists. Each of what a parking operator embeds has a page of its own, below its
 * parking operator, and a parking space leads to its garage, sensors, products and templates, as a group to its spaces.
 * So has every charging tariff, template, group and offer of a charging station operator, below it, and what names a
 * tariff, templates, groups or pools of it leads there; every energy meter template and group, below the charging
 * station operator or the grid operator it is of; and every group and offer of an e-mobility provider, below it.
 *
 * Brands are embedded in what they are a brand of, every level its own, each meaning something of its own: an operator
 * is a white-label solution for its brands, a pool, station or EVSE also appears under its brands. A table of them
 * on every page, each leading to its page below what it is a brand of; the levels are not mixed.
 *
 * What names a charging pool, a charging station or an EVSE by its identification only - a group of stations, a
 * parking space - leads to it through the roaming network, ~/RNs/rn1/CSs/{id}: the API redirects there to where it
 * is, below its operator and pool, and a browser follows.
 *
 * A charging pool, a charging station, an EVSE, a charging tariff and a parking space of a parking operator are drawn as
 * they are at runtime, with the properties their templates give them: what of it a template gives says so, and leads to
 * the template - found by asking the member as it is given, and its templates (see poiOrigins). So are the energy meters
 * a pool, station, EVSE or grid connection point embeds, with what their energy meter templates give them. Groups give
 * nothing: they collect members.
 *
 * A charging pool, a charging station and an EVSE show the charging tariff offers covering them, of their operator and
 * of e-mobility providers: every one, or those applying now or at a time, with the status of each now, the list asked
 * again when a status changes by the times of an offer (see poiOffers).
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
    /** Whether their table is left out when there are none: for what most entities have none of. */
    hideEmpty?: boolean;
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
    /** Whether it shows the charging tariff offers covering it: a charging pool, a charging station, an EVSE. */
    offers?:      boolean;
}

/** The brands of an operator, pool, station or EVSE: its own, embedded, each leading to its page below it. */
function brandsOf(label: string): Children {
    return {
        field:    'brands',
        label,
        segment:  'Brands',
        head:     [ 'Homepage' ],
        cells:    brand => [ typeof brand.homepage === 'string' ? brand.homepage : '' ],
        hideEmpty: true
    };
}

const kinds: Record<string, Kind> = {

    CSOs:   { label: 'Charging station operator',  icon: 'fa-building',
              children: [ { field: 'chargingPools',  label: 'Charging pools',  segment: 'CPs',
                            head:  [ 'Address', 'Stations', 'EVSEs' ],
                            cells: pool => [ addressLine(pool.address as Address | undefined), (pool.chargingStations as unknown[] | undefined)?.length ?? 0, evseCountOf(pool as Pick<ChargingPool, 'chargingStations'>) ],
                            all:     steps => `${entityURL(`${steps[0]!.path}/CPs`)}?operatorId=${encodeURIComponent(steps[1]!.id)}`,
                            listed:  steps => ({ path: `${steps[0]!.path}/CPs`, query: { operatorId: steps[1]!.id, limit: childrenShown } }) },
                          ...operatorCollections.map(collection => listedChildren(collection)),
                          brandsOf('Brands it is a white label for') ],
              parameters: { without: [ 'chargingPools', ...operatorCollections.map(collection => collection.field) ].join(',') } },

    CPs:    { label: 'Charging pool',  icon: 'fa-charging-station',  offers: true,
              parameters: { expand: 'gridConnectionPoint' },
              children: [ { field: 'chargingStations',  label: 'Charging stations',  segment: 'CSs',
                            head:  [ 'EVSEs', 'Max power' ],
                            cells: station => [ (station.EVSEs as unknown[] | undefined)?.length ?? 0, withUnit('maxPower', station.maxPower) ] },
                          brandsOf('Brands it also appears under') ] },

    CSs:    { label: 'Charging station',  icon: 'fa-charging-station',  offers: true,
              children: [ { field: 'EVSEs',  label: 'EVSEs',  segment: 'EVSEs',
                            head:  [ 'Max power', 'Connectors' ],
                            cells: evse => [ withUnit('maxPower', evse.maxPower),
                                             ((evse.chargingConnectors as { type?: string }[] | undefined) ?? []).map(connector => connector.type ?? '?').join(', ') ] },
                          brandsOf('Brands it also appears under') ] },

    EVSEs:  { label: 'EVSE',  icon: 'fa-plug',  offers: true,
              children: [ brandsOf('Brands it also appears under') ] },

    // A brand of an operator, pool, station or EVSE, below it.
    Brands: { label: 'Brand',  icon: 'fa-copyright' },

    GOs:    { label: 'Grid operator',  icon: 'fa-bolt',
              children:   gridCollections.map(collection => listedChildren(collection)),
              parameters: { without: gridCollections.map(collection => collection.field).join(',') } },

    GCPs:   { label: 'Grid connection point',          icon: 'fa-plug-circle-bolt' },
    EMPs:   { label: 'E-mobility provider',            icon: 'fa-id-card',
              children:   providerCollections.map(collection => listedChildren(collection)),
              parameters: { without: providerCollections.map(collection => collection.field).join(',') } },
    CSMs:   { label: 'Charging station manufacturer',  icon: 'fa-industry'         },
    POs:    { label: 'Parking operator',               icon: 'fa-square-parking',
              children:   parkingCollections.map(collection => listedChildren(collection)),
              parameters: { without: parkingCollections.map(collection => collection.field).join(',') } },

    // What a parking operator embeds, each below it.
    ...Object.fromEntries(parkingCollections.map(collection => [ collection.segment, { label: collection.one, icon: collection.icon } ])),

    // The charging tariffs, templates, groups and offers of a charging station operator, each below it; an energy meter
    // template and group below a grid operator as well, a group and an offer below an e-mobility provider.
    ...Object.fromEntries(operatorCollections.map(collection => [ collection.segment, { label: collection.one, icon: collection.icon } ]))

};

/**
 * The fields of what an operator holds naming other things of it, by the segment of their list below the operator:
 * a tariff or pools of a charging station operator, a garage, spaces, sensors or products of a parking operator.
 */
const references: Record<string, Record<string, string>> = {
    CSOs:  { chargingTariffId:         'CTs',
             chargingTariffIds:        'CTs',
             chargingPoolIds:          'CPs',
             EVSEGroupIds:             'EVSEGs',
             chargingStationGroupIds:  'CSGs',
             chargingPoolGroupIds:     'CPGs' },
    POs:   { parkingGarageId:    'PGs',
             parkingSpaceIds:    'PSs',
             parkingSensorIds:   'PSNs',
             parkingProductIds:  'PPs' }
};

/**
 * The fields naming charging pools, charging stations or EVSEs by their identifications only, by the segment that
 * finds them in the roaming network: it redirects to where each is.
 */
const located: Record<string, string> = {
    chargingPoolId:      'CPs',
    chargingPoolIds:     'CPs',
    chargingStationId:   'CSs',
    chargingStationIds:  'CSs',
    EVSEId:              'EVSEs',
    EVSEIds:             'EVSEs'
};

/** What a roaming network holds, each with a list of its own: left out of its page, which counts them. */
export const networkHolds = [ 'chargingStationManufacturers', 'chargingStationOperators', 'eMobilityProviders', 'gridOperators', 'parkingOperators' ];

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


/** The fields holding energy meters: in what they measure, a thing of its own each. */
const meterFields = new Set([ 'energyMeter', 'energyMeters' ]);

/**
 * The value of a field, drawn as it reads best; null for one that is a block of fields of its own. Within, the field
 * of the entity holding it, of a thing in it.
 */
function valueView(key: string, value: unknown, steps: Step[], within?: string): TemplateResult | string | null {

    const network = steps[0]?.path ?? '';

    if (key === '@id' && typeof value === 'string')
        return html`<code>${value}</code>`;

    // The ETags of a roaming network, of all of it: [ format, algorithm, encoding, digest ] each, read as a POI ETag.
    if (key === 'eTags' && Array.isArray(value) && value.length > 0 &&
        value.every(eTag => Array.isArray(eTag) && eTag.every(part => typeof part === 'string')))
        return html`${(value as string[][]).map(eTag => html`<div><code>${eTag.join(':')}</code></div>`)}`;

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

    // The manufacturer of a charging station; the one of an energy meter, also as its template gives it, has no page.
    const ofMeter = (within !== undefined && meterFields.has(within)) || steps.at(-1)?.segment === 'EMTs';

    if (key === 'manufacturerId' && typeof value === 'string' && !ofMeter)
        return html`<a href=${entityURL(`${network}/CSMs/${encodeURIComponent(value)}`)}><code>${value}</code></a>`;

    // What a thing of an operator names of it: a tariff, pools, groups, a garage, sensors, products, spaces; and the
    // templates a member names, of its kind, or of an energy meter in it.
    const templates  = key === 'templateIds'
                           ? within !== undefined && meterFields.has(within) ? 'EMTs' : templateSegments[steps.at(-1)?.segment ?? '']
                           : undefined;
    const referenced = templates ?? (steps[1] ? references[steps[1].segment]?.[key] : undefined);

    if (referenced !== undefined &&
        (typeof value === 'string' || (Array.isArray(value) && value.every(item => typeof item === 'string'))))
    {
        const below = `${steps[1]!.path}/${referenced}`;
        return html`${([] as string[]).concat(value as string | string[]).map((id, index) => html`${index > 0 ? ', ' : ''}<a href=${entityURL(`${below}/${encodeURIComponent(id)}`)}><code>${id}</code></a>`)}`;
    }

    // A pool, station or EVSE named by its identification only: found in the roaming network, which leads to it.
    const finder = located[key];

    if (finder !== undefined && network !== '' &&
        (typeof value === 'string' || (Array.isArray(value) && value.every(item => typeof item === 'string'))))
    {
        return html`${([] as string[]).concat(value as string | string[]).map((id, index) => html`${index > 0 ? ', ' : ''}<a href=${entityURL(`${network}/${finder}/${encodeURIComponent(id)}`)}><code>${id}</code></a>`)}`;
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

/** The name of a field: "@id" is the identification, of a thing inside an entity as well; "eTags" its ETags. */
function labelOf(key: string): string {
    return key === '@id' ? 'Identification' : key === 'eTags' ? 'ETags' : humanizeKey(key);
}

/** Where a value comes from: the templates giving it, all of it or the elements named, each leading to its page. */
function originView(origin: Origin): TemplateResult {

    const link = (from: Origin['from'][number]) => html`<a href=${entityURL(from.path)}><code>${from.id}</code></a>`;

    return html`
        <div class="poi-origin muted small">
            <i class="fa-solid fa-clone"></i>
            ${origin.whole && origin.from.length === 1
                  ? html`from template ${link(origin.from[0]!)}`
                  : origin.from.map((from, index) => html`${index > 0 ? '; ' : ''}${from.elements?.join(', ') ?? ''} from template ${link(from)}`)}
        </div>`;

}

/**
 * The fields of an entity, or of a thing in it, a line each, and a thing of its own as a block below its name; of an
 * entity, where its templates give a value, which ones, and of an energy meter in it, which its energy meter templates give.
 * Within, the field of the entity holding the thing.
 */
function fieldsView(fields:    [ string, unknown ][],
                    steps:     Step[],
                    origins?:  Map<string, Origin> | null,
                    meters?:   Map<string, Map<string, Origin>>,
                    within?:   string): TemplateResult[] {

    return inReadingOrder(fields).map(([ key, value ]) => {

        const shown   = valueView(key, value, steps, within);
        const origin  = origins?.get(key);

        return shown !== null
            ? html`
                <div class="kv">
                    <span class="k">${labelOf(key)}</span>
                    <span class="poi-value">${shown}${origin ? originView(origin) : nothing}</span>
                </div>`
            : html`
                <div class="kv-nested">
                    <span class="k">${labelOf(key)}</span>
                    ${origin ? originView(origin) : nothing}
                    <div class="nested">
                        ${thingsIn(value)!.map(thing => html`
                            <div class="nested-item">
                                ${fieldsView(Object.entries(thing),
                                             steps,
                                             meterFields.has(key) && typeof thing['@id'] === 'string' ? meters?.get(thing['@id']) : undefined,
                                             undefined,
                                             key)}
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

            let loaded:     Loaded | null              = null;
            let listed:     (ChildrenPart | null)[]    = [];
            let origins:    Origins | null             = null;
            let problem:    unknown                    = null;
            let asking:     AbortController | null   = null;
            let cancelled                            = false;

            // The charging tariff offers covering it, those shown, and when they are asked again.
            let offers:         Offers | null                       = null;
            let offersProblem:  unknown                             = null;
            let offersShown:    OffersShown                         = 'all';
            let offersAt                                            = '';
            let offersAsking:   AbortController | null              = null;
            let offersTimer:    ReturnType<typeof setTimeout> | undefined;

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

                // What its templates give it, and the energy meters in it their energy meter templates.
                const given    = origins === null
                                     ? 0
                                     : origins.values.size + [ ...origins.meters.values() ].reduce((sum, meter) => sum + meter.size, 0);

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
                                          ${given > 0
                                                ? html`<p class="muted small poi-origins"><i class="fa-solid fa-clone"></i>
                                                           As it is at runtime: ${given === 1 ? 'one value comes' : `${given} values come`} from its templates.</p>`
                                                : nothing}
                                          <div class="kv-list">
                                              ${fieldsView(fields, steps, origins?.values, origins?.meters)}
                                          </div>
                                      </section>

                                      ${(kind.children ?? []).map((children, index) => {
                                            const part = listed[index] ?? embedded(children, loaded!.entity);
                                            return children.hideEmpty && part.count === 0 ? nothing : childrenView(children, part, steps);
                                        })}

                                      ${kind.offers
                                            ? offersView(offers, offersProblem, offersShown, offersAt, steps[0]!.path, {
                                                  show: shown => {
                                                      offersShown  = shown;
                                                      offersAt   ||= shown === 'at' ? localTimeOf(new Date()) : '';
                                                      offers       = null;
                                                      void loadCoveringOffers();
                                                  },
                                                  at:   value => {
                                                      offersAt  = value;
                                                      offers    = null;
                                                      void loadCoveringOffers();
                                                  }
                                              })
                                            : nothing}

                                      ${jsonView(loaded)}

                                  </div>`}

                `);

            }

            async function load(): Promise<void> {

                asking?.abort();
                const ask = asking = new AbortController();

                problem = null;
                origins = null;
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

                    draw();

                    // The offers covering it, once it is drawn.
                    if (kind.offers)
                        void loadCoveringOffers();

                    // Which of its values its templates give it, once it is drawn: without, where they cannot be asked.
                    origins = await loadOrigins(steps, entity.entity, kind.parameters, (kind.children ?? []).map(children => children.field), ask.signal).
                                        catch(() => null);

                    if (cancelled || ask !== asking)
                        return;

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

            /** The offers covering it, those shown; asked again when the status of one of them changes by its times. */
            async function loadCoveringOffers(): Promise<void> {

                clearTimeout(offersTimer);
                offersAsking?.abort();
                const ask = offersAsking = new AbortController();

                offersProblem = null;

                // Those applying at a time, once there is one.
                if (offersShown === 'at' && timeOf(offersAt) === null)
                {
                    offers = null;
                    draw();
                    return;
                }

                try
                {

                    const found = await loadOffers(self.path, offersShown, timeOf(offersAt), ask.signal);

                    if (cancelled || ask !== offersAsking)
                        return;

                    offers = found;

                    // A second late rather than early: the clock of the hub may be behind the one of the browser.
                    const next = nextRefresh(found.entries, offersShown, Date.now());

                    if (next !== null)
                        offersTimer = setTimeout(() => void loadCoveringOffers(), next - Date.now() + 1000);

                }
                catch (failure)
                {

                    if (cancelled || ask !== offersAsking)
                        return;

                    offers         = null;
                    offersProblem  = failure;

                }

                draw();

            }

            void load();

            return () => {
                cancelled = true;
                asking?.abort();
                offersAsking?.abort();
                clearTimeout(offersTimer);
            };

        }

    };

}


/**
 * A roaming network: what it says of itself, and how many it holds of each kind, each leading to its list. Not the
 * roaming network as its URL answers with JSON at first - that is everything it holds, all at once - but without what
 * it holds, and the counts of its lists.
 */
export const poiNetworkPage: Page = {

    title: () => 'Roaming network',

    render({ root, url }) {

        const roamingNetworkId  = roamingNetworkOf(url);
        const path              = networkPath(roamingNetworkId);

        let network:    Loaded | null            = null;
        let counts:     number[] | null          = null;
        let problem:    unknown                  = null;
        let asking:     AbortController | null   = null;
        let cancelled                            = false;

        // What a driver's app scans, asked on its own: a roaming network without one is shown as well.
        let anchor:     TrustAnchor | null       = null;
        let copied:     string | null            = null;

        function draw(): void {

            const content = shell(root, {
                                active:    poiHome,
                                title:     textOf(network?.entity.name) || roamingNetworkId,
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
                                          ${fieldsView(Object.entries(network?.entity ?? { '@id': roamingNetworkId }).filter(([ key ]) => !notAField.has(key)),
                                                       [ { segment: 'RNs', id: roamingNetworkId, path } ])}
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

                                  ${anchor ? trustAnchorCard(anchor, copied, () => void copyText(anchor!.uri).then(said => { copied = said; draw(); })) : nothing}

                                  ${network ? jsonView(network) : nothing}

                              </div>`}

            `);

        }

        async function load(): Promise<void> {

            asking?.abort();
            const ask = asking = new AbortController();

            problem = null;
            draw();

            void loadTrustAnchor(path, ask.signal).then(found => {
                if (!cancelled && ask === asking) {
                    anchor = found;
                    draw();
                }
            });

            try
            {

                // A part of one entry of each list says how many it has, in X-Total-Count: the least the POI
                // API answers, as a page of its lists has at least one entry.
                const [ entity, ...lists ] = await Promise.all([
                                                 poiEntity(path, { without: networkHolds.join(',') }, ask.signal),
                                                 ...sections.map(section => poiList<Summary>(`${path}/${section.segment}`, { limit: 1 }, ask.signal))
                                             ]);

                if (cancelled || ask !== asking)
                    return;

                network  = entity;
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
    [`${operator}/Brands/:id`]:                   entityPage('Brands'),
    [`${pool}/Brands/:id`]:                       entityPage('Brands'),
    [`${station}/Brands/:id`]:                    entityPage('Brands'),
    [`${station}/EVSEs/:evse/Brands/:id`]:        entityPage('Brands'),
    [gridOperator]:                  entityPage('GOs'),
    [`${gridOperator}/GCPs/:gcp`]:   entityPage('GCPs'),
    [`${gridOperator}/EMTs/:id`]:    entityPage('EMTs'),
    [`${gridOperator}/EMGs/:id`]:    entityPage('EMGs'),
    [`${network}/EMPs/:emp`]:        entityPage('EMPs'),
    ...Object.fromEntries(providerCollections.map(collection => [ `${network}/EMPs/:emp/${collection.segment}/:id`, entityPage(collection.segment) ])),
    [`${network}/CSMs/:csm`]:        entityPage('CSMs'),
    ...Object.fromEntries(operatorCollections.map(collection => [ `${operator}/${collection.segment}/:id`, entityPage(collection.segment) ])),
    [parkingOperator]:               entityPage('POs'),
    ...Object.fromEntries(parkingCollections.map(collection => [ `${parkingOperator}/${collection.segment}/:id`, entityPage(collection.segment) ]))
};
