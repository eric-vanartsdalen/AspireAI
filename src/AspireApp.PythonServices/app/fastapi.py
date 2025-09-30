from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import StreamingResponse

app = FastAPI(title="FastAPI Wrapper",
	description="FastAPI application used for providing an API to external projects.",
    summary="FastAPI app",
    version="0.0.1"
)

@app.get("/")
def read_root():
	return {"result": "Alive"}

@app.get("/health")
def read_root():
	return "Healthy"

@app.exception_handler(Exception)
async def global_exception_handler(request: Request, exc: Exception):
    return {"error": str(exc)}