import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { ApiError } from '../api/errors';
import { useStartSignIn } from '../features/session/useStartSignIn';

interface ErrorPanelProps {
  error: unknown;
  /** Shown above the message, for example "Could not load members". */
  title?: string;
}

/** Shows any error from an API call: status, message, error code, details and the correlation ID. */
export function ErrorPanel({ error, title }: ErrorPanelProps) {
  const startSignIn = useStartSignIn();
  const apiError = error instanceof ApiError ? error : null;
  const message = error instanceof Error ? error.message : String(error);
  const status = apiError ? (apiError.status === 0 ? 'No response' : `HTTP ${apiError.status}`) : null;

  return (
    <Alert
      severity="error"
      action={
        apiError?.errorCode === 'REAUTH_REQUIRED' ? (
          // Ask for the password (the session is past its absolute lifetime) and come back here
          <Button
            color="inherit"
            size="small"
            loading={startSignIn.isPending}
            onClick={() => startSignIn.mutate({ kind: 'login', forceLogin: true })}
          >
            Sign in again
          </Button>
        ) : undefined
      }
    >
      <AlertTitle>{title ?? status ?? 'Error'}</AlertTitle>
      <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        {title && status && <Typography variant="body2">{status}</Typography>}
        {apiError?.errorCode && <Chip size="small" label={apiError.errorCode} />}
        <Typography variant="body2">{message}</Typography>
      </Box>
      {apiError && apiError.errors.length > 0 && (
        <Box component="ul" sx={{ m: 0, mt: 1, pl: 3 }}>
          {apiError.errors.map((detail) => (
            <li key={detail}>
              <Typography variant="body2">{detail}</Typography>
            </li>
          ))}
        </Box>
      )}
      {apiError?.correlationId && (
        <Box sx={{ display: 'flex', alignItems: 'center', mt: 1 }}>
          <Typography variant="caption" sx={{ fontFamily: 'monospace' }}>
            Correlation ID: {apiError.correlationId}
          </Typography>
          <Tooltip title="Copy">
            <IconButton
              size="small"
              aria-label="Copy correlation ID"
              onClick={() => void navigator.clipboard.writeText(apiError.correlationId ?? '')}
            >
              <ContentCopyIcon fontSize="inherit" />
            </IconButton>
          </Tooltip>
        </Box>
      )}
    </Alert>
  );
}
