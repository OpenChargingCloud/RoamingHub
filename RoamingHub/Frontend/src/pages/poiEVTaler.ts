/**
 * EV Taler (concepts/EVTaler.md of WWCP POI): a charging station operator accepting GNU Taler names its merchant at a
 * GNU Taler merchant backend and the exchanges whose coins it accepts, its evTaler, and its charging pools name the
 * payment option EVTaler.
 *
 * - A charging station operator shows its merchant and its exchanges, as its evTaler gives them.
 * - A charging pool and an EVSE show how charging there is paid with EV Taler, as the POI API tells it, ~/…/evTaler:
 *   the EV Taler tariff of each EVSE and the exchanges in its currency, each as drivers see it, e.g.
 *   "EV Taler (Example Exchange, EUR)"; nothing where there is none.
 *
 * Nothing here pays: offers, payments and refunds are GNU Taler's. Changing the merchant is the operator's, with its
 * key for payments: the hub shows it, it does not sign it.
 */

import { poiEntity, textOf, type Entity, type I18N } from '../api/poi';
import { html, nothing, type TemplateResult } from '@node/view';
import { entityURL } from './poi';


/** An exchange of GNU Taler whose coins a charging station operator accepts. */
export interface EVTalerExchange {
    masterPublicKey:     string;
    baseURL:             string;
    currency:            string;
    name:                I18N;
    /** How drivers see it at an EVSE, e.g. "EV Taler (Example Exchange, EUR)": what the POI API tells of an EVSE or a pool. */
    paymentOptionName?:  I18N;
}

/** The merchant of a charging station operator at a GNU Taler merchant backend, and the exchanges it accepts. */
export interface EVTalerMerchant {
    merchantBaseURL:    string;
    merchantPublicKey:  string;
    exchanges:          EVTalerExchange[];
}

/** The EV Taler tariff of an EVSE, and its currency. */
export interface EVTalerTariff {
    EVSEId:            string;
    chargingTariffId:  string;
    currency:          string;
}

/** How charging at an EVSE or a charging pool is paid with EV Taler: the tariffs of its EVSEs, the merchant, the exchanges. */
export interface EVTalerAt extends EVTalerMerchant {
    tariffs:  EVTalerTariff[];
}


/** The given value as a multi-language text, or null when it is none. */
function i18nOf(Value: unknown): I18N | null {

    return typeof Value === 'object' && Value !== null && !Array.isArray(Value) &&
           Object.values(Value).length > 0 && Object.values(Value).every(text => typeof text === 'string')
               ? Value as I18N
               : null;

}

/** The given value as an exchange, or null when it is none. */
function exchangeOf(Value: unknown): EVTalerExchange | null {

    if (typeof Value !== 'object' || Value === null)
        return null;

    const exchange  = Value as Record<string, unknown>;
    const name      = i18nOf(exchange.name);
    const shown     = i18nOf(exchange.paymentOptionName);

    return typeof exchange.masterPublicKey === 'string' &&
           typeof exchange.baseURL         === 'string' &&
           typeof exchange.currency        === 'string' &&
           name !== null
               ? {
                     masterPublicKey:  exchange.masterPublicKey,
                     baseURL:          exchange.baseURL,
                     currency:         exchange.currency,
                     name,
                     ...(shown !== null ? { paymentOptionName: shown } : {})
                 }
               : null;

}

/** The given value as a tariff of an EVSE, or null when it is none. */
function tariffOf(Value: unknown): EVTalerTariff | null {

    if (typeof Value !== 'object' || Value === null)
        return null;

    const tariff = Value as Record<string, unknown>;

    return typeof tariff.EVSEId           === 'string' &&
           typeof tariff.chargingTariffId === 'string' &&
           typeof tariff.currency         === 'string'
               ? { EVSEId: tariff.EVSEId, chargingTariffId: tariff.chargingTariffId, currency: tariff.currency }
               : null;

}


/** The merchant of an operator, its evTaler as the POI API writes it; null when it is none. */
export function evTalerMerchantOf(Value: unknown): EVTalerMerchant | null {

    if (typeof Value !== 'object' || Value === null || Array.isArray(Value))
        return null;

    const merchant   = Value as Record<string, unknown>;
    const exchanges  = Array.isArray(merchant.exchanges) ? merchant.exchanges.map(exchangeOf) : [];

    return typeof merchant.merchantBaseURL   === 'string' &&
           typeof merchant.merchantPublicKey === 'string' &&
           exchanges.length > 0 && exchanges.every(exchange => exchange !== null)
               ? {
                     merchantBaseURL:    merchant.merchantBaseURL,
                     merchantPublicKey:  merchant.merchantPublicKey,
                     exchanges:          exchanges as EVTalerExchange[]
                 }
               : null;

}

/**
 * How charging is paid with EV Taler, as the POI API answers it: of an EVSE { EVSEId, chargingTariffId, currency,
 * merchantBaseURL, merchantPublicKey, exchanges }, of a charging pool { chargingPoolId, EVSEs: [ … ], merchantBaseURL,
 * merchantPublicKey, exchanges }; null when it is neither.
 */
export function evTalerAtOf(Answer: unknown): EVTalerAt | null {

    const merchant = evTalerMerchantOf(Answer);

    if (merchant === null)
        return null;

    const answer   = Answer as Record<string, unknown>;
    const tariffs  = Array.isArray(answer.EVSEs)
                         ? answer.EVSEs.map(tariffOf)
                         : [ tariffOf(answer) ];

    return tariffs.length > 0 && tariffs.every(tariff => tariff !== null)
               ? { ...merchant, tariffs: tariffs as EVTalerTariff[] }
               : null;

}

/**
 * How charging at the entity at the given path below the POI API is paid with EV Taler, ~/…/evTaler; null where it is
 * not - the POI API answers 404 then - or it could not be asked: the page shows no card then.
 */
export async function loadEVTaler(Path: string, Signal?: AbortSignal): Promise<EVTalerAt | null> {

    try {
        return evTalerAtOf((await poiEntity(Path, {}, Signal)).entity as Entity);
    }
    catch {
        return null;
    }

}


/** The base URL and the key of a merchant; outside a table, the key breaks where it must, on a phone. */
function merchantView(Merchant: EVTalerMerchant): TemplateResult {

    return html`
        <div class="kv-list">
            <div class="kv"><span class="k">Merchant</span><span class="poi-value path">${Merchant.merchantBaseURL}</span></div>
            <div class="kv"><span class="k">Merchant public key</span><span class="poi-value"><code>${Merchant.merchantPublicKey}</code></span></div>
        </div>`;

}


/**
 * The card of the merchant of a charging station operator: its instance at a GNU Taler merchant backend and the
 * exchanges it accepts, each by its name, currency, base URL and master public key.
 *
 * @param Merchant  The evTaler of the operator.
 */
export function evTalerMerchantCard(Merchant: EVTalerMerchant): TemplateResult {

    return html`
        <section class="card poi-ev-taler">
            <h2><i class="fa-solid fa-coins"></i> EV Taler <span class="chip">${Merchant.exchanges.length}</span></h2>
            <p class="hint">
                Its merchant at a GNU Taler merchant backend, and the exchanges whose coins it accepts: its charging pools
                naming EVTaler are paid step by step, at each EVSE with the exchanges in the currency of its tariff. Every
                offer for one of its stations must come from this merchant; it is changed only with the operator's key for
                payments.
            </p>
            ${merchantView(Merchant)}
            <div class="table-scroll">
                <table class="table poi-table">
                    <thead><tr><th>Exchange</th><th>Currency</th><th>Base URL</th><th>Master public key</th></tr></thead>
                    <tbody>
                        ${Merchant.exchanges.map(exchange => html`
                            <tr>
                                <td>${textOf(exchange.name)}</td>
                                <td>${exchange.currency}</td>
                                <td class="path">${exchange.baseURL}</td>
                                <td><code class="token">${exchange.masterPublicKey}</code></td>
                            </tr>`)}
                    </tbody>
                </table>
            </div>
        </section>`;

}

/**
 * The card of how charging at a charging pool or an EVSE is paid with EV Taler: the payment options as drivers see
 * them, the EV Taler tariff of each EVSE, leading to its page, and the merchant.
 *
 * @param At        What the POI API tells, ~/…/evTaler.
 * @param Operator  The path of the charging station operator below the POI API, which its tariffs are below.
 */
export function evTalerCard(At:        EVTalerAt,
                            Operator:  string): TemplateResult {

    return html`
        <section class="card poi-ev-taler">
            <h2><i class="fa-solid fa-coins"></i> EV Taler</h2>
            <p class="hint">
                Paid with GNU Taler, step by step, as its operator's tariff prices each step: with coins of these exchanges.
                Offers, payments and refunds are GNU Taler's.
            </p>
            <ul class="poi-ev-taler-options">
                ${At.exchanges.map(exchange => html`
                    <li><strong>${textOf(exchange.paymentOptionName) || textOf(exchange.name)}</strong>
                        <span class="muted small">exchange <code>${exchange.masterPublicKey}</code></span></li>`)}
            </ul>
            <div class="table-scroll">
                <table class="table poi-table">
                    <thead><tr><th>EVSE</th><th>EV Taler tariff</th><th>Currency</th></tr></thead>
                    <tbody>
                        ${At.tariffs.map(tariff => html`
                            <tr>
                                <td><code>${tariff.EVSEId}</code></td>
                                <td><a href=${entityURL(`${Operator}/CTs/${encodeURIComponent(tariff.chargingTariffId)}`)}><code>${tariff.chargingTariffId}</code></a></td>
                                <td>${tariff.currency}</td>
                            </tr>`)}
                    </tbody>
                </table>
            </div>
            ${merchantView(At)}
        </section>`;

}


/** Nothing, or the card of the merchant of the given entity, when it is an operator naming one. */
export function evTalerMerchantCardOf(Entity: Entity | undefined): TemplateResult | typeof nothing {

    const merchant = evTalerMerchantOf((Entity as Record<string, unknown> | undefined)?.evTaler);

    return merchant !== null
               ? evTalerMerchantCard(merchant)
               : nothing;

}
