import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import { useRef, useState } from 'react';

interface ShowLinkDialogProps {
  /** The join address; null closes the dialog. */
  url: string | null;
  onClose: () => void;
}

/** Shows a new join link once, with a copy button: only its hash is stored, so it cannot be shown again. */
export function ShowLinkDialog({ url, onClose }: ShowLinkDialogProps) {
  const field = useRef<HTMLInputElement>(null);
  const [copied, setCopied] = useState<'yes' | 'select' | null>(null);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url ?? '');
      setCopied('yes');
    } catch {
      // No clipboard access (not a secure context, or refused): select the text for a manual copy
      field.current?.select();
      setCopied('select');
    }
  };

  const close = () => {
    setCopied(null);
    onClose();
  };

  return (
    <Dialog open={url !== null} onClose={close} fullWidth maxWidth="md">
      <DialogTitle>Join link</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Alert severity="warning">
            This link is shown only now. Anyone with it can join with its role. Regenerate to get a new one.
          </Alert>
          <TextField
            inputRef={field}
            value={url ?? ''}
            size="small"
            fullWidth
            slotProps={{ htmlInput: { readOnly: true, onFocus: (event: React.FocusEvent<HTMLInputElement>) => event.target.select() } }}
          />
          {copied === 'yes' && <Alert severity="success">Copied.</Alert>}
          {copied === 'select' && <Alert severity="info">The link is selected: press Ctrl+C (or Cmd+C) to copy it.</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button startIcon={<ContentCopyIcon />} onClick={copy}>
          Copy
        </Button>
        <Button variant="contained" onClick={close}>
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
}
