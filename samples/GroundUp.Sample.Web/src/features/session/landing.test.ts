import { describe, expect, it } from 'vitest';
import { landingAction } from './landing';
import { closeTenantPicker, isTenantPickerOpen, requestTenantPicker, subscribeTenantPicker } from './pickerRequest';

describe('landingAction', () => {
  it('signed in: continue to the starting page', () => {
    expect(landingAction({ result: 'signed_in', next: '/members' })).toEqual({ navigateTo: '/members', openPicker: false });
  });

  it('signed in without a starting page: stay on the Session page', () => {
    expect(landingAction({ result: 'signed_in' })).toEqual({ navigateTo: null, openPicker: false });
  });

  it('organization to choose: continue to the starting page and open the picker there', () => {
    expect(landingAction({ result: 'tenant_picker', next: '/members' })).toEqual({ navigateTo: '/members', openPicker: true });
    expect(landingAction({ result: 'tenant_picker' })).toEqual({ navigateTo: null, openPicker: true });
  });

  it('no organization: stay on the Session page (the create form is there)', () => {
    expect(landingAction({ result: 'no_tenant', next: '/members' })).toEqual({ navigateTo: null, openPicker: false });
  });

  it('an error: stay on the Session page, even with a starting page', () => {
    expect(landingAction({ error: 'FLOW_EXPIRED', next: '/members' })).toEqual({ navigateTo: null, openPicker: false });
  });
});

describe('picker request', () => {
  it('opens and closes, notifying subscribers', () => {
    let notified = 0;
    const unsubscribe = subscribeTenantPicker(() => notified++);

    requestTenantPicker();
    expect(isTenantPickerOpen()).toBe(true);
    closeTenantPicker();
    expect(isTenantPickerOpen()).toBe(false);
    unsubscribe();

    expect(notified).toBe(2);
  });
});
