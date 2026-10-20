/**
 * The trust anchor of a roaming network: what a driver's app pins of it, the identity keys of its roaming network
 * operator by their thumbprints, as one URI for a QR code (SPECIFICATION.md of WWCP POI, section 8.6). The app scans
 * it, gets the proof of a charging station below the roaming network it names, and checks it offline: the key statement
 * the proof gives must be signed by one of these keys, and the attestation by a signing key it states.
 */

import qrcode from 'qrcode-generator';

import { poiEntity } from '../api/poi';
import { html, nothing, unsafeHTML, type TemplateResult } from '@node/view';


/** What GET ~/poi/RNs/{id}/trustAnchor answers. */
export interface TrustAnchor {
    roamingNetworkId:  string;
    identityKeyIds:    string[];
    uri:               string;
}


/** The given answer as a trust anchor, or null when it is none. */
export function trustAnchorOf(Answer: unknown): TrustAnchor | null {

    if (typeof Answer !== 'object' || Answer === null)
        return null;

    const { roamingNetworkId, identityKeyIds, uri } = Answer as Record<string, unknown>;

    return typeof roamingNetworkId === 'string' &&
           typeof uri === 'string' && /^https?:\/\//.test(uri) &&
           Array.isArray(identityKeyIds) && identityKeyIds.length > 0 &&
           identityKeyIds.every(id => typeof id === 'string' && /^[0-9a-f]{64}$/.test(id))
               ? { roamingNetworkId, identityKeyIds: identityKeyIds as string[], uri }
               : null;

}


/**
 * The trust anchor of the roaming network at the given path below the POI API, or null where it has none - no
 * operator with identity keys - or it could not be asked: the page shows no card then.
 */
export async function loadTrustAnchor(Path: string, Signal?: AbortSignal): Promise<TrustAnchor | null> {

    try {
        return trustAnchorOf((await poiEntity(`${Path}/trustAnchor`, {}, Signal)).entity);
    }
    catch {
        return null;
    }

}


/**
 * The QR code of the given text as the SVG the library writes, scaling with its box: only its modules go in, never
 * the text itself.
 */
export function qrSVG(Text: string): string {

    const qr = qrcode(0, 'M');

    qr.addData(Text, 'Byte');
    qr.make();

    return qr.createSvgTag({ cellSize: 4, margin: 4, scalable: true });

}


/**
 * The card of the trust anchor: its QR code, its URI to copy, and the identity keys it names.
 *
 * @param Anchor   The trust anchor.
 * @param Copied   What the last copy said, if anything.
 * @param Copy     Copies the URI.
 */
export function trustAnchorCard(Anchor:  TrustAnchor,
                                Copied:  string | null,
                                Copy:    () => void): TemplateResult {

    return html`
        <section class="card poi-trust-anchor">
            <h2><i class="fa-solid fa-qrcode"></i> Trust anchor</h2>
            <p class="hint">
                A driver's app scans this QR code to check the proofs of the charging stations of this roaming network
                offline: it trusts the identity keys of its roaming network operator by their thumbprints, and nothing else.
                Show it where it can not be swapped for another one - on the website or in the documents of the operator,
                not stuck on a charging station.
            </p>
            <div class="poi-qr" role="img" aria-label="QR code of the trust anchor">${unsafeHTML(qrSVG(Anchor.uri))}</div>
            <div class="kv-list">
                <div class="kv">
                    <span class="k">URI</span>
                    <span class="poi-value"><code class="path">${Anchor.uri}</code>
                        <button type="button" class="btn small" @click=${Copy}><i class="fa-solid fa-copy"></i> Copy</button>
                        ${Copied ? html`<span class="muted small">${Copied}</span>` : nothing}</span>
                </div>
                <div class="kv">
                    <span class="k">Identity keys</span>
                    <span class="poi-value">${Anchor.identityKeyIds.map(id => html`<div><code class="path">${id}</code></div>`)}</span>
                </div>
            </div>
        </section>`;

}
