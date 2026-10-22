import { expect, test } from '../fixtures';
import { abrirDialogo, loginYEsperarInicio } from '../helpers';

// The CI failure that led to the dialog helpers (main, run of PR #19): text typed into the second field of a dialog right after it opened
// ended up in the first one. These tests check what a person on a slow phone does: click another field and type while the dialog is
// still opening, and see that the app never moves the focus under them.
test.describe('dialogs: the focus does not jump while someone types', () => {
  test('a click on the second field while the dialog is still opening keeps the typed text in it', async ({ page }) => {
    await loginYEsperarInicio(page);
    await page.getByRole('button', { name: 'Ajustes' }).click();
    await page.getByRole('menuitem', { name: 'Usuarios' }).click();
    await expect(page.getByRole('heading', { name: 'Usuarios', exact: true })).toBeVisible();

    // Slow every animation down 20 times: the dialog takes seconds to open, so a click and some typing fit inside the opening.
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Animation.enable');
    await cdp.send('Animation.setPlaybackRate', { playbackRate: 0.05 });

    await page.getByRole('button', { name: 'Nuevo usuario' }).click();
    // The first field gets the focus when the dialog opens; click the second one right after, without waiting for the animation.
    await page.waitForFunction(() => document.activeElement?.id === 'username');
    const nombre = page.getByRole('dialog').getByLabel('Nombre');
    const caja = await nombre.boundingBox();
    expect(caja).not.toBeNull();
    await page.mouse.click(caja!.x + caja!.width / 2, caja!.y + caja!.height / 2);
    await page.keyboard.type('Persona de prueba', { delay: 40 });

    await cdp.send('Animation.setPlaybackRate', { playbackRate: 1 });
    const dialogo = page.getByRole('dialog');
    await expect(dialogo.getByLabel('Nombre')).toHaveValue('Persona de prueba');
    await expect(dialogo.getByLabel('Usuario')).toHaveValue('');
    await expect(dialogo.getByLabel('Nombre')).toBeFocused();
  });

  test('the helper opens a dialog with the focus already inside it', async ({ page }) => {
    await loginYEsperarInicio(page);
    await page.getByRole('button', { name: 'Ajustes' }).click();
    await page.getByRole('menuitem', { name: 'Usuarios' }).click();
    await expect(page.getByRole('heading', { name: 'Usuarios', exact: true })).toBeVisible();

    const dialogo = await abrirDialogo(page.getByRole('button', { name: 'Nuevo usuario' }));
    await expect(dialogo.getByLabel('Usuario')).toBeFocused();
  });
});
