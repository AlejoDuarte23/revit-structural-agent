from __future__ import annotations

import argparse
import json
import os
import sys
import uuid
from pathlib import Path

from aps_automation_sdk import (
    Activity,
    ActivityInputParameter,
    ActivityOutputParameter,
    AppBundle,
    WorkItem,
    delete_activity,
    delete_appbundle,
    get_token,
    set_nickname,
)


YEAR = 2025
APP_BUNDLE_BASENAME = "PileFoundationsDA"
ACTIVITY_BASENAME = "PileFoundationsActivity"
DEFAULT_ALIAS = "dev"
DEFAULT_OUTPUT_LOCAL_NAME = "result.rvt"
DEFAULT_PAYLOAD_LOCAL_NAME = "pile_foundations.json"


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent

    parser = argparse.ArgumentParser(
        description="Deploy and run the Revit pile foundations Design Automation bundle."
    )
    parser.add_argument("--nickname", required=True, help="APS nickname to use for appbundle and activity aliases.")
    parser.add_argument("--input-rvt", required=True, type=Path, help="Path to the input Revit model.")
    parser.add_argument("--input-json", required=True, type=Path, help="Path to the pile foundations JSON payload.")
    parser.add_argument(
        "--bundle-zip",
        type=Path,
        default=script_dir / "files" / "PileFoundationsDA.bundle.zip",
        help="Path to the zipped .bundle payload produced by build-da-bundle.ps1.",
    )
    parser.add_argument(
        "--output-rvt",
        type=Path,
        default=script_dir / "files" / "output" / DEFAULT_OUTPUT_LOCAL_NAME,
        help="Where to download the generated Revit model.",
    )
    parser.add_argument("--alias", default=DEFAULT_ALIAS, help="Alias for both the appbundle and activity.")
    parser.add_argument("--appbundle-name", default=f"{APP_BUNDLE_BASENAME}{YEAR}")
    parser.add_argument("--activity-name", default=f"{ACTIVITY_BASENAME}{YEAR}")
    parser.add_argument("--timeout", type=int, default=900, help="Maximum wait time in seconds.")
    parser.add_argument("--poll-interval", type=int, default=10, help="Polling interval in seconds.")
    parser.add_argument("--cleanup", action="store_true", help="Delete the activity and appbundle after the run.")
    return parser.parse_args()


def get_required_env(name: str) -> str:
    value = os.getenv(name)
    if value:
        return value

    print(f"Missing required environment variable: {name}", file=sys.stderr)
    raise SystemExit(2)


def main() -> int:
    args = parse_args()

    client_id = get_required_env("CLIENT_ID")
    client_secret = get_required_env("CLIENT_SECRET")

    bundle_zip = args.bundle_zip.resolve()
    input_rvt = args.input_rvt.resolve()
    input_json = args.input_json.resolve()
    output_rvt = args.output_rvt.resolve()

    if not bundle_zip.exists():
        raise FileNotFoundError(f"Bundle zip not found: {bundle_zip}")

    if not input_rvt.exists():
        raise FileNotFoundError(f"Input Revit file not found: {input_rvt}")

    if not input_json.exists():
        raise FileNotFoundError(f"Input JSON file not found: {input_json}")

    token = get_token(client_id=client_id, client_secret=client_secret)
    nickname = set_nickname(token, args.nickname)

    appbundle_full_alias = f"{nickname}.{args.appbundle_name}+{args.alias}"
    activity_full_alias = f"{nickname}.{args.activity_name}+{args.alias}"
    bucket_key = uuid.uuid4().hex

    bundle = AppBundle(
        appBundleId=args.appbundle_name,
        engine=f"Autodesk.Revit+{YEAR}",
        alias=args.alias,
        zip_path=str(bundle_zip),
        description=f"Create pile foundations for Revit {YEAR}",
    )
    bundle.deploy(token)

    input_revit = ActivityInputParameter(
        name="inputModel",
        localName="input.rvt",
        verb="get",
        description="Input Revit model",
        required=True,
        is_engine_input=True,
        bucketKey=bucket_key,
        objectKey="input.rvt",
    )

    input_payload = ActivityInputParameter(
        name="pilePayload",
        localName=DEFAULT_PAYLOAD_LOCAL_NAME,
        verb="get",
        description="Pile foundations JSON payload",
        required=True,
        bucketKey=bucket_key,
        objectKey=DEFAULT_PAYLOAD_LOCAL_NAME,
    )

    output_model = ActivityOutputParameter(
        name="resultModel",
        localName=DEFAULT_OUTPUT_LOCAL_NAME,
        verb="put",
        description="Output Revit model with pile foundations",
        bucketKey=bucket_key,
        objectKey=DEFAULT_OUTPUT_LOCAL_NAME,
    )

    activity = Activity(
        id=args.activity_name,
        parameters=[input_revit, input_payload, output_model],
        engine=f"Autodesk.Revit+{YEAR}",
        appbundle_full_name=appbundle_full_alias,
        description=f"Create pile foundations in Revit {YEAR} from JSON",
        alias=args.alias,
    )
    activity.set_revit_command_line()
    activity.deploy(token=token)

    input_revit.upload_file_to_oss(file_path=str(input_rvt), token=token)
    input_payload.upload_file_to_oss(file_path=str(input_json), token=token)

    work_item = WorkItem(
        parameters=[input_revit, input_payload, output_model],
        activity_full_alias=activity_full_alias,
    )

    status_response = work_item.execute(
        token=token,
        max_wait=args.timeout,
        interval=args.poll_interval,
    )
    last_status = status_response.get("status", "")

    print(json.dumps(status_response, indent=2))

    if last_status != "success":
        raise RuntimeError(f"Work item failed with status '{last_status}'.")

    output_rvt.parent.mkdir(parents=True, exist_ok=True)
    output_model.download_to(output_path=str(output_rvt), token=token)
    print(f"Downloaded generated Revit model to: {output_rvt}")

    if args.cleanup:
        delete_activity(activityId=args.activity_name, token=token)
        delete_appbundle(appbundleId=args.appbundle_name, token=token)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
