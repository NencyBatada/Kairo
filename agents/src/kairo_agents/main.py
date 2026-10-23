from fastapi import FastAPI

from kairo_agents.agents.ask.api import router as ask_router
from kairo_agents.agents.brand_kit.api import router as brand_kit_router
from kairo_agents.agents.check.api import router as check_router
from kairo_agents.agents.create.api import router as create_router
from kairo_agents.agents.create.media_api import router as create_media_router
from kairo_agents.agents.learn.api import router as learn_router
from kairo_agents.agents.plan.api import router as plan_router
from kairo_agents.agents.plan.proof_api import router as proof_router
from kairo_agents.agents.prove.api import router as prove_router
from kairo_agents.agents.prove.lab_api import router as prove_lab_router
from kairo_agents.agents.prove.report_api import router as prove_report_router
from kairo_agents.agents.sense.api import router as sense_router
from kairo_agents.media.accessibility import router as accessibility_router
from kairo_agents.telemetry import TelemetryMiddleware, configure

configure()
app = FastAPI(title="Kairo agent service")
app.add_middleware(TelemetryMiddleware)
app.include_router(ask_router)
app.include_router(brand_kit_router)
app.include_router(sense_router)
app.include_router(plan_router)
app.include_router(proof_router)
app.include_router(create_router)
app.include_router(create_media_router)
app.include_router(check_router)
app.include_router(prove_router)
app.include_router(prove_report_router)
app.include_router(prove_lab_router)
app.include_router(learn_router)
app.include_router(accessibility_router)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}
