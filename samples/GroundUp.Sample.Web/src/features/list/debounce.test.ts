import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { debounce, SEARCH_DEBOUNCE_MS } from './debounce';

describe('debounce', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('calls once, with the last value, after the wait', () => {
    const fn = vi.fn();
    const debounced = debounce(fn, SEARCH_DEBOUNCE_MS);

    debounced.call('a');
    vi.advanceTimersByTime(200);
    debounced.call('al');
    vi.advanceTimersByTime(200);
    debounced.call('ali');

    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS - 1);
    expect(fn).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(fn).toHaveBeenCalledOnce();
    expect(fn).toHaveBeenCalledWith('ali');
  });

  it('drops a pending call when cancelled', () => {
    const fn = vi.fn();
    const debounced = debounce(fn, SEARCH_DEBOUNCE_MS);

    debounced.call('a');
    debounced.cancel();
    vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS * 2);

    expect(fn).not.toHaveBeenCalled();
  });

  it('waits 300 ms for the search box', () => {
    expect(SEARCH_DEBOUNCE_MS).toBe(300);
  });
});
