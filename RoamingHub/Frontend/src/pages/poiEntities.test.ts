/**
 * The single things of the points of interest drawn, in a document of happy-dom, against a stand-in POI API: a
 * roaming network counted by its lists, and an entity asked at the URL of its page, drawn field by field, with its
 * children leading to theirs.
 */

import { asked, change, open, until, type Asked } from '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { inReadingOrder, poiEntityPages, poiNetworkPage, stepsOf, withUnit } = await import('./poiEntities.ts');

const operatorPage  = poiEntityPages['/poi/RNs/:rn/CSOs/:cso']!;
const poolPage      = poiEntityPages['/poi/RNs/:rn/CSOs/:cso/CPs/:cp']!;
const stationPage   = poiEntityPages['/poi/RNs/:rn/CSOs/:cso/CPs/:cp/CSs/:cs']!;
const gridPage      = poiEntityPages['/poi/RNs/:rn/GOs/:go']!;
const parkingPage   = poiEntityPages['/poi/RNs/:rn/POs/:po']!;
const spacePage     = poiEntityPages['/poi/RNs/:rn/POs/:po/PSs/:id']!;
const poolGroupPage = poiEntityPages['/poi/RNs/:rn/CSOs/:cso/CPGs/:id']!;
const poolTemplatePage = poiEntityPages['/poi/RNs/:rn/CSOs/:cso/CPTs/:id']!;
const providerPage  = poiEntityPages['/poi/RNs/:rn/EMPs/:emp']!;
const offerPage     = poiEntityPages['/poi/RNs/:rn/EMPs/:emp/CTOs/:id']!;
const poolBrandPage = poiEntityPages['/poi/RNs/:rn/CSOs/:cso/CPs/:cp/Brands/:id']!;
const evsePageOf    = (path: string) => open(evsePage, path, null, poiAPI, root => root.querySelector('.poi-entity') !== null);
const evsePage      = poiEntityPages['/poi/RNs/:rn/CSOs/:cso/CPs/:cp/CSs/:cs/EVSEs/:evse']!;
const pointPage     = poiEntityPages['/poi/RNs/:rn/GOs/:go/GCPs/:gcp']!;
const meterTemplatePage = poiEntityPages['/poi/RNs/:rn/GOs/:go/EMTs/:id']!;


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
    brands:          [ { '@id': 'city-charge', name: { en: 'City Charge' }, homepage: 'https://city.example' } ],
    created:         '2026-01-01T00:00:00.000Z',
    chargingStations: [
        { '@id': 'DE*GEF*S1', name: { en: 'Left' },  maxPower: 150000, EVSEs: [ { '@id': 'DE*GEF*E1*1' }, { '@id': 'DE*GEF*E1*2' } ] },
        { '@id': 'DE*GEF*S2', name: { en: 'Right' }, EVSEs: [] }
    ]
};

const evse = {
    '@id':               'DE*GEF*E1*1',
    chargingTariffIds:   [ 'DE*GEF*T1' ],
    maxPower:            22000,
    maxCurrent:          32,
    chargingConnectors:  [ { '@id': '1', type: 'IEC62196Type2Outlet', lockable: true } ]
};

/** The template of the grid meters of a grid operator: of ZAP, hardware version 2.0. */
const gridMeterTemplate = {
    '@id':            'net-grid-meters',
    name:             { en: 'Grid meters' },
    manufacturerId:   'ZAP',
    hardwareVersion:  '2.0'
};

/** The group of all grid meters of a grid operator: a collection. */
const gridMeterGroup = {
    '@id':            'net-all-grid-meters',
    name:             { en: 'All grid meters' },
    energyMeterIds:   [ 'grid-meter' ]
};

/** The template of the EVSE meters of a charging station operator: of a model, signing OCMF. */
const evseMeterTemplate = {
    '@id':             'gef-evse-meters',
    modelId:           'ACME-3000',
    signatureMethods:  [ 'OCMF' ]
};

/** The charging tariff offers covering the EVSE: of its operator, then of a provider. */
const night   = { '@id': 'gef-night',       discount: { percent: 20 }, chargingStationGroupIds: [ 'DE*GEF*SG1' ] };
const summer  = { '@id': 'gef-summer-2026', discount: { percent: 30 }, chargingPoolGroupIds: [ 'DE*GEF*PGA9' ],
                  notBefore: '2026-06-01T00:00:00.000Z', notAfter: '2026-08-31T23:59:59.999Z' };
const hpc     = { '@id': 'gdf-hpc-39',      chargingTariff: { currency: 'EUR', elements: [] }, chargingStationOperatorIds: [ 'DE*ION' ],
                  notBefore: '2026-10-01T00:00:00.000Z' };

/** The time the offers applying at a time are asked for: noon of 12 October, in the time zone of the browser. */
const noon    = new Date(2026, 9, 12, 12, 0).toISOString();

/** The stand-in POI API. */
/** The identity key of the operator of rn1, by its thumbprint. */
const identityKeyId = 'ab'.repeat(32);

// The epochs of rn1: the second one, after the first, with SHA-512 identifications.
const profiles  = { canonicalization: 'styx-canonical-json-v1', identificationHash: 'sha256', commit: 'https://open.charging.cloud/contexts/wwcp+json/roamingNetwork/commit/v3',
                    checkpoint: 'https://open.charging.cloud/contexts/wwcp+json/roamingNetwork/checkpoint/v1', priceStatement: 'https://open.charging.cloud/contexts/wwcp+json/roamingNetwork/priceStatement/v1', signatureAlgorithms: [] };
const epoch1    = { epoch: 1, profiles, status: 'closed', finalCheckpointId: 'ab'.repeat(32) };
const epoch2    = { epoch: 2, profiles: { ...profiles, identificationHash: 'sha512' }, status: 'active',
                    previous: { epoch: 1, checkpointId: 'ab'.repeat(32), eTags: [] } };

// A price statement of the EVSE E1*1, signed by its operator.
const priceStatement = { roamingNetworkId: 'rn1', entityType: 'EVSE', entityId: 'DE*GEF*E1*1',
                         priceStatus: { prices: [ { chargingTariffId: 'DE*GEF*T1', elements: [ { priceComponents: [ { type: 'ENERGY', price: 0.39, stepSize: 1 } ] } ] } ] },
                         validFrom: '2026-10-20T12:00:00.000Z', sequence: 3, previous: 'cd'.repeat(32), role: 'chargingStationOperator',
                         signer: 'DE*GEF', signedAt: '2026-10-20T12:00:00.000Z', algorithm: 'ES256', keyId: 'ef'.repeat(32), value: 'c2ln' };

function poiAPI({ path, query }: Asked): unknown {

    if (path === '/poi/RNs')
        return listOf([ { '@id': 'rn1', name: { en: 'Roaming Network 1' }, description: { en: 'The first one.' } } ], 1);

    // The roaming network without what it holds: what it says of itself, with the ETags of all of it.
    if (path === '/poi/RNs/rn1' && query.get('without') === 'chargingStationManufacturers,chargingStationOperators,eMobilityProviders,gridOperators,parkingOperators')
        return entityOf({ '@context': 'https://open.charging.cloud/contexts/POI3/roamingNetwork', '@id': 'rn1',
                          name: { en: 'Roaming Network 1' }, description: { en: 'The first one.' }, dataSource: 'https://data.example',
                          eTags: [ [ 'json', 'sha256', 'hex', 'abc' ], [ 'cbor', 'sha256', 'hex', 'def' ] ] }, '"json:sha256:hex:slim"');

    const counts: Record<string, number> = { CPs: 42, CSOs: 3, GOs: 1, EMPs: 2, CSMs: 0, POs: 0 };
    const list = /^\/poi\/RNs\/rn1\/([A-Za-z]+)$/.exec(path);

    // What a driver's app scans: the identity key of the operator of rn1.
    if (path === '/poi/RNs/rn1/trustAnchor')
        return entityOf({ roamingNetworkId: 'rn1', identityKeyIds: [ identityKeyId ], uri: `https://hub.example/poi/RNs/rn1?identityKeys=${identityKeyId}` });

    // Its epochs, and the price statements of it and of the EVSE E1*1.
    if (path === '/poi/RNs/rn1/history/epoch')
        return entityOf(epoch2);

    if (path === '/poi/RNs/rn1/history/epochs')
        return listOf([ epoch1, epoch2 ], 2);

    if (path === '/poi/RNs/rn1/history/priceStatements' || path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*1/priceStatements')
        return entityOf({ priceStatements: [ priceStatement ] });

    if (list && query.get('limit') === '1')
        return listOf([], counts[list[1]!] ?? 0);

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1')
        return entityOf(pool);

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*1')
        return entityOf(evse);

    // The offers covering the EVSE: by their status now, those applying now - the night offer in the evening - or at
    // noon, and every one.
    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*1/CTOs')
    {

        const status = query.get('status');

        if (status !== null)
            return status === 'active' ? listOf([ night, hpc ], 2) : listOf([], 0);

        if (query.get('applicable') === 'true')
            return query.get('at') === noon ? listOf([ hpc ], 1) : listOf([ night, hpc ], 2);

        return listOf([ night, summer, hpc ], 3);

    }

    // An operator without its pools, and its pools from the pools of the roaming network.
    if (path === '/poi/RNs/rn1/CSOs/DE*BIG' && query.get('without')?.startsWith('chargingPools,'))
        return entityOf({ '@id': 'DE*BIG', name: { en: 'Big' } });

    // Its tariffs, templates, groups and offers: one tariff, nothing else.
    if (path === '/poi/RNs/rn1/CSOs/DE*BIG/CTs')
        return listOf([ { '@id': 'DE*BIG*T1', currency: 'EUR', elements: [ {}, {} ] } ], 1);

    if (/^\/poi\/RNs\/rn1\/CSOs\/DE\*BIG\/(EVSETs|CSTs|CPTs|CTTs|EMTs|CTGs|CPGs|CSGs|EVSEGs|EMGs|CTOs)$/.test(path))
        return listOf([], 0);

    // A brand the pool also appears under: its own.
    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/Brands/city-charge')
        return entityOf({ '@id': 'city-charge', name: { en: 'City Charge' }, homepage: 'https://city.example' });

    // A group of pools of an operator, naming its pools; a template of pools, naming a tariff.
    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPGs/DE*GEF*PG1')
        return entityOf({ '@id': 'DE*GEF*PG1', chargingPoolIds: [ 'DE*GEF*P1', 'DE*GEF*P2' ] });

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPTs/DE*GEF*PT1')
        return entityOf({ '@id': 'DE*GEF*PT1', chargingTariffId: 'DE*GEF*T1', facilities: [ 'CAFE' ] });

    // An e-mobility provider without its groups and offers, and them from their lists: one offer of operator groups.
    if (path === '/poi/RNs/rn1/EMPs/DE-GDF' && query.get('without'))
        return entityOf({ '@id': 'DE-GDF', name: { en: 'GraphDefined Mobility' } });

    if (/^\/poi\/RNs\/rn1\/EMPs\/DE-GDF\/(EVSEGs|CSGs|CPGs)$/.test(path))
        return listOf([], 0);

    if (path === '/poi/RNs/rn1/EMPs/DE-GDF/CTOs')
        return listOf([ { '@id': 'gdf-hpc-39', discount: { percent: 20 }, chargingStationOperatorIds: [ 'DE*ION' ], notBefore: '2026-10-01T00:00:00.000Z' } ], 1);

    if (path === '/poi/RNs/rn1/EMPs/DE-GDF/CTOs/gdf-hpc-39')
        return entityOf({ '@id': 'gdf-hpc-39', discount: { percent: 20 }, chargingStationOperatorIds: [ 'DE*ION' ], notBefore: '2026-10-01T00:00:00.000Z' });

    if (path === '/poi/RNs/rn1/CPs' && query.get('operatorId') === 'DE*BIG')
    {
        const limit = Number(query.get('limit') ?? 1000);
        return new Response(JSON.stringify(Array.from({ length: Math.min(limit, 120) }, (_, index) => ({ '@id': `DE*BIG*P${String(index).padStart(3, '0')}`,
                                                                                                         chargingStations: [ { '@id': 'S', EVSEs: [ { '@id': 'E' } ] } ] }))),
                            { status: 200, headers: { 'Content-Type': 'application/json', 'X-Total-Count': '500', 'X-Filtered-Count': '120' } });
    }

    // A grid operator without its grid connection points and energy meter templates and groups, and them from their lists.
    if (path === '/poi/RNs/rn1/GOs/DE*NET' && query.get('without') === 'gridConnectionPoints,energyMeterTemplates,energyMeterGroups')
        return entityOf({ '@id': 'DE*NET', name: { en: 'Example Netz' } });

    if (path === '/poi/RNs/rn1/GOs/DE*NET/GCPs')
        return listOf([ { '@id': 'GCP1', name: { de: 'Netzanschluss 1' }, voltageLevel: 'LowVoltage' } ], 1);

    if (path === '/poi/RNs/rn1/GOs/DE*NET/EMTs')
        return listOf([ gridMeterTemplate ], 1);

    if (path === '/poi/RNs/rn1/GOs/DE*NET/EMTs/net-grid-meters')
        return entityOf(gridMeterTemplate);

    if (path === '/poi/RNs/rn1/GOs/DE*NET/EMGs')
        return listOf([ gridMeterGroup ], 1);

    // A grid connection point whose energy meter takes the manufacturer and the hardware version of its template.
    if (path === '/poi/RNs/rn1/GOs/DE*NET/GCPs/GCP1')
        return query.get('view') === 'stored'
                   ? entityOf({ '@id': 'GCP1', voltageLevel: 'LowVoltage', energyMeter: { '@id': 'grid-meter', role: 'grid', templateIds: [ 'net-grid-meters' ] } })
                   : entityOf({ '@id': 'GCP1', voltageLevel: 'LowVoltage', energyMeter: { '@id': 'grid-meter', role: 'grid', templateIds: [ 'net-grid-meters' ], manufacturerId: 'ZAP', hardwareVersion: '2.0' } });

    // An EVSE whose energy meter takes the model and a signature method of its template, named in another spelling, but
    // has no templates of its own.
    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*2')
        return query.get('view') === 'stored'
                   ? entityOf({ '@id': 'DE*GEF*E1*2', maxPower: 22000, energyMeter: { '@id': 'evse-meter-2', serialNumber: 'M-2', templateIds: [ 'GEF-EVSE-METERS' ] } })
                   : entityOf({ '@id': 'DE*GEF*E1*2', maxPower: 22000, energyMeter: { '@id': 'evse-meter-2', serialNumber: 'M-2', templateIds: [ 'GEF-EVSE-METERS' ], modelId: 'ACME-3000', signatureMethods: [ 'OCMF' ] } });

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*2/EVSETs' ||
        path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/CSTs' ||
        path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CPTs')
        return listOf([], 0);

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/EMTs')
        return listOf([ evseMeterTemplate ], 1);

    // A parking operator without anything it embeds, and that from their lists: one space, nothing else.
    if (path === '/poi/RNs/rn1/POs/DE*PAR' && query.get('without'))
        return entityOf({ '@id': 'DE*PAR', name: { en: 'Example Parking' } });

    if (path === '/poi/RNs/rn1/POs/DE*PAR/PSs/space-1')
        return entityOf({ '@id': 'space-1', name: { en: 'By the door' }, parkingGarageId: 'garage-1',
                          parkingSensorIds: [ 'sensor-1', 'sensor-2' ], parkingProductIds: [ 'day' ], chargingStationIds: [ 'DE*GEF*S1' ] });

    if (path === '/poi/RNs/rn1/POs/DE*PAR/PSs')
        return listOf([ { '@id': 'space-1', parkingGarageId: 'garage-1', parkingSensorIds: [ 'sensor-1' ] } ], 1);

    if (/^\/poi\/RNs\/rn1\/POs\/DE\*PAR\/(PGs|PSTs|PSGs|PSNs|PPs)$/.test(path))
        return listOf([], 0);

    // A charging station whose hotline and a feature its template gives it: as it is at runtime, as it is given, its templates.
    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S9')
        return query.get('view') === 'stored'
                   ? entityOf({ '@id': 'DE*GEF*S9', features: [ 'Reservable' ], templateIds: [ 'DE*GEF*ST1' ], EVSEs: [] })
                   : entityOf({ '@id': 'DE*GEF*S9', hotline: '+49 800 424242', features: [ 'Reservable', 'RFIDReader' ], templateIds: [ 'DE*GEF*ST1' ], EVSEs: [] });

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S9/CSTs')
        return listOf([ { '@id': 'DE*GEF*ST1', hotline: '+49 800 424242', features: [ 'RFIDReader' ] } ], 1);

    if (path.startsWith('/poi/RNs/rn2'))
        return new Response(JSON.stringify({ description: "The roaming network 'rn2' is unknown!" }),
                            { status: 404, headers: { 'Content-Type': 'application/json' } });

    return undefined;

}

const value = (root: HTMLElement, key: string) =>
    [ ...root.querySelectorAll('.poi-entity .kv') ].find(kv => kv.querySelector('.k')!.textContent!.trim() === key)?.querySelector('.poi-value');

/** The value of a field of a thing in an entity, e.g. of its energy meter. */
const nestedValue = (root: HTMLElement, thing: string, key: string) =>
    [ ...[ ...root.querySelectorAll('.poi-entity .kv-nested') ].find(block => block.querySelector('.k')!.textContent!.trim() === thing)!.querySelectorAll('.kv') ].
        find(kv => kv.querySelector('.k')!.textContent!.trim() === key)?.querySelector('.poi-value');


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
        assert.ok(asked.filter(one => one.path.startsWith('/poi/RNs/rn1/') && !one.path.endsWith('/trustAnchor') && !one.path.includes('/history/')).every(one => one.query.get('limit') === '1'),
                  'a list is asked for more than the one entry the POI API answers at least');

        // What it says of itself, without what it holds, field by field, and as JSON with its ETag.
        assert.equal(value(root, 'Data source')!.querySelector('a')!.getAttribute('href'), 'https://data.example');
        // Its ETags, of all of it, each as a POI ETag: the API sends [ format, algorithm, encoding, digest ].
        assert.deepEqual([ ...value(root, 'ETags')!.querySelectorAll('code') ].map(code => code.textContent), [ 'json:sha256:hex:abc', 'cbor:sha256:hex:def' ]);
        assert.equal([ ...root.querySelectorAll('.poi-entity .k') ].find(key => key.textContent!.trim() === 'Context'), undefined);
        assert.match(root.querySelector('.poi-json summary')!.textContent!, /json:sha256:hex:slim/);

    });

    it('show the trust anchor of a roaming network as a QR code to scan, its URI to copy', async () => {

        const root   = await open(poiNetworkPage, '/poi/RNs/rn1', null, poiAPI, root => root.querySelector('.poi-trust-anchor svg') !== null);
        const card   = root.querySelector('.poi-trust-anchor')!;

        assert.match(card.querySelector('h2')!.textContent!, /Trust anchor/);
        assert.ok(card.querySelector('.poi-qr svg')!.getAttribute('viewBox'), 'a QR code scaling with its box');
        assert.equal(card.querySelector('code')!.textContent, `https://hub.example/poi/RNs/rn1?identityKeys=${identityKeyId}`);
        assert.deepEqual([ ...card.querySelectorAll('code') ].slice(1).map(code => code.textContent), [ identityKeyId ]);

    });

    it('show the epoch of a roaming network with its profiles and every epoch, and its price statements, each leading to its entity', async () => {

        const root    = await open(poiNetworkPage, '/poi/RNs/rn1', null, poiAPI,
                                   root => root.querySelector('.poi-epochs') !== null && root.querySelector('.poi-price-statements') !== null);
        const epochs  = root.querySelector('.poi-epochs')!;
        const prices  = root.querySelector('.poi-price-statements')!;

        assert.match(epochs.querySelector('h2')!.textContent!,                       /Epoch 2\s*active/);
        assert.match(epochs.textContent!,                                            /Identifications\s*sha512/);
        assert.match(epochs.textContent!,                                            new RegExp(`After\\s*epoch 1,\\s*final checkpoint ${'ab'.repeat(32)}`));
        assert.equal(epochs.querySelectorAll('tbody tr').length,                     2);

        assert.match(prices.querySelector('h2')!.textContent!,                       /Price statements\s*1/);
        assert.match(prices.textContent!,                                            /DE\*GEF\*T1\s*energy 0\.39\/kWh in steps of 1 Wh/);
        assert.equal(prices.querySelector('tbody a')!.getAttribute('href'),          '/poi/RNs/rn1/EVSEs/DE*GEF*E1*1');

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
        // Its own brands as a table, each leading to its page below the pool.
        assert.equal(value(root, 'Brands'), undefined);
        const brands = [ ...root.querySelectorAll('.poi-children') ].find(card => card.querySelector('h2')!.textContent!.includes('Brands it also appears under'))!;
        assert.deepEqual([ ...brands.querySelectorAll('tbody td') ].map(cell => cell.textContent!.trim()), [ 'city-charge', 'City Charge', 'https://city.example' ]);
        assert.equal(brands.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/Brands/city-charge');

        // Its stations as a table, not as fields.
        assert.equal(value(root, 'Charging stations'), undefined);
        assert.deepEqual([ ...root.querySelector('.poi-children')!.querySelectorAll<HTMLAnchorElement>('tbody a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1', '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S2' ]);
        assert.deepEqual([ ...root.querySelector('.poi-children')!.querySelectorAll('tbody tr:first-child td') ].map(cell => cell.textContent!.trim()),
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

    it('show the signed prices of an EVSE', async () => {

        const root   = await open(evsePage, '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*1', null, poiAPI,
                                  root => root.querySelector('.poi-price-statements') !== null);
        const card   = root.querySelector('.poi-price-statements')!;

        assert.match(card.querySelector('h2')!.textContent!,  /Signed prices\s*1/);
        assert.match(card.textContent!,                       /#3\s*after cdcdcdcdcdcd…/);
        assert.match(card.textContent!,                       /until the next one/);

    });

    it('show an operator without its pools, the first of them from the pools of the roaming network, and where to search all of them', async () => {

        const root = await open(operatorPage, '/poi/RNs/rn1/CSOs/DE*BIG', null, poiAPI, root => root.querySelector('.poi-children') !== null);

        // Asked for in short, and for a page of its pools.
        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/CSOs/DE*BIG')!.query.get('without'),
                     'chargingPools,chargingTariffs,EVSETemplates,chargingStationTemplates,chargingPoolTemplates,chargingTariffTemplates,energyMeterTemplates,' +
                     'chargingTariffGroups,chargingPoolGroups,chargingStationGroups,EVSEGroups,energyMeterGroups,chargingTariffOffers');
        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/CPs')!.query.get('limit'),           '100');

        const pools = root.querySelector('.poi-children')!;

        assert.equal(pools.querySelectorAll('tbody tr').length, 100);
        assert.deepEqual([ ...pools.querySelectorAll('tbody tr:first-child td') ].map(cell => cell.textContent!.trim()),
                         [ 'DE*BIG*P000', '', '', '1', '1' ]);
        assert.equal(root.querySelector<HTMLAnchorElement>('.poi-children tbody a')!.getAttribute('href'), '/poi/RNs/rn1/CSOs/DE*BIG/CPs/DE*BIG*P000');
        assert.equal(root.querySelector('.poi-children .chip')!.textContent!.trim(), '120');
        assert.equal(root.querySelector<HTMLAnchorElement>('.poi-children p a')!.getAttribute('href'), '/poi/RNs/rn1/CPs?operatorId=DE*BIG');

    });

    it('show the tariffs, templates, groups and offers of an operator from their lists, and lead from a group to its pools, from a template to its tariff', async () => {

        const root = await open(operatorPage, '/poi/RNs/rn1/CSOs/DE*BIG', null, poiAPI, root => root.querySelectorAll('.poi-children').length === 13);

        assert.deepEqual([ ...root.querySelectorAll('.poi-children h2') ].map(heading => heading.textContent!.replace(/\s+/g, ' ').trim()),
                         [ 'Charging pools 120', 'Charging tariffs 1',
                           'EVSE templates 0', 'Charging station templates 0', 'Charging pool templates 0', 'Charging tariff templates 0', 'Energy meter templates 0',
                           'Charging tariff groups 0', 'Charging pool groups 0', 'Charging station groups 0', 'EVSE groups 0', 'Energy meter groups 0',
                           'Charging tariff offers 0' ]);

        const tariffs = root.querySelectorAll('.poi-children')[1]!;
        assert.deepEqual([ ...tariffs.querySelectorAll('tbody td') ].map(cell => cell.textContent!.trim()), [ 'DE*BIG*T1', '', 'EUR', '', '2' ]);
        assert.equal(tariffs.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/CSOs/DE*BIG/CTs/DE*BIG*T1');

        const group = await open(poolGroupPage, '/poi/RNs/rn1/CSOs/DE*GEF/CPGs/DE*GEF*PG1', null, poiAPI, root => root.querySelector('.poi-entity') !== null);
        const links = (key: string) => [ ...value(group, key)!.querySelectorAll<HTMLAnchorElement>('a') ].map(link => link.getAttribute('href'));

        assert.match(group.querySelector('.page-head')!.textContent!, /Charging pool group DE\*GEF\*PG1/);
        assert.deepEqual(links('Charging pool IDs'),  [ '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1', '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P2' ]);

        const template = await open(poolTemplatePage, '/poi/RNs/rn1/CSOs/DE*GEF/CPTs/DE*GEF*PT1', null, poiAPI, root => root.querySelector('.poi-entity') !== null);

        assert.match(template.querySelector('.page-head')!.textContent!, /Charging pool template DE\*GEF\*PT1/);
        assert.deepEqual([ ...value(template, 'Charging tariff ID')!.querySelectorAll<HTMLAnchorElement>('a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/CSOs/DE*GEF/CTs/DE*GEF*T1' ]);

    });

    it('show an e-mobility provider without its groups and offers, them from their lists, an offer below it', async () => {

        const root = await open(providerPage, '/poi/RNs/rn1/EMPs/DE-GDF', null, poiAPI, root => root.querySelectorAll('.poi-children').length === 4);

        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/EMPs/DE-GDF')!.query.get('without'),  'EVSEGroups,chargingStationGroups,chargingPoolGroups,chargingTariffOffers');
        assert.deepEqual([ ...root.querySelectorAll('.poi-children h2') ].map(heading => heading.textContent!.replace(/\s+/g, ' ').trim()),
                         [ 'EVSE groups 0', 'Charging station groups 0', 'Charging pool groups 0', 'Charging tariff offers 1' ]);

        const offers = root.querySelectorAll('.poi-children')[3]!;
        assert.deepEqual([ ...offers.querySelectorAll('tbody td') ].map(cell => cell.textContent!.trim()), [ 'gdf-hpc-39', '', '20 % less', '2026-10-01T00:00:00.000Z', '' ]);
        assert.equal(offers.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/EMPs/DE-GDF/CTOs/gdf-hpc-39');

        const offer = await open(offerPage, '/poi/RNs/rn1/EMPs/DE-GDF/CTOs/gdf-hpc-39', null, poiAPI, root => root.querySelector('.poi-entity') !== null);
        assert.match(offer.querySelector('.page-head')!.textContent!, /Charging tariff offer gdf-hpc-39/);

    });

    it('show a brand of a charging pool below the pool', async () => {

        const root = await open(poolBrandPage, '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/Brands/city-charge', null, poiAPI, root => root.querySelector('.poi-entity') !== null);

        assert.equal(root.querySelector('h1')!.textContent!.trim(), 'City Charge');
        assert.match(root.querySelector('.page-head')!.textContent!, /Brand city-charge/);
        assert.equal(value(root, 'Homepage')!.querySelector('a')!.getAttribute('href'), 'https://city.example');

        // Where it is: below the pool it is a brand of.
        assert.deepEqual([ ...root.querySelectorAll<HTMLAnchorElement>('.poi-path a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs', '/poi/RNs/rn1', '/poi/RNs/rn1/CSOs', '/poi/RNs/rn1/CSOs/DE*GEF', '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1' ]);

    });

    it('show what the templates of a charging station give it, each value leading to its template', async () => {

        const root = await open(stationPage, '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S9', null, poiAPI, root => root.querySelector('.poi-origin') !== null);

        assert.match(root.querySelector('.poi-origins')!.textContent!, /2 values come from its templates/);

        assert.match(value(root, 'Hotline')!.querySelector('.poi-origin')!.textContent!,          /from template DE\*GEF\*ST1/);
        assert.equal(value(root, 'Hotline')!.querySelector('.poi-origin a')!.getAttribute('href'), '/poi/RNs/rn1/CSOs/DE*GEF/CSTs/DE*GEF*ST1');

        // The templates it names lead to their pages below its operator.
        assert.deepEqual([ ...value(root, 'Template IDs')!.querySelectorAll<HTMLAnchorElement>('a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/CSOs/DE*GEF/CSTs/DE*GEF*ST1' ]);

        // Of an array, the elements its template gives it; its own ones from nowhere.
        assert.match(value(root, 'Features')!.querySelector('.poi-origin')!.textContent!,         /RFIDReader from template DE\*GEF\*ST1/);
        assert.doesNotMatch(value(root, 'Features')!.querySelector('.poi-origin')!.textContent!,  /Reservable/);
        assert.equal(value(root, 'Identification')!.querySelector('.poi-origin'),                  null);

        // Asked as it is given, and for its templates.
        assert.ok(asked.some(one => one.path === '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S9' && one.query.get('view') === 'stored'));

    });

    it('show the charging tariff offers covering an EVSE with their status now, those applying now or at a time, each leading to its page', async () => {

        const root  = await evsePageOf('/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*1');
        const rows  = () => [ ...root.querySelectorAll('.poi-offers tbody tr') ].map(row => [ ...row.querySelectorAll('td') ].map(cell => cell.textContent!.trim()));

        await until(() => rows().length === 3, 'the offers covering the EVSE were not shown');

        // Every one, of the operator first; the summer is over.
        assert.deepEqual(rows(), [ [ 'gef-night',       '', '20 % less',      'active',  '',                         '' ],
                                   [ 'gef-summer-2026', '', '30 % less',      'expired', '2026-06-01T00:00:00.000Z', '2026-08-31T23:59:59.999Z' ],
                                   [ 'gdf-hpc-39',      '', 'a price in EUR', 'active',  '2026-10-01T00:00:00.000Z', '' ] ]);
        assert.match(root.querySelector('.poi-offers h2')!.textContent!, /Charging tariff offers\s+3/);
        assert.equal(root.querySelectorAll('.poi-offers .chip.ok').length, 2);

        // Each leads to its page below its owner, through the roaming network.
        assert.deepEqual([ ...root.querySelectorAll<HTMLAnchorElement>('.poi-offers tbody a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/CTOs/gef-night', '/poi/RNs/rn1/CTOs/gef-summer-2026', '/poi/RNs/rn1/CTOs/gdf-hpc-39' ]);

        // Those applying now: active, without asking for their status.
        asked.length = 0;
        change(root.querySelector<HTMLSelectElement>('#poi-offers-shown')!, 'now');
        await until(() => rows().length === 2, 'those applying now were not shown');

        assert.deepEqual(rows().map(row => [ row[0], row[3] ]), [ [ 'gef-night', 'active' ], [ 'gdf-hpc-39', 'active' ] ]);
        assert.deepEqual(asked.map(one => [ one.query.get('applicable'), one.query.get('at'), one.query.get('status') ]), [ [ 'true', null, null ] ]);

        // Those applying at a time: at noon, asked in UTC.
        change(root.querySelector<HTMLSelectElement>('#poi-offers-shown')!, 'at');
        await until(() => root.querySelector('#poi-offers-at') !== null, 'no time can be chosen');

        change(root.querySelector<HTMLInputElement>('#poi-offers-at')!, '2026-10-12T12:00');
        await until(() => rows().length === 1, 'those applying at noon were not shown');

        assert.deepEqual(rows().map(row => [ row[0], row[3] ]), [ [ 'gdf-hpc-39', 'active' ] ]);
        assert.ok(asked.some(one => one.query.get('applicable') === 'true' && one.query.get('at') === noon));

    });

    it('lead from an EVSE to its tariffs', async () => {

        const root = await evsePageOf('/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*1');

        assert.deepEqual([ ...value(root, 'Charging tariff IDs')!.querySelectorAll<HTMLAnchorElement>('a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/CSOs/DE*GEF/CTs/DE*GEF*T1' ]);

    });

    it('show a grid operator without its grid connection points and energy meter templates and groups, them from their lists', async () => {

        const root = await open(gridPage, '/poi/RNs/rn1/GOs/DE*NET', null, poiAPI, root => root.querySelectorAll('.poi-children').length === 3);

        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/GOs/DE*NET')!.query.get('without'),  'gridConnectionPoints,energyMeterTemplates,energyMeterGroups');
        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/GOs/DE*NET/GCPs')!.query.get('limit'), '100');
        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/GOs/DE*NET/EMTs')!.query.get('limit'), '100');
        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/GOs/DE*NET/EMGs')!.query.get('limit'), '100');

        const [ points, templates, groups ] = [ ...root.querySelectorAll('.poi-children') ];

        assert.deepEqual([ ...points!.querySelectorAll('tbody tr:first-child td') ].map(cell => cell.textContent!.trim()),
                         [ 'GCP1', 'Netzanschluss 1', 'LowVoltage', '' ]);
        assert.equal(points!.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/GOs/DE*NET/GCPs/GCP1');
        assert.equal(points!.querySelector('.chip')!.textContent!.trim(), '1');

        // Its energy meter templates: of which manufacturer and model; its groups: how many meters.
        assert.match(templates!.querySelector('h2')!.textContent!, /Energy meter templates\s+1/);
        assert.deepEqual([ ...templates!.querySelectorAll('tbody tr:first-child td') ].map(cell => cell.textContent!.trim()),
                         [ 'net-grid-meters', 'Grid meters', 'ZAP', '' ]);
        assert.equal(templates!.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/GOs/DE*NET/EMTs/net-grid-meters');

        assert.match(groups!.querySelector('h2')!.textContent!, /Energy meter groups\s+1/);
        assert.deepEqual([ ...groups!.querySelectorAll('tbody tr:first-child td') ].map(cell => cell.textContent!.trim()),
                         [ 'net-all-grid-meters', 'All grid meters', '1' ]);

    });

    it('show an energy meter template below its grid operator, its manufacturer leading to no charging station manufacturer', async () => {

        const root = await open(meterTemplatePage, '/poi/RNs/rn1/GOs/DE*NET/EMTs/net-grid-meters', null, poiAPI, root => root.querySelector('.poi-entity') !== null);

        assert.equal(root.querySelector('h1')!.textContent!.trim(), 'Grid meters');
        assert.match(root.querySelector('.page-head')!.textContent!, /Energy meter template net-grid-meters/);
        assert.equal(value(root, 'Manufacturer ID')!.textContent!.trim(),        'ZAP');
        assert.equal(value(root, 'Manufacturer ID')!.querySelector('a'),         null);

        assert.deepEqual([ ...root.querySelectorAll<HTMLAnchorElement>('.poi-path a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs', '/poi/RNs/rn1', '/poi/RNs/rn1/GOs', '/poi/RNs/rn1/GOs/DE*NET' ]);

    });

    it('show what the energy meter template of a grid operator gives the meter of a grid connection point', async () => {

        const root = await open(pointPage, '/poi/RNs/rn1/GOs/DE*NET/GCPs/GCP1', null, poiAPI, root => root.querySelector('.poi-origin') !== null);

        assert.match(root.querySelector('.poi-origins')!.textContent!, /2 values come from its templates/);

        assert.match(nestedValue(root, 'Energy meter', 'Manufacturer ID')!.querySelector('.poi-origin')!.textContent!,   /from template net-grid-meters/);
        assert.equal(nestedValue(root, 'Energy meter', 'Manufacturer ID')!.querySelector('.poi-origin a')!.getAttribute('href'),
                     '/poi/RNs/rn1/GOs/DE*NET/EMTs/net-grid-meters');
        assert.match(nestedValue(root, 'Energy meter', 'Hardware version')!.querySelector('.poi-origin')!.textContent!,  /from template net-grid-meters/);
        assert.deepEqual([ ...nestedValue(root, 'Energy meter', 'Template IDs')!.querySelectorAll<HTMLAnchorElement>('a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/GOs/DE*NET/EMTs/net-grid-meters' ]);
        assert.equal(nestedValue(root, 'Energy meter', 'Role')!.querySelector('.poi-origin'),                              null);

        // The manufacturer of an energy meter is no charging station manufacturer.
        assert.equal(nestedValue(root, 'Energy meter', 'Manufacturer ID')!.querySelector('code')!.closest('a'),           null);

        // Asked as it is given, and for the energy meter templates of its grid operator.
        assert.ok(asked.some(one => one.path === '/poi/RNs/rn1/GOs/DE*NET/GCPs/GCP1' && one.query.get('view') === 'stored'));

    });

    it('show what the energy meter template of an operator gives the meter of an EVSE', async () => {

        const root = await open(evsePage, '/poi/RNs/rn1/CSOs/DE*GEF/CPs/DE*GEF*P1/CSs/DE*GEF*S1/EVSEs/DE*GEF*E1*2', null, poiAPI,
                                root => root.querySelector('.poi-origin') !== null);

        assert.match(root.querySelector('.poi-origins')!.textContent!, /2 values come from its templates/);

        assert.match(nestedValue(root, 'Energy meter', 'Model ID')!.querySelector('.poi-origin')!.textContent!,           /from template gef-evse-meters/);
        assert.equal(nestedValue(root, 'Energy meter', 'Model ID')!.querySelector('.poi-origin a')!.getAttribute('href'),
                     '/poi/RNs/rn1/CSOs/DE*GEF/EMTs/gef-evse-meters');
        // All of an array it has none of its own of: from its template, as a whole.
        assert.match(nestedValue(root, 'Energy meter', 'Signature methods')!.querySelector('.poi-origin')!.textContent!,  /^\s*from template gef-evse-meters\s*$/);
        assert.equal(nestedValue(root, 'Energy meter', 'Serial number')!.querySelector('.poi-origin'),                     null);
        assert.equal(value(root, 'Max power')!.querySelector('.poi-origin'),                                                null);

    });

    it('show a parking operator without what it embeds, each from its list, each leading to its page', async () => {

        const root = await open(parkingPage, '/poi/RNs/rn1/POs/DE*PAR', null, poiAPI, root => root.querySelectorAll('.poi-children').length === 6);

        assert.equal(asked.find(one => one.path === '/poi/RNs/rn1/POs/DE*PAR')!.query.get('without'),
                     'parkingGarages,parkingSpaces,parkingSpaceTemplates,parkingSpaceGroups,parkingSensors,parkingProducts');

        assert.deepEqual([ ...root.querySelectorAll('.poi-children h2') ].map(heading => heading.textContent!.replace(/\s+/g, ' ').trim()),
                         [ 'Parking garages 0', 'Parking spaces 1', 'Parking space templates 0', 'Parking space groups 0', 'Parking sensors 0', 'Parking products 0' ]);

        const spaces = root.querySelectorAll('.poi-children')[1]!;
        assert.deepEqual([ ...spaces.querySelectorAll('tbody td') ].map(cell => cell.textContent!.trim()),
                         [ 'space-1', '', 'garage-1', '1', '0' ]);
        assert.equal(spaces.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/POs/DE*PAR/PSs/space-1');

    });

    it('show a parking space below its parking operator, leading to its garage, sensors and products', async () => {

        const root = await open(spacePage, '/poi/RNs/rn1/POs/DE*PAR/PSs/space-1', null, poiAPI, root => root.querySelector('.poi-entity') !== null);

        assert.equal(root.querySelector('h1')!.textContent!.trim(), 'By the door');
        assert.match(root.querySelector('.page-head')!.textContent!, /Parking space space-1/);

        const links = (key: string) => [ ...value(root, key)!.querySelectorAll<HTMLAnchorElement>('a') ].map(link => link.getAttribute('href'));

        assert.deepEqual(links('Parking garage ID'),   [ '/poi/RNs/rn1/POs/DE*PAR/PGs/garage-1' ]);
        assert.deepEqual(links('Parking sensor IDs'),  [ '/poi/RNs/rn1/POs/DE*PAR/PSNs/sensor-1', '/poi/RNs/rn1/POs/DE*PAR/PSNs/sensor-2' ]);
        assert.deepEqual(links('Parking product IDs'), [ '/poi/RNs/rn1/POs/DE*PAR/PPs/day' ]);
        // A charging station is not below the parking operator: found in the roaming network, which leads to it.
        assert.deepEqual(links('Charging station IDs'), [ '/poi/RNs/rn1/CSs/DE*GEF*S1' ]);

        // Where it is: below its parking operator.
        assert.deepEqual([ ...root.querySelectorAll<HTMLAnchorElement>('.poi-path a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs', '/poi/RNs/rn1', '/poi/RNs/rn1/POs', '/poi/RNs/rn1/POs/DE*PAR' ]);

    });

    it('say why an entity could not be loaded, in the words of the API', async () => {

        const root = await open(operatorPage, '/poi/RNs/rn2/CSOs/DE*GEF', null, poiAPI, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /The roaming network 'rn2' is unknown!/);

    });

});
