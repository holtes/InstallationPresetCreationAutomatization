"""
download_icons.py
=================
Скачивает иконки Lucide с GitHub и конвертирует в PNG для Unity UI Toolkit.
Результат: Assets/Resources/Icons/<name>.png (белый stroke, 48×48, прозрачный фон)

Требования (чистый Python, без системных библиотек, работает на Windows):
    pip install requests svglib reportlab pillow

Запуск:
    cd client/
    python download_icons.py
"""

import io
import os
import re
import sys

# ── Проверка зависимостей ────────────────────────────────────────────────────
missing = []

try:
    import requests
except ImportError:
    missing.append("requests")

try:
    from svglib.svglib import svg2rlg
    from reportlab.graphics import renderPM
except ImportError:
    missing.append("svglib reportlab")

try:
    from PIL import Image
except ImportError:
    missing.append("pillow")

if missing:
    sys.exit(
        "Установите недостающие библиотеки:\n"
        f"  pip install {' '.join(missing)}\n"
        "\nПосле этого запустите скрипт снова."
    )

# ── Иконки ───────────────────────────────────────────────────────────────────
# ключ — имя файла в Lucide, значение — имя PNG в Unity
ICONS = {
    "save":          "save",
    "play":          "play",
    "hammer":        "hammer",
    "upload":        "upload",
    "x":             "x",
    "image":         "image",
    "video":         "video",
    "music":         "music",
    "box":           "box",
    "file-text":     "file-text",
    "puzzle":        "puzzle",
    "help-circle":   "help-circle",
    "brain":         "brain",
    "clock":         "clock",
    "sparkles":      "sparkles",
    "rotate-ccw":    "rotate-ccw",
    "monitor":       "monitor",
    "maximize":      "maximize",
    "check":         "check",
    "copy":          "copy",
    "external-link": "external-link",
    "download":      "download",
    "qr-code":       "qr-code",
}

SVG_URL = "https://raw.githubusercontent.com/lucide-icons/lucide/main/icons/{name}.svg"
OUT_DIR = os.path.join(os.path.dirname(__file__), "Assets", "Resources", "Icons")
SIZE    = 48  # px


# ── Утилиты ──────────────────────────────────────────────────────────────────

def patch_svg(svg_text: str, color: str = "#FFFFFF") -> str:
    """
    Lucide SVG использует currentColor.
    Заменяем на белый, чтобы иконки были видны на тёмном фоне в Unity.
    Также явно задаём viewBox и прозрачный фон.
    """
    svg_text = re.sub(r'stroke="currentColor"', f'stroke="{color}"', svg_text)
    svg_text = re.sub(r'fill="currentColor"',   f'fill="{color}"',   svg_text)
    # Убираем fill="none" у корневого <svg> если мешает прозрачности
    svg_text = re.sub(r'(<svg[^>]*)\s+fill="none"', r'\1', svg_text, count=1)
    return svg_text


def svg_bytes_to_png(svg_bytes: bytes, size: int) -> bytes:
    """SVG → PNG через svglib + reportlab → PIL → bytes."""
    # Записываем SVG во временный файл (svglib принимает путь)
    tmp_svg = os.path.join(OUT_DIR, "_tmp_.svg")
    with open(tmp_svg, "wb") as f:
        f.write(svg_bytes)

    try:
        drawing = svg2rlg(tmp_svg)
        if drawing is None:
            raise ValueError("svg2rlg вернул None — возможно, повреждённый SVG")

        # Масштабируем до нужного размера
        scale_x = size / drawing.width
        scale_y = size / drawing.height
        drawing.width  = size
        drawing.height = size
        drawing.transform = (scale_x, 0, 0, scale_y, 0, 0)

        # Рендерим в PNG через буфер
        buf = io.BytesIO()
        renderPM.drawToFile(drawing, buf, fmt="PNG", bg=0x00000000)
        buf.seek(0)

        # PIL: убеждаемся что канал прозрачности правильный
        img = Image.open(buf).convert("RGBA")
        out_buf = io.BytesIO()
        img.save(out_buf, format="PNG")
        return out_buf.getvalue()
    finally:
        if os.path.exists(tmp_svg):
            os.remove(tmp_svg)


# ── Основная логика ──────────────────────────────────────────────────────────

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print(f"Папка назначения: {OUT_DIR}\n")

    ok, fail = [], []

    for lucide_name, unity_name in ICONS.items():
        out_path = os.path.join(OUT_DIR, f"{unity_name}.png")

        if os.path.exists(out_path):
            print(f"  [skip]  {unity_name}.png  (уже существует)")
            ok.append(unity_name)
            continue

        # 1. Скачать SVG
        url = SVG_URL.format(name=lucide_name)
        try:
            resp = requests.get(url, timeout=15)
            resp.raise_for_status()
        except Exception as e:
            print(f"  [fail]  {lucide_name}: сетевая ошибка — {e}")
            fail.append(lucide_name)
            continue

        # 2. Патчим цвета
        svg_src = patch_svg(resp.text)

        # 3. Конвертируем SVG → PNG
        try:
            png_data = svg_bytes_to_png(svg_src.encode("utf-8"), SIZE)
            with open(out_path, "wb") as f:
                f.write(png_data)
            print(f"  [ok]    {unity_name}.png")
            ok.append(unity_name)
        except Exception as e:
            print(f"  [fail]  {unity_name}: конвертация — {e}")
            fail.append(lucide_name)

    # Итог
    print(f"\n{'─'*50}")
    print(f"Готово: {len(ok)}/{len(ICONS)} иконок")
    if fail:
        print(f"Не скачались ({len(fail)}): {', '.join(fail)}")
        print("Скачайте вручную: https://lucide.dev/icons/  →  Download PNG 48px белый")
    else:
        print("Все иконки готовы!")
        print("В Unity: Assets → Refresh  (или Ctrl+R)")


if __name__ == "__main__":
    main()
