# Version 1
FROM node:26-alpine
RUN apk add tini

ENTRYPOINT ["/sbin/tini", "--", "npm", "start"]

COPY index.js ./
COPY package*.json ./
RUN npm install --omit=dev
