# RendezvousNET

Utilidades para video VR (180°, 360°, ojo de pez, estéreo) escritas en **C# con .NET 10**.

| Proyecto | Plataforma | Para qué sirve |
|---|---|---|
| [**VrView**](VrView/README.md) | Android (`net10.0-android`) | Reproductor de video VR para usar el móvil en un visor tipo Cardboard. Reproduce carpetas enteras como lista, con aleatorio y repetición, y marca cortes en modo edición. |
| [**VrBrowser**](VrView/src/VrBrowser/README.md) | Android (`net10.0-android`) | Navegador para el visor: la página flota dentro del VR, el video de la página se ve en pantalla completa en VR y los videos detectados se mandan al reproductor de VrView. |
| [**VrCut**](VrCut/README.md) | Mac y Windows (Avalonia) | Editor para quitar partes de videos MP4 **sin perder calidad** (`ffmpeg -c copy`), con archivos locales o directamente en el teléfono vía USB (adb). |

VrView y VrCut trabajan juntas: en el visor (VrView, **modo edición**) marcas las partes que
sobran, y en la computadora VrCut las corta sin perder calidad y **mueve las marcas de favoritos**
para que sigan en la misma escena (una marca en 8:00, con 0:00–3:00 cortado, queda en 5:00). Todo
viaja en el `vrview.json` que VrView guarda junto a los videos.

## VrView — reproductor VR para Android

- **Carpetas como lista de reproducción**: orden natural, subcarpetas, aleatorio y repetir
  (no / todo / uno).
- **Formatos**: plano, 180°, 360°, ojo de pez, cubemap EAC (YouTube) y 3×2, malla VR180;
  mono, lado a lado (SBS), arriba-abajo (TB) y ojos invertidos.
- **Detección automática** del formato: metadatos esféricos del MP4 (`st3d`/`sv3d`), etiquetas
  en el nombre (`_180_LR`, `_360_TB`, `fisheye190`…) o la proporción del cuadro.
- **Editor de formato y perfiles** por video, carpeta o perfil con nombre, guardados en
  `vrview.json` junto a los videos.
- **Seguimiento sin deriva**: el giroscopio se corrige despacio con la brújula; calibración del
  error en reposo.
- **Biblioteca, marcas, horizonte, audio y subtítulos** desde el visor, sin quitárselo.
- **Modo edición** (interruptor aparte, para no mezclar con las marcas de favoritos): marca las
  partes a quitar, salta o muestra esas partes al reproducir y deja el corte a VrCut.
- **Videos web** que le manda VrBrowser, con las cookies del sitio.
- **Perfil del visor por código QR**, asistente de calibración de lentes y menú.
- Controles por mirada, toque, mando Bluetooth o teclado.

Sin Unity ni MAUI: solo el SDK de Android, OpenGL ES 2.0, `MediaPlayer` y ZXing.Net.
La lógica independiente de Android vive en `VrView.Core` y tiene pruebas xUnit.

Más detalles, controles y cómo compilar e instalar: [VrView/README.md](VrView/README.md).

## VrBrowser — la web dentro del visor

App aparte, hermana de VrView: navegador con sitios favoritos que detecta el video (MP4 o stream
HLS) de una página para verlo en VrView, guardarlo o descargarlo, y que con **🥽 En VR** pone la
página flotando dentro del visor, con los videos incrustados en pantalla completa en VR (cine,
180°, 360°, 2D o 3D). Usa la configuración del visor de VrView, así que se configura una sola vez.
Comparte con VrView el motor VR y `VrView.Core`.

Detalles: [VrView/src/VrBrowser/README.md](VrView/src/VrBrowser/README.md).

## VrCut — cortes sin pérdida

- Timeline con miniaturas, marcas de *keyframes* y ajuste automático a keyframe.
- Marca inicio/fin de cada parte a borrar, o quita la intro de un clic.
- Trabaja con **archivos locales o en un teléfono Android por USB**; el resultado vuelve a la
  misma carpeta.
- Pensado para VR: vista de un ojo en videos SBS, prefijo `cut_` para conservar las etiquetas
  del nombre y se intenta conservar la metadata esférica.
- **Lee los cortes y marcas del visor** (`vrview.json`) y al exportar reacomoda las marcas y la
  posición según lo que se cortó.
- Renombrar y borrar videos (a la papelera en la computadora).

Requisitos, uso y atajos: [VrCut/README.md](VrCut/README.md).

## Inicio rápido

Requisitos comunes: [SDK de .NET 10](https://dotnet.microsoft.com/download).

```bash
# VrCut (requiere ffmpeg; adb solo para el teléfono)
cd VrCut
dotnet run -c Release
```

```bash
# VrView: compilar, instalar y abrir en el teléfono conectado (requiere el workload de Android)
cd VrView/src/VrView
dotnet build -t:Run -f net10.0-android
```

```bash
# Pruebas de la lógica de VrView (en cualquier computadora)
cd VrView
dotnet test tests/VrView.Core.Tests
```

## Estructura

```
RendezvousNET/
├── VrView/                    # reproductor VR para Android
│   ├── src/VrView.Core/       # lógica pura (net10.0), testeable; VrCut también la usa
│   ├── src/VrView/            # la app Android
│   ├── src/VrBrowser/         # el navegador VR (app aparte, comparte el motor de VrView)
│   └── tests/VrView.Core.Tests/
└── VrCut/                     # editor de cortes sin pérdida (Avalonia, Mac/Windows)
```
