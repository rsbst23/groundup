import List from '@mui/material/List';
import ListItemButton, { type ListItemButtonOwnProps } from '@mui/material/ListItemButton';
import ListItemText from '@mui/material/ListItemText';
import { createLink } from '@tanstack/react-router';
import { forwardRef, type AnchorHTMLAttributes } from 'react';

// An MUI list item that navigates with the router, so the targets are type-checked.
const ListItemAnchor = forwardRef<HTMLAnchorElement, ListItemButtonOwnProps & AnchorHTMLAttributes<HTMLAnchorElement>>(
  (props, ref) => <ListItemButton component="a" ref={ref} {...props} />,
);
const ListItemLink = createLink(ListItemAnchor);

const areas = [
  { to: '/', label: 'Home' },
  { to: '/wizard', label: 'Setup' },
  { to: '/session', label: 'Session' },
  { to: '/members', label: 'Members' },
  { to: '/invitations', label: 'Invitations' },
  { to: '/join-links', label: 'Join links' },
  { to: '/roles', label: 'Roles' },
  { to: '/settings', label: 'Settings' },
  { to: '/data', label: 'Data' },
  { to: '/diagnostics', label: 'Diagnostics' },
  // The sample API's Development mailbox (GU-90); only while running the Vite dev server
  ...(import.meta.env.DEV ? ([{ to: '/mailbox', label: 'Mailbox' }] as const) : []),
] as const;

export function NavMenu() {
  return (
    <List component="nav" dense>
      {areas.map((area) => (
        <ListItemLink
          key={area.to}
          to={area.to}
          preload="intent"
          activeOptions={{ exact: area.to === '/' }}
          activeProps={{ selected: true }}
        >
          <ListItemText primary={area.label} />
        </ListItemLink>
      ))}
    </List>
  );
}
