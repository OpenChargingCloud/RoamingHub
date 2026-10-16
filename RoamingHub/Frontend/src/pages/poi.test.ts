/**
 * The points of interest drawn, in a document of happy-dom, against a stand-in POI API: a list at the URL of the
 * page, searched and paged by asking that URL again, and counted by what the API says in X-Total-Count and
 * X-Filtered-Count.
 */

import { asked, open, until, type Asked } from '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { addressLine, gridOperatorOf, poiChargingPoolsPage, poiGridConnectionPointsPage, poiNetworksPage, poiOperatorListPages, poiOperatorsPage, poiParkingListPages, queryOf, roamingNetworkOf } = await import('./poi.ts');


/** A part of a list, as the POI API answers: the entries, with the counts in the headers. */
function listOf(entries: unknown[], total: number, filtered = total): Response {
    return new Response(JSON.stringify(entries), {
               status:   200,
               headers:  { 'Content-Type': 'application/json', 'X-Total-Count': String(total), 'X-Filtered-Count': String(filtered) }
           });
}

const pools = [
    { '@id': 'DE*EXA*P1', name: { de: 'Jena Nord' },     chargingStationOperator: { '@id': 'DE*EXA', name: { en: 'Example Charging' } },
      address: { street: 'Nordstraße', houseNumber: '1', postalCode: '07743', city: { de: 'Jena' }, country: 'DEU' },
      chargingStations: [ { '@id': 'DE*EXA*S1', EVSEs: [ { '@id': 'DE*EXA*E1*1' }, { '@id': 'DE*EXA*E1*2' } ] } ] },
    { '@id': 'DE*GEF*P1', name: { de: 'Jena Paradies' }, chargingStationOperator: { '@id': 'DE*GEF', name: { en: 'GraphDefined' } } },
    { '@id': 'DE*GEF*P2', name: { de: 'Berlin Mitte' },  chargingStationOperator: { '@id': 'DE*GEF', name: { en: 'GraphDefined' } } }
];

const operators = [
    { '@id': 'DE*EXA', name: { en: 'Example Charging' } },
    { '@id': 'DE*GEF', name: { en: 'GraphDefined' } }
];

/** The stand-in POI API: the pools searched by name and operator, narrowed to an operator, and paged. */
function poiAPI({ path, query }: Asked): unknown {

    if (path === '/poi/RNs')
        return listOf([ { '@id': 'rn1', name: { en: 'Roaming Network 1' } } ], 1);

    if (path === '/poi/RNs/rn1/CSOs')
        return listOf(operators, 2);

    if (path === '/poi/RNs/rn1/CPs')
    {

        const words     = (query.get('match') ?? '').toLowerCase().split(' ').filter(Boolean);
        const operator  = query.get('operatorId');
        const matching  = pools.filter(pool => (!operator || pool.chargingStationOperator['@id'] === operator) &&
                                               words.every(word => JSON.stringify(pool).toLowerCase().includes(word)));
        const offset    = Number(query.get('offset') ?? 0);
        const limit     = Number(query.get('limit')  ?? 1000);

        return listOf(matching.slice(offset, offset + limit), pools.length, matching.length);

    }

    if (path === '/poi/RNs/rn1/GOs/DE*NET/GCPs')
        return listOf([ { '@id': 'GCP1', name: { de: 'Netzanschluss 1' }, voltageLevel: 'LowVoltage',
                          address: { street: 'Netzweg', houseNumber: '1', postalCode: '07743', city: { de: 'Jena' }, country: 'DEU' } } ], 1);

    if (path === '/poi/RNs/rn1/CSOs/DE*GEF/EVSEGs')
        return listOf([ { '@id': 'DE*GEF*EG1', EVSEIds: [ 'DE*GEF*E1*1', 'DE*GEF*E1*2' ], chargingTariffId: 'DE*GEF*T1' } ], 1);

    if (path === '/poi/RNs/rn1/POs/DE*PAR/PSs')
        return listOf([ { '@id': 'space-1', name: { en: 'By the door' }, parkingGarageId: 'garage-1', chargingStationIds: [ 'S1', 'S2' ] } ], 1);

    if (path === '/poi/RNs/rn2/CSOs')
        return new Response(JSON.stringify({ description: "The roaming network 'rn2' is unknown!" }),
                            { status: 404, headers: { 'Content-Type': 'application/json' } });

    return undefined;

}

const rows   = (root: HTMLElement) => [ ...root.querySelectorAll('tbody tr') ].map(row => row.querySelector('code')!.textContent);
const count  = (root: HTMLElement) => root.querySelector('#poi-count')!.textContent!.trim();


describe('the points of interest', () => {

    it('read the roaming network and the query of a page from its URL', () => {

        const url = new URL('http://127.0.0.1/poi/RNs/rn%201/CPs?match=jena&offset=50&limit=10&operatorId=DE*GEF');

        assert.equal(roamingNetworkOf(url), 'rn 1');
        assert.deepEqual(queryOf(url), { match: 'jena', offset: 50, limit: 10, operatorId: 'DE*GEF' });
        assert.deepEqual(queryOf(new URL('http://127.0.0.1/poi/RNs/rn1/CPs?offset=-3&limit=x')),
                         { match: '', offset: 0, limit: 25, operatorId: undefined });

        assert.equal(addressLine({ street: 'Am Paradiesbahnhof', houseNumber: '1', postalCode: '07743', city: { de: 'Jena' }, country: 'DEU' }),
                     'Am Paradiesbahnhof 1, 07743 Jena, DEU');

    });

    it('list the roaming networks, each leading to its page', async () => {

        const root = await open(poiNetworksPage, '/poi/RNs', null, poiAPI, root => root.querySelector('tbody') !== null);

        assert.deepEqual(rows(root), [ 'rn1' ]);
        assert.equal(root.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1');
        assert.equal(count(root), '1 - 1 of 1');
        assert.equal(asked[0]!.query.get('limit'), '25');

    });

    it('list the charging pools of every operator, with their operators, addresses and EVSEs', async () => {

        const root = await open(poiChargingPoolsPage, '/poi/RNs/rn1/CPs', null, poiAPI, root => root.querySelector('tbody') !== null);

        assert.deepEqual(rows(root), [ 'DE*EXA*P1', 'DE*GEF*P1', 'DE*GEF*P2' ]);
        assert.equal(count(root), '1 - 3 of 3');

        const first = root.querySelector('tbody tr')!.textContent!.replace(/\s+/g, ' ');
        assert.match(first, /Jena Nord/);
        assert.match(first, /Example Charging DE\*EXA/);
        assert.match(first, /Nordstraße 1, 07743 Jena, DEU/);
        assert.match(first, / 1 2 $/, 'one station with two EVSEs');

        // Each pool leading to its page, below its operator, and its operator to its own.
        assert.deepEqual([ ...root.querySelectorAll<HTMLAnchorElement>('tbody tr:first-child a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/CSOs/DE*EXA/CPs/DE*EXA*P1', '/poi/RNs/rn1/CSOs/DE*EXA' ]);

        // The operators to narrow the list to, beside every operator.
        assert.deepEqual([ ...root.querySelectorAll('#poi-operator option') ].map(option => option.textContent!.trim()),
                         [ 'every operator', 'Example Charging', 'GraphDefined' ]);

        // The sections of the roaming network, the charging pools marked.
        assert.equal(root.querySelector('.poi-sections a.active')!.textContent!.trim(), 'Charging pools');

    });

    it('search as the API does, and say how many were found among all', async () => {

        const root   = await open(poiChargingPoolsPage, '/poi/RNs/rn1/CPs', null, poiAPI, root => root.querySelector('tbody') !== null);
        const search = root.querySelector<HTMLInputElement>('#poi-search')!;

        search.value = 'jena';
        search.dispatchEvent(new Event('search'));

        await until(() => rows(root).length === 2, 'the search did not narrow the list');

        assert.deepEqual(rows(root), [ 'DE*EXA*P1', 'DE*GEF*P1' ]);
        assert.equal(count(root), '1 - 2 of 2, found among 3');
        assert.equal(asked.at(-1)!.query.get('match'), 'jena');
        assert.equal(asked.at(-1)!.query.get('offset'), null, 'a new search starts at the first page');

        // The address bar says what is shown.
        assert.equal(location.pathname + location.search, '/poi/RNs/rn1/CPs?match=jena');

        search.value = 'hamburg';
        search.dispatchEvent(new Event('search'));

        await until(() => root.querySelector('.hint') !== null, 'a search finding nothing said nothing');
        assert.match(root.querySelector('.hint')!.textContent!, /Nothing matches the search/);

    });

    it('narrow to an operator, and turn the pages', async () => {

        const root = await open(poiChargingPoolsPage, '/poi/RNs/rn1/CPs', null, poiAPI, root => root.querySelector('#poi-operator option[value="DE*GEF"]') !== null);

        const operator = root.querySelector<HTMLSelectElement>('#poi-operator')!;
        operator.value = 'DE*GEF';
        operator.dispatchEvent(new Event('change'));

        await until(() => rows(root).join() === 'DE*GEF*P1,DE*GEF*P2', 'the operator did not narrow the list');
        assert.equal(asked.at(-1)!.query.get('operatorId'), 'DE*GEF');

        const limit = root.querySelector<HTMLSelectElement>('#poi-limit')!;
        limit.value = '10';
        limit.dispatchEvent(new Event('change'));

        await until(() => asked.at(-1)!.query.get('limit') === '10', 'the size of a page was not asked for');

        const previous  = () => root.querySelector<HTMLButtonElement>('#poi-previous')!;
        const next      = () => root.querySelector<HTMLButtonElement>('#poi-next')!;

        await until(() => !next().disabled || rows(root).length === 2, 'the list was not drawn again');
        assert.ok(previous().disabled, 'there is no page before the first');
        assert.ok(next().disabled,     'there is no page after the last');

    });

    it('say why a list could not be loaded, in the words of the API', async () => {

        const root = await open(poiOperatorsPage, '/poi/RNs/rn2/CSOs', null, poiAPI, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /The roaming network 'rn2' is unknown!/);

    });

    it('list the grid connection points of a grid operator, each leading to its page', async () => {

        assert.equal(gridOperatorOf(new URL('http://127.0.0.1/poi/RNs/rn1/GOs/DE*NET/GCPs')), 'DE*NET');

        const root = await open(poiGridConnectionPointsPage, '/poi/RNs/rn1/GOs/DE*NET/GCPs', null, poiAPI, root => root.querySelector('tbody') !== null);

        assert.equal(root.querySelector('h1')!.textContent!.trim(), 'Grid connection points of DE*NET');
        assert.deepEqual([ ...root.querySelectorAll('tbody tr td') ].map(cell => cell.textContent!.trim()),
                         [ 'GCP1', 'Netzanschluss 1', 'LowVoltage', 'Netzweg 1, 07743 Jena, DEU' ]);
        assert.equal(root.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/GOs/DE*NET/GCPs/GCP1');
        assert.equal(root.querySelector('.poi-sections a.active')!.textContent!.trim(), 'Grid operators');

    });

    it('list the EVSE groups of a charging station operator, each leading to its page', async () => {

        const root = await open(poiOperatorListPages['/poi/RNs/:rn/CSOs/:cso/EVSEGs']!, '/poi/RNs/rn1/CSOs/DE*GEF/EVSEGs', null, poiAPI, root => root.querySelector('tbody') !== null);

        assert.equal(root.querySelector('h1')!.textContent!.trim(), 'EVSE groups of DE*GEF');
        assert.deepEqual([ ...root.querySelectorAll('tbody tr td') ].map(cell => cell.textContent!.trim()),
                         [ 'DE*GEF*EG1', '', '2', 'DE*GEF*T1' ]);
        assert.equal(root.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/CSOs/DE*GEF/EVSEGs/DE*GEF*EG1');
        assert.equal(root.querySelector('.poi-sections a.active')!.textContent!.trim(), 'Charging station operators');

    });

    it('list the parking spaces of a parking operator, each leading to its page', async () => {

        const root = await open(poiParkingListPages['/poi/RNs/:rn/POs/:po/PSs']!, '/poi/RNs/rn1/POs/DE*PAR/PSs', null, poiAPI, root => root.querySelector('tbody') !== null);

        assert.equal(root.querySelector('h1')!.textContent!.trim(), 'Parking spaces of DE*PAR');
        assert.deepEqual([ ...root.querySelectorAll('tbody tr td') ].map(cell => cell.textContent!.trim()),
                         [ 'space-1', 'By the door', 'garage-1', '0', '2' ]);
        assert.equal(root.querySelector<HTMLAnchorElement>('tbody a')!.getAttribute('href'), '/poi/RNs/rn1/POs/DE*PAR/PSs/space-1');
        assert.equal(root.querySelector('.poi-sections a.active')!.textContent!.trim(), 'Parking operators');

    });

    it('lead from a list in short to the page of each entry', async () => {

        const root = await open(poiOperatorsPage, '/poi/RNs/rn1/CSOs', null, poiAPI, root => root.querySelector('tbody') !== null);

        assert.deepEqual([ ...root.querySelectorAll<HTMLAnchorElement>('tbody a') ].map(link => link.getAttribute('href')),
                         [ '/poi/RNs/rn1/CSOs/DE*EXA', '/poi/RNs/rn1/CSOs/DE*GEF' ]);

    });

});
