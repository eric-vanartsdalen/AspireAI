# Neo4j Data Directories

This directory structure is used by the Neo4j container for data persistence via bind mounts.

## Directory Structure

```
database/neo4j/
??? data/     # Neo4j database files (persistent storage)
??? logs/     # Neo4j log files  
??? config/   # Neo4j configuration files
??? plugins/  # Neo4j plugins
```

## Notes

- These directories are bind-mounted to the Neo4j container
- Data persists across container restarts and rebuilds
- Files are directly accessible from the host system
- Already excluded from git via .gitignore

## Default Credentials

- Username: `neo4jadmin` (configurable via `neo4j-user` parameter)
- Password: `neo4j@secret` (configurable via `neo4j-pass` parameter)

## Access URLs

- Neo4j Browser: http://localhost:7474
- Bolt Protocol: bolt://localhost:7687