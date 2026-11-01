"""Generates required_members.json, what the botocore service models require of the responses of DynamoDB and DynamoDB Streams.

A record that holds a response must not refuse what the service may send, and the service model is what says what it may send. So the
expected `required` members come from the models of AWS and not from the C# records: ResponseRecordsTests compares the two. The walk starts
at the output shape of every operation this library has and follows structures through lists and maps, which reaches the shapes a response
shares with a request as well.

The file maps each structure shape to its required members, sorted. A shape of the same name in both services must require the same, or the
script stops, because one record stands for both.

Setup (outside the repository):
    python3 -m venv /tmp/botocore-venv
    /tmp/botocore-venv/bin/pip install botocore

Run (from any directory):
    /tmp/botocore-venv/bin/python generate_required_members.py           # rewrites required_members.json
    /tmp/botocore-venv/bin/python generate_required_members.py --check   # exits 1 if the committed file is stale
"""

import argparse
import json
import pathlib
import sys

import botocore
import botocore.session

OUTPUT = pathlib.Path(__file__).with_name("required_members.json")

OPERATIONS = {
    "dynamodb": [
        "BatchGetItem",
        "BatchWriteItem",
        "CreateTable",
        "DeleteItem",
        "DeleteTable",
        "DescribeTable",
        "DescribeTimeToLive",
        "GetItem",
        "ListTables",
        "PutItem",
        "Query",
        "Scan",
        "TransactGetItems",
        "TransactWriteItems",
        "UpdateItem",
        "UpdateTable",
        "UpdateTimeToLive",
    ],
    "dynamodbstreams": ["DescribeStream", "GetRecords", "GetShardIterator", "ListStreams"],
}


def walk(shape, found):
    """Adds a shape and all it holds to found: structure name to sorted required members."""
    if shape.type_name == "list":
        walk(shape.member, found)
    elif shape.type_name == "map":
        walk(shape.value, found)
    elif shape.type_name == "structure":
        required = sorted(shape.required_members)
        if shape.name in found:
            if found[shape.name] != required:
                sys.exit(f"The two services require different members of {shape.name}.")
            return
        found[shape.name] = required
        for member in shape.members.values():
            walk(member, found)


def build():
    found = {}
    for service_name, operations in OPERATIONS.items():
        service = botocore.session.get_session().get_service_model(service_name)
        for operation in operations:
            walk(service.operation_model(operation).output_shape, found)
    return {
        "generator": "generate_required_members.py",
        "botocore": botocore.__version__,
        "note": "Generated from botocore's service models. Do not edit by hand.",
        "required": dict(sorted(found.items())),
    }


def render(document):
    return json.dumps(document, indent=2, ensure_ascii=True) + "\n"


def main():
    arguments = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    arguments.add_argument("--check", action="store_true", help="fail if the committed file differs from the generated one")
    options = arguments.parse_args()

    text = render(build())
    if options.check:
        # Git may check the file out with CRLF.
        current = OUTPUT.read_text(encoding="utf-8").replace("\r\n", "\n") if OUTPUT.exists() else ""
        # The botocore version is informational; the content is what matters.
        strip = lambda value: "\n".join(line for line in value.splitlines() if '"botocore":' not in line)
        if strip(current) != strip(text):
            sys.exit(f"{OUTPUT.name} is stale: run this script without --check.")
        print(f"{OUTPUT.name} is up to date.")
        return

    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as file:
        file.write(text)
    print(f"Wrote {OUTPUT} (botocore {botocore.__version__}).")


if __name__ == "__main__":
    main()
