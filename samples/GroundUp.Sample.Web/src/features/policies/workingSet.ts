/**
 * A policy's permission keys while they are edited (GU-97d): the saved set, the unsaved additions and
 * the unsaved removals. Pure functions; each returns a new set.
 */
export interface WorkingSet {
  saved: ReadonlySet<string>;
  added: ReadonlySet<string>;
  removed: ReadonlySet<string>;
}

/** A working set with no changes. */
export function startWorkingSet(saved: Iterable<string>): WorkingSet {
  return { saved: new Set(saved), added: new Set(), removed: new Set() };
}

/** Adds a key: a removed saved key is restored; a new key becomes an unsaved addition. */
export function addKey(ws: WorkingSet, key: string): WorkingSet {
  if (ws.removed.has(key)) {
    return undoRemoval(ws, key);
  }
  if (ws.saved.has(key) || ws.added.has(key)) {
    return ws;
  }
  return { ...ws, added: new Set([...ws.added, key]) };
}

/** Adds several keys (for "Add all shown"). */
export function addKeys(ws: WorkingSet, keys: Iterable<string>): WorkingSet {
  let next = ws;
  for (const key of keys) {
    next = addKey(next, key);
  }
  return next;
}

/** Removes a key: an unsaved addition is dropped; a saved key becomes an unsaved removal. */
export function removeKey(ws: WorkingSet, key: string): WorkingSet {
  if (ws.added.has(key)) {
    const added = new Set(ws.added);
    added.delete(key);
    return { ...ws, added };
  }
  if (!ws.saved.has(key) || ws.removed.has(key)) {
    return ws;
  }
  return { ...ws, removed: new Set([...ws.removed, key]) };
}

/** Takes a key out of the unsaved removals. */
export function undoRemoval(ws: WorkingSet, key: string): WorkingSet {
  if (!ws.removed.has(key)) {
    return ws;
  }
  const removed = new Set(ws.removed);
  removed.delete(key);
  return { ...ws, removed };
}

/** The keys the policy would have if saved now. */
export function currentKeys(ws: WorkingSet): Set<string> {
  const keys = new Set([...ws.saved].filter((key) => !ws.removed.has(key)));
  for (const key of ws.added) {
    keys.add(key);
  }
  return keys;
}

/** Whether there is anything to save. */
export function isDirty(ws: WorkingSet): boolean {
  return ws.added.size > 0 || ws.removed.size > 0;
}

/** The unsaved changes, sorted. */
export function changes(ws: WorkingSet): { added: string[]; removed: string[] } {
  return { added: [...ws.added].sort(), removed: [...ws.removed].sort() };
}

/** The rows of the "In this policy" side: the current keys and the removed ones, each with its state. */
export function workingRows(ws: WorkingSet): { key: string; state: 'saved' | 'added' | 'removed' }[] {
  return [
    ...[...ws.saved].map((key) => ({ key, state: ws.removed.has(key) ? ('removed' as const) : ('saved' as const) })),
    ...[...ws.added].map((key) => ({ key, state: 'added' as const })),
  ];
}
