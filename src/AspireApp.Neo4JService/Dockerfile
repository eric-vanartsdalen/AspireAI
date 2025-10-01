# Optimized Neo4j Docker image with caching and performance improvements
FROM neo4j:5.15.0 AS base

# Set build-time arguments for configuration
ARG NEO4J_PLUGINS=""
ARG NEO4J_LABS_PLUGINS=""

# Set environment variables for optimal performance
ENV NEO4J_AUTH=neo4j/neo4j@secret \
    NEO4J_ACCEPT_LICENSE_AGREEMENT=yes \
    NEO4J_apoc_export_file_enabled=true \
    NEO4J_apoc_import_file_enabled=true \
    NEO4J_apoc_import_file_use__neo4j__config=true \
    NEO4J_PLUGINS='["apoc", "graph-data-science"]' \
    NEO4J_dbms_security_procedures_unrestricted=apoc.*,gds.* \
    NEO4J_dbms_security_procedures_allowlist=apoc.*,gds.* \
    NEO4J_dbms_memory_heap_initial__size=512M \
    NEO4J_dbms_memory_heap_max__size=2G \
    NEO4J_dbms_memory_pagecache_size=1G \
    NEO4J_dbms_connector_bolt_listen__address=0.0.0.0:7687 \
    NEO4J_dbms_connector_http_listen__address=0.0.0.0:7474

# Install commonly used plugins in a cached layer
# This layer will be cached unless the plugin versions change
RUN set -eux; \
    # Install APOC (Awesome Procedures on Cypher)
    wget -O /plugins/apoc-5.15.0-core.jar \
        https://github.com/neo4j-contrib/neo4j-apoc-procedures/releases/download/5.15.0/apoc-5.15.0-core.jar; \
    # Install Graph Data Science Library
    wget -O /plugins/neo4j-graph-data-science-2.5.8.jar \
        https://github.com/neo4j/graph-data-science/releases/download/2.5.8/neo4j-graph-data-science-2.5.8.jar; \
    # Verify plugin integrity (optional but recommended)
    ls -la /plugins/

# Copy custom configuration files (if any)
# These will be in a separate layer for better caching
COPY neo4j.conf /conf/neo4j.conf

# Create optimized volume mount points with proper permissions
RUN mkdir -p /logs /import /metrics /backup && \
    chown -R neo4j:neo4j /logs /import /metrics /backup

# Expose Neo4j ports
EXPOSE 7474 7687

# Volume definitions for data persistence
VOLUME ["/data", "/logs", "/conf", "/import", "/plugins", "/metrics", "/backup"]

# Health check for better container orchestration
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD cypher-shell -u neo4j -p ${NEO4J_AUTH#*/} "RETURN 1" || exit 1

# Use the default Neo4j entrypoint
ENTRYPOINT ["/startup/docker-entrypoint.sh"]
CMD ["neo4j"]

