// Captures the panels of the demo host in light and dark (Spanish) and once in English, plus two light pictures of the
// review line in the test table and of an item that still needs its note.
// Run the demo first:  dotnet run -c Release --urls http://127.0.0.1:5219
// Then:                node screenshots.mjs [baseUrl] [outputFolder]
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');

const base = process.argv[2] ?? 'http://127.0.0.1:5219';
const out = process.argv[3] ?? '/mnt/project-files/app-local/reglas/capturas';
mkdirSync(out, { recursive: true });

const text = {
  es: {
    simulate: 'Simular', clear: 'Limpiar', fix: 'Corregir', confirm: 'Confirmar', question: 'Pregunta',
    channel: 'Canal de contacto', topic: 'Tema', age: 'Antigüedad en días', yes: 'Sí',
    tabs: { read: 'Leer', simulate: 'Simular', tests: 'Casos de prueba', review: 'Revisión' },
    addCase: 'Agregar un caso', saveCase: 'Guardar caso', caseName: 'Nombre del caso', expected: 'Resultado que se espera',
    reviewer: 'Su nombre', note: 'Nota para quien mantiene la regla', review: 'Revisar', export: 'Exportar la revisión',
    noteCase: '¿Debe pasar este caso aunque el canal sea el correo?',
    noteRow: '¿El límite de 7 días es el correcto?', noteQuestion: '¿Debe ganar siempre el teléfono?',
    caseTitle: 'Una llamada urgente de ejemplo', person: 'Revisor de ejemplo',
  },
  en: {
    simulate: 'Simulate', clear: 'Clear', fix: 'Fix', confirm: 'Confirm', question: 'Question',
    channel: 'Contact channel', topic: 'Topic', age: 'Age in days', yes: 'Yes',
    tabs: { read: 'Read', simulate: 'Simulate', tests: 'Test cases', review: 'Review' },
    addCase: 'Add a case', saveCase: 'Save case', caseName: 'Case name', expected: 'Expected result',
    reviewer: 'Your name', note: "Note for the rule's maintainer", review: 'Review', export: 'Export the review',
    noteCase: 'Should this case pass even when the channel is email?',
    noteRow: 'Is the 7 day limit the right one?', noteQuestion: 'Should the phone always win?',
    caseTitle: 'An urgent call made up for the test', person: 'Example reviewer',
  },
};

const browser = await chromium.launch();

async function settle(page) {
  await page.waitForTimeout(250);
}

async function setText(locator, value, page) {
  await locator.fill(value);
  await locator.blur();
  await settle(page);
}

async function press(page, name) {
  await page.getByRole('button', { name, exact: true }).first().click();
  await settle(page);
}

async function openTab(page, t, key) {
  await page.getByRole('tab', { name: t.tabs[key] }).click();
  await settle(page);
}

async function shot(page, state, theme, locale) {
  const suffix = locale === 'en' ? '-en' : '';
  await page.screenshot({ path: `${out}/wb-${state}-${theme}${suffix}.png`, fullPage: true });
  console.log(`wrote wb-${state}-${theme}${suffix}.png`);
}

async function fillIntake(page, t, { channel, topic, urgent, age }) {
  await page.getByLabel(t.channel, { exact: true }).selectOption(channel);
  await settle(page);
  await setText(page.getByLabel(t.topic, { exact: true }), topic, page);
  const group = page.locator('.wb-radios').first();
  await group.getByLabel(urgent ? t.yes : 'No', { exact: true }).check();
  await settle(page);
  await setText(page.getByLabel(t.age, { exact: true }), age, page);
}

async function session(theme, locale, states) {
  const t = text[locale];
  const context = await browser.newContext({ viewport: { width: 1408, height: 1025 }, deviceScaleFactor: 2, locale: locale === 'es' ? 'es-ES' : 'en-US' });
  const page = await context.newPage();
  await page.goto(`${base}/?theme=${theme}&locale=${locale}`);
  await page.waitForSelector('.wb-root .wb-rule');
  await settle(page);

  // The routing rule (the second entry of the list) is the one the pictures show.
  await page.locator('.wb-rule').nth(1).click();
  await settle(page);

  if (states.includes('read')) {
    // One row marked "fix" with a note typed.
    await page.locator('.wb-row .wb-rv').nth(3).getByRole('button', { name: t.fix, exact: true }).click();
    await settle(page);
    await setText(page.getByLabel(t.note, { exact: true }).first(), t.noteRow, page);
    await shot(page, 'read', theme, locale);
  }

  if (states.includes('simulate')) {
    await openTab(page, t, 'simulate');
    await fillIntake(page, t, { channel: 'Phone', topic: 'billing-late', urgent: true, age: '100' });
    await press(page, t.simulate);
    await shot(page, 'simulate', theme, locale);
  }

  if (states.includes('error')) {
    await openTab(page, t, 'simulate');
    await setText(page.getByLabel(t.topic, { exact: true }), '', page);
    await setText(page.getByLabel(t.age, { exact: true }), 'abc', page);
    await press(page, t.simulate);
    await shot(page, 'error', theme, locale);
  }

  if (states.includes('refusal')) {
    // The rule that allows one row at a time: a phone call that is also urgent matches two rows.
    await page.locator('.wb-rule').nth(3).click();
    await settle(page);
    await openTab(page, t, 'simulate');
    await fillIntake(page, t, { channel: 'Phone', topic: 'billing-late', urgent: true, age: '3' });
    await press(page, t.simulate);
    await shot(page, 'refusal', theme, locale);
    await page.locator('.wb-rule').nth(1).click();
    await settle(page);
  }

  if (states.includes('tests')) {
    await openTab(page, t, 'tests');
    await press(page, t.addCase);
    await setText(page.getByLabel(t.caseName, { exact: true }), t.caseTitle, page);
    await fillIntake(page, t, { channel: 'Phone', topic: 'billing-late', urgent: true, age: '3' });
    // An expectation the rule does not meet, so the proposed case fails.
    await page.getByLabel(t.expected, { exact: true }).selectOption({ index: 3 });
    await settle(page);
    await press(page, t.saveCase);
    await shot(page, 'tests', theme, locale);
  }

  if (states.includes('tests-review')) {
    // A case row with its review line open: the third case, marked "fix" with a note typed.
    await openTab(page, t, 'tests');
    await page.locator('.wb-table tbody tr').nth(2).getByRole('button', { name: t.review, exact: true }).click();
    await settle(page);
    await page.locator('.wb-review-line .wb-rv').getByRole('button', { name: t.fix, exact: true }).click();
    await settle(page);
    await setText(page.locator('.wb-review-line').getByLabel(t.note, { exact: true }), t.noteCase, page);
    await shot(page, 'tests-review', theme, locale);
  }

  if (states.includes('review-note-missing')) {
    // A fix and a question with no note: the Review tab marks them and the export is held.
    await openTab(page, t, 'read');
    await page.locator('.wb-row .wb-rv').nth(1).getByRole('button', { name: t.fix, exact: true }).click();
    await settle(page);
    await page.locator('.wb-row .wb-rv').nth(2).getByRole('button', { name: t.question, exact: true }).click();
    await settle(page);
    await openTab(page, t, 'review');
    await setText(page.getByLabel(t.reviewer, { exact: true }), t.person, page);
    await press(page, t.export);
    await shot(page, 'review-note-missing', theme, locale);
  }

  if (states.includes('review')) {
    await openTab(page, t, 'read');
    await page.locator('.wb-row .wb-rv').nth(0).getByRole('button', { name: t.confirm, exact: true }).click();
    await settle(page);
    await page.locator('.wb-row .wb-rv').nth(1).getByRole('button', { name: t.question, exact: true }).click();
    await settle(page);
    await setText(page.getByLabel(t.note, { exact: true }).nth(1), t.noteQuestion, page);
    await openTab(page, t, 'review');
    await setText(page.getByLabel(t.reviewer, { exact: true }), t.person, page);
    await shot(page, 'review', theme, locale);
  }

  await context.close();
}

for (const theme of ['light', 'dark']) {
  await session(theme, 'es', ['read', 'simulate', 'error', 'refusal', 'tests', 'review']);
}
await session('light', 'en', ['simulate']);
await session('light', 'es', ['tests-review']);
await session('light', 'es', ['review-note-missing']);

await browser.close();
