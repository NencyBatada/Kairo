# Kairo agent service

The Python service that runs Kairo's agents (Sense, Plan, Create, Check, Prove and Learn) and Ask Kairo, built with Google's Agent Development Kit and FastAPI. The core calls it once per campaign stage; it never writes to Google Ads.

Run it on its own with `uv run uvicorn kairo_agents.main:app --reload`, or start the whole stack as described in the [root README](../README.md).

| Folder | Holds |
| --- | --- |
| `src/kairo_agents/agents/` | One package per agent. When an agent is built, its `agent.py` exposes `root_agent`, so `uv run adk web src/kairo_agents/agents` opens it in ADK's development UI. Brand Kit drafting (`brand_kit/`) is a single structured Gemini call through the Gen AI SDK, so it has no `root_agent`; its prompt is versioned in `brand_kit/prompts/`. |
| `src/kairo_agents/agents/create/` | Create's copy writers. `POST /create/copy` writes search ads with keywords, Demand Gen text and shopping titles in parallel structured Gemini calls, cleans them against the Google Ads limits in `limits.py`, the Brand Kit's banned words and competitors, and claims the products and brief don't support, and asks once more when a part breaks the rules. `POST /create/copy-instruction` rewrites one text asset from an instruction. |
| `src/kairo_agents/tools/` | Typed clients for the core's internal API |
| `src/kairo_agents/measurement/` | Holdout matching, smallest detectable lift and lift estimates. `holdout.py` backs `POST /plan/proof-plan`: it scores candidate regions by how their weekly sales, conversions and search demand move with the test area, and finds the smallest detectable lift from placebo tests on past periods, in numpy with no model call. |
| `src/kairo_agents/media/` | FFmpeg helpers for video |
