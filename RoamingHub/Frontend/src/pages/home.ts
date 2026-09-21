import { auth } from '../auth';
import type { Page } from '../router';

/**
 * Where "/" leads.
 *
 * To the traffic for whoever may read it, and that is most of the time: a hub
 * is configured a few times in its life and watched all day. To the
 * configuration for a viewer, who may see what this hub is but not what went
 * between its peers.
 *
 * A page that renders nothing and navigates on, with the address replaced, so
 * that the back button does not come here again.
 */
export const homePage: Page = {

    title: 'RoamingHub',

    render({ navigate }) {
        navigate(auth.can('readTraffic') ? '/traffic' : '/configuration', true);
    }

};
