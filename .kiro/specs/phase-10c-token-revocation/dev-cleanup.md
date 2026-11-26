# Development cleanup: role rows left by the old leave behaviour

Before GU-71, leaving a tenant only deactivated the membership (`AuthUserTenants.IsActive = false`). The user's role assignments in that tenant (`AuthUserRoles`) stayed. Since GU-71, leaving removes them in the same save, so only databases used before GU-71 can contain these rows. GroundUp has no released deployments, so this is a development-database concern only (decided in GU-73; no migration).

The rows are harmless while the membership stays inactive. On rejoin, though, they would silently restore the old roles, including TenantAdmin.

**Back up the database first.** The delete cannot be undone.

Check what would be removed:

```sql
SELECT ur."UserId", ur."TenantId", ur."RoleId"
FROM "AuthUserRoles" ur
JOIN "AuthUserTenants" ut
  ON ut."UserId" = ur."UserId" AND ut."TenantId" = ur."TenantId"
WHERE ut."IsActive" = false;
```

Delete them:

```sql
DELETE FROM "AuthUserRoles" ur
USING "AuthUserTenants" ut
WHERE ut."UserId" = ur."UserId"
  AND ut."TenantId" = ur."TenantId"
  AND ut."IsActive" = false;
```

Role assignments for users with no membership row at all are not covered. They don't come from the leave path.
