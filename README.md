# Codex Usage Monitor

A small Windows WPF companion, built with the Windows .NET Framework compiler. No Python, Node.js, npm packages, or separate SDK is required to run the widget. Python is only used for the compatibility probe.

## Run

Double-click `bin/CodexMonitor.exe` while Codex is open. The left percentage is five-hour remaining; the next is weekly remaining. Hover for labels, reset times and the last successful refresh.

Right-click for refresh, always-on-top, system/dark/light theme, centering, tray hiding, and exit. Double-click the widget to hide it. Restore it from the tray. Position, theme and topmost preference are saved under `%LOCALAPPDATA%/CodexUsageMonitor/settings.json`.

The widget refreshes every 60 seconds, retries with backoff on connection failure, and labels retained values stale. Missing windows display an em dash. It launches its own usage-only Codex backend over private stdio. It never submits prompts, answers approvals, or controls desktop tasks.

`Status —` is intentional: desktop task events have not been connected. It does not mean the desktop is idle. This is phase 2, the usage widget. Automatic launch-at-Codex-start is a later lifecycle phase; it is not installed yet. The widget exits after a detected Codex desktop process disappears for eight seconds, but closing only a window while Codex stays resident may not trigger that.

## Build and test

Run `./build.ps1` in PowerShell. It builds the monitor, hook relay and six quota-normalization tests. The build uses the installed .NET Framework C# compiler and WPF assemblies, not the installed .NET 6 runtime. Output is in `bin/`.

Override backend discovery with `CODEX_MONITOR_BINARY` if needed. By default the monitor prefers a running desktop-bundled Codex executable, then the newest local desktop backend directory. It lets Codex manage authentication; it never opens auth.json.

## Compatibility probe

See `docs/compatibility.md`. To repeat the isolated protocol exercises:

```powershell
python probe/probe.py --binary 'ABSOLUTE_PATH_TO_CODEX.exe' --exercise
```

This intentionally spends a small amount of usage on ephemeral probe turns, including one invalid-model failure and a pending command approval that is cancelled without being answered. Do not confuse these ephemeral backend tests with verification of the desktop UI's lifecycle events.

Four user hooks were installed for trust review. They run `bin/CodexMonitor.HookRelay.exe` through PowerShell, retain only event/session/turn metadata in `probe/results/hook-events`, and output `{}` with success. No prompt or tool content is retained. The hook relay is diagnostic only and is not the phase-3 status adapter.

Review/trust the definitions through Codex CLI `/hooks`, then use the desktop to exercise a fresh prompt, completion, an approval wait, and user stop. Desktop restart may be necessary to reload definitions; do not restart it while work is in flight. Delivery is verified only when matching desktop turn IDs arrive in the hook-event folder.

To remove these probe hook definitions while preserving unrelated hooks:

```powershell
./probe/install-hooks.ps1 -Remove
```

The installer uses PowerShell 7 (`ConvertFrom-Json -AsHashtable`). Existing hook files are backed up before updates. No trust state or approval policy is edited.

## Current limits

- Experimental Codex protocol: version upgrades may need compatibility updates.
- No passive desktop socket or active-tab interface has been verified.
- Hook delivery is pending Codex trust and a desktop lifecycle test.
- No complete task-state indicator, spinner, startup watcher, cloud-task tracking, or installer yet.
- Resource use must include both monitor and usage backend. The first WPF process sample was about 160 MiB and its live helper about 105 MiB; this is not yet an ultra-low-memory implementation.
- No cached account limits are persisted to disk. During a failed refresh, only the existing in-memory snapshot is shown as stale.
