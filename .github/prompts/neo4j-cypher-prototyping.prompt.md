agent: 'agent'
tools: ['read_file', 'grep_search', 'run_in_terminal', 'apply_patch']
description: 'Prototype and optimize Neo4j Cypher queries for graph database operations'
owner: '@eric-vanartsdalen'
audience: 'Graph Maintainers'
dependencies: ['Docker Desktop', 'Neo4j Aura or Desktop']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Writing Cypher queries for graph traversals, data imports, relationship queries, performance optimization.
- **Dependencies**: Neo4j database (containerized via Aspire), Cypher query language knowledge.
- **Sample Inputs**: Graph schema descriptions, query requirements (e.g., find related nodes, aggregate data).
- **Related Instructions**: See `../copilot-instructions.md` for Neo4j setup; reference `../instructions/sql-sp-generation.instructions.md` for query patterns.

# Neo4j Cypher Prototyping Guide

Your goal is to help create efficient Cypher queries for Neo4j graph operations in the Aspire-orchestrated setup.

## Query Patterns

### Basic Traversals
- Use `MATCH` for finding patterns: `MATCH (n:Label)-[r:RELATIONSHIP]->(m:Label)`
- Filter with `WHERE`: `WHERE n.property = 'value'`
- Return results: `RETURN n, r, m`

### Aggregations
- Count relationships: `MATCH (n)-[r]->() RETURN count(r)`
- Group by: `RETURN label, count(*)`

### Performance Tips
- Use indexes on frequently queried properties.
- Avoid cartesian products; use explicit paths.
- Profile queries with `PROFILE` or `EXPLAIN`.

### Data Import
- Use `LOAD CSV` for bulk imports.
- Create nodes: `CREATE (n:Label {properties})`
- Create relationships: `MATCH (a), (b) CREATE (a)-[r:REL]->(b)`

When prototyping, suggest optimized queries, explain execution plans, and ensure compatibility with the graph schema.