import InputAdornment from '@mui/material/InputAdornment';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TablePagination from '@mui/material/TablePagination';
import TableRow from '@mui/material/TableRow';
import TableSortLabel from '@mui/material/TableSortLabel';
import TextField from '@mui/material/TextField';
import SearchIcon from '@mui/icons-material/Search';
import { useState, type ReactNode } from 'react';
import { PAGE_SIZE } from '../../api/paging';
import { SEARCH_DEBOUNCE_MS, useDebouncedCallback } from './debounce';
import { nextSort, parseSort, type ListState } from './listSearch';

/** A column. With `sortField` (the API's field name) its header sorts the list. */
export interface ListColumn<T> {
  label: string;
  render: (row: T) => ReactNode;
  sortField?: string;
  align?: 'left' | 'right';
}

/** A status the filter offers: the enum number and its label. */
export interface StatusOption {
  value: number;
  label: string;
}

export interface ListTableProps<T> {
  columns: ListColumn<T>[];
  /** The page's rows; undefined while the first page loads. */
  rows: T[] | undefined;
  rowKey: (row: T) => string;
  totalRecords: number;
  state: ListState;
  /** A new view. `replace` is true while typing in the search box, so each key press is not a history entry. */
  onChange: (state: ListState, options?: { replace?: boolean }) => void;
  /** What the search covers, for the box's label ("Search email"). Omit to hide the box. */
  searchLabel?: string;
  /** The status filter's options. Omit to hide the filter. */
  statusOptions?: StatusOption[];
  emptyText: string;
}

/**
 * A paged table whose view (page, sort, search, status) is a {@link ListState} the page keeps in the
 * URL (GU-98). Sorting, searching and filtering run on the server; changing the search or the status
 * goes back to page 1.
 */
export function ListTable<T>({
  columns,
  rows,
  rowKey,
  totalRecords,
  state,
  onChange,
  searchLabel,
  statusOptions,
  emptyText,
}: ListTableProps<T>) {
  const sorted = parseSort(state.sort);

  // The box shows what is typed at once; the list follows after the debounce
  const [typed, setTyped] = useState(state.q ?? '');
  const [shownQ, setShownQ] = useState(state.q);
  if (shownQ !== state.q) {
    // The view changed from outside (back button, a link): show its search term
    setShownQ(state.q);
    setTyped(state.q ?? '');
  }
  const search = useDebouncedCallback((value: string) => {
    const q = value.trim() || undefined;
    if (q !== state.q) {
      setShownQ(q);
      onChange({ ...state, q, page: 1 }, { replace: true });
    }
  }, SEARCH_DEBOUNCE_MS);

  return (
    <>
      {(searchLabel || statusOptions) && (
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ my: 2 }}>
          {searchLabel && (
            <TextField
              size="small"
              label={searchLabel}
              value={typed}
              onChange={(event) => {
                setTyped(event.target.value);
                search.call(event.target.value);
              }}
              slotProps={{
                input: {
                  startAdornment: (
                    <InputAdornment position="start">
                      <SearchIcon fontSize="small" />
                    </InputAdornment>
                  ),
                },
              }}
              sx={{ minWidth: 260 }}
            />
          )}
          {statusOptions && (
            <TextField
              select
              size="small"
              label="Status"
              value={state.status === undefined ? '' : String(state.status)}
              onChange={(event) => {
                const value = event.target.value;
                onChange({ ...state, status: value === '' ? undefined : Number(value), page: 1 });
              }}
              sx={{ minWidth: 160 }}
            >
              <MenuItem value="">All</MenuItem>
              {statusOptions.map((option) => (
                <MenuItem key={option.value} value={String(option.value)}>
                  {option.label}
                </MenuItem>
              ))}
            </TextField>
          )}
        </Stack>
      )}

      <Table size="small">
        <TableHead>
          <TableRow>
            {columns.map((column) => {
              const active = column.sortField !== undefined && sorted?.field === column.sortField;
              return (
                <TableCell
                  key={column.label}
                  align={column.align}
                  sortDirection={active ? sorted!.direction : false}
                >
                  {column.sortField ? (
                    <TableSortLabel
                      active={active}
                      direction={active ? sorted!.direction : 'asc'}
                      onClick={() => onChange({ ...state, sort: nextSort(state.sort, column.sortField!), page: 1 })}
                    >
                      {column.label}
                    </TableSortLabel>
                  ) : (
                    column.label
                  )}
                </TableCell>
              );
            })}
          </TableRow>
        </TableHead>
        <TableBody>
          {rows === undefined && (
            <TableRow>
              <TableCell colSpan={columns.length}>Loading…</TableCell>
            </TableRow>
          )}
          {rows?.length === 0 && (
            <TableRow>
              <TableCell colSpan={columns.length}>{state.q || state.status !== undefined ? 'Nothing matches.' : emptyText}</TableCell>
            </TableRow>
          )}
          {rows?.map((row) => (
            <TableRow key={rowKey(row)}>
              {columns.map((column) => (
                <TableCell key={column.label} align={column.align}>
                  {column.render(row)}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {rows !== undefined && (
        <TablePagination
          component="div"
          count={totalRecords}
          page={Math.min(state.page - 1, Math.max(0, Math.ceil(totalRecords / PAGE_SIZE) - 1))}
          rowsPerPage={PAGE_SIZE}
          rowsPerPageOptions={[PAGE_SIZE]}
          onPageChange={(_, next) => onChange({ ...state, page: next + 1 })}
        />
      )}
    </>
  );
}
