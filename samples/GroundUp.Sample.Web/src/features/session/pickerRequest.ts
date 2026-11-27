/**
 * Asks the app-wide organization picker dialog to open. Kept in memory, so it survives the
 * client-side navigation from the Session page back to the page the sign-in started from.
 * The dialog opens on its own only right after a sign-in that needs a choice; otherwise the user
 * opens it from the banner.
 */
let open = false;
const listeners = new Set<() => void>();

function publish(next: boolean): void {
  open = next;
  listeners.forEach((listener) => listener());
}

export function requestTenantPicker(): void {
  publish(true);
}

export function closeTenantPicker(): void {
  publish(false);
}

export function subscribeTenantPicker(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function isTenantPickerOpen(): boolean {
  return open;
}
