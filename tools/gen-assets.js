#!/usr/bin/env node
/*
 * Generates:
 *   ui/assets/flags/<cc>.png   — 40x30 PNG flags (from lipis/flag-icons 4x3 set)
 *   ui/assets/countries.json   — ISO2 -> { en, ru } localized names
 *
 * Usage: node tools/gen-assets.js
 * Requires devDependency "sharp" (only needed when regenerating assets).
 */
const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const ROOT = path.join(__dirname, '..');
const SRC_FLAGS = process.env.FLAGS_DIR || path.join(ROOT, 'vendor', 'flag-icons', 'flags', '4x3');
const OUT_FLAGS = path.join(ROOT, 'ui', 'assets', 'flags');
const OUT_JSON = path.join(ROOT, 'ui', 'assets', 'countries.json');

// Extra localized names that CLDR intentionally returns as "territory" names,
// plus nicer short forms for the country picker.
const RU_OVERRIDES = {
  US: 'США',
  GB: 'Великобритания',
  KR: 'Южная Корея',
  KP: 'Северная Корея',
  AE: 'ОАЭ',
  CZ: 'Чехия',
  NL: 'Нидерланды',
  MM: 'Мьянма',
  LA: 'Лаос',
  SY: 'Сирия',
  VN: 'Вьетнам',
  IR: 'Иран',
  BO: 'Боливия',
  VE: 'Венесуэла',
  TZ: 'Танзания',
  MD: 'Молдавия',
  MK: 'Северная Македония',
  CD: 'ДР Конго',
  CG: 'Конго',
  CI: 'Кот-д’Ивуар',
  BN: 'Бруней',
  CV: 'Кабо-Верде',
  TL: 'Восточный Тимор',
  VA: 'Ватикан',
  FM: 'Микронезия',
  PS: 'Палестина',
  TW: 'Тайвань',
  HK: 'Гонконг',
  MO: 'Макао',
  XK: 'Косово',
};
const EN_OVERRIDES = {
  US: 'United States',
  GB: 'United Kingdom',
  KR: 'South Korea',
  KP: 'North Korea',
  AE: 'UAE',
  CZ: 'Czechia',
  NL: 'Netherlands',
  MM: 'Myanmar',
  LA: 'Laos',
  SY: 'Syria',
  VN: 'Vietnam',
  IR: 'Iran',
  BO: 'Bolivia',
  VE: 'Venezuela',
  TZ: 'Tanzania',
  MD: 'Moldova',
  MK: 'North Macedonia',
  CD: 'DR Congo',
  CG: 'Congo',
  CI: 'Ivory Coast',
  BN: 'Brunei',
  CV: 'Cabo Verde',
  TL: 'Timor-Leste',
  VA: 'Vatican City',
  FM: 'Micronesia',
  PS: 'Palestine',
  TW: 'Taiwan',
  HK: 'Hong Kong',
  MO: 'Macao',
  XK: 'Kosovo',
};

async function main() {
  // ---- 1. flags ----
  fs.mkdirSync(OUT_FLAGS, { recursive: true });
  const files = fs.readdirSync(SRC_FLAGS).filter((f) => f.toLowerCase().endsWith('.svg'));
  let count = 0;
  for (const f of files) {
    const cc = path.basename(f, '.svg').toLowerCase();
    if (cc.length !== 2 && !/^[a-z]{2}-/.test(cc)) continue;
    const png = await sharp(path.join(SRC_FLAGS, f))
      .resize(40, 30, { fit: 'fill' })
      .png({ compressionLevel: 9, palette: true })
      .toBuffer();
    fs.writeFileSync(path.join(OUT_FLAGS, cc + '.png'), png);
    count++;
  }
  console.log('flags written:', count);

  // ---- 2. country names ----
  const names = require('cldr-localenames-modern/main/en/territories.json').main.en.localeDisplayNames.territories;
  const namesRu = require('cldr-localenames-modern/main/ru/territories.json').main.ru.localeDisplayNames.territories;
  const codes = files.map((f) => path.basename(f, '.svg').toUpperCase()).filter((c) => names[c]);
  const out = {};
  for (const cc of codes) {
    out[cc] = {
      en: EN_OVERRIDES[cc] || names[cc],
      ru: RU_OVERRIDES[cc] || namesRu[cc] || names[cc],
    };
  }
  fs.writeFileSync(OUT_JSON, JSON.stringify(out, null, 0));
  console.log('countries written:', Object.keys(out).length, OUT_JSON);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
