#!/usr/bin/env node
// Fill Resources/Sandbox/sandbox.<lang>.json - the Sandbox editor's text in each UI
// language - from the game's own Localization.csv, so the editor uses the words
// players see in the game's Sandbox Options screen.
//
//   node scripts/gen-sandbox-i18n.mjs <Localization.csv> [--fills <dir>] [--missing <dir>]
//
// The csv is <7D2D install>/Data/Config/Localization.csv. For every English string in
// the sandbox-x.y.json tables, per language, the first of these wins:
//   1. what the language file already has (earlier runs, hand translations),
//   2. the game's translation of exactly that English (cleaned the way PackRelay's
//      extract-sandbox-options cleans it), preferring the Sandbox menu's go* rows -
//      or, for a value like "3 Days", the game's "{0} Days" format with the number put in,
//   3. the same English already translated under another table version,
//   4. --fills <dir>/<lang>.json, an { "english": "translation" } map for what the game
//      has no row for.
// Anything else stays out of the file and shows in English. --missing <dir> writes
// those leftovers, per language, as the input for a --fills pass.
//
// A new table (sandbox-3.4.json) only needs adding to VERSIONS and a rerun.

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const VERSIONS = ["3.0", "3.1", "3.2", "3.3"];

/** UI language -> Localization.csv column. English is the tables themselves. */
const COLUMNS = {
  de: "german",
  es: "spanish",
  fr: "french",
  it: "italian",
  ja: "japanese",
  ko: "koreana",
  pl: "polish",
  "pt-BR": "brazilian",
  ru: "russian",
  tr: "turkish",
  "zh-Hans": "schinese",
  "zh-Hant": "tchinese",
};

const args = process.argv.slice(2);
const flag = (name) => {
  const i = args.indexOf(name);
  return i < 0 ? undefined : args.splice(i, 2)[1];
};
const fillsDir = flag("--fills");
const missingDir = flag("--missing");
const csvPath = args[0];
if (!csvPath || !existsSync(csvPath)) {
  console.error("usage: node scripts/gen-sandbox-i18n.mjs <Localization.csv> [--fills <dir>] [--missing <dir>]");
  process.exit(1);
}

const sandboxDir = join(dirname(fileURLToPath(import.meta.url)), "..", "src", "Kitsune7Den", "Resources", "Sandbox");
const readJson = (p) => JSON.parse(readFileSync(p, "utf8").replace(/^﻿/, ""));

// ------------------------------------------------------------ the English

/** Every translatable string as [path, english]; path[0] is the version key ("v3_3"). */
function englishLeaves() {
  const out = [];
  for (const v of VERSIONS) {
    const t = readJson(join(sandboxDir, `sandbox-${v}.json`));
    const key = "v" + v.replace(".", "_");
    const add = (path, english) => {
      // "25%", "10" - nothing to translate
      if (english && /\p{L}/u.test(english)) out.push([[key, ...path], english]);
    };
    for (const [cat, label] of Object.entries(t.categoryLabels ?? {})) add(["categories", cat], label);
    for (const o of t.options ?? []) {
      add(["options", o.name, "label"], o.label);
      add(["options", o.name, "description"], o.description);
    }
    for (const [name, set] of Object.entries(t.valueSets ?? {}))
      set.values.forEach((val, i) => add(["values", name, String(i)], val.label));
    const groups = new Map();
    for (const p of t.presets ?? []) {
      add(["presets", p.name, "label"], p.label);
      add(["presets", p.name, "description"], p.description);
      groups.set(p.group, p.groupLabel);
    }
    for (const [g, label] of groups) add(["presetGroups", g], label);
  }
  return out;
}

// ---------------------------------------------------------- the game's csv

/** RFC4180-ish CSV -> rows (the game's file quotes embedded commas and newlines). */
function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = "";
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quoted) {
      if (c === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else quoted = false;
      } else field += c;
    } else if (c === '"') quoted = true;
    else if (c === ",") {
      row.push(field);
      field = "";
    } else if (c === "\n") {
      row.push(field);
      rows.push(row);
      row = [];
      field = "";
    } else if (c !== "\r") field += c;
  }
  if (field !== "" || row.length) rows.push([...row, field]);
  return rows;
}

/** The same folding extract-sandbox-options applies to the tables' English. */
const clean = (s) =>
  String(s)
    .replace(/[‘’ʼ]/g, "'")
    .replace(/[“”]/g, '"')
    .replace(/[–—]/g, "-")
    .replace(/…/g, "...")
    .replace(/[  ]/g, " ")
    .replace(/[^\x20-\x7e]/g, "")
    .replace(/\s+/g, " ")
    .trim();

/** cleaned english -> the csv row, preferring the Sandbox menu's go* rows, then UI rows. */
function gameStrings() {
  const rows = parseCsv(readFileSync(csvPath, "utf8").replace(/^﻿/, ""));
  const head = rows[0].map((h) => h.trim().toLowerCase());
  const en = head.indexOf("english");
  const cols = Object.fromEntries(Object.entries(COLUMNS).map(([l, c]) => [l, head.indexOf(c)]));
  for (const [l, i] of Object.entries(cols)) if (i < 0) throw new Error(`no ${COLUMNS[l]} column in ${csvPath}`);
  const best = new Map();
  for (const row of rows.slice(1)) {
    if (!row[en]) continue;
    const text = clean(row[en]);
    const score = ((row[0] ?? "").startsWith("go") ? 2 : 0) + (row[1] === "UI" ? 1 : 0);
    const prev = best.get(text);
    if (!prev || score > prev.score) best.set(text, { score, row });
  }

  // "{0} Days" -> /^(\d+(?:-\d+)?) Days$/, for value labels built from a display format
  const escape = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const formats = [...best.entries()]
    .filter(([text]) => /^[^{}]*\{0\}[^{}]*$/.test(text) && /\p{L}/u.test(text))
    .sort((a, b) => b[1].score - a[1].score)
    .map(([text, hit]) => ({
      re: new RegExp("^" + text.split("{0}").map(escape).join("(\\d+(?:-\\d+)?)") + "$"),
      row: hit.row,
    }));

  return (english, lang) => {
    const exact = best.get(clean(english))?.row[cols[lang]]?.trim();
    if (exact) return exact;
    for (const f of formats) {
      const m = clean(english).match(f.re);
      const fmt = m && f.row[cols[lang]]?.trim();
      if (fmt && fmt.includes("{0}")) return fmt.replaceAll("{0}", m[1]);
    }
    return undefined;
  };
}

// ------------------------------------------------------------------ trees

const get = (tree, path) => path.reduce((n, k) => (n && typeof n === "object" ? n[k] : undefined), tree);

function set(tree, path, value) {
  let n = tree;
  for (const k of path.slice(0, -1)) n = n[k] ??= {};
  n[path.at(-1)] = value;
}

/** Keys sorted at every level, so reruns diff cleanly. */
function sorted(tree) {
  if (!tree || typeof tree !== "object") return tree;
  return Object.fromEntries(
    Object.keys(tree)
      .sort((a, b) => (a < b ? -1 : a > b ? 1 : 0))
      .map((k) => [k, sorted(tree[k])]),
  );
}

// ------------------------------------------------------------------- run

const leaves = englishLeaves();
const game = gameStrings();
if (missingDir) mkdirSync(missingDir, { recursive: true });

for (const lang of Object.keys(COLUMNS)) {
  const file = join(sandboxDir, `sandbox.${lang}.json`);
  const have = existsSync(file) ? readJson(file) : {};
  const fillsFile = fillsDir && join(fillsDir, `${lang}.json`);
  const fills = fillsFile && existsSync(fillsFile) ? readJson(fillsFile) : {};

  const out = {};
  const byEnglish = new Map();
  const counts = { kept: 0, game: 0, reused: 0, filled: 0 };
  const missing = [];

  // Pass 1: what's there already and what the game has
  const pending = [];
  for (const [path, english] of leaves) {
    let text = get(have, path);
    if (typeof text === "string" && text.trim()) counts.kept++;
    else if ((text = game(english, lang))) counts.game++;
    else {
      pending.push([path, english]);
      continue;
    }
    set(out, path, text);
    if (!byEnglish.has(english)) byEnglish.set(english, text);
  }
  // Pass 2: the same English elsewhere, then the hand fills
  for (const [path, english] of pending) {
    let text = byEnglish.get(english);
    if (text) counts.reused++;
    else if ((text = fills[english]?.trim())) counts.filled++;
    else {
      missing.push(english);
      continue;
    }
    set(out, path, text);
    byEnglish.set(english, text);
  }

  writeFileSync(file, JSON.stringify(sorted(out), null, 1));
  const uniqueMissing = [...new Set(missing)];
  if (missingDir) writeFileSync(join(missingDir, `${lang}.json`), JSON.stringify(uniqueMissing, null, 1) + "\n");
  console.log(
    `${lang}: ${leaves.length - missing.length}/${leaves.length} ` +
      `(${counts.kept} kept, ${counts.game} from the game, ${counts.reused} reused, ${counts.filled} filled) - ` +
      `${uniqueMissing.length} distinct strings left in English`,
  );
}
