# Kairo agent service

The Python service that runs Kairo's agents (Sense, Plan, Create, Check, Prove and Learn) and Ask Kairo, built with Google's Agent Development Kit and FastAPI. The core calls it once per campaign stage; it never writes to Google Ads.

Run it on its own with `uv run uvicorn kairo_agents.main:app --reload`, or start the whole stack as described in the [root README](../README.md).

| Folder | Holds |
| --- | --- |
| `src/kairo_agents/agents/` | One package per agent. When an agent is built, its `agent.py` exposes `root_agent`, so `uv run adk web src/kairo_agents/agents` opens it in ADK's development UI. Brand Kit drafting (`brand_kit/`) is a single structured Gemini call through the Gen AI SDK, so it has no `root_agent`; its prompt is versioned in `brand_kit/prompts/`. |
| `src/kairo_agents/agents/create/` | Create's copy writers. `POST /create/copy` writes search ads with keywords, Demand Gen text and shopping titles in parallel structured Gemini calls, cleans them against the Google Ads limits in `limits.py`, the Brand Kit's banned words and competitors, and claims the products and brief don't support (English and Hindi claim words alike), checks the text is in the language's own script (`languages.py`, which lists every brief language with its script and, for English and Hindi, a voice), and asks once more when a part breaks the rules. The core calls it once per brief language. `POST /create/copy-instruction` rewrites one text asset from an instruction, keeping its language. |
| `src/kairo_agents/tools/` | Typed clients for the core's internal API |
| `src/kairo_agents/agents/ask/` | `POST /ask/turn` runs a request-scoped ADK conversation with a configurable Gemini Pro model (`KAIRO_ASK_MODEL`). Core owns private history and passes recent turns. Read, propose and answer-segment tools call the configured `KAIRO_API_URL`, using `KAIRO_API_AUDIENCE` on Cloud Run and the invocation credential supplied by core. Tools cannot confirm proposals, approve gates, launch or raise spend. Answers and proposal progress stream through core's private live events. |
| `src/kairo_agents/measurement/` | Holdout matching, smallest detectable lift and lift estimates. `holdout.py` backs `POST /plan/proof-plan`: it scores candidate regions by how their weekly sales, conversions and search demand move with the test area, and finds the smallest detectable lift from placebo tests on past periods, in numpy with no model call. |
| `src/kairo_agents/media/` | Image sizing and video helpers for the Create agent: cropping to the Google Ads image sizes, the voiceover track, captions and the logo end card, put together with FFmpeg. FFmpeg comes from the `imageio-ffmpeg` package, so nothing needs installing; set `KAIRO_FFMPEG` to use another build. Captions are rendered by libass with complex shaping and the bundled Noto Sans and Noto Sans Devanagari fonts (`media/fonts/`, SIL Open Font License), so Hindi is drawn correctly. |

## Create agent media

| Route | What it does | Model (environment variable) |
| --- | --- | --- |
| `POST /create/images` | Makes product and lifestyle images with the catalogue photos as reference, then crops each to an exact Ads size: landscape 1200×628, square 1200×1200, portrait 960×1200 and vertical 1080×1920. Sizes that fail are listed in `failures`. | Gemini image generation, default `gemini-2.5-flash-image` (`KAIRO_CREATE_IMAGE_MODEL`) |
| `POST /create/footage` | Plans the scenes, then animates each starting frame into an 8-second 9:16 clip with Veo, with no sound. | Scenes: `KAIRO_CREATE_MODEL` (default `gemini-3.5-flash`). Clips: Veo, default `veo-3.1-generate-001` (`KAIRO_CREATE_VIDEO_MODEL`), in `KAIRO_MEDIA_LOCATION` (default `us-central1`), because Veo isn't served from `global` |
| `POST /create/video` | Writes the voiceover script and captions, reads it with Cloud Text-to-Speech, and puts the clips, captions, voiceover and logo end card together into a 15-second 1080×1920 MP4. It also returns the voiceover as an MP3. | Script: `KAIRO_CREATE_MODEL`. Voice: Chirp 3 HD per language (`en-IN-Chirp3-HD-Achird` and `hi-IN-Chirp3-HD-Achird`, from the language table in `create/languages.py`), overridable with `KAIRO_TTS_VOICE_<LANGUAGE>`, such as `KAIRO_TTS_VOICE_HI`. A script in the wrong language is asked for once more. Languages without a voice are refused with 422 `no_voice` |

Media travels as base64 in the JSON; the core stores it, so the agent service never writes to Cloud Storage.

