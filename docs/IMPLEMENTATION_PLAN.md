# LLamaVoz — MVP Implementation Plan

> App name: **LLamaVoz** (the PRD's provisional name "VozLibre" was replaced by the user's chosen name).
> Derived from [PRD.md](PRD.md) v0.9 and [CLAUDE.md](CLAUDE.md). This document turns the PRD into concrete engineering decisions and an ordered build sequence. Where the PRD deliberately left choices open ([Pendiente de validación]), this plan names a **primary candidate** and marks it `(validate in POC)` — candidates are starting points for the POCs, not final commitments.

---

## 1. Tech stack (concrete candidates)

The PRD recommends Alternative A (§25.4): .NET + WinUI 3 UI, separate local inference service. This plan follows that.

| Layer | Choice | Rationale / notes |
|---|---|---|
| Language / runtime | **C# / .NET 8 (LTS)** | First-class UIA, audio, hotkey, tray APIs; team velocity per PRD §25.3 |
| UI framework | **WinUI 3 (Windows App SDK)** | Native Win11 look, accessibility support, per PRD recommendation |
| Tray icon | `Shell_NotifyIcon` via **H.NotifyIcon.WinUI** package (or direct P/Invoke) | WinUI 3 has no built-in tray support |
| Global hotkeys | **Low-level keyboard hook (`SetWindowsHookEx` WH_KEYBOARD_LL)** for push-to-talk; `RegisterHotKey` for toggle | `RegisterHotKey` alone can't detect key-up, which push-to-talk needs. Include the PRD's "stuck key" safety timeout (FR-006) |
| Audio capture | **WASAPI via NAudio** (`WasapiCapture`, `MMDeviceEnumerator`) | Device enumeration, default-device fallback, level meter (FR-004) |
| VAD | **Silero VAD** (ONNX model, MIT license) | Small, fast on CPU, widely used; runs inside the inference service |
| ASR | **Whisper-family model via whisper.cpp / Whisper.net bindings** `(validate in POC-2)` | es/en + built-in language detection (FR-010); MIT-licensed runtime and models; presets = model sizes (Fast/Balanced/Max quality) |
| Cleanup LLM | **Small quantized instruct model (3B-class) via llama.cpp / LLamaSharp** `(validate in POC-3; license review per R-13 before fixing the model)` | Runs on CPU with GPU offload optional; guardrail system prompt + trap-case test set for FR-016 |
| Translation es↔en | Two candidates: **(a) the same local LLM prompted for translation**, **(b) a dedicated Marian/OPUS-MT-class model** `(validate in POC-4; NLLB is CC-BY-NC — likely unusable commercially, verify)` | Option (a) means one fewer model download; quality comparison is exactly what POC-4 is for |
| Acceleration | **CPU baseline; DirectML/CUDA offload evaluated in POC-5** | Presets mapped to hardware after real measurements — no invented benchmarks |
| IPC (UI ↔ inference service) | **gRPC over named pipes** (`Grpc.AspNetCore` + named-pipe transport) | Typed contracts, streaming for partial transcripts (FR-011), easy to supervise/restart (FR-038) |
| Text insertion | **UIA (`IUIAutomation` COM via CsWin32) → `SendInput` with `KEYEVENTF_UNICODE` → clipboard+Ctrl+V with backup/restore → manual fallback** | Exactly the PRD §21 tiered strategy |
| Local storage | **SQLite (`Microsoft.Data.Sqlite`) + DPAPI (`ProtectedData`) for encryption at rest** | NFR-11; settings, dictionary, optional history |
| Installer / updates | **Velopack** (successor to Squirrel) with Authenticode signing; per-user install, no admin (FR-001) | Signed delta updates (FR-037); MSIX kept as an alternative if tray/hook restrictions prove acceptable |
| Testing | **xUnit** (unit), **FlaUI** (automated UIA insertion tests over the §27 matrix) | FlaUI drives real apps — the insertion test suite is a first-class deliverable |

## 2. Solution structure

```
LLamaVoz.sln
├── src/
│   ├── LLamaVoz.App/            # WinUI 3: tray, overlay, settings, onboarding
│   ├── LLamaVoz.Core/           # Pipeline orchestration, modes, domain types (no UI, no ML)
│   ├── LLamaVoz.Insertion/      # UIA / SendInput / clipboard tiered engine + target-window verification
│   ├── LLamaVoz.Audio/          # WASAPI capture, device management, level metering
│   ├── LLamaVoz.Inference/      # Separate process: VAD, ASR, LLM, translation, model manager
│   ├── LLamaVoz.Ipc/            # gRPC contracts shared by App and Inference
│   └── LLamaVoz.Storage/        # Encrypted SQLite: settings, dictionary, optional history
├── tests/
│   ├── LLamaVoz.Core.Tests/
│   ├── LLamaVoz.Insertion.Tests/  # FlaUI suite over the compatibility matrix
│   └── LLamaVoz.Intent.Tests/     # FR-016 "no invented facts" trap-case set
└── poc/                           # Throwaway POC spikes (kept for reference)
```

Two processes at runtime: **App** (UI, hotkeys, insertion — things that must touch the user's session) and **Inference** (heavy ML, restartable on crash per FR-038, supervised via heartbeat).

## 3. Milestones (ordered, gated)

Follows PRD §36 phases. Each milestone has an exit gate; do not start full feature work until the POC gates pass (PRD §34 requirement).

### M0 — Scaffolding (small)
Solution layout above, CI build, code signing cert acquisition started (long lead time — SmartScreen reputation, R-10), EditorConfig/analyzers.

### M1 — POC-1: Insertion spike ⚠️ highest risk (R-01)
Console/minimal-UI prototype of the tiered insertion engine. Test against the §27 matrix: Word, Outlook, Chrome/Edge fields, VS Code, Windows Terminal, Notion, Slack, Google Docs (expected weak). Measure per-app success by method; verify non-destructive clipboard restore (POC-6) and target-window verification under deliberate window switching.
**Gate:** ≥98% success on "green" apps; zero wrong-window or destructive insertions; documented honest matrix.

### M2 — POC-2/3/4/5: Local models spike
Bench harness (not product code) measuring on mid-range hardware (16 GB RAM, no dGPU as baseline):
- ASR: WER es/en + latency-to-final for 2–3 Whisper sizes (POC-2, targets §31)
- Cleanup: punctuation F1 + trap-case set for fact invention + latency for 2–3 small LLMs (POC-3)
- Translation: LLM-as-translator vs dedicated model, human eval on entity preservation (POC-4)
- CPU vs DirectML vs CUDA per model (POC-5) → define Fast/Balanced/Max presets from real data
**Gate:** at least one preset meets §31 latency/quality hypotheses; model licenses cleared by legal review (R-13). *This decides D-01 and D-03.*

### M3 — Core local dictation (Phase 1, large)
Tray app + settings skeleton, global hotkeys (push-to-talk + toggle + stuck-key timeout), mic selection + level meter, WASAPI capture + Silero VAD, inference service with ASR + supervision/restart, status overlay (listening/processing/inserted/error, DPI-aware, screen-reader announced), production insertion engine from POC-1 learnings, **literal mode**, undo (FR-029), password-field blocking (FR-030).
**Gate:** reliable end-to-end literal dictation in green-matrix apps, offline.

### M4 — AI layer + translation (Phase 2, medium-large)
Cleanup pipeline (punctuation, filler removal with toggle, spoken self-corrections, paragraphs/lists), personal dictionary wired into ASR post-processing, es↔en translation with editable preview (default-on for translation), per-app mode heuristics (terminal/IDE → literal default, FR-025), language auto-detect + manual override.
**Gate:** clean mode + translation at beta quality; trap-case suite green (FR-016).

### M5 — Privacy, models, updates (Phase 3, medium)
Onboarding flow (permissions explanation, model download with hash verification, hotkey setup, test dictation), private mode (zero retention, verified by test that watches for disk writes), delete-all, encrypted storage, model manager UI (download/update/remove per preset), signed auto-updates via Velopack, opt-in content-free error reporting.
**Gate:** PRD §38 privacy criteria pass an internal audit; install → dictate works on a clean Win11 VM without admin.

### M6 — Hardening + accessibility (medium)
Full keyboard navigation, screen-reader pass (Narrator + NVDA), high contrast, multi-monitor/DPI overlay testing, stress tests (long dictations, service kill/restart), Bluetooth mic profile warning (R-15), full insertion regression suite in CI.
**Gate:** NFR-17 accessibility criteria; crash rate targets in internal use.

### M7 — Closed beta (Phase 4)
20–50 users incl. ≥2 accessibility users; opt-in telemetry (content-free), structured feedback; iterate compatibility matrix and §31 metrics.
**Gate:** PRD §38 exit criteria → **M8: signed public MVP release**.

## 4. Key risks this plan front-loads

1. **Insertion reliability (R-01)** — M1 is first deliberate work; everything else is hostage to it.
2. **Local model quality/latency (R-02/03/04)** — M2 before any product ML code; presets come from measured data only.
3. **Model licensing (R-13)** — legal review is an explicit M2 gate, before models are baked in.
4. **Code signing / SmartScreen (R-10)** — cert acquisition starts at M0 because reputation takes time.
5. **Elevated apps (R-05, D-05)** — MVP position: *document the limitation* (no elevated helper component); revisit post-beta.

## 5. Deferred (per PRD MoSCoW)

Hands-free continuous mode, voice snippets, style profiles → v1.1. Additional languages, dictionary sync, Rust core extraction (Variant C) → v2, only if profiling justifies it.
