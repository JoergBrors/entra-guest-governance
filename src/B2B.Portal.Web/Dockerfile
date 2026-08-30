FROM node:22-alpine AS build
WORKDIR /app

ARG VITE_API_BASE_URL=http://localhost:5000
ARG VITE_DEV_PLATFORM_TENANT_ID=dev-tenant-a
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL
ENV VITE_DEV_PLATFORM_TENANT_ID=$VITE_DEV_PLATFORM_TENANT_ID

COPY src/B2B.Portal.Web/package*.json ./
RUN npm ci

COPY src/B2B.Portal.Web/ ./
RUN npm run build

FROM nginx:stable-alpine AS runtime
COPY src/B2B.Portal.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
