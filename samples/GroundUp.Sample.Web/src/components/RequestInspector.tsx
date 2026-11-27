import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Drawer from '@mui/material/Drawer';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { clearInspector } from '../api/inspector';
import { useInspectorEntries } from './useInspectorEntries';

function Json({ value }: { value: unknown }) {
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
  return (
    <Box
      component="pre"
      sx={{ m: 0, p: 1, bgcolor: 'grey.100', borderRadius: 1, fontSize: 12, overflow: 'auto', maxHeight: 240 }}
    >
      {text ?? 'null'}
    </Box>
  );
}

function statusColor(status: number | null): string {
  if (status === null) return 'text.secondary';
  if (status === 0 || status >= 500) return 'error.main';
  if (status >= 400) return 'warning.main';
  return 'success.main';
}

interface RequestInspectorProps {
  open: boolean;
  onClose: () => void;
}

/** A drawer listing recent API calls with their headers and bodies. Secrets were masked when recorded. */
export function RequestInspector({ open, onClose }: RequestInspectorProps) {
  const entries = useInspectorEntries();
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const selected = entries.find((entry) => entry.id === selectedId) ?? null;

  return (
    <Drawer anchor="bottom" open={open} onClose={onClose} slotProps={{ paper: { sx: { height: '50vh' } } }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 2, py: 1 }}>
        <Typography variant="h6" sx={{ flexGrow: 1 }}>
          Requests ({entries.length})
        </Typography>
        <Button size="small" onClick={() => void navigator.clipboard.writeText(JSON.stringify(entries, null, 2))}>
          Copy as JSON
        </Button>
        <Button size="small" onClick={() => { clearInspector(); setSelectedId(null); }}>
          Clear
        </Button>
        <Button size="small" onClick={onClose}>
          Close
        </Button>
      </Box>
      <Box sx={{ display: 'flex', flexGrow: 1, minHeight: 0 }}>
        <Box sx={{ width: '50%', overflow: 'auto' }}>
          <Table size="small" stickyHeader>
            <TableHead>
              <TableRow>
                <TableCell>Method</TableCell>
                <TableCell>URL</TableCell>
                <TableCell>Status</TableCell>
                <TableCell align="right">ms</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {entries.map((entry) => (
                <TableRow
                  key={entry.id}
                  hover
                  selected={entry.id === selectedId}
                  onClick={() => setSelectedId(entry.id)}
                  sx={{ cursor: 'pointer' }}
                >
                  <TableCell>{entry.method}</TableCell>
                  <TableCell sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>{entry.url}</TableCell>
                  <TableCell sx={{ color: entry.cancelled ? 'text.secondary' : statusColor(entry.status) }}>
                    {entry.cancelled ? 'cancelled' : (entry.status ?? '…')}
                  </TableCell>
                  <TableCell align="right">{entry.durationMs ?? ''}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Box>
        <Box sx={{ width: '50%', overflow: 'auto', px: 2, pb: 2 }}>
          {selected ? (
            <>
              <Typography variant="subtitle2" sx={{ mt: 1 }}>
                {selected.method} {selected.url} → {selected.status ?? 'in flight'}
                {selected.correlationId && ` · correlation ID ${selected.correlationId}`}
              </Typography>
              {selected.cancelled && (
                <Typography color="text.secondary">
                  Cancelled by the app before a response (in development React renders pages twice, and the first
                  call is cancelled). Not an error.
                </Typography>
              )}
              {selected.error && !selected.cancelled && <Typography color="error">{selected.error}</Typography>}
              <Typography variant="overline">Request headers</Typography>
              <Json value={selected.requestHeaders} />
              <Typography variant="overline">Request body</Typography>
              <Json value={selected.requestBody} />
              <Typography variant="overline">Response headers</Typography>
              <Json value={selected.responseHeaders} />
              <Typography variant="overline">Response body</Typography>
              <Json value={selected.responseBody} />
            </>
          ) : (
            <Typography color="text.secondary" sx={{ mt: 2 }}>
              Select a request to see its details.
            </Typography>
          )}
        </Box>
      </Box>
    </Drawer>
  );
}
