# AI quality evaluations

`v1` contains synthetic English/Hindi campaign inputs, deterministic provider replay responses,
two image-generation placements, one eight-second video-generation request, and forty Guardian
cases: twenty planted issues and twenty clean controls. Six issues are critical. The manifest
pins every input and media file by SHA-256. Change the fixture version when changing the corpus.
`build_fixtures.py` regenerates these synthetic assets; it makes no provider requests.

From `agents`, run `uv run python -m kairo_agents.evaluation --offline --output ../TestResults/evaluation-offline`.
This exercises production functions using injected provider replay and checks deterministic
format and Guardian rules. It never claims to verify model quality.

Live runs use `--live`, production generation and Guardian functions, and Google's
`evaluateInstances` pointwise evaluation service for brief/copy quality. Every brief/copy score
must be at least 0.8. Guardian must detect at least 95% of planted issues, reject all critical
issues, and falsely flag at most 5% of clean controls. All generation format checks must pass.
Generated media also undergoes Guardian screening. Critical findings are evaluated with the
production default blocking rules; existing authorized human overrides remain separate.

The runner reserves each provider request before dispatch, including repair attempts, and
disables SDK retries. It permits at most 100 text/judge requests, two image requests and one
video request, with a default fifteen-minute deadline and an absolute twenty-minute ceiling.
There is no automatic rerun when a limit is exhausted. Unknown tokens and unresolved provider
model versions remain null. Reports include fixture and prompt hashes, reported prompt/model
versions, scores, usage and failures. Generated synthetic outputs are separate artifact files.
Missing credentials, errors, timeouts and incomplete cases fail the live gate.

Live credentials must be GitHub OIDC external-account credentials impersonating the dedicated
`kairo-evaluations` service account. Required environment settings are `KAIRO_EVAL_PROJECT`,
`KAIRO_EVAL_SERVICE_ACCOUNT`, `KAIRO_EVAL_PROVIDER`, `GOOGLE_APPLICATION_CREDENTIALS` and
`KAIRO_EVAL_COMMIT`. No user credentials or service-account keys are accepted by the live CLI.

The Google judge uses the managed evaluation service. Its underlying model version and token
usage are not returned by that API and are recorded as unknown.
