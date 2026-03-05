# Optimized Neo4j Docker image with caching and performance improvements
FROM neo4j:2025.11.2-community AS base

# Set build-time arguments for configuration
ARG NEO4J_PLUGINS=""
ARG NEO4J_LABS_PLUGINS=""

# Set environment variables for optimal performance
ENV NEO4J_ACCEPT_LICENSE_AGREEMENT=yes \
    NEO4J_CONF=/conf \
    NEO4J_PLUGINS='["apoc", "graph-data-science"]' \
    NEO4J_dbms_security_procedures_unrestricted=apoc.*,gds.* \
    NEO4J_dbms_security_procedures_allowlist=apoc.*,gds.* \
    NEO4J_server_default__listen__address=0.0.0.0 \
    NEO4J_server_bolt_enabled=true \
    NEO4J_server_bolt_listen__address=:7687 \
    NEO4J_server_http_enabled=true \
    NEO4J_server_http_listen__address=:7474 \
    NEO4J_server_https_enabled=false \
    NEO4J_server_memory_heap_initial__size=512M \
    NEO4J_server_memory_heap_max__size=2G \
    NEO4J_server_memory_pagecache_size=1G \
    NEO4J_server_logs_gc_enabled=false \
    NEO4J_server_logs_gc_options="-Xlog:gc:file=/var/lib/neo4j/logs/gc.log"

# Install commonly used plugins in a cached layer
# This layer will be cached unless the plugin versions change
RUN set -eux; \
    # Ensure plugins directory exists and has correct ownership
    mkdir -p /plugins && chown -R neo4j:neo4j /plugins; \
    # Install APOC (Awesome Procedures on Cypher)
    wget -O /plugins/apoc-2025.11.2-core.jar \
        https://github.com/neo4j/apoc/releases/download/2025.11.2/apoc-2025.11.2-core.jar; \
    # Install Graph Data Science Library
    wget -O /plugins/neo4j-graph-data-science-2.24.0.jar \
        https://github.com/neo4j/graph-data-science/releases/download/2.24.0/neo4j-graph-data-science-2.24.0.jar; \
    # Verify plugin integrity (optional but recommended)
    ls -la /plugins/

# Copy custom configuration files (if any) into a defaults directory so they can be copied
# into a runtime volume if /conf is mounted
COPY neo4j.conf /conf-default/neo4j.conf
COPY apoc.conf /conf-default/apoc.conf

# Copy init script into a standard executable path and also into root for compatibility
COPY docker-entrypoint-init.sh /usr/local/bin/docker-entrypoint-init.sh
COPY docker-entrypoint-init.sh /docker-entrypoint-init.sh
# Normalize line endings and make executable to avoid CRLF/shebang issues on Linux
RUN sed -i 's/\r$//' /usr/local/bin/docker-entrypoint-init.sh /docker-entrypoint-init.sh || true && \
    chmod +x /usr/local/bin/docker-entrypoint-init.sh /docker-entrypoint-init.sh && \
    ln -sf /usr/local/bin/docker-entrypoint-init.sh /docker-entrypoint-init.sh

# Ensure a stable wrapper exists at /docker-entrypoint-init.sh in case orchestration refers to it
RUN printf '%s\n' '#!/bin/sh' 'exec /usr/local/bin/docker-entrypoint-init.sh "\$@"' > /docker-entrypoint-init.sh-wrapper && \
    mv /docker-entrypoint-init.sh-wrapper /docker-entrypoint-init.sh && \
    chmod +x /docker-entrypoint-init.sh && chown neo4j:neo4j /docker-entrypoint-init.sh || true

# Create optimized volume mount points with proper permissions
RUN mkdir -p /logs /import /metrics /backup /conf-default && \
    chown -R neo4j:neo4j /conf-default /plugins /logs /import /metrics /backup

# Expose Neo4j ports
EXPOSE 7474 7687

# Volume definitions for data persistence
VOLUME ["/data", "/logs", "/conf", "/import", "/plugins", "/metrics", "/backup"]

# Health check for better container orchestration
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD cypher-shell -u neo4j -p ${NEO4J_AUTH#*/} "RETURN 1" || exit 1

# Use the wrapper init script to ensure /conf contains the required files, then exec the default entrypoint
ENTRYPOINT ["/docker-entrypoint-init.sh"]
CMD ["neo4j"]

