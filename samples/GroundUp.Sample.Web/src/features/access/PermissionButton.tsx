import Button, { type ButtonProps } from '@mui/material/Button';
import IconButton, { type IconButtonProps } from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import { missingPermissionText, useCan } from './permissions';

interface PermissionProps {
  /** The permission the action needs. Without it the button is disabled and says which one is missing. */
  permission: string;
}

/** A button that is disabled, with a tooltip naming the permission, when the session lacks it. */
export function PermissionButton({ permission, disabled, ...props }: ButtonProps & PermissionProps) {
  const allowed = useCan(permission);
  if (allowed) {
    return <Button disabled={disabled} {...props} />;
  }
  return (
    <Tooltip title={missingPermissionText(permission)}>
      {/* A disabled button fires no events, so the tooltip needs a wrapper */}
      <span>
        <Button disabled {...props} />
      </span>
    </Tooltip>
  );
}

/** An icon button with the same rule. `label` is the tooltip and accessible name when allowed. */
export function PermissionIconButton({
  permission,
  label,
  disabled,
  ...props
}: IconButtonProps & PermissionProps & { label: string }) {
  const allowed = useCan(permission);
  return (
    <Tooltip title={allowed ? label : missingPermissionText(permission)}>
      <span>
        <IconButton aria-label={label} disabled={disabled || !allowed} {...props} />
      </span>
    </Tooltip>
  );
}
