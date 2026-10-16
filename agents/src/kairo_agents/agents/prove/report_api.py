import logging
import os
from collections.abc import Callable
from typing import Annotated, Any

from fastapi import APIRouter, Depends
from fastapi.responses import JSONResponse
from google.genai import types

from kairo_agents.agents.brand_kit.api import default_client
from kairo_agents.agents.plan.api import usage_of
from kairo_agents.agents.prove.prompt import REPORT_PROMPT_VERSION, report_instructions
from kairo_agents.agents.prove.report import clean_report
from kairo_agents.agents.prove.report_models import DraftedReport, ReportRequest, ReportResponse
from kairo_agents.agents.sense.prompt import data_block

DEFAULT_MODEL = "gemini-3.5-pro"

logger = logging.getLogger(__name__)
router = APIRouter()


def model_name() -> str:
    return os.environ.get("KAIRO_PROVE_MODEL") or DEFAULT_MODEL


def client_source() -> Callable[[], Any]:
    return default_client


ClientSource = Annotated[Callable[[], Any], Depends(client_source)]


@router.post("/prove/report", response_model=ReportResponse)
async def report(request: ReportRequest, clients: ClientSource) -> Any:
    model = model_name()
    try:
        response = await clients().aio.models.generate_content(
            model=model,
            contents=data_block(request),
            config=types.GenerateContentConfig(
                system_instruction=report_instructions(),
                response_mime_type="application/json",
                response_schema=DraftedReport,
                temperature=0.2,
            ),
        )
        drafted = DraftedReport.model_validate_json(response.text or "")
    except Exception:
        logger.exception("Prove couldn't write the impact report.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    cleaned = clean_report(drafted, request)
    if cleaned is None:
        logger.warning("Prove rejected an impact report draft with unsupported or missing text.")
        return JSONResponse(status_code=502, content={"error": "model_failed"})
    return ReportResponse(
        summary=cleaned.summary,
        next_step_reasoning=cleaned.next_step_reasoning,
        inconclusive_reason=cleaned.inconclusive_reason,
        next_test_advice=cleaned.next_test_advice,
        model=model,
        prompt_version=REPORT_PROMPT_VERSION,
        usage=usage_of(response),
    )
