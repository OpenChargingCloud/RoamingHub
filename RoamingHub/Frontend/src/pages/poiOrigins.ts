import { poiEntity, poiList, type Entity } from '../api/poi';

/**
 * Where the values of a charging pool, a charging station, an EVSE or a parking space come from that it does not have
 * as it is given: from its groups, which give their members properties at runtime. The POI API serves a member as it
 * is at runtime, ?view=stored as it is given, and lists its groups below it, e.g. ~/…/CSs/{id}/CSGs: what the first
 * has and the second has not, a group gave it - of its groups the one with the highest priority giving it, as the POI
 * API decides it.
 *
 * - A single value is the member's own, else the one of its group with the highest priority.
 * - An array is its own united with those of its groups, its own winning an element of the same key: an authentication
 *   mode by its type, a brand, a connector or a data license by its identification, an image by its URL, anything
 *   else by what it is.
 * - The charging tariffs of an EVSE are its own, else the charging tariff of its EVSE groups, else of the groups of its
 *   charging station, else of the groups of its charging pool.
 */


/** A group, and the path of its page below /poi: below its operator. */
export interface GroupOf {
    group:  Entity;
    path:   string;
}

/** Where a value comes from: the groups giving it, with the elements each gives of an array, and whether all of it. */
export interface Origin {
    from:   { id: string; path: string; elements?: string[] }[];
    whole:  boolean;
}

/** What the groups of a member give it: its groups, and of an EVSE the groups giving it a charging tariff. */
export interface Groups {
    groups:   GroupOf[];
    /** Of an EVSE: its own groups, those of its charging station and those of its charging pool, the most specific first. */
    tariffs?: GroupOf[][];
}

/** The keys of the elements of the arrays a member unites with those of its groups. */
const elementKeys: Record<string, string> = {
    authenticationModes:  'type',
    brands:               '@id',
    chargingConnectors:   '@id',
    dataLicenses:         '@id',
    images:               'url'
};

/** The arrays a member takes whole, its own, else the one of its groups: written as arrays, they are single values. */
const takenWhole = new Set([ 'currentTypes', 'chargingModes' ]);

/** What is never given by groups, or other at runtime for another reason. */
const notGiven = new Set([ '@context', '@id', 'name', 'description', 'dataSource', 'customData', 'created', 'lastChange' ]);

/** What the API embeds where a member names it, by the property naming it: the grid connection point of a pool. */
const expanded: Record<string, string> = { gridConnectionPoint: 'gridConnectionPointId' };


/** The groups by their priority, the highest first, of the same one in the order of their identifications. */
function byPriority(groups: GroupOf[]): GroupOf[] {

    const priority = (group: GroupOf) => typeof group.group.priority === 'number' ? group.group.priority : 0;
    const id       = (group: GroupOf) => group.group['@id'];

    return [ ...groups ].sort((a, b) => priority(b) - priority(a) || (id(a) < id(b) ? -1 : id(a) > id(b) ? 1 : 0));

}

/** The key of an element of the given array, by which the member's own one wins. */
function keyOf(key: string, element: unknown): string {

    const named = elementKeys[key];

    if (named !== undefined && typeof element === 'object' && element !== null)
    {
        const value = (element as Record<string, unknown>)[named];
        if (typeof value === 'string')
            return value;
    }

    return typeof element === 'string' ? element : JSON.stringify(element);

}

/** Whether two values are the same, as the API writes them. */
const same = (a: unknown, b: unknown) => JSON.stringify(a) === JSON.stringify(b);

/** Of the groups the first having the given property, as the one deciding it. */
const deciding = (groups: GroupOf[], key: string) => groups.find(group => group.group[key] !== undefined && group.group[key] !== null);


/**
 * Where the values of a member come from that it has at runtime, the effective one, but not as it is given, stored, by
 * their keys. Values of its own, and those no group gives, have none. What it embeds of its own, e.g. the charging
 * stations of a pool, is left out.
 */
export function originsOf(effective:  Entity,
                          stored:     Entity,
                          given:      Groups,
                          embedded:   string[] = []): Map<string, Origin> {

    const origins  = new Map<string, Origin>();
    const groups   = byPriority(given.groups);

    for (const [ key, value ] of Object.entries(effective))
    {

        if (notGiven.has(key) || embedded.includes(key) || same(value, stored[key]))
            continue;

        // The charging tariffs of an EVSE: those of its most specific groups giving one.
        if (key === 'chargingTariffIds' && given.tariffs)
        {

            for (const level of given.tariffs)
            {
                const group = deciding(byPriority(level), 'chargingTariffId');
                if (group)
                {
                    origins.set(key, { from: [ { id: group.group['@id'], path: group.path } ], whole: true });
                    break;
                }
            }

            continue;

        }

        // An array united with those of its groups: the elements it has not of its own, each of the first group giving it.
        if (Array.isArray(value) && !takenWhole.has(key))
        {

            const own   = new Set((Array.isArray(stored[key]) ? stored[key] as unknown[] : []).map(element => keyOf(key, element)));
            const from  = new Map<GroupOf, string[]>();

            for (const element of value)
            {

                const elementKey = keyOf(key, element);

                if (own.has(elementKey))
                    continue;

                const group = groups.find(group => Array.isArray(group.group[key]) &&
                                                   (group.group[key] as unknown[]).some(other => keyOf(key, other) === elementKey));

                if (group)
                    from.set(group, [ ...(from.get(group) ?? []), elementKey ]);

            }

            if (from.size > 0)
                origins.set(key, {
                    from:   [ ...from ].map(([ group, elements ]) => ({ id: group.group['@id'], path: group.path, elements })),
                    whole:  own.size === 0
                });

            continue;

        }

        // A single value it has not of its own: the one of its group with the highest priority giving one.
        if (stored[key] === undefined)
        {
            const group = deciding(groups, expanded[key] ?? key);
            if (group)
                origins.set(key, { from: [ { id: group.group['@id'], path: group.path } ], whole: true });
        }

    }

    return origins;

}


/** The groups of a member below its page, each with the path of its page below its operator. */
async function groupsOf(member:    string,
                        segment:   string,
                        operator:  string,
                        signal:    AbortSignal): Promise<GroupOf[]> {

    const part = await poiList<Entity>(`${member}/${segment}`, { limit: 100 }, signal);

    return part.entries.map(group => ({ group, path: `${operator}/${segment}/${encodeURIComponent(group['@id'])}` }));

}

/** The segments of the groups of a member, by the segment of the member. */
export const groupSegments: Record<string, string> = { CPs: 'CPGs', CSs: 'CSGs', EVSEs: 'EVSEGs', PSs: 'PSGs' };

/**
 * Where the values of the member at the given path come from: it as it is given, and its groups, asked for. The paths
 * below /poi of its roaming network, its operator, and, of an EVSE, its charging pool and station; the parameters the
 * member was asked with. Null for what has no groups.
 */
export async function loadOrigins(steps:       { segment: string; path: string }[],
                                  effective:   Entity,
                                  parameters:  Record<string, string> | undefined,
                                  embedded:    string[],
                                  signal:      AbortSignal): Promise<Map<string, Origin> | null> {

    const self      = steps.at(-1)!;
    const operator  = steps[1];
    const segment   = groupSegments[self.segment];

    // Groups are of charging station operators and of parking operators: the parking spaces of a charging station
    // operator have none.
    if (segment === undefined || operator === undefined ||
        (self.segment === 'PSs' ? operator.segment !== 'POs' : operator.segment !== 'CSOs'))
        return null;

    const isEVSE    = self.segment === 'EVSEs' && steps.length >= 5;

    const [ stored, groups, stationGroups, poolGroups ] = await Promise.all([
        poiEntity(self.path, { ...parameters, view: 'stored' }, signal),
        groupsOf(self.path, segment, operator.path, signal),
        isEVSE ? groupsOf(steps[3]!.path, 'CSGs', operator.path, signal) : Promise.resolve([]),
        isEVSE ? groupsOf(steps[2]!.path, 'CPGs', operator.path, signal) : Promise.resolve([])
    ]);

    return originsOf(effective, stored.entity, { groups, tariffs: isEVSE ? [ groups, stationGroups, poolGroups ] : undefined }, embedded);

}
