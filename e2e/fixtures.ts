import { test as base } from '@playwright/test';

export * from '@playwright/test';

/**
 * CI runs several times slower than a developer machine, and the first request after the API starts is the slowest, so a test that acts on
 * a control before its data has arrived passes locally by luck. With E2E_API_LATENCY_MS set, every /api response of the browser is delayed
 * by that many milliseconds: the specs of the flows that load data when a page opens run this way in CI (see CLAUDE.md, E2E tests).
 */
const latenciaMs = Number(process.env.E2E_API_LATENCY_MS ?? 0);

export const test = base.extend({
  context: async ({ context }, use) => {
    if (latenciaMs > 0) {
      await context.route('**/api/**', async (route) => {
        await new Promise((resolve) => setTimeout(resolve, latenciaMs));
        await route.continue();
      });
    }
    await use(context);
  },
});
