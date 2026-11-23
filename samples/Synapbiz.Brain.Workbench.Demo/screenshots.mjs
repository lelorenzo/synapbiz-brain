// Captures the panels of the demo host in light and dark (Spanish) and once in English, plus two light pictures of the
// review line in the test table and of an item that still needs its note, and the guided editing states (the row form, a refusal,
// the draft in Read, the test cases that change, the Changes tab and the discard dialog).
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
    tabs: { read: 'Leer', simulate: 'Simular', tests: 'Casos de prueba', review: 'Revisión', changes: 'Cambios' },
    addCase: 'Agregar un caso', saveCase: 'Guardar caso', caseName: 'Nombre del caso', expected: 'Resultado que se espera',
    reviewer: 'Su nombre', note: 'Nota para quien mantiene la regla', review: 'Revisar', export: 'Exportar la revisión',
    noteCase: '¿Debe pasar este caso aunque el canal sea el correo?',
    noteRow: '¿El límite de 7 días es el correcto?', noteQuestion: '¿Debe ganar siempre el teléfono?',
    caseTitle: 'Una llamada urgente de ejemplo', person: 'Revisor de ejemplo',
    propose: 'Proponer cambios', editRow: (n) => `Editar la fila ${n}`, apply: 'Aplicar a mi borrador', cancel: 'Cancelar',
    route: 'Ruta', routeValue: 'revisión manual', queue: 'Cola', queueValue: 'Cola telefónica', addEnd: 'Agregar una fila al final',
    age: 'Antigüedad en días', atLeast: 'es como mínimo', changesTab: 'Cambios', reason: 'Motivo del cambio',
    reasonText: 'Las solicitudes de facturas deben revisarse a mano.', rowName: 'Nombre de la fila', rowNameText: 'Facturas por la web', exportProposal: 'Exportar la propuesta', discard: 'Descartar mi borrador',
  },
  en: {
    simulate: 'Simulate', clear: 'Clear', fix: 'Fix', confirm: 'Confirm', question: 'Question',
    channel: 'Contact channel', topic: 'Topic', age: 'Age in days', yes: 'Yes',
    tabs: { read: 'Read', simulate: 'Simulate', tests: 'Test cases', review: 'Review', changes: 'Changes' },
    addCase: 'Add a case', saveCase: 'Save case', caseName: 'Case name', expected: 'Expected result',
    reviewer: 'Your name', note: "Note for the rule's maintainer", review: 'Review', export: 'Export the review',
    noteCase: 'Should this case pass even when the channel is email?',
    noteRow: 'Is the 7 day limit the right one?', noteQuestion: 'Should the phone always win?',
    caseTitle: 'An urgent call made up for the test', person: 'Example reviewer',
    propose: 'Propose changes', editRow: (n) => `Edit row ${n}`, apply: 'Apply to my draft', cancel: 'Cancel',
    route: 'Route', routeValue: 'manual review', queue: 'Queue', queueValue: 'Phone queue', addEnd: 'Add a row at the end',
    age: 'Age in days', atLeast: 'is at least', changesTab: 'Changes', reason: 'Reason for the change',
    reasonText: 'Billing requests must be reviewed by hand.', rowName: 'Row name', rowNameText: 'Invoices on the web', exportProposal: 'Export the proposal', discard: 'Discard my draft',
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

async function shot(page, state, theme, locale, fullPage = true) {
  const suffix = locale === 'en' ? '-en' : '';
  await page.screenshot({ path: `${out}/wb-${state}-${theme}${suffix}.png`, fullPage });
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

/** One row of the form: the select of the line whose label starts with the text. */
function line(page, label) {
  return page.locator('.wb-rowform .wb-fline').filter({ has: page.locator('.wb-flabel', { hasText: label }) }).first();
}

async function edits(theme, locale, states) {
  const t = text[locale];
  const context = await browser.newContext({ viewport: { width: 1408, height: 1025 }, deviceScaleFactor: 2, locale: locale === 'es' ? 'es-ES' : 'en-US' });
  const page = await context.newPage();
  await page.goto(`${base}/?theme=${theme}&locale=${locale}`);
  await page.waitForSelector('.wb-root .wb-rule');
  await settle(page);

  if (states.includes('edit-refusal')) {
    // The rule that allows one row at a time: a new row for any request at least five days old overlaps the urgent one.
    await page.locator('.wb-rule').nth(3).click();
    await settle(page);
    await press(page, t.propose);
    await press(page, t.addEnd);
    await line(page, t.age).locator('select').selectOption({ label: t.atLeast });
    await settle(page);
    await line(page, t.age).locator('input').first().fill('5');
    await line(page, t.age).locator('input').first().blur();
    await settle(page);
    await press(page, t.apply);
    await shot(page, 'edit-refusal', theme, locale);
    return context.close();
  }

  await page.locator('.wb-rule').nth(1).click();
  await settle(page);
  await press(page, t.propose);
  await page.getByRole('button', { name: t.editRow(2), exact: true }).click();
  await settle(page);
  await line(page, t.route).locator('select').selectOption({ label: t.routeValue });
  await settle(page);
  if (states.includes('edit-form')) {
    await shot(page, 'edit-form', theme, locale);
  }

  await press(page, t.apply);
  await page.getByRole('button', { name: t.editRow(1), exact: true }).click();
  await settle(page);
  await line(page, t.queue).locator('select').selectOption({ label: t.queueValue });
  await settle(page);
  await press(page, t.apply);
  if (states.includes('edit-draft')) {
    await shot(page, 'edit-draft', theme, locale);
  }

  await openTab(page, t, 'tests');
  if (states.includes('edit-cases')) {
    await shot(page, 'edit-cases', theme, locale);
  }

  if (states.includes('edit-changes')) {
    while (await page.locator('.wb-ask .wb-btn-primary').count() > 0) {
      await page.locator('.wb-ask .wb-btn-primary').first().click();
      await settle(page);
    }

    // A row whose only change is its name is listed too.
    await openTab(page, t, 'read');
    await page.getByRole('button', { name: t.editRow(3), exact: true }).click();
    await settle(page);
    await line(page, t.rowName).locator('input').fill(t.rowNameText);
    await line(page, t.rowName).locator('input').blur();
    await settle(page);
    await press(page, t.apply);
    await openTab(page, t, 'changes');
    await setText(page.getByLabel(t.reason, { exact: true }), t.reasonText, page);
    await setText(page.getByLabel(t.reviewer, { exact: true }), t.person, page);
    await press(page, t.exportProposal);
    await shot(page, 'edit-changes', theme, locale);
  }

  if (states.includes('edit-discard')) {
    await press(page, t.discard);
    // The scrim is fixed to the window, so the picture is the window and not the whole page.
    await page.evaluate(() => window.scrollTo(0, 0));
    await settle(page);
    await shot(page, 'edit-discard', theme, locale, false);
  }

  await context.close();
}

for (const theme of ['light', 'dark']) {
  await session(theme, 'es', ['read', 'simulate', 'error', 'refusal', 'tests', 'review']);
}
await session('light', 'en', ['simulate']);
await session('light', 'es', ['tests-review']);
await session('light', 'es', ['review-note-missing']);
for (const theme of ['light', 'dark']) {
  await edits(theme, 'es', ['edit-refusal']);
  await edits(theme, 'es', ['edit-form', 'edit-draft', 'edit-cases', 'edit-changes', 'edit-discard']);
}
await edits('light', 'en', ['edit-draft', 'edit-cases']);

await browser.close();
