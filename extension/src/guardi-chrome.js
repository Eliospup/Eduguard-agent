import { getBrowserTheme, getModeUi } from "./mode-ui.js";

/**
 * Apply or remove Guardi browser chrome based on runtime active flag and agent mode.
 * @param {typeof chrome} api
 * @param {boolean} active
 * @param {string} [uiMode]
 */
function defaultActionTitle(api) {
  const manifest = api.runtime.getManifest?.() || {};
  return (
    manifest.action?.default_title ||
    manifest.browser_action?.default_title ||
    manifest.name ||
    ""
  );
}

export function syncGuardiChrome(api, active, uiMode = "sub", showChrome = true) {
  const actionApi = api.action || api.browserAction;
  if (active && showChrome) {
    api.theme?.update?.(getBrowserTheme(uiMode))?.catch?.(() => {});
    const title = getModeUi(uiMode).copy.actionTitle;
    if (title && actionApi?.setTitle) {
      actionApi.setTitle({ title }).catch(() => {});
    }
  } else {
    api.theme?.reset?.()?.catch?.(() => {});
    const title = defaultActionTitle(api);
    if (title && actionApi?.setTitle) {
      actionApi.setTitle({ title }).catch(() => {});
    }
  }
}

/** @deprecated Use syncGuardiChrome — kept for callers that only install once. */
export function installGuardiChrome(api) {
  syncGuardiChrome(api, true);
}
