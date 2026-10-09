import { cp, mkdir, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { BROWSER_FILES } from './static-files.mjs';

const SOURCE = fileURLToPath(new URL('./', import.meta.url));
const OUTPUT = resolve(SOURCE, 'dist');

/** 只重建本项目 dist 发布目录，不打包依赖、测试、代理脚本或私有数据。 */
async function build() {
  // 删除前核实已解析的绝对目标属于本前端目录。
  if (dirname(OUTPUT) !== resolve(SOURCE)) throw new Error('发布目录必须位于前端项目内。');
  await rm(OUTPUT, { recursive: true, force: true });
  await mkdir(OUTPUT);
  await Promise.all(BROWSER_FILES.map(file => cp(resolve(SOURCE, file), resolve(OUTPUT, file))));
  await cp(resolve(SOURCE, 'assets'), resolve(OUTPUT, 'assets'), { recursive: true });
  console.log('静态发布文件已生成：frontend/dist；API 服务需独立部署。');
}
await build();
