"""
convert_icons.py
================
Выбирает нужные 23 SVG-иконки из папки источника,
конвертирует в PNG 48×48 (белый, прозрачный фон) через Playwright (Chromium),
перемещает в Assets/Resources/Icons/.

Требования:
    pip install playwright
    playwright install chromium
"""

import os
import re
import shutil
import sys

SRC_DIR = r"C:\Users\user\Desktop\WorkProjects\icons"
DST_DIR = r"F:\IneractiveInstallationsAutomatization\client\Assets\Resources\Icons"
SIZE    = 48

ICONS = [
    "save", "play", "hammer", "upload", "x",
    "image", "video", "music", "box", "file-text",
    "puzzle", "help-circle", "brain", "clock", "sparkles",
    "rotate-ccw", "monitor", "maximize", "check", "copy",
    "external-link", "download", "qr-code",
]

try:
    from playwright.sync_api import sync_playwright
except ImportError:
    sys.exit("Установите playwright:\n  pip install playwright\n  playwright install chromium")


def patch_svg(svg_text: str) -> str:
    """Заменяем currentColor на белый — иконки на тёмном фоне Unity."""
    svg_text = re.sub(r'stroke="currentColor"', 'stroke="#FFFFFF"', svg_text)
    svg_text = re.sub(r'fill="currentColor"',   'fill="#FFFFFF"',   svg_text)
    # Убеждаемся, что у корневого <svg> задан размер
    svg_text = re.sub(
        r'(<svg[^>]*?)width="[^"]*"', rf'\g<1>width="{SIZE}"', svg_text
    )
    svg_text = re.sub(
        r'(<svg[^>]*?)height="[^"]*"', rf'\g<1>height="{SIZE}"', svg_text
    )
    return svg_text


def main():
    os.makedirs(DST_DIR, exist_ok=True)

    # Проверяем, какие SVG вообще есть
    missing_src = [n for n in ICONS if not os.path.exists(os.path.join(SRC_DIR, f"{n}.svg"))]
    if missing_src:
        print(f"[warn] Не найдены в {SRC_DIR}:")
        for n in missing_src:
            print(f"       {n}.svg")

    to_convert = [n for n in ICONS if os.path.exists(os.path.join(SRC_DIR, f"{n}.svg"))]
    print(f"\nНайдено {len(to_convert)}/{len(ICONS)} иконок. Конвертируем...\n")

    ok, fail = [], []

    with sync_playwright() as pw:
        browser = pw.chromium.launch()
        page = browser.new_page(viewport={"width": SIZE, "height": SIZE})
        page.add_style_tag(content="* { margin:0; padding:0; }")

        for name in to_convert:
            svg_path = os.path.join(SRC_DIR, f"{name}.svg")
            dst_path = os.path.join(DST_DIR, f"{name}.png")

            try:
                with open(svg_path, encoding="utf-8") as f:
                    svg_text = f.read()

                svg_text = patch_svg(svg_text)

                html = f"""<!DOCTYPE html>
<html>
<head><style>
  html, body {{ margin:0; padding:0; background:transparent; width:{SIZE}px; height:{SIZE}px; overflow:hidden; }}
  svg {{ display:block; width:{SIZE}px; height:{SIZE}px; }}
</style></head>
<body>{svg_text}</body>
</html>"""

                page.set_content(html, wait_until="load")
                page.screenshot(
                    path=dst_path,
                    omit_background=True,
                    clip={"x": 0, "y": 0, "width": SIZE, "height": SIZE},
                )
                print(f"  [ok]    {name}.png")
                ok.append(name)

            except Exception as e:
                print(f"  [fail]  {name}: {e}")
                fail.append(name)

        browser.close()

    print(f"\n{'─'*50}")
    print(f"Готово: {len(ok)}/{len(to_convert)} конвертировано")
    print(f"Папка: {DST_DIR}")
    if fail:
        print(f"Ошибки: {', '.join(fail)}")
    if missing_src:
        print(f"Не было SVG: {', '.join(missing_src)}")


if __name__ == "__main__":
    main()
