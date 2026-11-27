import AppBar from '@mui/material/AppBar';
import Badge from '@mui/material/Badge';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Drawer from '@mui/material/Drawer';
import Toolbar from '@mui/material/Toolbar';
import Typography from '@mui/material/Typography';
import { useState, type ReactNode } from 'react';
import { NavMenu } from './NavMenu';
import { RequestInspector } from './RequestInspector';
import { SessionButton } from './SessionButton';
import { useInspectorEntries } from './useInspectorEntries';
import { SessionBanner } from './SessionBanner';

const drawerWidth = 200;

interface AppLayoutProps {
  children: ReactNode;
}

/** App bar, navigation on the left, the session banner and the page on the right, the request inspector below. */
export function AppLayout({ children }: AppLayoutProps) {
  const [inspectorOpen, setInspectorOpen] = useState(false);
  const requestCount = useInspectorEntries().length;

  return (
    <Box sx={{ display: 'flex' }}>
      <AppBar position="fixed" sx={{ zIndex: (theme) => theme.zIndex.drawer + 1 }}>
        <Toolbar>
          <Typography variant="h6" component="div" sx={{ flexGrow: 1 }}>
            GroundUp Sample
          </Typography>
          <Typography variant="body2" title="The host this app is served from" sx={{ mr: 2 }}>
            {window.location.host}
          </Typography>
          <Box sx={{ mr: 2 }}>
            <SessionButton />
          </Box>
          <Badge badgeContent={requestCount} color="secondary" max={99}>
            <Button color="inherit" variant="outlined" size="small" onClick={() => setInspectorOpen(true)}>
              Requests
            </Button>
          </Badge>
        </Toolbar>
      </AppBar>
      <Drawer
        variant="permanent"
        sx={{
          width: drawerWidth,
          flexShrink: 0,
          [`& .MuiDrawer-paper`]: { width: drawerWidth, boxSizing: 'border-box' },
        }}
      >
        <Toolbar />
        <NavMenu />
      </Drawer>
      <Box component="main" sx={{ flexGrow: 1, p: 3, minWidth: 0 }}>
        <Toolbar />
        <SessionBanner />
        {children}
      </Box>
      <RequestInspector open={inspectorOpen} onClose={() => setInspectorOpen(false)} />
    </Box>
  );
}
