# Use official Neo4j image
FROM neo4j:latest

# Expose Neo4j ports
EXPOSE 7474 7687

# Set default environment variables (can be overridden by Aspire)
ENV NEO4J_AUTH=neo4j/neo4j@secret

# Create volume mount points
VOLUME ["/logs", "/config", "/data", "/plugins"]

# Set restart policy is handled by Aspire, not needed in Dockerfile

