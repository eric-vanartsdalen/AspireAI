"""
PydanticAI provider implementation — wraps PydanticAI behind the AgentProvider protocol.

This adapter allows the critique pipeline to use PydanticAI for agent orchestration
while maintaining the ability to swap frameworks without changing pipeline logic.
"""

from __future__ import annotations

import logging
import os
from typing import Any

from pydantic_ai import Agent

from .agent_provider import AgentProvider, AgentResponse

logger = logging.getLogger(__name__)


class PydanticAIProvider:
    """
    PydanticAI adapter implementing the AgentProvider protocol.

    Manages PydanticAI agent instances and translates between framework-specific
    types and the normalized AgentProvider contract.
    """

    def __init__(
        self,
        model_name: str | None = None,
        endpoint: str | None = None,
    ):
        """
        Initialize PydanticAI provider.

        Args:
            model_name: LLM model to use (defaults to env OLLAMA_MODEL or phi4-mini:latest)
            endpoint: Ollama endpoint (defaults to env OLLAMA_ENDPOINT)
        """
        self.model_name = model_name or os.getenv("OLLAMA_MODEL", "phi4-mini:latest")
        self.endpoint = endpoint or os.getenv("OLLAMA_ENDPOINT", "")
        self._agent_cache: dict[str, Agent] = {}

        # Default system prompts for standard agent roles
        self._default_prompts = {
            "planner": "You are a planning agent. Break down complex questions into logical sub-queries.",
            "retriever": "You are a retrieval agent. Query the knowledge base and return relevant information.",
            "synthesizer": "You are a synthesis agent. Merge multiple pieces of information into a coherent response.",
            "critic": "You are a critic agent. Evaluate responses for accuracy, consistency, and gaps.",
        }

    def _get_or_create_agent(self, agent_name: str, system_prompt: str | None = None) -> Agent:
        """Get cached agent or create new one with the given system prompt."""
        cache_key = f"{agent_name}:{system_prompt}"
        
        if cache_key not in self._agent_cache:
            prompt = system_prompt or self._default_prompts.get(agent_name, "You are a helpful AI assistant.")
            
            # Create PydanticAI agent configured for Ollama
            # Use openai-compatible endpoint with custom base_url
            agent = Agent(
                model=f"openai:{self.model_name}",
                system_prompt=prompt,
            )
            
            # Configure for Ollama endpoint if available
            if self.endpoint:
                # PydanticAI uses httpx for API calls - we'll rely on environment configuration
                os.environ["OPENAI_BASE_URL"] = f"{self.endpoint}/v1"
                os.environ["OPENAI_API_KEY"] = "ollama"  # Ollama doesn't need real key
            
            self._agent_cache[cache_key] = agent
        
        return self._agent_cache[cache_key]

    async def run_agent(
        self,
        agent_name: str,
        prompt: str,
        context: dict[str, Any] | None = None,
        tools: list[str] | None = None,
    ) -> AgentResponse:
        """
        Execute a named agent with PydanticAI.

        Args:
            agent_name: Agent role identifier
            prompt: User prompt for the agent
            context: Additional context (currently passed as part of prompt)
            tools: Tool names (not yet implemented - future extension)

        Returns:
            Normalized AgentResponse
        """
        try:
            agent = self._get_or_create_agent(agent_name)
            
            # Build full prompt with context
            full_prompt = prompt
            if context:
                context_str = "\n\n".join(f"{k}: {v}" for k, v in context.items())
                full_prompt = f"{context_str}\n\n{prompt}"
            
            # Run agent synchronously for now (PydanticAI supports async but needs setup)
            result = await agent.run(full_prompt)
            
            # Extract response data
            content = str(result.data) if hasattr(result, 'data') else str(result)
            
            # Try to extract reasoning from result metadata if available
            reasoning = None
            metadata = {}
            if hasattr(result, 'usage'):
                metadata['usage'] = result.usage
            
            return AgentResponse(
                content=content,
                reasoning=reasoning,
                tool_calls=None,  # Future: extract tool calls if PydanticAI supports
                metadata=metadata,
            )
        
        except Exception as e:
            logger.error(f"PydanticAI agent {agent_name} failed: {e}")
            raise RuntimeError(f"Agent {agent_name} execution failed: {e}")

    async def run_multi_agent(
        self,
        agents: list[tuple[str, str]],
        initial_context: dict[str, Any] | None = None,
    ) -> list[AgentResponse]:
        """
        Execute multiple agents sequentially, passing results forward.

        Each agent's output becomes available in context for subsequent agents.

        Args:
            agents: List of (agent_name, prompt) tuples
            initial_context: Starting context for first agent

        Returns:
            List of AgentResponse objects from each agent
        """
        context = initial_context or {}
        responses: list[AgentResponse] = []
        
        for agent_name, prompt in agents:
            response = await self.run_agent(agent_name, prompt, context=context)
            responses.append(response)
            
            # Add this agent's output to context for next agent
            context[f"{agent_name}_output"] = response.content
            if response.reasoning:
                context[f"{agent_name}_reasoning"] = response.reasoning
        
        return responses

    def is_available(self) -> bool:
        """Check if PydanticAI provider is configured."""
        return bool(self.endpoint and self.model_name)
