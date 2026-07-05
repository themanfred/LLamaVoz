# LLamaVoz — Informe de evaluación

Fecha: 2026-07-05 11:08 · Máquina: THOMAS_FREUND · CPU lógicas: 12

> Audio generado con las voces TTS locales de Windows (System.Speech): más limpio que un micrófono real, así que el WER es un límite inferior. Sirve para comparar niveles y modos, no como verdad absoluta.

## Voces TTS disponibles

- Microsoft Sabina Desktop (es-MX)
- Microsoft Zira Desktop (en-US)
- Microsoft David Desktop (en-US)

## 1. Comparación de niveles por caso (audio de una pieza)

Draft = `ggml-tiny.bin` (greedy) · Verify = `ggml-base.bin` (beam 5)

| Caso | Idioma | Nivel | WER | Conf. | Latencia | Audio | Detectado | Salida |
|---|---|---|---|---|---|---|---|---|
| es-simple | es | Draft | 0% | 0.87 | 1545 ms | 3.7 s | es | Esto se ve mucho mejor, no hay ni comparación. |
| es-simple | es | Verify | 0% | 0.87 | 5801 ms | 3.7 s | es | Esto se ve mucho mejor, no hay ni comparación. |
| es-numeros | es | Draft | 28% | 0.83 | 2564 ms | 5.8 s | es | La reunión es el 14 de marzo a las 13 y media y costará 2500 euros. |
| es-numeros | es | Verify | 28% | 0.88 | 6248 ms | 5.8 s | es | La reunión es el 14 de marzo a las 3 y media y costará 2.500 euros. |
| es-nombres | es | Draft | 17% | 0.77 | 2441 ms | 4.0 s | es | Mi amigo Joaquina Caba de tener un hijo que se llama Antonio. |
| es-nombres | es | Verify | 0% | 0.88 | 5982 ms | 4.0 s | es | Mi amigo Joaquín acaba de tener un hijo que se llama Antonio. |
| es-coloquial | es | Draft | 0% | 0.89 | 2582 ms | 6.2 s | es | Ahora mismo estoy hablando, voy a ver si esto tiene sentido o realmente está equivocándose… |
| es-coloquial | es | Verify | 0% | 0.90 | 6167 ms | 6.2 s | es | Ahora mismo estoy hablando, voy a ver si esto tiene sentido o realmente está equivocándose… |
| en-simple | en | Draft | 0% | 0.89 | 2450 ms | 3.8 s | en | I am looking at a beautiful skyline and the sun is shining. |
| en-simple | en | Verify | 0% | 0.91 | 5854 ms | 3.8 s | en | I am looking at a beautiful skyline and the sun is shining. |
| en-names | en | Draft | 17% | 0.76 | 2499 ms | 5.0 s | en | My friend Kurt wants a formal email can gratulating him for his achievements. |
| en-names | en | Verify | 0% | 0.88 | 5898 ms | 5.0 s | en | My friend Kurt wants a formal email congratulating him for his achievements. |
| auto-es | auto | Draft | 0% | 0.85 | 4736 ms | 4.9 s | es | Quiero comprobar que la detección automática elige español y no lo mezcla. |
| auto-es | auto | Verify | 0% | 0.88 | 11311 ms | 4.9 s | es | Quiero comprobar que la detección automática elige español y no lo mezcla. |
| auto-en | auto | Draft | 0% | 0.88 | 4683 ms | 4.1 s | en | Let us check that automatic detection picks English and keeps it. |
| auto-en | auto | Verify | 0% | 0.84 | 10159 ms | 4.1 s | en | Let us check that Automatic Detection picks English and keeps it. |

## 2. Sesión de streaming (VAD + modos de calidad)

Dos frases con una pausa de 0,9 s entre ellas, entregadas en chunks de 50 ms como el micrófono real. La latencia mostrada es el total de FinishAsync sin tiempo real de habla (peor caso; en uso real los segmentos previos ya están transcritos al soltar la tecla).

| Escenario | Modo | WER | Latencia total | Segmentos | Verificados | Idioma | Salida |
|---|---|---|---|---|---|---|---|
| es-stream | accurate | 0% | 10400 ms | 2 | 0 | es | Primero digo una frase completa con contenido claro. Después de una pausa digo la segunda … |
| es-stream | balanced | 0% | 9651 ms | 2 | 1 | es | Primero digo una frase completa con contenido claro. Después de una pausa digo la segunda … |
| es-stream | fast | 0% | 4874 ms | 2 | 0 | es | Primero digo una frase completa con contenido claro. Después de una pausa digo la segunda … |
| en-stream | accurate | 0% | 22878 ms | 2 | 0 | en | First I say one complete sentence with clear content. After a pause I say the second part … |
| en-stream | balanced | 0% | 20985 ms | 2 | 1 | en | First I say one complete sentence with clear content. After a pause I say the second part … |
| en-stream | fast | 0% | 8233 ms | 2 | 0 | en | First I say one complete sentence with clear content. After a pause I say the second part … |

## 3. Filtros anti-alucinación (comprobaciones unitarias)

| Comprobación | Esperado | Resultado |
|---|---|---|
| anotación [Música] se descarta | `True` | ✅ |
| anotación (Aplausos) se descarta | `True` | ✅ |
| anotación ♪♪ se descarta | `True` | ✅ |
| frase normal NO es anotación | `False` | ✅ |
| carácter CJK suelto se elimina | `lots of chicken  wings` | ✅ |
| texto latino queda intacto | `todo normal aquí` | ✅ |
| segmento mayoritariamente CJK detectado | `True` | ✅ |
| préstamo latino ('software okay') pasa | `False` | ✅ |

## Resumen

- Comprobaciones de filtros: 8/8 correctas.
- Ver tablas 1 y 2 para WER/latencia por nivel y modo.

## Análisis

1. **Verify (base + beam 5) corrige exactamente los fallos reportados con voz real.** En los dos
   casos con nombres propios ("Joaquín… Antonio", "Kurt… congratulating"), Draft produjo errores
   (17% WER: "Joaquina Caba", "can gratulating") y Verify los resolvió al 0%. El beam search en el
   nivel de calidad está justificado.
2. **El 28% de "error" en `es-numeros` es normalización, no error.** Se dijo "catorce" y Whisper
   escribió "14" — semánticamente correcto; el WER lo penaliza. El único fallo real fue del Draft
   ("a las **13** y media" en vez de "las tres y media"), que Verify corrigió.
3. **La confianza de tiny discrimina bien y quedó calibrada.** Borradores correctos puntuaron
   ≥0.85; borradores con errores reales, 0.76–0.83. El umbral de aceptación pasó de 0.92 a **0.85**
   con estos datos: en esta pasada, el modo Equilibrado re-verificó solo 1 de 2 segmentos y pasó
   de ser el modo más lento (verificaba todo con el umbral 0.92) a ser más rápido que Preciso con
   el mismo WER (0%).
4. **El streaming no pierde palabras en las uniones.** Con pausa de 0,9 s entre frases, los tres
   modos reconstruyeron el texto completo (0% WER) y AUTO mantuvo un solo idioma por sesión.
5. **La detección automática es cara**: los casos `auto-*` tardaron ~2× frente al idioma fijo
   (p. ej. auto-es Verify 11,3 s vs es-simple Verify 5,8 s). Fijar ES/EN desde la píldora no es
   solo un tema de precisión: también reduce la latencia a la mitad.
6. **Límites de esta eval**: voz TTS limpia (el WER con micrófono real será mayor), solo 2 voces
   (es-MX, en-US), y las latencias son de pasada única sin el solapamiento del streaming real
   (al dictar en vivo, los segmentos previos ya están transcritos al soltar la tecla).

**Recomendaciones siguientes:** probar `ggml-small` como nivel de máxima calidad (los errores
restantes son de capacidad del modelo), añadir vocabulario personal vía initial prompt para
nombres, y repetir esta eval con grabaciones reales de Thomas como referencia.

_Generado por `poc/LLamaVoz.Poc.Eval` (+ análisis manual) — totalmente local._
