import type { PermissionModuleDto } from '../../api/types';

/** The selected permission keys after clicking one checkbox. */
export function toggleKey(selected: ReadonlySet<string>, key: string): Set<string> {
  const next = new Set(selected);
  if (next.has(key)) {
    next.delete(key);
  } else {
    next.add(key);
  }
  return next;
}

/** A module heading's checkbox: all, some (indeterminate) or none of its permissions selected. */
export function moduleState(module: PermissionModuleDto, selected: ReadonlySet<string>): 'all' | 'some' | 'none' {
  const count = module.permissions.filter((p) => selected.has(p.key)).length;
  if (count === 0) {
    return 'none';
  }
  return count === module.permissions.length ? 'all' : 'some';
}

/** The selected keys after clicking a module heading: all of them unless all were selected, then none. */
export function toggleModule(module: PermissionModuleDto, selected: ReadonlySet<string>): Set<string> {
  const next = new Set(selected);
  const selectAll = moduleState(module, selected) !== 'all';
  for (const permission of module.permissions) {
    if (selectAll) {
      next.add(permission.key);
    } else {
      next.delete(permission.key);
    }
  }
  return next;
}

/** Whether the permission set changed (order does not matter). */
export function keysChanged(before: readonly string[], after: ReadonlySet<string>): boolean {
  return before.length !== after.size || before.some((key) => !after.has(key));
}

/** The keys to send, sorted so requests are stable. */
export function sortedKeys(selected: ReadonlySet<string>): string[] {
  return [...selected].sort();
}
