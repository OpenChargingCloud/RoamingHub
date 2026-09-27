import type { Operation, Permission, Resource } from './api/client';
import { auth } from './auth';
import { config } from './config';
import { html, must, render, type HTMLFragment } from './html';
import { toURL } from './basePath';

/**
 * The frame every signed-in page sits in: the menu on the left, a heading and
 * whatever the page puts under it on the right.
 *
 * The menu is here and not in main.ts because each page renders itself into an
 * emptied outlet - so the frame is drawn again with every navigation, and the
 * entry that is current is simply the one that says so. One place decides what
 * the hub has pages for.
 */

export interface MenuEntry {
    path:         string;
    label:        string;
    /** A Font Awesome class, e.g. "fa-sliders". */
    icon:         string;
    /**
     * What it takes to see this entry - any one of these, written the way
     * the hub writes them: "traffic:read". An entry without it is for
     * everybody who is signed in. A courtesy like every greyed-out button:
     * the hub checks every request again when it arrives.
     */
    permission?:  Permission[];
    /** The pages below this one, shown indented while one of them is open. */
    children?:    MenuEntry[];
}

/**
 * What the hub can show.
 *
 * The traffic first, and not the configuration: a hub is configured a few
 * times in its life and read all day, and what somebody walks up to it with
 * is "are these two seeing each other?". It carries its own permission for
 * the same reason it is its own page - what went between the peers is their
 * business passing through here, not this hub's own.
 *
 * The pages below the configuration carry one each as well, for the resource
 * they show - the name servers, the time servers, the certificate store, the
 * peers. A role from the configuration file may read the configuration and
 * not the store, or the name servers and not the peers, and an entry it may
 * not read is a page that answers 403. The three roles a hub brings read all
 * of them, so for them nothing is hidden.
 */
export const menu: MenuEntry[] = [
    {
        path:        '/traffic',
        label:       'Traffic',
        icon:        'fa-right-left',
        permission:  ['traffic:read']
    },
    {
        path:        '/configuration',
        label:       'Configuration',
        icon:        'fa-sliders',
        permission:  ['configuration:read'],
        children:  [
            { path: '/configuration/dns',            label: 'DNS client',    icon: 'fa-magnifying-glass-location',  permission: ['dns:read']          },
            { path: '/configuration/nts',            label: 'NTS client',    icon: 'fa-clock',                      permission: ['nts:read']          },
            { path: '/configuration/certificates',   label: 'Certificates',  icon: 'fa-certificate',                permission: ['certificates:read'] },
            { path: '/configuration/ocpi',           label: 'OCPI',          icon: 'fa-plug'                                                          },
            { path: '/configuration/ocpi/partners',  label: 'Peers',         icon: 'fa-handshake',                  permission: ['peers:read']        }
        ]
    },
    { path: '/logs', label: 'Logs', icon: 'fa-list-ul', permission: ['configuration:read'] }
];

/** Every entry of the menu, parents and children alike. */
export function allMenuEntries(): MenuEntry[] {
    return menu.flatMap(entry => [entry, ...(entry.children ?? [])]);
}

/**
 * Whether the person signed in may see an entry - a child as much as the
 * entry it is under.
 *
 * Asked the way every page asks, an operation on a resource: a permission is
 * written "resource:operation", and neither half has a colon of its own.
 */
function visible(entry: MenuEntry): boolean {
    return entry.permission === undefined ||
           entry.permission.some(permission => {
               const [ resource, operation ] = permission.split(':') as [ Resource, Operation ];
               return auth.can(resource, operation);
           });
}


export interface ShellOptions {
    /** The menu entry to mark as the current one. */
    active:     string;
    /** The heading of the page. */
    title:      string;
    /** One line under the heading, or nothing. */
    subtitle?:  string;
    /** Buttons and such, shown at the right of the heading. */
    actions?:   HTMLFragment;
}


/**
 * Draw the frame into the given root and hand back the element the page is to
 * render itself into.
 */
export function shell(root:     HTMLElement,
                      options:  ShellOptions): HTMLElement {

    render(root, html`
        <div class="shell">

            <nav class="sidebar" aria-label="Sections">

                <div class="brand">
                    <i class="fa-solid fa-circle-nodes"></i>
                    <span>RoamingHub</span>
                </div>

                <ul class="menu">
                    ${menu.filter(visible).map(entry => html`
                        <li>
                            ${link(entry, options.active)}
                            ${entry.children && isOpen(entry, options.active)
                                  ? html`
                                      <ul class="submenu">
                                          ${entry.children.filter(visible).map(child => html`<li>${link(child, options.active)}</li>`)}
                                      </ul>
                                  `
                                  : ''}
                        </li>
                    `)}
                </ul>

                <div class="sidebar-foot">
                    <div class="who" title="Signed in as ${auth.user?.roles?.join(', ') ?? 'nobody'}">
                        <i class="fa-solid fa-user"></i>
                        <span>${auth.user?.username ?? '-'}</span>
                    </div>
                    ${auth.user?.roles?.length
                          ? html`<div class="roles small muted">${auth.user.roles.join(', ')}</div>`
                          : ''}
                    <button type="button" id="sign-out" class="btn small">Sign out</button>
                    <div class="versions small muted">
                        Hub ${config.serverVersion} &middot; web ${config.frontendVersion}
                    </div>
                </div>

            </nav>

            <main class="content">

                <header class="page-head">
                    <div>
                        <h1>${options.title}</h1>
                        ${options.subtitle ? html`<p class="muted">${options.subtitle}</p>` : ''}
                    </div>
                    <div class="page-actions">${options.actions ?? ''}</div>
                </header>

                <div id="content-body" class="content-body"></div>

            </main>

        </div>
    `);

    must<HTMLButtonElement>(root, '#sign-out').
        addEventListener('click', () => void auth.signOut());

    return must<HTMLElement>(root, '#content-body');

}


/** One entry of the menu, marked when it is the page being shown. */
function link(entry: MenuEntry, active: string): HTMLFragment {

    const current = entry.path === active;

    return html`
        <a href="${toURL(entry.path)}"
           class="${current ? 'active' : ''}"
           ${current ? html`aria-current="page"` : ''}>
            <i class="fa-solid ${entry.icon}"></i>
            <span>${entry.label}</span>
        </a>
    `;

}

/**
 * Whether an entry's children are shown: while the entry itself is open, or
 * while one of them is. The menu unfolds where somebody is and stays folded
 * everywhere else, so a hub with a dozen pages still fits on the left.
 */
function isOpen(entry: MenuEntry, active: string): boolean {
    return entry.path === active ||
           (entry.children ?? []).some(child => child.path === active);
}
