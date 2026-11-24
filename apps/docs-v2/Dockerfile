# Build context: repository root. Content is generated from the canonical wiki.
FROM node:24-bookworm-slim AS build
WORKDIR /repo/apps/docs-v2
COPY apps/docs-v2/package*.json ./
RUN npm ci
COPY apps/docs-v2/ ./
COPY wiki/ /repo/wiki/
COPY docs-site/sidebar.mjs /repo/docs-site/sidebar.mjs
COPY docs-site/public/ /repo/docs-site/public/
COPY docs-site/src/assets/ /repo/docs-site/src/assets/
# The configuration builder's catalog is generated from the real configuration files (scripts/config-catalog.mjs).
COPY src/Api/appsettings.json src/Api/appsettings.Development.json src/Api/appsettings.Docker.json /repo/src/Api/
COPY .template.config/template.json /repo/.template.config/template.json
COPY .env.example docker-compose*.yml /repo/
COPY .git/ /repo/.git/
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*
ARG DOCS_SITE=https://docs.example.com
ARG NEXT_PUBLIC_DOCS_BASE=
ARG DOCS_PRODUCTION=false
ENV DOCS_SITE=$DOCS_SITE NEXT_PUBLIC_DOCS_BASE=$NEXT_PUBLIC_DOCS_BASE DOCS_PRODUCTION=$DOCS_PRODUCTION
RUN npm run build

FROM node:24-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production PORT=3000 HOSTNAME=0.0.0.0
COPY --from=build --chown=node:node /repo/apps/docs-v2/.next/standalone ./
COPY --from=build --chown=node:node /repo/apps/docs-v2/.next/static ./.next/static
COPY --from=build --chown=node:node /repo/apps/docs-v2/public ./public
USER node
EXPOSE 3000
CMD ["node", "server.js"]
