# Kairo agent service

The Python service that runs Kairo's agents (Sense, Plan, Create, Check, Prove and Learn) and Ask Kairo, built with Google's Agent Development Kit and FastAPI. The core calls it once per campaign stage; it never writes to Google Ads.

Run it on its own with `uv run uvicorn kairo_agents.main:app --reload`, or start the whole stack as described in the [root README](../README.md).

| Folder | Holds |
| --- | --- |
| `src/kairo_agents/agents/` | One package per agent. When an agent is built, its `agent.py` exposes `root_agent`, so `uv run adk web src/kairo_agents/agents` opens it in ADK's development UI. Brand Kit drafting (`brand_kit/`) is a single structured Gemini call through the Gen AI SDK, so it has no `root_agent`; its prompt is versioned in `brand_kit/prompts/`. |
| `src/kairo_agents/tools/` | Typed clients for the core's internal API |
| `src/kairo_agents/measurement/` | Holdout matching, smallest detectable lift and lift estimates |
| `src/kairo_agents/media/` | FFmpeg helpers for video |
