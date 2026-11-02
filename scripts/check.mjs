import { readFile } from 'node:fs/promises';

const html = await readFile(new URL('../dist/index.html', import.meta.url), 'utf8');
const css = await readFile(new URL('../dist/styles.css', import.meta.url), 'utf8');
const js = await readFile(new URL('../dist/app.js', import.meta.url), 'utf8');

const checks = [
  ['login screen', html.includes('id="login-form"')],
  ['application shell', html.includes('id="app-view"')],
  ['receptionist account', js.includes('reception@pawcare.test')],
  ['manager account', js.includes('manager@pawcare.test')],
  ['inquiry module', js.includes("page: 'inquiries'") && js.includes('openInquiry')],
  ['promotional offers module', js.includes("page: 'promotions'") && js.includes('openPromotion')],
  ['manager satisfaction module', js.includes("page: 'satisfaction'")],
  ['manager feedback module', js.includes("page: 'feedback'")],
  ['manager reports module', js.includes("page: 'reports'")],
  ['brand gold token', css.includes('--gold:#d4b87a')],
  ['brand sage token', css.includes('--sage:#7f8b6c')],
  ['responsive layout', css.includes('@media(max-width:820px)')],
  ['local persistence', js.includes('localStorage.setItem')]
];

const failed = checks.filter(([, ok]) => !ok);
for (const [label, ok] of checks) console.log(`${ok ? 'PASS' : 'FAIL'}  ${label}`);
if (failed.length) process.exit(1);
console.log(`\n${checks.length} checks passed.`);
