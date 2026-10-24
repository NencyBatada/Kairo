import argparse
import asyncio
import logging
from pathlib import Path

from kairo_agents.evaluation.budget import Limits
from kairo_agents.evaluation.runner import evaluate


def main() -> int:
    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--live", action="store_true")
    mode.add_argument("--offline", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--seconds", type=float, default=1080)
    args = parser.parse_args()
    logging.disable(logging.CRITICAL)
    passed = asyncio.run(evaluate(args.output, args.live, Limits(seconds=args.seconds)))
    print("Evaluation passed" if passed else "Evaluation failed; inspect report.json")
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
