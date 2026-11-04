# Publicar VrView en Google Play

Todo el material de la ficha está en [`fastlane/metadata/android/`](../fastlane/metadata/android/),
con la estructura que usa `fastlane supply`, y también se puede copiar a mano en Play Console.

| Idioma de la ficha | Carpeta |
|---|---|
| Inglés (predeterminado) | `en-US` |
| Español (Latinoamérica) | `es-419` |
| Español (España) | `es-ES` |
| Francés | `fr-FR` |
| Alemán | `de-DE` |

Cada carpeta trae `title.txt` (30 caracteres), `short_description.txt` (80), `full_description.txt`
(4000), `changelogs/3.txt` (500) e `images/` con `icon.png` (512×512) y `featureGraphic.png`
(1024×500). Los tests revisan estos límites.

## Lo que falta antes de publicar

1. **Capturas**: requieren un APK con esta versión (ver abajo). Play pide al menos 2 por idioma.
2. **Correo de contacto**: cambia `CONTACT_EMAIL` en [`privacy-policy.html`](privacy-policy.html).
3. **Publicar la política de privacidad** en una URL pública (por ejemplo GitHub Pages) y ponerla en
   Play Console. Es obligatoria.
4. **Clave de firma**: crea tu propia clave de subida y guárdala fuera del repo. Sin copia de seguridad
   no podrás restablecerla sin pasar por soporte de Google.
5. **Prueba cerrada**: las cuentas personales de desarrollador creadas desde noviembre de 2023 deben
   tener 12 personas probando 14 días seguidos antes de poder publicar. Confírmalo en tu cuenta.
6. **Revisar las traducciones** al francés y al alemán con alguien nativo si es posible, y el nombre
   «VrView» en la búsqueda de la tienda, por si hay apps parecidas.

## La cerveza

En Play Console → Monetizar → Productos → Productos únicos, crea:

- **ID del producto**: `tip_beer` (debe coincidir con `TipJar.ProductId`; no se puede cambiar).
- **Precio**: 1.99 USD, con precios locales automáticos.

| Idioma | Nombre | Descripción |
|---|---|---|
| Inglés | Buy me a beer | A thank-you for the developer. It unlocks nothing: VrView is already complete. |
| Español | Invítame una cerveza | Un agradecimiento al desarrollador. No desbloquea nada: VrView ya viene completo. |
| Francés | Offrez-moi une bière | Un merci au développeur. Cela ne débloque rien : VrView est déjà complet. |
| Alemán | Spendier mir ein Bier | Ein Dankeschön an den Entwickler. Es schaltet nichts frei: VrView ist bereits komplett. |

Es un producto consumible: se puede invitar más de una vez. La app no guarda quién pagó y no cambia
en nada al pagar. Para pagos de prueba, agrega tu cuenta en Play Console → Pruebas de licencia e
instala la app desde una pista de prueba: en un APK instalado con `adb` el botón solo avisa que no
está disponible.

## Respuestas para Play Console

Confírmalas con el manifiesto del `.aab` final, porque la librería de Google Play Billing agrega
dependencias propias.

- **Seguridad de los datos**: la app no recopila ni comparte datos. Los pagos los procesa Google Play.
- **Anuncios**: no contiene anuncios.
- **Acceso a la app**: todo disponible sin cuenta.
- **Clasificación de contenido**: reproductor multimedia sin contenido propio. Contesta el cuestionario
  según lo que la app incluye, no según lo que cada persona reproduce.
- **Público objetivo**: adultos; no está dirigida a menores.
- **Categoría**: Reproductores y editores de video.

## Generar el paquete para Play

Play exige `.aab` para apps nuevas (el proyecto genera `.apk` por defecto para instalar con `adb`):

```bash
cd VrView/src/VrView
dotnet publish -c Release -p:AndroidPackageFormats=aab -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=/ruta/a/tu-clave.jks -p:AndroidSigningKeyAlias=vrview \
  -p:AndroidSigningKeyPass=env:VRVIEW_KEY_PASS -p:AndroidSigningStorePass=env:VRVIEW_STORE_PASS
```

Sube `ApplicationVersion` en `VrView.csproj` en cada versión y agrega su `changelogs/<versión>.txt`.

## Capturas

No uses videos reales: los clips de demostración son originales, sin derechos de terceros.

```bash
python3 VrView/store/tools/make_demo_videos.py /tmp/VrViewDemo
adb push /tmp/VrViewDemo /sdcard/Movies/VrViewDemo
```

Abre VrView en el teléfono, elige la carpeta `Movies/VrViewDemo` y ejecuta:

```bash
python3 VrView/store/tools/capture_screenshots.py
```

Deja las imágenes en `images/phoneScreenshots/` de cada idioma, ya con su texto. Para regenerar el
icono y el gráfico destacado: `python3 VrView/store/tools/make_graphics.py`.
