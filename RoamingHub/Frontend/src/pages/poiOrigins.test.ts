/**
 * Where the values of a member come from that its templates give it: its own ones from nowhere, a single value from the
 * first of its templates giving one, the elements of an array each from the first template giving it, the charging
 * tariffs of an EVSE from its most specific templates, and the values of an energy meter from the templates it names.
 */

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { originsOf, meterOriginsOf } = await import('./poiOrigins.ts');

const template = (segment: string, entity: Record<string, unknown>) =>
    ({ template: entity as { '@id': string }, path: `/RNs/rn1/CSOs/DE*GEF/${segment}/${encodeURIComponent(entity['@id'] as string)}` });


describe('where the values of a member come from', () => {

    it('a single value from the first of its templates giving one, its own from nowhere', () => {

        const origins = originsOf({ '@id': 'DE*GEF*S1', hotline: '+49 2', modelId: 'M1', maxPower: 22000, templateIds: [ 'DE*GEF*ST2', 'DE*GEF*ST1', 'DE*GEF*ST3' ], lastChange: '2026-10-17' },
                                  { '@id': 'DE*GEF*S1', maxPower: 22000, templateIds: [ 'DE*GEF*ST2', 'DE*GEF*ST1', 'DE*GEF*ST3' ], lastChange: '2026-01-01' },
                                  { templates: [ template('CSTs', { '@id': 'DE*GEF*ST2', hotline: '+49 2' }),
                                                 template('CSTs', { '@id': 'DE*GEF*ST1', hotline: '+49 1' }),
                                                 template('CSTs', { '@id': 'DE*GEF*ST3', modelId: 'M1', maxPower: 50000 }) ] });

        assert.deepEqual([ ...origins.keys() ], [ 'hotline', 'modelId' ]);
        assert.deepEqual(origins.get('hotline'), { from: [ { id: 'DE*GEF*ST2', path: '/RNs/rn1/CSOs/DE*GEF/CSTs/DE*GEF*ST2' } ], whole: true });
        assert.equal(origins.get('modelId')!.from[0]!.id, 'DE*GEF*ST3');

    });

    it('the elements of an array it has not of its own, each from the first template giving it, by their keys', () => {

        const origins = originsOf({ '@id': 'DE*GEF*P1',
                                    facilities:           [ 'CAFE', 'SUPERMARKET', 'WIFI' ],
                                    authenticationModes:  [ { type: 'RFID', cardTypes: [ 'MIFARE_CLASSIC' ] }, { type: 'APP' } ],
                                    chargingStations:     [ { '@id': 'DE*GEF*S1' } ] },
                                  { '@id': 'DE*GEF*P1',
                                    facilities:           [ 'CAFE' ],
                                    chargingStations:     [] },
                                  { templates: [ template('CPTs', { '@id': 'DE*GEF*PT1', facilities: [ 'SUPERMARKET' ], authenticationModes: [ { type: 'RFID', cardTypes: [ 'MIFARE_CLASSIC' ] } ] }),
                                                 template('CPTs', { '@id': 'DE*GEF*PT2', facilities: [ 'SUPERMARKET', 'WIFI' ], authenticationModes: [ { type: 'APP' } ] }) ] },
                                  [ 'chargingStations' ]);

        assert.deepEqual(origins.get('facilities'), { from: [ { id: 'DE*GEF*PT1', path: '/RNs/rn1/CSOs/DE*GEF/CPTs/DE*GEF*PT1', elements: [ 'SUPERMARKET' ] },
                                                              { id: 'DE*GEF*PT2', path: '/RNs/rn1/CSOs/DE*GEF/CPTs/DE*GEF*PT2', elements: [ 'WIFI' ] } ],
                                                      whole: false });

        // An authentication mode by its type; all of them from templates.
        assert.deepEqual(origins.get('authenticationModes')!.from.map(from => [ from.id, from.elements ]), [ [ 'DE*GEF*PT1', [ 'RFID' ] ], [ 'DE*GEF*PT2', [ 'APP' ] ] ]);
        assert.equal(origins.get('authenticationModes')!.whole, true);

        // What it embeds is its own.
        assert.equal(origins.has('chargingStations'), false);

    });

    it('the charging tariffs of an EVSE from its most specific templates naming one, its current types as one value', () => {

        const evseTemplates     = [ template('EVSETs', { '@id': 'DE*GEF*ET1', currentTypes: [ 'AC_ThreePhases' ] }) ];
        const stationTemplates  = [ template('CSTs',   { '@id': 'DE*GEF*ST1', chargingTariffId: 'DE*GEF*T2' }) ];
        const poolTemplates     = [ template('CPTs',   { '@id': 'DE*GEF*PT1', chargingTariffId: 'DE*GEF*T1' }) ];

        const origins = originsOf({ '@id': 'DE*GEF*E1*1', chargingTariffIds: [ 'DE*GEF*T2' ], currentTypes: [ 'AC_ThreePhases' ] },
                                  { '@id': 'DE*GEF*E1*1' },
                                  { templates: evseTemplates, tariffs: [ evseTemplates, stationTemplates, poolTemplates ] });

        assert.deepEqual(origins.get('chargingTariffIds'), { from: [ { id: 'DE*GEF*ST1', path: '/RNs/rn1/CSOs/DE*GEF/CSTs/DE*GEF*ST1' } ], whole: true });
        assert.deepEqual(origins.get('currentTypes'),      { from: [ { id: 'DE*GEF*ET1', path: '/RNs/rn1/CSOs/DE*GEF/EVSETs/DE*GEF*ET1' } ], whole: true });

    });

    it('the prices of a charging tariff from its template, its elements as one value', () => {

        const origins = originsOf({ '@id': 'DE*GEF*T1', currency: 'EUR', elements: [ { priceComponents: [ { type: 'ENERGY', price: 0.39 } ] } ] },
                                  { '@id': 'DE*GEF*T1' },
                                  { templates: [ template('CTTs', { '@id': 'DE*GEF*TT1', currency: 'EUR', elements: [ { priceComponents: [ { type: 'ENERGY', price: 0.39 } ] } ] }) ] });

        assert.deepEqual(origins.get('currency'), { from: [ { id: 'DE*GEF*TT1', path: '/RNs/rn1/CSOs/DE*GEF/CTTs/DE*GEF*TT1' } ], whole: true });
        assert.deepEqual(origins.get('elements'), { from: [ { id: 'DE*GEF*TT1', path: '/RNs/rn1/CSOs/DE*GEF/CTTs/DE*GEF*TT1' } ], whole: true });

    });

    it('the values of an energy meter from the templates it names, in its order', () => {

        const templates = [ template('EMTs', { '@id': 'acme', manufacturerId: 'ACME', modelId: 'ACME-3000' }),
                            template('EMTs', { '@id': 'zap',  manufacturerId: 'ZAP' }) ];

        const origins = meterOriginsOf({ '@id': 'DE*GEF*E1*1', energyMeter: { '@id': 'meter-1', manufacturerId: 'ZAP', modelId: 'ACME-3000', templateIds: [ 'ZAP', 'acme' ] } },
                                       { '@id': 'DE*GEF*E1*1', energyMeter: { '@id': 'meter-1', templateIds: [ 'ZAP', 'acme' ] } },
                                       templates);

        assert.deepEqual(origins.get('meter-1')!.get('manufacturerId')!.from.map(from => from.id), [ 'zap' ]);
        assert.deepEqual(origins.get('meter-1')!.get('modelId')!.from.map(from => from.id),        [ 'acme' ]);

    });

});
