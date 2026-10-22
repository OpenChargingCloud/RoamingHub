/**
 * The trust anchor of a roaming network: what a driver's app pins of it, the identity keys of its roaming network
 * operator by their thumbprints and how many of their holders must sign, its minConsensus (SPECIFICATION.md of WWCP
 * POI, sections 4.1 and 8.6). Its QR code holds WWCP/TA-2: and the Base45 of its CBOR, with the name and description of
 * the network; its URI says the same without them. The app scans it, gets the proof of a
 * charging station below the roaming network it names, and checks it offline: the key statement the proof gives must
 * be signed by these keys of minConsensus holders, and the attestations by signing keys it states of as many.
 */

import qrcode from 'qrcode-generator';

import { poiEntity } from '../api/poi';
import { html, nothing, unsafeHTML, type TemplateResult } from '@node/view';


/** What GET ~/poi/RNs/{id}/trustAnchor answers. */
export interface TrustAnchor {
    roamingNetworkId:  string;
    identityKeyIds:    string[];
    /** How many holders of the identity keys must sign: 1 unless several share them as a quorum. */
    minConsensus:      number;
    uri:               string;
    /** The text of its QR code, WWCP/TA-2: and the Base45 of its CBOR; null from a hub not writing it. */
    qrCode:            string | null;
}


/** What the text of the QR code of a trust anchor is: its prefix and Base45, of the alphanumeric mode of QR codes. */
const qrCodeText = /^WWCP\/TA-2:[0-9A-Z $%*+\-./:]+$/;


/** The given answer as a trust anchor, or null when it is none. */
export function trustAnchorOf(Answer: unknown): TrustAnchor | null {

    if (typeof Answer !== 'object' || Answer === null)
        return null;

    const { roamingNetworkId, identityKeyIds, minConsensus = 1, uri, qrCode = null } = Answer as Record<string, unknown>;

    return typeof roamingNetworkId === 'string' &&
           typeof uri === 'string' && /^https?:\/\//.test(uri) &&
           Array.isArray(identityKeyIds) && identityKeyIds.length > 0 &&
           identityKeyIds.every(id => typeof id === 'string' && /^[0-9a-f]{64}$/.test(id)) &&
           typeof minConsensus === 'number' && Number.isInteger(minConsensus) && minConsensus >= 1 && minConsensus <= identityKeyIds.length &&
           (qrCode === null || (typeof qrCode === 'string' && qrCodeText.test(qrCode)))
               ? { roamingNetworkId, identityKeyIds: identityKeyIds as string[], minConsensus, uri, qrCode: qrCode as string | null }
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
 * the text itself. The alphanumeric mode takes Base45 in fewer modules than bytes.
 */
export function qrSVG(Text: string, Mode: 'Byte' | 'Alphanumeric' = 'Byte'): string {

    const qr = qrcode(0, 'M');

    qr.addData(Text, Mode);
    qr.make();

    return qr.createSvgTag({ cellSize: 4, margin: 4, scalable: true });

}


/**
 * The card of the trust anchor: its QR code, of its CBOR where the hub writes it, else of its URI; its URI to copy;
 * the identity keys it names, and how many of their holders must sign.
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
            <div class="poi-qr" role="img" aria-label="QR code of the trust anchor">${unsafeHTML(Anchor.qrCode ? qrSVG(Anchor.qrCode, 'Alphanumeric') : qrSVG(Anchor.uri))}</div>
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
                <div class="kv">
                    <span class="k">Quorum</span>
                    <span class="poi-value">${Anchor.minConsensus > 1
                                                  ? `${Anchor.minConsensus} holders of these keys sign together: none of them alone`
                                                  : 'one holder signs'}</span>
                </div>
            </div>
        </section>`;

}
