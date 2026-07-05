<p align="center">
  <img src="src/LLamaVoz.App/Assets/logo.png" alt="LLamaVoz logo" width="140"/>
</p>

<h1 align="center">LLamaVoz 🦙</h1>

<p align="center">
  <b>Local-first AI voice dictation for Windows.</b><br/>
  Hold a hotkey, speak naturally, and get clean text inserted right where your cursor is — in any app.<br/>
  100% on-device. Your voice never leaves your computer.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2011-blue"/>
  <img alt=".NET" src="https://img.shields.io/badge/.NET-8.0-512BD4"/>
  <img alt="License" src="https://img.shields.io/badge/license-MIT-green"/>
  <img alt="Evals" src="https://img.shields.io/badge/evals-22%20pass%20%2F%200%20fail-brightgreen"/>
</p>

---

## What it does

1. Put your cursor in any text field — email, chat, editor, browser.
2. **Hold `Ctrl+Alt`** (push-to-talk) or **tap `Win+Alt`** (toggle) and speak. A floating pill shows live mic levels.
3. Release (or tap again). Your speech is transcribed **locally with Whisper** while you talk (streaming), and the text is inserted at your cursor.
4. `Esc` cancels at any moment and the audio is discarded.

Spanish and English are detected automatically (or lock a language from the panel/tray). A dashboard window shows live state and word counters.

## Features

- **Two activation modes**, both with configurable modifier combos: hold-to-talk and tap-to-toggle.
- **Streaming ASR**: audio is segmented by a voice-activity detector and transcribed *while you speak*, so the wait after releasing the key is typically around a second.
- **Speculative transcription** (draft→verify): a tiny model drafts each segment and the base model re-checks only low-confidence ones — pick Fast / Balanced / Accurate in the panel.
- **Tiered text insertion** that never destroys your content: UI Automation → simulated Unicode typing → clipboard paste with backup/restore → manual fallback. It refuses to insert if you switched windows mid-dictation, and refuses password fields.
- **Privacy by design**: audio exists only in memory and is never written to disk; no network calls at runtime; the stats file stores numbers only, never your text; transcripts shown in the panel live in memory only.
- **Anti-hallucination guards**: silence produces no text, non-speech annotations (`[Música]`…) are dropped, foreign-script hallucinations are flagged instead of trusted.
- Tray-resident, with an always-available mini-pill above the taskbar (click the mic to dictate, click the chip to switch language).

## Requirements

- Windows 11 (Windows 10 untested)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A microphone
- ~250 MB disk for the Whisper models

## Getting started

```powershell
git clone https://github.com/<you>/llamavoz.git
cd llamavoz

# Download the local speech models (see models/README.md for details)
Invoke-WebRequest https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin -OutFile models/ggml-base.bin
Invoke-WebRequest https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin -OutFile models/ggml-tiny.bin

# Build & run
dotnet run --project src/LLamaVoz.App
```

Or double-click **`LLamaVoz.cmd`**, which builds on first run and starts the app in the background. The app lives in the system tray: double-click the tray icon for the panel; right-click → *Salir* to quit.

Default hotkeys: `Ctrl+Alt` (hold) and `Win+Alt` (tap). Change them in the panel under **ATAJOS**.

## How it's tested

The repo ships a full **evaluation suite** that exercises the real product code — not mocks — and writes an honest report ([EVALS-REPORT.md](EVALS-REPORT.md)) including failures and skipped cases:

```powershell
.\RunEvals.cmd            # everything
.\RunEvals.cmd asr perf   # subsets: unit | asr | perf | streaming | insertion | pipeline
```

| Category | What it verifies |
|---|---|
| `unit` | 51 xUnit tests: settings normalization/migration, hotkey parsing, stats rollover, anti-hallucination filters, the WER metric itself |
| `asr` | Word Error Rate vs reference for es/en × tiny/base × forced/auto language; numbers, proper nouns, a 30 s paragraph; pure silence must produce **no** text; wrong-language forcing must be flagged, not invented |
| `perf` | Model load times, real-time factor per model, tail latency after key release (target ≤ 2.5 s) |
| `streaming` | VAD segmentation at pauses, draft→verify routing counters, language locking in AUTO |
| `insertion` | All insertion tiers against a window owned by the test itself, clipboard restore verified with a sentinel, wrong-window blocking, password-field blocking |
| `pipeline` | End-to-end: WAV → streaming session → insertion engine → text verified inside a real TextBox |

Test audio is generated with Windows TTS (synthetic — WER figures are labeled as such; real-voice numbers await beta). Cases that need desktop focus **skip instead of failing** when you're using the machine, and the report says why.

Latest full run: **22 pass · 0 fail · 5 skipped** (focus-dependent cases; they pass on an idle desktop). The suite has already paid for itself: it caught a silence-hallucination bug and a tail-latency regression, and validated the `audio_ctx` optimization that made the engine ~3× faster.

## Architecture

```
┌────────────────────────── LLamaVoz.App (WPF) ──────────────────────────┐
│  Tray icon · Overlay pill · Dashboard panel · Low-level keyboard hook  │
│  DictationController: Standby → Listening → Processing state machine  │
└──────┬──────────────────────────┬──────────────────────────┬──────────┘
       │                          │                          │
  LLamaVoz.Audio            StreamingTranscriptionSession    LLamaVoz.Insertion
  (NAudio mic capture,      (energy VAD segmentation,        (UIA → SendInput →
   16 kHz mono chunks)       draft→verify Whisper tiers,      clipboard → manual;
                             in-process Whisper.net)          FR-028/FR-030 guards)
```

Projects: `src/LLamaVoz.Core` (shared types) · `Audio` · `Insertion` · `App` · `evals/LLamaVoz.Evals` (eval runner) · `tests/` (xUnit) · `poc/` (measurement harnesses). `Inference`, `Ipc`, `Storage` are stubs for the planned out-of-process inference service.

Design docs live in [docs/](docs/): the full [PRD](docs/PRD.md) (Spanish), the [implementation plan](docs/IMPLEMENTATION_PLAN.md), and measured [POC](docs/POC-RESULTS.md)/[eval](docs/EVAL-RESULTS.md) results.

## Known limitations

Honest list — see the PRD for the full compatibility discussion:

- **Elevated (admin) apps**: Windows UIPI blocks input injection into elevated windows unless LLamaVoz also runs elevated.
- **Canvas editors** (e.g. Google Docs) don't expose text via UI Automation; insertion falls back to typing/clipboard with lower reliability.
- **Undo of an insertion** (beyond the target app's own Ctrl+Z) is not implemented yet.
- Spanish/English are the tuned languages (fr/de selectable but unvalidated).
- Inference runs in-process; the crash-isolated inference service from the plan is future work.
- No signed installer yet — you build from source.

## Roadmap

- Local LLM cleanup layer (filler removal, punctuation, lists) with strict no-invention guardrails
- Local es↔en translation with preview
- Insertion undo, personal dictionary, signed installer + auto-update
- Out-of-process inference service with crash recovery

## Contributing

Issues and PRs welcome. Please run `RunEvals.cmd` before submitting — the suite must stay at 0 FAIL, and new features should come with eval cases. Privacy is non-negotiable: no feature may send audio or text off-device without explicit opt-in consent.

## License

[MIT](LICENSE) © 2026 Thomas Freund Paternostro

---

## 🇪🇸 Resumen en español

**LLamaVoz** es dictado por voz con IA, 100 % local, para Windows. Mantén `Ctrl+Alt` (o toca `Win+Alt`), habla con naturalidad, y el texto transcrito aparece donde está tu cursor — en cualquier aplicación. Tu voz nunca sale de tu computadora: la transcripción usa Whisper en tu propio equipo, el audio solo existe en memoria y no hay llamadas de red.

**Para empezar**: clona el repo, descarga los dos modelos Whisper a `models/` (comandos arriba o en [models/README.md](models/README.md)) y ejecuta `LLamaVoz.cmd`. La app vive en la bandeja del sistema; el panel muestra tu contador de palabras, el último dictado y la configuración de idioma, calidad y atajos.

**Cómo está probado**: `RunEvals.cmd` ejecuta la suite completa de evaluación (51 tests unitarios + ~27 casos de ASR, rendimiento, streaming, inserción y pipeline extremo a extremo) y genera [EVALS-REPORT.md](EVALS-REPORT.md) con los resultados, incluyendo fallos y casos omitidos con su motivo. Última corrida completa: **22 ✅ · 0 ❌ · 5 omitidos** (casos que requieren el escritorio libre).

La documentación de diseño (PRD completo en español, plan de implementación y resultados medidos) está en [docs/](docs/).
