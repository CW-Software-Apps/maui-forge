# Remote Access — Implementation Plan

## Architecture

```
┌─ Machine A (Server) ─────────────────────┐
│  maui-forge --serve --token abc123       │
│                                          │
│  ┌─ Kestrel :5123 ────────────────────┐  │
│  │  REST API + SignalR               │  │
│  │  Token middleware (X-MauiForge)   │  │
│  │  UDP broadcast responder :5124    │  │
│  └───────────────────────────────────┘  │
│                                          │
│  Scans local dirs, builds, deploys       │
└──────────────────────────────────────────┘
         ▲                    ▲
         │ HTTP + Token       │ UDP discovery
         ▼                    ▼
┌─ Machine B (Client) ─────────────────────┐
│  maui-forge                              │
│  ┌─ TUI ─────────────────────────────┐   │
│  │  "Connect to Remote..."           │   │
│  │  → Scans network (UDP broadcast)  │   │
│  │  → Lists servers found            │   │
│  │  → Enter token                    │   │
│  │  → Fetches app list via HTTP      │   │
│  │  → Shows remote apps in table     │   │
│  │  → Actions send HTTP to server    │   │
│  └───────────────────────────────────┘   │
│  ┌─ Web Dashboard ───────────────────┐   │
│  │  "Remote Connection" panel        │   │
│  │  → Server IP + token → proxy/view │   │
│  └───────────────────────────────────┘   │
└──────────────────────────────────────────┘
```

---

## Files to create / modify

### New files

| File | Purpose |
|------|---------|
| `Services/RemoteDiscoveryService.cs` | UDP broadcast discovery (client + server) |
| `Services/RemoteClientService.cs` | HTTP client: fetch apps, execute actions, stream logs from remote server |
| `UI/RemoteConnectScreen.cs` | TUI screen: scan, select server, enter token, show remote apps |

### Modified files

| File | Changes |
|------|---------|
| `Program.cs` | `--serve` / `--token` / `--discover` CLI args; wire RemoteDiscoveryService + RemoteClientService |
| `Services/WebStartup.cs` | Bind to `0.0.0.0` in serve mode; add `TokenMiddleware`; `GET /api/remote/info`; UDP broadcast responder on startup |
| `UI/AppListScreen.cs` | New menu item "Connect to Remote..." → opens RemoteConnectScreen |
| `UI/AppDetailScreen.cs` | When in remote mode, send actions via HTTP instead of local execution |
| `Services/StateService.cs` | Persist known remotes: `{ Host, Port, Token, LastUsed }` |
| `Models/PersistentState.cs` | Add `KnownRemotes` list |
| `wwwroot/index.html` | Remote connection panel + token prompt in web dashboard |

---

## Phase 1 — Server mode

### CLI args (Program.cs)

```
--serve          Bind Kestrel to 0.0.0.0 (not localhost)
--token <val>    Require this token for all API access
                 (if omitted, auto-generate and print at startup)
--port <n>       Web port (default 5123)
--discover-port  UDP discovery port (default 5124)
```

### Token auth (WebStartup.cs)

```csharp
// Middleware: check X-MauiForge-Token header on all /api/ routes
// Skip check for GET /api/remote/info (returns { version, hostname, requiresToken })
// If token matches (or no token configured), pass through
// If token mismatch, return 401
```

All existing API endpoints remain unchanged — just add the middleware filter.

### UDP broadcast responder (RemoteDiscoveryService.cs — server side)

```csharp
void StartResponder(int port)
  // Listen on UDP port 5124
  // On receiving "MAUI_FORGE_PING":
  //   Respond "MAUI_FORGE_PONG|<hostname>|<web-port>|<requires-token>"
```

---

## Phase 2 — Client discovery (RemoteDiscoveryService.cs — client side)

```csharp
List<RemoteServer> Discover(int timeoutMs = 3000)
  // Broadcast UDP "MAUI_FORGE_PING" to 255.255.255.255:5124
  // Collect responses for timeoutMs
  // Return list of { IP, Port, Hostname, TokenRequired }

record RemoteServer(string Host, int Port, string Hostname, bool TokenRequired);
```

---

## Phase 3 — Remote Client (RemoteClientService.cs)

```csharp
class RemoteClientService
{
  bool IsConnected { get; }
  RemoteServer CurrentServer { get; }

  void Connect(RemoteServer server, string? token);
  void Disconnect();

  // App operations (all send HTTP to remote server)
  Task<List<AppEntry>> GetApps();
  Task<AppEntry> RefreshApp(string dir);
  Task BumpVersion(string dir, string ver, string build);
  Task<GitResult> GitPull(string dir);
  Task<BuildResult> Build(string dir, string platform, string config);
  Task<BuildResult> BuildAndRun(string dir, string platform, ...);
  // ... all actions from the API
}
```

Each method calls the corresponding REST endpoint on the remote server with the token header.

For build logs: the client opens a **SignalR connection** to the remote server's `/hubs/logs` hub, subscribing to the same `LogReceived` events. The TUI displays them live just like local builds.

---

## Phase 4 — TUI integration

### RemoteConnectScreen.cs

```
── Remote Connection ──────────────────────────────────────
  Scanning network...  (3 sec timeout)
  
  Found 2 servers:
  > 192.168.1.50:5123  Build-Mac.local      (token required)
  > 192.168.1.80:5123  Windows-Dev-PC       (token required)
  
  [ ] Enter IP manually...
  [ ] Back
```

After selecting a server → prompted for token:
```
  Token: [________________]
  [Connect]  [Cancel]
```

On success → replaces local app list with remote apps.

### AppListScreen changes

New menu item at the bottom:
```
  >  Connect to Remote...
  >  Quit
```

When connected:
- Shows "🌐 Remote: Build-Mac.local (192.168.1.50)" in the header
- App table shows remote apps
- "Disconnect" replaces "Connect to Remote..."

### AppDetailScreen changes

When in remote mode:
- All actions (bump, run, archive, pull, push, build) call `RemoteClientService` methods
- Build logs stream via remote SignalR
- "Back" returns to remote app list

---

## Phase 5 — Web Dashboard integration

### Token prompt

When the server requires a token and a request lacks it:
- API returns 401
- Frontend shows a modal: "Enter remote access token"
- Token saved in `sessionStorage`
- Retry the request with the token

### Remote connection panel

New section in sidebar:
```
── Remote Access ──
  Status: ● Connected to Build-Mac (192.168.1.50)
  [Disconnect]
```

When not connected:
```
── Remote Access ──
  [Connect to Remote...]
```

The web dashboard can also discover servers via the UDP broadcast (using an API call or JS UDP — JS can't do raw UDP, so the discovery would need to be done server-side and exposed via an endpoint like `GET /api/remote/scan`).

---

## Data flow (example: "Run iOS")

```
Client TUI                           Server HTTP API              Server local
─────────────────                    ───────────────────          ───────────
User: selects "Run iOS"
User: picks device
User: enters token (first time)
       │
       │ POST /api/apps/run
       │ { dir, platform, deviceId, ... }
       │ Headers: X-MauiForge-Token: abc123
       ▼
                                 Verify token ✓
                                 Start build process
                                 Return 202 Accepted
       │
       │ Open SignalR /hubs/logs
       ▼
Show live logs                      ◄── LogReceived events ────  dotnet build
in progress modal                                                  mlaunch ...
       │
       │ "===STEP:DONE==="
       ▼
Show "Launched successfully"
```

---

## State persistence

### PersistentState.cs — new fields

```csharp
List<SavedRemote> KnownRemotes = new();

record SavedRemote
{
    string Host;
    int Port;
    string Hostname;
    string? Token;       // encrypted or plain (user's choice)
    DateTime LastUsed;
}
```

Known remotes appear in the discovery list with a "saved" indicator and auto-fill the token.

---

## Security considerations

| Concern | Mitigation |
|---------|-----------|
| Token in plaintext in state file | Optional: hash the token, store only the hash. Server compares hash. |
| Token sent over HTTP | Add `--cert` / `--https` for HTTPS support (future). Token over LAN is acceptable for most users. |
| Unauthorized discovery | Discovery only reveals IP and hostname — no app data. Token required for all operations. |
| UDP spoofing | Discovery is advisory only. Client validates server by connecting and checking token challenge. |

---

## Implementation order

1. **`--serve` + `--token`** in Program.cs + WebStartup.cs (bind 0.0.0.0, token middleware, `/api/remote/info`)
2. **RemoteDiscoveryService.cs** (UDP responder + client scanner)
3. **RemoteClientService.cs** (HTTP client for all operations + SignalR log streaming)
4. **RemoteConnectScreen.cs** (TUI: scan → select → auth → remote app list)
5. **AppListScreen.cs** + **AppDetailScreen.cs** remote mode wiring
6. **Web dashboard**: token prompt modal, remote status indicator
7. **State persistence**: save known remotes

---

## Open questions

1. **Log streaming in TUI over remote**: SignalR from console app requires `Microsoft.AspNetCore.SignalR.Client` NuGet. Alternatives: poll a log endpoint, or simpler — just show "Build started" and notify when done (no live streaming in remote TUI mode).

2. **File paths**: Remote apps have different directory structures. Paths shown in the TUI are from the server's perspective. "Open Folder" / "Open IDE" actions open on the SERVER machine, not the client.

3. **Android emulator / iOS devices**: Connected to the server machine. The client sees them as available devices on the server.
