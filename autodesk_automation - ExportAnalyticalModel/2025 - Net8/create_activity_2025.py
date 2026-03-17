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
APP_BUNDLE_BASENAME = "AnalyticalExportDA"
ACTIVITY_BASENAME = "AnalyticalExportActivity"
DEFAULT_ALIAS = "dev"
DEFAULT_OUTPUT_LOCAL_NAME = "analytical_export.json"


def parse_args() -> argparse.Namespace:
    script_dir = Path(__file__).resolve().parent

    parser = argparse.ArgumentParser(
        description="Deploy and run the Revit analytical export Design Automation bundle."
    )
    parser.add_argument("--nickname", required=True, help="APS nickname to use for appbundle and activity aliases.")
    parser.add_argument("--input-rvt", required=True, type=Path, help="Path to the input Revit model.")
    parser.add_argument(
        "--bundle-zip",
        type=Path,
        default=script_dir / "files" / "AnalyticalExportDA.bundle.zip",
        help="Path to the zipped .bundle payload produced by build-da-bundle.ps1.",
    )
    parser.add_argument(
        "--output-json",
        type=Path,
        default=script_dir / "files" / "output" / DEFAULT_OUTPUT_LOCAL_NAME,
        help="Where to download the exported JSON result.",
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
    output_json = args.output_json.resolve()

    if not bundle_zip.exists():
        raise FileNotFoundError(f"Bundle zip not found: {bundle_zip}")

    if not input_rvt.exists():
        raise FileNotFoundError(f"Input Revit file not found: {input_rvt}")

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
        description=f"Analytical export for Revit {YEAR}",
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

    output_result = ActivityOutputParameter(
        name="exportJson",
        localName=DEFAULT_OUTPUT_LOCAL_NAME,
        verb="put",
        description="Exported analytical model JSON",
        bucketKey=bucket_key,
        objectKey=DEFAULT_OUTPUT_LOCAL_NAME,
    )

    activity = Activity(
        id=args.activity_name,
        parameters=[input_revit, output_result],
        engine=f"Autodesk.Revit+{YEAR}",
        appbundle_full_name=appbundle_full_alias,
        description=f"Export analytical model JSON for Revit {YEAR}",
        alias=args.alias,
    )
    activity.set_revit_command_line()
    activity.deploy(token=token)

    input_revit.upload_file_to_oss(file_path=str(input_rvt), token=token)

    work_item = WorkItem(
        parameters=[input_revit, output_result],
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

    output_json.parent.mkdir(parents=True, exist_ok=True)
    output_result.download_to(output_path=str(output_json), token=token)
    print(f"Downloaded analytical export to: {output_json}")

    if args.cleanup:
        delete_activity(activityId=args.activity_name, token=token)
        delete_appbundle(appbundleId=args.appbundle_name, token=token)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
