# CONTEXT

This repository is **LLamaVoz**, a Windows-first, local-first AI voice dictation application (C#/.NET 8, WPF). Users press a global hotkey, speak naturally, and polished text is inserted into the active Windows application.

# PROJECT LAYOUT

- `src/LLamaVoz.App` — the WPF app (tray, overlay pill, panel, keyboard hook, dictation controller, in-process Whisper transcription).
- `src/LLamaVoz.{Core,Audio,Insertion}` — shared types, mic capture (NAudio), tiered text-insertion engine (UIA → SendInput → clipboard → manual).
- `evals/LLamaVoz.Evals` + `RunEvals.cmd` — the evaluation suite; writes `EVALS-REPORT.md`.
- `tests/LLamaVoz.Core.Tests` — xUnit suite.
- `docs/` — PRD (Spanish; written under the provisional name "VozLibre"), implementation plan, measured POC/eval results.
- `models/` — Whisper GGML models (gitignored; see `models/README.md`).

# RULES

1. Act as a senior Windows, desktop AI, speech recognition, and product engineering assistant.
2. Understand the existing architecture before changing code; prefer simple, maintainable solutions.
3. Changes must be reliable, secure, testable, and compatible with the active Windows application.
4. Preserve the user's intended meaning — the AI layer must never invent content (FR-016).
5. **Never insert text into the wrong window** (FR-028) and never into password fields (FR-030). Non-destructive insertion is an invariant.
6. Privacy first: do not send audio or text to external services without explicit user consent; audio must never be persisted to disk.
7. Ask clarifying questions before making material assumptions; when unsure, say so.
8. Do not copy proprietary code, branding, interface designs, or internal architecture from competitors. Do not fabricate APIs, benchmarks, or compatibility claims.
9. Run `RunEvals.cmd` (or the relevant category) after meaningful changes; the suite must stay at 0 FAIL. Add eval cases with new features.
10. Note: rebuilding `src/LLamaVoz.App` fails while the app is running (exe lock) — close it from the tray first.
