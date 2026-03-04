mate — Multi-Agent Testing & Evaluation
Quick Start Guide
=====================================================

You received this file as part of the quickstart zip attached to a
GitHub Release. To always get the latest version, visit:
  https://github.com/holgerimbery/mate/releases/latest

Images are published to the GitHub Container Registry (GHCR):
  ghcr.io/holgerimbery/mate-webui
  ghcr.io/holgerimbery/mate-worker

Prerequisites
-------------
- Docker Desktop  https://www.docker.com/products/docker-desktop/
- For Entra ID authentication: an Azure App Registration
  See: https://github.com/holgerimbery/mate/wiki/User-API-Keys

What the stack starts
---------------------
  docker compose up -d starts four containers automatically:

  mate-postgres   PostgreSQL 17        — application database
  mate-azurite    Azurite 3.x          — Azure Blob Storage emulator (document storage)
  mate-webui      mate Web UI + API    — http://localhost:5000
  mate-worker     mate test-run worker — background job processor

  No extra flags or profiles are needed.

Steps
-----
1. Copy .env.template to .env
     Windows:   copy .env.template .env
     Mac/Linux: cp .env.template .env

2. Open .env in a text editor.

   Authentication is DISABLED by default (Authentication__Scheme=Generic).
   Anyone who can reach the app gets full Admin access.
   This is fine on a trusted internal network or behind a VPN.

   ⚠ If the app will be internet-accessible, set:
       Authentication__Scheme=EntraId
     and fill in the AzureAd__TenantId, AzureAd__ClientId,
     AzureAd__ClientSecret, and AzureAd__RedirectUri values.
   See: https://github.com/holgerimbery/mate/wiki/User-API-Keys

3. (Optional) Pin all images to the exact release version to avoid
   unintended upgrades. Edit docker-compose.yml and replace :latest with
   the version tag, e.g.:
     image: ghcr.io/holgerimbery/mate-webui:0.6.0
     image: ghcr.io/holgerimbery/mate-worker:0.6.0

4. Pull the images and start the stack:
     docker compose pull
     docker compose up -d

5. Open your browser at:
     http://localhost:5000

6. Use the Settings pages to configure agent connectors, test suites,
   and judge settings.

CI / Automation (API keys)
--------------------------
For headless CI/CD runs, generate a REST API key in
Settings → API Keys and pass it as the X-Api-Key header.
Interactive API docs are available at http://localhost:5000/scalar/v1
and the OpenAPI spec at http://localhost:5000/openapi/v1.json

Stopping
--------
  docker compose down

  ⚠ To also delete all stored data (database, blobs, logs):
      docker compose down -v

Data persistence
----------------
Data is stored in three named Docker volumes that survive restarts and
'docker compose down' (but NOT 'docker compose down -v'):

  mate-pgdata     PostgreSQL database files
  mate-azurite    Blob storage (uploaded documents, run artefacts)
  mate-logs       Application log files

Database backups are the responsibility of the infrastructure layer.
Use pg_dump or Azure Database Console to create portable snapshots.
The Settings → Data Management page keeps the backup button for API
compatibility, but it returns an empty file in PostgreSQL mode.

Documentation
-------------
  https://github.com/holgerimbery/mate/wiki

Security note
-------------
Never expose this application directly to the internet without a
reverse proxy with TLS termination (nginx, Caddy, Traefik, etc.).
