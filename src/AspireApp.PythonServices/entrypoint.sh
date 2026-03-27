#!/bin/sh
set -e

# Fix permissions on bind-mounted directories at container startup.
# This script runs as root before dropping to appuser via gosu, ensuring
# directories bind-mounted from the host (which may be root-owned) are
# writable by appuser regardless of the Docker host OS or backend.
# -R recursive ensures existing files (e.g. data-resources.db pre-created by
# AppHost.cs on the host) are also re-owned, not just the directory itself.
for dir in \
    /app/database \
    /app/docs-database \
    /app/data \
    /app/data/uploads \
    /app/data/processed \
    /app/data/processed/documents \
    /app/data/inputs; do
    mkdir -p "$dir"
    chown -R appuser:appuser "$dir" 2>/dev/null || true
    chmod -R 775 "$dir" 2>/dev/null || true
done

# Remove stale SQLite WAL/SHM files left over from ungraceful container stops.
# A 0-byte WAL with a non-empty SHM is an inconsistent state that causes
# "disk I/O error" when SQLite tries to open the database.
# These files are always safe to remove at startup before any DB connections open.
find /app/docs-database /app/database \
    \( -name "*.db-wal" -o -name "*.db-shm" \) \
    -delete 2>/dev/null || true

exec gosu appuser "$@"