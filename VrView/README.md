# VrView

> this should be my own VR viewer with folder playlist and suffle for android.

VrView es un reproductor de vídeo VR para Android pensado para usar el móvil dentro de un
visor tipo Cardboard. Reproduce **carpetas enteras como lista de reproducción**, con
**modo aleatorio** y repetición. Toma como modelo *VR Media Player* (Reproductor multimedia VR,
de eMage Systems).

Está escrito en **C# con .NET 10 para Android** (`net10.0-android`), con vistas nativas de
Android y OpenGL ES 2.0. No usa Unity, MAUI ni librerías externas: solo el SDK de Android y
`MediaPlayer` del sistema.

## Funciones

| Función | Detalle |
|---|---|
| Lista por carpeta | Eliges una carpeta con el selector del sistema (no hace falta ningún permiso de almacenamiento) y se listan sus vídeos en orden natural (`clip2` va antes que `clip10`). Puedes incluir subcarpetas. Al terminar un vídeo empieza el siguiente. |
| Aleatorio y repetición | Aleatorio sí/no. Repetir: no / todo / uno. «Anterior» vuelve por el orden real de reproducción, también en aleatorio. Al volver a empezar la lista aleatoria se baraja de nuevo, sin repetir seguido el último vídeo. |
| Recordar | Recuerda la última carpeta, el último vídeo («Continuar») y la posición de cada vídeo. También guarda por vídeo el formato elegido a mano y la corrección del horizonte. |
| Formatos | Plano 2D, 180°, 360° y ojo de pez (190° por defecto, o el valor de la etiqueta). Mono, lado a lado (SBS/LR), arriba-abajo (TB/OU), y ojos invertidos (RL/BT). |
| Detección automática | Por etiquetas en el nombre del archivo y, si faltan, por las proporciones del vídeo. Se puede cambiar a mano (botón **Formato**: auto → cada formato → auto). |
| Visor VR | Pantalla partida para los dos ojos, con seguimiento de la cabeza por el sensor de rotación (giroscopio). Zoom (campo de visión), separación de lentes (IPD) y corrección de la distorsión de las lentes (no / suave / fuerte). |
| Horizonte | **Recentrar** (pone el centro del vídeo donde miras) y corrección manual de inclinación, nivel (alabeo) y giro para vídeos grabados torcidos. Se guarda para cada vídeo. |
| Sin visor | Vista «pantalla» (una sola imagen): arrastra con el dedo para mirar alrededor y pellizca para hacer zoom. |
| Abrir con… | Aparece en «Abrir con» de galerías y gestores de archivos para reproducir un vídeo suelto. |

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

- **Tocar la pantalla** (o el botón/palanca del visor): abre el menú VR. Con el menú abierto,
  pulsa el botón al que apunta el punto central; si no apuntas a nada, lo cierra.
- **Mirar hacia abajo** un momento: abre el menú.
- **Clic mirando**: deja el punto sobre un botón ~1,5 s y se pulsa solo (se puede desactivar
  en Ajustes). Los botones de ±10 s, volumen y horizonte se repiten mientras sigas mirando.
- En la barra de progreso del menú, mira el punto del vídeo al que quieres saltar.
- El menú se oculta solo si dejas de mirarlo unos segundos.

Menú principal: anterior, −10 s, reproducir/pausa, +10 s, siguiente, volumen −/+, aleatorio,
repetir, formato, horizonte, recentrar, ajustes, salir.

### Mando Bluetooth, teclado, mandos de visor y botones multimedia

| Botón | Acción |
|---|---|
| A / OK / Enter | reproducir/pausa (con el menú abierto: pulsar lo que miras) |
| Espacio / Play-Pausa / botón del auricular | reproducir/pausa |
| ← / → (cruceta) · Rebobinar / Avanzar | −10 s / +10 s |
| ↑ / ↓ (cruceta) | volumen + / − |
| L1 / R1 · Anterior / Siguiente · RePág / AvPág | vídeo anterior / siguiente |
| L2 / R2 | −60 s / +60 s |
| X / Start / Menú / `M` | abrir o cerrar el menú VR |
| B / Esc / Atrás | volver o cerrar el menú (Atrás sin menú: volver a la lista) |
| Y / `R` | recentrar |
| Select / `F` | cambiar formato |
| `H` | menú de horizonte (ahí la cruceta ajusta inclinación/nivel y L1/R1 el giro) |
| `S` | aleatorio sí/no |
| `V` | vista visor VR / pantalla |
| Teclas de volumen | volumen del sistema |

Los mandos pequeños de visor que se comportan como mando de juego, teclado o mando multimedia
funcionan con esta tabla. Los botones de los auriculares Bluetooth llegan por la sesión
multimedia.

### Fuera del visor (pantalla táctil)

- El botón **☰** de la esquina superior derecha muestra u oculta los controles táctiles.
  En vista «pantalla» basta con tocar el vídeo.
- Controles: lista, vista (visor VR / pantalla), formato, horizonte, recentrar, ajustes, barra
  de progreso, anterior, ±10 s, reproducir/pausa, siguiente, aleatorio, repetir y volumen.
- En vista «pantalla»: arrastra para mirar alrededor y pellizca para hacer zoom.

### Ajustar el horizonte

Abre **Horizonte** (en el menú VR, en los controles táctiles o con `H`):

- **Inclinar ↑/↓** corrige vídeos grabados mirando un poco hacia arriba o hacia abajo.
- **Nivelar ↺/↻** endereza un horizonte torcido.
- **Girar ←/→** cambia qué parte del vídeo queda delante.
- **Restablecer** quita la corrección. Los valores se guardan para cada vídeo.

**Recentrar** no se guarda: pone el frente del vídeo donde estás mirando ahora. Se hace solo al
abrir el reproductor.

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
│   ├── VrView.Core/          # net10.0, sin Android: lógica pura y testeable
│   │   ├── Playlist.cs        # orden, aleatorio, repetición
│   │   ├── FormatDetector.cs  # etiquetas del nombre y proporciones → formato
│   │   ├── VideoFormat.cs     # proyección, estéreo, recorte por ojo
│   │   ├── VrMath.cs          # sensor → cámara, recentrar, horizonte, proyección
│   │   ├── MeshBuilder.cs     # esfera 360, cúpula 180, ojo de pez, pantalla plana
│   │   ├── VrMenu.cs          # disposición del menú VR y «clic mirando»
│   │   └── VideoState.cs …    # posición, formato y horizonte guardados por vídeo
│   └── VrView/               # net10.0-android: la app
│       ├── MainActivity.cs    # elegir carpeta, lista, aleatorio/repetir, continuar
│       ├── PlayerActivity.cs  # MediaPlayer, controles táctiles, mando, sesión multimedia
│       ├── HeadTracker.cs     # sensor de rotación (game rotation vector)
│       ├── FolderScanner.cs   # listado de la carpeta (Storage Access Framework)
│       └── Gl/                # renderizador OpenGL ES, shaders y menú VR
└── tests/VrView.Core.Tests/  # xUnit
```

## Limitaciones conocidas y posibles mejoras

- Usa el `MediaPlayer` del sistema. Los códecs soportados (H.264, H.265/HEVC, VP9, AV1…) y la
  resolución máxima (4K, 5.7K, 8K) dependen del móvil.
- No hay subtítulos, selección de pista de audio, fotos, ni reproducción por red (DLNA/SMB/HTTP).
- La corrección de lentes usa coeficientes genéricos. No lee el código QR de perfil de
  Cardboard.
- Los vídeos planos se ven en una pantalla virtual sobre fondo negro, sin sala de cine.
