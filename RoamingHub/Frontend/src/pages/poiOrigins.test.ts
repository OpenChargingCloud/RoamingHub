/**
 * Where the values of a member come from that its groups give it: its own ones from nowhere, a single value from its
 * group with the highest priority, the elements of an array each from the first group giving it, and the charging
 * tariffs of an EVSE from its most specific groups.
 */

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { originsOf } = await import('./poiOrigins.ts');

const group = (segment: string, entity: Record<string, unknown>) =>
    ({ group: entity as { '@id': string }, path: `/RNs/rn1/CSOs/DE*GEF/${segment}/${encodeURIComponent(entity['@id'] as string)}` });


describe('where the values of a member come from', () => {

    it('a single value from its group with the highest priority giving one, its own from nowhere', () => {

        const origins = originsOf({ '@id': 'DE*GEF*S1', hotline: '+49 1', modelId: 'M1', maxPower: 22000, lastChange: '2026-10-17' },
                                  { '@id': 'DE*GEF*S1', maxPower: 22000, lastChange: '2026-01-01' },
                                  { groups: [ group('CSGs', { '@id': 'DE*GEF*SG2', hotline: '+49 2' }),
                                              group('CSGs', { '@id': 'DE*GEF*SG1', priority: 5, hotline: '+49 1' }),
                                              group('CSGs', { '@id': 'DE*GEF*SG3', modelId: 'M1', maxPower: 50000 }) ] });

        assert.deepEqual([ ...origins.keys() ], [ 'hotline', 'modelId' ]);
        assert.deepEqual(origins.get('hotline'), { from: [ { id: 'DE*GEF*SG1', path: '/RNs/rn1/CSOs/DE*GEF/CSGs/DE*GEF*SG1' } ], whole: true });
        assert.equal(origins.get('modelId')!.from[0]!.id, 'DE*GEF*SG3');

    });

    it('the elements of an array it has not of its own, each from the first group giving it, by their keys', () => {

        const origins = originsOf({ '@id': 'DE*GEF*P1',
                                    facilities:           [ 'CAFE', 'SUPERMARKET', 'WIFI' ],
                                    authenticationModes:  [ { type: 'RFID', cardTypes: [ 'MIFARE_CLASSIC' ] }, { type: 'APP' } ],
                                    chargingStations:     [ { '@id': 'DE*GEF*S1' } ] },
                                  { '@id': 'DE*GEF*P1',
                                    facilities:           [ 'CAFE' ],
                                    chargingStations:     [] },
                                  { groups: [ group('CPGs', { '@id': 'DE*GEF*PG1', facilities: [ 'SUPERMARKET' ], authenticationModes: [ { type: 'RFID', cardTypes: [ 'MIFARE_CLASSIC' ] } ] }),
                                              group('CPGs', { '@id': 'DE*GEF*PG2', facilities: [ 'SUPERMARKET', 'WIFI' ], authenticationModes: [ { type: 'APP' } ] }) ] },
                                  [ 'chargingStations' ]);

        assert.deepEqual(origins.get('facilities'), { from: [ { id: 'DE*GEF*PG1', path: '/RNs/rn1/CSOs/DE*GEF/CPGs/DE*GEF*PG1', elements: [ 'SUPERMARKET' ] },
                                                              { id: 'DE*GEF*PG2', path: '/RNs/rn1/CSOs/DE*GEF/CPGs/DE*GEF*PG2', elements: [ 'WIFI' ] } ],
                                                      whole: false });

        // An authentication mode by its type; all of them from groups.
        assert.deepEqual(origins.get('authenticationModes')!.from.map(from => [ from.id, from.elements ]), [ [ 'DE*GEF*PG1', [ 'RFID' ] ], [ 'DE*GEF*PG2', [ 'APP' ] ] ]);
        assert.equal(origins.get('authenticationModes')!.whole, true);

        // What it embeds is its own.
        assert.equal(origins.has('chargingStations'), false);

    });

    it('the charging tariffs of an EVSE from its most specific groups giving one, its current types as one value', () => {

        const evseGroups     = [ group('EVSEGs', { '@id': 'DE*GEF*EG1', currentTypes: [ 'AC_ThreePhases' ] }) ];
        const stationGroups  = [ group('CSGs',   { '@id': 'DE*GEF*SG1', chargingTariffId: 'DE*GEF*T2' }) ];
        const poolGroups     = [ group('CPGs',   { '@id': 'DE*GEF*PG1', chargingTariffId: 'DE*GEF*T1' }) ];

        const origins = originsOf({ '@id': 'DE*GEF*E1*1', chargingTariffIds: [ 'DE*GEF*T2' ], currentTypes: [ 'AC_ThreePhases' ] },
                                  { '@id': 'DE*GEF*E1*1' },
                                  { groups: evseGroups, tariffs: [ evseGroups, stationGroups, poolGroups ] });

        assert.deepEqual(origins.get('chargingTariffIds'), { from: [ { id: 'DE*GEF*SG1', path: '/RNs/rn1/CSOs/DE*GEF/CSGs/DE*GEF*SG1' } ], whole: true });
        assert.deepEqual(origins.get('currentTypes'),      { from: [ { id: 'DE*GEF*EG1', path: '/RNs/rn1/CSOs/DE*GEF/EVSEGs/DE*GEF*EG1' } ], whole: true });

    });

});
