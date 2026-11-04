# VrView

> this should be my own VR viewer with folder playlist and suffle for android.

VrView es un reproductor de vídeo VR para Android pensado para usar el móvil dentro de un
visor tipo Cardboard. Reproduce **carpetas enteras como lista de reproducción**, con
**modo aleatorio** y repetición. Toma como modelo *VR Media Player* (Reproductor multimedia VR,
de eMage Systems).

Está escrito en **C# con .NET 10 para Android** (`net10.0-android`), con vistas nativas de
Android y OpenGL ES 2.0. No usa Unity ni MAUI: solo el SDK de Android, `MediaPlayer` del sistema y
ZXing.Net para leer el código QR del visor.

La app es gratis y viene completa, en inglés, español, francés y alemán: sigue el idioma del teléfono
(o el de la app, en Android 13 y posteriores). Los textos están en `src/VrView.Core/Translations.cs`.
Tiene un botón opcional para invitar una cerveza con Google Play. El material para publicarla está en
[`store/`](store/README.md).

## Funciones

| Función | Detalle |
|---|---|
| Lista por carpeta | Eliges una carpeta con el selector del sistema (no hace falta ningún permiso de almacenamiento) y se listan sus vídeos en orden natural (`clip2` va antes que `clip10`). Puedes incluir subcarpetas. Al terminar un vídeo empieza el siguiente. |
| Biblioteca en el visor | Ventana **Biblioteca**: carpetas usadas, miniaturas (se guardan en caché), formato, duración, avance y marcas de cada vídeo. Filtros (180°, 360°, plano, sin ver, con marcas), orden (nombre, avance, tamaño), «▶ Continuar», aleatorio y repetir. No hace falta quitarse el visor para cambiar de vídeo o de carpeta. |
| Aleatorio y repetición | Aleatorio sí/no. Repetir: no / todo / uno. «Anterior» vuelve por el orden real de reproducción, también en aleatorio. |
| Formatos | Plano, 180°, 360°, ojo de pez, **cubemap EAC** (YouTube) y cubemap 3×2, y la **malla incrustada** de las cámaras VR180. Mono, lado a lado (SBS/LR), arriba-abajo (TB/OU) y ojos invertidos. |
| Editor de formato | Ventana **Formato** con todo a la vista: proyección, ojos, cobertura horizontal y vertical, ángulo y tipo de lente, centro y tamaño del círculo (ojo de pez). Cada valor muestra si es **estándar o cambiado** (tocar el valor lo regresa al estándar); lo que no aplica se ve apagado con la razón. Vista del **archivo original** con guías. |
| ¿De dónde sale el formato? | El editor muestra la lista y cuál se usa: este video → perfil o formato de la carpeta (si el nombre no trae etiquetas) → metadatos del archivo → etiquetas del nombre → proporción del cuadro. |
| Perfiles | Un formato completo con nombre («Cámara 200°»). Una carpeta usa un perfil; si corriges el perfil, se corrigen todos sus videos. «Guardar en: solo este video / toda la carpeta / perfil». |
| Correcciones opcionales | **Tapa abajo (nadir)** para la zona «pellizcada» bajo la cámara y **horizonte del video**: interruptores **apagados por defecto**, por video o por perfil, nunca para todos sin que lo veas. |
| Metadatos VR | Lee los metadatos esféricos del MP4 (`st3d`, `sv3d`: equirectangular con cobertura, cubemap, malla VR180) y los usa como formato automático. |
| Anti-deriva | La imagen ya no se recorre de lado con el tiempo: el giroscopio se corrige muy despacio con la brújula (ignorando saltos magnéticos) y se resta el error del giroscopio medido en reposo. Modos: anti-deriva, solo giroscopio, brújula. Medidor de «grados por minuto» y botón **Calibrar**. |
| Marcas | **＋ Marca**, **◀ Marca / Marca ▶**, rayitas amarillas en la barra y ventana **Marcas** con miniaturas. Pueden tener **nombre** (botón «✎ Nombrar marca» en la pantalla táctil). |
| Vista previa en la barra | Al mirar la barra de progreso aparece una miniatura del momento al que saltarías. |
| Audio y subtítulos | Elegir pista de audio y subtítulos (los del video o un `.srt` con el mismo nombre al lado del video). Tamaño ajustable. |
| Información | Archivo, tamaño, resolución, fps, códec y bits, velocidad, **decodificador por hardware o software**, cuadros mostrados por segundo, metadatos VR, formato y su origen, horizonte, marcas, dónde se guarda y estado del sensor. |
| Visor | Pantalla partida, zoom, separación de lentes, distorsión (no / suave / fuerte / **perfil del visor por código QR**). **Asistente de calibración** en 3 pasos: lentes (cruces lejanas), menú (cruces cercanas) y distorsión (cuadrícula). |
| Controles | Tamaño, opacidad, separación del menú, clic con la mirada y su tiempo, botones ◀ ▶ a los lados (**fijos en la escena** o **siguiendo la cabeza**) y mirar abajo para abrir el menú. |
| Calidad | Alta (4 muestras por píxel y antialiasing 4×) o normal; malla normal o densa; mostrar fps. |
| Recordar | Última carpeta, último vídeo, posición, formato, horizonte y marcas de cada vídeo, en **`vrview.json` dentro de la carpeta** (sobrevive a reinstalar la app). |
| Sin visor | Vista «pantalla»: arrastra para mirar alrededor y pellizca para zoom. Las mismas ventanas aparecen en la pantalla táctil, con deslizadores que se arrastran con el dedo. |
| Abrir con… | Aparece en «Abrir con» de galerías y gestores de archivos. |

### Etiquetas reconocidas en el nombre del archivo

No distingue mayúsculas. Las etiquetas pueden ir separadas por `_`, `-`, `.`, espacios, etc.

| Etiqueta | Significado |
|---|---|
| `180`, `VR180`, `180x180` | 180° (media esfera) |
| `360`, `360x180` | 360° (esfera completa) |
| `fisheye`, `fisheye190`, `FE200`, `MKX200`, `RF52` | ojo de pez (el número es el campo de visión) |
| `LR`, `SBS`, `3DH`, `HSBS`, `side by side` | lado a lado (ojo izquierdo a la izquierda) |
| `RL` | lado a lado con los ojos invertidos |
| `TB`, `OU`, `3DV`, `TAB`, `over under` | arriba-abajo (ojo izquierdo arriba) |
| `BT` | arriba-abajo con los ojos invertidos |
| `mono`, `2D`, `flat` | sin estéreo / plano |
| combinadas: `180LR`, `360TB`, `180_3dh`… | |

Ejemplos: `playa_180_LR.mp4` → 180° SBS, `ciudad_360_TB.mp4` → 360° TB,
`peli.3D.HSBS.mkv` → plano SBS. Si solo pone `_180`, se deduce el estéreo por la forma del
fotograma (2:1 → SBS, 1:1 → mono). Sin etiquetas, un vídeo 2:1 exacto se trata como 360° mono y
el resto como plano.


## Controles

### Con el móvil en el visor

- **Tocar la pantalla** (o el botón/palanca del visor): abre la barra del reproductor. Con una
  ventana abierta, pulsa lo que señala el punto central; si no señala nada, la cierra.
- **Mirar hacia abajo** un momento abre la barra (se puede desactivar).
- **Clic mirando**: deja el punto sobre un botón (1,4 s por defecto) y se pulsa solo. ±10 s,
  volumen, horizonte y los **− / +** de los deslizadores se repiten, cada vez más rápido.
- **Deslizadores**: mira el punto de la barra donde quieres el valor y espera (o toca). La rayita
  gris marca el valor estándar.
- Mientras cambias un valor del formato, la ventana se vuelve semitransparente para ver el efecto.
- La barra del reproductor se oculta sola si dejas de mirarla; las demás ventanas no (para que
  puedas mirar el video mientras ajustas). Todas tienen **✕**.

**Ventanas**: Reproductor (barra), Formato, Marcas, Horizonte, Audio y subtítulos, Información,
Biblioteca, Ajustes (pestañas Visor, Controles, Seguimiento, Calidad, Datos) y el Asistente de
calibración.

### Mando Bluetooth, teclado, mandos de visor y botones multimedia

| Botón | Acción |
|---|---|
| A / OK / Enter | reproducir/pausa (con una ventana abierta: pulsar lo que miras) |
| Espacio / Play-Pausa / botón del auricular | reproducir/pausa |
| ← / → (cruceta) · Rebobinar / Avanzar | −10 s / +10 s |
| ↑ / ↓ (cruceta) | volumen + / − |
| L1 / R1 · Anterior / Siguiente · RePág / AvPág | vídeo anterior / siguiente |
| L2 / R2 | marca anterior / siguiente (sin marcas: −60 s / +60 s) |
| L3 / `K` | añadir marca |
| X / Start / Menú / `M` | abrir o cerrar la barra |
| B / Esc / Atrás | volver a la ventana anterior o cerrar (sin ventanas: volver a la lista) |
| Y / `R` | recentrar |
| Select / `F` | editor de formato |
| `H` · `I` · `L` | horizonte (la cruceta ajusta inclinación/nivel y L1/R1 el giro) · información · biblioteca |
| `S` | aleatorio sí/no |
| `V` | vista visor VR / pantalla |

### Fuera del visor (pantalla táctil)

- El botón **☰** muestra u oculta los controles táctiles (en vista «pantalla» basta tocar el vídeo).
- Arriba: lista, vista, Formato, Marcas, Horizonte, Audio y subt., Info, Biblioteca, Ajustes,
  «✎ Nombrar marca» y ocultar. Abajo: barra de progreso con las marcas y los botones de reproducción.
- Las ventanas son las mismas que en el visor; los deslizadores se arrastran con el dedo. En
  Ajustes → Datos, tocar «Usar en carpeta» también permite **renombrar** el perfil.
- **Perfil del visor**: en la pantalla de la lista, botón «Visor: escanear su código QR». Elige una
  foto del código impreso en el visor (o escribe su dirección); se configuran la distorsión, el
  campo de visión y la separación de lentes.

## El formato: dónde vive y cómo se decide

El editor guarda en el lugar que elijas abajo: **Solo este video**, **Toda la carpeta** (los videos
sin etiquetas en el nombre) o un **Perfil**. «Volver a automático» borra el formato de ese lugar.
Si el nombre del video trae etiquetas, la carpeta no se le aplica (lo dice el editor); al guardar
«toda la carpeta» desde un video así, se guarda también para él.

| Parámetro | Para qué | Aplica a |
|---|---|---|
| Cobertura horizontal | cuánto abarca de lado a lado (180° o 360° estándar) | 180°, 360° |
| Cobertura vertical | bájala si arriba o abajo se ve estirado o «pellizcado» | 180°, 360° |
| Ángulo y tipo de lente | equidistante / equisólida / estereográfica | ojo de pez |
| Centro y tamaño del círculo | cuando la cámara no graba el círculo centrado | ojo de pez |
| Tapa abajo | disco oscuro de 10°–90° bajo tus pies (apagada por defecto) | todos menos plano |

### `vrview.json` (versión 2)

Vive en la carpeta elegida, junto a los videos; es legible y se puede editar a mano. Un archivo de
la versión anterior se convierte solo.

```json
{
  "version": 2,
  "profiles": {
    "Cámara 200°": { "projection": "180", "eyes": "sbs", "coverage": [200, 180], "nadir": 30 }
  },
  "folders": { "": { "profile": "Cámara 200°" } },
  "videos": {
    "paseo_lago_180_LR.mp4": {
      "position": 768000,
      "format": { "projection": "180", "eyes": "sbs", "coverage": [180, 150] },
      "horizon": { "pitch": 2, "roll": -3.5, "yaw": 0 },
      "spots": [ 135000, { "at": 1214000, "name": "Muelle" } ]
    }
  }
}
```

`projection`: `flat`, `180`, `360`, `fisheye` (+ `fov`, `lens`, `center`, `radius`), `eac`,
`cubemap`, `mesh`. `eyes`: `mono`, `sbs`, `tb` (+ `"swap": true`). Tiempos en milisegundos, ángulos
en grados. Los ajustes generales (visor, controles, seguimiento) se guardan en la app.

Si elegiste la carpeta con una versión antigua (solo lectura), **vuelve a elegirla** para darle
permiso de escritura; los perfiles necesitan poder escribir `vrview.json`.

## La imagen que se recorre de lado (deriva)

El sensor «game rotation vector» combina giroscopio y acelerómetro, pero no brújula: la
inclinación siempre se corrige con la gravedad, pero el giro de lado no, y el pequeño error del
giroscopio se suma (medio grado por minuto son 15° en media hora). En **Ajustes → Seguimiento**:

- **Anti-deriva** (por defecto): se compara con el sensor que sí usa brújula y la diferencia se
  corrige muy despacio (no se nota). Si el campo magnético salta (bocina, mesa de metal) se ignora.
- **Solo giroscopio**: lo más suave; se resta el error medido con el teléfono quieto.
- **Brújula**: nunca se desvía, puede temblar cerca de metal.
- **Calibrar**: deja el teléfono quieto 5 s sobre la mesa para medir el error del giroscopio.
- El **medidor** muestra cuántos grados por minuto se desvía el giroscopio frente a la brújula.

## Ajustar el horizonte

Ventana **Horizonte** (o el interruptor «Horizonte de este video» del editor):

- **Fijar con la cabeza**: aparece una línea azul fija a tu vista; inclina la cabeza hasta que
  quede sobre el horizonte del vídeo y quédate quieto. Tocar la pantalla lo cancela.
- **Inclinar**, **Nivelar** y **Girar** a mano; **Restablecer** lo quita. Se guarda por video.
- **Recentrar** pone el frente del vídeo donde miras (también arriba/abajo). Con «Recentrar al
  empezar cada video» se hace solo de lado a lado al abrir cada video.

## Alinear lentes y menú

**Ajustes → Visor → Asistente de calibración**: 1) mueve «Separación de lentes» hasta que las dos
cruces lejanas (roja y verde, una por ojo) se vuelvan una; 2) «Separación del menú» hasta que se
junten las cruces cercanas; 3) elige la distorsión con la que la cuadrícula se ve recta.

## Compilar e instalar en el móvil desde un Mac

### 1. Requisitos (una sola vez)

```bash
# SDK de .NET 10: https://dotnet.microsoft.com/download (o: brew install --cask dotnet-sdk)
dotnet --version            # 10.0.x

# Workload de Android para .NET
sudo dotnet workload install android

# JDK 17 o posterior (por ejemplo el de Microsoft)
brew install --cask microsoft-openjdk@21

# SDK de Android (plataforma 36, build-tools, platform-tools con adb), sin Android Studio
cd VrView/src/VrView
dotnet build -t:InstallAndroidDependencies -f net10.0-android \
  -p:AndroidSdkDirectory="$HOME/Library/Android/sdk" \
  -p:AcceptAndroidSDKLicenses=True
export ANDROID_HOME="$HOME/Library/Android/sdk"
export PATH="$ANDROID_HOME/platform-tools:$PATH"   # para tener adb
```

Si ya tienes Android Studio, su SDK (`~/Library/Android/sdk`) sirve tal cual.

### 2. Preparar el teléfono

1. *Ajustes → Información del teléfono →* toca 7 veces **Número de compilación** para activar
   las opciones de desarrollador.
2. *Opciones de desarrollador →* activa **Depuración USB** (o **Depuración inalámbrica**).
3. Conecta el móvil por USB, acepta el aviso en el móvil y comprueba con `adb devices`.
   Sin cable: `adb pair IP:PUERTO` y después `adb connect IP:PUERTO` (lo indica la pantalla de
   depuración inalámbrica).

### 3. Instalar

Opción A, compilar, instalar y abrir en un paso (Debug):

```bash
cd VrView/src/VrView
dotnet build -t:Run -f net10.0-android
```

Opción B, generar el APK optimizado (Release, AOT y trimming) e instalarlo con adb:

```bash
cd VrView/src/VrView
dotnet publish -c Release
adb install -r bin/Release/net10.0-android/publish/net.rendezvous.vrview-Signed.apk
```

También puedes copiar ese `.apk` al móvil y abrirlo desde el gestor de archivos (tendrás que
permitir «instalar apps desconocidas»). El APK va firmado con la clave de depuración, que sirve
para uso personal. Para publicarlo habría que firmarlo con una clave propia
(`-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=...`).

Requiere Android 8.0 o superior y un móvil con giroscopio para el seguimiento de la cabeza.

### Pruebas

La lógica que no depende de Android está en `VrView.Core` y tiene pruebas que se ejecutan en
cualquier ordenador: lista y aleatorio, detección de formato, matemáticas de orientación,
mallas y menú.

```bash
cd VrView
dotnet test tests/VrView.Core.Tests
```

## Estructura

```
VrView/
├── VrView.slnx
├── src/
│   ├── VrView.Core/            # net10.0, sin Android: lógica pura y testeable
│   │   ├── Playlist.cs          # orden, aleatorio, repetición
│   │   ├── FormatDetector.cs    # etiquetas del nombre y proporciones → formato
│   │   ├── FormatResolver.cs    # video → perfil/carpeta → metadatos → etiquetas → proporción
│   │   ├── VideoFormat.cs       # proyección, estéreo, ajuste fino, recorte por ojo
│   │   ├── Params.cs            # rangos, pasos y valores estándar de cada deslizador
│   │   ├── FolderSettings.cs    # vrview.json v2 (perfiles, carpetas, videos) + FormatJson.cs
│   │   ├── VrMenu.cs            # elementos, ventanas, prueba de impacto, clic con la mirada
│   │   ├── Windows.cs           # todas las ventanas (visor y pantalla táctil) + UiModel.cs
│   │   ├── DriftCorrector.cs    # anti-deriva: sesgo en reposo + corrección lenta con brújula
│   │   ├── MeshBuilder.cs       # esfera, cúpula, ojo de pez, pantalla plana
│   │   ├── CubemapMesh.cs       # cubemap EAC / 3×2 (verificado contra ffmpeg v360)
│   │   ├── SphericalMetadata.cs # st3d / sv3d / mallas mshp del MP4
│   │   ├── ViewerProfile.cs     # perfil Cardboard (URL del QR) + QrDecoder.cs (ZXing.Net)
│   │   └── VrMath.cs …          # sensor → cámara, recentrar, horizonte, proyección
│   └── VrView/                 # net10.0-android: la app
│       ├── MainActivity.cs      # carpeta, lista, continuar, perfil del visor por QR
│       ├── PlayerActivity*.cs   # reproducción, ventanas y sus acciones, entrada
│       ├── HeadTracker.cs       # sensores con anti-deriva
│       ├── VideoFrames.cs, LibraryThumbs.cs, MediaInfoReader.cs
│       ├── WindowView.cs        # las ventanas en la pantalla táctil
│       └── Gl/                  # renderizador OpenGL ES, shaders, pintor de ventanas
└── tests/VrView.Core.Tests/    # xUnit
```

## Limitaciones conocidas

- Usa el `MediaPlayer` del sistema: códecs y resolución máxima dependen del móvil (la ventana
  Información dice si hay decodificador por hardware).
- Probado con pruebas automáticas y datos simulados, no en todos los teléfonos: la corrección de
  deriva depende de la brújula de cada móvil (si se porta mal, usa «Solo giroscopio»), y las
  mallas VR180 incrustadas se probaron con archivos sintéticos.
- Los enlaces cortos `goo.gl` de algunos visores antiguos ya no funcionan (Google los apagó);
  en ese caso escribe la dirección completa del perfil.
- Sin reproducción por red (DLNA/SMB/HTTP) ni fotos.
