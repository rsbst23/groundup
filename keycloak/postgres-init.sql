-- Creates the Keycloak database on first Postgres container start.
-- The main 'groundup' database is created via POSTGRES_DB env var.
SELECT 'CREATE DATABASE keycloak'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'keycloak')\gexec
