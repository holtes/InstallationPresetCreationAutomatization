/**
 * convert_icons.mjs
 * Выбирает 23 SVG-иконки из SRC_DIR, конвертирует в PNG 48×48 белый/прозрачный,
 * кладёт в DST_DIR.
 *
 * Требования: node >= 14
 *   npm install sharp
 */

import sharp from "sharp";
import fs from "fs";
import path from "path";

const SRC_DIR = String.raw`C:\Users\user\Desktop\WorkProjects\icons`;
const DST_DIR = String.raw`F:\IneractiveInstallationsAutomatization\client\Assets\Resources\Icons`;
const SIZE = 48;

const ICONS = [
  "save", "play", "hammer", "upload", "x",
  "image", "video", "music", "box", "file-text",
  "puzzle", "help-circle", "brain", "clock", "sparkles",
  "rotate-ccw", "monitor", "maximize", "check", "copy",
  "external-link", "download", "qr-code",
];

function patchSvg(svgText) {
  // Lucide использует currentColor — меняем на белый для Unity
  return svgText
    .replaceAll('stroke="currentColor"', 'stroke="#FFFFFF"')
    .replaceAll('fill="currentColor"',   'fill="#FFFFFF"');
}

async function main() {
  fs.mkdirSync(DST_DIR, { recursive: true });

  const ok = [], fail = [], missingSrc = [];

  for (const name of ICONS) {
    const srcPath = path.join(SRC_DIR, `${name}.svg`);
    const dstPath = path.join(DST_DIR, `${name}.png`);

    if (!fs.existsSync(srcPath)) {
      console.log(`  [miss]  ${name}.svg — не найден`);
      missingSrc.push(name);
      continue;
    }

    try {
      const svgText = patchSvg(fs.readFileSync(srcPath, "utf8"));

      await sharp(Buffer.from(svgText))
        .resize(SIZE, SIZE)
        .png()
        .toFile(dstPath);

      console.log(`  [ok]    ${name}.png`);
      ok.push(name);
    } catch (e) {
      console.log(`  [fail]  ${name}: ${e.message}`);
      fail.push(name);
    }
  }

  console.log("\n" + "─".repeat(50));
  console.log(`Готово: ${ok.length}/${ICONS.length}`);
  if (fail.length)      console.log(`Ошибки:     ${fail.join(", ")}`);
  if (missingSrc.length) console.log(`Нет SVG:    ${missingSrc.join(", ")}`);
  console.log(`Папка: ${DST_DIR}`);
}

main().catch(err => { console.error(err); process.exit(1); });
