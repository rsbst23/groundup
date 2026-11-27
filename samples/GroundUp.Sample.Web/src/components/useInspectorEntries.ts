import { useSyncExternalStore } from 'react';
import { getInspectorSnapshot, subscribeInspector, type InspectorEntry } from '../api/inspector';

/** The recorded API calls, newest first; re-renders when a call is recorded or completed. */
export function useInspectorEntries(): readonly InspectorEntry[] {
  return useSyncExternalStore(subscribeInspector, getInspectorSnapshot);
}
