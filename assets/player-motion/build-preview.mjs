import ts from 'typescript';
import fs from 'node:fs';
import path from 'node:path';

const srcDir = 'assets/player-motion';
const outDir = 'assets/player-motion/preview';
fs.mkdirSync(outDir, { recursive: true });

const files = ['skeleton.ts', 'pose.ts', 'motion.ts', 'view.ts', 'input.ts', 'preview-entry.ts'];

for (const f of files) {
  const full = path.join(srcDir, f);
  const src = fs.readFileSync(full, 'utf8');
  const out = ts.transpileModule(src, {
    compilerOptions: {
      module: ts.ModuleKind.ESNext,
      target: ts.ScriptTarget.ES2020,
      isolatedModules: true,
    },
    fileName: full,
  }).outputText;

  // 浏览器不能解析 .ts 扩展名，改成 .js
  const fixed = out.replace(/(from\s+['"])(\.{1,2}\/[^'"]+)\.ts(['"])/g, '$1$2.js$3');

  fs.writeFileSync(path.join(outDir, f.replace(/\.ts$/, '.js')), fixed);
  console.log('built', f, '->', fixed.length, 'bytes');
}
