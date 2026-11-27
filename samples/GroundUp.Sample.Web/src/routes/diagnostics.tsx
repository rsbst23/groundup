import { createFileRoute } from '@tanstack/react-router';
import { Placeholder } from '../components/Placeholder';

export const Route = createFileRoute('/diagnostics')({
  component: DiagnosticsPage,
});

function DiagnosticsPage() {
  return (
    <Placeholder
      title="Diagnostics"
      story="GU-93"
      description="Health, correlation IDs and the event log."
    />
  );
}
