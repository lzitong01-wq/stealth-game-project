/**
 * 把动作模块转译成浏览器可直接运行的 JS，并生成预览页。
 *
 * 用法（在项目根执行）：
 *   node assets/player-motion/build-preview.mjs
 *   cd assets/player-motion/preview && python -m http.server 15175
 *
 * 为什么不用 esbuild：本机 esbuild 缺 Windows 二进制，TypeScript 自带的
 * transpileModule 足够（无类型检查需求，单测与 project check 已覆盖）。
 */
import ts from 'typescript';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const outDir = path.join(here, 'preview');
fs.mkdirSync(outDir, { recursive: true });

const files = [
  'skeleton.ts',
  'pose.ts',
  'motion.ts',
  'view.ts',
  'view3d.ts',
  'input.ts',
  'preview-entry.ts',
];

for (const f of files) {
  const full = path.join(here, f);
  const src = fs.readFileSync(full, 'utf8');
  const out = ts.transpileModule(src, {
    compilerOptions: {
      module: ts.ModuleKind.ESNext,
      target: ts.ScriptTarget.ES2020,
      isolatedModules: true,
    },
    fileName: full,
  }).outputText;

  // 浏览器不能解析 .ts 扩展名，改成 .js；输出都在同一目录，统一成同级引用
  const fixed = out.replace(
    /(from\s+['"])(?:\.{1,2}\/)?([\w.-]+)\.ts(['"])/g,
    '$1./$2.js$3',
  );

  fs.writeFileSync(path.join(outDir, f.replace(/\.ts$/, '.js')), fixed);
  console.log('built', f, '->', fixed.length, 'bytes');
}

const html = `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>玩家动作系统 · 预览</title>
<style>
  html, body { margin: 0; height: 100%; background: #0a1114; overflow: hidden; }
</style>
</head>
<body>
<script type="module" src="./preview-entry.js"></script>
</body>
</html>
`;
fs.writeFileSync(path.join(outDir, 'index.html'), html);
console.log('built index.html ->', html.length, 'bytes');
