import { createFileRoute } from '@tanstack/react-router';
import { JoinLinksPage } from '../features/joinLinks/JoinLinksPage';
import { listSearchSchema } from '../features/list/listSearch';

// The list's view (page, sort, search, status) is in the URL (GU-98)
export const Route = createFileRoute('/join-links')({
  validateSearch: listSearchSchema,
  component: JoinLinksPage,
});