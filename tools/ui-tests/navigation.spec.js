// Calm page switching (rules, webhooks): what a switch does after it was refused or the user moved on, against the
// web demo with a mocked clock. Every switch here is to a page two or more away, so it fades on the mocked clock
// instead of scrolling smoothly in real time.
const { test, expect } = require('@playwright/test');

const PAGES = ['one', 'two', 'three', 'four', 'five'].map((id) => ({ id, title: id[0].toUpperCase() + id.slice(1),
  widgets: [{ id: 'n-' + id, type: 'note', x: 0, y: 0, w: 12, h: 6, options: { text: 'Page ' + id } }] }));

async function show(page) {
  await page.clock.install();
  await page.goto('/panel/');
  await page.evaluate((pages) => {
    localStorage.clear();
    localStorage.setItem('pb-demo-config', JSON.stringify({
      night: { enabled: false }, rules: [], shortcuts: [], pages,
      display: { idleReturnSec: 0, minDwellSec: 0, touchGraceSec: 20, switchCountdownSec: 3 },
    }));
  }, PAGES);
  await page.reload();
  await expect(page.locator('#dots i')).toHaveCount(PAGES.length);
}

const activeDot = (page) => page.locator('#dots i').evaluateAll((d) => d.findIndex((x) => x.classList.contains('on')));
// fastForward fires each due timer at most once per call, so the panel's 250 ms and 1 s timers are stepped through
async function step(page, ms) { for (let t = 0; t < ms; t += 250) await page.clock.fastForward(250); }
async function stepUntil(page, fn, max = 10_000) {
  for (let t = 0; t < max; t += 250) { if (await fn()) return; await page.clock.fastForward(250); }
  expect(await fn()).toBe(true);
}
const emit = (page, kind, data) => page.evaluate(([k, d]) => window.PhoneBoxDemo.emit(k, d), [kind, data]);
// the demo's agent never lists this rule as active, like a rule that has ended
const holdThree = (page) => emit(page, 'goto', { page: 'three', rule: 'r1', name: 'R1', hold: true, countdown: 0, priority: 5 });

test('Stay on "Back to" after a rule ends keeps the page', async ({ page }) => {
  await show(page);
  await holdThree(page);
  await stepUntil(page, async () => (await activeDot(page)) === 2);
  await emit(page, 'back', { rule: 'r1' });
  await stepUntil(page, async () => /Back to/.test(await page.locator('#nvLine').textContent())
    && /\bon\b/.test(await page.locator('#navCard').getAttribute('class')));
  await page.locator('#nvStay').click();
  await step(page, 30_000);
  expect(await activeDot(page)).toBe(2);
});

test('a page opened later by hand is not undone by a rule that ended while you were elsewhere', async ({ page }) => {
  await show(page);
  await holdThree(page);
  await stepUntil(page, async () => (await activeDot(page)) === 2);
  await page.keyboard.press('ArrowRight');
  await emit(page, 'back', { rule: 'r1' });
  await step(page, 30_000);
  expect(await activeDot(page)).toBe(3);
  await page.keyboard.press('ArrowLeft');
  await step(page, 40_000);
  expect(await activeDot(page)).toBe(2);
});

test('a notification with a malformed countdown switches after the default one and blocks nothing', async ({ page }) => {
  await show(page);
  await emit(page, 'notify', { title: 'Deploy', text: 'done', page: 'three', countdown: 'soon' });
  await step(page, 6000);
  expect(await activeDot(page)).toBe(2);
  await emit(page, 'notify', { title: 'Deploy', text: 'done', page: 'five', countdown: 1e9 }); // at most 30 s
  await step(page, 35_000);
  expect(await activeDot(page)).toBe(4);
});

test('a notification whose countdown is an object is shown, and the next event still arrives', async ({ page }) => {
  await show(page);
  // valid JSON, but Number() of it throws ("Cannot convert object to primitive value")
  await emit(page, 'notify', { title: 'Odd', text: 'object countdown', page: 'three', countdown: { toString: null } });
  await expect(page.locator('#toast')).toContainText('object countdown');
  await step(page, 6000);
  expect(await activeDot(page)).toBe(2);
  await emit(page, 'notify', { title: 'Next', text: 'still delivered' });
  await expect(page.locator('#toast')).toContainText('still delivered');
});
