import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/')({
  component: Home,
});

const repo = 'https://github.com/rsbst23/groundup/blob/main';

function Home() {
  return (
    <Stack spacing={2}>
      <Paper sx={{ p: 3 }}>
        <Typography variant="h5" gutterBottom>
          GroundUp Sample
        </Typography>
        <Typography gutterBottom>
          A browser app for calling and testing the GroundUp sample API. It is served from{' '}
          <strong>{window.location.host}</strong>; API calls go through this host to the sample API.
        </Typography>
      </Paper>
      <Paper sx={{ p: 3 }}>
        <Typography variant="h6" gutterBottom>
          Links
        </Typography>
        <Stack spacing={1}>
          <Link href="/swagger" target="_blank" rel="noreferrer">
            Swagger (the sample API, through this host)
          </Link>
          <Link href="http://localhost:8080/admin/master/console/#/groundup" target="_blank" rel="noreferrer">
            Keycloak admin console (groundup realm)
          </Link>
          <Link href={`${repo}/samples/GroundUp.Sample.Web/TESTING.md`} target="_blank" rel="noreferrer">
            Testing manual for this app
          </Link>
          <Link href={`${repo}/.kiro/specs/phase-10c-auth-dispatcher/MANUAL_TESTING.md`} target="_blank" rel="noreferrer">
            API testing guide (Swagger)
          </Link>
        </Stack>
      </Paper>
    </Stack>
  );
}
