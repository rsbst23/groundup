import { createFileRoute } from '@tanstack/react-router';
import { MailboxPage } from '../features/mailbox/MailboxPage';

/** The sample API's Development mailbox (in the navigation only while running the Vite dev server). */
export const Route = createFileRoute('/mailbox')({
  component: MailboxPage,
});
