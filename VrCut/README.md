# VrCut

Editor gráfico para quitar partes (intro, escenas) de videos MP4 **sin perder calidad**.
Funciona en Mac y Windows, con archivos locales o directamente en un teléfono Android vía USB.

Los cortes se hacen copiando los datos del video (`ffmpeg -c copy`), sin volver a codificar:
la calidad es idéntica y el archivo resultante siempre es más pequeño que el original.

## Requisitos

| | Mac (Homebrew) | Windows (winget) |
|---|---|---|
| .NET 10 SDK | `brew install dotnet` | `winget install Microsoft.DotNet.SDK.10` |
| ffmpeg | `brew install ffmpeg` | `winget install Gyan.FFmpeg` |
| adb (solo para el teléfono) | `brew install android-platform-tools` | `winget install Google.PlatformTools` |

En el teléfono: activa **Opciones de desarrollador → Depuración USB** y acepta el aviso al conectarlo.

## Ejecutar

```bash
cd VrCut
dotnet run -c Release
```

Para generar un ejecutable independiente:

```bash
dotnet publish -c Release -r osx-arm64 --self-contained   # Mac con Apple Silicon
dotnet publish -c Release -r win-x64 --self-contained     # Windows
```

## Uso

1. Arriba elige **Esta computadora** o **Teléfono Android** y la carpeta (doble clic para entrar en subcarpetas).
2. Ordena la lista por **nombre, fecha o tamaño** (el botón de al lado invierte el orden).
3. Selecciona un video y pulsa **📥 Importar** (o Enter / doble clic): aparecen las miniaturas en el
   timeline y la vista previa del cuadro. Un clic simple solo selecciona, no carga nada.
4. Haz clic o arrastra en el timeline para mover el cabezal (flechas: ±1 s, Shift+flechas: ±10 s).
5. Pulsa **Marcar inicio (I)** y **Marcar fin (O)** para cada parte a borrar (se ve en rojo).
   **✂ Inicio → aquí** quita la intro de un solo clic.
6. **Exportar sin pérdida**: guarda `cut_<nombre>.mp4` junto al original (o lo reemplaza).
   Si el video viene del teléfono, el resultado se envía de vuelta a la misma carpeta.

### Renombrar y borrar

- **✏️ Renombrar** (F2 o clic derecho): el nombre aparece seleccionado sin la extensión.
- **🗑 Borrar** (Supr o clic derecho): puedes seleccionar varios videos con Cmd/Ctrl+clic o Shift+clic.
  En Mac van a la Papelera (la primera vez macOS pide permiso para controlar Finder) y en Windows a la
  Papelera de reciclaje. **En el teléfono se borran definitivamente**: Android no tiene papelera por USB.
- Las carpetas se pueden renombrar pero no borrar, para evitar accidentes.

### Keyframes

Sin volver a codificar, cada parte conservada debe empezar en un *keyframe* (las marcas azules
bajo el timeline). Con **Ajustar a keyframe** activado, el final de cada corte se mueve al siguiente
keyframe, así nunca queda un pedazo de la escena borrada. El resultado coincide con lo que ves.

### Videos VR

- **Vista de un ojo (SBS)** muestra solo la mitad izquierda en las miniaturas de videos lado a lado.
- Se usa el prefijo `cut_` para que las etiquetas al final del nombre (`_180_LR`, `_360_TB`...) sigan funcionando.
- Se intenta conservar la metadata esférica (`-strict unofficial`); si tu visor no detecta el formato,
  basta con la etiqueta en el nombre.
