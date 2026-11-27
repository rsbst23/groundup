import type { SessionSearch } from './sessionSearch';

export interface LandingAction {
  /** Where to go after handling the outcome; null means stay on the Session page (with the address cleared). */
  navigateTo: string | null;
  /** Open the organization picker dialog (signed in, an organization must be chosen). */
  openPicker: boolean;
}

/**
 * What the Session page does when the sign-in callback brings the browser back (GU-89):
 * a successful sign-in, with or without an organization to choose, continues to the page the
 * user started from; choosing happens in the picker dialog there. With no organization at all,
 * or after an error, the user stays on the Session page (the create form and the explanation are there).
 */
export function landingAction(outcome: SessionSearch): LandingAction {
  if (outcome.error) {
    return { navigateTo: null, openPicker: false };
  }
  switch (outcome.result) {
    case 'signed_in':
      return { navigateTo: outcome.next ?? null, openPicker: false };
    case 'tenant_picker':
      return { navigateTo: outcome.next ?? null, openPicker: true };
    default:
      return { navigateTo: null, openPicker: false };
  }
}
