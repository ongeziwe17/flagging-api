**Feature Flags API**

- Minimal ASP.NET Core 8 API for feature flags with SQL Server storage and Redis caching.
- Includes Docker Compose environments for development, staging, and production.

**Dev Setup**

- Prerequisites: Docker Desktop, Docker Compose v2, .NET 8 SDK (optional for local dev without containers).
- Create an env file: copy `infra/.env.dev.example` to `infra/.env.dev` and set strong secrets.
- Start stack: `docker compose -f infra/docker-compose.yaml -f infra/docker-compose.dev.yaml --env-file infra/.env.dev up -d --build`
- API: `http://localhost:8080` (serves `wwwroot/admin` UI and `/api/*`).
- SQL Server: `localhost:1433` (user `sa`, password from env). Redis: `localhost:6379` (password from env).

**Staging/Prod**

- Use the same base file with overlays: staging `infra/docker-compose.staging.yaml`, prod `infra/docker-compose.prod.yaml`.
- Example: `docker compose -f infra/docker-compose.yaml -f infra/docker-compose.staging.yaml --env-file infra/.env.staging up -d --build`
- Ensure `Admin__BootstrapAdminKey` (via `ADMIN_KEY`) is set before first run to bootstrap an admin API key.

**Notes**

- The API runs EF Core migrations at startup and seeds default environments (dev, staging, prod).
- Override connection strings via env: `ConnectionStrings__Default`, `ConnectionStrings__Redis`.
