# Centro de Mando

Panel de control para PC con Windows: monitoriza el hardware, controla ventiladores e iluminación, configura periféricos compatibles y ayuda a que el equipo vaya más fluido. Todo en una sola ventana y en español.

## Qué hace

| Sección | Funciones |
|---|---|
| **Panel** | Temperatura y uso de CPU, GPU y RAM; red; discos; ventiladores. Cuentas atrás personalizables y el tiempo de tu localidad. |
| **Monitor** | Todos los sensores al detalle: núcleos, relojes, voltajes, potencias, discos NVMe/SATA. |
| **Ventiladores** | Velocidad de cada ventilador y control manual o por curva de temperatura. |
| **RGB** | Efectos y colores (con rueda de color) para los conectores RGB/ARGB de placas **ASUS Aura**. |
| **Periféricos** | Detecta automáticamente teclados, ratones, mandos y auriculares. Configura los compatibles (ver abajo) y controla el sonido: volumen, silencio y dispositivo de salida y entrada predeterminado. |
| **Overclock** | GPU NVIDIA (reloj, memoria, límite de potencia y temperatura), planes de energía, y guías de BIOS adaptadas a tu procesador y memoria. Liberar RAM. |
| **Procesos** | Lista de procesos por consumo y cierre rápido de lo que sobra. |

Todo se detecta solo al abrir la app: procesador, gráfica, placa, memoria, discos, ventiladores y periféricos.

Disponible en 10 idiomas: español, English, français, Deutsch, italiano, português, català, polski, русский y 简体中文. Se elige en la barra lateral; la primera vez se usa el idioma de Windows.

### Periféricos compatibles

| Tipo | Modelos | Qué se puede cambiar |
|---|---|---|
| Teclados magnéticos | AJAZZ y otras marcas con chip RongYuan (USB `3151`) | Punto de actuación, Rapid Trigger y zona muerta por tecla; efectos de iluminación |
| Ratones | VXE y otras marcas con chip Compx (USB `373B`) | DPI, frecuencia de sondeo, distancia de levantamiento y más |
| Logitech | Ratones y teclados G (por cable o receptor Lightspeed/Unifying/Bolt) | Iluminación RGB, DPI y batería |
| Razer | Ratones y teclados con Chroma | Iluminación RGB, brillo y DPI |
| Redragon y marcas con chip EVision | Teclados (USB `320F`) y ratones Redragon (M711, M908…) | Efecto, color, brillo y velocidad |
| SteelSeries | Rival 3, 100/110, 300, 600, Aerox 3, Apex 3 TKL | Color, brillo y efecto |

Los demás dispositivos aparecen como «Solo detección». Cierra el programa oficial de cada marca (G HUB, Synapse, SteelSeries GG…) mientras uses Centro de Mando, para que no se peleen por el control.

## Requisitos

- Windows 10 u 11 de 64 bits.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64).
- WebView2 Runtime (viene con Windows 11).
- Para leer temperaturas y controlar ventiladores: el controlador [PawnIO](https://pawnio.eu/). La app avisa si falta.
- Para teclados AJAZZ: tener instalado el driver oficial de AJAZZ. Centro de Mando usa su servicio en segundo plano, pero no hace falta abrir la app de AJAZZ.

La app pide permisos de administrador al abrirse; sin ellos funciona en modo limitado (sin temperaturas de CPU, control de ventiladores ni overclock).

## Instalación

1. Descarga el `.zip` de la última versión en **Releases**.
2. Descomprímelo donde quieras y abre `CentroDeMando.exe`.

Los ajustes (ubicación del clima, cuentas atrás, curvas de ventiladores…) se guardan en `%APPDATA%\CentroDeMando\settings.json`, solo en tu PC.

## Compilar desde el código

```bash
dotnet publish CentroDeMando/CentroDeMando.csproj -c Release -o publicar
```

## Avisos

- El overclock y el control de ventiladores pueden dañar el hardware si se usan mal. Úsalos bajo tu responsabilidad.
- Proyecto no oficial: no está afiliado a AJAZZ, VXE, ASUS, NVIDIA ni AMD.
- El tiempo procede de [Open-Meteo](https://open-meteo.com/).

## Licencia

MIT. Consulta el archivo `LICENSE`.
