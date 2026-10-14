from fastapi import FastAPI

from kairo_agents.agents.brand_kit.api import router as brand_kit_router

app = FastAPI(title="Kairo agent service")
app.include_router(brand_kit_router)


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}
