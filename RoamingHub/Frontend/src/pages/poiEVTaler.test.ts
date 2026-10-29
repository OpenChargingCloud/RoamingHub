/**
 * EV Taler: the merchant of a charging station operator is read only as one, and shown with its exchanges; how charging
 * at a charging pool or an EVSE is paid is read from what the POI API answers, and shown with the payment options as
 * drivers see them and the tariff of each EVSE, leading to its page.
 */

import '../../test/hub.ts';

import { strict as assert }  from 'node:assert';
import { describe, it }      from 'node:test';

const { evTalerAtOf, evTalerCard, evTalerMerchantCardOf, evTalerMerchantOf } = await import('./poiEVTaler.ts');
const { render } = await import('@node/view');


const merchantKey     = 'ESMBKNR5CMZPAYH9AD0QNKCPYSJSE0EN5MH4FMPKN683R63HMD3G';
const eurExchangeKey  = 'F80MFRG8HVH6R9CQ47KRFQSJP3T6DBJ4K1D9B703RJY3Z39TBMJ0';
const chfExchangeKey  = 'YJ7H5JVNVM5SG5FAE3BGQ35AEPHXBH27MWNRPFQ0SVW9BH0BN9B0';

/** The evTaler of DE*GEF, as the POI API writes it in the operator. */
function merchant(Extra: Record<string, unknown> = {}): Record<string, unknown> {
    return {
        merchantBaseURL:    'https://taler.gef.example/instances/de-gef/',
        merchantPublicKey:  merchantKey,
        exchanges: [
            { masterPublicKey: eurExchangeKey, baseURL: 'https://exchange.example.eu/', currency: 'EUR', name: { en: 'Example Exchange' } },
            { masterPublicKey: chfExchangeKey, baseURL: 'https://exchange.example.ch/', currency: 'CHF', name: { en: 'Example Franc Exchange' } }
        ],
        ...Extra
    };
}

/** What the POI API answers for an EVSE, ~/EVSEs/DE*GEF*E1*1/evTaler. */
function ofEVSE(Extra: Record<string, unknown> = {}): Record<string, unknown> {
    return {
        EVSEId:             'DE*GEF*E1*1',
        chargingTariffId:   'DE*GEF*T_DC',
        currency:           'EUR',
        merchantBaseURL:    'https://taler.gef.example/instances/de-gef/',
        merchantPublicKey:  merchantKey,
        exchanges: [
            { masterPublicKey: eurExchangeKey, baseURL: 'https://exchange.example.eu/', currency: 'EUR', name: { en: 'Example Exchange' },
              paymentOptionName: { en: 'EV Taler (Example Exchange, EUR)' } }
        ],
        ...Extra
    };
}


describe('EV Taler', () => {

    it('reads the merchant of an operator only where it is one', () => {

        const read = evTalerMerchantOf(merchant());

        assert.equal(read?.merchantPublicKey,                                   merchantKey);
        assert.deepEqual(read?.exchanges.map(exchange => exchange.currency),    [ 'EUR', 'CHF' ]);

        assert.equal(evTalerMerchantOf(null),                                   null);
        assert.equal(evTalerMerchantOf(merchant({ exchanges: [] })),            null, 'no exchanges');
        assert.equal(evTalerMerchantOf(merchant({ merchantPublicKey: 42 })),    null);
        assert.equal(evTalerMerchantOf(merchant({ exchanges: [ { masterPublicKey: eurExchangeKey, baseURL: 'https://exchange.example.eu/', currency: 'EUR' } ] })), null, 'an exchange without a name');

    });

    it('reads what the POI API tells of an EVSE and of a charging pool', () => {

        const atEVSE  = evTalerAtOf(ofEVSE());
        const atPool  = evTalerAtOf({ chargingPoolId: 'DE*GEF*P1',
                                      merchantBaseURL: 'https://taler.gef.example/instances/de-gef/', merchantPublicKey: merchantKey,
                                      EVSEs: [ { EVSEId: 'DE*GEF*E1*1', chargingTariffId: 'DE*GEF*T_DC', currency: 'EUR' },
                                               { EVSEId: 'DE*GEF*E1*2', chargingTariffId: 'DE*GEF*T_AC', currency: 'EUR' } ],
                                      exchanges: ofEVSE().exchanges });

        assert.deepEqual(atEVSE?.tariffs,                                        [ { EVSEId: 'DE*GEF*E1*1', chargingTariffId: 'DE*GEF*T_DC', currency: 'EUR' } ]);
        assert.equal(atEVSE?.exchanges[0]?.paymentOptionName?.en,               'EV Taler (Example Exchange, EUR)');
        assert.deepEqual(atPool?.tariffs.map(tariff => tariff.chargingTariffId), [ 'DE*GEF*T_DC', 'DE*GEF*T_AC' ]);

        assert.equal(evTalerAtOf({ description: 'The EVSE \'DE*GEF*E2*1\' offers no EV Taler!' }), null);
        assert.equal(evTalerAtOf(ofEVSE({ chargingTariffId: undefined })),      null, 'an EVSE without its tariff');

    });

    it('shows the merchant of an operator with its exchanges, and nothing for an operator without one', () => {

        const root = document.createElement('div');

        render(root, evTalerMerchantCardOf({ '@id': 'DE*GEF', evTaler: merchant() }));

        const text = root.textContent ?? '';

        assert.match(text,  /EV Taler\s*2/);
        assert.match(text,  /https:\/\/taler\.gef\.example\/instances\/de-gef\//);
        assert.match(text,  new RegExp(merchantKey));
        assert.match(text,  /Example Exchange\s*EUR\s*https:\/\/exchange\.example\.eu\//);
        assert.match(text,  /Example Franc Exchange\s*CHF/);

        render(root, evTalerMerchantCardOf({ '@id': 'DE*GEF' }));
        assert.equal(root.textContent?.trim(), '');

    });

    it('shows how charging at an EVSE is paid, its tariff leading to its page below the operator', () => {

        const root = document.createElement('div');

        render(root, evTalerCard(evTalerAtOf(ofEVSE())!, '/RNs/rn1/CSOs/DE*GEF'));

        const text  = root.textContent ?? '';
        const links = [ ...root.querySelectorAll('a') ].map(link => link.getAttribute('href'));

        assert.match(text,       /EV Taler \(Example Exchange, EUR\)/);
        assert.match(text,       /DE\*GEF\*E1\*1\s*DE\*GEF\*T_DC\s*EUR/);
        assert.doesNotMatch(text, /Franc/, 'only the exchanges the POI API tells for it');
        assert.deepEqual(links,  [ '/poi/RNs/rn1/CSOs/DE*GEF/CTs/DE*GEF*T_DC' ]);

    });

    it('shows what it is given as text, never as markup', () => {

        const root = document.createElement('div');

        render(root, evTalerMerchantCardOf({ '@id': 'DE*GEF', evTaler: merchant({ exchanges: [ { masterPublicKey: eurExchangeKey, baseURL: 'https://exchange.example.eu/', currency: 'EUR', name: { en: '<img src=x onerror=alert(1)>' } } ] }) }));

        assert.equal(root.querySelectorAll('img').length, 0);
        assert.match(root.textContent ?? '', /<img src=x onerror=alert\(1\)>/);

    });

});
