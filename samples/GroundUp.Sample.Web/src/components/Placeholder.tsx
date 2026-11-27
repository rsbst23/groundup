import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Typography from '@mui/material/Typography';

interface PlaceholderProps {
  title: string;
  /** The Jira story that builds this page, for example "GU-88". */
  story: string;
  description: string;
}

/** Stands in for a page that a later story builds. */
export function Placeholder({ title, story, description }: PlaceholderProps) {
  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="h5" gutterBottom>
        {title}
      </Typography>
      <Typography gutterBottom>{description}</Typography>
      <Typography color="text.secondary">
        Not built yet. Built in{' '}
        <Link href={`https://prosbeck.atlassian.net/browse/${story}`} target="_blank" rel="noreferrer">
          {story}
        </Link>
        .
      </Typography>
    </Paper>
  );
}
