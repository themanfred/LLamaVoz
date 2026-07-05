# Whisper models

LLamaVoz runs speech recognition 100% locally with [whisper.cpp](https://github.com/ggerganov/whisper.cpp) GGML models. They are too large to commit, so download them into this folder:

| File | Size | Role |
|---|---|---|
| `ggml-base.bin` | ~142 MB | **Verify tier** — quality model (required) |
| `ggml-tiny.bin` | ~75 MB | **Draft tier** — fast model for "Rápido"/"Equilibrado" modes (recommended) |

## PowerShell

```powershell
Invoke-WebRequest https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin -OutFile models/ggml-base.bin
Invoke-WebRequest https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin -OutFile models/ggml-tiny.bin
```

## curl

```bash
curl -L -o models/ggml-base.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin
curl -L -o models/ggml-tiny.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin
```

With only one model present the app still works: quality modes that need the missing tier gracefully fall back to the available one. Set the `LLAMAVOZ_MODEL` environment variable to pin a specific model file for both tiers.

Models are OpenAI Whisper weights converted to GGML (MIT-licensed conversion; Whisper weights are MIT).
