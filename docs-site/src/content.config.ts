import { defineCollection } from 'astro:content';
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';
import { siteBase } from '../sidebar.mjs';

export const collections = {
  docs: defineCollection({
    loader: docsLoader(),
    schema: (context) => docsSchema()(context).transform((data) => {
      // Hero links are literal URLs; Starlight does not prepend Astro's base.
      if (data.hero) for (const action of data.hero.actions) {
        if (action.link.startsWith('/') && !action.link.startsWith('//')) {
          action.link = siteBase + action.link.slice(1);
        }
      }
      return data;
    }),
  }),
};
