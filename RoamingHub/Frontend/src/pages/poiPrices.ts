/**
 * The price statements of a roaming network (SPECIFICATION.md of WWCP POI, section 12): the prices a charging station
 * operator sets for a charging pool, a charging station or an EVSE at runtime, each signed by one of its signing keys
 * when it sets them, one chain per entity. A driver's app gets those of a charging station with its proof and checks
 * them offline; months later a checkpoint proves which was the current one.
 *
 * - The roaming network lists the current statement of every entity, ~/RNs/{id}/history/priceStatements: the latest
 *   signed are shown first.
 * - A charging station or an EVSE has those of its pool, of itself and of its EVSEs, ~/…/priceStatements.
 * - Each tells its prices by the charging tariffs they replace the elements of, how long they hold - until the next one,
 *   unless it names an end - which link of its chain it is, and who signed it with which key.
 */

import { poiEntity, type Entity } from '../api/poi';
import { formatTimestamp } from '@node/ui';
import { html, nothing, type TemplateResult } from '@node/view';
import { entityURL } from './poi';


/** A price component of a price status: what is priced, the price, and the step it is counted in. */
export interface PriceComponent {
    type:       string;
    price:      number;
    stepSize?:  number;
}

/** The prices of one charging tariff: its elements, replaced. */
export interface TariffPrices {
    chargingTariffId:  string;
    elements:          { priceComponents: PriceComponent[]; restrictions?: unknown }[];
}

/** A price statement, as the POI API answers it. */
export interface PriceStatement {
    entityType:   'ChargingPool' | 'ChargingStation' | 'EVSE';
    entityId:     string;
    prices:       TariffPrices[];
    validFrom:    string;
    validUntil?:  string;
    sequence:     number;
    previous?:    string;
    signer:       string;
    role:         string;
    signedAt:     string;
    keyId:        string;
    algorithm:    string;
}

/** How many statements the card of the roaming network shows: the rest are in the list of the POI API. */
export const priceStatementsShown = 50;

/** The entity types of price statements, and the segment their pages are found by below the roaming network. */
const findBy: Record<PriceStatement['entityType'], { segment: string; label: string }> = {
    ChargingPool:     { segment: 'CPs',    label: 'Charging pool' },
    ChargingStation:  { segment: 'CSs',    label: 'Charging station' },
    EVSE:             { segment: 'EVSEs',  label: 'EVSE' }
};


/** The given price statement as one, or null when it is none. */
function priceStatementOf(Value: unknown): PriceStatement | null {

    if (typeof Value !== 'object' || Value === null)
        return null;

    const statement = Value as Record<string, unknown>;
    const prices    = (statement.priceStatus as { prices?: unknown } | undefined)?.prices;

    return typeof statement.entityType === 'string' && statement.entityType in findBy &&
           typeof statement.entityId   === 'string' &&
           typeof statement.validFrom  === 'string' &&
           typeof statement.sequence   === 'number' &&
           typeof statement.signer     === 'string' &&
           typeof statement.signedAt   === 'string' &&
           typeof statement.keyId      === 'string' &&
           Array.isArray(prices) &&
           prices.every(price => typeof price === 'object' && price !== null && typeof (price as TariffPrices).chargingTariffId === 'string' && Array.isArray((price as TariffPrices).elements))
               ? {
                     entityType:  statement.entityType as PriceStatement['entityType'],
                     entityId:    statement.entityId,
                     prices:      prices as TariffPrices[],
                     validFrom:   statement.validFrom,
                     ...(typeof statement.validUntil === 'string' ? { validUntil: statement.validUntil } : {}),
                     sequence:    statement.sequence,
                     ...(typeof statement.previous   === 'string' ? { previous:   statement.previous }   : {}),
                     signer:      statement.signer,
                     role:        typeof statement.role      === 'string' ? statement.role      : '',
                     signedAt:    statement.signedAt,
                     keyId:       statement.keyId,
                     algorithm:   typeof statement.algorithm === 'string' ? statement.algorithm : ''
                 }
               : null;

}


/**
 * The price statements of the given answer, { priceStatements: [ … ] }, the latest signed first; those that are no
 * price statements left out.
 */
export function priceStatementsOf(Answer: unknown): PriceStatement[] {

    const statements = (Answer as { priceStatements?: unknown } | null)?.priceStatements;

    return Array.isArray(statements)
               ? statements.map(priceStatementOf).
                            filter((statement): statement is PriceStatement => statement !== null).
                            sort((a, b) => Date.parse(b.signedAt) - Date.parse(a.signedAt))
               : [];

}


/**
 * The price statements at the given path below the POI API, e.g. /RNs/rn1/history/priceStatements; null where there
 * are none to ask - a roaming network without a history here - or they could not be asked: the page shows no card then.
 */
export async function loadPriceStatements(Path: string, Signal?: AbortSignal): Promise<PriceStatement[] | null> {

    try {
        return priceStatementsOf((await poiEntity(Path, {}, Signal)).entity as Entity);
    }
    catch {
        return null;
    }

}


/**
 * What a price component prices, per which unit, and the unit of its steps, as in OCPI: energy per kWh in steps of Wh,
 * the time charging and the time parked per hour in steps of seconds, the current per A in steps of A, a flat fee.
 */
const dimensions: Record<string, { label: string; per?: string; step?: string }> = {
    ENERGY:        { label: 'energy',         per: 'kWh',  step: 'Wh' },
    TIME:          { label: 'charging time',  per: 'h',    step: 's' },
    PARKING_TIME:  { label: 'parking time',   per: 'h',    step: 's' },
    MAX_CURRENT:   { label: 'max current',    per: 'A',    step: 'A' },
    MIN_CURRENT:   { label: 'min current',    per: 'A',    step: 'A' },
    FLAT:          { label: 'flat' }
};


/**
 * What the given price components price, in one line, in the currency of their tariff:
 * "energy 0.39/kWh in steps of 1 Wh, flat 2.5".
 */
export function componentsText(Components: PriceComponent[]): string {

    return Components.map(component => {

                          const dimension  = dimensions[component.type] ?? { label: component.type.toLowerCase() };
                          const price      = `${dimension.label} ${component.price}${dimension.per ? `/${dimension.per}` : ''}`;

                          return component.stepSize !== undefined && dimension.step
                                     ? `${price} in steps of ${component.stepSize} ${dimension.step}`
                                     : price;

                      }).
                      join(', ');

}


/** The prices of a statement, one line per charging tariff and element. */
function pricesView(Statement: PriceStatement): TemplateResult {

    return html`${Statement.prices.map(tariff => tariff.elements.map(element => html`
        <div><code>${tariff.chargingTariffId}</code> ${componentsText(element.priceComponents)}${element.restrictions !== undefined ? html` <span class="muted small">(restricted)</span>` : nothing}</div>`))}`;

}


/** How long a statement holds: from when, and until when or until the next one. */
function validityView(Statement: PriceStatement): TemplateResult {

    return html`${formatTimestamp(Statement.validFrom)}<br><span class="muted small">${Statement.validUntil ? `until ${formatTimestamp(Statement.validUntil)}` : 'until the next one'}</span>`;

}


/** The rows of the given statements. */
function rowsView(Statements: PriceStatement[], Network: string): TemplateResult {

    return html`
        <div class="table-scroll">
            <table class="table poi-table poi-prices">
                <thead><tr><th>Of</th><th>Prices</th><th>Valid</th><th>Chain</th><th>Signed</th></tr></thead>
                <tbody>
                    ${Statements.map(statement => html`
                        <tr>
                            <td><span class="muted small">${findBy[statement.entityType].label}</span><br>
                                <a href=${entityURL(`${Network}/${findBy[statement.entityType].segment}/${encodeURIComponent(statement.entityId)}`)}><code>${statement.entityId}</code></a></td>
                            <td>${pricesView(statement)}</td>
                            <td>${validityView(statement)}</td>
                            <td>#${statement.sequence}${statement.previous ? html`<br><span class="muted small" title=${'after ' + statement.previous}>after <code>${statement.previous.slice(0, 12)}…</code></span>` : nothing}</td>
                            <td>${statement.signer}<br><span class="muted small">${formatTimestamp(statement.signedAt)}</span><br>
                                <span class="muted small" title=${statement.algorithm + ' key ' + statement.keyId}>key <code>${statement.keyId.slice(0, 12)}…</code></span></td>
                        </tr>`)}
                </tbody>
            </table>
        </div>`;

}


/**
 * The card of the price statements of a roaming network: the current one of every entity, the latest signed first.
 *
 * @param Statements  The statements.
 * @param Network     The path of the roaming network below the POI API.
 */
export function priceStatementsCard(Statements:  PriceStatement[],
                                    Network:     string): TemplateResult {

    const rows = Statements.slice(0, priceStatementsShown);

    return html`
        <section class="card poi-price-statements">
            <h2><i class="fa-solid fa-file-signature"></i> Price statements <span class="chip">${Statements.length}</span></h2>
            <p class="hint">
                The prices the charging station operators set at runtime, each signed by the operator when it sets them: a
                driver's app checks them offline, with the proof of the charging station. Each replaces the elements of the
                charging tariffs it names, and holds until the next one of its entity unless it names an end.
            </p>
            ${rows.length === 0
                  ? html`<p class="muted">None: no operator has stated prices here.</p>`
                  : rowsView(rows, Network)}
            ${Statements.length > rows.length
                  ? html`<p class="muted small">The ${rows.length} signed last of ${Statements.length}; all of them are in the list of the POI API.</p>`
                  : nothing}
        </section>`;

}


/**
 * The card of the signed prices of a charging station or an EVSE: the statements of its pool, of itself and of its
 * EVSEs, none shown where there are none.
 *
 * @param Statements  The statements.
 * @param Network     The path of the roaming network below the POI API.
 */
export function signedPricesCard(Statements:  PriceStatement[],
                                 Network:     string): TemplateResult | typeof nothing {

    if (Statements.length === 0)
        return nothing;

    return html`
        <section class="card poi-price-statements">
            <h2><i class="fa-solid fa-file-signature"></i> Signed prices <span class="chip">${Statements.length}</span></h2>
            <p class="hint">
                What its operator states at runtime, for its charging pool, for it and for its EVSEs, each signed when it was
                set: they replace the elements of the charging tariffs they name.
            </p>
            ${rowsView(Statements, Network)}
        </section>`;

}
