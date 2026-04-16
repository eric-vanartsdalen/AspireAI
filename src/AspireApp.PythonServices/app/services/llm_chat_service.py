"""
LLM chat service for generating responses via Ollama.

Uses the same urllib-based HTTP pattern as EmbeddingService for consistency.
Supports Ollama /api/chat endpoint for multi-turn chat completions.
"""

import json
import logging
import os
from typing import Any
from urllib import error, request

from ..contracts import ConversationMessage

logger = logging.getLogger(__name__)

_DEFAULT_SYSTEM_PROMPT = (
    "You are a knowledgeable assistant. Use the provided context to answer "
    "the user's question accurately. If the context does not contain enough "
    "information, say so clearly rather than guessing. Cite your sources "
    "when possible."
)


class LlmChatService:
    """Generate chat completions via Ollama."""

    def __init__(
        self,
        model_name: str | None = None,
        endpoint: str | None = None,
        timeout_seconds: int | None = None,
    ):
        self.model_name = model_name or os.getenv("CHAT_MODEL", "phi4-mini:latest")
        self.endpoint = (endpoint or os.getenv("OLLAMA_ENDPOINT") or "").rstrip("/")
        self._timeout_seconds = timeout_seconds or int(os.getenv("CHAT_TIMEOUT", "120"))

    def _post_ollama(self, path: str, payload: dict[str, Any]) -> dict[str, Any]:
        if not self.endpoint:
            raise RuntimeError("OLLAMA_ENDPOINT is not configured.")

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

    def is_available(self) -> bool:
        """Check if the chat service endpoint is configured."""
        return bool(self.endpoint)

    def generate(
        self,
        user_message: str,
        *,
        system_prompt: str | None = None,
        context: str | None = None,
        conversation_history: list[ConversationMessage] | None = None,
    ) -> str:
        """
        Generate a chat completion.

        Args:
            user_message: The user's question or prompt.
            system_prompt: Optional system prompt override.
            context: Retrieved knowledge context to inject before the user message.
            conversation_history: Prior user/assistant turns for follow-up context.

        Returns:
            The assistant's response text.

        Raises:
            RuntimeError: If Ollama is unavailable or the request fails.
        """
        messages: list[dict[str, str]] = []

        system = system_prompt or _DEFAULT_SYSTEM_PROMPT
        if context:
            system = f"{system}\n\n--- Retrieved Context ---\n{context}"

        messages.append({"role": "system", "content": system})
        for message in conversation_history or []:
            role = message.role.strip().lower()
            content = message.content.strip()
            if role not in {"user", "assistant"} or not content:
                continue

            messages.append({"role": role, "content": content})

        messages.append({"role": "user", "content": user_message})

        try:
            response = self._post_ollama(
                "/api/chat",
                {
                    "model": self.model_name,
                    "messages": messages,
                    "stream": False,
                },
            )
            content = response.get("message", {}).get("content", "")
            if not content:
                logger.warning("Ollama returned empty response content")
            return content
        except (error.URLError, error.HTTPError) as e:
            logger.error(f"Ollama chat request failed: {e}")
            raise RuntimeError(f"LLM chat generation failed: {e}") from e
        except (json.JSONDecodeError, ValueError) as e:
            logger.error(f"Ollama chat response parse error: {e}")
            raise RuntimeError(f"LLM chat response invalid: {e}") from e
