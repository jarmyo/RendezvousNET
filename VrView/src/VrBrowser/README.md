# VrBrowser

Navegador para ver **videos de sitios web en un visor VR** tipo Cardboard. Es la app hermana de
[VrView](../../README.md): VrView reproduce tus videos y VrBrowser trae los de la web.

- En el **teléfono** navegas, guardas sitios favoritos y detectas el video de una página (MP4 o
  stream HLS) para verlo en el reproductor de VrView, guardarlo o descargarlo.
- En el **visor** la página flota frente a ti y el video de la página se ve en pantalla completa
  dentro del VR, como pantalla de cine, 180° o 360°, en 2D o 3D.

**Necesita VrView** para dos cosas: reproducir los videos detectados y la **configuración del
visor** (perfil del QR, lentes, campo de visión, controles), que se hace una sola vez, en VrView, y
VrBrowser la lee al abrirse. Sin VrView el navegador funciona igual, con el visor genérico. Las dos
apps deben estar firmadas con la misma clave. La primera vez, VrBrowser también trae los favoritos y
videos web que VrView guardaba cuando el navegador era parte de él.

## En el teléfono: encontrar el video

Algunos sitios de videos VR tienen su propio visor web y no siempre dejan descargar el archivo. Al
abrir VrBrowser ves un navegador normal, con la última página que visitaste:

- **☆ Sitio / ★ Sitio** guarda o quita la página de los favoritos; **★ Favoritos** los lista
  (mantén pulsado uno para renombrarlo o borrarlo).
- Reproduce el video en la página unos segundos y pulsa **🎬 Videos (n)**: aparecen las direcciones
  de video que usó la página (archivos MP4/WebM/MOV y streams HLS `.m3u8`). Para cada uno:
  **▶ Ver en el visor de VrView**, **★ Guardar en Videos web**, **⬇ Descargar** (solo archivos;
  se guardan en *Descargas/VrView*, carpeta que luego puedes elegir en la lista) o copiar el enlace.
  Si el sitio ofrece su propio botón de descarga, VrView también lo atiende.
- Los **★ Videos web** guardados están en **★ Favoritos**. Se reproducen en VrView y recuerdan
  posición, formato, marcas y horizonte como cualquier video. Usa etiquetas en el nombre
  (`_180_LR`…) para que el formato salga solo.
- Las direcciones de stream suelen **caducar**. Si un video guardado deja de cargar, mantenlo
  pulsado en «★ Videos web» → **Abrir su página** y vuelve a reproducirlo o guardarlo: el enlace se
  renueva y se conservan su nombre y lo recordado.

VrBrowser le pasa a VrView las mismas cookies, agente y página de origen que usa el navegador, así que funcionan los
sitios en los que inicias sesión. No funcionan los videos protegidos con DRM, los streams DASH
(`.mpd`, el reproductor del sistema no los soporta) ni las transmisiones que el sitio arma por
partes sin una dirección propia.

## En el visor: la web flotando frente a ti

**🥽 En VR** (en el navegador del teléfono, en la misma página) abre la web **dentro del visor**: la
página flota frente a ti como una pantalla grande y, debajo, tiene una barra. Se usa como cualquier
navegador: enlaces, botones, menús, buscadores y formularios.

- **Clic**: apunta con la retícula a cualquier elemento de la página (enlace, botón, casilla, campo
  de texto) y pulsa el botón del visor, toca la pantalla o pulsa A en el mando. Con el clic con la
  mirada activado (en los ajustes de VrView), basta con mirar el enlace, botón o campo el tiempo de
  espera: el anillo de la retícula se llena y hace clic una vez. Sobre texto, imágenes y videos no
  hace clic, así que leer no activa nada. Los botones de la
  barra y del teclado también se pulsan mirándolos, como en el resto de VrView.
- **Teclado en el visor**: al hacer clic en un campo de texto de la página se abre un teclado debajo
  de ella. Muestra lo que llevas escrito (con puntos si es una contraseña) y tiene ñ, números,
  símbolos y vocales con acento (**?123**), mayúsculas (**⇧**), espacio, ⌫, flechas ← → para mover el
  cursor y **⏎**, que envía el formulario o hace la búsqueda. Para pasar a otro campo, haz clic en él;
  al hacer clic fuera de un campo, el teclado se cierra. También se abre con **⌨ Teclado**.
- **Listas y mensajes de la página**: al hacer clic en una lista desplegable, sus opciones se abren en
  una ventana del visor (con varias páginas si son muchas). Los avisos y preguntas de la página
  («¿Seguro que quieres…?») también se muestran ahí, con **Aceptar** y **Cancelar**. Salir de la ventana
  sin responder cuenta como Cancelar.
- **🔍 Dirección**: escribe una dirección (con **.com** a la mano) o palabras para buscar en Google,
  y **Ir**.
- **Teclado Bluetooth**: si tienes uno conectado, escribe directo en el campo de la página.
- **Barra**: ◀ Atrás, Adelante ▶, ⟳, 🔍 Dirección, ⌨ Teclado, Recentrar, **▲ Subir / ▼ Bajar**
  (desplaza la página o el recuadro que está en medio), zoom − / ＋, **★ Favoritos** y **☆ Sitio**.
  También están **🎬 Reproductor**, que abre en VrView el video que detectó la página, y
  **📱 Pantalla del teléfono**, que vuelve al navegador normal. Las sesiones y cookies son las mismas
  en los dos navegadores.
- **Pantalla completa en VR**: el botón ⛶ del reproductor del sitio ya no manda el video a la
  pantalla del teléfono, sino que lo muestra en el visor. **⛶ Video en VR** hace lo mismo con
  cualquier video de la página, aunque el sitio no tenga pantalla completa. La página toma la forma
  del video y se dibuja con el mismo motor del reproductor. En **Formato** eliges cómo se ve: pantalla
  de cine (con **Pantalla grande**), 180°, 360°, ojo de pez o cubemap, en 2D o en 3D (lado a lado o
  arriba-abajo). VrView recuerda el formato elegido para cada sitio. Si el título de la página trae
  etiquetas (`180 SBS`, `360`…), lo toma solo.
- En pantalla completa, un toque muestra u oculta la barra: ◀◀ 10 s, ▶/II, 10 s ▶▶, Recentrar,
  Formato y ✕ Salir. La barra se oculta sola a los pocos segundos.
- **Mando o teclado**: A / Enter = clic, B / Esc = atrás, ▲ / ▼ = desplazar, Y / R = recentrar,
  L1 / F = entrar o salir de **⛶ Video en VR**, X / Menú = mostrar la barra, Espacio = ▶/II y, en
  pantalla completa, ◀ / ▶ = 10 s atrás o adelante.

Como se ve la imagen que dibuja la página, aquí sí se pueden ver videos que el reproductor del
sistema no abre: los streams armados por partes o los de sitios que no dan la dirección del archivo.
La calidad es la de la página (hasta 2048 px por el lado largo). Para la mejor calidad, saltos y
marcas, usa **🎬 Reproductor** cuando esté disponible.

## Compilar e instalar

Igual que VrView (ver sus [requisitos](../../README.md#compilar-e-instalar-en-el-móvil-desde-un-mac)):

```bash
cd VrView/src/VrBrowser
dotnet publish -c Release
adb install -r bin/Release/net10.0-android/publish/net.rendezvous.vrbrowser-Signed.apk
```

El motor VR (`../VrView/Gl/`), el sensor y los ajustes son los archivos de VrView, compilados también
en esta app (ver `VrBrowser.csproj`). Los textos están en `../VrView.Core/Translations.cs` y lo que
se dicen las dos apps, en `../VrView.Core/AppLinks.cs`.

| Archivo | Qué hace |
|---|---|
| `BrowserActivity.cs` | navegador del teléfono, favoritos, detección y descarga de videos |
| `VrBrowserActivity.cs` | la página dentro del visor y su pantalla completa en VR |
| `WebSurface.cs` | la página dibujada en una textura (pantalla virtual privada) |
| `ViewerLink.cs` | ajustes del visor desde VrView y videos al reproductor de VrView |
| `WebVideosDialog.cs`, `WebHeaders.cs` | videos web guardados; cookies, agente y origen |
