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
| Recordar | Recuerda la última carpeta, el último vídeo («Continuar») y la posición de cada vídeo. También guarda por vídeo el formato elegido a mano, la corrección del horizonte y las marcas. Todo eso se guarda en **`vrview.json` dentro de la carpeta** (ver abajo), así que sobrevive a reinstalar la app. |
| Marcas | Guarda momentos del vídeo (**＋ Marca**) y salta entre ellos (**◀ Marca / Marca ▶**). Se ven como rayitas amarillas en la barra de progreso y, en la página **Marcas**, como miniaturas: mira una para saltar a ella, o activa **Borrar: sí** para quitarla. |
| Formatos | Plano 2D, 180°, 360° y ojo de pez (190° por defecto, o el valor de la etiqueta). Mono, lado a lado (SBS/LR), arriba-abajo (TB/OU), y ojos invertidos (RL/BT). |
| Detección automática | Por etiquetas en el nombre del archivo y, si faltan, por las proporciones del vídeo. Se puede cambiar a mano (botón **Formato**: auto → cada formato → auto). |
| Formato por carpeta | El formato que eliges a mano se guarda para ese vídeo **y para su carpeta**: los demás vídeos de la carpeta sin etiquetas en el nombre lo usan automáticamente (se indica «(carpeta)»). Las etiquetas del nombre siempre mandan. Volver a «auto» lo olvida. |
| Ajuste fino del formato | Para cámaras que no siguen el estándar exacto: **campo horizontal y vertical** de vídeos 180°/360° (p. ej. 200°×180° o 180°×120°), y en ojo de pez **ángulo, tipo de lente** (equidistante / equisólida / estereográfica), **posición** y **tamaño del círculo**. Se guarda con el formato del vídeo y de su carpeta. |
| Tapa abajo (nadir) | Disco negro difuminado bajo tus pies (20°–50°) para tapar la zona «pellizcada» donde estaba el trípode o la mano. |
| Tamaño y opacidad de los controles | **Controles: chicos / medianos / grandes** y **Opacidad: 100 / 80 / 60 / 40 %** (en Ajustes) cambian el menú VR y los botones táctiles. El menú VR tiene un **✕** para cerrarlo y los controles táctiles un botón **✕ Ocultar**. |
| Visor VR | Pantalla partida para los dos ojos, con seguimiento de la cabeza por el sensor de rotación (giroscopio). Zoom (campo de visión), separación de lentes (IPD) y corrección de la distorsión de las lentes (no / suave / fuerte). |
| Horizonte | **Fijar con la cabeza** (inclinas la cabeza hasta ver el horizonte derecho, te quedas quieto y se fija), **Recentrar** (pone el centro del vídeo donde miras) y corrección manual de inclinación, nivel (alabeo) y giro. Se guarda para cada vídeo. |
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
repetir, formato, horizonte, marcas, ajustes, salir, y **✕** (arriba a la derecha) para cerrarlo.
(**Recentrar** está en la página Horizonte, en el botón Y del mando y en los controles táctiles.)

Página **Marcas**: miniaturas de las marcas (5 por página), ◀ Marca, ＋ Marca, Marca ▶, páginas y
«Borrar: sí/no».

### Mando Bluetooth, teclado, mandos de visor y botones multimedia

| Botón | Acción |
|---|---|
| A / OK / Enter | reproducir/pausa (con el menú abierto: pulsar lo que miras) |
| Espacio / Play-Pausa / botón del auricular | reproducir/pausa |
| ← / → (cruceta) · Rebobinar / Avanzar | −10 s / +10 s |
| ↑ / ↓ (cruceta) | volumen + / − |
| L1 / R1 · Anterior / Siguiente · RePág / AvPág | vídeo anterior / siguiente |
| L2 / R2 | marca anterior / siguiente (sin marcas: −60 s / +60 s) |
| L3 (pulsar el stick) / `K` | añadir marca |
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
- Controles: lista, vista (visor VR / pantalla), formato, horizonte, recentrar, ajustes,
  tamaño de controles, ocultar, barra de progreso, anterior, ±10 s, reproducir/pausa, siguiente,
  aleatorio, repetir y volumen.
- En vista «pantalla»: arrastra para mirar alrededor y pellizca para hacer zoom.

### Ajustes en la carpeta: `vrview.json`

Al elegir una carpeta, VrView crea en ella un archivo `vrview.json` con lo que recuerda de sus
vídeos: posición, formato elegido a mano, horizonte y marcas, y el formato de cada subcarpeta.
Como vive junto a los vídeos, no se pierde al reinstalar la app, viaja con la carpeta si la copias
a otro teléfono y puedes editarlo a mano:

```json
{
  "version": 1,
  "folders": { "": "Dome180,SideBySide,0,190" },
  "videos": {
    "paseo_180_LR.mp4": { "position": 61000, "roll": -3.5, "spots": [ 12000, 95000 ] },
    "Conciertos/show.mp4": { "format": "Sphere360,TopBottom,0,190" }
  }
}
```

Tiempos en milisegundos y ángulos en grados. Los vídeos se identifican por su ruta dentro de la
carpeta elegida. Los ajustes generales (zoom, lentes, tamaño y opacidad de controles…) siguen en la
app.

Si elegiste la carpeta con una versión anterior de VrView (que pedía solo lectura), **vuelve a
elegirla** para darle permiso de escritura; mientras tanto los ajustes se guardan en la app como
antes.

### Ajuste fino del formato

En **Ajustes → Ajuste fino** (menú VR o controles táctiles). Los botones que no aplican al formato
actual aparecen apagados.

- **Campo H − / +**: en 180°/360°, cuánto abarca el vídeo de lado a lado; en ojo de pez, el ángulo
  del lente. Si la imagen se ve apretada hacia los lados, súbelo; si se ve estirada, bájalo.
- **Campo V − / +** (180°/360°): cuánto abarca de arriba a abajo. Muchas cámaras no llegan a 180°:
  si al mirar arriba o abajo se ve estirado o «pellizcado», bájalo.
- **Imagen ← → ↑ ↓**, **Círculo − / +**, **Lente** (solo ojo de pez): centra y ajusta el círculo de la
  imagen cuando la cámara no lo graba exactamente en el centro, y cambia la curva del lente.
- **Tapa abajo**: no / 20° / 30° / 40° / 50°.
- **Restablecer** vuelve al formato estándar.

Los valores se guardan en el formato del vídeo (y como formato de su carpeta), así que también van
en `vrview.json`, p. ej. `"Fisheye,SideBySide,0,200,lens=Equisolid,cx=0.01,r=1.03,nadir=30"`.
Cambiar de formato con el botón **Formato** quita el ajuste fino.

### Ajustar el horizonte

Abre **Horizonte** (en el menú VR, en los controles táctiles o con `H`):

- **Fijar con la cabeza** (lo más rápido): el menú se cierra y aparece una línea azul fija a tu
  vista. Inclina la cabeza (y mira arriba o abajo si hace falta) hasta que la línea quede sobre el
  horizonte del vídeo, y quédate quieto: tras un momento se llena el círculo central y esa
  posición queda como «derecho y al frente». Ya puedes enderezar la cabeza. Tocar la pantalla,
  A/OK o Atrás lo cancelan; si no te quedas quieto en 30 s, se cancela solo.
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
