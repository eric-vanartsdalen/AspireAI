agent: 'agent'
tools: ['run_in_terminal', 'read_file', 'grep_search', 'apply_patch']
description: 'Create and run scripts to evaluate AI model performance in the Aspire setup'
owner: '@eric-vanartsdalen'
audience: 'AI Maintainers'
dependencies: ['Python 3.12', 'Ollama CLI']
last_reviewed: '2025-11-02'

## Metadata
- **Use Cases**: Evaluating Ollama model responses, benchmarking AI performance, testing RAG accuracy, generating evaluation metrics.
- **Dependencies**: Python, Ollama API, access to test data in `data/`, evaluation libraries (e.g., scikit-learn for metrics).
- **Sample Inputs**: Test queries, expected responses, evaluation criteria (accuracy, relevance).
- **Related Instructions**: See `../copilot-instructions.md` for AI endpoint config and `../instructions/python.instructions.md` for scripting patterns.

# AI Evaluation Scripts Guide

Your goal is to help create scripts for evaluating AI models integrated via Ollama in the Aspire platform.

## Evaluation Types

### Response Quality
- Accuracy: Compare against ground truth.
- Relevance: Check if responses match query intent.
- Coherence: Assess logical flow.

### Performance Metrics
- Latency: Measure response times.
- Throughput: Test concurrent requests.
- Token usage: Monitor efficiency.

### RAG Evaluation
- Retrieval accuracy: Verify relevant documents fetched.
- Generation quality: Evaluate answer synthesis.
- Hallucination detection: Flag fabricated information.

## Script Structure
- Load test data from `data/processed/`.
- Query Ollama via configured endpoint.
- Compute metrics (precision, recall, F1).
- Output reports and visualizations.

When creating scripts, ensure they integrate with the Aspire setup, use async for performance, and provide actionable insights.

## Example Commands
- `python src/AspireApp.PythonServices/demo_processing.py --input data/uploads/sample.pdf`
- `python src/AspireApp.PythonServices/scripts/test_concurrent_access.py --threads 8 --operations 25`
- `Invoke-RestMethod -Uri http://localhost:11434/api/generate -Method Post -Body (@{prompt='health check'} | ConvertTo-Json) -ContentType 'application/json'`
- `ollama run llama3 "Summarize Aspire architecture"`