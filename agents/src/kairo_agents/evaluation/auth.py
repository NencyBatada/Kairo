import json
import os
import re
from pathlib import Path
from typing import Any

import google.auth


def live_credentials(project: str, service_account: str, provider: str) -> Any:
    if not re.fullmatch(r"[a-z][a-z0-9-]{4,61}[a-z0-9]", project):
        raise ValueError("invalid_evaluation_project")
    if service_account != f"kairo-evaluations@{project}.iam.gserviceaccount.com":
        raise ValueError("dedicated_evaluation_identity_required")
    if not re.fullmatch(
        r"projects/\d+/locations/global/workloadIdentityPools/[a-z0-9-]+/providers/[a-z0-9-]+",
        provider,
    ):
        raise ValueError("invalid_evaluation_provider")
    name = os.environ.get("GOOGLE_APPLICATION_CREDENTIALS")
    if not name:
        raise ValueError("oidc_credentials_required")
    data = json.loads(Path(name).read_text(encoding="utf-8"))
    endpoint = (
        "https://iamcredentials.googleapis.com/v1/projects/-/serviceAccounts/"
        + service_account
        + ":generateAccessToken"
    )
    if (
        data.get("type") != "external_account"
        or data.get("audience") != "//iam.googleapis.com/" + provider
        or data.get("service_account_impersonation_url") != endpoint
        or data.get("token_url") != "https://sts.googleapis.com/v1/token"
        or data.get("subject_token_type") != "urn:ietf:params:oauth:token-type:jwt"
    ):
        raise ValueError("trusted_oidc_identity_required")
    credentials, _ = google.auth.load_credentials_from_file(
        name, scopes=["https://www.googleapis.com/auth/cloud-platform"]
    )
    return credentials
