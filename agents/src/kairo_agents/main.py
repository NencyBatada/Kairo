from fastapi import FastAPI

from kairo_agents.agents.brand_kit.api import router as brand_kit_router
from kairo_agents.agents.create.api import router as create_router
from kairo_agents.agents.create.media_api import router as create_media_router
from kairo_agents.agents.plan.api import router as plan_router
from kairo_agents.agents.plan.proof_api import router as proof_router
from kairo_agents.agents.sense.api import router as sense_router

app = FastAPI(title="Kairo agent service")
app.include_router(brand_kit_router)
app.include_router(sense_router)
app.include_router(plan_router)
app.include_router(proof_router)
app.include_router(create_router)
app.include_router(create_media_router)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}
