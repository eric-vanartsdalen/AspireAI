"""
Agent provider abstraction — framework-agnostic interface for agent orchestration.

This protocol defines the boundary between the critique pipeline and the underlying
agent framework (currently PydanticAI, but designed to be swappable).
"""

from __future__ import annotations

from typing import Protocol, Any
from dataclasses import dataclass


@dataclass
class AgentResponse:
    """Normalized agent response shape, independent of framework internals."""

    content: str
    reasoning: str | None = None
    tool_calls: list[dict[str, Any]] | None = None
    metadata: dict[str, Any] | None = None


class AgentProvider(Protocol):
    """
    Framework-agnostic agent orchestration interface.

    Implementations wrap specific frameworks (PydanticAI, LangGraph, CrewAI)
    behind this contract to keep the critique pipeline decoupled.
    """

    async def run_agent(
        self,
        agent_name: str,
        prompt: str,
        context: dict[str, Any] | None = None,
        tools: list[str] | None = None,
    ) -> AgentResponse:
        """
        Execute a named agent with the given prompt and context.

        Args:
            agent_name: Identifier for the agent role (planner, retriever, synthesizer, critic)
            prompt: Natural language instruction for the agent
            context: Additional context data accessible to the agent
            tools: Optional list of tool names the agent can use

        Returns:
            AgentResponse with normalized content, reasoning, and metadata
        """
        ...

    async def run_multi_agent(
        self,
        agents: list[tuple[str, str]],
        initial_context: dict[str, Any] | None = None,
    ) -> list[AgentResponse]:
        """
        Execute multiple agents sequentially, passing context between them.

        Args:
            agents: List of (agent_name, prompt) tuples to execute in order
            initial_context: Starting context passed to first agent

        Returns:
            List of AgentResponse objects, one per agent execution
        """
        ...

    def is_available(self) -> bool:
        """Check if the provider is configured and ready to use."""
        ...
