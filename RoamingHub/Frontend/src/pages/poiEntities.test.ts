/**
 * The single things of the points of interest drawn, in a document of happy-dom, against a stand-in POI API: a
 * roaming network counted by its lists, and an entity asked at the URL of its page, drawn field by field, with its
 * children leading to theirs.
 */

import { asked, open, type Asked } from '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { inReadingOrder, poiEntityPages, poiNetworkPage, stepsOf, withUnit } = await import('./poiEntities.ts');

const operatorPage  = poiEntityPages['/poi/RNs/:rn/CSOs/:cso']!;
const poolPage      = poiEntityPages['/poi/RNs/:rn/CSOs/:cso/CPs/:cp']!;
const evsePage      = poiEntityPages['/poi/RNs/:rn/CSOs/:cso/CPs/:cp/CSs/:cs/EVSEs/:evse']!;


/** An entity, as the POI API answers: with its ETag. */
function entityOf(entity: unknown, eTag = '"json:sha256:hex:abc"'): Response {
    return new Response(JSON.stringify(entity), { status: 200, headers: { 'Content-Type': 'application/json', ETag: eTag } });
}

/** A list, as the POI API answers: the entries, with the counts in the headers. */
function listOf(entries: unknown[], total: number): Response {
    return new Response(JSON.stringify(entries), {
               status:   200,
               headers:  { 'Content-Type': 'application/json', 'X-Total-Count': String(total), 'X-Filtered-Count': String(total) }
           });
}

const pool = {
    '@id':           'DE*GEF*P1',
    name:            { de: 'Jena Paradies', en: 'Jena Paradise' },
    description:     { en: 'Behind the station.' },
    address:         { street: 'Am Paradiesbahnhof', houseNumber: '1', postalCode: '07743', city: { de: 'Jena' }, country: 'DEU' },
    geoLocation:     { lat: 50.9192, lng: 11.5853 },
    gridConnectionPoint: { '@id': 'GCP1', gridOperatorId: 'DE*NET', name: { en: 'Paradies' }, voltageLevel: 'LowVoltage' },
    hotline:         '+49 3641 0000',
    brandIds:        [ 'B1', 'B2' ],
    created:         '2026-01-01T00:00:00.000Z',
    chargingStations: [
        { '@id': 'DE*GEF*S1', name: { en: 'Left' },  maxPower: 150000, EVSEs: [ { '@id': 'DE*GEF*E1*1' }, { '@id': 'DE*GEF*E1*2' } ] },
        { '@id': 'DE*GEF*S2', name: { en: 'Right' }, EVSEs: [] }
    ]
};

const evse = {
    '@id':               'DE*GEF*E1*1',
    maxPower:            22000,
    maxCurrent:          32,
    chargingConnectors:  [ { '@id': '1', type: 'IEC62196Type2Outlet', lockable: true } ]
};

/** The stand-in POI API. */
function poiAPI({ path, query }: Asked): unknown {

    if (path === '/poi/RNs')
        return listOf([ { '@id': 'rn1', name: { en: 'Roaming Network 1' }, description: { en: 'The first one.' } } ], 1);

    const counts: Record<string, number> = { CPs: 42, CSOs: 3, GOs: 1, EMPs: 2, CSMs: 0, POs: 0 };
    const list = /^\/poi\/RNs\/rn1\/([A-Za-z]+)$/.exec(path);

    if (list && query.get('limit') === '0')
        return listOf([], counts[list[1]!] ?? 0);

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1')
        return entityOf(pool);

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*1')
        return entityOf(evse);

    // An operator without its pools, and its pools from the pools of the roaming network.
    if (path === '/poi/RNs/rn1/CSOs/DE*BIG' && query.get('without') === 'chargingPools')
        return entityOf({ '@id': 'DE*BIG', name: { en: 'Big' } });

    if (path === '/poi/RNs/rn1/CPs' && query.get('operatorId') === 'DE*BIG')
    {
        const limit = Number(query.get('limit') ?? 1000);
        return new Response(JSON.stringify(Array.from({ length: Math.min(limit, 120) }, (_, index) => ({ '@id': `DE*BIG*P${String(index).padStart(3, '0')}`,
                                                                                                         chargingStations: [ { '@id': 'S', EVSEs: [ { '@id': 'E' } ] } ] }))),
                            { status: 200, headers: { 'Content-Type': 'application/json', 'X-Total-Count': '500', 'X-Filtered-Count': '120' } });
    }

    if (path.startsWith('/poi/RNs/rn2'))
        return new Response(JSON.stringify({ description: "The roaming network 'rn2' is unknown!" }),
                            { status: 404, headers: { 'Content-Type': 'application/json' } });

    return undefined;

}

const value = (root: HTMLElement, key: string) =>
    [ ...root.querySelectorAll('.poi-entity .kv') ].find(kv => kv.querySelector('.k')!.textContent!.trim() === key)?.querySelector('.poi-value');


describe('the single things of the points of interest', () => {

    it('find the steps of an entity in its URL, and give a number its unit', () => {

        assert.deepEqual(stepsOf(new URL('http://127.0.0.1/poi/RNs/rn%201/CSOs/DE*GEF/CPs/DE*GEF*P1')),
                         [ { segment: 'RNs',   id: 'rn 1',       path: '/RNs/rn%201' },
                           { segment: 'CSOs',  id: 'DE*GEF',     path: '/RNs/rn%201/CSOs/DE*GEF' },
                           { segment: 'CPs',   id: 'DE*GEF*P1',  path: '/RNs/rn%201/CSOs/DE*GEF/CPs/DE*GEF*P1' } ]);

        assert.equal(withUnit('maxPower',    150000), '150 kW');
        assert.equal(withUnit('maxCurrent',  32),     '32 A');
        assert.equal(withUnit('hotline',     '+49'),  '+49');

        // What it is first, when it changed last, and the rest as the API sent it between.
        assert.deepEqual(inReadingOrder(Object.entries({ address: 1, created: 2, description: 3, geoLocation: 4, lastChange: 5, name: 6, '@id': 7 })).map(([ key ]) => key),
                         [ '@id', 'name', 'description', 'address', 'geoLocation', 'created', 'lastChange' ]);

    });

    it('show a roaming network with how many it holds of each kind, counted by its lists', async () => {

        const root = await open(poiNetworkPage, '/poi/RNs/rn1', null, poiAPI, root => root.querySelector('.poi-holds') !== null);

        assert.equal(root.querySelector('h1')!.textContent!.trim(), 'Roaming Network 1');
        assert.equal(root.querySelector('#poi-count-CPs')!.textContent!.trim(),   '42');
        assert.equal(root.querySelector('#poi-count-CSOs')!.textContent!.trim(),  '3');
        assert.match(root.querySelector('.poi-entity')!.textContent!, /The first one\./);

        assert.equal(root.querySelector<HTMLAnchorElement>('.poi-holds a')!.getAttribute('href'), '/poi/RNs/rn1/CPs');
        assert.ok(asked.filter(one => one.path.startsWith('/poi/RNs/rn1/')).every(one => one.query.get('limit') === '0'),
                  'no list is asked for an entry');

    });

    it('show a charging pool field by field, its grid connection point and its stations leading to their pages', async () => {

        const root = await open(poolPage, '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1', null, poiAPI, root => root.querySelector('.poi-entity') !== null);

        assert.equal(asked[0]!.query.get('expand'), 'gridConnectionPoint', 'asked with its grid connection point embedded');

        assert.match(root.querySelector('h1')!.textContent!, /Jena Paradies|Jena Paradise/);
        assert.equal(value(root, 'Identification')!.textContent!.trim(), 'DE*GEF*P1');
        assert.match(value(root, 'Name')!.textContent!, /Jena Paradies\s+de/);
        assert.equal(value(root, 'Address')!.textContent!.trim(), 'Am Paradiesbahnhof 1, 07743 Jena, DEU');
        assert.match(value(root, 'Geo location')!.querySelector('a')!.getAttribute('href')!, /openstreetmap\.org\/\?mlat=50\.9192&mlon=11\.5853/);
        assert.equal(value(root, 'Grid connection point')!.querySelector('a')!.getAttribute('href'), '/poi/RNs/rn1/GOs/DE*NET/GCPs/GCP1');
        assert.equal(value(root, 'Brand IDs')!.textContent!.trim(), 'B1, B2');

        // Its stations as a table, not as fields.
        assert.equal(value(root, 'Charging stations'), undefined);
        assert.deepEqual([ ...root.querySelectorAll<HTMLAnchorElement>('.poi-children tbody a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1', '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S2' ]);
        assert.deepEqual([ ...root.querySelectorAll('.poi-children tbody tr:first-child td') ].map(cell => cell.textContent!.trim()),
                         [ 'DE*GEF*S1', 'Left', '2', '150 kW' ]);

        // Where it is, every step leading to its page.
        assert.deepEqual([ ...root.querySelectorAll<HTMLAnchorElement>('.poi-path a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs', '/poi/RNs/rn1', '/poi/RNs/rn1/CSOs', '/poi/RNs/rn1/CSOs/DE*GEF' ]);

        // As the API sent it, with its ETag.
        assert.match(root.querySelector('.poi-json summary')!.textContent!, /json:sha256:hex:abc/);
        assert.deepEqual(JSON.parse(root.querySelector('.poi-json pre')!.textContent!), pool);

    });

    it('show an EVSE with its units and its connectors', async () => {

        const root = await open(evsePage, '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*1', null, poiAPI,
                                root => root.querySelector('.poi-entity') !== null);

        assert.equal(value(root, 'Max power')!.textContent!.trim(),   '22 kW');
        assert.equal(value(root, 'Max current')!.textContent!.trim(), '32 A');
        assert.match(root.querySelector('.poi-entity .kv-nested')!.textContent!, /Charging connectors[\s\S]*IEC62196Type2Outlet/);
        assert.equal(root.querySelector('.poi-children'), null, 'an EVSE has no children of its own');

    });

    it('show an operator without its pools, the first of them from the pools of the roaming network, and where to search all of them', async () => {

        const root = await open(operatorPage, '/poi/RNs/rn1/CSOs/DE*BIG', null, poiAPI, root => root.querySelector('.poi-children') !== null);

        // Asked for in short, and for a page of its pools.
        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/CSOs/DE*BIG')!.query.get('without'), 'chargingPools');
        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/CPs')!.query.get('limit'),           '100');

        assert.equal(root.querySelectorAll('.poi-children tbody tr').length, 100);
        assert.deepEqual([ ...root.querySelectorAll('.poi-children tbody tr:first-child td') ].map(cell => cell.textContent!.trim()),
                         [ 'DE*BIG*P000', '', '', '1', '1' ]);
        assert.equal(root.querySelector<HTMLAnchorElement>('.poi-children tbody a')!.getAttribute('href'), '/poi/RNs/rn1/CSOs/DE*BIG/CPs/DE*BIG*P000');
        assert.equal(root.querySelector('.poi-children .chip')!.textContent!.trim(), '120');
        assert.equal(root.querySelector<HTMLAnchorElement>('.poi-children p a')!.getAttribute('href'), '/poi/RNs/rn1/CPs?operatorId=DE*BIG');

    });

    it('say why an entity could not be loaded, in the words of the API', async () => {

        const root = await open(operatorPage, '/poi/RNs/rn2/CSOs/DE*GEF', null, poiAPI, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /The roaming network 'rn2' is unknown!/);

    });

});
