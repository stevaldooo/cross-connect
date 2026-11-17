# CrossConnect

Share one keyboard and mouse across two Windows computers.

> ⚠️ Early development (M3). Not ready for daily use.

## Status

| Milestone | Scope | Status |
|---|---|---|
| M1 | TCP communication (HELLO / PING-PONG handshake) | ✅ Done |
| M2 | Device pairing | ⏳ Not started |
| M3 | Keyboard forwarding | ✅ Done |
| M4 | Mouse forwarding | Planned |
| M5 | Screen-edge switching | Planned |
| M6 | UI and reliability | Planned |
| M7 | Security and release | Planned |

## Keyboard forwarding (M3)

Two Windows machines on the same LAN:

1. **Host** (the machine with the physical keyboard): run CrossConnect → *Start as Host*.
2. **Client** (the machine that will receive input): run CrossConnect → *Connect to Host*, enter the host's IP.
3. On the host, press **Start Forwarding**. Every keystroke is now applied on the client instead of the host.
4. Press **Scroll Lock** at any time to pause or resume forwarding.
5. Held keys are released on the client when forwarding stops or the connection drops.

## Build and run

Requires the .NET 10 SDK and Windows.

```powershell
dotnet build cross-connect/CrossConnect.slnx
dotnet run --project cross-connect/src/CrossConnect.App
```

## Tests

```powershell
dotnet test cross-connect/CrossConnect.slnx
```

Unit tests cover the protocol serialization, pressed-key tracking, and both
forwarding/receiving session orchestrators (using fakes — no real keyboard
hooks or input injection run during tests).
