from fastapi import FastAPI

app = FastAPI(title="Kairo agent service")


@app.get("/health")
def health() -> dict[str, str]:
    return {"status": "ok"}
