# AGENTS.md

## Project Overview

IoT agricultural irrigation automation system deployed on Raspberry Pi. Three Docker services orchestrated via `docker-compose.yml`.

**Architecture:** Arduino (sensor/relay) <--BLE/Serial--> ble-service (Python) <--MQTT--> backend (.NET 8) <--SQLite--> readings.db. Static HTML frontend served by backend.

## Services

| Service | Tech | Port | Notes |
|---|---|---|---|
| `mqtt` | Eclipse Mosquitto 2.0 | 1883 (TCP), 9001 (WS) | Anonymous access enabled |
| `ble-service` | Python 3.11 + Bleak + paho-mqtt | — | Runs privileged (needs `/dev/rfcomm0` and D-Bus) |
| `backend` | .NET 8 (C#) + EF Core SQLite | 5000 | Serves API + static HTML from `wwwroot/` |

## MQTT Topics

- `ble/readings` — sensor data (temp/humidity) from Arduino
- `ble/status` — relay states + connection status
- `ble/commands` — relay ON/OFF commands sent to Arduino
- `ble/diagnostico` — passive BLE scan results (ESP32 etc.)

## Deploy (Production)

Development happens on PC, deployed to Raspberry Pi:

```bash
# On dev PC
git add . && git commit -m "description" && git push

# On Raspberry Pi
git pull
docker compose up --build -d
```

External access via Cloudflare tunnel: `cloudflared tunnel --url http://localhost:5000 &`

## Local Development

### Backend (.NET 8)
```bash
cd backend
dotnet restore WebApplication1.csproj
dotnet run
```
Runs on `http://localhost:5000`. SQLite DB auto-created at `data/readings.db`. Kestrel listens on all interfaces port 5000.

### BLE Service (Python)
Requires Bluetooth hardware (`/dev/rfcomm0` for HC-05 module). Run inside Docker — not practical locally without the Pi's Bluetooth stack.

```bash
cd ble-service
pip install -r requirements.txt
python -u main.py
```

## Key Gotchas

- **`ble-service` must run privileged** (`privileged: true` in compose) — it needs direct access to `/dev` and `/var/run/dbus` for Bluetooth.
- **SQLite WAL mode** is set explicitly at startup (`PRAGMA journal_mode=WAL`).
- **BleScanLogs FIFO**: Backend auto-prunes `BleScanLogs` table to max 1000 rows on insert (`Program.cs:126-138`).
- **`AutomationService`** runs every 10s (`BackgroundService`), compares humidity to stage thresholds and sends relay commands via MQTT. Skips cultivos in `MANUAL` mode.
- **Python CMD uses `["python", "-u", "main.py"]`** — the `-u` flag is critical for real-time Docker logs (unbuffered stdout).
- **Backend `Program.cs` is a single-file minimal API** — no controllers, no startup class. All endpoints defined inline with `app.MapGet/MapPost/MapDelete`.
- **MQTT broker hostname is `mqtt`** (Docker service name) — hardcoded in both backend and ble-service.

## DB Schema (EF Core — Code-First)

Defined in `backend/Data/AppDbContext.cs`. Tables: `Readings`, `Cultivos`, `Etapas`, `RelayStatuses`, `BleScanLogs`. Database is created via `EnsureCreated()` (no migrations).

## File Structure

```
backend/
  Program.cs          — API endpoints + MQTT client setup (single-file minimal API)
  Data/AppDbContext.cs — EF Core DbContext
  Models/             — Reading, Cultivo, Etapa, RelayStatus, BleScanLog, etc.
  Services/AutomationService.cs — Background irrigation logic
  wwwroot/            — Static HTML frontend (index, estado, pozo, recetas, diagnostico)
ble-service/
  main.py             — BLE serial bridge + passive BLE scanner + MQTT bridge
mosquitto/
  mosquitto.conf      — Broker config (anonymous, WS on 9001)
data/
  readings.db         — SQLite database (git-tracked, lives on Pi)
```
