#!/bin/sh
set -eu

# Populate /conf from /conf-default only if /conf is missing or empty
if [ ! -d /conf ] || [ -z "$(ls -A /conf 2>/dev/null)" ]; then
  echo "[init] Populating /conf from /conf-default"
  mkdir -p /conf
  cp -a /conf-default/* /conf/ || true
  chown -R neo4j:neo4j /conf || true
else
  echo "[init] /conf already exists and is non-empty; will sanitize configuration"
fi

# Ensure /plugins exists and owned by neo4j
mkdir -p /plugins
chown -R neo4j:neo4j /plugins || true

# If neo4j.conf contains APOC settings, move them to apoc.conf (required for Neo4j v5)
if [ -f /conf/neo4j.conf ]; then
  # Extract apoc.* lines
  if grep -E '^\s*apoc\.' /conf/neo4j.conf >/dev/null 2>&1; then
    echo "[init] Found APOC settings in /conf/neo4j.conf - moving to /conf/apoc.conf"
    mkdir -p /conf
    # Ensure apoc.conf exists
    touch /conf/apoc.conf
    # Append apoc.* lines to apoc.conf, avoiding duplicates
    grep -E '^\s*apoc\.' /conf/neo4j.conf | sed 's/^\s*//' | while IFS= read line; do
      # Skip empty lines
      [ -z "${line}" ] && continue
      if ! grep -Fxq "${line}" /conf/apoc.conf 2>/dev/null; then
        echo "${line}" >> /conf/apoc.conf
      fi
    done
    # Remove apoc.* lines from neo4j.conf
    sed -i.bak '/^\s*apoc\./d' /conf/neo4j.conf || true
    rm -f /conf/neo4j.conf.bak || true
    chown neo4j:neo4j /conf/apoc.conf || true
  fi

  # Ensure gds.* is present in dbms.security.procedures.unrestricted
  if grep -E '^\s*dbms\.security\.procedures\.unrestricted\s*=\s*' /conf/neo4j.conf >/dev/null 2>&1; then
    # If line exists but doesn't contain gds.*, append it
    if ! grep -E '^\s*dbms\.security\.procedures\.unrestricted\s*=.*gds\.' /conf/neo4j.conf >/dev/null 2>&1; then
      echo "[init] Adding gds.* to dbms.security.procedures.unrestricted in neo4j.conf"
      sed -E -i.bak "s#^(\s*dbms\.security\.procedures\.unrestricted\s*=\s*)(.*)#\1\2,gds.*#" /conf/neo4j.conf || true
      rm -f /conf/neo4j.conf.bak || true
    fi
  else
    # Add the setting if missing
    echo "[init] Adding dbms.security.procedures.unrestricted=apoc.*,gds.* to neo4j.conf"
    echo "dbms.security.procedures.unrestricted=apoc.*,gds.*" >> /conf/neo4j.conf
  fi

  # Ensure allowlist contains gds.* as well
  if grep -E '^\s*dbms\.security\.procedures\.allowlist\s*=\s*' /conf/neo4j.conf >/dev/null 2>&1; then
    if ! grep -E '^\s*dbms\.security\.procedures\.allowlist\s*=.*gds\.' /conf/neo4j.conf >/dev/null 2>&1; then
      echo "[init] Adding gds.* to dbms.security.procedures.allowlist in neo4j.conf"
      sed -E -i.bak "s#^(\s*dbms\.security\.procedures\.allowlist\s*=\s*)(.*)#\1\2,gds.*#" /conf/neo4j.conf || true
      rm -f /conf/neo4j.conf.bak || true
    fi
  else
    echo "[init] Adding dbms.security.procedures.allowlist=apoc.*,gds.* to neo4j.conf"
    echo "dbms.security.procedures.allowlist=apoc.*,gds.*" >> /conf/neo4j.conf
  fi
fi

# Final ownership fix
chown -R neo4j:neo4j /conf /plugins || true
chown -R neo4j:neo4j /var/lib/neo4j || true
# Ensure default config path points to mounted /conf
ln -sfn /conf /var/lib/neo4j/conf || true

# Set initial password from NEO4J_AUTH when provided and auth store is absent
if [ -n "${NEO4J_AUTH:-}" ] && [ "${NEO4J_AUTH}" != "none" ]; then
  auth_password="${NEO4J_AUTH#*/}"
  if [ "${NEO4J_AUTH}" != "${auth_password}" ]; then
    if [ ! -f /data/dbms/auth ]; then
      echo "[init] Setting initial neo4j password from NEO4J_AUTH"
      neo4j-admin dbms set-initial-password "${auth_password}" || true
    else
      echo "[init] Existing auth store detected; skipping initial password set"
    fi
  else
    echo "[init] NEO4J_AUTH missing separator; skipping initial password set"
  fi
fi

# Handle runtimes that forward a literal "$@" placeholder instead of real args
if [ "$#" -eq 1 ] && { [ "$1" = '$@' ] || [ "$1" = '"$@"' ]; }; then
  set --
fi

# Default to foreground console mode for container usage
if [ "$#" -eq 0 ]; then
  set -- neo4j console
elif [ "$1" = "neo4j" ] && [ "$#" -eq 1 ]; then
  set -- neo4j console
fi

# Start Neo4j or execute the provided command
if [ "$#" -gt 0 ]; then
  echo "[init] Launching command: $*"
  exec "$@"
else
  echo "[init] Launching neo4j"
  exec neo4j console
fi
