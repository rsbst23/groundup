import RefreshIcon from '@mui/icons-material/Refresh';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import Typography from '@mui/material/Typography';
import { useQuery } from '@tanstack/react-query';
import { listDevEmails } from '../../api/devMailbox';
import { ApiError } from '../../api/errors';
import { ErrorPanel } from '../../components/ErrorPanel';
import { formatDateTime } from '../common/dates';
import { SignedOutPanel } from '../session/components/SignedOutPanel';
import { useSession } from '../session/sessionQuery';

/**
 * The invitation emails the sample API kept instead of sending them (Development only, GU-90).
 * The links open this app's accept page. They contain tokens, which work like passwords.
 */
export function MailboxPage() {
  const { me, isLoading } = useSession();
  const emails = useQuery({
    queryKey: ['dev-mailbox'] as const,
    queryFn: ({ signal }) => listDevEmails(signal),
    enabled: me !== null,
  });

  if (isLoading) {
    return null;
  }
  if (!me) {
    return <SignedOutPanel />;
  }

  const notDevelopment = emails.error instanceof ApiError && emails.error.status === 404;

  return (
    <Paper sx={{ p: 3 }}>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
        <Typography variant="h5">Mailbox</Typography>
        <Button startIcon={<RefreshIcon />} loading={emails.isFetching} onClick={() => void emails.refetch()}>
          Refresh
        </Button>
      </Stack>
      <Typography color="text.secondary" gutterBottom>
        In Development the sample API keeps the last 50 invitation emails here instead of sending them. Opening a link
        is what the invitee would do: sign out first, or use a private window, to accept as them.
      </Typography>

      {notDevelopment ? (
        <Alert severity="info">The dev mailbox is not available: the API is not running in Development.</Alert>
      ) : emails.isError ? (
        <ErrorPanel error={emails.error} title="Could not load the mailbox" />
      ) : (
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Sent</TableCell>
              <TableCell>To</TableCell>
              <TableCell>Organization</TableCell>
              <TableCell>Role</TableCell>
              <TableCell>Expires</TableCell>
              <TableCell>Link</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {emails.isPending && (
              <TableRow>
                <TableCell colSpan={6}>Loading…</TableCell>
              </TableRow>
            )}
            {emails.data?.length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>No emails yet. Send an invitation from the Invitations page.</TableCell>
              </TableRow>
            )}
            {emails.data?.map((email) => (
              <TableRow key={`${email.invitationId}-${email.sentAt}`}>
                <TableCell>{formatDateTime(email.sentAt)}</TableCell>
                <TableCell>{email.to}</TableCell>
                <TableCell>{email.organizationName}</TableCell>
                <TableCell>{email.roleName}</TableCell>
                <TableCell>{formatDateTime(email.expiresAt)}</TableCell>
                <TableCell>
                  <Link href={email.acceptUrl}>Open the invitation</Link>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
    </Paper>
  );
}
