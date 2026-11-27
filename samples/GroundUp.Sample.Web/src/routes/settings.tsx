import { createFileRoute } from '@tanstack/react-router';
import { Placeholder } from '../components/Placeholder';

export const Route = createFileRoute('/settings')({
  component: SettingsPage,
});

function SettingsPage() {
  return (
    <Placeholder
      title="Settings"
      story="GU-91"
      description="Browse settings and set values by level."
    />
  );
}
