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

3. (Optional) Pin both images to the exact release version to avoid
   unintended upgrades. Edit docker-compose.yml and replace :latest with
   the version tag, e.g.:
     image: ghcr.io/holgerimbery/mate-webui:0.3.2
     image: ghcr.io/holgerimbery/mate-worker:0.3.2

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

Data & Backups
--------------
All data is stored in the Docker named volume (mate-data). It persists
across restarts and survives 'docker compose down' (but NOT
'docker compose down -v').
Use the built-in Backup & Restore in Settings → Data Management to
export a portable copy of your database.

Documentation
-------------
  https://github.com/holgerimbery/mate/wiki

Security note
-------------
Never expose this application directly to the internet without a
reverse proxy with TLS termination (nginx, Caddy, Traefik, etc.).
