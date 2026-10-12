import importlib

import pytest

AGENTS = ["sense", "plan", "create", "check", "prove", "learn", "ask"]


@pytest.mark.parametrize("name", AGENTS)
def test_agent_package_exists(name: str) -> None:
    importlib.import_module(f"kairo_agents.agents.{name}")
