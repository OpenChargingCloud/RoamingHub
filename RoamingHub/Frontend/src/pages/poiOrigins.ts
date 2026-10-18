import { poiEntity, poiList, type Entity } from '../api/poi';

/**
 * Where the values of a charging pool, a charging station, an EVSE or a parking space come from that it does not have
 * as it is given: from its templates, which it names in its templateIds and whose properties it takes at runtime. The
 * POI API serves a member as it is at runtime, ?view=stored as it is given, and lists its templates below it, in its
 * order, e.g. ~/…/CSs/{id}/CSTs: what the first has and the second has not, a template gave it - the first of its
 * templates giving it, as the POI API decides it.
 *
 * - A single value is the member's own, else the one of the first of its templates giving one.
 * - An array is its own united with those of its templates, its own winning an element of the same key, else the first
 *   template giving it: an authentication mode by its type, a brand, a connector or a data license by its
 *   identification, an image by its URL, anything else by what it is.
 * - The charging tariffs of an EVSE are its own, else the charging tariff of the first of its EVSE templates naming one,
 *   else of the first template of its charging station naming one, else of the first template of its charging pool.
 *
 * The energy meters a charging pool, a charging station, an EVSE or a grid connection point embeds take what their
 * energy meter templates give them by the same rules: the templates a meter names in its templateIds, of its owner, a
 * charging station operator or a grid operator, listed below it, ~/…/{CSOs|GOs}/{id}/EMTs.
 *
 * Groups give nothing: a member is in groups, which collect members, but takes nothing from them.
 */


/** A template, and the path of its page below /poi: below its owner. */
export interface TemplateOf {
    template:  Entity;
    path:      string;
}

/** Where a value comes from: the templates giving it, with the elements each gives of an array, and whether all of it. */
export interface Origin {
    from:   { id: string; path: string; elements?: string[] }[];
    whole:  boolean;
}

/** What the templates of a member give it: its templates, in its order, and of an EVSE the templates giving it a charging tariff. */
export interface Templates {
    templates:  TemplateOf[];
    /** Of an EVSE: its own templates, those of its charging station and those of its charging pool, the most specific first. */
    tariffs?:   TemplateOf[][];
}

/** Where the values of an entity come from, by their keys; and those of the energy meters in it, by their identifications. */
export interface Origins {
    values:  Map<string, Origin>;
    meters:  Map<string, Map<string, Origin>>;
}

/** The keys of the elements of the arrays a member unites with those of its templates. */
const elementKeys: Record<string, string> = {
    authenticationModes:         'type',
    brands:                      '@id',
    chargingConnectors:          '@id',
    dataLicenses:                '@id',
    images:                      'url',
    transparencySoftwareStatus:  'transparencySoftwareId'
};

/**
 * The arrays a member takes whole, its own, else the one of its templates: written as arrays, they are single values,
 * e.g. the elements of a charging tariff.
 */
const takenWhole = new Set([ 'currentTypes', 'chargingModes', 'elements' ]);

/** What is never given by templates, or other at runtime for another reason. */
const notGiven = new Set([ '@context', '@id', 'name', 'description', 'dataSource', 'customData', 'created', 'lastChange', 'templateIds' ]);

/** What the API embeds where a member names it, by the property naming it: the grid connection point of a pool. */
const expanded: Record<string, string> = { gridConnectionPoint: 'gridConnectionPointId' };


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

/** Of the templates the first having the given property, as the one deciding it. */
const deciding = (templates: TemplateOf[], key: string) => templates.find(template => template.template[key] !== undefined && template.template[key] !== null);


/**
 * Where the values of a member come from that it has at runtime, the effective one, but not as it is given, stored, by
 * their keys. Values of its own, and those no template gives, have none. What it embeds of its own, e.g. the charging
 * stations of a pool, is left out.
 */
export function originsOf(effective:  Entity,
                          stored:     Entity,
                          given:      Templates,
                          embedded:   string[] = []): Map<string, Origin> {

    const origins    = new Map<string, Origin>();
    const templates  = given.templates;

    for (const [ key, value ] of Object.entries(effective))
    {

        if (notGiven.has(key) || embedded.includes(key) || same(value, stored[key]))
            continue;

        // The charging tariffs of an EVSE: those of its most specific templates naming one.
        if (key === 'chargingTariffIds' && given.tariffs)
        {

            for (const level of given.tariffs)
            {
                const template = deciding(level, 'chargingTariffId');
                if (template)
                {
                    origins.set(key, { from: [ { id: template.template['@id'], path: template.path } ], whole: true });
                    break;
                }
            }

            continue;

        }

        // An array united with those of its templates: the elements it has not of its own, each of the first template giving it.
        if (Array.isArray(value) && !takenWhole.has(key))
        {

            const own   = new Set((Array.isArray(stored[key]) ? stored[key] as unknown[] : []).map(element => keyOf(key, element)));
            const from  = new Map<TemplateOf, string[]>();

            for (const element of value)
            {

                const elementKey = keyOf(key, element);

                if (own.has(elementKey))
                    continue;

                const template = templates.find(template => Array.isArray(template.template[key]) &&
                                                            (template.template[key] as unknown[]).some(other => keyOf(key, other) === elementKey));

                if (template)
                    from.set(template, [ ...(from.get(template) ?? []), elementKey ]);

            }

            if (from.size > 0)
                origins.set(key, {
                    from:   [ ...from ].map(([ template, elements ]) => ({ id: template.template['@id'], path: template.path, elements })),
                    whole:  own.size === 0
                });

            continue;

        }

        // A single value it has not of its own: the one of the first of its templates giving one.
        if (stored[key] === undefined)
        {
            const template = deciding(templates, expanded[key] ?? key);
            if (template)
                origins.set(key, { from: [ { id: template.template['@id'], path: template.path } ], whole: true });
        }

    }

    return origins;

}


/** The energy meters an entity embeds: its energy meter, or its energy meters. */
export function metersOf(entity: Entity): Entity[] {

    const isMeter = (meter: unknown): meter is Entity => typeof meter === 'object' && meter !== null && typeof (meter as Entity)['@id'] === 'string';

    return [ ...(isMeter(entity.energyMeter) ? [ entity.energyMeter ] : []),
             ...(Array.isArray(entity.energyMeters) ? entity.energyMeters.filter(isMeter) : []) ];

}

/**
 * Where the values of the energy meters an entity embeds come from that they have at runtime, but not as they are
 * given: from the energy meter templates each names, in its order, by the identifications of the meters. Meters taking
 * nothing have none.
 */
export function meterOriginsOf(effective:  Entity,
                               stored:     Entity,
                               templates:  TemplateOf[]): Map<string, Map<string, Origin>> {

    const origins       = new Map<string, Map<string, Origin>>();
    const storedMeters  = new Map(metersOf(stored).map(meter => [ meter['@id'].toLowerCase(), meter ]));

    for (const meter of metersOf(effective))
    {

        const own = storedMeters.get(meter['@id'].toLowerCase());

        // The templates it names, in its order; identifications of energy meter templates compare without case.
        const named   = Array.isArray(meter.templateIds) ? (meter.templateIds as unknown[]).filter((id): id is string => typeof id === 'string') : [];
        const ofMeter = named.map(id => templates.find(template => template.template['@id'].toLowerCase() === id.toLowerCase())).
                              filter((template): template is TemplateOf => template !== undefined);

        if (own === undefined || ofMeter.length === 0)
            continue;

        const given = originsOf(meter, own, { templates: ofMeter });

        if (given.size > 0)
            origins.set(meter['@id'], given);

    }

    return origins;

}


/** The templates of a member below its page, in its order, each with the path of its page below its owner. */
async function templatesOf(member:   string,
                           segment:  string,
                           owner:    string,
                           signal:   AbortSignal): Promise<TemplateOf[]> {

    const part = await poiList<Entity>(`${member}/${segment}`, { limit: 100 }, signal);

    return part.entries.map(template => ({ template, path: `${owner}/${segment}/${encodeURIComponent(template['@id'])}` }));

}

/** The templates of an owner below its page, each with the path of its page below it. */
async function templatesOfOwner(owner:    string,
                                segment:  string,
                                signal:   AbortSignal): Promise<TemplateOf[]> {

    const part = await poiList<Entity>(`${owner}/${segment}`, { limit: 100 }, signal);

    return part.entries.map(template => ({ template, path: `${owner}/${segment}/${encodeURIComponent(template['@id'])}` }));

}

/** The segments of the templates of a member, by the segment of the member. */
export const templateSegments: Record<string, string> = { CPs: 'CPTs', CSs: 'CSTs', EVSEs: 'EVSETs', PSs: 'PSTs', CTs: 'CTTs' };

/** The segment of the owner of the energy meter templates, by the segment of what embeds energy meters. */
const meterOwners: Record<string, string> = { CPs: 'CSOs', CSs: 'CSOs', EVSEs: 'CSOs', GCPs: 'GOs' };

/**
 * Where the values of the entity at the given path come from: it as it is given, and its templates, asked for; and of
 * the energy meters it embeds, the energy meter templates of its owner. The paths below /poi of its roaming network, its
 * owner, and, of an EVSE, its charging pool and station; the parameters it was asked with. Null for what has no
 * templates, nor energy meters with templates.
 */
export async function loadOrigins(steps:       { segment: string; path: string }[],
                                  effective:   Entity,
                                  parameters:  Record<string, string> | undefined,
                                  embedded:    string[],
                                  signal:      AbortSignal): Promise<Origins | null> {

    const self      = steps.at(-1)!;
    const owner     = steps[1];
    const segment   = templateSegments[self.segment];

    // Templates are of charging station operators and of parking operators: the parking spaces of a charging station
    // operator have none.
    const ofMember  = segment !== undefined && owner !== undefined &&
                      (self.segment === 'PSs' ? owner.segment === 'POs' : owner.segment === 'CSOs');

    // Energy meter templates are of charging station operators and of grid operators.
    const ofMeters  = owner !== undefined && meterOwners[self.segment] === owner.segment &&
                      metersOf(effective).some(meter => Array.isArray(meter.templateIds) && meter.templateIds.length > 0);

    if (!ofMember && !ofMeters)
        return null;

    const isEVSE    = ofMember && self.segment === 'EVSEs' && steps.length >= 5;

    // Without its templates, or the energy meter templates, where they can not be asked: what the others give still shows.
    const [ stored, templates, stationTemplates, poolTemplates, meterTemplates ] = await Promise.all([
        poiEntity(self.path, { ...parameters, view: 'stored' }, signal),
        ofMember ? templatesOf(self.path, segment!, owner!.path, signal).catch(() => null) : Promise.resolve(null),
        isEVSE   ? templatesOf(steps[3]!.path, 'CSTs', owner!.path, signal).catch(() => []) : Promise.resolve([]),
        isEVSE   ? templatesOf(steps[2]!.path, 'CPTs', owner!.path, signal).catch(() => []) : Promise.resolve([]),
        ofMeters ? templatesOfOwner(owner!.path, 'EMTs', signal).catch(() => []) : Promise.resolve([])
    ]);

    return {
        values:  templates === null
                     ? new Map()
                     : originsOf(effective, stored.entity, { templates, tariffs: isEVSE ? [ templates, stationTemplates, poolTemplates ] : undefined }, embedded),
        meters:  meterOriginsOf(effective, stored.entity, meterTemplates)
    };

}
