#!/bin/bash
# Runs once, when the PostgreSQL container's data volume is first created.
#
# Each service gets its OWN database and its OWN login role. Neither role can connect to the
# other service's database, which makes "no shared database" a rule the database enforces,
# not just a convention. Both databases share one container only to keep local setup light.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-EOSQL
    CREATE ROLE estimates_app LOGIN PASSWORD '${ESTIMATES_DB_PASSWORD}';
    CREATE DATABASE estimates OWNER estimates_app;
    REVOKE CONNECT ON DATABASE estimates FROM PUBLIC;

    CREATE ROLE accounting_app LOGIN PASSWORD '${ACCOUNTING_DB_PASSWORD}';
    CREATE DATABASE accounting OWNER accounting_app;
    REVOKE CONNECT ON DATABASE accounting FROM PUBLIC;
EOSQL

echo "Created databases 'estimates' and 'accounting' with separate owners."
