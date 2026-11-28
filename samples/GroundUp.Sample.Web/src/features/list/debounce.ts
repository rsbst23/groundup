import { useEffect, useRef, useState } from 'react';

/** How long the search box waits after the last key press before it searches. */
export const SEARCH_DEBOUNCE_MS = 300;

/** A debounced function: `call` restarts the wait, `cancel` drops a pending call. */
export interface Debounced<A extends unknown[]> {
  call: (...args: A) => void;
  cancel: () => void;
}

/** Calls `fn` once `ms` have passed without another `call`, with the last arguments. */
export function debounce<A extends unknown[]>(fn: (...args: A) => void, ms: number): Debounced<A> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  return {
    call: (...args: A) => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        timer = undefined;
        fn(...args);
      }, ms);
    },
    cancel: () => {
      clearTimeout(timer);
      timer = undefined;
    },
  };
}

/**
 * A debounced callback for a component. It always calls the latest `fn`, and a pending call is
 * dropped when the component unmounts.
 */
export function useDebouncedCallback<A extends unknown[]>(fn: (...args: A) => void, ms: number): Debounced<A> {
  const latest = useRef(fn);
  useEffect(() => {
    latest.current = fn;
  }, [fn]);

  // The debounced runner is created once; each call hands it the latest callback to run
  const [runner] = useState(() => debounce((run: () => void) => run(), ms));

  useEffect(() => () => runner.cancel(), [runner]);
  return {
    call: (...args: A) => runner.call(() => latest.current(...args)),
    cancel: runner.cancel,
  };
}
