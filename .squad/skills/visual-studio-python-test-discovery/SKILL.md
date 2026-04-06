---
name: "visual-studio-python-test-discovery"
description: "Keep AspireAI Python pytest discovery aligned between CLI and Visual Studio .pyproj workflows"
domain: "testing"
confidence: "high"
source: "earned"
---

## Context

AspireAI's Python service still uses a Visual Studio `.pyproj` with `TestFramework` set to `Pytest`. CLI pytest will discover tests from the filesystem, but Visual Studio Test Explorer follows the project file closely enough that missing `Compile Include` entries can hide regression tests.

## Patterns

### Add regression tests to the `.pyproj`

If a Python test under `src\AspireApp.PythonServices\tests\` is supposed to run in Visual Studio, add it to `AspireApp.PythonServices.pyproj` under `<Compile Include=...>`. Also add the `tests\` folder if it is not already listed.

### Keep helper modules alongside the tests

If a regression test imports a local helper such as `tests\fake_postgres.py`, include that helper in the `.pyproj` too. Hidden helpers make VS discovery and navigation brittle.

### Keep utility scripts out of pytest discovery

Files that are operational scripts rather than automated tests should not expose `test_*` functions. Rename the callable (for example, `run_docker_builds`) and keep script execution behind `if __name__ == "__main__":`.

### Bootstrap the VS interpreter with smoke-test dependencies

The `.pyproj` points Visual Studio at `.venv`. Any local bootstrap path that creates `.venv` must install the packages required by the smoke gate, including `psycopg[binary]`, `psycopg-pool`, and `pytest`.

## Examples

- `src\AspireApp.PythonServices\AspireApp.PythonServices.pyproj`
- `src\AspireApp.PythonServices\tests\test_p0_contract_audit.py`
- `src\AspireApp.PythonServices\tests\test_processing_pipeline_regression.py`
- `src\AspireApp.PythonServices\test_build_config.py`

## Anti-Patterns

- Relying on CLI pytest discovery alone when Visual Studio is the expected workflow
- Leaving real regression tests out of the `.pyproj`
- Leaving utility scripts with `test_*` entry points that trigger Docker or environment setup during automated runs
- Creating `.venv` with only partial dependencies, then treating smoke-test import failures as product regressions
