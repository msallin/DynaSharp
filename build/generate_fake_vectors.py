"""Records the behaviour vectors of FakeDynamoDb: sequences of requests sent to DynamoDB Local, with what it answered.

The fake must not be its own oracle, so the answers come from DynamoDB Local, the emulator of AWS, in Docker. Each case is a short
sequence of requests on one table: the setup (CreateTable, PutItem) and the requests that decide an open question of the fake,
listed in spec/tasks.md under M4. botocore signs every request, as a client of DynamoDB would; the body is sent as written here,
not through botocore's serializer, so that a request the client model would refuse (a constant of the wrong type, 101 operands)
still reaches DynamoDB Local.

The test FakeVectorTests in tests/DynaSharp.UnitTests replays every case through FakeDynamoDb.Answer on a fresh fake and compares,
step by step: the HTTP status and the error code always, the message where the step says "compareMessage", and the body of a
successful answer as JSON (member order free, set elements in any order). The answers of CreateTable and DeleteTable are not
recorded, because they hold dates and ARNs; only their status is.

A string value "${x*307200}" in a request stands for 307200 times "x", so that items near the size limits stay small in the file, and "${streamArn}"
and "${shardId}" for the stream ARN and the first shard id the last answer of the case named. Both this script and the test expand them. A step can
record only some paths of its answer ("select" for the value, "present" for whether there is one), where the rest holds ARNs, ids and dates.

Setup (outside the repository):
    python3 -m venv /tmp/botocore-venv
    /tmp/botocore-venv/bin/pip install botocore

Run (needs Docker, starts and removes the image of tests/DynaSharp.IntegrationTests/Backends/dynamodb-local/Dockerfile):
    /tmp/botocore-venv/bin/python build/generate_fake_vectors.py                  # rewrites the vectors
    /tmp/botocore-venv/bin/python build/generate_fake_vectors.py --check          # exits 1 if a committed file is stale
    /tmp/botocore-venv/bin/python build/generate_fake_vectors.py --endpoint http://localhost:8001   # an emulator that runs already

Non-ASCII text is built from code points and written as \\uXXXX escapes (ensure_ascii), so editors and tools cannot change what the
vectors contain.
"""

import argparse
import json
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

import botocore.auth
import botocore.awsrequest
import botocore.credentials

ROOT = pathlib.Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "tests" / "DynaSharp.UnitTests" / "Testing" / "Fake" / "Vectors"
DOCKERFILE = ROOT / "tests" / "DynaSharp.IntegrationTests" / "Backends" / "dynamodb-local" / "Dockerfile"
TABLE = "fake-vectors"
REGION = "us-east-1"

E_ACUTE = chr(0x00E9)  # 2 bytes in UTF-8, 1 UTF-16 unit
GRIN = chr(0x1F600)  # 4 bytes in UTF-8, 2 UTF-16 units

# Input: "${x*3}" -> "xxx"
REPEAT = re.compile(r"^\$\{(.)\*(\d+)\}$")
# Input: "${streamArn}" -> the LatestStreamArn of the last answer that had one
VARIABLE = re.compile(r"^\$\{(streamArn|shardId)\}$")

# Their target prefix is DynamoDBStreams_20120810, and their answers hold ARNs, ids and dates, so only status, code and what a step selects are recorded.
STREAM_OPERATIONS = {"ListStreams", "DescribeStream", "GetShardIterator", "GetRecords"}


def S(value):
    return {"S": value}


def N(value):
    return {"N": str(value)}


def BOOL(value):
    return {"BOOL": value}


NULL = {"NULL": True}


def L(*values):
    return {"L": list(values)}


def M(**members):
    return {"M": members}


def SS(*values):
    return {"SS": list(values)}


def table(sort=False, global_index=None):
    """A CreateTable request on demand: pk (S), optionally sk (S), optionally a global index on g (S) with a projection type."""
    definitions = [{"AttributeName": "pk", "AttributeType": "S"}]
    keys = [{"AttributeName": "pk", "KeyType": "HASH"}]
    if sort:
        definitions.append({"AttributeName": "sk", "AttributeType": "S"})
        keys.append({"AttributeName": "sk", "KeyType": "RANGE"})
    request = {"TableName": TABLE, "AttributeDefinitions": definitions, "KeySchema": keys, "BillingMode": "PAY_PER_REQUEST"}
    if global_index is not None:
        definitions.append({"AttributeName": "g", "AttributeType": "S"})
        request["GlobalSecondaryIndexes"] = [
            {"IndexName": "gsi", "KeySchema": [{"AttributeName": "g", "KeyType": "HASH"}], "Projection": {"ProjectionType": global_index}},
        ]
    return ("CreateTable", request)


def put(item):
    return ("PutItem", {"TableName": TABLE, "Item": item})


def count(filter_expression, values, names=None, key=None):
    """A Query of the partition a that counts what passes a filter."""
    request = {
        "TableName": TABLE,
        "KeyConditionExpression": "pk = :pk",
        "FilterExpression": filter_expression,
        "Select": "COUNT",
        "ExpressionAttributeValues": {":pk": S(key or "a"), **values},
    }
    if names:
        request["ExpressionAttributeNames"] = names
    return ("Query", request)


def query(key_condition, values, **extra):
    return ("Query", {"TableName": TABLE, "KeyConditionExpression": key_condition, "ExpressionAttributeValues": values, **extra})


def update(key, expression, values=None, condition=None, returns=None):
    request = {"TableName": TABLE, "Key": {"pk": S(key)}, "UpdateExpression": expression}
    if values:
        request["ExpressionAttributeValues"] = values
    if condition:
        request["ConditionExpression"] = condition
    if returns:
        request["ReturnValues"] = returns
    return ("UpdateItem", request)


def delete_if(key, condition, values):
    return ("DeleteItem", {"TableName": TABLE, "Key": {"pk": S(key)}, "ConditionExpression": condition, "ExpressionAttributeValues": values})


def message(step):
    """Marks a step whose error message is part of what it decides."""
    return step + ({"compareMessage": True},)


def selecting(step, select=(), present=()):
    """Records of a success only the values at the paths of select, and whether there is one at the paths of present."""
    return step + ({"select": list(select), "present": list(present)},)


def stream_table(view="NEW_AND_OLD_IMAGES", enabled=True):
    name, request = table()
    specification = {"StreamEnabled": enabled}
    if view:
        specification["StreamViewType"] = view
    return (name, {**request, "StreamSpecification": specification})


def stream_spec(enabled, view=None):
    specification = {"StreamEnabled": enabled}
    if view:
        specification["StreamViewType"] = view
    return ("UpdateTable", {"TableName": TABLE, "StreamSpecification": specification})


def iterator(kind, sequence=None):
    request = {"StreamArn": "${streamArn}", "ShardId": "${shardId}", "ShardIteratorType": kind}
    if sequence:
        request["SequenceNumber"] = sequence
    return ("GetShardIterator", request)


DESCRIBE_STREAM = ("DescribeStream", {"StreamArn": "${streamArn}"})
UNKNOWN_STREAM = "arn:aws:dynamodb:us-east-1:000000000000:table/never-existed/stream/2026-01-01T00:00:00.000"


IN_VALUES = {f":v{i}": N(i) for i in range(101)}

# Each case: (file, name, what it decides, steps). A step is (operation, request) or (operation, request, options).
CASES = [
    ("conditions", "size-of-a-string-counts-utf8-bytes",
     "Open 1: size of a string counts UTF-8 bytes. The string is e-acute and a grin: 2 characters, 3 UTF-16 units, 6 bytes.",
     [table(), put({"pk": S("a"), "s": S(E_ACUTE + GRIN)}),
      count("size(s) = :n", {":n": N(2)}), count("size(s) = :n", {":n": N(3)}), count("size(s) = :n", {":n": N(6)})]),
    ("conditions", "size-of-a-scalar",
     "Open 2: size of a number, a boolean or a null, in a filter and in a condition.",
     [table(), put({"pk": S("a"), "n": N(5), "b": BOOL(True), "z": NULL}),
      count("size(n) > :zero", {":zero": N(0)}), count("size(b) > :zero", {":zero": N(0)}), count("size(z) > :zero", {":zero": N(0)}),
      update("a", "SET t = :t", {":t": S("x"), ":zero": N(0)}, condition="size(n) > :zero")]),
    ("conditions", "not-equal-on-missing-or-other-type",
     "Open 3: a <> :v holds where a is missing or of another type.",
     [table(), put({"pk": S("a"), "n": N(5)}),
      count("#m <> :v", {":v": N(5)}, names={"#m": "missing"}), count("n <> :v", {":v": S("5")}), count("n <> :v", {":v": N(5)})]),
    ("conditions", "constant-operands",
     "Open 4: size(:v), begins_with(:v, a), contains(:v, a) and :v = :w are evaluated.",
     [table(), put({"pk": S("a"), "s": S("abc")}),
      count("size(:v) = :three", {":v": S("abc"), ":three": N(3)}),
      count("begins_with(:v, s)", {":v": S("abcdef")}),
      count("contains(:v, s)", {":v": S("xabcx")}),
      count(":v = :w", {":v": S("x"), ":w": S("x")})]),
    ("conditions", "constant-of-the-wrong-type",
     "Open 4: a constant of the wrong type is rejected before the item is read for begins_with, ADD, DELETE, + and list_append, not for <. Every request names an item that does not exist.",
     [table(),
      delete_if("none", "begins_with(s, :n)", {":n": N(1)}),
      delete_if("none", "s < :l", {":l": L(S("x"))}),
      update("none", "ADD n :s", {":s": S("x")}),
      update("none", "DELETE ss :n", {":n": N(1)}),
      update("none", "SET x = :s + :n", {":s": S("x"), ":n": N(1)}),
      update("none", "SET x = list_append(:s, l)", {":s": S("x")})]),
    ("messages", "syntax-error",
     "Open 5: the message of a syntax error, with its token and near.",
     [table(), message(count("pk = = :v", {":v": S("a")}))]),
    ("messages", "function-not-allowed-in-a-condition",
     "Open 5: a function of updates in a condition.",
     [table(), message(delete_if("a", "if_not_exists(a, :v) = :v", {":v": S("x")}))]),
    ("messages", "in-with-101-operands",
     "Open 5: IN takes at most 100 operands.",
     [table(), message(count("n IN (" + ", ".join(IN_VALUES) + ")", IN_VALUES))]),
    ("messages", "key-condition-operator",
     "Open 5: an operator a key condition does not take.",
     [table(sort=True), message(query("pk = :p AND sk <> :s", {":p": S("a"), ":s": S("1")}))]),
    ("messages", "key-condition-two-names",
     "Open 5: two attribute names in one condition of a key condition.",
     [table(sort=True), message(query("pk = :p AND sk = pk", {":p": S("a")}))]),
    ("messages", "key-condition-without-a-key",
     "Open 5: a key condition that compares two values.",
     [table(sort=True), message(query(":p = :q", {":p": S("a"), ":q": S("a")}))]),
    ("messages", "key-condition-on-a-non-key-attribute",
     "Open 5: a key condition on an attribute that is no key.",
     [table(sort=True), message(query("pk = :p AND #o = :o", {":p": S("a"), ":o": S("1")}, ExpressionAttributeNames={"#o": "other"}))]),
    ("messages", "key-condition-without-the-partition-key",
     "Open 5: a key condition on the sort key only.",
     [table(sort=True), message(query("sk = :s", {":s": S("1")}))]),
    ("messages", "name-starting-with-an-underscore",
     "Finding of P2: a bare name that starts with _ is a syntax error.",
     [table(), message(count("_a = :v", {":v": S("x")}))]),
    ("updates", "remove-and-set-paths",
     "Open 6: REMOVE a.b without a, with a but without b, SET past the end of a list, REMOVE past the end of a list.",
     [table(), put({"pk": S("a"), "m": M(x=N(1)), "l": L(N(1), N(2))}),
      update("a", "REMOVE nomap.b", returns="ALL_NEW"),
      update("a", "REMOVE m.b", returns="ALL_NEW"),
      update("a", "SET l[10] = :v", {":v": N(3)}, returns="ALL_NEW"),
      update("a", "REMOVE l[10]", returns="ALL_NEW")]),
    ("updates", "updated-old-and-new",
     "Open 7: what UPDATED_OLD and UPDATED_NEW hold for an update of a nested path, and that a removed path is not in UPDATED_NEW.",
     [table(), put({"pk": S("a"), "m": M(x=N(1), y=N(2)), "l": L(N(1), N(2)), "t": S("top")}),
      update("a", "SET m.x = :n", {":n": N(10)}, returns="UPDATED_NEW"),
      update("a", "SET m.x = :n", {":n": N(11)}, returns="UPDATED_OLD"),
      update("a", "REMOVE m.y SET t = :s", {":s": S("changed")}, returns="UPDATED_NEW"),
      update("a", "REMOVE m.x", returns="UPDATED_OLD")]),
    ("keys", "key-condition-sides-and-empty-values",
     "Open 8: a key condition takes the value on either side, and rejects an empty partition value but not an empty sort value.",
     [table(sort=True), put({"pk": S("a"), "sk": S("1")}), put({"pk": S("a"), "sk": S("2")}), put({"pk": S("a"), "sk": S("3")}),
      query("pk = :p AND :v < sk", {":p": S("a"), ":v": S("1")}),
      query("sk > :v AND pk = :p", {":p": S("a"), ":v": S("2")}),
      query("pk = :p", {":p": S("")}),
      query("pk = :p AND sk = :s", {":p": S("a"), ":s": S("")})]),
    ("reads", "select-rules",
     "P3 open: the messages of Select: SPECIFIC_ATTRIBUTES without a projection, COUNT with one, ALL_ATTRIBUTES on a KEYS_ONLY index, ALL_PROJECTED_ATTRIBUTES on the table.",
     [table(global_index="KEYS_ONLY"),
      message(query("pk = :p", {":p": S("a")}, Select="SPECIFIC_ATTRIBUTES")),
      message(query("pk = :p", {":p": S("a")}, Select="COUNT", ProjectionExpression="pk")),
      message(query("g = :g", {":g": S("a")}, IndexName="gsi", Select="ALL_ATTRIBUTES")),
      message(query("pk = :p", {":p": S("a")}, Select="ALL_PROJECTED_ATTRIBUTES")),
      message(query("pk = :p", {":p": S("a")}, Select="ALL_ATTRIBUTES", ProjectionExpression="pk")),
      message(query("g = :g", {":g": S("a")}, IndexName="gsi", Select="ALL_PROJECTED_ATTRIBUTES", ProjectionExpression="pk")),
      message(("Scan", {"TableName": TABLE, "Select": "ALL_PROJECTED_ATTRIBUTES"})),
      message(("Scan", {"TableName": TABLE, "Select": "COUNT", "ProjectionExpression": "pk"}))]),
    ("reads", "page-of-one-megabyte",
     "P3 open: the item that brings a page to 1 MB ends it. Five items of 300 KB, one partition: the fourth passes 1 MB, and the page ends with it.",
     [table(sort=True)]
     + [put({"pk": S("a"), "sk": S(str(i)), "v": S("${x*307200}")}) for i in range(1, 6)]
     + [query("pk = :p", {":p": S("a")}, ProjectionExpression="sk"), query("pk = :p", {":p": S("a")}, Select="COUNT")]),
    ("writes", "transaction-data-error",
     "P4 open: a data error inside a transaction, a key of the wrong type or a number of 39 digits.",
     [table(),
      ("TransactWriteItems", {"TransactItems": [
          {"Put": {"TableName": TABLE, "Item": {"pk": S("a")}}},
          {"Put": {"TableName": TABLE, "Item": {"pk": N(1)}}}]}),
      ("TransactWriteItems", {"TransactItems": [
          {"Put": {"TableName": TABLE, "Item": {"pk": S("a")}}},
          {"Put": {"TableName": TABLE, "Item": {"pk": S("b"), "n": N("1" * 39)}}}]})]),
    ("writes", "transaction-capacity",
     "P4 open: what a condition check costs, and what a transaction with a token costs the first time.",
     [table(), put({"pk": S("a")}),
      ("TransactWriteItems", {"TransactItems": [
          {"ConditionCheck": {"TableName": TABLE, "Key": {"pk": S("a")}, "ConditionExpression": "attribute_exists(pk)"}}],
          "ReturnConsumedCapacity": "TOTAL"}),
      ("TransactWriteItems", {"TransactItems": [{"Put": {"TableName": TABLE, "Item": {"pk": S("b")}}}],
                              "ClientRequestToken": "token-1", "ReturnConsumedCapacity": "TOTAL"})]),
    ("writes", "index-entry-size",
     "P4 open: an index entry is sized without the 100 bytes of overhead. The item is 1000 bytes, so with 100 bytes more its index write would cost 2 units.",
     [table(global_index="ALL"),
      ("PutItem", {"TableName": TABLE, "Item": {"pk": S("a"), "g": S("g"), "v": S("${x*994}")}, "ReturnConsumedCapacity": "INDEXES"})]),
    ("streams", "disable-a-stream-that-is-off",
     "P6 open: disabling the stream of a table that has none.",
     [table(), message(stream_spec(False))]),
    ("streams", "disabled-with-a-view-type",
     "P6 open: StreamEnabled false with a StreamViewType, in CreateTable and in UpdateTable.",
     [message(stream_table(view="NEW_IMAGE", enabled=False)), stream_table(), message(stream_spec(False, "NEW_IMAGE"))]),
    ("streams", "iterator-with-a-sequence-number",
     "P6 open: GetShardIterator with a SequenceNumber for TRIM_HORIZON and LATEST. (AFTER_SEQUENCE_NUMBER past the last record is an InternalFailure of DynamoDB Local, so it decides nothing.)",
     [stream_table(), selecting(DESCRIBE_STREAM, select=["StreamDescription.StreamStatus"]),
      message(iterator("TRIM_HORIZON", "1")), message(iterator("LATEST", "1"))]),
    ("streams", "list-streams-of-what-does-not-exist",
     "P6 open: ListStreams with the name of a table that never existed, and with an ExclusiveStartStreamArn nobody has.",
     [table(), selecting(("ListStreams", {"TableName": "never-existed"}), select=["Streams"]), selecting(("ListStreams", {"ExclusiveStartStreamArn": UNKNOWN_STREAM}), select=["Streams"])]),
    ("streams", "describe-table-after-disabling",
     "P6 open: what DescribeTable reports of a stream that was disabled.",
     [stream_table(), stream_spec(False),
      selecting(("DescribeTable", {"TableName": TABLE}), select=["Table.StreamSpecification"], present=["Table.LatestStreamArn", "Table.LatestStreamLabel"])]),
]


def expand(value, variables=None):
    """Replaces every "${c*n}" string in a request by n times c, and "${streamArn}" or "${shardId}" by what an earlier answer of the case held."""
    if isinstance(value, dict):
        return {key: expand(member, variables) for key, member in value.items()}
    if isinstance(value, list):
        return [expand(element, variables) for element in value]
    if isinstance(value, str):
        match = REPEAT.match(value)
        if match:
            return match.group(1) * int(match.group(2))
        match = VARIABLE.match(value)
        return (variables or {})[match.group(1)] if match else value
    return value


def capture(body, variables):
    """Keeps the stream ARN and the shard id an answer names, for the requests after it; the replay does the same with the answers of the fake."""
    for holder in ("TableDescription", "Table"):
        if isinstance(body.get(holder), dict) and "LatestStreamArn" in body[holder]:
            variables["streamArn"] = body[holder]["LatestStreamArn"]
    shards = body.get("StreamDescription", {}).get("Shards") or []
    if shards:
        variables["shardId"] = shards[0]["ShardId"]


def at(body, path):
    """Input: {"Table":{"StreamSpecification":{...}}}, "Table.StreamSpecification" -> the value there, or None."""
    for name in path.split("."):
        body = body.get(name) if isinstance(body, dict) else None
    return body


def send(endpoint, operation, request, variables=None):
    body = json.dumps(expand(request, variables)).encode("utf-8")
    prefix = "DynamoDBStreams_20120810." if operation in STREAM_OPERATIONS else "DynamoDB_20120810."
    headers = {"Content-Type": "application/x-amz-json-1.0", "X-Amz-Target": prefix + operation}
    signed = botocore.awsrequest.AWSRequest(method="POST", url=endpoint, data=body, headers=headers)
    botocore.auth.SigV4Auth(botocore.credentials.Credentials("test", "test"), "dynamodb", REGION).add_auth(signed)
    http = urllib.request.Request(endpoint, data=body, headers=dict(signed.headers.items()), method="POST")
    try:
        with urllib.request.urlopen(http) as response:
            return response.status, json.loads(response.read())
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read())


def record(endpoint, steps):
    recorded = []
    variables = {}
    for step in steps:
        operation, request = step[0], step[1]
        options = step[2] if len(step) > 2 else {}
        status, body = send(endpoint, operation, request, variables)
        capture(body, variables)
        entry = {"operation": operation, "request": request, "status": status}
        if status == 200:
            if "select" in options or "present" in options:
                entry["select"] = {path: at(body, path) for path in options.get("select", [])}
                entry["present"] = {path: at(body, path) is not None for path in options.get("present", [])}
            elif operation not in ("CreateTable", "DeleteTable", "UpdateTable", "DescribeTable") and operation not in STREAM_OPERATIONS:
                entry["response"] = body
        else:
            entry["code"] = body["__type"].rsplit("#", 1)[-1]
            entry["message"] = body.get("message", body.get("Message"))
            if "CancellationReasons" in body:
                entry["cancellationReasons"] = body["CancellationReasons"]
            if options.get("compareMessage"):
                entry["compareMessage"] = True
        recorded.append(entry)
    send(endpoint, "DeleteTable", {"TableName": TABLE})
    return recorded


def image():
    for line in DOCKERFILE.read_text().splitlines():
        words = line.split()
        if len(words) >= 2 and words[0].upper() == "FROM":
            return words[1]
    raise SystemExit(f"{DOCKERFILE} has no FROM line")


def start_container():
    container = subprocess.check_output(
        ["docker", "run", "-d", "--rm", "-p", "127.0.0.1::8000", image(), "-jar", "DynamoDBLocal.jar", "-inMemory", "-sharedDb"], text=True).strip()
    port = subprocess.check_output(["docker", "port", container, "8000/tcp"], text=True).strip().splitlines()[0].rsplit(":", 1)[1]
    endpoint = f"http://127.0.0.1:{port}"
    for _ in range(200):
        try:
            if send(endpoint, "ListTables", {})[0] == 200:
                return container, endpoint
        except (urllib.error.URLError, ConnectionError, json.JSONDecodeError):
            pass
        time.sleep(0.1)
    subprocess.run(["docker", "rm", "-f", container], check=False)
    raise SystemExit("DynamoDB Local did not answer ListTables")


def render(endpoint, source):
    files = {}
    for file, name, decides, steps in CASES:
        files.setdefault(file, []).append({"name": name, "decides": decides, "steps": record(endpoint, steps)})
    return {f"{file}.json": json.dumps({"source": source, "cases": cases}, indent=2, ensure_ascii=True) + "\n" for file, cases in files.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="exit 1 if a committed file differs")
    parser.add_argument("--endpoint", help="a DynamoDB Local that runs already, instead of a container of the pinned image")
    arguments = parser.parse_args()

    container = None
    endpoint = arguments.endpoint
    if endpoint is None:
        container, endpoint = start_container()
    try:
        rendered = render(endpoint, f"DynamoDB Local, {image()}, recorded by build/generate_fake_vectors.py")
    finally:
        if container:
            subprocess.run(["docker", "rm", "-f", container], check=False, stdout=subprocess.DEVNULL)

    OUTPUT.mkdir(parents=True, exist_ok=True)
    stale = []
    for name, text in rendered.items():
        path = OUTPUT / name
        if arguments.check:
            if not path.exists() or path.read_text(encoding="utf-8").replace("\r\n", "\n") != text:
                stale.append(name)
        else:
            path.write_text(text, encoding="utf-8", newline="\n")
    if stale:
        print("stale: " + ", ".join(stale), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
