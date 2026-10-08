FROM node:24-bookworm-slim AS build
WORKDIR /app/frontend
RUN corepack enable && corepack prepare pnpm@11.19.0 --activate
COPY frontend/ ./
RUN pnpm install --frozen-lockfile
RUN pnpm build

FROM node:24-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production PORT=3000 HOSTNAME=0.0.0.0
COPY --from=build --chown=node:node /app/frontend/apps/web/.next/standalone ./
COPY --from=build --chown=node:node /app/frontend/apps/web/.next/static ./apps/web/.next/static
USER node
EXPOSE 3000
CMD ["node", "apps/web/server.js"]
