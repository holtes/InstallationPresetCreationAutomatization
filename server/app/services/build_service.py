"""
Модуль сборки WebGL.

Отвечает за:
- Подготовку Unity-проекта (копирование ассетов, генерация конфига)
- Запуск Unity CLI для экспорта WebGL
- Применение оптимизаций (сжатие текстур, LOD, ограничение FPS)
- Формирование артефактов и URL для публикации
"""

import json
import logging
import os
import shutil
import subprocess
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

from app.core.config import settings

logger = logging.getLogger(__name__)

# Заранее собранный WebGL-бандл «Пазл». Используется как универсальный артефакт:
# в текущей итерации платформа не запускает Unity на сервере — мы берём prebuilt-бандл
# и инжектим в него выбранный пользователем ассет (StreamingAssets/<filename>) +
# project_config.json (cols/rows/asset_id). Bootstrap внутри бандла читает config
# при старте и собирает пазл из этой картинки.
PREBUILT_PUZZLE_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(__file__)))),
    "builds", "prebuilt", "puzzle",
)


def prepare_build_directory(build_job_id: int, configuration: dict, asset_paths: list[str]) -> str:
    """
    Prepare a build directory with all necessary files.
    Returns the path to the build workspace.
    """
    workspace = os.path.join(settings.BUILDS_DIR, f"workspace_{build_job_id}")
    os.makedirs(workspace, exist_ok=True)

    # Copy assets to workspace
    assets_dir = os.path.join(workspace, "Assets", "StreamingAssets")
    os.makedirs(assets_dir, exist_ok=True)
    for asset_path in asset_paths:
        if os.path.exists(asset_path):
            dest = os.path.join(assets_dir, os.path.basename(asset_path))
            shutil.copy2(asset_path, dest)

    # Write configuration file for Unity to read
    config_path = os.path.join(workspace, "Assets", "StreamingAssets", "project_config.json")
    with open(config_path, "w", encoding="utf-8") as f:
        json.dump(configuration, f, ensure_ascii=False, indent=2)

    return workspace


def build_optimization_args(hardware_profile: dict | None) -> list[str]:
    """Generate Unity CLI arguments for optimization based on hardware profile."""
    args = []

    if hardware_profile:
        memory = hardware_profile.get("memory_limit_mb", 0)
        if memory and memory < 4096:
            args.extend([
                "-textureCompression", "ASTC",
                "-compressTexturesOnImport",
            ])

        gpu = hardware_profile.get("gpu_class", "")
        if gpu and "integrated" in gpu.lower():
            args.extend([
                "-graphicsAPI", "WebGL2",
            ])

    return args


def run_unity_build(
    workspace: str,
    build_job_id: int,
    hardware_profile: dict | None = None,
) -> dict:
    """
    Execute Unity CLI build to produce WebGL output.

    Returns: {
        "success": bool,
        "output_dir": str,
        "build_time_seconds": float,
        "logs": list[str],
        "optimizations": list[str],
    }
    """
    output_dir = os.path.join(settings.BUILDS_DIR, f"build_{build_job_id}")
    os.makedirs(output_dir, exist_ok=True)

    start_time = time.time()
    logs = []
    optimizations = []

    # Текущая итерация: всегда отдаём prebuilt WebGL-бандл «Пазл» с подменой
    # пользовательской картинки. Реальный запуск Unity на сервере отключён —
    # см. PREBUILT_PUZZLE_DIR в шапке модуля.
    if os.path.isdir(PREBUILT_PUZZLE_DIR):
        return _copy_prebuilt_build(output_dir, workspace, build_job_id, start_time)

    logger.warning("Prebuilt puzzle build not found at %s — falling back to simulated", PREBUILT_PUZZLE_DIR)
    return _simulated_build(output_dir, build_job_id, start_time)

    unity_exe = settings.UNITY_EXECUTABLE
    unity_project = settings.UNITY_PROJECT_PATH

    # Check if Unity executable is available
    if not os.path.exists(unity_exe):
        logger.warning("Unity executable not found at %s — running simulated build", unity_exe)
        return _simulated_build(output_dir, build_job_id, start_time)

    cmd = [
        unity_exe,
        "-quit",
        "-batchmode",
        "-nographics",
        "-projectPath", unity_project,
        "-executeMethod", "BuildScript.BuildWebGL",
        "-buildTarget", "WebGL",
        "-outputPath", output_dir,
        "-configPath", os.path.join(workspace, "Assets", "StreamingAssets", "project_config.json"),
        "-logFile", os.path.join(output_dir, "build.log"),
    ]

    opt_args = build_optimization_args(hardware_profile)
    cmd.extend(opt_args)
    if opt_args:
        optimizations.append(f"Applied CLI optimizations: {', '.join(opt_args)}")

    logs.append(f"Starting Unity build: {' '.join(cmd)}")

    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=3600,
        )

        logs.append(f"Unity exit code: {result.returncode}")
        if result.stdout:
            logs.append(f"stdout: {result.stdout[-2000:]}")
        if result.stderr:
            logs.append(f"stderr: {result.stderr[-2000:]}")

        build_time = time.time() - start_time

        if result.returncode == 0:
            optimizations.extend(_apply_post_build_optimizations(output_dir, hardware_profile))
            return {
                "success": True,
                "output_dir": output_dir,
                "build_time_seconds": round(build_time, 2),
                "logs": logs,
                "optimizations": optimizations,
            }
        else:
            return {
                "success": False,
                "output_dir": output_dir,
                "build_time_seconds": round(build_time, 2),
                "logs": logs,
                "optimizations": [],
            }

    except subprocess.TimeoutExpired:
        logs.append("Build timed out after 3600 seconds")
        return {
            "success": False,
            "output_dir": output_dir,
            "build_time_seconds": 3600,
            "logs": logs,
            "optimizations": [],
        }
    except Exception as e:
        logs.append(f"Build error: {e}")
        return {
            "success": False,
            "output_dir": output_dir,
            "build_time_seconds": time.time() - start_time,
            "logs": logs,
            "optimizations": [],
        }


def _copy_prebuilt_build(
    output_dir: str,
    workspace: str,
    build_job_id: int,
    start_time: float,
) -> dict:
    """
    Копирует prebuilt-бандл «Пазл» в output_dir и инжектит пользовательский ассет.

    Pipeline:
      1. copytree(prebuilt → output_dir) — Build/, TemplateData/, index.html, StreamingAssets/ (placeholder)
      2. Из workspace/Assets/StreamingAssets/project_config.json читаем имя файла слота puzzle_image
      3. Копируем картинку из workspace в output_dir/StreamingAssets/<filename>
      4. Перезаписываем output_dir/StreamingAssets/project_config.json актуальным конфигом

    Unity-бандл при старте читает StreamingAssets/project_config.json и собирает пазл
    из указанной картинки (см. Assets/Build/PuzzleWebGLBootstrap.cs в клиенте).
    """
    logs: list[str] = []

    # 1. Копируем prebuilt-бандл целиком
    if os.path.isdir(output_dir):
        shutil.rmtree(output_dir)
    shutil.copytree(PREBUILT_PUZZLE_DIR, output_dir)
    logs.append(f"Copied prebuilt bundle: {PREBUILT_PUZZLE_DIR} → {output_dir}")

    streaming_dst = os.path.join(output_dir, "StreamingAssets")
    os.makedirs(streaming_dst, exist_ok=True)

    # 2. Берём актуальный config из workspace
    streaming_src = os.path.join(workspace, "Assets", "StreamingAssets")
    workspace_config_path = os.path.join(streaming_src, "project_config.json")
    config = {}
    if os.path.isfile(workspace_config_path):
        try:
            with open(workspace_config_path, "r", encoding="utf-8") as f:
                config = json.load(f)
        except Exception as ex:
            logs.append(f"Не удалось прочитать project_config.json: {ex}")

    # 3. Копируем картинку слота puzzle_image
    puzzle_asset = (config.get("assets") or {}).get("puzzle_image") or {}
    puzzle_filename = puzzle_asset.get("filename")
    if puzzle_filename:
        src_image = os.path.join(streaming_src, puzzle_filename)
        if os.path.isfile(src_image):
            shutil.copy2(src_image, os.path.join(streaming_dst, puzzle_filename))
            logs.append(f"Injected puzzle_image asset: {puzzle_filename}")
        else:
            logs.append(f"WARN: файл слота puzzle_image не найден в workspace: {src_image}")
    else:
        logs.append("WARN: в project_config.json отсутствует assets.puzzle_image.filename")

    # 4. Перезаписываем project_config.json (даже если puzzle_filename не нашли — пусть бандл прочитает)
    if config:
        with open(os.path.join(streaming_dst, "project_config.json"), "w", encoding="utf-8") as f:
            json.dump(config, f, ensure_ascii=False, indent=2)
        logs.append("Overrode StreamingAssets/project_config.json")

    return {
        "success": True,
        "output_dir": output_dir,
        "build_time_seconds": round(time.time() - start_time, 2),
        "logs": logs,
        "optimizations": ["prebuilt_bundle"],
    }


def _simulated_build(output_dir: str, build_job_id: int, start_time: float) -> dict:
    """
    Create a simulated WebGL build for development/testing when Unity is not available.
    Generates a minimal HTML page that demonstrates the build output.
    """
    build_id = str(uuid.uuid4())[:8]

    index_html = f"""<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>WebGL Build #{build_job_id}</title>
    <style>
        body {{ margin: 0; background: #1a1a2e; color: #eee; font-family: Arial, sans-serif;
               display: flex; justify-content: center; align-items: center; height: 100vh; }}
        .container {{ text-align: center; padding: 40px; background: #16213e; border-radius: 16px;
                     box-shadow: 0 8px 32px rgba(0,0,0,0.3); }}
        h1 {{ color: #e94560; margin-bottom: 8px; }}
        .meta {{ color: #888; font-size: 14px; }}
        canvas {{ width: 640px; height: 480px; border: 2px solid #e94560; border-radius: 8px;
                 margin-top: 20px; display: block; margin-left: auto; margin-right: auto; }}
    </style>
</head>
<body>
    <div class="container">
        <h1>Interactive Mechanic</h1>
        <p class="meta">Build ID: {build_id} | Job #{build_job_id}</p>
        <canvas id="gameCanvas" width="640" height="480"></canvas>
        <p>WebGL build placeholder — replace with Unity WebGL output</p>
        <script>
            const canvas = document.getElementById('gameCanvas');
            const ctx = canvas.getContext('2d');
            let t = 0;
            function draw() {{
                ctx.fillStyle = '#1a1a2e';
                ctx.fillRect(0, 0, 640, 480);
                ctx.fillStyle = '#e94560';
                ctx.beginPath();
                ctx.arc(320 + Math.sin(t) * 100, 240 + Math.cos(t * 0.7) * 80, 30, 0, Math.PI * 2);
                ctx.fill();
                ctx.fillStyle = '#eee';
                ctx.font = '16px Arial';
                ctx.textAlign = 'center';
                ctx.fillText('WebGL Placeholder — Build #{build_job_id}', 320, 440);
                t += 0.02;
                requestAnimationFrame(draw);
            }}
            draw();
        </script>
    </div>
</body>
</html>"""

    with open(os.path.join(output_dir, "index.html"), "w", encoding="utf-8") as f:
        f.write(index_html)

    return {
        "success": True,
        "output_dir": output_dir,
        "build_time_seconds": round(time.time() - start_time, 2),
        "logs": ["Simulated build (Unity not available)", f"Output: {output_dir}"],
        "optimizations": ["simulated_mode"],
    }


def _apply_post_build_optimizations(output_dir: str, hardware_profile: dict | None) -> list[str]:
    """Apply post-build optimizations (Brotli/Gzip compression, etc.)."""
    applied = []

    # Compress .js and .wasm files with gzip if available
    for ext in ("*.js", "*.wasm", "*.data"):
        for fpath in Path(output_dir).rglob(ext):
            try:
                import gzip
                with open(fpath, "rb") as f_in:
                    with gzip.open(f"{fpath}.gz", "wb") as f_out:
                        shutil.copyfileobj(f_in, f_out)
                applied.append(f"gzip: {fpath.name}")
            except Exception:
                pass

    return applied


def get_build_size(output_dir: str) -> int:
    """Calculate total size of build artifacts."""
    total = 0
    for dirpath, _, filenames in os.walk(output_dir):
        for f in filenames:
            total += os.path.getsize(os.path.join(dirpath, f))
    return total


def generate_iframe_code(artifact_url: str, width: int = 960, height: int = 600) -> str:
    """Generate iframe embed code for the WebGL build."""
    return (
        f'<iframe src="{artifact_url}" '
        f'width="{width}" height="{height}" '
        f'frameborder="0" allowfullscreen '
        f'allow="autoplay; fullscreen; gamepad"></iframe>'
    )
