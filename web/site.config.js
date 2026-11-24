// Where the site is served: https://autorejoin.is-a.bot/ (free-domains/is-a.bot#888, CNAME to GitHub Pages).
// postbuild writes docs/CNAME for a custom domain. SITE_URL in the environment overrides it for one build.
export const SITE_URL = process.env.SITE_URL || 'https://autorejoin.is-a.bot/';
