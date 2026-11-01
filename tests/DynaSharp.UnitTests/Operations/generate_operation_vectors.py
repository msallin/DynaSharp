"""Generates operation_vectors.json, the wire-format test data of the DynamoDB and DynamoDB Streams operations, with botocore.

The expected JSON must not come from the C# implementation, so it is produced here by the reference implementation of AWS:

* Request vectors: the parameters of a request go through botocore's own serializer
  (botocore.serialize.create_serializer('json') with the operation model of the dynamodb service, or of the dynamodbstreams
  service for ListStreams, DescribeStream, GetShardIterator and GetRecords) and the exact
  request body and X-Amz-Target are stored. The tests build the same request with the C# model and compare the JSON
  semantically (key order may differ).
* Command request vectors: the same, for the commands of the table API. A command writes placeholders of its own, so these
  hold the expressions a person would write, and the tests resolve the placeholders on both sides before they compare.
* Response vectors: a canned response body (real samples of the AWS API reference, and crafted pages for the edge cases)
  goes through botocore's own parser (botocore.parsers.create_parser('json')) and the parsed result is stored. The tests
  feed the canned body to the C# operation and compare what it returned with what botocore returned. Binary values are
  shown base64-encoded in both.
* Error vectors: the body of a failed response goes through botocore's parser twice: for the error code and message, as
  every botocore client reads an error, and with the shape of the modeled exception that the code names, which is where
  botocore reads members such as the CancellationReasons of a TransactionCanceledException. The tests feed the body to the
  error mapper of the C# library and compare the exception it made with both results.

The AWS API reference has worked examples for BatchGetItem and BatchWriteItem only; TransactGetItems and TransactWriteItems have
none. Their request and response vectors are crafted from the documented syntax, and their error vectors come from a real response
and from the cancellation reason codes and messages that the reference lists. The "source" field of a vector says where it comes from;
it is empty for vectors made up to cover an edge case.

Where the request of the API reference sends a value that is the default in the C# model (ReturnValues NONE), the vector
leaves it out, because the C# model does not send defaults.

Setup (outside the repository):
    python3 -m venv /tmp/botocore-venv
    /tmp/botocore-venv/bin/pip install botocore

Run (from any directory):
    /tmp/botocore-venv/bin/python generate_operation_vectors.py           # rewrites operation_vectors.json
    /tmp/botocore-venv/bin/python generate_operation_vectors.py --check   # exits 1 if the committed file is stale

The inputs of every vector are defined here only. Non-ASCII text is built from code points and written as \\uXXXX escapes
(ensure_ascii), so editors and tools cannot change what the vectors contain.
"""

import argparse
import base64
import datetime
import json
import pathlib
import sys

import botocore
import botocore.parsers
import botocore.serialize
import botocore.session

OUTPUT = pathlib.Path(__file__).with_name("operation_vectors.json")
CONTENT_TYPE = "application/x-amz-json-1.0"

# Characters that tools like to normalize, written as code points.
E_ACUTE = chr(0x00E9)
GRIN = chr(0x1F600)  # outside the BMP: a surrogate pair in JSON
NAIVE_NAME = "na" + chr(0x00EF) + "ve"


def S(value):
    return {"S": value}


def N(value):
    return {"N": str(value)}


def B(value):
    return {"B": value}


def BOOL(value):
    return {"BOOL": value}


NULL = {"NULL": True}

AWS_REFERENCE = "AWS API reference, {operation}, example '{example}'"


STREAMS_OPERATIONS = {"ListStreams", "DescribeStream", "GetShardIterator", "GetRecords"}
STREAMS_REFERENCE = "DynamoDB Streams API reference, {operation}, example '{example}'"


def service_of(operation):
    """The botocore service whose model defines the operation."""
    return "dynamodbstreams" if operation in STREAMS_OPERATIONS else "dynamodb"


def request(operation, name, params, source=None):
    return {"operation": operation, "name": name, "source": source, "params": params}


def response(operation, name, wire, source=None):
    """wire is the canned body as JSON text, or a Python value that is serialized compactly."""
    if not isinstance(wire, str):
        wire = json.dumps(wire, separators=(",", ":"))
    return {"operation": operation, "name": name, "source": source, "wire": wire}


# ---------------------------------------------------------------------------------------------------------------------
# Requests
# ---------------------------------------------------------------------------------------------------------------------

THREAD_KEY = {"ForumName": S("Amazon DynamoDB"), "Subject": S("Maximum number of items?")}

ALL_VALUE_TYPES = {
    ":s": S("text"),
    ":n": N("-12.5E+3"),
    ":b": B(bytes([0, 1, 2, 253, 254, 255])),
    ":t": BOOL(True),
    ":f": BOOL(False),
    ":null": NULL,
    ":ss": {"SS": ["a", "b"]},
    ":ns": {"NS": ["1", "2.5"]},
    ":bs": {"BS": [b"\x00\x01", b"\xff"]},
    ":l": {"L": [S("x"), N("1"), {"L": []}, {"M": {}}, BOOL(False)]},
    ":m": {"M": {"inner": {"M": {"deep": {"L": [N("1"), S("two")]}}}, "empty": S("")}},
}

SPECIAL_TEXT = 'quote " backslash \\ slash / tab \t newline \n control ' + chr(1) + " " + E_ACUTE + " " + GRIN + " </script>"

ALL_OPTIONS_QUERY = {
    "TableName": "Orders",
    "IndexName": "ByStatus",
    "Select": "SPECIFIC_ATTRIBUTES",
    "Limit": 25,
    "ConsistentRead": True,
    "ScanIndexForward": False,
    "ExclusiveStartKey": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#9"), "status": S("OPEN")},
    "ReturnConsumedCapacity": "INDEXES",
    "ProjectionExpression": "#s, total, address.city",
    "FilterExpression": "total > :min AND #s <> :cancelled",
    "KeyConditionExpression": "#s = :status AND begins_with(sk, :prefix)",
    "ExpressionAttributeNames": {"#s": "status"},
    "ExpressionAttributeValues": {
        ":status": S("OPEN"),
        ":prefix": S("ORDER#"),
        ":min": N("100"),
        ":cancelled": S("CANCELLED"),
    },
}

ALL_OPTIONS_SCAN = {
    "TableName": "Orders",
    "IndexName": "ByStatus",
    "Select": "ALL_PROJECTED_ATTRIBUTES",
    "Limit": 500,
    "ConsistentRead": True,
    "ExclusiveStartKey": {"pk": S("CUSTOMER#7"), "sk": S("ORDER#2")},
    "TotalSegments": 8,
    "Segment": 5,
    "ReturnConsumedCapacity": "TOTAL",
    "ProjectionExpression": "#s, total",
    "FilterExpression": "total > :min",
    "ExpressionAttributeNames": {"#s": "status"},
    "ExpressionAttributeValues": {":min": N("100")},
}

SELECT_VALUES = {
    "AllAttributes": "ALL_ATTRIBUTES",
    "AllProjectedAttributes": "ALL_PROJECTED_ATTRIBUTES",
    "SpecificAttributes": "SPECIFIC_ATTRIBUTES",
    "Count": "COUNT",
}

RETURN_VALUES = {
    "AllOld": "ALL_OLD",
    "UpdatedOld": "UPDATED_OLD",
    "AllNew": "ALL_NEW",
    "UpdatedNew": "UPDATED_NEW",
}

# ---------------------------------------------------------------------------------------------------------------------
# Requests of the command API: each is the request a person writes for what a command means, with the expressions in their
# plain form. CommandVectorTests builds the command and compares by meaning, with every placeholder resolved on both sides.
# ---------------------------------------------------------------------------------------------------------------------

SHOP_KEY = {"pk": S("CUSTOMER#alice"), "sk": S("PROFILE")}

COMMAND_REQUESTS = [
    request("GetItem", "Key", {"TableName": "Shop", "Key": SHOP_KEY}),
    request("GetItem", "NumberKey", {"TableName": "Shop", "Key": {"pk": N("7"), "sk": N("2.5")}}),
    request(
        "GetItem",
        "ConsistentProjection",
        {
            "TableName": "Shop",
            "Key": SHOP_KEY,
            "ConsistentRead": True,
            "ProjectionExpression": "fullName, address.city, lines[0].sku",
            "ReturnConsumedCapacity": "TOTAL",
        },
    ),
    request(
        "PutItem",
        "AllValueTypes",
        {
            "TableName": "Shop",
            "Item": {
                "pk": S("a"),
                "n": N("-12.5E+3"),
                "b": B(bytes([0, 1, 2, 253, 254, 255])),
                "t": BOOL(True),
                "z": NULL,
                "ss": {"SS": ["a", "b"]},
                "l": {"L": [S("x"), N("1")]},
                "m": {"M": {"inner": S("y")}},
            },
        },
    ),
    request(
        "PutItem",
        "IfNotExists",
        {
            "TableName": "Shop",
            "Item": {"pk": S("a"), "name": S("Alice")},
            "ConditionExpression": "attribute_not_exists(pk)",
            "ReturnValues": "ALL_OLD",
            "ReturnValuesOnConditionCheckFailure": "ALL_OLD",
            "ReturnConsumedCapacity": "INDEXES",
            "ReturnItemCollectionMetrics": "SIZE",
        },
    ),
    request(
        "PutItem",
        "SpecialCharacters",
        {
            "TableName": "Shop",
            "Item": {"pk": S(SPECIAL_TEXT), NAIVE_NAME: S(SPECIAL_TEXT)},
            "ConditionExpression": "#n <> :v",
            "ExpressionAttributeNames": {"#n": SPECIAL_TEXT},
            "ExpressionAttributeValues": {":v": S(SPECIAL_TEXT)},
        },
    ),
    request(
        "UpdateItem",
        "ReservedWords",
        {
            "TableName": "Shop",
            "Key": SHOP_KEY,
            "UpdateExpression": "SET #status = :s REMOVE #order ADD #size :n",
            "ExpressionAttributeNames": {"#status": "status", "#order": "order", "#size": "size"},
            "ExpressionAttributeValues": {":s": S("open"), ":n": N("1")},
        },
    ),
    request(
        "UpdateItem",
        "NestedPaths",
        {
            "TableName": "Shop",
            "Key": SHOP_KEY,
            "UpdateExpression": "SET address.city = :c, lines[2].sku = :s REMOVE matrix[0][1]",
            "ExpressionAttributeValues": {":c": S("Bern"), ":s": S("x-1")},
        },
    ),
    request(
        "UpdateItem",
        "SetIfNotExists",
        {
            "TableName": "Shop",
            "Key": SHOP_KEY,
            "UpdateExpression": "SET visits = if_not_exists(visits, :zero)",
            "ExpressionAttributeValues": {":zero": N("0")},
        },
    ),
    request(
        "UpdateItem",
        "ListAppend",
        {
            "TableName": "Shop",
            "Key": SHOP_KEY,
            "UpdateExpression": "SET tags = list_append(tags, :more)",
            "ConditionExpression": "attribute_exists(pk) AND size(tags) < :limit",
            "ExpressionAttributeValues": {":more": {"L": [S("x")]}, ":limit": N("10")},
        },
    ),
    request(
        "UpdateItem",
        "Conditions",
        {
            "TableName": "Shop",
            "Key": SHOP_KEY,
            "UpdateExpression": "SET a = :one",
            "ConditionExpression": "((v BETWEEN :one AND :two AND begins_with(s, :p)) OR NOT (n <> :one)) AND (attribute_not_exists(gone))",
            "ExpressionAttributeValues": {":one": N("1"), ":two": N("2"), ":p": S("x")},
        },
    ),
    request(
        "DeleteItem",
        "IfExists",
        {
            "TableName": "Shop",
            "Key": SHOP_KEY,
            "ConditionExpression": "attribute_exists(pk)",
            "ReturnValuesOnConditionCheckFailure": "ALL_OLD",
        },
    ),
    request(
        "Query",
        "Partition",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice")},
        },
    ),
    request(
        "Query",
        "SortKeyEquals",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk AND sk = :sk",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":sk": S("PROFILE")},
        },
    ),
    request(
        "Query",
        "SortKeyBelow",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk AND sk < :sk",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":sk": S("ORDER#5")},
        },
    ),
    request(
        "Query",
        "SortKeyUpTo",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk AND sk <= :sk",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":sk": S("ORDER#5")},
        },
    ),
    request(
        "Query",
        "SortKeyAbove",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk AND sk > :sk",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":sk": N("5")},
        },
    ),
    request(
        "Query",
        "SortKeyFrom",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk AND sk >= :sk",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":sk": S("ORDER#5")},
        },
    ),
    request(
        "Query",
        "SortKeyBetween",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk AND sk BETWEEN :low AND :high",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":low": S("ORDER#1"), ":high": S("ORDER#9")},
        },
    ),
    request(
        "Query",
        "SortKeyBeginsWith",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk AND begins_with(sk, :prefix)",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":prefix": S("ORDER#")},
        },
    ),
    request(
        "Query",
        "ReservedWords",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk",
            "FilterExpression": "#status = :s AND #size > :n",
            "ProjectionExpression": "#status, #order, address.city",
            "ExpressionAttributeNames": {"#status": "status", "#size": "size", "#order": "order"},
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":s": S("open"), ":n": N("1")},
        },
    ),
    request(
        "Query",
        "TwoFilters",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk",
            "FilterExpression": "(a = :one) AND (size(tags) < :ten)",
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":one": N("1"), ":ten": N("10")},
        },
    ),
    request(
        "Query",
        "Entity",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk AND begins_with(sk, :prefix)",
            "FilterExpression": "(#t = :type) AND (fullName = :name)",
            "ExpressionAttributeNames": {"#t": "_type"},
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":prefix": S("P"), ":type": S("CUSTOMER"), ":name": S("Alice")},
        },
    ),
    request(
        "Query",
        "EntityProjection",
        {
            "TableName": "Shop",
            "KeyConditionExpression": "pk = :pk",
            "FilterExpression": "#t = :type",
            "ProjectionExpression": "fullName, #t, pk, sk",
            "ExpressionAttributeNames": {"#t": "_type"},
            "ExpressionAttributeValues": {":pk": S("CUSTOMER#alice"), ":type": S("CUSTOMER")},
        },
    ),
    request(
        "Query",
        "EntityIndexProjection",
        {
            "TableName": "Shop",
            "IndexName": "ByName",
            "KeyConditionExpression": "fullName = :name",
            "FilterExpression": "#t = :type",
            "ProjectionExpression": "email, #t, pk, sk, fullName",
            "ExpressionAttributeNames": {"#t": "_type"},
            "ExpressionAttributeValues": {":name": S("Alice"), ":type": S("CUSTOMER")},
        },
    ),
    request(
        "Scan",
        "Entity",
        {
            "TableName": "Shop",
            "FilterExpression": "(#t = :type) AND (orderCount > :none)",
            "ExpressionAttributeNames": {"#t": "_type"},
            "ExpressionAttributeValues": {":type": S("CUSTOMER"), ":none": N("0")},
        },
    ),
    request(
        "Scan",
        "Interpolated",
        {
            "TableName": "Shop",
            "FilterExpression": "begins_with(sk, :prefix) AND amount > :min",
            "ProjectionExpression": "sk, amount",
            "ExpressionAttributeValues": {":prefix": S("ORDER#"), ":min": N("100")},
        },
    ),
]

REQUESTS = [
    # PutItem: only the member that this package adds.
    request(
        "PutItem",
        "ReturnItemCollectionMetrics",
        {
            "TableName": "Orders",
            "Item": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#1")},
            "ReturnItemCollectionMetrics": "SIZE",
        },
    ),
    # UpdateItem
    request("UpdateItem", "Minimal", {"TableName": "Users", "Key": {"pk": S("user-1")}}),
    request(
        "UpdateItem",
        "ConditionalUpdateFromApiReference",
        {
            "TableName": "Thread",
            "Key": THREAD_KEY,
            "UpdateExpression": "set LastPostedBy = :val1",
            "ConditionExpression": "LastPostedBy = :val2",
            "ExpressionAttributeValues": {":val1": S("alice@example.com"), ":val2": S("fred@example.com")},
            "ReturnValues": "ALL_NEW",
        },
        AWS_REFERENCE.format(operation="UpdateItem", example="Conditional Update"),
    ),
    request(
        "UpdateItem",
        "AtomicCounterFromApiReference",
        {
            "TableName": "Thread",
            "Key": {"ForumName": S("Amazon DynamoDB"), "Subject": S("A question about updates")},
            "UpdateExpression": "set Replies = Replies + :num",
            "ExpressionAttributeValues": {":num": N("1")},
        },
        AWS_REFERENCE.format(operation="UpdateItem", example="Atomic Counter") + " (ReturnValues NONE left out)",
    ),
    request(
        "UpdateItem",
        "AllOptions",
        {
            "TableName": "Orders",
            "Key": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#1")},
            "UpdateExpression": "SET #s = :new, visits = visits + :one REMOVE tempFlag ADD tags :tags DELETE oldTags :old",
            "ConditionExpression": "#s = :expected AND version = :version",
            "ExpressionAttributeNames": {"#s": "status"},
            "ExpressionAttributeValues": {
                ":new": S("SHIPPED"),
                ":one": N("1"),
                ":tags": {"SS": ["fragile"]},
                ":old": {"SS": ["draft"]},
                ":expected": S("OPEN"),
                ":version": N("7"),
            },
            "ReturnValues": "UPDATED_NEW",
            "ReturnValuesOnConditionCheckFailure": "ALL_OLD",
            "ReturnConsumedCapacity": "INDEXES",
            "ReturnItemCollectionMetrics": "SIZE",
        },
    ),
    request(
        "UpdateItem",
        "AllAttributeValueTypes",
        {
            "TableName": "Users",
            "Key": {"pk": S("user-1")},
            "UpdateExpression": "SET a = :s",
            "ExpressionAttributeValues": ALL_VALUE_TYPES,
        },
    ),
    request(
        "UpdateItem",
        "SpecialCharacters",
        {
            "TableName": "Users",
            "Key": {SPECIAL_TEXT: S(SPECIAL_TEXT), NAIVE_NAME: S("")},
            "UpdateExpression": "SET #n = :v",
            "ExpressionAttributeNames": {"#n": SPECIAL_TEXT},
            "ExpressionAttributeValues": {":v": S(SPECIAL_TEXT)},
        },
    ),
]

for _name, _wire in RETURN_VALUES.items():
    REQUESTS.append(
        request(
            "UpdateItem",
            "ReturnValues" + _name,
            {"TableName": "Users", "Key": {"pk": S("user-1")}, "UpdateExpression": "SET a = :v",
             "ExpressionAttributeValues": {":v": N("1")}, "ReturnValues": _wire},
        )
    )

REQUESTS += [
    # DeleteItem
    request("DeleteItem", "Minimal", {"TableName": "Users", "Key": {"pk": S("user-1")}}),
    request(
        "DeleteItem",
        "FromApiReference",
        {
            "TableName": "Thread",
            "Key": {"ForumName": S("Amazon DynamoDB"), "Subject": S("How do I update multiple items?")},
            "ConditionExpression": "attribute_not_exists(Replies)",
            "ReturnValues": "ALL_OLD",
        },
        AWS_REFERENCE.format(operation="DeleteItem", example="Delete an Item"),
    ),
    request(
        "DeleteItem",
        "AllOptions",
        {
            "TableName": "Orders",
            "Key": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#1")},
            "ConditionExpression": "#s = :expected AND version = :version",
            "ExpressionAttributeNames": {"#s": "status"},
            "ExpressionAttributeValues": {":expected": S("CANCELLED"), ":version": N("7")},
            "ReturnValues": "ALL_OLD",
            "ReturnValuesOnConditionCheckFailure": "ALL_OLD",
            "ReturnConsumedCapacity": "TOTAL",
            "ReturnItemCollectionMetrics": "SIZE",
        },
    ),
    request(
        "DeleteItem",
        "ConsumedCapacityIndexes",
        {"TableName": "Users", "Key": {"pk": S("user-1")}, "ReturnConsumedCapacity": "INDEXES"},
    ),
    # Query
    request(
        "Query",
        "Minimal",
        {
            "TableName": "Users",
            "KeyConditionExpression": "pk = :pk",
            "ExpressionAttributeValues": {":pk": S("user-1")},
        },
    ),
    request(
        "Query",
        "RangeOfItemsFromApiReference",
        {
            "TableName": "Reply",
            "IndexName": "PostedBy-Index",
            "Limit": 3,
            "ConsistentRead": True,
            "ProjectionExpression": "Id, PostedBy, ReplyDateTime",
            "KeyConditionExpression": "Id = :v1 AND PostedBy BETWEEN :v2a AND :v2b",
            "ExpressionAttributeValues": {
                ":v1": S("Amazon DynamoDB#DynamoDB Thread 1"),
                ":v2a": S("User A"),
                ":v2b": S("User C"),
            },
            "ReturnConsumedCapacity": "TOTAL",
        },
        AWS_REFERENCE.format(operation="Query", example="Retrieve a Range of Items"),
    ),
    request(
        "Query",
        "CountItemsFromApiReference",
        {
            "TableName": "Thread",
            "ConsistentRead": True,
            "KeyConditionExpression": "ForumName = :val",
            "ExpressionAttributeValues": {":val": S("Amazon DynamoDB")},
        },
        AWS_REFERENCE.format(operation="Query", example="Count Items"),
    ),
    request("Query", "AllOptions", ALL_OPTIONS_QUERY),
    request(
        "Query",
        "ScanIndexForwardTrue",
        {"TableName": "Users", "KeyConditionExpression": "pk = :pk", "ScanIndexForward": True,
         "ExpressionAttributeValues": {":pk": S("user-1")}},
    ),
    request(
        "Query",
        "ScanIndexForwardFalseWithLimitOne",
        {"TableName": "Users", "KeyConditionExpression": "pk = :pk", "ScanIndexForward": False, "Limit": 1,
         "ExpressionAttributeValues": {":pk": S("user-1")}},
    ),
    request(
        "Query",
        "LimitIntMax",
        {"TableName": "Users", "KeyConditionExpression": "pk = :pk", "Limit": 2147483647,
         "ExpressionAttributeValues": {":pk": S("user-1")}},
    ),
    request(
        "Query",
        "ExclusiveStartKeyWithBinaryAndNumber",
        {
            "TableName": "Blobs",
            "KeyConditionExpression": "pk = :pk",
            "ExpressionAttributeValues": {":pk": B(b"\x00\xffkey")},
            "ExclusiveStartKey": {"pk": B(b"\x00\xffkey"), "sk": N("-42.5")},
        },
    ),
    request(
        "Query",
        "EmptyExclusiveStartKey",
        {"TableName": "Users", "KeyConditionExpression": "pk = :pk", "ExclusiveStartKey": {},
         "ExpressionAttributeValues": {":pk": S("user-1")}},
    ),
    request(
        "Query",
        "SpecialCharacters",
        {
            "TableName": "Users",
            "KeyConditionExpression": "#n = :v",
            "ExpressionAttributeNames": {"#n": SPECIAL_TEXT},
            "ExpressionAttributeValues": {":v": S(SPECIAL_TEXT)},
            "FilterExpression": "contains(#n, :v)",
        },
    ),
    # Scan
    request("Scan", "Minimal", {"TableName": "Users"}),
    request(
        "Scan",
        "ReturnAllItemsFromApiReference",
        {"TableName": "Reply", "ReturnConsumedCapacity": "TOTAL"},
        AWS_REFERENCE.format(operation="Scan", example="Return All Items"),
    ),
    request(
        "Scan",
        "FilterExpressionFromApiReference",
        {
            "TableName": "Reply",
            "FilterExpression": "PostedBy = :val",
            "ExpressionAttributeValues": {":val": S("joe@example.com")},
            "ReturnConsumedCapacity": "TOTAL",
        },
        AWS_REFERENCE.format(operation="Scan", example="Use a Filter Expression"),
    ),
    request("Scan", "AllOptions", ALL_OPTIONS_SCAN),
    request(
        "Scan",
        "ParallelSegment",
        {"TableName": "Users", "Segment": 3, "TotalSegments": 4},
    ),
    request(
        "Scan",
        "ParallelSegmentWithStartKey",
        {"TableName": "Users", "Segment": 3, "TotalSegments": 4, "Limit": 100,
         "ExclusiveStartKey": {"pk": S("user-77")}},
    ),
    # Segment 0 is a value, not "unset".
    request("Scan", "SegmentZeroOfOne", {"TableName": "Users", "Segment": 0, "TotalSegments": 1}),
    request("Scan", "SegmentBounds", {"TableName": "Users", "Segment": 999999, "TotalSegments": 1000000}),
    request("Scan", "LimitIntMax", {"TableName": "Users", "Limit": 2147483647}),
    request("Scan", "SecondaryIndex", {"TableName": "Orders", "IndexName": "ByStatus", "ConsistentRead": True}),
]

for _name, _wire in SELECT_VALUES.items():
    REQUESTS.append(
        request(
            "Query",
            "Select" + _name,
            {"TableName": "Users", "KeyConditionExpression": "pk = :pk", "Select": _wire,
             "ExpressionAttributeValues": {":pk": S("user-1")}},
        )
    )
    REQUESTS.append(request("Scan", "Select" + _name, {"TableName": "Users", "Select": _wire}))

# Batches and transactions. Left out where the C# model sends no defaults: ConsistentRead false, the NONE enum values.
TOKEN = "3d2f7c8e-5a41-4b6e-9a0f-2c1d8e7b6a54"  # 36 characters, the longest token DynamoDB accepts
ARN = "arn:aws:dynamodb:eu-central-1:123456789012:table/Users"


def forum_put(name):
    return {"PutRequest": {"Item": {"Name": S(name), "Category": S("Amazon Web Services")}}}


def numbered_key(index):
    return {"pk": S("USER#" + str(index)), "sk": N(index)}


ORDER_PUT = {
    "TableName": "Orders",
    "Item": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#1"), "total": N("12.5")},
    "ConditionExpression": "attribute_not_exists(pk)",
    "ExpressionAttributeNames": {"#t": "total"},
    "ExpressionAttributeValues": {":min": N("0")},
    "ReturnValuesOnConditionCheckFailure": "ALL_OLD",
}

CUSTOMER_UPDATE = {
    "TableName": "Customers",
    "Key": {"pk": S("CUSTOMER#1")},
    "UpdateExpression": "SET orders = orders + :one, #s = :active",
    "ConditionExpression": "attribute_exists(pk) AND version = :version",
    "ExpressionAttributeNames": {"#s": "status"},
    "ExpressionAttributeValues": {":one": N("1"), ":active": S("ACTIVE"), ":version": N("4")},
    "ReturnValuesOnConditionCheckFailure": "ALL_OLD",
}

COUPON_DELETE = {
    "TableName": "Coupons",
    "Key": {"code": S("WELCOME10")},
    "ConditionExpression": "#u = :unused",
    "ExpressionAttributeNames": {"#u": "used"},
    "ExpressionAttributeValues": {":unused": BOOL(False)},
    "ReturnValuesOnConditionCheckFailure": "ALL_OLD",
}

STOCK_CHECK = {
    "TableName": "Stock",
    "Key": {"sku": S("SKU-1"), "warehouse": N("7")},
    "ConditionExpression": "quantity >= :needed",
    "ExpressionAttributeValues": {":needed": N("2")},
    "ReturnValuesOnConditionCheckFailure": "ALL_OLD",
}

REQUESTS += [
    # BatchGetItem
    request(
        "BatchGetItem",
        "RetrieveItemsFromMultipleTablesFromApiReference",
        {
            "RequestItems": {
                "Forum": {
                    "Keys": [{"Name": S("Amazon DynamoDB")}, {"Name": S("Amazon RDS")}, {"Name": S("Amazon Redshift")}],
                    "ProjectionExpression": "Name, Threads, Messages, Views",
                },
                "Thread": {
                    "Keys": [{"ForumName": S("Amazon DynamoDB"), "Subject": S("Concurrent reads")}],
                    "ProjectionExpression": "Tags, Message",
                },
            },
            "ReturnConsumedCapacity": "TOTAL",
        },
        AWS_REFERENCE.format(operation="BatchGetItem", example="Retrieve Items from Multiple Tables"),
    ),
    request("BatchGetItem", "Minimal", {"RequestItems": {"Users": {"Keys": [{"pk": S("user-1")}]}}}),
    request(
        "BatchGetItem",
        "AllOptions",
        {
            "RequestItems": {
                "Orders": {
                    "Keys": [{"pk": S("CUSTOMER#1"), "sk": S("ORDER#1")}, {"pk": S("CUSTOMER#1"), "sk": S("ORDER#2")}],
                    "ConsistentRead": True,
                    "ProjectionExpression": "#s, total, address.city",
                    "ExpressionAttributeNames": {"#s": "status"},
                },
                "Customers": {"Keys": [{"pk": S("CUSTOMER#1")}]},
            },
            "ReturnConsumedCapacity": "INDEXES",
        },
    ),
    request(
        "BatchGetItem",
        "BinaryAndNumberKeys",
        {"RequestItems": {"Blobs": {"Keys": [{"pk": B(b"\x00\xffkey"), "sk": N("-42.5")}, {"pk": B(b"\x01"), "sk": N("1E+3")}]}}},
    ),
    request(
        "BatchGetItem",
        "TableArnAsKey",
        {"RequestItems": {ARN: {"Keys": [{"pk": S("user-1")}]}}, "ReturnConsumedCapacity": "TOTAL"},
    ),
    request(
        "BatchGetItem",
        "HundredKeys",
        {"RequestItems": {"Users": {"Keys": [numbered_key(i) for i in range(100)]}}},
    ),
    request(
        "BatchGetItem",
        "SpecialCharacters",
        {
            "RequestItems": {
                SPECIAL_TEXT: {
                    "Keys": [{SPECIAL_TEXT: S(SPECIAL_TEXT)}],
                    "ProjectionExpression": "#n",
                    "ExpressionAttributeNames": {"#n": SPECIAL_TEXT},
                }
            }
        },
    ),
    # BatchWriteItem
    request(
        "BatchWriteItem",
        "MultipleOperationsFromApiReference",
        {
            "RequestItems": {
                "Forum": [
                    forum_put("Amazon DynamoDB"),
                    forum_put("Amazon RDS"),
                    forum_put("Amazon Redshift"),
                    forum_put("Amazon ElastiCache"),
                ]
            },
            "ReturnConsumedCapacity": "TOTAL",
        },
        AWS_REFERENCE.format(operation="BatchWriteItem", example="Multiple Operations on One Table"),
    ),
    request(
        "BatchWriteItem",
        "Minimal",
        {"RequestItems": {"Users": [{"PutRequest": {"Item": {"pk": S("user-1")}}}]}},
    ),
    request(
        "BatchWriteItem",
        "PutAndDeleteInOneTable",
        {
            "RequestItems": {
                "Users": [
                    {"PutRequest": {"Item": {"pk": S("user-1"), "name": S("Ada")}}},
                    {"DeleteRequest": {"Key": {"pk": S("user-2")}}},
                    {"DeleteRequest": {"Key": {"pk": S("user-3")}}},
                ]
            }
        },
    ),
    request(
        "BatchWriteItem",
        "MultipleTablesAllOptions",
        {
            "RequestItems": {
                "Orders": [
                    {"PutRequest": {"Item": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#1")}}},
                    {"DeleteRequest": {"Key": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#0")}}},
                ],
                "Customers": [{"PutRequest": {"Item": {"pk": S("CUSTOMER#1")}}}],
            },
            "ReturnConsumedCapacity": "INDEXES",
            "ReturnItemCollectionMetrics": "SIZE",
        },
    ),
    request(
        "BatchWriteItem",
        "AllAttributeValueTypes",
        {"RequestItems": {"Users": [{"PutRequest": {"Item": dict(ALL_VALUE_TYPES, pk=S("user-1"))}}]}},
    ),
    request(
        "BatchWriteItem",
        "TwentyFivePuts",
        {"RequestItems": {"Users": [{"PutRequest": {"Item": numbered_key(i)}} for i in range(25)]}},
    ),
    request(
        "BatchWriteItem",
        "TableArnAsKey",
        {"RequestItems": {ARN: [{"DeleteRequest": {"Key": {"pk": S("user-1")}}}]}},
    ),
    request(
        "BatchWriteItem",
        "SpecialCharacters",
        {
            "RequestItems": {
                SPECIAL_TEXT: [
                    {"PutRequest": {"Item": {SPECIAL_TEXT: S(SPECIAL_TEXT)}}},
                    {"DeleteRequest": {"Key": {NAIVE_NAME: S(E_ACUTE)}}},
                ]
            },
            "ReturnItemCollectionMetrics": "SIZE",
        },
    ),
    # TransactGetItems
    request(
        "TransactGetItems",
        "Minimal",
        {"TransactItems": [{"Get": {"TableName": "Users", "Key": {"pk": S("user-1")}}}]},
    ),
    request(
        "TransactGetItems",
        "AllOptionsOnTwoTables",
        {
            "TransactItems": [
                {
                    "Get": {
                        "TableName": "Orders",
                        "Key": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#1")},
                        "ProjectionExpression": "#s, total",
                        "ExpressionAttributeNames": {"#s": "status"},
                    }
                },
                {"Get": {"TableName": "Customers", "Key": {"pk": S("CUSTOMER#1")}}},
                {"Get": {"TableName": "Orders", "Key": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#2")}}},
            ],
            "ReturnConsumedCapacity": "TOTAL",
        },
    ),
    request(
        "TransactGetItems",
        "ReturnConsumedCapacityIndexes",
        {"TransactItems": [{"Get": {"TableName": "Users", "Key": {"pk": S("user-1")}}}], "ReturnConsumedCapacity": "INDEXES"},
    ),
    request(
        "TransactGetItems",
        "HundredActions",
        {"TransactItems": [{"Get": {"TableName": "Users", "Key": numbered_key(i)}} for i in range(100)]},
    ),
    request(
        "TransactGetItems",
        "SpecialCharacters",
        {
            "TransactItems": [
                {
                    "Get": {
                        "TableName": SPECIAL_TEXT,
                        "Key": {SPECIAL_TEXT: S(SPECIAL_TEXT)},
                        "ProjectionExpression": "#n",
                        "ExpressionAttributeNames": {"#n": SPECIAL_TEXT},
                    }
                }
            ]
        },
    ),
    # TransactWriteItems: botocore does not fill ClientRequestToken, the client does, so the vectors set it where they want one.
    request(
        "TransactWriteItems",
        "PutOnly",
        {"TransactItems": [{"Put": {"TableName": "Users", "Item": {"pk": S("user-1")}}}], "ClientRequestToken": TOKEN},
    ),
    request(
        "TransactWriteItems",
        "NoToken",
        {"TransactItems": [{"Put": {"TableName": "Users", "Item": {"pk": S("user-1")}}}]},
    ),
    request(
        "TransactWriteItems",
        "EveryActionKindWithEveryOption",
        {
            "TransactItems": [
                {"ConditionCheck": STOCK_CHECK},
                {"Put": ORDER_PUT},
                {"Update": CUSTOMER_UPDATE},
                {"Delete": COUPON_DELETE},
            ],
            "ReturnConsumedCapacity": "INDEXES",
            "ReturnItemCollectionMetrics": "SIZE",
            "ClientRequestToken": TOKEN,
        },
    ),
    request(
        "TransactWriteItems",
        "EveryActionKindMinimal",
        {
            "TransactItems": [
                {"ConditionCheck": {"TableName": "Customers", "Key": {"pk": S("CUSTOMER#1")}, "ConditionExpression": "attribute_exists(pk)"}},
                {"Put": {"TableName": "Orders", "Item": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#1")}}},
                {"Update": {"TableName": "Customers", "Key": {"pk": S("CUSTOMER#1")}, "UpdateExpression": "ADD orders :one",
                            "ExpressionAttributeValues": {":one": N("1")}}},
                {"Delete": {"TableName": "Coupons", "Key": {"code": S("WELCOME10")}}},
            ],
            "ClientRequestToken": TOKEN,
        },
    ),
    request(
        "TransactWriteItems",
        "ReturnValuesOnConditionCheckFailureAllOldOnEachKind",
        {
            "TransactItems": [
                {"ConditionCheck": dict(STOCK_CHECK)},
                {"Put": {"TableName": "Orders", "Item": {"pk": S("a")}, "ReturnValuesOnConditionCheckFailure": "ALL_OLD"}},
                {"Update": {"TableName": "Customers", "Key": {"pk": S("a")}, "UpdateExpression": "SET a = :a",
                            "ExpressionAttributeValues": {":a": N("1")}, "ReturnValuesOnConditionCheckFailure": "ALL_OLD"}},
                {"Delete": {"TableName": "Coupons", "Key": {"code": S("a")}, "ReturnValuesOnConditionCheckFailure": "ALL_OLD"}},
            ],
            "ClientRequestToken": TOKEN,
        },
    ),
    request(
        "TransactWriteItems",
        "AllAttributeValueTypes",
        {
            "TransactItems": [
                {"Put": {"TableName": "Users", "Item": dict(ALL_VALUE_TYPES, pk=S("user-1"))}},
                {"Update": {"TableName": "Users", "Key": {"pk": S("user-2")}, "UpdateExpression": "SET a = :s",
                            "ExpressionAttributeValues": ALL_VALUE_TYPES}},
            ],
            "ClientRequestToken": TOKEN,
        },
    ),
    request(
        "TransactWriteItems",
        "HundredActions",
        {"TransactItems": [{"Put": {"TableName": "Users", "Item": numbered_key(i)}} for i in range(100)], "ClientRequestToken": TOKEN},
    ),
    request(
        "TransactWriteItems",
        "SpecialCharacters",
        {
            "TransactItems": [
                {"Put": {"TableName": SPECIAL_TEXT, "Item": {SPECIAL_TEXT: S(SPECIAL_TEXT)}, "ConditionExpression": "#n = :v",
                         "ExpressionAttributeNames": {"#n": SPECIAL_TEXT}, "ExpressionAttributeValues": {":v": S(SPECIAL_TEXT)}}},
            ],
            "ClientRequestToken": TOKEN,
        },
    ),
    request(
        "TransactWriteItems",
        "ReturnConsumedCapacityTotalAndTableArn",
        {
            "TransactItems": [{"Delete": {"TableName": ARN, "Key": {"pk": S("user-1")}}}],
            "ReturnConsumedCapacity": "TOTAL",
            "ClientRequestToken": "a",
        },
    ),
]

# ---------------------------------------------------------------------------------------------------------------------
# Table and stream requests: CreateTable, DeleteTable, UpdateTimeToLive, ListStreams, DescribeStream, GetShardIterator, GetRecords
# ---------------------------------------------------------------------------------------------------------------------


def ATTR(name, attribute_type):
    return {"AttributeName": name, "AttributeType": attribute_type}


def KEY(name, key_type):
    return {"AttributeName": name, "KeyType": key_type}


STREAM_VIEW_TYPES = {
    "NewImage": "NEW_IMAGE",
    "OldImage": "OLD_IMAGE",
    "NewAndOldImages": "NEW_AND_OLD_IMAGES",
    "KeysOnly": "KEYS_ONLY",
}

FORUM_STREAM_ARN = "arn:aws:dynamodb:us-west-2:111122223333:table/Forum/stream/2015-05-20T20:51:10.252"
FORUM_SHARD_ITERATOR = FORUM_STREAM_ARN + "|1|AAAAAAAAAAEvJp6D+zaQ...  <remaining characters omitted> ..."
FORUM_SHARD_ID = "shardId-00000001414576573621-f55eea83"

ORDERS_ATTRIBUTES = [ATTR("pk", "S"), ATTR("sk", "S"), ATTR("status", "S"), ATTR("createdAt", "N")]
ORDERS_KEY = [KEY("pk", "HASH"), KEY("sk", "RANGE")]

REQUESTS += [
    # CreateTable
    request(
        "CreateTable",
        "Minimal",
        {
            "AttributeDefinitions": [ATTR("pk", "S")],
            "TableName": "Users",
            "KeySchema": [KEY("pk", "HASH")],
            "ProvisionedThroughput": {"ReadCapacityUnits": 5, "WriteCapacityUnits": 5},
        },
    ),
    request(
        "CreateTable",
        "CreateATableFromApiReference",
        {
            "AttributeDefinitions": [ATTR("ForumName", "S"), ATTR("Subject", "S"), ATTR("LastPostDateTime", "S")],
            "TableName": "Thread",
            "KeySchema": [KEY("ForumName", "HASH"), KEY("Subject", "RANGE")],
            "LocalSecondaryIndexes": [
                {
                    "IndexName": "LastPostIndex",
                    "KeySchema": [KEY("ForumName", "HASH"), KEY("LastPostDateTime", "RANGE")],
                    "Projection": {"ProjectionType": "KEYS_ONLY"},
                }
            ],
            "BillingMode": "PAY_PER_REQUEST",
            "Tags": [{"Key": "Owner", "Value": "BlueTeam"}],
        },
        AWS_REFERENCE.format(operation="CreateTable", example="Create a Table"),
    ),
    request(
        "CreateTable",
        "AllOptions",
        {
            "AttributeDefinitions": ORDERS_ATTRIBUTES,
            "TableName": "Orders",
            "KeySchema": ORDERS_KEY,
            "LocalSecondaryIndexes": [
                {
                    "IndexName": "ByCreatedAt",
                    "KeySchema": [KEY("pk", "HASH"), KEY("createdAt", "RANGE")],
                    "Projection": {"ProjectionType": "INCLUDE", "NonKeyAttributes": ["total", "customer"]},
                }
            ],
            "GlobalSecondaryIndexes": [
                {
                    "IndexName": "ByStatus",
                    "KeySchema": [KEY("status", "HASH"), KEY("createdAt", "RANGE")],
                    "Projection": {"ProjectionType": "ALL"},
                    "ProvisionedThroughput": {"ReadCapacityUnits": 3, "WriteCapacityUnits": 2},
                }
            ],
            "BillingMode": "PROVISIONED",
            "ProvisionedThroughput": {"ReadCapacityUnits": 10, "WriteCapacityUnits": 20},
            "StreamSpecification": {"StreamEnabled": True, "StreamViewType": "NEW_AND_OLD_IMAGES"},
            "SSESpecification": {
                "Enabled": True,
                "SSEType": "KMS",
                "KMSMasterKeyId": "arn:aws:kms:eu-central-2:123456789012:key/1234abcd-12ab-34cd-56ef-1234567890ab",
            },
            "Tags": [{"Key": "Owner", "Value": "BlueTeam"}, {"Key": "Env", "Value": ""}],
            "TableClass": "STANDARD_INFREQUENT_ACCESS",
            "DeletionProtectionEnabled": True,
        },
    ),
    request(
        "CreateTable",
        "OnDemandWithGlobalSecondaryIndexes",
        {
            "AttributeDefinitions": ORDERS_ATTRIBUTES,
            "TableName": "Orders",
            "KeySchema": ORDERS_KEY,
            "GlobalSecondaryIndexes": [
                {"IndexName": "ByStatus", "KeySchema": [KEY("status", "HASH")], "Projection": {"ProjectionType": "KEYS_ONLY"}},
                {"IndexName": "ByCreatedAt", "KeySchema": [KEY("createdAt", "HASH")], "Projection": {"ProjectionType": "ALL"}},
            ],
            "BillingMode": "PAY_PER_REQUEST",
        },
    ),
    request(
        "CreateTable",
        "KeyTypesAndLargeCapacity",
        {
            "AttributeDefinitions": [ATTR("pk", "B"), ATTR("sk", "N")],
            "TableName": "Blobs",
            "KeySchema": [KEY("pk", "HASH"), KEY("sk", "RANGE")],
            "ProvisionedThroughput": {"ReadCapacityUnits": 9223372036854775807, "WriteCapacityUnits": 1},
        },
    ),
    request(
        "CreateTable",
        "StreamDisabled",
        {
            "AttributeDefinitions": [ATTR("pk", "S")],
            "TableName": "Users",
            "KeySchema": [KEY("pk", "HASH")],
            "BillingMode": "PAY_PER_REQUEST",
            "StreamSpecification": {"StreamEnabled": False},
        },
    ),
    request(
        "CreateTable",
        "SseEnabledWithoutKey",
        {
            "AttributeDefinitions": [ATTR("pk", "S")],
            "TableName": "Users",
            "KeySchema": [KEY("pk", "HASH")],
            "BillingMode": "PAY_PER_REQUEST",
            "SSESpecification": {"Enabled": True},
        },
    ),
    request(
        "CreateTable",
        "SseAes256",
        {
            "AttributeDefinitions": [ATTR("pk", "S")],
            "TableName": "Users",
            "KeySchema": [KEY("pk", "HASH")],
            "BillingMode": "PAY_PER_REQUEST",
            "SSESpecification": {"SSEType": "AES256"},
        },
    ),
    request(
        "CreateTable",
        "TableClassStandard",
        {
            "AttributeDefinitions": [ATTR("pk", "S")],
            "TableName": "Users",
            "KeySchema": [KEY("pk", "HASH")],
            "BillingMode": "PAY_PER_REQUEST",
            "TableClass": "STANDARD",
        },
    ),
    request(
        "CreateTable",
        "SpecialCharactersInTags",
        {
            "AttributeDefinitions": [ATTR("pk", "S")],
            "TableName": "Users",
            "KeySchema": [KEY("pk", "HASH")],
            "BillingMode": "PAY_PER_REQUEST",
            "Tags": [{"Key": SPECIAL_TEXT, "Value": SPECIAL_TEXT}],
        },
    ),
    # DeleteTable
    request(
        "DeleteTable",
        "DeleteATableFromApiReference",
        {"TableName": "Reply"},
        AWS_REFERENCE.format(operation="DeleteTable", example="Delete a Table"),
    ),
    request(
        "DeleteTable",
        "TableArn",
        {"TableName": "arn:aws:dynamodb:us-west-2:123456789012:table/Reply"},
    ),
    # UpdateTimeToLive
    request(
        "UpdateTimeToLive",
        "Enable",
        {"TableName": "TTLExample", "TimeToLiveSpecification": {"Enabled": True, "AttributeName": "ttl"}},
        "AWS CLI reference, update-time-to-live, example 'To enable TTL' (the API reference has the syntax only)",
    ),
    request(
        "UpdateTimeToLive",
        "Disable",
        {"TableName": "TTLExample", "TimeToLiveSpecification": {"Enabled": False, "AttributeName": "ttl"}},
        "AWS CLI reference, update-time-to-live, example 'To disable TTL' (the API reference has the syntax only)",
    ),
    request(
        "UpdateTimeToLive",
        "SpecialCharactersInAttributeName",
        {"TableName": "TTLExample", "TimeToLiveSpecification": {"Enabled": True, "AttributeName": SPECIAL_TEXT}},
    ),
    # ListStreams
    request(
        "ListStreams",
        "RetrieveAllStreamArnsFromApiReference",
        {},
        STREAMS_REFERENCE.format(operation="ListStreams", example="Retrieve All Stream ARNs"),
    ),
    request("ListStreams", "OneTable", {"TableName": "Forum"}),
    request(
        "ListStreams",
        "AllOptions",
        {"TableName": "Forum", "Limit": 25, "ExclusiveStartStreamArn": FORUM_STREAM_ARN},
    ),
    request("ListStreams", "LimitIntMax", {"Limit": 2147483647}),
    # DescribeStream
    request("DescribeStream", "Minimal", {"StreamArn": FORUM_STREAM_ARN}),
    request(
        "DescribeStream",
        "ChildShardsFromApiReference",
        {
            "StreamArn": FORUM_STREAM_ARN,
            "ShardFilter": {"Type": "CHILD_SHARDS", "ShardId": "shardId-00000001741631711871-1f6a72cf"},
        },
        STREAMS_REFERENCE.format(operation="DescribeStream", example="Retrieve Information About a Stream")
        + " (the reference prints ShardFilter as a string; it is an object)",
    ),
    request(
        "DescribeStream",
        "AllOptions",
        {
            "StreamArn": FORUM_STREAM_ARN,
            "Limit": 100,
            "ExclusiveStartShardId": FORUM_SHARD_ID,
            "ShardFilter": {"Type": "CHILD_SHARDS", "ShardId": FORUM_SHARD_ID},
        },
    ),
    # GetShardIterator
    request(
        "GetShardIterator",
        "TrimHorizonFromApiReference",
        {"StreamArn": FORUM_STREAM_ARN, "ShardId": "00000001414576573621-f55eea83", "ShardIteratorType": "TRIM_HORIZON"},
        STREAMS_REFERENCE.format(operation="GetShardIterator", example="Retrieve a Shard Iterator For a Stream"),
    ),
    request(
        "GetShardIterator",
        "Latest",
        {"StreamArn": FORUM_STREAM_ARN, "ShardId": FORUM_SHARD_ID, "ShardIteratorType": "LATEST"},
    ),
    request(
        "GetShardIterator",
        "AtSequenceNumber",
        {
            "StreamArn": FORUM_STREAM_ARN,
            "ShardId": FORUM_SHARD_ID,
            "ShardIteratorType": "AT_SEQUENCE_NUMBER",
            "SequenceNumber": "300000000000000499659",
        },
    ),
    request(
        "GetShardIterator",
        "AfterSequenceNumber",
        {
            "StreamArn": FORUM_STREAM_ARN,
            "ShardId": FORUM_SHARD_ID,
            "ShardIteratorType": "AFTER_SEQUENCE_NUMBER",
            "SequenceNumber": "300000000000000499659",
        },
    ),
    # GetRecords
    request(
        "GetRecords",
        "RetrieveStreamRecordsFromApiReference",
        {"ShardIterator": FORUM_SHARD_ITERATOR},
        STREAMS_REFERENCE.format(operation="GetRecords", example="Retrieve stream records from a shard"),
    ),
    request("GetRecords", "WithLimit", {"ShardIterator": FORUM_SHARD_ITERATOR, "Limit": 1000}),
    request("GetRecords", "LimitIntMax", {"ShardIterator": FORUM_SHARD_ITERATOR, "Limit": 2147483647}),
]

for _name, _wire in STREAM_VIEW_TYPES.items():
    REQUESTS.append(
        request(
            "CreateTable",
            "StreamView" + _name,
            {
                "AttributeDefinitions": [ATTR("pk", "S")],
                "TableName": "Users",
                "KeySchema": [KEY("pk", "HASH")],
                "BillingMode": "PAY_PER_REQUEST",
                "StreamSpecification": {"StreamEnabled": True, "StreamViewType": _wire},
            },
        )
    )

# ListTables, DescribeTimeToLive and UpdateTable. The API reference has the syntax of these operations but no worked requests that
# this file could quote, so the vectors are crafted from the documented syntax and have no source.
BY_STATUS_KEY = [KEY("status", "HASH"), KEY("createdAt", "RANGE")]

REQUESTS += [
    # ListTables
    request("ListTables", "NothingSet", {}),
    request("ListTables", "Limit", {"Limit": 3}),
    request("ListTables", "AllOptions", {"ExclusiveStartTableName": "Forum", "Limit": 100}),
    request("ListTables", "LimitOne", {"Limit": 1}),
    request("ListTables", "ExclusiveStartTableNameOnly", {"ExclusiveStartTableName": "Orders"}),
    # DescribeTimeToLive
    request("DescribeTimeToLive", "TableName", {"TableName": "TTLExample"}),
    request("DescribeTimeToLive", "TableArn", {"TableName": "arn:aws:dynamodb:us-west-2:123456789012:table/TTLExample"}),
    request("DescribeTimeToLive", "SpecialCharactersInTableName", {"TableName": SPECIAL_TEXT}),
    # UpdateTable
    request("UpdateTable", "OnlyTheTableName", {"TableName": "Orders"}),
    request("UpdateTable", "BillingModePayPerRequest", {"TableName": "Orders", "BillingMode": "PAY_PER_REQUEST"}),
    request(
        "UpdateTable",
        "BillingModeProvisionedWithIndexCapacity",
        {
            "TableName": "Orders",
            "BillingMode": "PROVISIONED",
            "ProvisionedThroughput": {"ReadCapacityUnits": 5, "WriteCapacityUnits": 6},
            "GlobalSecondaryIndexUpdates": [
                {"Update": {"IndexName": "ByStatus", "ProvisionedThroughput": {"ReadCapacityUnits": 3, "WriteCapacityUnits": 2}}}
            ],
        },
    ),
    request(
        "UpdateTable",
        "ProvisionedThroughputOnly",
        {"TableName": "Orders", "ProvisionedThroughput": {"ReadCapacityUnits": 10, "WriteCapacityUnits": 20}},
    ),
    request(
        "UpdateTable",
        "LargeProvisionedThroughput",
        {"TableName": "Orders", "ProvisionedThroughput": {"ReadCapacityUnits": 9223372036854775807, "WriteCapacityUnits": 1}},
    ),
    request(
        "UpdateTable",
        "StreamEnabled",
        {"TableName": "Orders", "StreamSpecification": {"StreamEnabled": True, "StreamViewType": "NEW_AND_OLD_IMAGES"}},
    ),
    request("UpdateTable", "StreamDisabled", {"TableName": "Orders", "StreamSpecification": {"StreamEnabled": False}}),
    request(
        "UpdateTable",
        "GlobalSecondaryIndexCreateProvisioned",
        {
            "TableName": "Orders",
            "AttributeDefinitions": [ATTR("status", "S"), ATTR("createdAt", "N")],
            "GlobalSecondaryIndexUpdates": [
                {
                    "Create": {
                        "IndexName": "ByStatus",
                        "KeySchema": BY_STATUS_KEY,
                        "Projection": {"ProjectionType": "INCLUDE", "NonKeyAttributes": ["total", "customer"]},
                        "ProvisionedThroughput": {"ReadCapacityUnits": 3, "WriteCapacityUnits": 2},
                    }
                }
            ],
        },
    ),
    request(
        "UpdateTable",
        "GlobalSecondaryIndexCreateOnDemand",
        {
            "TableName": "Orders",
            "AttributeDefinitions": [ATTR("status", "S")],
            "GlobalSecondaryIndexUpdates": [
                {"Create": {"IndexName": "ByStatus", "KeySchema": [KEY("status", "HASH")], "Projection": {"ProjectionType": "KEYS_ONLY"}}}
            ],
        },
    ),
    request(
        "UpdateTable",
        "GlobalSecondaryIndexCreateAllProjection",
        {
            "TableName": "Orders",
            "AttributeDefinitions": [ATTR("status", "S")],
            "GlobalSecondaryIndexUpdates": [
                {"Create": {"IndexName": "ByStatus", "KeySchema": [KEY("status", "HASH")], "Projection": {"ProjectionType": "ALL"}}}
            ],
        },
    ),
    request(
        "UpdateTable",
        "GlobalSecondaryIndexUpdateCapacity",
        {
            "TableName": "Orders",
            "GlobalSecondaryIndexUpdates": [
                {"Update": {"IndexName": "ByStatus", "ProvisionedThroughput": {"ReadCapacityUnits": 1, "WriteCapacityUnits": 1}}}
            ],
        },
    ),
    request(
        "UpdateTable",
        "GlobalSecondaryIndexDelete",
        {"TableName": "Orders", "GlobalSecondaryIndexUpdates": [{"Delete": {"IndexName": "ByStatus"}}]},
    ),
    request(
        "UpdateTable",
        "GlobalSecondaryIndexUpdatesOfEveryKind",
        {
            "TableName": "Orders",
            "AttributeDefinitions": [ATTR("customer", "S")],
            "GlobalSecondaryIndexUpdates": [
                {"Delete": {"IndexName": "ByStatus"}},
                {"Update": {"IndexName": "ByCreatedAt", "ProvisionedThroughput": {"ReadCapacityUnits": 4, "WriteCapacityUnits": 4}}},
                {"Create": {"IndexName": "ByCustomer", "KeySchema": [KEY("customer", "HASH")], "Projection": {"ProjectionType": "ALL"}}},
            ],
        },
    ),
    request("UpdateTable", "DeletionProtectionOn", {"TableName": "Orders", "DeletionProtectionEnabled": True}),
    request("UpdateTable", "DeletionProtectionOff", {"TableName": "Orders", "DeletionProtectionEnabled": False}),
    request("UpdateTable", "TableClassStandard", {"TableName": "Orders", "TableClass": "STANDARD"}),
    request("UpdateTable", "TableClassStandardInfrequentAccess", {"TableName": "Orders", "TableClass": "STANDARD_INFREQUENT_ACCESS"}),
    request("UpdateTable", "TableArnAsName", {"TableName": ARN, "DeletionProtectionEnabled": True}),
    request(
        "UpdateTable",
        "AllOptions",
        {
            "TableName": "Orders",
            "AttributeDefinitions": [ATTR("status", "S"), ATTR("createdAt", "N")],
            "BillingMode": "PROVISIONED",
            "ProvisionedThroughput": {"ReadCapacityUnits": 10, "WriteCapacityUnits": 20},
            "GlobalSecondaryIndexUpdates": [
                {
                    "Create": {
                        "IndexName": "ByStatus",
                        "KeySchema": BY_STATUS_KEY,
                        "Projection": {"ProjectionType": "ALL"},
                        "ProvisionedThroughput": {"ReadCapacityUnits": 3, "WriteCapacityUnits": 2},
                    }
                }
            ],
            "StreamSpecification": {"StreamEnabled": True, "StreamViewType": "KEYS_ONLY"},
            "TableClass": "STANDARD_INFREQUENT_ACCESS",
            "DeletionProtectionEnabled": True,
        },
    ),
    request(
        "UpdateTable",
        "SpecialCharacters",
        {
            "TableName": SPECIAL_TEXT,
            "AttributeDefinitions": [ATTR(SPECIAL_TEXT, "S")],
            "GlobalSecondaryIndexUpdates": [
                {"Create": {"IndexName": SPECIAL_TEXT, "KeySchema": [KEY(SPECIAL_TEXT, "HASH")], "Projection": {"ProjectionType": "ALL"}}}
            ],
        },
    ),
]

for _name, _wire in STREAM_VIEW_TYPES.items():
    REQUESTS.append(
        request(
            "UpdateTable",
            "StreamView" + _name,
            {"TableName": "Orders", "StreamSpecification": {"StreamEnabled": True, "StreamViewType": _wire}},
        )
    )


# ---------------------------------------------------------------------------------------------------------------------
# Responses
# ---------------------------------------------------------------------------------------------------------------------

UNKNOWN_NESTED = {"Nested": {"Deeper": [1, {"x": None}, "text", True, 1.5]}, "Flag": False}

THREAD_ITEM = {
    "LastPostedBy": S("alice@example.com"),
    "ForumName": S("Amazon DynamoDB"),
    "LastPostDateTime": S("20130320010350"),
    "Tags": {"SS": ["Update", "Multiple Items", "HelpMe"]},
    "Subject": S("Maximum number of items?"),
    "Views": N("5"),
    "Message": S("I want to put 10 million data items to an Amazon DynamoDB table.  Is there an upper limit?"),
}

ALL_TYPES_ITEM = {
    "pk": S("USER#1"),
    "sk": N("-12.5E+3"),
    "string": S("text"),
    "emptyString": S(""),
    "number": N("9.99999999999999999999999999999999999E+125"),
    "binary": B(base64.b64encode(bytes([0, 1, 2, 253, 254, 255])).decode("ascii")),
    "true": BOOL(True),
    "false": BOOL(False),
    "null": NULL,
    "stringSet": {"SS": ["a", "b", "c"]},
    "numberSet": {"NS": ["1", "2.5", "-3"]},
    "binarySet": {"BS": [base64.b64encode(b"\x00\x01").decode("ascii"), base64.b64encode(b"\xff").decode("ascii")]},
    "list": {"L": [S("x"), N("1"), {"L": []}, {"M": {}}, BOOL(False), NULL]},
    "map": {"M": {"inner": {"M": {"deep": {"L": [N("1"), S("two")]}}}, "empty": S("")}},
    NAIVE_NAME: S(E_ACUTE + GRIN),
    SPECIAL_TEXT: S(SPECIAL_TEXT),
}

CONSUMED_CAPACITY_WITH_INDEXES = {
    "TableName": "Orders",
    "CapacityUnits": 6.5,
    "ReadCapacityUnits": 2.5,
    "WriteCapacityUnits": 4.0,
    "Table": {"CapacityUnits": 3.0, "ReadCapacityUnits": 1.0, "WriteCapacityUnits": 2.0},
    "LocalSecondaryIndexes": {"ByDate": {"CapacityUnits": 1.5}},
    "GlobalSecondaryIndexes": {"ByEmail": {"CapacityUnits": 1.0, "WriteCapacityUnits": 1.0}, "ByTeam": {"CapacityUnits": 1.0}},
    # A member that the service model of botocore does not know (yet): ignored by both parsers.
    "VectorIndexes": {"embedding": {"VectorSearchRequestBytes": 1024}},
}

METRICS = {"ItemCollectionKey": {"pk": S("CUSTOMER#1")}, "SizeEstimateRangeGB": [0.0, 1.0]}

RESPONSES = [
    # PutItem
    response("PutItem", "ItemCollectionMetrics", {"ItemCollectionMetrics": METRICS, "Unknown": UNKNOWN_NESTED}),
    # UpdateItem
    response(
        "UpdateItem",
        "ConditionalUpdateFromApiReference",
        {"Attributes": THREAD_ITEM},
        AWS_REFERENCE.format(operation="UpdateItem", example="Conditional Update"),
    ),
    response(
        "UpdateItem",
        "AtomicCounterFromApiReference",
        "{\n}",
        AWS_REFERENCE.format(operation="UpdateItem", example="Atomic Counter"),
    ),
    response(
        "UpdateItem",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "ItemCollectionMetrics": dict(METRICS, FutureMember={"a": [1, 2]}),
            "ConsumedCapacity": CONSUMED_CAPACITY_WITH_INDEXES,
            "Attributes": {"Views": N("6"), "Tags": {"SS": ["Update"]}},
            "Trailing": [1, 2, 3],
        },
    ),
    response("UpdateItem", "OnlyUnknownProperties", {"Unknown": UNKNOWN_NESTED, "Other": "text"}),
    # DeleteItem
    response(
        "DeleteItem",
        "FromApiReference",
        {
            "Attributes": {
                "LastPostedBy": S("fred@example.com"),
                "ForumName": S("Amazon DynamoDB"),
                "LastPostDateTime": S("201303201023"),
                "Tags": {"SS": ["Update", "Multiple Items", "HelpMe"]},
                "Subject": S("How do I update multiple items?"),
                "Message": S("I want to update multiple items in a single call. What's the best way to do that?"),
            }
        },
        AWS_REFERENCE.format(operation="DeleteItem", example="Delete an Item"),
    ),
    response("DeleteItem", "NothingDeleted", "{}"),
    response(
        "DeleteItem",
        "AllMembersAndUnknownProperties",
        {
            "ConsumedCapacity": {"TableName": "Orders", "CapacityUnits": 1.0, "Unknown": 1},
            "Unknown": UNKNOWN_NESTED,
            "ItemCollectionMetrics": {"SizeEstimateRangeGB": [0.5, 2.0], "ItemCollectionKey": {"pk": N("7")}},
            "Attributes": ALL_TYPES_ITEM,
        },
    ),
    # Query
    response(
        "Query",
        "RangeOfItemsFromApiReference",
        {
            "ConsumedCapacity": {"CapacityUnits": 1, "TableName": "Reply"},
            "Count": 2,
            "Items": [
                {
                    "ReplyDateTime": S("2015-02-18T20:27:36.165Z"),
                    "PostedBy": S("User A"),
                    "Id": S("Amazon DynamoDB#DynamoDB Thread 1"),
                },
                {
                    "ReplyDateTime": S("2015-02-25T20:27:36.165Z"),
                    "PostedBy": S("User B"),
                    "Id": S("Amazon DynamoDB#DynamoDB Thread 1"),
                },
            ],
            "ScannedCount": 2,
        },
        AWS_REFERENCE.format(operation="Query", example="Retrieve a Range of Items"),
    ),
    response(
        "Query",
        "CountItemsFromApiReference",
        '{\n          "Count":2,\n          "ScannedCount":2\n}',
        AWS_REFERENCE.format(operation="Query", example="Count Items"),
    ),
    # A filter removed everything that was read: no items, but the page is not the last one.
    response(
        "Query",
        "EmptyPageWithLastEvaluatedKey",
        {
            "Count": 0,
            "Items": [],
            "LastEvaluatedKey": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#100")},
            "ScannedCount": 100,
        },
    ),
    response("Query", "EmptyLastPage", {"Count": 0, "Items": [], "ScannedCount": 0}),
    # The order of the properties is not specified: the items come last, the key and the capacity first.
    response(
        "Query",
        "PropertiesInAnotherOrder",
        {
            "LastEvaluatedKey": {"pk": S("CUSTOMER#1"), "sk": B(base64.b64encode(b"\x00\x01").decode("ascii"))},
            "ConsumedCapacity": CONSUMED_CAPACITY_WITH_INDEXES,
            "ScannedCount": 3,
            "Count": 2,
            "Items": [{"pk": S("a")}, {"pk": S("b"), "n": N("1")}],
        },
    ),
    response("Query", "AllAttributeTypes", {"Count": 1, "Items": [ALL_TYPES_ITEM], "ScannedCount": 1}),
    response(
        "Query",
        "ManyItemsOfDifferentShape",
        {
            "Count": 3,
            "Items": [{"pk": S("a")}, {}, {"pk": S("c"), "nested": {"M": {"l": {"L": [{"M": {"x": N("1")}}]}}}}],
            "ScannedCount": 3,
        },
    ),
    response(
        "Query",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "Count": 1,
            "ConsumedCapacity": CONSUMED_CAPACITY_WITH_INDEXES,
            "Items": [{"pk": S("a")}],
            "Other": [UNKNOWN_NESTED],
            "ScannedCount": 4,
            "LastEvaluatedKey": {"pk": S("a")},
        },
    ),
    # Scan
    response(
        "Scan",
        "ReturnAllItemsFromApiReference",
        {
            "ConsumedCapacity": {"CapacityUnits": 0.5, "TableName": "Reply"},
            "Count": 4,
            "Items": [
                {
                    "PostedBy": S("joe@example.com"),
                    "ReplyDateTime": S("20130320115336"),
                    "Id": S("Amazon DynamoDB#How do I update multiple items?"),
                    "Message": S("Have you looked at BatchWriteItem?"),
                },
                {
                    "PostedBy": S("fred@example.com"),
                    "ReplyDateTime": S("20130320115342"),
                    "Id": S("Amazon DynamoDB#How do I update multiple items?"),
                    "Message": S("No, I didn't know about that.  Where can I find more information?"),
                },
                {
                    "PostedBy": S("joe@example.com"),
                    "ReplyDateTime": S("20130320115347"),
                    "Id": S("Amazon DynamoDB#How do I update multiple items?"),
                    "Message": S("BatchWriteItem is documented in the Amazon DynamoDB API Reference."),
                },
                {
                    "PostedBy": S("fred@example.com"),
                    "ReplyDateTime": S("20130320115352"),
                    "Id": S("Amazon DynamoDB#How do I update multiple items?"),
                    "Message": S("OK, I'll take a look at that.  Thanks!"),
                },
            ],
            "ScannedCount": 4,
        },
        AWS_REFERENCE.format(operation="Scan", example="Return All Items"),
    ),
    response(
        "Scan",
        "FilterExpressionFromApiReference",
        {
            "ConsumedCapacity": {"CapacityUnits": 0.5, "TableName": "Reply"},
            "Count": 2,
            "Items": [
                {
                    "PostedBy": S("joe@example.com"),
                    "ReplyDateTime": S("20130320115336"),
                    "Id": S("Amazon DynamoDB#How do I update multiple items?"),
                    "Message": S("Have you looked at BatchWriteItem?"),
                },
                {
                    "PostedBy": S("joe@example.com"),
                    "ReplyDateTime": S("20130320115347"),
                    "Id": S("Amazon DynamoDB#How do I update multiple items?"),
                    "Message": S("BatchWriteItem is documented in the Amazon DynamoDB API Reference."),
                },
            ],
            "ScannedCount": 4,
        },
        AWS_REFERENCE.format(operation="Scan", example="Use a Filter Expression"),
    ),
    # One page of a parallel scan: the key belongs to this segment and must be sent back with the same segment.
    response(
        "Scan",
        "ParallelSegmentPage",
        {
            "Count": 2,
            "Items": [{"pk": S("user-31")}, {"pk": S("user-77"), "n": N("7")}],
            "ScannedCount": 2,
            "LastEvaluatedKey": {"pk": S("user-77")},
        },
    ),
    response(
        "Scan",
        "EmptyPageWithLastEvaluatedKey",
        {"Count": 0, "Items": [], "LastEvaluatedKey": {"pk": S("user-77")}, "ScannedCount": 1000},
    ),
    response("Scan", "SelectCountPage", {"Count": 130, "ScannedCount": 250, "LastEvaluatedKey": {"pk": S("user-250")}}),
    response("Scan", "AllAttributeTypes", {"Count": 1, "Items": [ALL_TYPES_ITEM], "ScannedCount": 1}),
    response(
        "Scan",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "Items": [{"pk": S("a")}, {"pk": S("b")}],
            "ConsumedCapacity": CONSUMED_CAPACITY_WITH_INDEXES,
            "Count": 2,
            "Trailing": {"a": {"b": {"c": [[], {}]}}},
            "ScannedCount": 2,
        },
    ),
]

FORUM_ITEMS = [
    {"Name": S("Amazon DynamoDB"), "Threads": N("5"), "Messages": N("19"), "Views": N("35")},
    {"Name": S("Amazon RDS"), "Threads": N("8"), "Messages": N("32"), "Views": N("38")},
    {"Name": S("Amazon Redshift"), "Threads": N("12"), "Messages": N("55"), "Views": N("47")},
]

THREAD_ITEMS = [
    {
        "Tags": {"SS": ["Reads", "MultipleUsers"]},
        "Message": S("How many users can read a single data item at a time? Are there any limits?"),
    }
]

CAPACITY_PER_TABLE = [
    {
        "TableName": "Orders",
        "CapacityUnits": 6.0,
        "Table": {"CapacityUnits": 4.0},
        "GlobalSecondaryIndexes": {"ByStatus": {"CapacityUnits": 2.0}},
    },
    {"TableName": "Customers", "CapacityUnits": 1.0, "ReadCapacityUnits": 1.0, "WriteCapacityUnits": 0.0},
]

METRICS_PER_TABLE = {
    "Orders": [
        {"ItemCollectionKey": {"pk": S("CUSTOMER#1")}, "SizeEstimateRangeGB": [0.0, 1.0]},
        {"ItemCollectionKey": {"pk": S("CUSTOMER#2")}, "SizeEstimateRangeGB": [1.5, 2.5], "FutureMember": [1]},
    ],
    "Customers": [{"ItemCollectionKey": {"pk": N("7")}, "SizeEstimateRangeGB": [0.5, 0.75]}],
    "Empty": [],
}

UNPROCESSED_KEYS = {
    "Orders": {
        "Keys": [{"pk": S("CUSTOMER#1"), "sk": S("ORDER#2")}, {"pk": S("CUSTOMER#1"), "sk": S("ORDER#3")}],
        "ConsistentRead": True,
        "ProjectionExpression": "#s, total",
        "ExpressionAttributeNames": {"#s": "status"},
        "FutureMember": {"a": [1]},
    },
    "Customers": {"Keys": [{"pk": B(base64.b64encode(b"\x00\xff").decode("ascii"))}]},
}

UNPROCESSED_WRITES = {
    "Orders": [
        {"PutRequest": {"Item": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#1"), "total": N("12.5")}}},
        {"DeleteRequest": {"Key": {"pk": S("CUSTOMER#1"), "sk": S("ORDER#0")}}},
    ],
    "Customers": [{"PutRequest": {"Item": ALL_TYPES_ITEM, "FutureMember": 1}}],
}

RESPONSES += [
    # BatchGetItem
    response(
        "BatchGetItem",
        "RetrieveItemsFromMultipleTablesFromApiReference",
        {
            "Responses": {"Forum": FORUM_ITEMS, "Thread": THREAD_ITEMS},
            "UnprocessedKeys": {},
            "ConsumedCapacity": [{"TableName": "Forum", "CapacityUnits": 3}, {"TableName": "Thread", "CapacityUnits": 1}],
        },
        AWS_REFERENCE.format(operation="BatchGetItem", example="Retrieve Items from Multiple Tables")
        + " (the comma missing after the Forum array in the published sample is added)",
    ),
    response("BatchGetItem", "NoMembers", "{}"),
    response("BatchGetItem", "EmptyResponsesAndUnprocessedKeys", {"Responses": {}, "UnprocessedKeys": {}}),
    response("BatchGetItem", "TableWithoutItems", {"Responses": {"Forum": [], "Thread": THREAD_ITEMS}}),
    response(
        "BatchGetItem",
        "NothingReadAllKeysUnprocessed",
        {"Responses": {}, "UnprocessedKeys": UNPROCESSED_KEYS},
    ),
    response(
        "BatchGetItem",
        "PartialResultAcrossTables",
        {
            "Responses": {"Orders": [{"pk": S("CUSTOMER#1"), "sk": S("ORDER#1"), "total": N("12.5")}], "Customers": [{"pk": S("CUSTOMER#1")}]},
            "UnprocessedKeys": {"Orders": UNPROCESSED_KEYS["Orders"]},
            "ConsumedCapacity": CAPACITY_PER_TABLE,
        },
    ),
    response(
        "BatchGetItem",
        "PropertiesInAnotherOrder",
        {
            "ConsumedCapacity": CAPACITY_PER_TABLE,
            "UnprocessedKeys": {"Customers": UNPROCESSED_KEYS["Customers"]},
            "Responses": {"Orders": [{"pk": S("a")}, {}, {"pk": S("c")}]},
        },
    ),
    response("BatchGetItem", "AllAttributeTypes", {"Responses": {"Users": [ALL_TYPES_ITEM]}}),
    response(
        "BatchGetItem",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "Responses": {"Users": [{"pk": S("a")}]},
            "Other": [UNKNOWN_NESTED],
            "UnprocessedKeys": {"Users": {"Keys": [{"pk": S("b")}], "Unknown": UNKNOWN_NESTED}},
            "ConsumedCapacity": [dict(CAPACITY_PER_TABLE[0], VectorIndexes={"embedding": {"VectorSearchRequestBytes": 1}})],
            "Trailing": {"a": {"b": {"c": [[], {}]}}},
        },
    ),
    # BatchWriteItem
    response(
        "BatchWriteItem",
        "MultipleOperationsFromApiReference",
        {
            "UnprocessedItems": {"Forum": [forum_put("Amazon ElastiCache")]},
            "ConsumedCapacity": [{"TableName": "Forum", "CapacityUnits": 3}],
        },
        AWS_REFERENCE.format(operation="BatchWriteItem", example="Multiple Operations on One Table"),
    ),
    response("BatchWriteItem", "NoMembers", "{}"),
    response("BatchWriteItem", "EmptyUnprocessedItems", {"UnprocessedItems": {}}),
    response("BatchWriteItem", "UnprocessedPutsAndDeletesOnTwoTables", {"UnprocessedItems": UNPROCESSED_WRITES}),
    response(
        "BatchWriteItem",
        "ItemCollectionMetricsAndCapacityPerTable",
        {
            "UnprocessedItems": {},
            "ItemCollectionMetrics": METRICS_PER_TABLE,
            "ConsumedCapacity": CAPACITY_PER_TABLE,
        },
    ),
    response(
        "BatchWriteItem",
        "PropertiesInAnotherOrder",
        {
            "ConsumedCapacity": CAPACITY_PER_TABLE,
            "ItemCollectionMetrics": {"Orders": METRICS_PER_TABLE["Orders"][:1]},
            "UnprocessedItems": {"Orders": UNPROCESSED_WRITES["Orders"][1:]},
        },
    ),
    response(
        "BatchWriteItem",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "UnprocessedItems": {"Users": [{"DeleteRequest": {"Key": {"pk": S("a")}, "Unknown": 1}, "Future": True}]},
            "Other": [UNKNOWN_NESTED],
            "ItemCollectionMetrics": {"Users": [dict(METRICS_PER_TABLE["Orders"][0], FutureMember={"a": [1, 2]})]},
            "ConsumedCapacity": CAPACITY_PER_TABLE,
            "Trailing": [1, 2, 3],
        },
    ),
    # TransactGetItems
    response("TransactGetItems", "NoMembers", "{}"),
    response(
        "TransactGetItems",
        "ItemsInRequestOrderWithAMissingItem",
        {
            "Responses": [
                {"Item": {"pk": S("user-1"), "name": S("Ada")}},
                {},
                {"Item": {"pk": S("user-3"), "name": S("Grace")}},
            ]
        },
    ),
    response("TransactGetItems", "ExistingItemWithoutProjectedAttributes", {"Responses": [{"Item": {}}, {}]}),
    response("TransactGetItems", "EmptyResponses", {"Responses": []}),
    response(
        "TransactGetItems",
        "ConsumedCapacityPerTable",
        {"Responses": [{"Item": {"pk": S("a")}}], "ConsumedCapacity": CAPACITY_PER_TABLE},
    ),
    response("TransactGetItems", "AllAttributeTypes", {"Responses": [{"Item": ALL_TYPES_ITEM}]}),
    response(
        "TransactGetItems",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "ConsumedCapacity": CAPACITY_PER_TABLE,
            "Responses": [{"Item": {"pk": S("a")}, "Unknown": UNKNOWN_NESTED}, {"Other": [1]}],
            "Trailing": {"a": 1},
        },
    ),
    # TransactWriteItems
    response("TransactWriteItems", "NoMembers", "{}"),
    response(
        "TransactWriteItems",
        "ConsumedCapacityAndItemCollectionMetrics",
        {"ConsumedCapacity": CAPACITY_PER_TABLE, "ItemCollectionMetrics": METRICS_PER_TABLE},
    ),
    response(
        "TransactWriteItems",
        "PropertiesInAnotherOrder",
        {"ItemCollectionMetrics": {"Orders": METRICS_PER_TABLE["Orders"][:1]}, "ConsumedCapacity": CAPACITY_PER_TABLE[:1]},
    ),
    response(
        "TransactWriteItems",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "ConsumedCapacity": CAPACITY_PER_TABLE,
            "Other": [UNKNOWN_NESTED],
            "ItemCollectionMetrics": {"Users": [dict(METRICS_PER_TABLE["Orders"][0], FutureMember=UNKNOWN_NESTED)]},
            "Trailing": [[], {}],
        },
    ),
]

# ---------------------------------------------------------------------------------------------------------------------
# Errors
# ---------------------------------------------------------------------------------------------------------------------

CANCELLATION_REASONS_FROM_API_REFERENCE = (
    "AWS API reference, TransactWriteItems, TransactionCanceledException: the cancellation reason codes and messages"
)


def error(operation, name, status, wire, source=None):
    """wire is the error body as JSON text, or a Python value that is serialized compactly."""
    if not isinstance(wire, str):
        wire = json.dumps(wire, separators=(",", ":"))
    return {"operation": operation, "name": name, "source": source, "status": status, "wire": wire}


def coral(code, **members):
    return dict({"__type": "com.amazonaws.dynamodb.v20120810#" + code}, **members)


ERRORS = [
    # The body of a real response, of a transaction whose first action failed its condition.
    error(
        "TransactWriteItems",
        "ConditionFailedOnTheFirstAction",
        400,
        '{"__type":"com.amazonaws.dynamodb.v20120810#TransactionCanceledException","CancellationReasons":[{"Code":"ConditionalCheckFailed",'
        '"Message":"The conditional request failed"},{"Code":"None"}],"Message":"Transaction cancelled, please refer cancellation reasons '
        'for specific reasons [ConditionalCheckFailed, None]"}',
        "Response of a real DynamoDB call (TransactWriteItems)",
    ),
    error(
        "TransactWriteItems",
        "EveryDocumentedReasonCode",
        400,
        coral(
            "TransactionCanceledException",
            CancellationReasons=[
                {"Code": "None"},
                {"Code": "ConditionalCheckFailed", "Message": "The conditional request failed."},
                {"Code": "ItemCollectionSizeLimitExceeded", "Message": "Collection size exceeded."},
                {"Code": "TransactionConflict", "Message": "Transaction is ongoing for the item."},
                {
                    "Code": "ProvisionedThroughputExceeded",
                    "Message": "The level of configured provisioned throughput for the table was exceeded. "
                    "Consider increasing your provisioning level with the UpdateTable API.",
                },
                {
                    "Code": "ThrottlingError",
                    "Message": "Throughput exceeds the current capacity of your table or index. DynamoDB is automatically scaling your "
                    "table or index so please try again shortly.",
                },
                {"Code": "ValidationError", "Message": "One or more parameter values were invalid."},
            ],
            Message="Transaction cancelled, please refer cancellation reasons for specific reasons [None, ConditionalCheckFailed, "
            "ItemCollectionSizeLimitExceeded, TransactionConflict, ProvisionedThroughputExceeded, ThrottlingError, ValidationError]",
        ),
        CANCELLATION_REASONS_FROM_API_REFERENCE,
    ),
    error(
        "TransactWriteItems",
        "ReasonsWithTheItemThatFailedTheCondition",
        400,
        coral(
            "TransactionCanceledException",
            CancellationReasons=[
                {"Code": "None"},
                {"Code": "ConditionalCheckFailed", "Message": "The conditional request failed", "Item": ALL_TYPES_ITEM},
                {"Code": "ConditionalCheckFailed", "Message": "The conditional request failed", "Item": {}},
            ],
            Message="Transaction cancelled, please refer cancellation reasons for specific reasons [None, ConditionalCheckFailed, ConditionalCheckFailed]",
        ),
    ),
    error(
        "TransactWriteItems",
        "PropertiesInAnotherOrderWithUnknownMembers",
        400,
        coral(
            "TransactionCanceledException",
            Message="Transaction cancelled",
            Unknown=UNKNOWN_NESTED,
            CancellationReasons=[{"Message": "m", "Unknown": UNKNOWN_NESTED, "Code": "TransactionConflict"}],
        ),
    ),
    error("TransactWriteItems", "NoReasons", 400, coral("TransactionCanceledException", Message="Transaction cancelled")),
    error("TransactWriteItems", "EmptyReasons", 400, coral("TransactionCanceledException", CancellationReasons=[], Message="Transaction cancelled")),
    error(
        "TransactGetItems",
        "ConflictWithAConcurrentWrite",
        400,
        coral(
            "TransactionCanceledException",
            CancellationReasons=[{"Code": "None"}, {"Code": "TransactionConflict", "Message": "Transaction is ongoing for the item"}],
            message="Transaction cancelled, please refer cancellation reasons for specific reasons [None, TransactionConflict]",
        ),
    ),
    error(
        "TransactWriteItems",
        "TransactionInProgress",
        400,
        coral("TransactionInProgressException", Message="The transaction with the given request token is already in progress."),
    ),
    error(
        "TransactWriteItems",
        "IdempotentParameterMismatch",
        400,
        coral("IdempotentParameterMismatchException", Message="The request uses the same client token as a previous, but non-identical request."),
    ),
    error(
        "UpdateItem",
        "TransactionConflict",
        400,
        coral("TransactionConflictException", Message="Transaction is ongoing for the item"),
    ),
    error(
        "BatchWriteItem",
        "ReplicatedWriteConflict",
        400,
        coral("ReplicatedWriteConflictException", Message="Replicated write conflict"),
    ),
]


# ---------------------------------------------------------------------------------------------------------------------
# Table and stream responses
# ---------------------------------------------------------------------------------------------------------------------

THREAD_TABLE = {
    "TableArn": "arn:aws:dynamodb:us-west-2:123456789012:table/Thread",
    "AttributeDefinitions": [ATTR("ForumName", "S"), ATTR("LastPostDateTime", "S"), ATTR("Subject", "S")],
    "CreationDateTime": 1.36372808007e9,
    "ItemCount": 0,
    "KeySchema": [KEY("ForumName", "HASH"), KEY("Subject", "RANGE")],
    "LocalSecondaryIndexes": [
        {
            "IndexArn": "arn:aws:dynamodb:us-west-2:123456789012:table/Thread/index/LastPostIndex",
            "IndexName": "LastPostIndex",
            "IndexSizeBytes": 0,
            "ItemCount": 0,
            "KeySchema": [KEY("ForumName", "HASH"), KEY("LastPostDateTime", "RANGE")],
            "Projection": {"ProjectionType": "KEYS_ONLY"},
        }
    ],
    "TableName": "Thread",
    "TableSizeBytes": 0,
    "TableStatus": "CREATING",
}

FULL_TABLE = {
    "TableName": "Orders",
    "TableArn": "arn:aws:dynamodb:eu-central-2:123456789012:table/Orders",
    "TableId": "d4a0fa45-7c8e-4c5e-b3f4-2e9a8d8b3b3c",
    "TableStatus": "UPDATING",
    "CreationDateTime": 1761901254.813,
    "AttributeDefinitions": ORDERS_ATTRIBUTES,
    "KeySchema": ORDERS_KEY,
    "ProvisionedThroughput": {"NumberOfDecreasesToday": 0, "ReadCapacityUnits": 5, "WriteCapacityUnits": 5},
    "TableSizeBytes": 1234567890123,
    "ItemCount": 987654321098,
    "BillingModeSummary": {"BillingMode": "PAY_PER_REQUEST", "LastUpdateToPayPerRequestDateTime": 1761901300.5},
    "LocalSecondaryIndexes": [
        {
            "IndexName": "ByCreatedAt",
            "KeySchema": [KEY("pk", "HASH"), KEY("createdAt", "RANGE")],
            "Projection": {"ProjectionType": "INCLUDE", "NonKeyAttributes": ["total", "customer"]},
            "IndexSizeBytes": 10,
            "ItemCount": 2,
            "IndexArn": "arn:aws:dynamodb:eu-central-2:123456789012:table/Orders/index/ByCreatedAt",
            "FutureMember": [1, 2],
        }
    ],
    "GlobalSecondaryIndexes": [
        {
            "IndexName": "ByStatus",
            "KeySchema": [KEY("status", "HASH"), KEY("createdAt", "RANGE")],
            "Projection": {"ProjectionType": "ALL"},
            "IndexStatus": "CREATING",
            "Backfilling": True,
            "ProvisionedThroughput": {"ReadCapacityUnits": 3, "WriteCapacityUnits": 2},
            "WarmThroughput": {"ReadUnitsPerSecond": 12000, "WriteUnitsPerSecond": 4000, "Status": "ACTIVE"},
        },
        {
            "IndexName": "ByCustomer",
            "KeySchema": [KEY("customer", "HASH")],
            "Projection": {"ProjectionType": "KEYS_ONLY"},
            "IndexStatus": "ACTIVE",
            "Backfilling": False,
        },
        {"IndexName": "Minimal"},
    ],
    "StreamSpecification": {"StreamEnabled": True, "StreamViewType": "NEW_AND_OLD_IMAGES"},
    "LatestStreamLabel": "2026-10-31T12:00:00.000",
    "LatestStreamArn": "arn:aws:dynamodb:eu-central-2:123456789012:table/Orders/stream/2026-10-31T12:00:00.000",
    "SSEDescription": {"Status": "ENABLED", "SSEType": "KMS", "KMSMasterKeyArn": "arn:aws:kms:eu-central-2:123456789012:key/1"},
    "TableClassSummary": {"TableClass": "STANDARD_INFREQUENT_ACCESS"},
    "DeletionProtectionEnabled": True,
    "FutureMember": UNKNOWN_NESTED,
}

FORUM_KEY_SCHEMA = [KEY("ForumName", "HASH"), KEY("Subject", "RANGE")]


def stream_record(event_id, event_name, view, keys, sequence_number, new_image=None, old_image=None, extra=None):
    """A record of the shape GetRecords returns; the images are included as far as the view type has them."""
    change = {
        "ApproximateCreationDateTime": 1.46480431e9,
        "Keys": keys,
        "SequenceNumber": sequence_number,
        "SizeBytes": 41,
        "StreamViewType": view,
    }
    if new_image is not None:
        change["NewImage"] = new_image
    if old_image is not None:
        change["OldImage"] = old_image
    record = {
        "awsRegion": "us-west-2",
        "dynamodb": change,
        "eventID": event_id,
        "eventName": event_name,
        "eventSource": "aws:dynamodb",
        "eventVersion": "1.1",
    }
    if extra:
        record.update(extra)
    return record


FORUM_KEYS = {"ForumName": S("DynamoDB"), "Subject": S("DynamoDB Thread 3")}
OLD_THREAD = {"ForumName": S("DynamoDB"), "Subject": S("DynamoDB Thread 3"), "Views": N("5"), "Tags": {"SS": ["a", "b"]}}
NEW_THREAD = {"ForumName": S("DynamoDB"), "Subject": S("DynamoDB Thread 3"), "Views": N("6"), "Tags": {"SS": ["a", "b"]}}

GET_RECORDS_FROM_API_REFERENCE = {
    "NextShardIterator": FORUM_STREAM_ARN + "|1|AAAAAAAAAAGQBYshYDEe ... <remaining characters omitted> ...",
    "Records": [
        {
            "awsRegion": "us-west-2",
            "dynamodb": {
                "ApproximateCreationDateTime": 1.46480431e9,
                "Keys": {"ForumName": S("DynamoDB"), "Subject": S("DynamoDB Thread 3")},
                "SequenceNumber": "300000000000000499659",
                "SizeBytes": 41,
                "StreamViewType": "KEYS_ONLY",
            },
            "eventID": "e2fd9c34eff2d779b297b26f5fef4206",
            "eventName": "INSERT",
            "eventSource": "aws:dynamodb",
            "eventVersion": "1.0",
        },
        {
            "awsRegion": "us-west-2",
            "dynamodb": {
                "ApproximateCreationDateTime": 1.46480527e9,
                "Keys": {"ForumName": S("DynamoDB"), "Subject": S("DynamoDB Thread 1")},
                "SequenceNumber": "400000000000000499660",
                "SizeBytes": 41,
                "StreamViewType": "KEYS_ONLY",
            },
            "eventID": "4b25bd0da9a181a155114127e4837252",
            "eventName": "MODIFY",
            "eventSource": "aws:dynamodb",
            "eventVersion": "1.0",
        },
        {
            "awsRegion": "us-west-2",
            "dynamodb": {
                "ApproximateCreationDateTime": 1.46480646e9,
                "Keys": {"ForumName": S("DynamoDB"), "Subject": S("DynamoDB Thread 2")},
                "SequenceNumber": "500000000000000499661",
                "SizeBytes": 41,
                "StreamViewType": "KEYS_ONLY",
            },
            "eventID": "740280c73a3df7842edab3548a1b08ad",
            "eventName": "REMOVE",
            "eventSource": "aws:dynamodb",
            "eventVersion": "1.0",
        },
    ],
}

RESPONSES += [
    # CreateTable
    response(
        "CreateTable",
        "CreateATableFromApiReference",
        {"TableDescription": THREAD_TABLE},
        AWS_REFERENCE.format(operation="CreateTable", example="Create a Table"),
    ),
    response(
        "CreateTable",
        "AllMembersAndUnknownProperties",
        {"Unknown": UNKNOWN_NESTED, "TableDescription": FULL_TABLE, "Trailing": [1, 2, 3]},
    ),
    response("CreateTable", "OnlyTheName", {"TableDescription": {"TableName": "Users"}}),
    response(
        "CreateTable",
        "ExplicitDefaults",
        {
            "TableDescription": {
                "TableName": "Users",
                "KeySchema": [],
                "AttributeDefinitions": [],
                "LocalSecondaryIndexes": [],
                "GlobalSecondaryIndexes": [],
                "DeletionProtectionEnabled": False,
                "ItemCount": 0,
                "TableSizeBytes": 0,
            }
        },
    ),
    # DeleteTable
    response(
        "DeleteTable",
        "DeleteATableFromApiReference",
        {
            "TableDescription": {
                "TableArn": "arn:aws:dynamodb:us-west-2:123456789012:table/Reply",
                "ItemCount": 0,
                "ProvisionedThroughput": {"NumberOfDecreasesToday": 0, "ReadCapacityUnits": 5, "WriteCapacityUnits": 5},
                "TableName": "Reply",
                "TableSizeBytes": 0,
                "TableStatus": "DELETING",
            }
        },
        AWS_REFERENCE.format(operation="DeleteTable", example="Delete a Table"),
    ),
    response(
        "DeleteTable",
        "AllMembersAndUnknownProperties",
        {"TableDescription": dict(FULL_TABLE, TableStatus="DELETING"), "Unknown": UNKNOWN_NESTED},
    ),
    # UpdateTimeToLive
    response(
        "UpdateTimeToLive",
        "EnableFromCliReference",
        {"TimeToLiveSpecification": {"Enabled": True, "AttributeName": "ttl"}},
        "AWS CLI reference, update-time-to-live, example 'To enable TTL' (output)",
    ),
    response("UpdateTimeToLive", "Disable", {"TimeToLiveSpecification": {"Enabled": False, "AttributeName": "ttl"}}),
    response(
        "UpdateTimeToLive",
        "UnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "TimeToLiveSpecification": {"AttributeName": SPECIAL_TEXT, "Extra": [1], "Enabled": True},
        },
    ),
    response("UpdateTimeToLive", "NoSpecification", {}),
    # ListStreams
    response(
        "ListStreams",
        "RetrieveAllStreamArnsFromApiReference",
        {
            "Streams": [
                {
                    "StreamArn": "arn:aws:dynamodb:us-wesst-2:111122223333:table/Forum/stream/2015-05-20T20:51:10.252",
                    "TableName": "Forum",
                    "StreamLabel": "2015-05-20T20:51:10.252",
                },
                {
                    "StreamArn": "arn:aws:dynamodb:us-west-2:111122223333:table/Forum/stream/2015-05-20T20:50:02.714",
                    "TableName": "Forum",
                    "StreamLabel": "2015-05-20T20:50:02.714",
                },
                {
                    "StreamArn": "arn:aws:dynamodb:us-west-2:111122223333:table/Forum/stream/2015-05-19T23:03:50.641",
                    "TableName": "Forum",
                    "StreamLabel": "2015-05-19T23:03:50.641",
                },
            ]
        },
        STREAMS_REFERENCE.format(operation="ListStreams", example="Retrieve All Stream ARNs")
        + " (the reference omits the rest of the list; the typo in the region of the first ARN is kept)",
    ),
    response(
        "ListStreams",
        "PageWithLastEvaluatedStreamArn",
        {
            "LastEvaluatedStreamArn": FORUM_STREAM_ARN,
            "Streams": [{"StreamArn": FORUM_STREAM_ARN, "TableName": "Forum", "StreamLabel": "2015-05-20T20:51:10.252"}],
        },
    ),
    response("ListStreams", "NoStreams", {"Streams": []}),
    response("ListStreams", "NothingAtAll", {}),
    response(
        "ListStreams",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "Streams": [
                {"StreamArn": FORUM_STREAM_ARN, "Future": {"a": [1]}},
                {"StreamArn": FORUM_STREAM_ARN + "x", "TableName": "Forum", "StreamLabel": "label", "Other": 1},
            ],
            "LastEvaluatedStreamArn": FORUM_STREAM_ARN,
        },
    ),
    # DescribeStream
    response(
        "DescribeStream",
        "RetrieveInformationAboutAStreamFromApiReference",
        {
            "StreamDescription": {
                "StreamArn": FORUM_STREAM_ARN,
                "StreamLabel": "2015-05-20T20:51:10.252",
                "StreamStatus": "ENABLED",
                "StreamViewType": "NEW_AND_OLD_IMAGES",
                "CreationRequestDateTime": 1.432155070252e9,
                "TableName": "Forum",
                "KeySchema": FORUM_KEY_SCHEMA,
                "Shards": [
                    {
                        "SequenceNumberRange": {
                            "EndingSequenceNumber": "20500000000000000910398",
                            "StartingSequenceNumber": "20500000000000000910398",
                        },
                        "ShardId": "shardId-00000001414562045508-2bac9cd2",
                    },
                    {
                        "ParentShardId": "shardId-00000001414562045508-2bac9cd2",
                        "SequenceNumberRange": {
                            "EndingSequenceNumber": "820400000000000001192334",
                            "StartingSequenceNumber": "820400000000000001192334",
                        },
                        "ShardId": "shardId-00000001414576573621-f55eea83",
                    },
                    {
                        "ParentShardId": "shardId-00000001414576573621-f55eea83",
                        "SequenceNumberRange": {
                            "EndingSequenceNumber": "1683700000000000001135967",
                            "StartingSequenceNumber": "1683700000000000001135967",
                        },
                        "ShardId": "shardId-00000001414592258131-674fd923",
                    },
                    {
                        "ParentShardId": "shardId-00000001414592258131-674fd923",
                        "SequenceNumberRange": {"StartingSequenceNumber": "2574600000000000000935255"},
                        "ShardId": "shardId-00000001414608446368-3a1afbaf",
                    },
                ],
            }
        },
        STREAMS_REFERENCE.format(operation="DescribeStream", example="Retrieve Information About a Stream")
        + " (the reference prints CreationRequestDateTime as a date string and ends the object with a trailing comma, which"
        + " the service does not send; epoch seconds for the same moment and valid JSON here)",
    ),
    response(
        "DescribeStream",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "StreamDescription": {
                "FutureMember": [1, {"a": None}],
                "StreamArn": FORUM_STREAM_ARN,
                "StreamLabel": "2015-05-20T20:51:10.252",
                "StreamStatus": "DISABLING",
                "StreamViewType": "KEYS_ONLY",
                "CreationRequestDateTime": 1432155070.5,
                "TableName": "Forum",
                "KeySchema": [KEY("ForumName", "HASH")],
                "Shards": [
                    {"ShardId": "shardId-1", "Future": 1},
                    {
                        "ShardId": "shardId-2",
                        "ParentShardId": "shardId-1",
                        "SequenceNumberRange": {"StartingSequenceNumber": "1", "EndingSequenceNumber": "2", "Future": [1]},
                    },
                ],
                "LastEvaluatedShardId": "shardId-2",
            },
        },
    ),
    response("DescribeStream", "OnlyTheArn", {"StreamDescription": {"StreamArn": FORUM_STREAM_ARN}}),
    # GetShardIterator
    response(
        "GetShardIterator",
        "TrimHorizonFromApiReference",
        {"ShardIterator": FORUM_SHARD_ITERATOR},
        STREAMS_REFERENCE.format(operation="GetShardIterator", example="Retrieve a Shard Iterator For a Stream"),
    ),
    response("GetShardIterator", "UnknownProperties", {"Unknown": UNKNOWN_NESTED, "ShardIterator": "iterator", "Other": [1]}),
    # GetRecords
    response(
        "GetRecords",
        "RetrieveStreamRecordsFromApiReference",
        GET_RECORDS_FROM_API_REFERENCE,
        STREAMS_REFERENCE.format(operation="GetRecords", example="Retrieve stream records from a shard"),
    ),
    response("GetRecords", "EmptyWithNextShardIterator", {"Records": [], "NextShardIterator": FORUM_SHARD_ITERATOR}),
    response("GetRecords", "ClosedShardWithoutNextShardIterator", {"Records": []}),
    response(
        "GetRecords",
        "LastRecordsOfAClosedShard",
        {"Records": GET_RECORDS_FROM_API_REFERENCE["Records"][:1]},
    ),
    response("GetRecords", "NothingAtAll", {}),
    response(
        "GetRecords",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "Records": [
                stream_record(
                    "id-1",
                    "MODIFY",
                    "NEW_AND_OLD_IMAGES",
                    FORUM_KEYS,
                    "100000000000000000001",
                    new_image=ALL_TYPES_ITEM,
                    old_image=OLD_THREAD,
                    extra={"userIdentity": {"PrincipalId": "dynamodb.amazonaws.com", "Type": "Service", "Extra": 1}, "futureMember": [1]},
                ),
                stream_record("id-2", "REMOVE", "KEYS_ONLY", FORUM_KEYS, "100000000000000000002"),
            ],
            "NextShardIterator": FORUM_SHARD_ITERATOR,
            "Trailing": {"a": {"b": []}},
        },
    ),
    response(
        "GetRecords",
        "RemovedByTimeToLive",
        {
            "Records": [
                stream_record(
                    "ttl-1",
                    "REMOVE",
                    "OLD_IMAGE",
                    FORUM_KEYS,
                    "100000000000000000003",
                    old_image=OLD_THREAD,
                    extra={"userIdentity": {"PrincipalId": "dynamodb.amazonaws.com", "Type": "Service"}},
                )
            ],
            "NextShardIterator": FORUM_SHARD_ITERATOR,
        },
        "Amazon DynamoDB Streams API reference, Identity data type (PrincipalId and Type of a deletion by Time to Live); the record is crafted",
    ),
    response(
        "GetRecords",
        "ManyRecords",
        {
            "Records": [
                stream_record(
                    "id-%d" % index,
                    "INSERT",
                    "NEW_IMAGE",
                    {"pk": S("user-%d" % index)},
                    "%021d" % (100000000000000000000 + index),
                    new_image={"pk": S("user-%d" % index), "n": N(index)},
                )
                for index in range(40)
            ],
            "NextShardIterator": FORUM_SHARD_ITERATOR,
        },
    ),
]

for _name, _wire in STREAM_VIEW_TYPES.items():
    _new = {"ForumName": S("DynamoDB"), "Subject": S("DynamoDB Thread 3"), "Views": N("6")}
    _old = {"ForumName": S("DynamoDB"), "Subject": S("DynamoDB Thread 3"), "Views": N("5")}
    RESPONSES.append(
        response(
            "GetRecords",
            "StreamView" + _name,
            {
                "NextShardIterator": FORUM_SHARD_ITERATOR,
                "Records": [
                    stream_record("ev-1", "INSERT", _wire, FORUM_KEYS, "100000000000000000011", new_image=_new if _wire in ("NEW_IMAGE", "NEW_AND_OLD_IMAGES") else None),
                    stream_record(
                        "ev-2",
                        "MODIFY",
                        _wire,
                        FORUM_KEYS,
                        "100000000000000000012",
                        new_image=_new if _wire in ("NEW_IMAGE", "NEW_AND_OLD_IMAGES") else None,
                        old_image=_old if _wire in ("OLD_IMAGE", "NEW_AND_OLD_IMAGES") else None,
                    ),
                    stream_record("ev-3", "REMOVE", _wire, FORUM_KEYS, "100000000000000000013", old_image=_old if _wire in ("OLD_IMAGE", "NEW_AND_OLD_IMAGES") else None),
                ],
            },
        )
    )
    RESPONSES.append(
        response(
            "DescribeStream",
            "StreamView" + _name,
            {
                "StreamDescription": {
                    "StreamArn": FORUM_STREAM_ARN,
                    "StreamStatus": "ENABLED",
                    "StreamViewType": _wire,
                    "TableName": "Forum",
                    "KeySchema": FORUM_KEY_SCHEMA,
                    "Shards": [],
                }
            },
        )
    )
    RESPONSES.append(
        response(
            "CreateTable",
            "StreamView" + _name,
            {
                "TableDescription": {
                    "TableName": "Users",
                    "TableStatus": "CREATING",
                    "StreamSpecification": {"StreamEnabled": True, "StreamViewType": _wire},
                    "LatestStreamLabel": "2026-10-31T12:00:00.000",
                    "LatestStreamArn": FORUM_STREAM_ARN,
                }
            },
        )
    )

# ListTables, DescribeTimeToLive and UpdateTable responses, crafted from the documented syntax.
RESPONSES += [
    # ListTables
    response("ListTables", "PageWithLastEvaluatedTableName", {"LastEvaluatedTableName": "Reply", "TableNames": ["Forum", "ProductCatalog", "Reply"]}),
    response("ListTables", "LastPage", {"TableNames": ["Thread", "Users"]}),
    response("ListTables", "NoTables", {"TableNames": []}),
    response("ListTables", "NothingAtAll", "{}"),
    response("ListTables", "OnlyLastEvaluatedTableName", {"LastEvaluatedTableName": "Forum"}),
    response(
        "ListTables",
        "SpecialCharactersInNames",
        {"TableNames": [SPECIAL_TEXT, NAIVE_NAME + GRIN]},
    ),
    response(
        "ListTables",
        "AllMembersAndUnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "TableNames": ["a-table", "b-table"],
            "LastEvaluatedTableName": "b-table",
            "Trailing": [1, 2, 3],
        },
    ),
    # DescribeTimeToLive
    response(
        "DescribeTimeToLive",
        "Enabled",
        {"TimeToLiveDescription": {"TimeToLiveStatus": "ENABLED", "AttributeName": "ttl"}},
    ),
    response("DescribeTimeToLive", "NeverEnabled", {"TimeToLiveDescription": {"TimeToLiveStatus": "DISABLED"}}),
    response(
        "DescribeTimeToLive",
        "Enabling",
        {"TimeToLiveDescription": {"TimeToLiveStatus": "ENABLING", "AttributeName": "expiresAt"}},
    ),
    response(
        "DescribeTimeToLive",
        "Disabling",
        {"TimeToLiveDescription": {"TimeToLiveStatus": "DISABLING", "AttributeName": "expiresAt"}},
    ),
    response(
        "DescribeTimeToLive",
        "DisabledAfterUse",
        {"TimeToLiveDescription": {"TimeToLiveStatus": "DISABLED", "AttributeName": "expiresAt"}},
    ),
    response("DescribeTimeToLive", "NoDescription", "{}"),
    response("DescribeTimeToLive", "EmptyDescription", {"TimeToLiveDescription": {}}),
    response(
        "DescribeTimeToLive",
        "UnknownProperties",
        {
            "Unknown": UNKNOWN_NESTED,
            "TimeToLiveDescription": {"AttributeName": SPECIAL_TEXT, "Extra": [1], "TimeToLiveStatus": "ENABLED"},
        },
    ),
    # UpdateTable
    response(
        "UpdateTable",
        "AllMembersAndUnknownProperties",
        {"Unknown": UNKNOWN_NESTED, "TableDescription": FULL_TABLE, "Trailing": [1, 2, 3]},
    ),
    response("UpdateTable", "OnlyTheName", {"TableDescription": {"TableName": "Orders"}}),
    response(
        "UpdateTable",
        "ProvisionedCapacityAndTableClass",
        {
            "TableDescription": {
                "TableName": "Orders",
                "TableStatus": "UPDATING",
                "ProvisionedThroughput": {
                    "LastIncreaseDateTime": 1761901300.5,
                    "NumberOfDecreasesToday": 1,
                    "ReadCapacityUnits": 10,
                    "WriteCapacityUnits": 20,
                },
                "TableClassSummary": {"TableClass": "STANDARD", "LastUpdateDateTime": 1761901300.5},
                "GlobalSecondaryIndexes": [
                    {
                        "IndexName": "ByStatus",
                        "IndexStatus": "CREATING",
                        "Backfilling": True,
                        "ProvisionedThroughput": {"NumberOfDecreasesToday": 0, "ReadCapacityUnits": 3, "WriteCapacityUnits": 2},
                    },
                    {"IndexName": "ByCustomer", "IndexStatus": "DELETING", "ProvisionedThroughput": {}},
                ],
            }
        },
    ),
    response(
        "UpdateTable",
        "OnDemandReportsZeroCapacity",
        {
            "TableDescription": {
                "TableName": "Orders",
                "TableStatus": "UPDATING",
                "BillingModeSummary": {"BillingMode": "PAY_PER_REQUEST", "LastUpdateToPayPerRequestDateTime": 1761901300.5},
                "ProvisionedThroughput": {"NumberOfDecreasesToday": 0, "ReadCapacityUnits": 0, "WriteCapacityUnits": 0},
                "TableClassSummary": {"TableClass": "STANDARD_INFREQUENT_ACCESS"},
            }
        },
    ),
]


# ---------------------------------------------------------------------------------------------------------------------
# Running botocore
# ---------------------------------------------------------------------------------------------------------------------


def to_json_value(value):
    """Makes botocore's output JSON-serializable: bytes are shown base64-encoded, as DynamoDB sends them."""
    if isinstance(value, bytes):
        return base64.b64encode(value).decode("ascii")
    if isinstance(value, datetime.datetime):
        # Timestamps come back as datetime objects; DynamoDB sends them as epoch seconds, and this library keeps milliseconds.
        return round(value.timestamp(), 3)
    if isinstance(value, dict):
        return {key: to_json_value(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return [to_json_value(item) for item in value]
    return value


def serialize_request(services, vector):
    service, serializer, _ = services[service_of(vector["operation"])]
    operation = service.operation_model(vector["operation"])
    serialized = serializer.serialize_to_request(vector["params"], operation)
    headers = serialized["headers"]
    if headers["Content-Type"] != CONTENT_TYPE:
        sys.exit(f"{vector['operation']}/{vector['name']}: botocore sends another content type: {headers['Content-Type']}")
    if serialized["method"] != "POST" or serialized["url_path"] != "/":
        sys.exit(f"{vector['operation']}/{vector['name']}: botocore does not POST to /")
    body = serialized["body"].decode("utf-8")
    return {
        "service": service.service_name,
        "operation": vector["operation"],
        "name": vector["name"],
        "source": vector["source"],
        "target": headers["X-Amz-Target"],
        "params": to_json_value(vector["params"]),
        "body": body,
    }


def parse_response(services, vector):
    service, _, parser = services[service_of(vector["operation"])]
    operation = service.operation_model(vector["operation"])
    raw = {"body": vector["wire"].encode("utf-8"), "headers": {}, "status_code": 200}
    parsed = parser.parse(raw, operation.output_shape)
    parsed.pop("ResponseMetadata")
    return {
        "service": service.service_name,
        "operation": vector["operation"],
        "name": vector["name"],
        "source": vector["source"],
        "wire": vector["wire"],
        "parsed": to_json_value(parsed),
    }


def parse_error(services, vector):
    service, _, parser = services[service_of(vector["operation"])]
    """Parses an error body twice, the way botocore does for a client: as a generic error (code and message), and with the shape of the
    modeled exception that the code names, which is where botocore reads members such as CancellationReasons."""
    operation = service.operation_model(vector["operation"])
    raw = {"body": vector["wire"].encode("utf-8"), "headers": {}, "status_code": vector["status"]}
    generic = parser.parse(raw, operation.output_shape)["Error"]
    modeled = parser.parse(raw, service.shape_for(generic["Code"]))
    modeled.pop("ResponseMetadata", None)
    return {
        "operation": vector["operation"],
        "name": vector["name"],
        "source": vector["source"],
        "status": vector["status"],
        "wire": vector["wire"],
        "code": generic["Code"],
        "message": generic["Message"],
        "parsed": to_json_value(modeled),
    }


def check_unique(vectors, kind):
    seen = set()
    for vector in vectors:
        key = (vector["operation"], vector["name"])
        if key in seen:
            sys.exit(f"Duplicate {kind} vector {key}")
        seen.add(key)


def build():
    services = {}
    for name in ("dynamodb", "dynamodbstreams"):
        service = botocore.session.get_session().get_service_model(name)
        if service.protocol != "json":
            sys.exit(f"The {name} model of this botocore uses the protocol {service.protocol}, not json.")
        services[name] = (service, botocore.serialize.create_serializer(service.protocol), botocore.parsers.create_parser(service.protocol))
    check_unique(REQUESTS, "request")
    check_unique(COMMAND_REQUESTS, "command request")
    check_unique(RESPONSES, "response")
    check_unique(ERRORS, "error")
    return {
        "generator": "generate_operation_vectors.py",
        "botocore": botocore.__version__,
        "note": "Generated with botocore's serializer and parser. Do not edit by hand. Binary values are base64 text.",
        "requests": [serialize_request(services, vector) for vector in REQUESTS],
        "commandRequests": [serialize_request(services, vector) for vector in COMMAND_REQUESTS],
        "responses": [parse_response(services, vector) for vector in RESPONSES],
        "errors": [parse_error(services, vector) for vector in ERRORS],
    }


def render(document):
    # ensure_ascii writes every non-ASCII character as an escape, so the file is plain ASCII.
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
    print(
        f"Wrote {len(REQUESTS)} request, {len(COMMAND_REQUESTS)} command, {len(RESPONSES)} response and {len(ERRORS)} error vectors to {OUTPUT} "
        f"(botocore {botocore.__version__})."
    )


if __name__ == "__main__":
    main()
