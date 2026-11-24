// Interactive tools of the guide: pages that are not rendered from wiki/. The sitemap, llms.txt, the search index
// (scripts/sync-content.mjs) and the navigation all read this list, so a tool is found wherever a page would be.
export const tools = [
  {
    id: 'config-builder',
    url: '/tools/config-builder/',
    title: 'Configuration builder',
    description: 'Build appsettings, environment variables and the secrets list for a StarterKit product: pick the modules and the environment, change what differs, check it against the production rules, and export.',
    // Words a reader may search for; the search index uses them as the page body.
    keywords: 'appsettings appsettings.json configuration config settings environment variables env .env docker compose secrets production checks options generator builder export import share modules',
  },
];

export const configBuilder = tools[0];
