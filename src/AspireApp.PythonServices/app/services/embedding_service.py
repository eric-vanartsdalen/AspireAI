"""
Embedding service for generating vector embeddings from text.

P2-C foundation: prefer Ollama when Aspire wires an endpoint, with a local
sentence-transformers fallback for direct Python runs.
"""

import json
import logging
import os
from typing import List
from urllib import error, request

logger = logging.getLogger(__name__)


class EmbeddingService:
    """
    Generate text embeddings for semantic search.

    P2-C implementation: prefer Ollama when configured through AppHost, while
    preserving a local sentence-transformers fallback for standalone use.
    """

    def __init__(self, model_name: str | None = None):
        """
        Initialize embedding service with specified model.

        Args:
            model_name: Model identifier.
        """
        self.model_name = model_name or os.getenv(
            "EMBEDDING_MODEL",
            "sentence-transformers/all-MiniLM-L6-v2"
        )
        self.endpoint = (os.getenv("OLLAMA_ENDPOINT") or "").rstrip("/")
        self._model = None
        self._embedding_dimension = int(
            os.getenv("EMBEDDING_DIM")
            or os.getenv("EMBEDDING_DIMENSION")
            or "384"
        )
        self._timeout_seconds = int(os.getenv("EMBEDDING_TIMEOUT", "60"))

    def _load_model(self):
        """
        Lazy-load embedding model.

        Used only when Ollama is not configured for the current process.
        """
        if self._model is None:
            try:
                from sentence_transformers import SentenceTransformer
                self._model = SentenceTransformer(self.model_name)
                logger.info(f"Loaded embedding model: {self.model_name}")
            except ImportError:
                logger.warning("sentence-transformers not installed for local fallback embeddings.")
                return None
            except Exception as e:
                logger.error(f"Failed to load embedding model {self.model_name}: {e}")
                return None
        return self._model

    def _post_ollama(self, path: str, payload: dict) -> dict:
        if not self.endpoint:
            raise ValueError("OLLAMA_ENDPOINT is not configured.")

        body = json.dumps(payload).encode("utf-8")
        url = f"{self.endpoint}{path}"
        req = request.Request(
            url,
            data=body,
            headers={"Content-Type": "application/json"},
            method="POST",
        )

        with request.urlopen(req, timeout=self._timeout_seconds) as response:
            return json.loads(response.read().decode("utf-8"))

    def _embed_text_with_ollama(self, text: str) -> List[float] | None:
        try:
            response = self._post_ollama(
                "/api/embed",
                {"model": self.model_name, "input": text},
            )
            if "embedding" in response:
                return response["embedding"]
            if "embeddings" in response and response["embeddings"]:
                return response["embeddings"][0]
        except (error.URLError, error.HTTPError, ValueError, json.JSONDecodeError) as e:
            logger.warning(f"Ollama /api/embed request failed, trying legacy endpoint: {e}")

        try:
            response = self._post_ollama(
                "/api/embeddings",
                {"model": self.model_name, "prompt": text},
            )
            if "embedding" in response:
                return response["embedding"]
        except (error.URLError, error.HTTPError, ValueError, json.JSONDecodeError) as e:
            logger.error(f"Ollama embedding request failed: {e}")

        return None

    def _embed_batch_with_ollama(
        self,
        texts: List[str],
    ) -> List[List[float]] | None:
        try:
            response = self._post_ollama(
                "/api/embed",
                {"model": self.model_name, "input": texts},
            )
            embeddings = response.get("embeddings")
            if isinstance(embeddings, list):
                return embeddings
        except (error.URLError, error.HTTPError, ValueError, json.JSONDecodeError) as e:
            logger.warning(f"Ollama batch embedding request failed, falling back to single requests: {e}")

        embeddings: List[List[float]] = []
        for text in texts:
            embedding = self._embed_text_with_ollama(text)
            if embedding is None:
                return None
            embeddings.append(embedding)
        return embeddings

    def is_available(self) -> bool:
        """Check if embedding generation is available."""
        if self.endpoint:
            return True
        return self._load_model() is not None

    def get_embedding_dimension(self) -> int:
        """Get the dimension of embeddings produced by this service."""
        return self._embedding_dimension

    def embed_text(self, text: str) -> List[float] | None:
        """
        Generate embedding vector for a single text.

        Args:
            text: Text to embed

        Returns:
            Embedding vector as list of floats, or None if embedding unavailable
        """
        if self.endpoint:
            return self._embed_text_with_ollama(text)

        model = self._load_model()
        if model is None:
            return None

        try:
            embedding = model.encode(text, convert_to_numpy=True)
            return embedding.tolist()
        except Exception as e:
            logger.error(f"Embedding generation failed: {e}")
            return None

    def embed_batch(
        self,
        texts: List[str],
        batch_size: int = 32,
        show_progress: bool = False
    ) -> List[List[float]] | None:
        """
        Generate embeddings for multiple texts efficiently.

        Args:
            texts: List of texts to embed
            batch_size: Number of texts to process at once
            show_progress: Show progress bar during batch encoding

        Returns:
            List of embedding vectors, or None if embedding unavailable
        """
        if self.endpoint:
            return self._embed_batch_with_ollama(texts)

        model = self._load_model()
        if model is None:
            return None

        try:
            embeddings = model.encode(
                texts,
                batch_size=batch_size,
                show_progress_bar=show_progress,
                convert_to_numpy=True
            )
            return [emb.tolist() for emb in embeddings]
        except Exception as e:
            logger.error(f"Batch embedding generation failed: {e}")
            return None
