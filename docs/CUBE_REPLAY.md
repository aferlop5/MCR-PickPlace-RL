# Reproducción del cubo fijo y grabación del aprendizaje

La escena de entrenamiento independiente es `Assets/Scenes/PickPlaceCubeReplay.unity`; la de evaluación final es `Assets/Scenes/PickPlaceCubeFinalReplay.unity`. Ambas se reconstruyeron desde `docs/reference_placefix4_cube/` sin modificar la escena actual. La primera tiene 50 arenas y la cámara histórica enfoca la arena inicial. La segunda tiene una sola arena para grabación. El prefab independiente es `Assets/Prefabs/TrainingArenaCubeReplay.prefab` y usa un agente C# independiente con 33 observaciones y 8 acciones. El cubo mide 5 × 5 × 5 cm, masa 0,1 kg; cubo, base y objetivo se restauran a posición y orientación fijas. `MaxStep = 1000`, `timeScale = 1` y periodo de decisión 5. Su Behavior se llama `FrankaCubeReplay`, distinto del Behavior del prisma, para que el entrenador rechace una escena equivocada.

**Corrección del 28/10/2026.** El primer intento de `franka_cube_replay_from_scratch` generó un checkpoint de 30.000 pasos cuya capa de entrada es `(256,35)`: se conectó la escena `PickPlaceV3` del prisma (35 observaciones, objeto 3,5 × 5 × 7 cm). Se conserva para trazabilidad, pero no sirve para el entrenamiento del cubo ni debe reanudarse. El run correcto usa ahora el ID `franka_cube_replay_from_scratch_v2` y comienza con pesos aleatorios. Abrir la escena correcta en Unity es obligatorio: el comando PowerShell inicia Python, pero no cambia la escena abierta en el editor.

Si Unity comunica `Connected new brain: FrankaPickPlaceV3`, está abierta la escena equivocada: el entrenador detiene ese intento antes de crear un modelo. En el segundo intento se archivaron los tres archivos de diagnóstico, sin checkpoint ni episodios, en `results/franka_cube_replay_from_scratch_v2_failed_scene_20261028_200331/`. El script repetirá este archivado de forma segura si vuelve a haber únicamente archivos de diagnóstico; nunca mueve una carpeta que contenga checkpoint, CSV o ficheros desconocidos. La comprobación corta `.\scripts\train_cube_from_zero_timelapse.ps1 -CheckOnly` valida los archivos y deja el Run ID libre, pero no puede conocer la escena actualmente abierta en Unity.

La compatibilidad estática del checkpoint final con la arquitectura del replay se puede repetir con `.\.venv\Scripts\python.exe .\tools\validate_cube_replay.py` desde la raíz del proyecto. La comprobación carga los pesos y ejecuta una inferencia sintética de forma corta; la comprobación visual del movimiento requiere abrir Unity y pulsar Play.

## Entrenar desde pesos aleatorios

Detén Play, espera a que Unity importe los archivos actualizados y **abre** `Assets/Scenes/PickPlaceCubeReplay.unity` (doble clic en la ventana Project). Comprueba que la jerarquía incluye `TrainingArenaSpawnerCubeReplay` y que el prefab `TrainingArenaCubeReplay` muestra `TestObject` con escala `(0.05, 0.05, 0.05)` y Behavior `FrankaCubeReplay`. Guarda la escena. En PowerShell:

```powershell
cd 'C:\Users\agust\OneDrive\Escritorio\modelado\t1\PickPlaceRL'
.\scripts\train_cube_from_zero_timelapse.ps1 -RecordTimelapse -TimeoutWait 600
```

Cuando aparezca `Listening on port 5004`, pulsa **Play** en la escena del cubo. El primer arranque no utiliza `--initialize-from`, `--resume` ni un checkpoint antiguo. El script rechaza un run ID ya existente. El run nuevo es `franka_cube_replay_from_scratch_v2`; sus resultados y CSV se guardan en carpetas de ese nombre. El trainer conserva hasta 30 checkpoints numerados, cada 250.000 pasos aproximadamente. La semilla del trainer es 12345; la física y la planificación de arenas pueden introducir variación adicional.

La secuencia es un currículum **equivalente y distinto de la historia exacta**: comienza con Lift05cm, pasa a Lift10cm y Lift20cm cuando la recompensa suavizada supera 2,0 tras un mínimo de 100 episodios de lección, y finalmente llega a PickPlace30cm. Se usa desde el principio la lógica final de PlaceFix4. El paso entre lecciones ocurre dentro del mismo run, por lo que todos los pesos posteriores proceden únicamente de esta ejecución. El presupuesto es de 7.000.000 pasos PPO; no se garantiza que aprenda a completar la tarea. Para evaluar una condición de éxito, revisa al menos los últimos 1.000 episodios válidos de la lección final: objetivo orientativo ≥85 % Place físico y ≥70 % ControlledPlace, junto a una tasa HighRelease ≤10 %. Si no los alcanza, conserva y presenta el resultado real.

Para detenerlo, pulsa **Ctrl+C una vez** en la terminal, espera a que se guarde el checkpoint y después detén Play. Para reanudar sin crear pesos nuevos:

```powershell
.\scripts\train_cube_from_zero_timelapse.ps1 -Resume -RecordTimelapse -TimeoutWait 600
```

`-Resume` exige un checkpoint del run nuevo. No carga PlaceFix4 ni otro run. Los CSV esperados son `episodes.csv`, `summary.csv` y `episode_events.csv`; TensorBoard y los checkpoints quedan bajo `results/franka_cube_replay_from_scratch_v2/`.

## Evaluar el modelo final histórico

Abre y guarda `Assets/Scenes/PickPlaceCubeFinalReplay.unity`. Ejecuta:

```powershell
.\scripts\replay_cube_final.ps1 -TimeoutWait 600
```

El script verifica una copia del checkpoint PlaceFix4 en `results/franka_cube_final_evaluation_v2/`, activa `--inference --resume --deterministic` y no ejecuta PPO. Cuando escuche, pulsa **Play**. Los episodios de evaluación se registran aparte bajo `TrainingLogs/franka_cube_final_evaluation_v2/`. La política usa los mismos 33 valores de entrada y 8 salidas que el prefab del cubo. Para grabar, captura la vista Game de la única arena; la cámara puede ajustarse visualmente sin cambiar la física.

## Reconstruir un timelapse con checkpoints

Esta es una **reconstrucción posterior**, no una grabación continua de todas las horas de entrenamiento. Tras obtener checkpoints, genera un manifiesto de pasos y métricas reales:

```powershell
.\.venv\Scripts\python.exe .\tools\cube_timelapse_manifest.py
```

El archivo `TimelapseCube/manifest.csv` relaciona cada checkpoint retenido con su paso PPO y los últimos valores TensorBoard disponibles de lección, reward y tasas. Para visualizar, por ejemplo, el checkpoint del paso `250000`, ejecuta:

```powershell
.\scripts\replay_cube_checkpoint.ps1 -Step 250000
```

Usa el número **exacto** listado en el manifiesto, porque los checkpoints reales pueden quedar unos pasos antes o después del intervalo nominal. El script crea una escena independiente de una arena y un run de inferencia propio para ese paso. Abre la escena que muestra la terminal, pulsa Play y graba un clip corto de la vista Game como `TimelapseCube/clips/step_<paso>.mp4`. Repite para los pasos representativos: inicio, aparición de Reach, Grasp, Lift y Place. La captura se hace durante inferencia de cada checkpoint; no se inventan frames intermedios del entrenamiento.

Con FFmpeg instalado y clips ya grabados, el montaje cronológico con rótulos de paso, lección, recompensa y tasas disponibles de Reach, Grasp, Lift y ControlledPlace se genera con:

```powershell
& 'C:\Users\agust\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' .\tools\make_cube_timelapse.py
```

El resultado sería `TimelapseCube/timelapse.mp4`. Si FFmpeg no está disponible, conserva los clips y el manifiesto para montarlos manualmente. La curva de reward aparece en TensorBoard y puede colocarse al lado de los clips; el montaje anterior no la superpone todavía. No hay MP4 antes de entrenar y grabar clips reales.

## Precauciones de interpretación

Los checkpoints históricos no se borran ni se sobrescriben. La reproducción desde cero usa un único run y una lección de elevación progresiva, pero no repite exactamente todos los cambios de código que tuvieron lugar en la historia. Aunque la configuración PPO se mantenga, distintas semillas, contactos y estados de física pueden producir otra curva de aprendizaje. El modelo final histórico debe evaluarse con el cubo; no es compatible con una interfaz de observación de tamaño diferente. Las métricas del vídeo proceden del manifiesto TensorBoard y los CSV, no de una estimación visual.
