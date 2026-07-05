# LLamaVoz — Resultados de POC

> Registro honesto de lo medido hasta ahora. Hardware: máquina de Thomas (12 núcleos lógicos, sin GPU dedicada verificada). Fecha: 2026-07-04.

## POC-1 — Inserción escalonada (PRD §34, riesgo R-01)

**Harness:** `poc/LLamaVoz.Poc.Insertion` (modos `selftest` / `countdown` / `inspect`).

**Resultados del selftest (5/5 ✔):** contra una ventana WinForms propia con TextBox:

| Escenario | Resultado |
|---|---|
| UIA headless (SetValue directo, sin foco) | ✔ texto exacto, incl. emoji/acentos |
| Tier 1 — UIA ValuePattern (control vacío) | ✔ |
| Tier 2 — SendInput Unicode (35 chars, con par sustituto 🦙) | ✔ |
| Tier 3 — Portapapeles con respaldo/restauración | ✔ texto insertado y centinela restaurado |
| FR-028 — bloqueo por cambio de ventana | ✔ bloqueado, 0 caracteres escritos |

**Hallazgo real adicional:** en la primera ejecución, con VS Code activo, Windows negó el foco a la ventana de prueba y el motor **rechazó insertar** (FR-028) en vez de escribir en la app del usuario. El invariante "nunca en la ventana equivocada" funcionó en condiciones reales.

**Limitaciones conocidas (pendientes):**
- Tier 1 solo actúa sobre controles vacíos (SetValue reemplaza todo el valor; con contenido existente cae a Tier 2). Inserción UIA en posición de cursor con contenido existente: pendiente.
- Deshacer (FR-029): no implementado aún.
- Respaldo de portapapeles no textual (imágenes, archivos): se avisa, no se restaura (limitación FR-027 documentada).
- **Matriz §27 de apps reales: sin datos aún.** Probar con `countdown` contra Word, Chrome, Notion, Terminal, etc.

## POC-2 — ASR local (PRD §34, riesgo R-02)

**Harness:** `poc/LLamaVoz.Poc.Asr` (Whisper.net 1.5.0 / whisper.cpp, CPU, 12 hilos).
**Audio de prueba:** TTS de Windows (Sabina es-MX, Zira en-US), 16 kHz mono, ~8 s por frase. *Nota: audio sintético, no habla real de campo cercano — los WER reales se medirán con voz humana.*

| Modelo | Tamaño | Carga | RTF es | RTF en | Calidad observada |
|---|---|---|---|---|---|
| ggml-tiny | 74 MB | ~170 ms | **0.28x** | **0.46x** | 1 error es ("dedicado" por "de dictado", artefacto del TTS); en perfecto |
| ggml-base | 141 MB | ~300 ms | 0.78x | 0.59x | Mismo error es; en perfecto; mejor capitalización |

Detección de idioma: correcta (es/en) en ambos modelos. RTF = tiempo de proceso / duración del audio (menor es mejor).

**Conclusión clave:** procesar el audio *al final* del dictado no cumple NFR-02 (≤1.5 s a texto final): con base, 8 s de audio ⇒ ~5–6 s de espera. **La arquitectura debe transcribir en streaming durante el habla** (segmentos por VAD), de modo que al soltar la tecla solo quede el último segmento por procesar (~1–2 s de audio ⇒ <1 s con tiny, ~1.5 s con base). Esto también habilita FR-011 (parciales en vivo).

**Presets tentativos (a confirmar con voz real y POC-5 de aceleración):**
- **Rápido:** tiny (RTF ~0.3x) — margen holgado para streaming en CPU.
- **Equilibrado:** base — viable con streaming; evaluar variantes cuantizadas (q5_1) y small.
- **Máxima calidad:** small/medium — probablemente requiera aceleración (POC-5).

## Pendientes de M2

- POC-3: LLM local de limpieza (llama.cpp/LLamaSharp) con set trampa de "no inventar".
  Nota (2026-07-05): evaluar la decodificación especulativa de llama.cpp (`--model-draft`,
  draft pequeño + modelo de limpieza como verificador) — aplicación directa del paper
  DSpark/DeepSeek. Las ideas draft→verify y verificación por confianza ya se aplicaron
  al ASR (`StreamingTranscriptionSession`, modo "Equilibrado": tiny borrador + base
  verificador solo en segmentos de baja confianza).
- POC-4: traducción es↔en.
- POC-5: aceleración (CPU vs DirectML/CUDA con Whisper.net.Runtime.* variantes).
- WER con voz humana real (es/en) y micrófonos reales (FR-004: enumeración ya implementada en `LLamaVoz.Audio`).
