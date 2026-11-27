import { createFileRoute } from '@tanstack/react-router';
import { Placeholder } from '../components/Placeholder';

export const Route = createFileRoute('/wizard')({
  component: WizardPage,
});

function WizardPage() {
  return (
    <Placeholder
      title="Setup"
      story="GU-88"
      description="First-run setup: the bootstrap token and the setup wizard steps."
    />
  );
}
