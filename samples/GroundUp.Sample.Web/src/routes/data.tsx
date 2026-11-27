import Typography from '@mui/material/Typography';
import { createFileRoute } from '@tanstack/react-router';
import { z } from 'zod';
import { Placeholder } from '../components/Placeholder';

// Typed, validated search parameters: /data?page=2 gives page as a number; a bad value falls back.
// GU-92 adds filter and sort parameters here.
const dataSearchSchema = z.object({
  page: z.coerce.number().int().min(1).catch(1),
});

export const Route = createFileRoute('/data')({
  validateSearch: dataSearchSchema,
  component: DataPage,
});

function DataPage() {
  const { page } = Route.useSearch();
  return (
    <>
      <Placeholder
        title="Data"
        story="GU-92"
        description="The sample entities: lists with filtering, sorting and paging, create, edit and delete."
      />
      <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
        Search parameter check: page = {page}
      </Typography>
    </>
  );
}
