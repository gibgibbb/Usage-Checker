# Compatibility probe — 2026-09-14

## Tested environment

- Windows desktop package `OpenAI.Codex_26.908.4834.0`, executable `ChatGPT.exe`.
- Desktop backend under `%LOCALAPPDATA%/OpenAI/Codex/bin/bffc5354119c8421/codex.exe`.
- Separate CLI `0.154.0`; the running desktop backend is not assumed to be identical.
- User's existing ChatGPT authentication; no credentials extracted, copied, printed, or persisted by the monitor/probe.

## Results

| Check | Result | Evidence / limit |
|---|---|---|
| Independent usage read | PASS | Private stdio app-server returned both 300-minute and 10,080-minute Codex windows. |
| Account matching | PASS | SHA-256 account-ID prefix `30c956675e0ba9d0` matches the desktop tool, independent Python probe and compiled widget. Percentages changed as ongoing work consumed usage. |
| Completion | PASS, isolated backend | Ephemeral turn returned `turn/completed` with status `completed`. |
| Cancellation | PASS, isolated backend | Probe-owned `turn/interrupt` produced terminal status `interrupted`. No desktop turn was cancelled. |
| Approval waiting | PASS, isolated backend | `item/commandExecution/requestApproval` and `activeFlags: [waitingOnApproval]` observed. Pending probe was interrupted without any approval response. |
| Terminal failure | PASS, isolated backend | Deliberately invalid model produced status `failed` and `error` event. |
| RPC validation failure | PASS | Invalid thread ID returned `-32600`. Separate from terminal failure. |
| Independent server sees desktop runtime | NO | `thread/loaded/list` returned an empty list while desktop work was active. |
| Standard daemon attachment | UNAVAILABLE | Bundled `app-server daemon version` could not connect to the standard control socket (Windows socket error 10050). This alone does not prove all private IPC is absent. |
| Desktop transport | NO PUBLIC LISTENER VERIFIED | Running desktop backend has no `--listen` flag; earlier TCP inspection found no listening TCP endpoint. No private-pipe interception attempted. |
| Hook discovery | PASS | `hooks/list` found all four installed user hook definitions with no errors/warnings. |
| Hook trust | PENDING | All four reported `untrusted` on the probe read. No trust bypass performed. |
| Desktop hook delivery | NOT YET VERIFIED | Requires trust and an actual desktop lifecycle test. Isolated server lifecycle tests are not a substitute. |
| Exact active desktop tab | UNVERIFIED | No read-only supported selection source established. |

## Phase 2 validation

- Compiled C# WPF GUI successfully reads usage using its own backend.
- Rendered actual WPF output inspected: both percentages and unavailable-status label fit the compact window.
- Live desktop window discovered through Computer Use after launching through that service; accessibility reports separate five-hour and weekly labels.
- Context menu opens. Full tray/theme/Alt+Tab/desktop-exit acceptance remains to be completed; these are implemented but not all manually verified.
- Six passing tests cover reversed window ordering, quota-bucket isolation, missing data, out-of-range values, unrelated legacy buckets, unexpected window length and unknown display.
- Normal usage refresh is 60 seconds; transient failure retry is 30, 60, 120, 240, then 300 seconds.
- The GUI intentionally says `Status —` until a desktop event source is proven and connected.

## Safety and reproducibility

Probe turns are ephemeral. None of the existing desktop tasks were resumed, subscribed, interrupted or modified. The approval test never answered a server approval request. Model failure was deliberately induced on the isolated test turn only.

Hook definitions are in the user's `.codex/hooks.json` and point to reviewable source in `src/HookRelay.cs`. The relay writes an allowlisted metadata event and returns `{}`. There is no approval decision, no additionalContext, and no transcript retention. Installed hooks are removable through `probe/install-hooks.ps1 -Remove`. Trust must be performed by the user in Codex.

Generated protocol schemas and machine-local JSON results are excluded from Git. Raw exercise output was observed on 2026-09-14 at 07:07 UTC; an early probe version overwrote `independent.json` during the later read-only hook check. This script now saves exercise runs separately to `exercise.json`; the table above records the actually observed results rather than fabricating a replacement capture.

## References

- [Official app-server protocol](https://learn.chatgpt.com/docs/app-server)
- [Official hook lifecycle and trust behavior](https://learn.chatgpt.com/docs/hooks)
- Exact installed protocol generated with `codex app-server generate-json-schema`.

## Next acceptance gate

After trusting the hooks, observe a fresh desktop prompt through completion, an approval wait and a user cancellation. Correlate actual hook `session_id` and `turn_id` with the desktop task, and record gaps such as terminal failures (there is no assumed dedicated failure hook). Only then implement the status adapter. If delivery requires a desktop restart, defer that restart until the user has no work running.
