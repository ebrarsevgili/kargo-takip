# Cargo Tracking System

A small cargo tracking app built with ASP.NET Core 10 (Minimal API).

- **Public tracking page:** shows the status and movement history of a shipment by tracking number.
- **Admin panel:** create shipments, list and filter them, add movements (status updates).
- **REST API:** the panel and the tracking page use it; Postman, `curl` or any other client works too.

The project uses **no NuGet packages**, only what ships with ASP.NET Core. Data is stored in `data/shipments.json`.
The user interface text is in Turkish.

## Running it

Requirement: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). With Visual Studio you need
**Visual Studio 2026** (18.0 or later); Visual Studio 2022 cannot target `net10.0`. VS Code and the terminal only need the SDK.

```bash
cd KargoTakip.Api
dotnet run
```

Then open:

- Tracking page: <http://localhost:5205>
- Admin panel: <http://localhost:5205/admin.html>

In the Development environment the admin key is `gelistirme-anahtari` (set in `appsettings.Development.json`).
Create a shipment in the panel first, then look its tracking number up on the tracking page.

> .NET 8 and .NET 9 reach end of support on 10 November 2026, so the project targets .NET 10 (LTS, supported until November 2028).

## API

Admin endpoints require the `X-Api-Key` header. `KargoTakip.Api.http` has ready-made requests
(works with Visual Studio and with the REST Client extension in VS Code).

| Method | URL | Auth | Purpose |
|---|---|---|---|
| GET | `/api/track/{trackingNumber}` | public | Status and movements of a shipment (receiver name masked, no phone) |
| POST | `/api/shipments` | key | Creates a shipment and generates its tracking number |
| GET | `/api/shipments?status=InTransit` | key | Lists shipments (`status` is optional) |
| GET | `/api/shipments/{trackingNumber}` | key | Full details of one shipment |
| POST | `/api/shipments/{trackingNumber}/events` | key | Adds a movement and updates the status |

Tracking number format: `KT` + 10 digits (for example `KT1234567890`).

### Statuses and rules

`Created → PickedUp → InTransit → OutForDelivery → Delivered`, and `Cancelled` at any time.

- `Created` is added automatically when a shipment is created and cannot be added by hand.
- The status cannot go backwards (for example `PickedUp` cannot be added while the shipment is `InTransit`).
  The same status may be added again (several transfers).
- A `Delivered` or `Cancelled` shipment accepts no more movements (409).
- Invalid requests return 400 with a per-field error list, unknown shipments return 404.

## Project layout

```
KargoTakip.Api/
├── Program.cs                      endpoints, authorization, rate limiting, service registration
├── Models/                         Shipment, TrackingEvent, request/response DTOs
├── Services/ShipmentService.cs     validation, tracking number generation, status rules
├── Storage/
│   ├── IShipmentRepository.cs      storage contract
│   └── JsonShipmentRepository.cs   JSON file implementation
└── wwwroot/                        index.html (tracking), admin.html (panel), app.css, common.js
```

## Security notes

- Admin endpoints are protected by one shared API key, compared in constant time. A real application should use user
  accounts and role-based authorization (ASP.NET Core Identity / JWT).
- **Before going live** do not keep the key in code; supply it through an environment variable:
  `Admin__ApiKey=a-long-random-value`. If no key is configured the admin endpoints return 503.
- The app does not redirect to HTTPS; run it behind HTTPS in production or the key travels in clear text.
- The tracking endpoint is limited to 30 requests per minute per IP (against tracking-number guessing). Behind a
  reverse proxy such as nginx you need the `ForwardedHeaders` setting, otherwise every request looks like one IP.
- The UI never writes server data with `innerHTML`; data is always inserted as text.

## Limits

- A JSON file suits small data sets and learning projects; many records or several server instances need a real database.
- No notifications (SMS / e-mail), no carrier integrations, no route or map.
