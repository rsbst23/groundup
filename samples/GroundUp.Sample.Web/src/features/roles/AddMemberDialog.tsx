import Autocomplete from '@mui/material/Autocomplete';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { listMembers } from '../../api/members';
import type { TenantMemberDto, TenantRoleDto } from '../../api/types';
import { ErrorPanel } from '../../components/ErrorPanel';
import { SEARCH_DEBOUNCE_MS, useDebouncedCallback } from '../list/debounce';
import { membersKey, memberName } from '../members/membersQuery';
import { holdsRole } from './rolesQuery';

interface AddMemberDialogProps {
  tenantId: string;
  role: TenantRoleDto;
  open: boolean;
  busy: boolean;
  error: unknown;
  onSubmit: (member: TenantMemberDto) => void;
  onClose: () => void;
}

/**
 * Give the role to a member: a search over the organization's members as you type (first 25 matches
 * by email), people who already hold the role shown but not selectable.
 */
export function AddMemberDialog({ tenantId, role, open, busy, error, onSubmit, onClose }: AddMemberDialogProps) {
  const [input, setInput] = useState('');
  const [term, setTerm] = useState('');
  const [chosen, setChosen] = useState<TenantMemberDto | null>(null);
  const search = useDebouncedCallback((value: string) => setTerm(value.trim()), SEARCH_DEBOUNCE_MS);

  const results = useQuery({
    queryKey: [...membersKey, tenantId, 'search', term] as const,
    queryFn: ({ signal }) => listMembers({ page: 1, q: term }, signal),
    enabled: open && term.length > 0,
    placeholderData: keepPreviousData,
  });

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>Add a member to {role.name}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Autocomplete
            options={term.length > 0 ? (results.data?.items ?? []) : []}
            value={chosen}
            onChange={(_, value) => setChosen(value)}
            inputValue={input}
            onInputChange={(_, value) => {
              setInput(value);
              search.call(value);
            }}
            // The API searched already; show its results as they are
            filterOptions={(options) => options}
            getOptionLabel={(member) => `${memberName(member)} (${member.email})`}
            getOptionDisabled={(member) => holdsRole(member, role.id)}
            isOptionEqualToValue={(a, b) => a.userId === b.userId}
            loading={results.isFetching}
            noOptionsText={term.length === 0 ? 'Type a name or email' : 'No member matches'}
            renderOption={({ key, ...props }, member) => (
              <Box component="li" key={key} {...props}>
                <Box>
                  <Typography variant="body2">{memberName(member)}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {member.email}
                    {holdsRole(member, role.id) && ' · has the role'}
                  </Typography>
                </Box>
              </Box>
            )}
            renderInput={(params) => (
              <TextField {...params} label="Member" size="small" autoFocus helperText="Search by name or email." />
            )}
          />
          {results.isError && <ErrorPanel error={results.error} title="Could not search the members" />}
          {Boolean(error) && <ErrorPanel error={error} title={`Could not give ${role.name}`} />}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>
          Cancel
        </Button>
        <Button variant="contained" loading={busy} disabled={chosen === null} onClick={() => chosen && onSubmit(chosen)}>
          Add
        </Button>
      </DialogActions>
    </Dialog>
  );
}
