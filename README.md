# PickPlaceRL

Proyecto de Unity 6.3 LTS con un robot Franka FR3, pinza física y Unity ML-Agents. Este repositorio conserva los experimentos históricos de Reach, Pick & Place con cubo y los posteriores con prisma. **Para entrenar el cubo fijo desde pesos nuevos y preparar un vídeo del aprendizaje, empieza por la guía siguiente.** Las secciones posteriores documentan la evolución histórica y pueden referirse a otras escenas y otros Run ID.

## Guía completa: entrenar el cubo desde cero y grabar la demostración

Esta guía está pensada para otra persona que clone el repositorio en **Windows 11**. Utiliza exclusivamente la escena independiente `Assets/Scenes/PickPlaceCubeReplay.unity`. No hay que modificar la escena actual `PickPlaceV3.unity`, que contiene el prisma. El entrenamiento nuevo usa el Run ID **`franka_cube_replay_from_scratch_v2`** y el Behavior **`FrankaCubeReplay`**. En una copia nueva, el primer arranque crea pesos aleatorios: no carga PlaceFix4 ni otro checkpoint. Las versiones históricas permanecen separadas.

### 1. Preparar una copia nueva

Instala Git y **Unity 6000.3.25f1** con soporte para este proyecto. Clona el repositorio y entra en su raíz:

```powershell
git clone https://github.com/aferlop5/MCR-PickPlace-RL.git
cd .\MCR-PickPlace-RL
```

En **Unity Hub**, añade esa carpeta como proyecto y ábrela con la versión indicada. Espera a que termine la importación y a que la consola de Unity no muestre errores de compilación C#. El proyecto instala la parte Unity de ML-Agents desde `Packages/manifest.json`.

En una terminal PowerShell situada en la misma raíz, prepara Python 3.10.12 y las dependencias del entrenador:

```powershell
.\scripts\setup_training_env.ps1
.\.venv\Scripts\python.exe .\tools\check_training_environment.py
```

El script crea `.python31012/`, `.venv/` y `.mlagents-src/` si faltan. Esas carpetas, los resultados PPO y los CSV están ignorados por Git. La comprobación debe terminar con `TRAINING ENVIRONMENT OK`. Si PowerShell impide ejecutar scripts, usa una sesión con scripts locales permitidos para tu usuario (`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`) y vuelve a ejecutar el setup.

### 2. Abrir y verificar **la escena correcta** en Unity

1. Con **Play detenido**, en la ventana **Project** de Unity abre `Assets > Scenes > PickPlaceCubeReplay` con doble clic. La escena abierta debe llamarse `PickPlaceCubeReplay`.
2. En **Hierarchy** debe aparecer `TrainingArenaSpawnerCubeReplay`. En el Inspector tiene **50 arenas**, diez columnas y `timeScale = 1`.
3. El prefab `Assets/Prefabs/TrainingArenaCubeReplay.prefab` contiene `TestObject`: escala **(0.05, 0.05, 0.05) m**, `BoxCollider`, `Rigidbody` de **0.1 kg** y rotación inicial fija. El cubo, el objetivo y la base del robot vuelven siempre a la misma pose al iniciar un episodio. Durante la manipulación, la física sí puede hacer girar el cubo.
4. En el robot de esa arena, `Behavior Parameters` debe indicar **`FrankaCubeReplay`**, **33 observaciones**, **8 acciones continuas**, sin modelo asignado y `Behavior Type = Default`. `Decision Period = 5` y `MaxStep = 1000`.
5. Guarda la escena. **No pulses Play todavía.** Abrir el script de PowerShell no cambia automáticamente la escena de Unity.

La comprobación estática, opcional, verifica archivos, configuración y Python; no puede detectar qué escena tienes abierta en el Editor:

```powershell
.\scripts\train_cube_from_zero_timelapse.ps1 -CheckOnly
```

### 3. Iniciar el entrenamiento desde pesos aleatorios

Con la escena del cubo abierta y Play detenido, ejecuta en PowerShell:

```powershell
.\scripts\train_cube_from_zero_timelapse.ps1 -RecordTimelapse -TimeoutWait 600
```

Espera a que la terminal muestre **`Listening on port 5004`**. Entonces pulsa **Play** en Unity. La siguiente señal correcta es **`Connected new brain: FrankaCubeReplay?team=0`**. Cuando empiecen las líneas de progreso de PPO, el entrenamiento está en marcha. Si aparece `FrankaPickPlaceV3`, está abierta la escena del prisma: detén Play, cierra esa terminal y abre `PickPlaceCubeReplay` antes de repetir. El Behavior diferente hace que el entrenador rechace esa conexión antes de aprender.

El script comprueba que el Run ID no tenga ya un entrenamiento. Si una conexión fallida solo dejó configuración y diagnósticos, los archiva con sufijo `_failed_scene_...` sin borrarlos y libera el ID. Si hay checkpoint, CSV o archivos desconocidos, se detiene para protegerlos. **No añadas `-Resume` en el primer inicio.** El run nuevo está en `results/franka_cube_replay_from_scratch_v2/` y sus CSV en `TrainingLogs/franka_cube_replay_from_scratch_v2/`.

La política controla siete articulaciones y la pinza. Se entrena con 50 arenas paralelas, `timeScale = 1`, PPO, semilla de Python `12345` y presupuesto de **7.000.000 pasos PPO**. El currículum avanza automáticamente por **Lift05cm → Lift10cm → Lift20cm → PickPlace30cm** según la recompensa suavizada; no hay que editar YAML entre etapas. Usa la lógica final del cubo desde el comienzo, por lo que **no reproduce exactamente** todos los cambios históricos entre Safe50 y PlaceFix4. El resultado final puede ser distinto; no se promete una tasa de éxito concreta.

`-RecordTimelapse` **no graba vídeo**: mantiene hasta 30 checkpoints numerados, aproximadamente cada 250.000 pasos PPO, para reconstruir después el progreso. Un paso PPO es experiencia registrada por el entrenador; no equivale a un fotograma ni a un segundo del vídeo.

### 4. Vigilar el aprendizaje y grabar el comienzo en directo

En otra terminal, desde la raíz del proyecto, puedes abrir TensorBoard:

```powershell
.\.venv\Scripts\tensorboard.exe --logdir .\results --port 6007
```

Abre `http://localhost:6007/` y filtra el run `franka_cube_replay_from_scratch_v2`. Mira `Environment/Cumulative Reward` junto con las tasas `V3/ReachRate`, `V3/GraspConfirmedRate`, `V3/LiftRate` y `V3/ControlledPlaceSuccessRate`. Una recompensa creciente por sí sola no demuestra que el robot coloque el cubo.

Si quieres mostrar la **política realmente inicial**, crea primero la carpeta con `New-Item -ItemType Directory -Force .\TimelapseCube\clips`. Maximiza la ventana **Game** para que se vea la primera arena y graba con tu capturador de pantalla un clip breve durante los primeros minutos del entrenamiento; guárdalo, por ejemplo, como `TimelapseCube/inicio_en_vivo.mp4`. Esa toma es **grabación en directo**. Los checkpoints no suelen incluir el instante de peso aleatorio exactamente en el paso 0. Mantén identificada esa toma como «inicio en directo»; el montaje automático descrito abajo solo concatena clips de checkpoints con un paso PPO registrado.

### 5. Parar y reanudar sin perder el modelo

Para parar, pulsa **Ctrl+C una sola vez en la terminal del entrenador**. Espera a que ML-Agents termine de guardar y vuelva el prompt de PowerShell; **después** detén Play en Unity. Comprueba que exista el checkpoint propio:

```powershell
Get-Item .\results\franka_cube_replay_from_scratch_v2\FrankaCubeReplay\checkpoint.pt |
  Select-Object Length, LastWriteTime
```

Para continuar en otra sesión, abre **la misma escena del cubo** y ejecuta:

```powershell
.\scripts\train_cube_from_zero_timelapse.ps1 -Resume -RecordTimelapse -TimeoutWait 600
```

Vuelve a pulsar Play solo cuando el entrenador indique que escucha en el puerto 5004. `-Resume` exige el checkpoint de **este run**; no carga PlaceFix4 ni el intento antiguo de 35 observaciones. Si se interrumpe antes del primer checkpoint, no hay pesos desde los que reanudar: conserva los archivos de ese intento y comienza una copia limpia del experimento. No uses `-Force` para ocultar el problema.

### 6. Elegir checkpoints reales para el vídeo

**Detén primero el entrenamiento**; Unity Editor y el entrenador de inferencia no deben disputar la misma conexión. Cuando haya checkpoints numerados, genera el manifiesto:

```powershell
.\.venv\Scripts\python.exe .\tools\cube_timelapse_manifest.py
Import-Csv .\TimelapseCube\manifest.csv |
  Select-Object step, lesson, reward, reach, grasp, lift, controlled_place |
  Format-Table
```

`TimelapseCube/manifest.csv` contiene los **pasos reales** de los checkpoints retenidos y los últimos valores TensorBoard disponibles hasta cada paso. Elige entre cinco y ocho momentos representativos: comportamiento temprano, mejora de Reach, agarre, elevación, transporte y colocación si realmente aparecen. No inventes una fase que las métricas o el vídeo no muestran. Copia siempre el número de la columna `step`; puede diferir ligeramente del intervalo nominal de 250.000.

### 7. Reproducir y grabar cada checkpoint

Para cada paso elegido, introduce un número de la columna `step` y ejecuta:

```powershell
$paso = [int](Read-Host 'Paso exacto de manifest.csv')
.\scripts\replay_cube_checkpoint.ps1 -Step $paso -TimeoutWait 600
```

El script comprueba el paso interno del checkpoint, crea una **copia de inferencia** en un Run ID propio y muestra la ruta de una escena de **una sola arena**. En Unity, con Play detenido, abre la escena que indique la terminal, espera a la importación y guarda. Cuando la terminal muestre `Listening on port 5004`, pulsa Play y captura varios episodios de la ventana **Game**. Guarda el clip como `TimelapseCube/clips/step_<paso>.mp4`, sustituyendo `<paso>` por el mismo número que introdujiste. Para pasar al siguiente checkpoint: **Ctrl+C en la terminal de inferencia**, espera al prompt, detén Play, abre la nueva escena y repite. Los clips representan inferencias de pesos guardados; **no son fotogramas continuos del entrenamiento**.

Para mostrar aparte lo que consiguió el modelo **histórico** PlaceFix4, abre `Assets/Scenes/PickPlaceCubeFinalReplay.unity` y ejecuta `.\scripts\replay_cube_final.ps1 -TimeoutWait 600`; pulsa Play cuando escuche. Graba ese material con el rótulo «PlaceFix4 histórico». Ese modelo **no pertenece al run desde cero** y no debe presentarse como el último checkpoint del nuevo entrenamiento.

### 8. Montar `timelapse.mp4`

Instala **FFmpeg** en el equipo y comprueba `ffmpeg -version` en PowerShell. Con `manifest.csv` y los clips `step_<pasos>.mp4` ya grabados, ejecuta:

```powershell
.\.venv\Scripts\python.exe .\tools\make_cube_timelapse.py
```

El script ordena los clips por paso, los normaliza a 1280×720 y 30 fps, añade rótulos con paso PPO, lección, recompensa y tasas realmente disponibles, y crea `TimelapseCube/timelapse.mp4`. Si FFmpeg no está instalado, conserva `manifest.csv` y los clips para montarlos manualmente. `inicio_en_vivo.mp4` y la demostración histórica de PlaceFix4 se añaden manualmente si se quieren incluir: no pertenecen a la secuencia automática de checkpoints. `TimelapseCube/`, `results/` y `TrainingLogs/` se quedan fuera de Git; comparte el vídeo final por separado.

### 9. Cómo interpretar el resultado y errores frecuentes

- **Escena equivocada:** `Connected new brain: FrankaPickPlaceV3` indica prisma. Abre `PickPlaceCubeReplay` antes de Play; la señal correcta es `FrankaCubeReplay`.
- **El Run ID ya existe:** si hay checkpoint del V2, usa `-Resume`; el script nunca lo sobrescribe. Si no hay checkpoint pero sí datos, consérvalos y prepara una copia limpia para otro intento.
- **`-Resume` dice que falta checkpoint:** la sesión se detuvo antes de guardar pesos. Los CSV y eventos pueden existir, pero no permiten recuperar la red.
- **No hay checkpoints en el manifiesto:** espera a que el run guarde uno o interrúmpelo correctamente después de haber entrenado. Los pasos exactos pueden diferir del intervalo configurado.
- **La pieza gira durante Play:** la rotación **inicial** del cubo es fija; los contactos con los dedos y la mesa pueden rotarlo físicamente durante un episodio.
- **No mejora Place:** conserva el resultado real. Como criterio orientativo, revisa al menos 1.000 episodios válidos de la lección final y compara Place físico, ControlledPlace y HighRelease; la recompensa por sí sola no basta.

Para más detalles técnicos del replay y sus límites, consulta [`docs/CUBE_REPLAY.md`](docs/CUBE_REPLAY.md). El resto de este README conserva la documentación histórica de V2 y V3.

## V2 - Reach: situación y resultado histórico

- `Assets/Scenes/SampleScene.unity` contiene el robot `fr3`, la mesa `Table` y el cubo `TestObject`. El Franka tiene siete articulaciones y pinza física de dos dedos.
- `RobotPickSequence.cs` conserva las posturas calibradas Home, Pre-Pick y Pick. Según la prueba manual del proyecto, permite agarrar y levantar físicamente el cubo. La secuencia no incluye una fase de depositarlo en otro lugar; no se han cambiado esas posturas, el URDF, los colliders, las masas ni el agarre.
- `FrankaLiftAgent.cs` es el agente de entrenamiento. Tiene dos etapas seleccionables en el Inspector: `Reach` y `Lift`. `Reach` es el valor inicial. La etapa de transporte y colocación está pendiente.
- La arena se agrupa como `Assets/Prefabs/TrainingArena.prefab`. Tras el benchmark corto, `SampleScene` está configurada con `numberOfArenas = 16`. El spawner asigna `arenaId` desde 0 y todas las instancias comparten la política `FrankaLift`.
- `trainer_config.yaml` configura PPO. El antiguo run `franka_reach_v1` solo contiene una muestra agregada a 10 000 pasos; el run local `franka_reach_v2` ya muestra éxito sostenido en Reach con cubo fijo, descrito más abajo.
- La escena usa `Behavior Name = FrankaLift`, 23 observaciones vectoriales, 8 acciones continuas y `Decision Requester` cada 5 pasos físicos, con acciones entre decisiones.

## Ejecutar primero una arena

1. Abre esta carpeta en Unity Hub con **Unity 6000.3.25f1** y carga `Assets/Scenes/SampleScene.unity`.
2. Para la comprobación inicial, cambia temporalmente `numberOfArenas` de 16 a 1. En `TrainingArena/fr3/FrankaLiftAgent`, selecciona `stage = Reach` y deja `randomizeCube` desactivado. Para entrenar con las 16 arenas ya validadas, vuelve a poner 16 y guarda la escena.
3. En `fr3`, comprueba `Behavior Parameters`: `Behavior Name = FrankaLift`, `Vector Observation Space Size = 23`, `Continuous Actions = 8`, `Discrete Branches = 0`, `Behavior Type = Default`, sin modelo. Deja `Decision Requester` activado con `Decision Period = 5` y `Take Actions Between Decisions` activado.
4. Arranca `mlagents-learn` en PowerShell con los comandos de abajo. Cuando indique que espera al Editor, pulsa **Play** en Unity.

Se usa `Decision Requester` porque ya está configurado en la escena y mantiene una cadencia física estable; no se llama a `RequestDecision()` desde el agente. Al pulsar Play sin entrenador, `Behavior Type = Default` usa `Heuristic()` como alternativa. Haz clic en la vista **Game** para darle foco: `1/2`, `3/4`, `5/6`, `Q/E`, `A/D`, `Z/C` y `R/F` mueven las siete articulaciones; **Espacio** cierra la pinza y soltarlo la abre. Para ver aprendizaje real hace falta el entrenador.

El script `RobotJointController.cs` es una herramienta de control manual por Inspector. No debe habilitarse junto al agente o `RobotPickSequence` porque los tres escribirían sobre los mismos `ArticulationDrive`.

### Repetir la demostración manual

Mantén `numberOfArenas = 1`. En `TrainingArena/fr3`, desactiva `FrankaLiftAgent` y `Decision Requester`, añade el componente `RobotPickSequence` y asigna `Joint Bodies` con tamaño 7 en orden `link1`…`link7`. Asigna también `fr3_leftfinger` y `fr3_rightfinger` a los dos campos de la pinza. Pulsa **Play**: la secuencia empieza en `Start()`, recoge el cubo y vuelve a Home con él sujeto. Esta preparación puede quedar como override de la instancia de la escena; para entrenar, vuelve a desactivar la secuencia y a activar el agente y `Decision Requester`.

## Entorno Python en Windows 11 con venv

La parte Unity instalada es `com.unity.ml-agents` **4.1.0**. Su [código de la rama 4.1.0](https://raw.githubusercontent.com/Unity-Technologies/ml-agents/release/4.1.0/ml-agents/mlagents/trainers/__init__.py) declara Python `mlagents` **1.2.0.dev0** y su [setup.py](https://raw.githubusercontent.com/Unity-Technologies/ml-agents/release/4.1.0/ml-agents/setup.py) exige Python **3.10.1–3.10.12**. Usa **Python 3.10.12 de 64 bits**. La versión publicada en PyPI 1.1.0 corresponde a otra publicación; aquí se instala la rama `release/4.1.0` para emparejar las dos partes.

Python.org no distribuye instalador de Windows para 3.10.12: fue una versión publicada solo como código fuente. En este equipo se instaló **Python 3.10.12 dentro de `.python31012/`** mediante `uv`, únicamente para obtener el intérprete. El entorno de paquetes usa el `venv` y `pip` estándar de Python, sin conda. Si partes de una copia nueva del proyecto, instala primero el intérprete así:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
$uvZip = Join-Path $env:TEMP 'codex-uv-win64.zip'
$uvHome = Join-Path $env:TEMP 'codex-uv-win64'
Invoke-WebRequest 'https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip' -OutFile $uvZip
Expand-Archive -LiteralPath $uvZip -DestinationPath $uvHome -Force
& (Join-Path $uvHome 'uv.exe') python install 3.10.12 --install-dir '.python31012' --no-bin --no-registry
& '.\.python31012\cpython-3.10.12-windows-x86_64-none\python.exe' --version
& '.\.python31012\cpython-3.10.12-windows-x86_64-none\python.exe' -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip setuptools wheel
if (!(Test-Path '.mlagents-src')) { git clone --depth 1 --branch release/4.1.0 https://github.com/Unity-Technologies/ml-agents.git .mlagents-src }
python -m pip install 'torch==2.2.2'
python -m pip install .\.mlagents-src\ml-agents-envs
python -m pip install .\.mlagents-src\ml-agents
python -m pip check
python -c "from importlib.metadata import version; print(version('mlagents'), version('mlagents_envs'))"
mlagents-learn --help
```

Los dos números impresos por Python deben ser `1.2.0.dev0`. La combinación verificada en este equipo está fijada en `scripts/setup_training_env.ps1`: Python 3.10.12, ML-Agents 1.2.0.dev0, torch 2.2.2+cpu, setuptools 80.9.0, protobuf 3.20.3, numpy 1.23.5 y TensorBoard 2.20.0. `.python31012/`, `.venv/`, `.mlagents-src/`, `results/` y `TrainingLogs/` están excluidos de Git. El clon de ML-Agents está en el commit `ee0a08ccae597094003844d0121317f9790a1676` de `release/4.1.0`.

Si PowerShell bloquea la activación, usa una sesión que permita scripts locales para tu usuario (`Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`) o ejecuta los comandos con `.\.venv\Scripts\python.exe` sin activar el entorno.

Para el siguiente entrenamiento limpio, con `stage = Reach`, `numberOfArenas = 16` y `trainingRunName = franka_reach_v2` como queda guardado en la escena:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train.ps1 -RunId franka_reach_v2
```

Cuando la terminal muestre que espera a Unity, pulsa **Play** en el Editor. No se usa `--env` porque el entorno corre dentro del Editor. Para detenerlo, interrumpe primero el entrenador con **Ctrl+C** una vez y espera a que guarde el checkpoint; después detén Play. Si necesitas reanudar el mismo experimento:

```powershell
.\scripts\train.ps1 -RunId franka_reach_v2 -Resume
```

Abre otra ventana de PowerShell, activa el mismo `venv` y abre TensorBoard:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\.venv\Scripts\Activate.ps1
tensorboard --logdir .\results --port 6007
```

Abre [http://localhost:6007/](http://localhost:6007/). **En este PC, Windows reserva el puerto 6006** (`netsh int ipv4 show excludedportrange protocol=tcp`), por lo que `tensorboard --logdir .\results --port 6006` no puede abrirlo. TensorBoard 2.20.0 se comprobó con HTTP 200 en 6007. No es necesario modificar las reservas de puertos del sistema.

## Curriculum manual

1. **Reach:** una arena, cubo fijo, `stage = Reach`. El episodio termina al acercar el TCP a menos de `reachDistance` (por defecto 7 cm). Observa que la distancia mínima baje y la tasa de éxito suba antes de seguir.
2. **Lift:** cambia el mismo componente a `stage = Lift`; mantén la misma forma de observaciones y acciones. Puedes inicializar la red con el experimento anterior:

   ```powershell
   mlagents-learn .\trainer_config.yaml --run-id=franka_lift_v1 --initialize-from=franka_reach_v2
   ```

   El éxito requiere elevar el cubo `targetHeight = 0.15 m` por encima de su altura inicial. Esta etapa puede requerir más pasos que Reach; aumenta `max_steps` de forma explícita tras revisar las curvas de aprendizaje.
3. **Variación pequeña:** una vez que Lift sea estable con el cubo fijo, activa `randomizeCube` y empieza con `cubeRandomXZ = 0.03 m`. Amplía el rango solo cuando la tasa de éxito se mantenga.
4. **Pick-and-place:** requiere añadir un destino, observaciones cubo→destino, fase de transporte y recompensa de colocación. Aún no está implementado.

## Recompensas y final de episodio

En cada paso físico, el agente recibe `2 × (distancia_anterior − distancia_actual) − 0.0005`. Acercarse suma, alejarse resta y el pequeño coste evita episodios sin avance. Entrar por primera vez en el radio de Reach suma `0.35`; en la etapa Reach también suma `1` y termina. En Lift, cerrar la pinza cerca del cubo puede sumar una sola vez `0.15`; el cambio de altura aporta `5 × (altura_actual − altura_anterior)`, y elevarlo 15 cm suma `3` y termina. Salir de la mesa, caer más de 10 cm o superar claramente los límites articulares termina con `−1`. `Max Step = 1000` cierra los episodios largos.

El bonus por cierre es una aproximación geométrica, no un detector de contacto: su magnitud es pequeña para que el éxito dependa del levantamiento físico. Ninguna recompensa pega el cubo a la pinza.

## Observaciones y acciones

`CollectObservations()` emite **23 números** siempre en el mismo orden:

| Índices | Contenido |
| --- | --- |
| 0–6 | Posiciones de `link1` a `link7`, normalizadas con los límites de cada drive. |
| 7–13 | Velocidades de `link1` a `link7`, divididas por `jointSpeed` y limitadas a [−1, 1]. |
| 14–16 | Vector cubo − TCP (x, y, z) en los ejes locales de la arena. El vector contrario sería redundante. |
| 17 | Altura del cubo respecto a su posición inicial dentro de esa arena. |
| 18 | Apertura medida del dedo izquierdo: 0 cerrado, 1 abierto. |
| 19–21 | Velocidad lineal del cubo (x, y, z) en los ejes locales de la arena. |
| 22 | Etapa: 0 Reach, 1 Lift. |

Las **8 acciones continuas** son: índices 0–6, variación del objetivo de `link1` a `link7` en ese orden; índice 7, pinza (`< 0` cerrar, `>= 0` abrir). Cada objetivo articular se mueve a velocidad limitada y se recorta con `Mathf.Clamp` entre `lowerLimit` y `upperLimit`. Las referencias espaciales son locales a cada arena para que sus coordenadas globales no cambien el problema.

## Reset y varias arenas

Cada `FrankaLiftAgent` guarda al inicializarse la posición del cubo y de la raíz articulada respecto a su propia arena. Al comenzar un episodio restaura la postura física Home, los objetivos y velocidades articulares, la pinza abierta, la pose y velocidades del cubo y la memoria de recompensas. La posición fija del cubo coincide con la escena original. La opción `randomizeCube` solo desplaza X/Z de **ese** cubo.

`TrainingArena.prefab` contiene la mesa, el robot y el cubo. `TrainingArenaReferences` comprueba que cada articulación, dedo, TCP y cubo pertenecen a la misma instancia. `TrainingArenaSpawner` copia la primera arena en una cuadrícula y asigna IDs 0…15; todas comparten `Behavior Name = FrankaLift` y una sola política PPO. Usa una cámara de la escena, no una por arena. Si el agente está activo, se desactiva cualquier `RobotPickSequence` o `RobotJointController` añadido por accidente; con el agente desactivado, la demo manual sigue disponible.

Avanza **1 → 2 → 4 → 8 → 16** cambiando `numberOfArenas` en el Inspector y manteniendo `columns = 4`, `spacing = 5 m`. Comprueba en Play, especialmente al pasar a 2, que cada cubo se reinicia solo en su mesa y que no hay errores de referencias. Aumentar instancias acelera la recogida de experiencias, pero no reemplaza la validación de una arena.

`TrainingArenaSpawner.timeScale` empieza en 1; después de comprobar la estabilidad puedes subirlo antes de Play para acelerar la simulación. El spawner mantiene el valor del Inspector durante Play y restaura el anterior al terminar. No hay cámaras ni bucles `Update` por arena. `trainer_config.yaml` fija también `engine_settings.time_scale: 1`: se comprobó que, al omitirlo, el entrenador Python aplica 20 y sobrescribe la escala de Unity. Esta es la razón técnica del cambio en el YAML. `episodes.csv` registra `Time.timeScale` efectivo al terminar cada episodio.

## Señales de aprendizaje

En TensorBoard mira `Environment/Cumulative Reward` y `Environment/Episode Length` junto a `Franka/Success`, `Franka/MinimumDistance` y `Franka/MaximumHeight`. Para Reach, `MinimumDistance` debe bajar y `Success` subir de manera sostenida. Para Lift, además debe subir `MaximumHeight` hasta superar 0.15 m. Una recompensa media creciente sin mejorar estas medidas puede indicar que el agente explota el shaping sin resolver la tarea. Compara varias ventanas de entrenamiento, no un solo episodio.

## Validación realizada

El 6 de octubre de 2026 se importó una copia temporal con Unity 6000.3.25f1 y se compiló el código sin errores C#. Unity generó el prefab y una comprobación en el Editor confirmó que dos instancias conservan referencias internas distintas. En Play se probaron una arena en Reach y dos en Lift sin entrenador durante 300 actualizaciones de Editor por configuración. También se forzó el reset de una instancia y una acción articular extrema: la otra arena permaneció independiente y el target probado quedó dentro de los límites.

En esa copia Unity emitió una excepción de su propio `UnityEditor.Search.SearchDatabase` al indexar recursos; la traza no apunta al código del agente. En el proyecto actual se instaló Python 3.10.12, se crearon `venv` y paquetes de ML-Agents, y `pip check` terminó sin dependencias rotas. `mlagents-learn` se conectó a Unity 4.1.0 con el comportamiento `FrankaLift`. El benchmark nuevo compiló sin errores C#, comprobó referencias exclusivas en 1/2/4/8/16 arenas y ejecutó Play corto en las cinco configuraciones. Una prueba adicional de 30 segundos conectó 16 arenas con PPO y guardó un modelo de diagnóstico. La ejecución posterior `franka_reach_v2` sí mostró éxito sostenido en Reach con cubo fijo; sus cifras se detallan en la sección de resultados. La demostración física manual se conserva.

## Archivos relevantes

| Ruta | Función |
| --- | --- |
| `Assets/Scenes/SampleScene.unity` | Escena de entrenamiento inicial. |
| `Assets/Prefabs/TrainingArena.prefab` | Arena autocontenida reutilizable. |
| `Assets/Scripts/FrankaLiftAgent.cs` | Observaciones, acciones, reset y recompensas. |
| `Assets/Scripts/TrainingArenaReferences.cs` | Verificación de referencias internas. |
| `Assets/Scripts/TrainingArenaSpawner.cs` | Cuadrícula de arenas; la escena está preparada con 16. |
| `Assets/Scripts/TrainingCsvLogger.cs` | CSV centralizado y buffered de episodios, resumen y pasos opcionales. |
| `Assets/Editor/TrainingArenaBenchmark.cs` | Validación de referencias y rendimiento 1/2/4/8/16. |
| `Assets/Editor/TrainingArenaSetup.cs` | Herramienta para regenerar el prefab desde la escena original si fuera necesario. |
| `Assets/Scripts/RobotPickSequence.cs` | Demo manual calibrada, conservada. |
| `trainer_config.yaml` | PPO: lote 512, búfer 10240, red 256×2, horizonte 128, 1.5 M pasos. |
| `scripts/setup_training_env.ps1`, `scripts/train.ps1` | Instalación reproducible y lanzamiento protegido por Run ID. |
| `tools/check_training_environment.py`, `tools/export_tensorboard_csv.py` | Comprobación de dependencias y exportación de métricas. |

En V2 no hay un destino de colocación. Reach con cubo fijo muestra éxito sostenido en el run local `franka_reach_v2`; el experimento V3 se documenta al final de este archivo.

## Preparar y comprobar el entorno de forma reproducible

Desde PowerShell en la raíz del proyecto:

```powershell
.\scripts\setup_training_env.ps1
.\.venv\Scripts\python.exe .\tools\check_training_environment.py
.\.venv\Scripts\python.exe -m pip check
```

El setup usa Python 3.10.12 local, `venv` y `pip`; descarga `uv` solo si falta el intérprete. Comprueba el commit exacto de ML-Agents y no reinstala un entorno que ya pasa todas las comprobaciones. Para reparar deliberadamente paquetes de un entorno existente, ejecuta `setup_training_env.ps1 -ForceReinstall`. El verificador exige `pkg_resources`, la tupla de `StrictVersion('1.5.0').version` y el chequeo de comunicación 1.5.0 de ML-Agents. Debe terminar en `TRAINING ENVIRONMENT OK`.

El reset deja cinco llamadas de acción sin movimiento controlado para que Unity actualice la pose articulada antes de fijar `initial_distance_m`. Se detectó que medirla inmediatamente después de `SetJointPositions` daba una distancia antigua y una recompensa positiva falsa; en la prueba corregida el primer episodio pasó de `+1.088` artificial a `−0.038` con la misma política heurística inmóvil.

## Run In Background y continuidad del Editor

En `ProjectSettings/ProjectSettings.asset`, `runInBackground` está activado. También lo activa `TrainingArenaSpawner.Awake()` durante el entrenamiento. En Unity compruébalo en **Edit → Project Settings → Player → Resolution and Presentation → Run In Background**. [Unity 6.3 documenta](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Application-runInBackground.html) que, si está desactivado, el Player se pausa al perder el foco. Esto reduce el riesgo de timeout al consultar PowerShell o TensorBoard, aunque no evita una pausa manual del Editor, suspensión de Windows, saturación de CPU, excepciones C# o tiempos de física excesivos.

## Run IDs: nuevo, continuar y sobrescribir

`trainingRunName` en `TrainingArenaSpawner` **debe coincidir** con el `RunId` de Python. Guarda `SampleScene` después de cambiarlo; `scripts/train.ps1` comprueba el valor serializado para evitar que `results/<run-id>/` y `TrainingLogs/<run-id>/` se mezclen. La escena preparada usa `franka_reach_v2`: el run v1 contiene datos anteriores a la corrección del reset y del time scale.

```powershell
# Nuevo run: la escena ya tiene trainingRunName = franka_reach_v2.
.\scripts\train.ps1 -RunId franka_reach_v2

# Continuar v2 tras crear un checkpoint, conservándolo:
.\scripts\train.ps1 -RunId franka_reach_v2 -Resume

# Solo para sobrescribir deliberadamente los resultados de ese run:
.\scripts\train.ps1 -RunId franka_reach_v2 -Force
```

`-Resume` exige un `checkpoint.pt`. Sin `-Resume` ni `-Force`, el script rechaza un Run ID ya presente. `-Force` permite a ML-Agents sobrescribir `results/<run-id>`; el script no borra `TrainingLogs/`. El script usa `--timeout-wait=120` segundos para dar margen a una respuesta lenta del Editor; se puede ajustar con `-TimeoutWait` entre 10 y 600. No atribuyas a ese margen una corrección de un Editor detenido. En condiciones normales utiliza `-Resume`.

Los comandos equivalentes de ML-Agents son `mlagents-learn .\trainer_config.yaml --run-id=franka_reach_v2`, el mismo con `--resume`, y el mismo con `--force`. Para continuar el histórico `franka_reach_v1`, cambia y guarda primero `trainingRunName` a ese valor en el Inspector; después usa `--run-id=franka_reach_v1 --resume`. No combines sus checkpoints ni sus CSV con v2, que tiene un reset y una escala temporal corregidos.

### Parar y guardar `franka_reach_v2`

1. En la terminal donde corre `train.ps1`, pulsa **Ctrl+C una sola vez**.
2. Espera el mensaje de interrupción, la exportación del modelo y el regreso al prompt de PowerShell. ML-Agents guarda el modelo al gestionar `KeyboardInterrupt`.
3. **Después** detén Play en Unity.

El checkpoint de este run se encuentra en `results/franka_reach_v2/FrankaLift/checkpoint.pt`. En la sesión del 6 de octubre de 2026 ya existía uno creado en torno a los 500 000 pasos. Comprueba su fecha después de parar:

```powershell
Get-Item .\results\franka_reach_v2\FrankaLift\checkpoint.pt |
  Select-Object Length, LastWriteTime
```

### Reanudar exactamente el mismo modelo `v2`

Mantén `stage = Reach` y `trainingRunName = franka_reach_v2` en la escena guardada. En una nueva terminal:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train.ps1 -RunId franka_reach_v2 -Resume
```

Cuando aparezca `Listening on port 5004`, pulsa **Play** en Unity. `-Resume` carga el checkpoint y continúa el mismo run; **no uses `-Force`** para reanudar porque sobrescribe sus resultados. No arranques una segunda instancia del entrenador mientras la primera siga ejecutándose.

### Resultado observado de Reach fijo en `v2`

La salida y los CSV de la sesión mostraron, en torno a 560 000 pasos, `Franka/Success` de 0 a 1 y `Franka/MinimumDistance` de 0.455 a 0.0674 m. En los últimos 1 600 episodios examinados, las 16 arenas alcanzaron el umbral de 0.07 m en sus 100 episodios respectivos. La recompensa media pasó de −0.948 a aproximadamente 2.283 y luego se estabilizó. Esto respalda que la política resuelve **Reach con cubo fijo en la configuración entrenada**; todavía no valida Lift, agarre ni posiciones nuevas del cubo. El checkpoint y los eventos de TensorBoard están excluidos de Git por su tamaño, por lo que estas cifras describen la ejecución local y no se reproducen directamente con un checkout limpio.

## CSV de entrenamiento

El logger único, compartido por todas las arenas, crea `TrainingLogs/<trainingRunName>/` al entrar en Play. Añade a `episodes.csv` **una fila por episodio y arena**; el `arena_id` va de 0 a `numberOfArenas - 1`. `arena_episode` cuenta episodios de esa arena dentro de la sesión de Play; `global_episode` continúa desde la última fila del mismo run. `session_id` distingue las entradas de Play y permite filtrar cuando se reanuda un run. `episode_steps` cuenta llamadas a `OnActionReceived`, incluidos los pasos en los que `DecisionRequester` repite la acción entre decisiones.

`episodes.csv` contiene, en este orden: `timestamp_utc,session_id,run_name,stage,arena_id,arena_episode,global_episode,episode_steps,cumulative_reward,success,terminal_reason,initial_distance_m,minimum_distance_m,final_distance_m,maximum_cube_height_m,final_cube_height_m,reached_cube,gripper_close_attempts,cube_random_x_m,cube_random_z_m,number_of_arenas,time_scale`. `terminal_reason` distingue `reach_success`, `lift_success`, `timeout`, `cube_fell`, `invalid_robot_state` y `manual_stop`. Los CSV usan UTF-8, una sola cabecera y decimales con punto (`InvariantCulture`). Los valores no finitos se sustituyen por 0 para mantener un CSV numérico; un episodio con estado inválido se marca como tal.

`summary.csv` agrega por defecto cada 100 episodios terminados: `timestamp_utc,session_id,run_name,global_episode,episodes_in_window,mean_reward,success_rate,mean_episode_length,mean_minimum_distance,mean_final_distance,mean_maximum_height,number_of_arenas,time_scale,stage`. La ventana parcial se guarda al detener Play correctamente. Ajusta `summaryWindowEpisodes` en el Inspector.

`steps.csv` es opcional. Activa `detailedStepLogging` y define `stepLogEveryNDecisions` (por defecto 10) para registrar una fila cada N **decisiones**, no cada `FixedUpdate`. Contiene `timestamp_utc,session_id,run_name,arena_id,episode,episode_step,stage,distance_tcp_cube,cube_height,instant_reward,cumulative_reward,gripper_opening,action_0,action_1,action_2,action_3,action_4,action_5,action_6,action_gripper`. Por defecto está desactivado para no cargar 16 arenas con datos de diagnóstico.

`StreamWriter` permanece abierto con búfer de 64 KiB; `flushEveryEpisodes` vale 32 y se hace `Flush/Close` al terminar Play. No hay escritura a disco por `FixedUpdate`. El resumen de ventana y las métricas `Franka/Success`, `Franka/MinimumDistance` y `Franka/MaximumHeight` del `StatsRecorder` se conservan además en TensorBoard; el CSV no sustituye los event files.

Comprueba la estructura, los IDs y los contadores de un run CSV así:

```powershell
.\.venv\Scripts\python.exe .\tools\validate_training_logs.py --run-id arena_benchmark_16 --expected-arenas 16
```

## Exportar TensorBoard a CSV

Con el mismo `venv`, ejecuta:

```powershell
.\.venv\Scripts\python.exe .\tools\export_tensorboard_csv.py --run-id franka_reach_v1
```

El script recorre `results/franka_reach_v1/`, lee todos los scalars y escribe `TrainingLogs/franka_reach_v1/tensorboard_metrics.csv` con `run_id,metric,step,wall_time,value`. Incluye, cuando existen, `Environment/Cumulative Reward`, `Environment/Episode Length`, `Franka/Success`, `Franka/MinimumDistance` y `Franka/MaximumHeight`; conserva los event files originales. El run real existente produjo 8 muestras scalar de tres event files, todas las métricas principales solo a 10 000 pasos. Una muestra no permite concluir que Reach aprenda.

## Benchmark 1/2/4/8/16 arenas y Time Scale

La herramienta **Tools → PickPlaceRL → Validar referencias 1-2-4-8-16** comprueba en el prefab referencias internas exclusivas, posiciones locales iguales, siete articulaciones, dos dedos, TCP, cubo, 23 observaciones, ocho acciones, `FrankaLift`, `Decision Period = 5` y ausencia de controladores manuales. **Tools → PickPlaceRL → Benchmark de arenas 1-2-4-8-16** ejecuta cinco Play Mode de 15 segundos a `timeScale = 1` y escribe `TrainingLogs/arena_benchmark.csv` con `number_of_arenas,time_scale,duration_seconds,episodes_completed,episodes_per_second,agent_action_calls_per_second,errors,editor_search_errors,stable,active_agents,local_references,run_in_background`. En esa prueba se fija temporalmente `MaxStep = 100` para provocar resets y validar los CSV; al terminar restaura la configuración serializada de la escena.

| Arenas | Episodios/s | Acciones de agente/s | Errores del agente | Excepciones del indexador de Unity |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 0.467 | 49.332 | 0 | 1 |
| 2 | 0.933 | 98.665 | 0 | 1 |
| 4 | 1.867 | 197.863 | 0 | 1 |
| 8 | 3.733 | 395.702 | 0 | 1 |
| 16 | 7.466 | 790.347 | 0 | 1 |

Las cinco pruebas acabaron con `stable = 1`, `active_agents` igual al número solicitado, referencias internas válidas y `run_in_background = 1`. La excepción `UnityEditor.Search.SearchDatabase` es conocida del Editor y se registra por separado; no apareció en el agente. **16 es la configuración recomendada en este PC a `timeScale = 1`** porque mantuvo el ritmo físico, multiplicó las muestras simuladas y se probó con PPO conectado; falta comparar ese PPO conectado con los otros cuatro recuentos. Cada run CSV del benchmark contiene dos sesiones: la inicial, que reveló el problema de la distancia de reset, y la corregida. Filtra por el `session_id` más reciente para analizar el comportamiento actual. La herramienta **Validar CSV detallado y randomización con 2 arenas** comprueba el modo opcional de pasos y los desplazamientos X/Z sin cambiar la configuración guardada.

Las llamadas de acción por segundo del benchmark miden capacidad de simulación de Unity, **no** el throughput completo de PPO con Python. Si 16 arenas bajan esa tasa o provocan errores, usa el recuento más rápido y estable. No aumentes `timeScale` solo por acelerar el reloj simulado: comprueba muestras por segundo real y estabilidad de la física. Empieza en 1 y sube gradualmente; vuelve a 1 si hay vibraciones, penetraciones o timeouts. Solo tras estabilizar el Editor merece la pena estudiar un build de entrenamiento probado con `mlagents-learn ... --env=<build>` y, si corresponde, `--no-graphics`.

### Prueba corta con PPO conectado

En una copia temporal del proyecto se conectó `mlagents-learn` a 16 arenas del Editor en batchmode con el código corregido. Durante 30.001 segundos reales se registraron 218 656 llamadas a `OnActionReceived` (7 288.388/s), 208 episodios terminados y 16 filas `manual_stop` al cerrar Play. `episodes.csv` contiene IDs 0–15 y `time_scale = 1` en todas sus filas; el `configuration.yaml` guardado por ML-Agents confirma `engine_settings.time_scale: 1.0`. El entrenador guardó un modelo de diagnóstico tras 43 648 pasos. Los archivos de la prueba están en `TrainingLogs/franka_reach_16_timescale1_smoke/`, `TrainingLogs/connected_smoke_timescale1.csv` y `results/franka_reach_16_timescale1_smoke/`. El exportador produjo 47 puntos scalar de TensorBoard y `validate_training_logs.py` aceptó las 224 filas, sus tres ventanas de resumen y los 16 IDs.

Una fila real de `episodes.csv`, con los nombres de columnas indicados arriba, es:

```csv
2026-10-06T17:20:53.1959185Z,20261006T172050031Z_d11db777,franka_reach_16_timescale1_smoke,Reach,0,1,1,1000,-0.406321377,0,timeout,0.552659571,0.469268739,0.505818963,-0.0244708881,-0.025000006,0,54,0,0,16,1
```

La prueba confirma conexión, producción de experiencia, logging y guardado; 30 segundos no permiten afirmar aprendizaje de Reach. Tampoco compara el PPO conectado entre 1, 2, 4 y 8 arenas. Se cerró Play deliberadamente para comprobar la reconexión: Python intentó reiniciar el worker sin repetir el `AttributeError` de `StrictVersion`. Al interrumpir el entrenador mientras esperaba una nueva conexión se observó un `UnityTimeOutException`; por eso el procedimiento normal es detener el entrenador y esperar el guardado antes de cerrar Play. El cambio de foco de un Editor con interfaz gráfica no se midió directamente; `Run In Background` se comprobó activo.

## Diagnóstico de errores observados

### `The Unity environment took too long to respond`

El aviso `[WARNING] Restarting worker[0] after 'The Unity environment took too long to respond...'` indica que Python no recibió respuesta de Unity a tiempo. En el proyecto `runInBackground` estaba a 0; perder el foco es una causa plausible, pero el registro disponible no demuestra que fuera la única. Comprueba que Play sigue activo, que Unity no está pausado, que Windows no suspende el equipo y que la consola de Unity no muestra excepciones. Repite primero con una arena y `timeScale = 1`; después examina CPU, GC y el benchmark antes de aumentar arenas. El logger escribe por episodio, de modo que no introduce E/S en cada paso.

### `StrictVersion` durante el reinicio del worker

Tras el timeout, ML-Agents intenta reiniciar el worker y en la comprobación de versiones se observó `AttributeError: 'StrictVersion' object has no attribute 'version'`. Son **dos fases distintas**: el fallo de compatibilidad ocurre al intentar reconectar; no explica el timeout previo. La rama `release/4.1.0` usa `distutils.version.StrictVersion` y accede a `.version`. En el `venv` actual con setuptools 80.9.0, `StrictVersion('1.5.0').version` devuelve `(1, 5, 0)`, `pkg_resources` importa y `_check_communication_compatibility('1.5.0', ...)` devuelve `True`. Una prueba aislada con setuptools 84.0.0 mostró que `.version` también funciona allí, pero `pkg_resources` ya no está disponible. Por eso se fija **80.9.0** para cubrir ambos requisitos; el AttributeError histórico no se reproduce ahora y no se ha parcheado `site-packages` ni `.mlagents-src`. Si reaparece, ejecuta `check_training_environment.py` desde la misma terminal que inicia `mlagents-learn` y conserva el traceback completo para comparar el intérprete y los imports efectivos.

### Otros problemas frecuentes

- **Python 3.13:** usa el intérprete 3.10.12 local y el `venv` del proyecto. El rango de la rama es 3.10.1–3.10.12.
- **`ModuleNotFoundError: pkg_resources`:** ejecuta `setup_training_env.ps1 -ForceReinstall` y `check_training_environment.py`; el pin verificado es setuptools 80.9.0.
- **Run ID ya existente:** usa `-Resume` o elige un Run ID nuevo y actualiza `trainingRunName`; reserva `-Force` para sobrescribir deliberadamente.
- **Unity no conecta:** inicia primero el entrenador (puerto 5004), después Play; confirma `Behavior Name = FrankaLift`, `Behavior Type = Default`, sin modelo y que el firewall permite la conexión local.
- **Al cambiar de ventana se detiene:** comprueba las dos opciones de Run In Background, que el Editor no esté pausado y que Windows no haya suspendido la sesión.
- **TensorBoard no puede usar 6006:** el puerto está reservado por Windows en este equipo; usa `--port 6007`, que se verificó con HTTP 200.

## Plan histórico tras V2

Reach con cubo fijo ya presenta varias ventanas con `Franka/MinimumDistance` baja y `Franka/Success = 1`. Las ideas de inicializar Lift desde V2 y randomizar el cubo pertenecen a ese plan histórico. El run anterior `franka_pickplace_v3` se conserva con su base aleatoria y 35 observaciones; la variante actual empieza un run independiente con base fija.

## V3 - Pick and Place

### Diagnóstico que motiva Lift36

En el run anterior `franka_pickplace_v3_fixed30`, el análisis de 673 episodios terminados mostró 669 `timeout`; en los últimos 100, Reach fue 99/100 y `was_grasped` 97/100, pero Lift fue **0/100**. La máxima elevación del cubo de esos últimos 100 episodios fue 0.0239 m y el máximo en los 673 fue 0.0553 m. Hubo aproximadamente 121 transiciones de cierre y 121 de apertura por episodio de 2500 acciones. `was_grasped` era entonces un contacto bilateral instantáneo: no probaba agarre sostenido. La recompensa media reciente, aproximadamente 0.65, podía subir sin completar Lift. Los datos locales de ese run se eliminaron durante la limpieza de V3.

Lift36 registró la duración del contacto y la altura del cubo **mientras** había contacto bilateral. Ese run se descartó; la variante Safe50 conserva la medida cinemática y la regularización suave, descritas al final de este documento.

V3 usa `Assets/Scenes/PickPlaceV3.unity`, el prefab `Assets/Prefabs/TrainingArenaV3.prefab`, el agente `FrankaPickPlaceAgentV3` y una sola política, `FrankaPickPlaceV3`. V2, Safe50, PlaceFix1/2/3/4 y Prism V1 se conservan como baseline. La escena activa está preparada para `franka_pickplace_v3_prism_v3`; su entrenamiento aún no se ha iniciado.

El objeto activo es un prisma rectangular de **0.035 × 0.05 × 0.07 m** (X/Y/Z), con la misma masa de 0.1 kg y el mismo `BoxCollider` centrado que el cubo anterior de 0.05 m por lado. Restaura en cada episodio la posición local fija `(-0.40, 0.10, 0)` antes de asentarse, pone ambas velocidades a cero y recibe un yaw nuevo dentro del rango del nivel de orientación activo. Cada arena toma su propia muestra. La raíz del Franka vuelve **siempre** a `(0.40, 0.05, 0)` en coordenadas locales de la arena mediante `ArticulationBody.TeleportRoot`, sin variación de X, Y, Z ni rotación. Se restauran Home, drives, velocidades articulares y pinza abierta. El agente deja cinco llamadas de acción para asentamiento físico antes de medir las distancias iniciales. Los offsets X/Z siguen en los CSV por compatibilidad del esquema, pero valen siempre cero. Los estados serializados anteriores se conservan en `docs/reference_placefix4_cube/` y `docs/reference_prism_v1/`.

`PlaceTarget` es un disco verde, fino, con `Collider.isTrigger`, que no forma un obstáculo físico. Su centro está **0.18 m en +Z local** respecto al cubo: la izquierda del Franka mirando hacia el cubo, elegida para V3. Está sobre la mesa. El radio de aceptación del centro del cubo es 0.04 m; el disco visual tiene 0.08 m de diámetro. Las arenas están separadas 5 m y el spawner crea una cuadrícula de **10 × 5 = 50 desde el primer Play**, con `timeScale = 1` y `Run In Background` activo.

![Vista superior medida de una arena V3](docs/v3_target_layout.svg)

### Observaciones y acciones

Hay **35 observaciones** vectoriales en este orden. Las primeras 33 conservan exactamente el orden y la escala de PlaceFix4; se quitaron las dos observaciones constantes del offset de base antes de ese baseline:

| Índices | Contenido |
| --- | --- |
| 0–6 | Ángulos de `link1` a `link7`, normalizados con los límites de cada drive |
| 7–13 | Velocidades articulares de `link1` a `link7`, divididas por `jointSpeed` y recortadas a [−1, 1] |
| 14–16 | TCP → cubo, X/Y/Z locales de la arena, en metros |
| 17–19 | Cubo → target, X/Y/Z locales, en metros |
| 20 | Altura del cubo sobre su posición asentada inicial, en metros |
| 21 | Apertura de pinza normalizada [0, 1] |
| 22–24 | Velocidad lineal del cubo, X/Y/Z locales, en m/s |
| 25–27 | Velocidad angular del cubo, X/Y/Z locales, en rad/s |
| 28–32 | Fase `Reach, Grasp, Lift, Transport, Place` en one-hot |
| 33 | `sin(2 × yaw)` del eje longitudinal actual del prisma |
| 34 | `cos(2 × yaw)` del eje longitudinal actual del prisma |

Las **8 acciones continuas** son incrementos del target de `link1` a `link7` (0–6), limitados por `ArticulationDrive` y `jointSpeed = 35°/s`; el índice 7 cierra la pinza con valor ≤−0.25, la abre con valor ≥+0.60 y conserva la orden anterior entre ambos umbrales. La histéresis anterior abría ya en +0.25; los CSV Safe50 mostraron que muchos agarres confirmados duraban pocas decisiones y acababan con la pinza abierta. El nuevo umbral reduce esa sensibilidad sin bloquear la apertura voluntaria, ni alterar las 33 observaciones o las 8 acciones. `DecisionRequester` pide una decisión cada cinco pasos físicos y repite acciones entre decisiones. En modo Heuristic sin Python: `1/2`, `3/4`, `5/6`, `Q/E`, `A/D`, `Z/C`, `R/F` controlan los siete joints y **Espacio** cierra la pinza. Para intentarlo manualmente, usa una arena, pulsa Play y enfoca la vista Game.

### Secuencia, recompensa y éxito

El episodio termina con `MaxStep = 1000`. ML-Agents incrementa `StepCount` en cada paso físico y ejecuta la acción antes de comprobar ese límite; con `Time.fixedDeltaTime = 0.02`, son aproximadamente 20 segundos simulados y 200 decisiones de política (periodo 5, con acciones repetidas). Más del 99 % de los hitos recientes Reach, Grasp y Lift de 1 cm ocurrieron antes del paso 1000. Se eliminó el timeout explícito de 60 segundos; `Time.fixedTimeAsDouble` solo mide la duración para el CSV. Cada paso conserva el coste −0.0005. Al llegar a 1000 sin éxito, se registra `timeout` y una sola penalización terminal de −1.00: el coste temporal total es aproximadamente −1.50, como antes en 60 segundos. Las recompensas Reach, Grasp y Lift, los hitos y la histéresis de pinza no cambian.

El éxito final de Pick and Place exige Reach, Grasp, **Lift físico de al menos 0.30 m**, entrada al target y liberación allí. Después el centro del cubo debe estar a ≤0.04 m horizontal del target, a ≤0.025 m de la altura de reposo, con pinza abierta, velocidad lineal ≤0.04 m/s y angular ≤0.5 rad/s, durante **12 decisiones consecutivas**. Empujarlo o deslizarlo sin levantarlo no satisface el criterio. Las razones terminales incluyen `place_success`, `curriculum_lift_success`, `timeout`, `cube_fell`, `cube_out_of_bounds`, `invalid_robot_state` y `manual_stop`.

### PPO y comandos

#### Prism V3: práctica de orientaciones difíciles (preparado; PPO pendiente)

Prism V2 completó 6.337 episodios en el CSV examinado. En los últimos 1.000, Reach y Grasp fueron 100 %, Lift30cm 94,8 % y ControlledPlace 89,5 %. Por yaw módulo 180°, Lift fue 81,7 % en 30°–45°, 99,5 % en 45°–135° y 82,2 % en 135°–150°. Estos datos motivan practicar más los dos bordes; no prueban aún una mejora de V3.

En `OnEpisodeBegin`, cada arena y episodio toma su propia muestra. Con `prism_yaw_level: 1` y `prism_focus_probability: 0.50`, la mitad de los episodios escoge con igual probabilidad yaw uniforme 30°–45° o 135°–150° (`focused`); la otra mitad escoge yaw uniforme 30°–150° (`uniform`). Luego suma 0° o 180° con igual probabilidad. El yaw final queda entre 30° y 330°, dentro de 0°–360°. Así, cada borde concentra **31,25 %** de los episodios y ambos **62,5 %**; con muestreo uniforme, ambos sumarían 25 %. Cambia solo `value` bajo `prism_focus_probability` en `trainer_config_v3_prism_v3.yaml` para ajustar la mezcla entre 0 y 1. El valor por defecto en C# cuando no llega ese parámetro es 0, de modo que la configuración V2 histórica sigue siendo uniforme. Los niveles 2 (15°–165°) y 3 (0°–180°) siguen disponibles para cambio manual y usan siempre muestreo uniforme; no hay promoción automática.

Se mantienen las **35 observaciones** (33 anteriores más `sin(2*yaw)` y `cos(2*yaw)`), **8 acciones continuas**, todas las recompensas incluido el máximo de alineación **+0,20**, la física del prisma de 3,5 × 5 × 7 cm y 0,1 kg, base y posición inicial fijas, 50 arenas, `timeScale = 1`, `MaxStep = 1000` y umbrales de pinza CLOSE ≤−0,25 / OPEN ≥+0,60. `PickPlace30cm` usa `lift_goal_m = 0.30`; PPO conserva los hiperparámetros de V2 y dispone de 4.000.000 pasos nuevos.

La transferencia preparada proviene de `results/franka_pickplace_v3_prism_v2/FrankaPickPlaceV3/checkpoint.pt`, **paso 563.640**, SHA-256 `4f2e43529b79fec100a8d39951dbd815362c6d8f1bb91d51c59e0ef31e0b63b7`. `tools/prepare_prism_v3_transfer.py` guardó una instantánea en `results/franka_pickplace_v3_prism_v3_transfer/FrankaPickPlaceV3/checkpoint.pt`: actor, critic y normalizadores son copias exactas; el contador arranca en 0 y Adam es nuevo. Se comprobaron carga estricta, igualdad de pesos y salidas, forma 35/8 y el cargador nativo de ML-Agents. `transfer_manifest.json` registra paso y hash. Si V2 sigue entrenando, esta transferencia permanece fijada en ese checkpoint. La escena, prefab, agente, logger, YAML y script originales de V2 están en `docs/reference_prism_v2/`; los resultados y CSV de V2 siguen intactos.

`episodes.csv` de V3 añade `orientation_sampling_mode` (`focused` o `uniform`) y `orientation_focus_probability` (valor configurado por episodio); conserva `orientation_curriculum_level`, yaw, alineación y demás columnas. `summary.csv` y `episode_events.csv` conservan sus campos. El análisis por ángulo se obtiene con:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\.venv\Scripts\python.exe .\tools\report_prism_orientation.py
```

El informe ofrece Lift, ControlledPlace, alineación en el primer Grasp y recuento por sectores de 15° y 5°, ventanas recientes (últimos 1.000 y los 1.000 anteriores), y separa `focused`/`uniform`. Además estima Lift y ControlledPlace para yaw uniforme 30°–150° mediante media de las ocho tasas de sectores de 15°; si falta algún sector, muestra `n/a`. Es una estimación descriptiva de los episodios muestreados, no una evaluación independiente. Para examinar V2 como referencia, pasa su CSV como argumento posicional. Se omiten los `manual_stop`.

Antes de considerar el nivel 2, pide **al menos 100 episodios recientes en cada borde de 15°** y **≥85 % Lift en cada uno**. Revisa también 30°–35° y 145°–150°, el centro 45°–135°, ControlledPlace y la alineación del primer agarre. El Lift global de V3 queda sesgado por la nueva distribución y no debe decidir por sí solo el avance.

Para el primer entrenamiento, abre y guarda `Assets/Scenes/PickPlaceV3.unity` en Unity, ejecuta en PowerShell y, cuando ML-Agents escuche en el puerto 5004, pulsa **Play**:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_prism_v3.ps1 -TimeoutWait 600
```

Para parar, pulsa **Ctrl+C una vez** en la terminal del entrenador, espera a que vuelva el prompt y se guarde el checkpoint, y después detén Play. Para reanudar el mismo run, con su checkpoint propio:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_prism_v3.ps1 -Resume -TimeoutWait 600
```

Estos comandos están documentados para el siguiente entrenamiento; la preparación de V3 no ha iniciado PPO.

#### Prism V2: orientación y agarre (histórico)

En 2.909 episodios completos de Prism V1, Lift30cm fue **64,1 %**. El desglose del CSV muestra **0,5 %** de Lift con yaw módulo 180° entre 0° y 15° (215 episodios), **0,8 %** entre 165° y 180° (236 episodios) y **99,0 %** entre 45° y 135° (1.464 episodios). La dificultad está concentrada en los extremos. Los Lift logrados en esos sectores indican que el agarre no es absolutamente imposible; no demuestran todavía robustez ni que cualquier postura sea alcanzable.

En el prefab, los dos `ArticulationBody` de los dedos son prismáticos sobre sus ejes X locales; el derecho está girado aproximadamente 180° respecto al izquierdo, así que sus desplazamientos se oponen. Cada drive tiene límites −0,04…0 m; el target abierto es −0,04 m y el de cierre actual −0,014 m. La separación nominal de recorrido es 0,08 m, mayor que los 0,07 m del lado largo del prisma. Los colliders de dedos y prisma están activos y no son triggers. El TCP está fijado a la mano; `joint7` admite aproximadamente ±172,8°, pero la orientación y posición efectiva de la pinza dependen de la cadena de siete articulaciones. **No se presupone que mover solo joint7 resuelva el agarre.** No se cambió geometría, masa, fricción ni histéresis de pinza.

Prism V2 conserva exactamente **35 observaciones** y **8 acciones continuas**. El yaw real sigue codificado en las observaciones 33 y 34 con `sin(2*yaw)` y `cos(2*yaw)`. `prism_yaw_level` es un parámetro de entorno de ML-Agents con **una sola lección manual** en `trainer_config_v3_prism_v2.yaml`. No hay avance automático por reward o Lift global, porque ese promedio ocultaría los fallos en los extremos.

| Nivel | Yaw módulo 180° | Bordes a revisar por separado |
| --- | --- | --- |
| 1 | 30°–150° | 30°–45° y 135°–150° |
| 2 | 15°–165° | 15°–30° y 150°–165° |
| 3 | 0°–180° | 0°–15° y 165°–180° |

En cada episodio, cada arena toma un yaw continuo uniforme del intervalo del nivel y suma al azar 0° o 180°. El nivel 3 recupera la distribución uniforme de 0° a 360°. La posición inicial sigue fija. `episodes.csv` añade `orientation_curriculum_level`, `orientation_yaw_min_deg` y `orientation_yaw_max_deg` junto al `initial_object_yaw_deg` ya existente.

La pequeña señal de alineación usa el **vector real entre los transforms de los dos dedos** y `cubeTransform.right` (eje X estrecho actual del prisma), ambos proyectados al plano horizontal local de la arena y normalizados. Su métrica es `a = abs(dot(ejeDedos, ejeEstrecho))`, en [0,1]. Solo se evalúa en decisiones con Reach alcanzado, TCP a ≤0,07 m, orden activa de cierre, antes del primer Grasp confirmado y sin alejarse del prisma. No depende del flag histórico `graspAttempted`: en V1 la pinza ya estaba cerrada antes de entrar en su radio en 1.834 de 2.908 episodios con Grasp confirmado. La primera alineación elegible fija la referencia; cada nuevo máximo `a` da `0,20 × (a_nuevo − a_mejor_anterior)`. Al ser un máximo monótono, la suma por episodio es **≤+0,20** y no se vuelve a cobrar por oscilación ni por esperar. No se modificaron las recompensas Reach, Grasp, Lift, Transport, PrePlace, Release ni Place. El CSV añade `finger_short_axis_alignment_at_first_grasp`, `reward_alignment_total` y `grasp_to_first_physical_loss_steps`; este último cuenta desde Grasp confirmado hasta la primera acción sin agarre físico, incluso si el contacto se recupera antes de la confirmación de pérdida.

El origen es `results/franka_pickplace_v3_prism_v1/FrankaPickPlaceV3/checkpoint.pt` en el paso **363.960** al preparar esta versión. Actor, critic, normalizadores y acciones son compatibles directamente porque ambos runs usan 35/8. Sin embargo, el `--initialize-from` de ML-Agents 1.2.0.dev0 carga también el estado Adam antiguo. Para iniciar el contador y Adam de forma limpia, `tools/prepare_prism_v2_transfer.py` copió **sin alterar** todos los tensores de actor y critic a `results/franka_pickplace_v3_prism_v2_transfer/FrankaPickPlaceV3/checkpoint.pt`, puso step 0 y creó Adam nuevo. Se verificaron carga estricta, igualdad exacta de tensores y salidas, y el cargador nativo que utiliza `--initialize-from`. `transfer_manifest.json` fija paso y hash SHA-256 del origen. Esta transferencia es una instantánea del paso 363.960; si Prism V1 continúa, sus checkpoints posteriores no alteran la instantánea preparada.

`trainer_config_v3_prism_v2.yaml` conserva PPO e hiperparámetros de V1, `PickPlace30cm` con `lift_goal_m = 0.30` y presupuesto `max_steps = 4000000`. La escena histórica tenía 50 arenas a `timeScale = 1`; el prefab mantenía `FrankaPickPlaceV3`, `MaxStep = 1000`, 35 observaciones y 8 acciones. El Run ID y los CSV históricos son `franka_pickplace_v3_prism_v2`. Una copia de escena, prefab, agente, logger, YAML y script de V1 está en `docs/reference_prism_v1/` para reproducir aquella configuración. Sus checkpoints y CSV permanecen intactos.

El primer inicio histórico de Prism V2 usaba el siguiente comando con su escena de referencia. La escena activa ya está preparada para Prism V3:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_prism_v2.ps1 -TimeoutWait 600
```

Para continuar aquel run habría que restaurar su escena, agente y logger desde `docs/reference_prism_v2/` antes de ejecutar:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_prism_v2.ps1 -Resume -TimeoutWait 600
```

Para revisar los sectores históricos de V2 con el informe actualizado, ejecuta `.\.venv\Scripts\python.exe .\tools\report_prism_orientation.py .\TrainingLogs\franka_pickplace_v3_prism_v2\episodes.csv`. El avance de nivel siempre requiere una decisión manual; el nivel se lee al comienzo de cada episodio.

#### Baseline PlaceFix4 (cubo anterior)

PlaceFix3 mantenía la racha de tres decisiones de retención al cruzar Transport → TargetAligned → PrePlace, pero el bucle de hitos actualizaba `previousReleaseHeightError` antes de comparar el umbral y bloqueaba todos los hitos. PlaceFix4 concede cada hito 30/25/20/15/10/5 cm una sola vez cuando coinciden Lift completado, cubo sujeto y alineado ≤0.04 m, tres decisiones continuas de retención, sin release activo, velocidad vertical > −0.40 m/s y altura ≤ umbral; también funciona si la primera evaluación elegible ya está bajo el umbral. Conserva los importes de recompensa y la detección de liberación. `episodes.csv` registra el primer instante elegible y las rachas en la alineación y elegibilidad; `episode_events.csv` añade un único evento `preplace_eligible` por episodio con altura, distancia horizontal, velocidad, racha y estado de pinza.

`franka_pickplace_v3_placefix4` empezó directamente en PickPlace30cm (`lift_goal_m = 0.30`) con los pesos del `checkpoint.pt` más reciente de PlaceFix3 mediante `--initialize-from=franka_pickplace_v3_placefix3`. Conserva 33 observaciones, 8 acciones, 50 arenas, `timeScale = 1`, `MaxStep = 1000` y `max_steps = 4000000`. Su checkpoint y sus CSV son la referencia del cubo anterior. **No reanudar PlaceFix4 con el prisma actual.**

El comando histórico de reanudación de PlaceFix4 requiere restaurar previamente el cubo y su escena. Su script comprueba el Run ID y rechaza la escena actual de Prism V3.

#### Prism V1 (histórico; 363.960 steps al preparar V2)

El prisma mide 3,5 × 5 × 7 cm (X/Y/Z), conserva la masa de 0,1 kg y aparece en posición fija con yaw uniforme de 0° a 360° por episodio y por arena. `CollectObservations()` toma **el `Transform.forward` actual** (eje local +Z) y lo pasa al marco de la arena, fijo respecto a la base del Franka. Calcula `yawRadians = atan2(forwardLocal.x, forwardLocal.z)` sobre la proyección horizontal; si la proyección no es válida, usa yaw 0. Añade `sin(2*yawRadians)` y `cos(2*yawRadians)` al final. Los pares de 0°/180° y 90°/270° son equivalentes, pero longitudinal y transversal difieren; la pareja es continua al cruzar 360°. Se conservaron las primeras 33 observaciones y las 8 acciones. El prefab declara 35 valores, 8 acciones continuas, `FrankaPickPlaceV3` y `MaxStep = 1000`; la escena declara 50 arenas a `timeScale = 1`.

El último `results/franka_pickplace_v3_placefix4/FrankaPickPlaceV3/checkpoint.pt` registra el paso **4.000.182**. El ML-Agents local 1.2.0.dev0 y PyTorch 2.2.2+cpu no adaptan automáticamente una capa de entrada de 33 a 35: el cargador de `--initialize-from` captura el error de tamaño y podría seguir con partes sin inicializar. `tools/prepare_prism_v1_transfer.py` construyó un checkpoint nuevo en `results/franka_pickplace_v3_prism_v1_transfer/FrankaPickPlaceV3/checkpoint.pt` sin tocar PlaceFix4. Copió exactamente los pesos compatibles de actor y critic, mantuvo las primeras 33 columnas de sus capas de entrada y puso a cero las dos nuevas. Copió las primeras 33 estadísticas de normalización; para las nuevas puso media 0 y varianza acumulada igual al número de muestras, equivalente a desviación típica inicial 1. El estado de Adam es nuevo, con learning rate inicial de 0,0003, y el contador del nuevo run empieza en 0. `transfer_manifest.json` anota el hash SHA-256 y el paso de origen.

La transferencia se validó con `load_state_dict(strict=True)` en actor y critic de 35 entradas, igualdad de los tensores conservados y de las salidas iniciales frente al modelo de 33 entradas, una pasada finita de inferencia y carga del optimizador nuevo. La configuración `trainer_config_v3_prism_v1.yaml` conserva los hiperparámetros de PlaceFix4 y comienza en `PickPlace30cm` (`lift_goal_m = 0.30`), con presupuesto total `max_steps = 4000000`. Las recompensas, colisiones, geometría, base fija, target y lógica de pinza no se cambiaron en aquella preparación. Prism V1 se entrenó después; su checkpoint del paso 363.960 sirvió como origen de Prism V2.

El primer inicio histórico de Prism V1 usó la configuración conservada en `docs/reference_prism_v1/`. **No ejecutes este comando con la escena y el agente actuales de Prism V2.**

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_prism_v1.ps1 -TimeoutWait 600
```

El script de V1 usaba `--initialize-from=franka_pickplace_v3_prism_v1_transfer` solo la primera vez. Sus resultados están en `results/franka_pickplace_v3_prism_v1/` y sus CSV en `TrainingLogs/franka_pickplace_v3_prism_v1/`; `episodes.csv` registra `initial_object_yaw_deg` del reset.

Para parar, pulsa **Ctrl+C una sola vez** en la terminal del entrenador, espera a que guarde `checkpoint.pt` y vuelva el prompt; después detén Play. Para continuar desde el checkpoint propio, con la misma escena:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_prism_v1.ps1 -Resume -TimeoutWait 600
```

`-Resume` usa exclusivamente `results/franka_pickplace_v3_prism_v1/FrankaPickPlaceV3/checkpoint.pt`. Si el primer intento se interrumpe antes de crear ese archivo, habrá que revisar los resultados parciales y elegir cómo recuperarlos; el script impide sobrescribirlos automáticamente.

#### Baseline PlaceFix3

En PlaceFix2 algunos hitos PrePlace se activaron después de perder el cubo: `holding` permitía un recontacto bilateral instantáneo durante la caída. PlaceFix3 conserva los rewards y los hitos 30/25/20/15/10/5 cm, pero exige retención continua de **tres decisiones**, contacto bilateral, pinza cerrada, TCP próximo, sin release activo, alineación horizontal ≤0.04 m y velocidad vertical del cubo **> −0.40 m/s**. El código de PlaceFix3 tenía un fallo en la comparación del umbral que impidió concederlos. Tras perderlo, un regrasp debe satisfacer de nuevo esas tres decisiones. Los eventos de PlaceFix2 muestran caída de hasta −1.2 m/s en los hitos falsos, frente a −0.26 m/s como mínimo en `target_aligned`.

PlaceFix3 exige también esa retención robusta para `release_ready`. Registra tres instantes separados: `release_command` al ordenar OPEN, `confirmed_grasp_loss` al validar dos acciones físicas seguidas sin contacto bilateral (anotando el primer instante sin contacto), y `release` cuando además la pinza está abierta ≥0.2 dentro de 15 acciones. Controlled release usa la altura de la primera pérdida física y exige que OPEN comenzara en `release_ready` tras retención continua. High release usa la altura de OPEN o de esa primera pérdida, nunca la altura tardía de confirmación. El CSV añade esos pasos, alturas, velocidades y estado de retención. Los premios +0.35 ReleaseReady, +0.50 controlled release, −0.50 high release, +2 Place físico y +8 Place controlado permanecen iguales.

`franka_pickplace_v3_placefix3` comienza directamente en PickPlace30cm (`lift_goal_m = 0.30`) con pesos del `checkpoint.pt` más reciente de PlaceFix2 mediante `--initialize-from=franka_pickplace_v3_placefix2`. Conserva 33 observaciones, 8 acciones, 50 arenas, `timeScale = 1`, `MaxStep = 1000` por episodio y `max_steps = 3000000` para el trainer. Para iniciarlo, guarda la escena y ejecuta:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_placefix3.ps1 -TimeoutWait 600
```

Cuando escuche en el puerto 5004, pulsa Play. Para detenerlo, Ctrl+C una vez en el entrenador, espera el guardado y detén Play. Para reanudar PlaceFix3: `.\scripts\train_v3_placefix3.ps1 -Resume -TimeoutWait 600`.

#### Baseline PlaceFix2 (configuración anterior)

Sus comandos históricos requieren volver a seleccionar su `trainingRunName`; la escena actual está preparada para Prism V3.

PlaceFix1 mejoró el éxito físico de Place, pero siguió soltando el cubo desde unos 30 cm: `release_ready` y `controlled_release` no aparecieron en unos 4735 episodios observados. PlaceFix2 arranca en un Run ID nuevo con `--initialize-from=franka_pickplace_v3_placefix1`, que toma su `checkpoint.pt` más reciente; el último verificado corresponde al paso 513808. Empieza directamente en `PickPlace30cm` (`lift_goal_m = 0.30`), conserva la arquitectura y los hiperparámetros PPO, y tiene `max_steps = 3000000`. Cada episodio mantiene `MaxStep = 1000`, con 33 observaciones, 8 acciones y 50 arenas fijas.

Tras Lift, la alineación horizontal ≤0.04 m con agarre físico habilita PrePlace. Una nueva mejor bajada recibe `8 ×` los metros de progreso. Al cruzar por primera vez 30/25/20/15/10/5 cm sobre el centro de apoyo esperado del cubo, todavía sujeto y alineado, se añaden respectivamente +0.10/+0.15/+0.20/+0.25/+0.30/+0.40. `release_ready` sigue exigiendo ≤0.035 m horizontal y altura entre −0.025 y +0.05 m, con +0.35 una vez. La apertura confirmada por pérdida física de contacto en esa zona da +0.50; soltar por encima de +0.05 m cerca del target da −0.50 una vez. El control y la histéresis de la pinza no cambian.

`place_success` sigue indicando que el cubo quedó físicamente estable en el target. En ese caso se conserva un bonus físico final de +2; `controlled_place_success` requiere además `release_ready` y una liberación física controlada correspondiente a esa colocación, y recibe otros +8. Se mantienen los premios físicos previos por altura, liberación en target y estabilidad. Por tanto el bonus final es +2 para un drop y +10 para un Place controlado. `episodes.csv` añade milestones, sus pasos, mejores alturas sujetas y la separación de reward físico/controlado; `summary.csv` añade tasas de milestones y de controlled Place; TensorBoard añade tasas PrePlace de 30/20/10/5 cm y `V3/ControlledPlaceSuccessRate`.

Abre y guarda `PickPlaceV3.unity`. En PowerShell:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_placefix2.ps1 -TimeoutWait 600
```

Cuando el entrenador escuche en el puerto 5004, pulsa Play. Para parar, pulsa Ctrl+C una vez en el entrenador, espera a que guarde y después detén Play. Para reanudar PlaceFix2: `.\scripts\train_v3_placefix2.ps1 -Resume -TimeoutWait 600`.

#### Baseline PlaceFix1 (configuración anterior)

Los comandos históricos de PlaceFix1 y Safe50 que siguen requieren volver a seleccionar su `trainingRunName` en la escena; la escena actual está preparada para Prism V3.

El nuevo run `franka_pickplace_v3_placefix1` comienza en el paso **0 del entrenador** con los pesos del `checkpoint.pt` más reciente de `results/franka_pickplace_v3_safe50/FrankaPickPlaceV3/`, mediante `--initialize-from=franka_pickplace_v3_safe50`. No se reanuda ni modifica Safe50. `trainer_config_v3_placefix1.yaml` conserva los hiperparámetros PPO y la arquitectura de Safe50, fija una única lección `PickPlace30cm` con `lift_goal_m = 0.30` desde el primer episodio y establece `max_steps = 3000000` para el run nuevo. El agente mantiene `MaxStep = 1000` por episodio, **33 observaciones**, **8 acciones**, 50 arenas fijas y `timeScale = 1`.

Tras Lift, `target_aligned` exige cubo físicamente sujeto y distancia horizontal cubo-target ≤0.04 m. `releaseHeightAboveTarget` es la Y local del centro del cubo menos la Y local esperada de su centro apoyado: superficie superior del collider del target más la semialtura vertical actual del collider del cubo. PrePlace recompensa solo nuevas mejoras de descenso mientras continúa alineado y sujeto: `3 ×` los metros de progreso vertical hacia esa altura, con marca de mejor aproximación para evitar oscilaciones. `release_ready` exige además distancia horizontal ≤0.035 m y altura relativa entre −0.025 y +0.05 m; da +0.35 una vez. Una apertura seguida de pérdida física del contacto bilateral y apertura de pinza ≥0.2 dentro de 15 acciones da +0.45 una vez si comenzó y terminó en esa zona; si se suelta a más de 0.05 m sobre ella, estando cerca horizontalmente, se aplica −0.25 una vez. El criterio final de Place estable y su recompensa +10 no cambian.

En PowerShell, abre y guarda `PickPlaceV3.unity`, ejecuta:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3_placefix1.ps1 -TimeoutWait 600
```

Cuando aparezca `Listening on port 5004`, pulsa **Play** en Unity. Para detenerlo, pulsa **Ctrl+C una sola vez** en la terminal, espera el guardado y después detén Play. Para reanudar el nuevo run, usa `.\scripts\train_v3_placefix1.ps1 -Resume -TimeoutWait 600`. `episodes.csv` incorpora hitos de alineación, preparación y release, geometría/velocidad/acción en la liberación y cuatro acumulados de reward; `episode_events.csv` añade como máximo un evento `target_aligned`, `release_ready` y `release`; `summary.csv` agrega tasas y medias de release; TensorBoard añade `V3/ReleaseReadyRate`, `V3/ControlledReleaseRate`, `V3/HighReleaseRate`, `V3/MeanReleaseHeight` y `V3/PlaceSuccessRate`.

#### Baseline Safe50 (configuración anterior)

`trainer_config_v3_safe50.yaml` usa PPO con lote 512, búfer **20480**, learning rate 0.0003, red **256 × 2**, horizonte **256**, límite total **7 millones** de pasos, resumen cada 10000 y checkpoint cada 250000. El mismo run `franka_pickplace_v3_safe50` se reanuda desde su checkpoint cercano a 5 millones mediante `-Resume`; este límite permite aproximadamente 2 millones de pasos adicionales. El parámetro de entorno `lift_goal_m` mantiene las lecciones **0.05 → 0.10 → 0.20 → 0.30 m**. Las tres primeras avanzan cuando, tras al menos 100 episodios, la recompensa media suavizada supera 2.0; el bono de +3 solo se obtiene al levantar físicamente hasta el objetivo. La lección final vuelve a la tarea completa de recoger, trasladar y dejar. Unity lee el objetivo al inicio de cada episodio, por lo que algunas arenas pueden terminar un episodio anterior después del cambio de lección. `engine_settings.time_scale = 1`.

Abre y guarda `PickPlaceV3.unity` en Unity. En PowerShell, desde la raíz del proyecto:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_v3.ps1 -Resume -TimeoutWait 600
```

El script comprueba el entorno Python, la escena con **50 arenas**, el config, el Run ID y el checkpoint. El run Safe50 actual conserva sus pesos, resultados y CSV. Para continuarlo ejecuta `-Resume` y, cuando aparezca `Listening on port 5004`, pulsa **Play** en Unity. Para parar: **Ctrl+C una sola vez** en la terminal del entrenador, espera a que guarde y vuelva el prompt; después detén Play. Para reanudar:

```powershell
.\scripts\train_v3.ps1 -Resume -TimeoutWait 600
```

`-Resume` exige `results/franka_pickplace_v3_safe50/FrankaPickPlaceV3/checkpoint.pt` del run nuevo. El script rechaza Run ID V3 antiguos. Para otro Run ID nuevo, cambia primero y guarda `trainingRunName` del spawner. `-Force` solo se acepta explícitamente y sobrescribe resultados de ese Run ID; no sirve para continuar.

En otra terminal, TensorBoard usa **6007** porque 6006 está ocupado:

```powershell
.\.venv\Scripts\tensorboard.exe --logdir .\results --port 6007
.\.venv\Scripts\python.exe .\tools\export_tensorboard_csv.py --run-id franka_pickplace_v3_safe50
.\.venv\Scripts\python.exe .\tools\validate_training_logs_v3.py --run-id franka_pickplace_v3_safe50 --expected-arenas 50
.\.venv\Scripts\python.exe .\tools\diagnose_lift_v3.py --run-id franka_pickplace_v3_safe50 --last 100
```

Abre [http://localhost:6007](http://localhost:6007). El exportador vuelca los scalar tags a `TrainingLogs/franka_pickplace_v3_safe50/tensorboard_metrics.csv` sin borrar los event files. TensorBoard recibe `Environment/Cumulative Reward`, `Environment/Episode Length` y las series de éxito, agarre, elevación y regularización detalladas más abajo. Compara Lift con el objetivo actual de la lección; un Reach alto con poco agarre sostenido apunta al contacto o la pinza.

### CSV V3

El logger compartido crea `TrainingLogs/<trainingRunName>/episodes.csv`: una fila por episodio y arena, `arena_id` de **0 a 49**, con `session_id` distinto cada entrada en Play. Incluye timestamp, run, episodio local/global, pasos, recompensa, éxito, razón terminal, arenas y escala temporal; offsets X/Z de la base —**ambos cero en la variante actual**— y coordenadas locales X/Y/Z efectivas de esa base; posición local X/Y/Z del spawn fijo del cubo y del target; distancias TCP-cubo inicial/mínima/final; altura inicial, máxima elevación y altura final; offset local X/Z del target; distancias cubo-target inicial/mínima/final; flags de cada fase; intentos de cerrar/abrir; pasos de los primeros hitos; decisiones estables; velocidades finales. Así cada fila contiene las posiciones necesarias para reconstruir el escenario sin depender de una versión futura del prefab. `cube_initial_height_m` es la Y local del centro del cubo **después del asentamiento**, mientras `cube_spawn_local_y_m` es la Y fija antes de asentarse; `maximum_cube_height_m` es la elevación sobre la primera.

Columnas de `episodes.csv`, en orden por grupos:

| Grupo | Columnas exactas |
| --- | --- |
| Identidad | `timestamp_utc`, `session_id`, `run_name`, `arena_id`, `arena_episode`, `global_episode`, `episode_steps`, `cumulative_reward`, `success`, `terminal_reason`, `number_of_arenas`, `time_scale` |
| Posición | `robot_base_offset_x_m`, `robot_base_offset_z_m`, `robot_base_local_x_m`, `robot_base_local_y_m`, `robot_base_local_z_m`, `cube_spawn_local_x_m`, `cube_spawn_local_y_m`, `cube_spawn_local_z_m`, `target_local_x_m`, `target_local_y_m`, `target_local_z_m` |
| Distancia y altura | `initial_tcp_cube_distance_m`, `minimum_tcp_cube_distance_m`, `final_tcp_cube_distance_m`, `cube_initial_height_m`, `maximum_cube_height_m`, `final_cube_height_m`, `target_offset_x_m`, `target_offset_z_m`, `initial_cube_target_distance_m`, `minimum_cube_target_distance_m`, `final_cube_target_distance_m` |
| Fases | `reached_cube`, `grasp_attempted`, `was_grasped`, `was_lifted`, `entered_target`, `released_in_target`, `placed_successfully` |
| Control | `gripper_close_attempts`, `gripper_open_attempts`, `step_reached_cube`, `step_first_grasp`, `step_first_lift`, `step_entered_target`, `step_success`, `stable_target_decisions`, `final_cube_linear_speed`, `final_cube_angular_speed` |
| Diagnóstico Lift36 | `lift_goal_m`, `bilateral_contact_actions`, `holding_actions`, `max_consecutive_holding_actions`, `maximum_held_cube_height_m`, `near_joint_limit_actions`, `minimum_joint_limit_margin_deg` |
| Diagnóstico Safe50 | `minimum_manipulability`, `mean_manipulability`, `final_manipulability`, `minimum_joint_limit_margin`, `maximum_joint_velocity`, `singularity_warning_count`, `joint_limit_warning_count` |

`summary.csv` agrega cada 100 episodios terminados: recompensa y longitud medias; tasas Reach/Grasp/Lift/TargetEntry/Place; distancias y altura medias; pasos medios hasta Reach, Lift y Place **solo entre episodios que alcanzaron ese hito**. `reach_samples`, `lift_samples` y `place_samples` explicitan esos denominadores. La ventana parcial se escribe al parar Play. `manual_stop` queda en `episodes.csv` pero no entra en las tasas. `steps.csv` está **desactivado** por defecto; al activar `detailedStepLogging`, registra cada 10 decisiones por defecto, con fase, distancias, altura, recompensa, offsets y ocho acciones. Los decimales CSV usan `InvariantCulture`.

El nuevo `episodes.csv` añade pasos de Reach, primer intento, primer contacto bilateral, agarre confirmado, primer movimiento vertical, altura máxima y Lift; apertura y distancia TCP-cubo en Reach, Grasp y altura máxima; contacto por dedo, retención y pérdida; velocidad vertical; márgenes normalizados y velocidades de cada uno de los siete joints; magnitud y saturación de acciones, y acumulados separados de rewards. `reward_accounted_total` suma Reach, Grasp, Lift, Transport, Place, singularidad, límite, velocidad, tiempo, fallo, pérdida de agarre y timeout; debe concordar con `cumulative_reward`. `episode_events.csv` guarda `reach`, `first_bilateral_contact`, `grasp_confirmed`, `max_height` definitivo, `lift_success` y, si ocurre, `first_grasp_loss`, con postura articular, manipulabilidad, margen, distancia, altura y pinza. `curriculum_lesson` y `lift_goal_m` identifican la lección de cada episodio. No hay logging por cada FixedUpdate.

La revisión anterior agregó a `episodes.csv` las acciones de pinza tras Grasp, la primera pérdida, los hitos de 1 a 5 cm, `episode_duration_sim_seconds` y `timeout_limit_seconds`. Esta revisión añade `reward_timeout_penalty_total` y `episode_max_step`; las filas históricas se conservan con esas dos celdas vacías. Las sesiones nuevas muestran `timeout_limit_seconds = MaxStep × Time.fixedDeltaTime`, actualmente 20.

Columnas de `summary.csv`: `timestamp_utc`, `session_id`, `run_name`, `global_episode`, `episodes_in_window`, `mean_reward`, `reach_rate`, `grasp_rate`, `lift_rate`, `target_entry_rate`, `place_success_rate`, `mean_episode_length`, `mean_min_tcp_cube_distance`, `mean_max_cube_height`, `mean_min_cube_target_distance`, `mean_final_cube_target_distance`, `mean_steps_to_reach`, `mean_steps_to_lift`, `mean_steps_to_place`, `reach_samples`, `lift_samples`, `place_samples`, `number_of_arenas`, `time_scale`; se añaden las medias de `lift_goal_m`, contacto bilateral, acciones sujetando, racha máxima de agarre, altura mientras se sujeta, acciones cerca de límites, margen mínimo articular, y en Safe50 `mean_minimum_manipulability`, `mean_minimum_joint_limit_margin`, `mean_maximum_joint_velocity`.

Las nuevas columnas de `summary.csv` son `lift_1cm_rate` a `lift_5cm_rate`, `mean_longest_grasp_retention_decisions`, `mean_grasp_lost_count`, `mean_gripper_open_action_rate_after_grasp`, `mean_reward_reach`, `mean_reward_grasp`, `mean_reward_lift`, `mean_episode_duration_sim_seconds` y `timeout_rate`. TensorBoard agrega `V3/Lift1cmRate` a `V3/Lift5cmRate`, `V3/MeanGraspRetention` y `V3/GraspLossRate`.

La comprobación mínima está en **Tools → PickPlaceRL → V3 → Comprobación corta de 50 arenas**. Las tablas siguientes son historia de desarrollo, no validación del reward actual. Sus carpetas de resultados y CSV experimentales se descartan en la limpieza; las rutas históricas citadas a continuación ya no existirán. Solo se conserva íntegro el run V2.

El benchmark Lift36 del 6 de octubre de 2026 compiló en Unity 6000.3.25f1 y probó cada tamaño durante 15 s reales a `timeScale = 1`, sin conectar PPO. Los seis escenarios acabaron estables, sin errores del agente y con referencias locales válidas. La excepción conocida de `UnityEditor.Search.SearchDatabase` apareció una vez por escenario y se contabilizó aparte. Cada timeout de la tabla es **forzado por `MaxStep = 100` del benchmark**, no indica fallo de aprendizaje. Los CSV de los seis escenarios pasaron `validate_training_logs_v3.py` y se copiaron a `TrainingLogs/v3_lift36_arena_benchmark_<N>/`.

| Arenas | Acciones de agente/s | Episodios/s | Timeouts forzados | Estable |
| ---: | ---: | ---: | ---: | :---: |
| 1 | 49.266 | 0.467 | 7 | Sí |
| 4 | 197.594 | 1.867 | 28 | Sí |
| 8 | 394.110 | 3.733 | 56 | Sí |
| 16 | 791.446 | 7.466 | 112 | Sí |
| 24 | 1182.322 | 11.199 | 168 | Sí |
| 36 | **1780.795** | **16.800** | 252 | **Sí** |

La subida de 24 a 36 mantuvo unas 49 llamadas de acción por segundo y arena en esta prueba de infraestructura. No mide la velocidad de PPO, el uso de GPU ni la tasa de éxito de Pick and Place. El entrenamiento real debe comprobar esos datos junto con `holding_actions`, `maximum_held_cube_height_m` y `lift_rate`.

En una copia temporal del proyecto, Unity 6000.3.25f1 compiló la primera versión V3 y verificó referencias internas con los cinco tamaños. **El siguiente benchmark, guardado en `TrainingLogs/v3_arena_benchmark.csv`, es histórico, previo a la base fija y al umbral de 0,30 m**; no describe el rendimiento ni valida Lift36. El benchmark Play Mode anterior del 6 de octubre de 2026, 15 s reales por tamaño a `timeScale = 1`, produjo:

| Arenas | Episodios/s | Acciones de agente/s | Timeouts | Errores del agente | Estable |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 0.467 | 49.266 | 7 | 0 | Sí |
| 4 | 1.867 | 197.862 | 28 | 0 | Sí |
| 8 | 3.733 | 395.686 | 56 | 0 | Sí |
| 16 | 7.466 | 790.355 | 112 | 0 | Sí |
| 24 | 11.199 | 1187.130 | 168 | 0 | Sí |

La excepción `UnityEditor.Search.SearchDatabase` del indexador del Editor apareció en aquel benchmark histórico y no procede del agente. Sus timeouts eran forzados con `MaxStep = 100`; la escena actual usa `MaxStep = 1000`. El benchmark no mide aprendizaje.

El probe físico de Unity usó las posturas calibradas Pick y PrePick y un giro de −12° de `link1`: el TCP quedó a **0.0063 m** del cubo en Pick, **0.0245 m** de la pose elevada sobre el target y **0.0228 m** de la pose de colocación. Las articulaciones respetaron sus límites, el target estaba dentro de la mesa y era trigger. En una ejecución manual con física automática el cubo llegó a elevarse **0.465 m** y ambos sensores de dedos detectaron contacto simultáneo. Sin embargo, tres intentos de una secuencia manual completa tocaron el cubo pero no lo conservaron al levantarlo, y no consiguieron depositarlo: el agarre y la colocación de extremo a extremo quedan **pendientes de validación**. El criterio puro de éxito rechazó empujar sin Lift, lanzar a velocidad alta, no soltar y pinza cerrada; aceptó una colocación estable. Eso verifica la lógica de la condición, no una manipulación física completa.

La conexión PPO de diagnóstico **de la versión anterior con base aleatoria** y 24 arenas durante 31.256 s de Play produjo **240 024 llamadas de acción** (7679.349/s), 96 episodios terminados y 24 `manual_stop` al cerrar Play. Se generó un checkpoint de un Run ID separado, `franka_pickplace_v3_final_smoke24`. Una segunda conexión de 10.117 s con `--resume` cargó los pesos **desde el paso 39 840**, produjo otras **90 744** llamadas de acción (8969.061/s) y 24 episodios terminados. Los CSV combinados tienen **168 filas de 24 arenas en dos sesiones** (120 timeouts y 48 `manual_stop`) y dos ventanas de resumen. Se exportaron **207 puntos escalares** de TensorBoard. La política breve no logró Grasp ni Lift y la recompensa media fue negativa: el smoke comprueba conexión, reanudación y registros, no aprendizaje. Los artefactos están en `TrainingLogs/franka_pickplace_v3_final_smoke24/` y `results/franka_pickplace_v3_final_smoke24/`. Después también se inició el run `franka_pickplace_v3` y se guardaron sus checkpoint y CSV; se conservan como histórico y **no deben reanudarse con la variante actual**. Más tarde `franka_pickplace_v3_fixed30` empezó desde cero y generó el checkpoint usado por Lift36. Solo para aquella prueba se usó una copia temporal del config con `max_steps = 100000`, `checkpoint_interval = 10000` y `summary_freq = 1000`; el config de producción Lift36 conserva los valores indicados arriba. El entrenador de diagnóstico se cortó desde una terminal no interactiva, por lo que `--resume` avisó que faltaba `training_status.json`; sí confirmó la carga del checkpoint y el paso. Para uso normal, interrumpe el entrenador interactivo con Ctrl+C y espera el guardado.

## Evitación de singularidades en V3

Una singularidad es una postura en la que ciertos movimientos del TCP resultan difíciles o imposibles aunque las articulaciones se muevan. Puede dejar el brazo estirado o con poca capacidad para corregir la pinza durante Grasp, Lift y Transport. **PPO sigue controlando directamente las siete articulaciones y la pinza**: esta variante no introduce IK, planificador cartesiano ni un solver que sustituya las acciones.

`FrankaTcpManipulabilityV3` construye el Jacobiano geométrico **6×7** del TCP. Para cada articulación revoluta `i`, toma el origen `p_i` de `joint.transform.TransformPoint(joint.anchorPosition)` y el eje unitario `a_i` del eje X del anclaje, `(joint.transform.rotation * joint.anchorRotation) * Vector3.right`, ambos en coordenadas mundiales. Con el TCP en `p_tcp`, la columna es `[(a_i × (p_tcp − p_i))/0.5 m; a_i]`: tres velocidades lineales divididas por una longitud característica de 0.5 m y tres angulares. Unity define [`anchorPosition`](https://docs.unity3d.com/cn/6000.0/ScriptReference/ArticulationBody-anchorPosition.html) y [`anchorRotation`](https://docs.unity3d.com/cn/6000.0/ScriptReference/ArticulationBody-anchorRotation.html) en el espacio del cuerpo; la articulación revoluta gira sobre el eje X del anclaje, como indica el [manual de Unity](https://docs.unity3d.com/es/2020.1/Manual/class-ArticulationBody.html). Todos los ejes y puntos se expresan en mundo antes del producto vectorial.

Se calcula `G = J Jᵀ + 10⁻⁸ I₆` y la medida **`w = exp(½ log det G)`**, evaluada mediante Cholesky como producto de sus seis diagonales. Es una versión regularizada y adimensional de la manipulabilidad de Yoshikawa; el término diagonal evita un determinante numéricamente inestable cerca de cero. Los buffers `J`, `G` y Cholesky se reservan una vez por agente; cada muestra usa los siete joints, sin LINQ, SVD ni matrices nuevas. Se mide **cada 5 decisiones** por defecto, aproximadamente cada 25 pasos físicos con `DecisionPeriod = 5`, y una vez más al terminar si hace falta registrar la postura final.

La regularización usa estos valores iniciales, editables en el Inspector del agente:

| Concepto | Inicio | Efecto |
| --- | ---: | --- |
| `manipulabilitySafeThreshold` | 0.10 | Sin penalización por encima |
| `manipulabilityDangerThreshold` | 0.025 | Bajo este valor, la penalización sube gradualmente hasta el doble |
| `singularityPenaltyWeight` | 0.00005 | Se resta solo en una muestra peligrosa; máximo 0.00010 por muestra |
| `jointLimitSafeFraction` | 0.12 | Zona central sin penalización; se mide la distancia normalizada al límite más próximo |
| `jointLimitPenaltyWeight` | 0.00001 | Máximo por acción cerca de un límite |
| `jointVelocitySafeDegPerSec` | 52.5°/s | Sin penalización por debajo |
| `jointVelocityPenaltyWeight` | 0.000005 | Máximo por acción si la velocidad supera mucho el umbral |

Estas restas son secundarias frente a Reach, Grasp, Lift, Transport y Place. Acercarse a un límite no termina el episodio. Siguen **33 observaciones y 8 acciones**; el run actual se reanuda desde su checkpoint.

`minimum_joint_limit_margin_deg` y la columna histórica `minimum_joint_limit_margin` expresan grados firmados: un valor negativo indica que la posición física sobrepasó un límite por menos de los 10° tolerados antes de declarar estado inválido. `near_joint_limit_actions` y `joint_limit_warning_count` cuentan acciones, como máximo una vez por acción; el primero usa ≤5°, el segundo margen normalizado <0.12. El nuevo `minimum_joint_limit_margin_normalized` y los siete `jointN_min_margin` usan `clamp(min(ángulo−lower, upper−ángulo)/(upper−lower), 0, 0.5)`: 0 es el límite o sobrepaso y 0.5 el centro; `joint_closest_to_limit` es el joint 1–7 que alcanzó el menor margen del episodio. TensorBoard `V3/MinimumJointLimitMargin` usa ahora esta escala normalizada. Se añaden `V3/ReachRate`, `V3/GraspConfirmedRate`, `V3/LiftRate`, `V3/RewardReach`, `V3/RewardGrasp`, `V3/RewardLift` y `V3/RewardRegularization` (suma firmada de las tres penalizaciones), además de `V3/BilateralContactRate` y `V3/GraspRetention`. Se conservan los pesos pequeños de las penalizaciones.

Safe50 usa **50 arenas simultáneas**, distribuidas en 10 columnas y 5 filas, con 5 m de separación, `timeScale = 1`, base fija, cubo y target fijos, y `detailedStepLogging = false`. La comprobación corta en Unity instanció 50 agentes con IDs 0–49, referencias independientes y cero errores del agente; sus 150 filas CSV y el resumen pasaron el validador. La postura inicial dio una manipulabilidad media cercana a **0.590**, por encima del umbral seguro. Hubo una excepción del indexador de búsqueda del Editor (`UnityEditor.Search.SearchDatabase`), ajena al agente. Esta prueba **no mide throughput ni demuestra que 50 arenas sean más rápidas que 24**.
