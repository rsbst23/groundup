import Link from '@mui/material/Link';
import { createLink } from '@tanstack/react-router';

/** An MUI link that navigates with the router, so its targets are type-checked. */
export const RouterLink = createLink(Link);
