/**
 * The price statements of a roaming network: what the POI API answers is read only as price statements, the latest
 * signed first, and each is shown with its prices, how long it holds, its link in the chain and who signed it.
 */

import '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { componentsText, priceStatementsCard, priceStatementsOf, priceStatementsShown, signedPricesCard } = await import('./poiPrices.ts');
const { render } = await import('@node/view');


/** A price statement of the given entity, signed at the given second, as the POI API answers it. */
function statement(EntityType: string, EntityId: string, Second: number, Extra: Record<string, unknown> = {}): Record<string, unknown> {
    return {
        roamingNetworkId:  'rn1',
        entityType:        EntityType,
        entityId:          EntityId,
        priceStatus:       { prices: [ { chargingTariffId: 'DE*GEF*T1', elements: [ { priceComponents: [ { type: 'ENERGY', price: 0.39, stepSize: 1 } ] } ] } ] },
        validFrom:         `2026-10-20T12:00:${String(Second).padStart(2, '0')}.000Z`,
        sequence:          Second,
        role:              'chargingStationOperator',
        signer:            'DE*GEF',
        signedAt:          `2026-10-20T12:00:${String(Second).padStart(2, '0')}.000Z`,
        algorithm:         'ES256',
        keyId:             'ab'.repeat(32),
        value:             'c2lnbmF0dXJl',
        ...Extra
    };
}


describe('the price statements of a roaming network', () => {

    it('are read only from what are price statements, the latest signed first', () => {

        const read = priceStatementsOf({ priceStatements: [ statement('EVSE',            'DE*GEF*E1*1', 1),
                                                            statement('ChargingStation', 'DE*GEF*S1',   3, { previous: 'cd'.repeat(32), validUntil: '2026-10-21T00:00:00.000Z' }),
                                                            statement('Tariff',          'DE*GEF*T1',   2),
                                                            { entityType: 'EVSE' },
                                                            null ] });

        assert.deepEqual(read.map(one => one.entityId),  [ 'DE*GEF*S1', 'DE*GEF*E1*1' ]);
        assert.equal(read[0]!.previous,                  'cd'.repeat(32));
        assert.equal(read[0]!.validUntil,                '2026-10-21T00:00:00.000Z');
        assert.equal(read[1]!.validUntil,                undefined);

        assert.deepEqual(priceStatementsOf(null),        []);
        assert.deepEqual(priceStatementsOf({}),          []);
        assert.deepEqual(priceStatementsOf([ statement('EVSE', 'DE*GEF*E1*1', 1) ]), [], 'an array is no answer of the POI API');

    });

    it('tell their prices in one line per element', () => {

        assert.equal(componentsText([ { type: 'ENERGY', price: 0.39, stepSize: 1 }, { type: 'FLAT', price: 2.5 } ]),  'energy 0.39/kWh in steps of 1 Wh, flat 2.5');
        assert.equal(componentsText([ { type: 'TIME', price: 3, stepSize: 60 }, { type: 'PARKING_TIME', price: 1.2, stepSize: 900 } ]),
                     'charging time 3/h in steps of 60 s, parking time 1.2/h in steps of 900 s');
        assert.equal(componentsText([ { type: 'SOMETHING', price: 1 } ]),  'something 1', 'a type it does not know, as it is');

    });

    it('are shown with their entities, prices, validity, chain and signer, each entity leading to its page', () => {

        const root = document.createElement('div');

        render(root, priceStatementsCard(priceStatementsOf({ priceStatements: [ statement('EVSE', 'DE*GEF*E1*1', 1),
                                                                         statement('ChargingStation', 'DE*GEF*S1', 2, { previous: 'cd'.repeat(32) }) ] }),
                                   '/RNs/rn1'));

        const text  = root.textContent ?? '';
        const links = [ ...root.querySelectorAll('a') ].map(link => link.getAttribute('href'));

        assert.match(text,  /Price statements\s*2/);
        assert.match(text,  /DE\*GEF\*T1\s*energy 0\.39\/kWh in steps of 1 Wh/);
        assert.match(text,  /until the next one/);
        assert.match(text,  /#2\s*after cdcdcdcdcdcd…/);
        assert.match(text,  /DE\*GEF\s*\S.*2026.*\s*key abababababab…/);
        assert.deepEqual(links, [ '/poi/RNs/rn1/CSs/DE*GEF*S1', '/poi/RNs/rn1/EVSEs/DE*GEF*E1*1' ]);

    });

    it('show the latest signed ones only, and say there are more', () => {

        const root = document.createElement('div');
        const many = Array.from({ length: priceStatementsShown + 5 }, (_, index) => statement('EVSE', `DE*GEF*E${index}*1`, index % 60));

        render(root, priceStatementsCard(priceStatementsOf({ priceStatements: many }), '/RNs/rn1'));

        assert.equal(root.querySelectorAll('tbody tr').length,  priceStatementsShown);
        assert.match(root.textContent ?? '',                    new RegExp(`The ${priceStatementsShown} signed last of ${priceStatementsShown + 5}`));

    });

    it('say there are none, and show no card of signed prices for a station without any', () => {

        const root = document.createElement('div');

        render(root, priceStatementsCard([], '/RNs/rn1'));
        assert.match(root.textContent ?? '', /None: no operator has stated prices here\./);

        render(root, signedPricesCard([], '/RNs/rn1'));
        assert.equal(root.textContent?.trim(), '');

    });

    it('show what they are given as text, never as markup', () => {

        const root = document.createElement('div');

        render(root, signedPricesCard(priceStatementsOf({ priceStatements: [ statement('EVSE', '<img src=x onerror=alert(1)>', 1) ] }), '/RNs/rn1'));

        assert.equal(root.querySelectorAll('img').length, 0);
        assert.match(root.textContent ?? '', /<img src=x onerror=alert\(1\)>/);

    });

});
