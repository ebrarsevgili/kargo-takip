# Cargo Tracking System

A cargo tracking and small logistics app built with ASP.NET Core 10 (Minimal API) and plain JavaScript. The user
interface text is in Turkish; code, comments and documentation are in English.

- **Public tracking page** with a route map and the movement history of a shipment.
- **Sign-in and registration** with cookie authentication and three roles.
- **Dashboard** with an overview, a searchable and paged shipment list, a detail panel and a printable shipping label.
- **Logistics:** couriers (with their own logins), vehicles, and assigning shipments to couriers with a capacity check.

The backend uses **no NuGet packages**, only what ships with ASP.NET Core. Data is stored in JSON files under `data/`.
The frontend has no build step; fonts, Leaflet and a QR code library are vendored in `wwwroot/vendor`.

## Roles

| Role | What it can do |
|---|---|
| **Admin** | Everything: all shipments (create, edit, delete, add movements, cancel), assign couriers, manage couriers and vehicles |
| **Courier** | See only the shipments assigned to them and add movements to them (cannot cancel) |
| **Customer** | Register on the sign-in page, create shipments, see only their own shipments |

Admin accounts are never created through registration. The first admin is seeded at start-up from configuration (see below).

## Running it

Requirement: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). With Visual Studio you need
**Visual Studio 2026** (18.0 or later); Visual Studio 2022 cannot target `net10.0`. VS Code and the terminal only need the SDK.

```bash
cd KargoTakip.Api
dotnet run
```

Then open <http://localhost:5205>:

- `/` is the public tracking page.
- `/login.html` is the sign-in and registration page; after signing in you land in the dashboard (`/app.html`).

In the Development environment an admin account is seeded from `appsettings.Development.json`:
user `admin`, password `Admin123!`. **Never use these values anywhere else.** Outside Development no admin is created
unless you supply one, for example:

```bash
export Admin__Username=ebrar
export Admin__Password='a-long-password-with-1-number'
```

Passwords need 8 to 128 characters with at least one letter and one digit. Create a few couriers and vehicles as the admin,
register a customer in a private window, create shipments and hand them to a courier to see all three views.

> .NET 8 and .NET 9 reach end of support on 10 November 2026, so the project targets .NET 10 (LTS, supported until November 2028).

## API

`KargoTakip.Api.http` has a ready-made request for every endpoint (Visual Studio and the REST Client extension in VS Code).
Authentication is a cookie, so sign in first (`POST /api/auth/login`) and keep the cookie.

| Method | URL | Who | Purpose |
|---|---|---|---|
| GET | `/api/track/{trackingNumber}` | public | Status, route and movements (receiver name masked, no phone) |
| POST | `/api/auth/register` | public | Creates a customer account and signs it in |
| POST | `/api/auth/login` | public | Signs in |
| POST | `/api/auth/logout` | signed in | Signs out |
| GET | `/api/auth/me` | signed in | Current user and role |
| GET | `/api/shipments?q=&status=&page=&pageSize=&unassigned=` | signed in | Search and paging; each role only gets its own scope |
| GET | `/api/shipments/stats` | signed in | Numbers for the overview |
| POST | `/api/shipments` | admin, customer | Creates a shipment and generates its tracking number |
| GET | `/api/shipments/{trackingNumber}` | signed in | Full details (own scope only) |
| PUT | `/api/shipments/{trackingNumber}` | admin | Edits a shipment that is still open |
| DELETE | `/api/shipments/{trackingNumber}` | admin | Deletes a shipment |
| PUT | `/api/shipments/{trackingNumber}/courier` | admin | Assigns or clears the courier (capacity check) |
| POST | `/api/shipments/{trackingNumber}/events` | admin, courier | Adds a movement and updates the status |
| GET/POST/PUT/DELETE | `/api/couriers` | admin | Manage couriers; a courier gets a login at creation |
| GET/POST/PUT/DELETE | `/api/vehicles` | admin | Manage vehicles |

Tracking number format: `KT` + 10 digits (for example `KT1234567890`).

### Statuses and rules

`Created → PickedUp → InTransit → OutForDelivery → Delivered`, and `Cancelled` (admin only) at any time.

- `Created` is added automatically when a shipment is created and cannot be added by hand.
- The status cannot go backwards. The same status may be added again (several transfers).
- A `Delivered` or `Cancelled` shipment accepts no more movements (409) and can no longer be edited or reassigned.
- A vehicle belongs to one courier. A shipment can only be assigned if the courier's open shipments stay within the
  vehicle's capacity. A courier with open shipments cannot be deleted.
- Invalid requests return 400 with a per-field error list. A shipment you are not allowed to see looks like a 404.

## Project layout

```
KargoTakip.Api/
├── Program.cs               service registration, authentication, rate limiting, public tracking endpoint
├── Endpoints/               shipments, couriers/vehicles and auth endpoints
├── Services/                ShipmentService, FleetService, AuthService (rules, role scoping) + ServiceResult
├── Security/                Actor (the caller as seen by services), PBKDF2 password hasher
├── Models/                  domain models and request/response DTOs
├── Storage/                 repository interfaces and their JSON file implementations
└── wwwroot/
    ├── index.html, track.js         public tracking page
    ├── login.html, login.js         sign-in and registration
    ├── app.html, app.js, views/     dashboard shell and its pages
    ├── label.html, label.js         printable A6 shipping label with a QR code
    ├── ui.js, map.js, cities.js     shared helpers, route map, the 81 provinces
    └── vendor/                      fonts, Leaflet, QR code library (with licenses)
```

Services never see HTTP: endpoints turn the signed-in user into an `Actor`, call a service, and map its `ServiceResult`
(Ok, NotFound, Invalid, Conflict, Forbidden) to a status code. Repositories are interfaces, so the JSON files could be
replaced by a database without touching the services.

## Security notes

- Passwords are hashed with PBKDF2 (per-user salt). Sign-in takes the same time for unknown and known user names.
- The auth cookie is `HttpOnly`, `SameSite=Strict`, expires after 8 hours of inactivity and returns 401/403 instead of
  redirecting API calls. On every request the user is looked up again, so a deleted account or a changed role takes
  effect immediately.
- **Before going live** serve the app over HTTPS and set `CookieSecurePolicy.Always` in `Program.cs`; the default
  (`SameAsRequest`) exists so plain `http://localhost` works.
- Sign-in and registration are limited to 20 requests per minute per IP, the public tracking endpoint to 30. Behind a
  reverse proxy such as nginx you need the `ForwardedHeaders` setting, otherwise every request looks like one IP.
- The UI never writes server data with `innerHTML`; data is always inserted as text.
- On start-up ASP.NET Core may log `No XML encryptor configured`: the key that protects the auth cookie is stored
  unencrypted on disk. Fine for local use; configure Data Protection (key storage and encryption) for a real deployment.
- The map loads tiles from OpenStreetMap, so the browser contacts `tile.openstreetmap.org`. Nothing else leaves the machine.

## Testing

The app was checked with `curl` smoke tests for every endpoint and role, and with Playwright browser tests for the login,
shipment list, detail panel, courier/vehicle pages and the label (including a QR decode and a print-to-PDF check on
the A6 page). Those scripts are not part of this repository.

## Limits

- JSON files suit small data sets and learning projects. Every write is serialized by one lock, which is correct for a
  single server instance but not for several.
- No notifications (SMS / e-mail), no carrier integrations, no live GPS; the route map draws the straight line between
  the origin and destination city plus the cities of the recorded movements.
