FROM python:3.13-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

COPY --from=ghcr.io/astral-sh/uv:0.12.5 /uv /uvx /bin/

WORKDIR /app
ENV UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    UV_PYTHON_DOWNLOADS=never

COPY pyproject.toml uv.lock README.md ./
RUN uv sync --locked --no-dev --no-install-project

COPY src ./src
RUN uv sync --locked --no-dev

RUN useradd --system --uid 10001 kairo
USER kairo

ENV PATH="/app/.venv/bin:$PATH" \
    PORT=8080
EXPOSE 8080
CMD ["sh", "-c", "exec uvicorn kairo_agents.main:app --host 0.0.0.0 --port ${PORT}"]
