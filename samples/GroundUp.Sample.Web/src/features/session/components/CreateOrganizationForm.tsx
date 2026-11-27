import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { useState } from 'react';
import { ErrorPanel } from '../../../components/ErrorPanel';
import { useSessionActions } from '../useSessionActions';

const MAX_NAME_LENGTH = 200;

/** Creates an organization with the user as its TenantAdmin. The current tenant does not change. */
export function CreateOrganizationForm() {
  const { createOrganization, switchTenant } = useSessionActions();
  const [name, setName] = useState('');
  const trimmed = name.trim();
  const tooLong = trimmed.length > MAX_NAME_LENGTH;
  const created = createOrganization.data;

  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h5" gutterBottom>
        Create an organization
      </Typography>
      <Stack spacing={2} sx={{ maxWidth: 520 }}>
        <Stack direction="row" spacing={2} sx={{ alignItems: 'flex-start' }}>
          <TextField
            label="Name"
            size="small"
            value={name}
            onChange={(event) => setName(event.target.value)}
            error={tooLong}
            helperText={tooLong ? `At most ${MAX_NAME_LENGTH} characters.` : 'You become its TenantAdmin.'}
            sx={{ flexGrow: 1 }}
          />
          <Button
            variant="outlined"
            loading={createOrganization.isPending}
            disabled={trimmed.length === 0 || tooLong}
            onClick={() => createOrganization.mutate(trimmed, { onSuccess: () => setName('') })}
          >
            Create
          </Button>
        </Stack>
        {created && (
          <Alert
            severity="success"
            action={
              <Button
                color="inherit"
                size="small"
                loading={switchTenant.isPending}
                onClick={() => switchTenant.mutate(created.id)}
              >
                Switch to it
              </Button>
            }
          >
            Created {created.name} (<code>{created.slug}</code>).
          </Alert>
        )}
        {createOrganization.isError && <ErrorPanel error={createOrganization.error} title="Could not create the organization" />}
        {switchTenant.isError && <ErrorPanel error={switchTenant.error} title="Could not switch" />}
      </Stack>
    </Paper>
  );
}
