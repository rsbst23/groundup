import { createFileRoute } from '@tanstack/react-router';
import { JoinLinksPage } from '../features/joinLinks/JoinLinksPage';

export const Route = createFileRoute('/join-links')({
  component: JoinLinksPage,
});
