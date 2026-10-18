import { toURL } from '@node/basePath';
import { ApiError } from '@node/api/client';

/**
 * The points of interest this hub serves with POI 3 - its QUERY interface, below "/poi". A page of it
 * stands at the very URL of its list or entity: a browser opening /poi/RNs/rn1/CPs gets this web interface,
 * and the page asks the same URL for the data, as JSON. The POI API is public: nobody signs in for it.
 */

/** Where the POI API is, below the base of this hub. */
export const poiBase = '/poi';

/** A multi-language text: { "de": "...", "en": "..." }. */
export type I18N = Record<string, string>;

/** An entry of a list of the roaming networks or of what they hold, in short. */
export interface Summary {
    '@id':          string;
    name?:          I18N;
    description?:   I18N;
}

/** An address, as POI 3 writes it embedded. */
export interface Address {
    street?:       string;
    houseNumber?:  string;
    postalCode?:   string;
    city?:         I18N;
    country?:      string;
}

/** A charging pool of the QUERY interface: as its operator embeds it, with the operator in short. */
export interface ChargingPool extends Summary {
    address?:                  Address;
    geoLocation?:              { lat: number; lng: number };
    gridConnectionPointId?:    string;
    chargingStations?:         { '@id': string; EVSEs?: { '@id': string }[] }[];
    chargingStationOperator:   Summary;
}

/** What a list query asks for: words to search, and the part of the list; and what else narrows it, e.g. status=active. */
export interface Query {
    match?:       string;
    offset?:      number;
    limit?:       number;
    operatorId?:  string;
    filters?:     Record<string, string>;
}

/** One part of a list: its entries, how many there are at all, and how many match. */
export interface ListPart<T> {
    entries:   T[];
    total:     number;
    filtered:  number;
}

/** The URL of a list of the POI API with the given query, below the base of this hub. */
export function poiURL(path: string, query: Query = {}): string {

    const parameters = new URLSearchParams();

    if (query.match?.trim())
        parameters.set('match', query.match.trim());

    if (query.operatorId)
        parameters.set('operatorId', query.operatorId);

    for (const [ name, value ] of Object.entries(query.filters ?? {}))
        parameters.set(name, value);

    if (query.offset)
        parameters.set('offset', String(query.offset));

    // 0 as well: no entry, only the counts.
    if (query.limit !== undefined)
        parameters.set('limit', String(query.limit));

    const search = parameters.toString();

    return toURL(`${poiBase}${path}`) + (search ? `?${search}` : '');

}

/** An entity of the POI API as it answers: its identification, and whatever it has. */
export interface Entity {
    '@id':           string;
    name?:           I18N;
    description?:    I18N;
    [field: string]: unknown;
}

/** An entity, and the ETag of the representation it came as. */
export interface Loaded {
    entity:  Entity;
    eTag:    string | null;
}

/**
 * GET with Accept: application/json - a browser asking without would get the page again. A refusal throws an
 * ApiError with what the API says, { "description": "..." }.
 */
async function poiGet(url:      string,
                      signal?:  AbortSignal): Promise<{ body: unknown; response: Response }> {

    const response = await fetch(url, {
                               headers:      { Accept: 'application/json' },
                               credentials:  'same-origin',
                               signal
                           });

    const body = await response.json().catch(() => undefined) as unknown;

    if (!response.ok)
        throw new ApiError(response.status,
                           (body as { description?: string } | undefined)?.description ?? `${response.status} ${response.statusText}`,
                           body);

    return { body, response };

}

/** A part of a list of the POI API, and its counts from X-Total-Count and X-Filtered-Count. */
export async function poiList<T>(path:     string,
                                 query:    Query,
                                 signal?:  AbortSignal): Promise<ListPart<T>> {

    const { body, response } = await poiGet(poiURL(path, query), signal);

    const entries   = Array.isArray(body) ? body as T[] : [];
    const total     = Number(response.headers.get('X-Total-Count')    ?? entries.length);
    const filtered  = Number(response.headers.get('X-Filtered-Count') ?? total);

    return { entries, total, filtered };

}

/** An entity of the POI API at the given path, e.g. /RNs/rn1/CSOs/DE*GEF, with what else to ask for. */
export async function poiEntity(path:        string,
                                parameters:  Record<string, string> = {},
                                signal?:     AbortSignal): Promise<Loaded> {

    const search              = new URLSearchParams(parameters).toString();
    const { body, response }  = await poiGet(toURL(`${poiBase}${path}`) + (search ? `?${search}` : ''), signal);

    if (typeof body !== 'object' || body === null || Array.isArray(body))
        throw new ApiError(response.status, 'The POI API answered with something that is no entity.', body);

    return { entity: body as Entity, eTag: response.headers.get('ETag') };

}

/** The text of a multi-language text: in the language of the browser, else English, else any. */
export function textOf(text: I18N | undefined): string {

    if (!text)
        return '';

    const language = (navigator.language ?? 'en').slice(0, 2);

    return text[language] ?? text['en'] ?? Object.values(text)[0] ?? '';

}
