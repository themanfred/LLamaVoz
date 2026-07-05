# LLamaVoz — Reporte de Evals

Generado: 2026-07-05 12:01 · Máquina: THOMAS_FREUND, 12 núcleos lógicos · Modelos: ggml-base.bin, ggml-tiny.bin

> **Nota de honestidad:** el audio de prueba es TTS sintético de Windows (voces Sabina/Zira). Las cifras de WER reflejan ese audio, no voz humana real de campo cercano. Los umbrales son hipótesis iniciales del PRD §31, ajustables con datos de beta.

## Resumen

| Categoría | Pass | Fail | Skip | Total |
|---|---|---|---|---|
| unit | 1 | 0 | 0 | 1 |
| asr | 12 | 0 | 0 | 12 |
| perf | 5 | 0 | 0 | 5 |
| streaming | 3 | 0 | 0 | 3 |
| insertion | 1 | 0 | 4 | 5 |
| pipeline | 0 | 0 | 1 | 1 |
| **total** | **22** | **0** | **5** | **27** |

**RESULTADO GLOBAL: ✅ PASS**

Duración total: 1.7 min

## Detalle — unit

| Caso | Estado | Métricas | Duración |
|---|---|---|---|
| unit-suite | ✅ PASS | superados: 53<br>fallidos: 0 | 7.6s |

## Detalle — asr

| Caso | Estado | Métricas | Duración |
|---|---|---|---|
| asr-es-base-forced | ✅ PASS | WER: 8.7%<br>umbral: 15%<br>confianza: 0.80<br>audio: 8.2 s | 2.7s |
| asr-es-tiny-forced | ✅ PASS | WER: 8.7%<br>umbral: 25%<br>confianza: 0.82<br>audio: 8.2 s | 1.1s |
| asr-en-base-forced | ✅ PASS | WER: 0.0%<br>umbral: 15%<br>confianza: 0.89<br>audio: 7.9 s | 2.1s |
| asr-en-tiny-forced | ✅ PASS | WER: 4.8%<br>umbral: 25%<br>confianza: 0.88<br>audio: 7.9 s | 0.8s |
| asr-numbers-es | ✅ PASS | WER: 12.0%<br>umbral: 35%<br>confianza: 0.79<br>audio: 13.9 s | 3.3s |
| asr-propernouns-en | ✅ PASS | WER: 5.3%<br>umbral: 25%<br>confianza: 0.91<br>audio: 7.3 s | 1.7s |
| asr-paragraph-es | ✅ PASS | WER: 2.6%<br>umbral: 15%<br>confianza: 0.91<br>audio: 29.3 s | 10.8s |
| asr-es-base-auto | ✅ PASS | idioma detectado: es<br>WER: 8.7%<br>confianza: 0.80 | 5.5s |
| asr-en-base-auto | ✅ PASS | idioma detectado: en<br>WER: 0.0%<br>confianza: 0.89 | 5.4s |
| asr-silence | ✅ PASS | salida: (vacía) | 0.0s |
| asr-short-clip | ✅ PASS | salida: "sí"<br>confianza: 0.40 | 0.9s |
| asr-lang-mismatch | ✅ PASS | salida: "Hello, this is a local dictation test. Tomorrow I have a meeting at 3 in the afternoon with the development team."<br>confianza: 0.80<br>marcada: False | 2.0s |

## Detalle — perf

| Caso | Estado | Métricas | Duración |
|---|---|---|---|
| perf-load-tiny | ✅ PASS | carga: 185 ms<br>umbral: 2 s | 0.2s |
| perf-load-base | ✅ PASS | carga: 316 ms<br>umbral: 3 s | 0.3s |
| perf-rtf-tiny | ✅ PASS | RTF: 0.10x<br>umbral: 0.5x | 1.7s |
| perf-rtf-base | ✅ PASS | RTF: 0.25x<br>umbral: 1.0x | 4.1s |
| perf-tail-latency | ✅ PASS | cola tras soltar: 0.32 s<br>umbral: 2.5 s<br>segmentos: 2 | 10.1s |

## Detalle — streaming

| Caso | Estado | Métricas | Duración |
|---|---|---|---|
| stream-vad-segmentation | ✅ PASS | segmentos: 5<br>WER combinado: 10.9% | 5.7s |
| stream-tier-routing | ✅ PASS | segmentos: 8<br>verificados con base: 6<br>aceptados como draft: 2 | 13.4s |
| stream-auto-lock | ✅ PASS | idioma sesión: es<br>idioma bloqueado: es | 9.0s |

## Detalle — insertion

| Caso | Estado | Métricas | Duración |
|---|---|---|---|
| ins-uia-tier1 | ○ SKIP | — | 2.3s |
| ins-keyboard-tier2 | ○ SKIP | — | 2.1s |
| ins-clipboard-tier3 | ○ SKIP | — | 2.2s |
| ins-window-change-block | ✅ PASS | bloqueado: True<br>texto en ventana objetivo: (nada) | 0.5s |
| ins-password-block | ○ SKIP | — | 2.1s |

## Detalle — pipeline

| Caso | Estado | Métricas | Duración |
|---|---|---|---|
| e2e-dictation-es | ○ SKIP | — | 4.4s |

## ○ Omitidos

| Caso | Motivo |
|---|---|
| ins-uia-tier1 | Escritorio en uso: la ventana de prueba no pudo tomar el foco. |
| ins-keyboard-tier2 | Escritorio en uso: la ventana de prueba no pudo tomar el foco. |
| ins-clipboard-tier3 | Escritorio en uso: la ventana de prueba no pudo tomar el foco. |
| ins-password-block | Escritorio en uso: la ventana de prueba no pudo tomar el foco. |
| e2e-dictation-es | Escritorio en uso: no se pudo enfocar la ventana de prueba. |

_Los casos omitidos requieren el escritorio libre: vuelve a ejecutar `RunEvals.cmd` sin tocar teclado/ratón durante la fase de inserción._
