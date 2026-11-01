"""Generates key_vectors.json, the golden vectors of the key encodings, with Python's decimal, datetime, uuid and ipaddress.

The encoded form of a key is an on-disk contract, so the expected bytes must not come from the C# implementation. They are
produced here, from the grammar in spec/requirements.md ("Key grammar") alone, by code that shares nothing with src/DynaSharp/Keys:

* Write vectors: a value (and a format) goes in, the exact UTF-8 bytes of the segment come out, or the value is rejected.
  Decimals are `decimal.Decimal` (exact, no binary rounding), timestamps are `datetime` plus an explicit count of 100 ns ticks
  (datetime has microseconds only), text is encoded with Python's strict UTF-8 and UTF-16 codecs.
* Read vectors: a candidate text goes in. Each encoder has a decoder that is deliberately more lenient than the grammar
  (uuid.UUID accepts upper case, braces and missing hyphens, Decimal accepts any sign and spelling, ipaddress.ip_address
  accepts what it accepts). The spec says `Parse(x)` succeeds exactly when `Format(Parse(x)) == x`, so a candidate is accepted
  if and only if encoding the decoded value gives the candidate back. Every candidate also carries the verdict its author
  intended, and the script stops if the two disagree: a wrong label and a wrong decoder cannot hide each other.
* Composite vectors: key templates. The script derives the delimiters of every placeholder itself (the characters adjacent to
  it in the template) and builds the key from the segments; reading is checked by a regular expression built from the template.

Setup: none, the standard library is enough (Python 3.9 or newer).

Run (from any directory):
    python generate_key_vectors.py           # rewrites key_vectors.json
    python generate_key_vectors.py --check   # exits 1 if the committed file is stale

The inputs of every vector are defined here only. Non-ASCII text is built from code points and written as \\uXXXX escapes
(ensure_ascii), so editors and tools cannot change what the vectors contain. Text that is not well-formed UTF-16 cannot be
a JSON string, so every string vector carries its UTF-16 code units, and every read vector carries the bytes as hex.
"""

import argparse
import datetime
import ipaddress
import json
import pathlib
import re
import sys
import uuid
from decimal import ROUND_HALF_EVEN, Decimal, getcontext

getcontext().prec = 200

OUTPUT = pathlib.Path(__file__).with_name("key_vectors.json")

# Characters that tools like to normalize, written as code points.
U_UMLAUT = chr(0x00FC)
CJK = chr(0x65E5) + chr(0x672C) + chr(0x8A9E)
GRIN = chr(0x1F600)  # outside the BMP: a surrogate pair in UTF-16, four bytes in UTF-8
REPLACEMENT = chr(0xFFFD)
MAX_SCALAR = chr(0x10FFFF)
DEL = chr(0x7F)
NEL = chr(0x85)  # a C1 control, which the grammar allows

LONG_MIN = -(2**63)
LONG_MAX = 2**63 - 1
ULONG_MAX = 2**64 - 1
DECIMAL_MAX = Decimal("79228162514264337593543950335")

TIME_FORMATS = ["Year", "Month", "Day", "Hour", "Minute", "Second", "Millisecond", "Microsecond", "Ticks"]


class Rejected(Exception):
    """The grammar rejects the value or the text."""


# --------------------------------------------------------------------------------------------------------------------------
# Encoders: value -> bytes, or Rejected.
# --------------------------------------------------------------------------------------------------------------------------


def units_of(text):
    """UTF-16 code units of well-formed text."""
    raw = text.encode("utf-16-le")
    return [int.from_bytes(raw[i : i + 2], "little") for i in range(0, len(raw), 2)]


def text_of(units):
    """Text of UTF-16 code units; raises Rejected if they are not well-formed (a lone surrogate)."""
    raw = b"".join(unit.to_bytes(2, "little") for unit in units)
    try:
        return raw.decode("utf-16-le")
    except UnicodeDecodeError:
        raise Rejected("ill-formed UTF-16")


def check_segment(text, delimiters):
    if text == "":
        raise Rejected("empty")
    if any(ord(c) < 0x20 for c in text):
        raise Rejected("control character")
    if any(d in text for d in delimiters):
        raise Rejected("delimiter")
    if MAX_SCALAR in text:
        # Reserved for the upper bound of a range, so that prefix + U+10FFFF is above every key under the prefix.
        raise Rejected("highest scalar")


def encode_string(units, delimiters):
    text = text_of(units)
    check_segment(text, delimiters)
    return text.encode("utf-8")


def encode_integer(value, delimiters):
    text = str(value)
    check_segment(text, delimiters)
    return text.encode("ascii")


def encode_digits(value, width):
    if value < 0:
        raise Rejected("negative")
    text = str(value)
    if len(text) > width:
        raise Rejected("too wide")
    return text.zfill(width).encode("ascii")


def encode_decimal(value, delimiters):
    number = Decimal(value)
    if not number.is_finite():
        raise Rejected("not a number")
    # 1.0 and 1 are equal and are one key; negative zero is zero.
    text = "0" if number == 0 else format(number.normalize(), "f")
    check_segment(text, delimiters)
    return text.encode("ascii")


def encode_fixed_point(value, integer_digits, fraction_digits):
    number = Decimal(value)
    negative = number < 0  # Decimal("-0.00") < 0 is False: negative zero is P
    magnitude = abs(number)
    step = Decimal(1).scaleb(-fraction_digits)
    rounded = magnitude.quantize(step, rounding=ROUND_HALF_EVEN)
    if rounded != magnitude:
        raise Rejected("fraction digits")
    text = format(rounded, "f")
    whole = text.split(".")[0]
    if len(whole) > integer_digits:
        raise Rejected("out of range")
    body = whole.zfill(integer_digits) + ("." + text.split(".")[1] if fraction_digits > 0 else "")
    if negative:
        body = "".join(c if c == "." else str(9 - int(c)) for c in body)
    return (("N" if negative else "P") + body).encode("ascii")


def time_text(utc, ticks, time_format):
    """The text of a UTC time (a naive datetime) with 0..9999999 extra ticks of the second, truncated to the format."""
    seconds = utc.isoformat(timespec="seconds")  # yyyy-MM-ddTHH:mm:ss, year padded to four digits
    index = TIME_FORMATS.index(time_format)
    if index <= 4:
        return seconds[: [4, 7, 10, 13, 16][index]]
    if time_format == "Second":
        return seconds + "Z"
    if time_format == "Millisecond":
        return f"{seconds}.{ticks // 10_000:03d}Z"
    if time_format == "Microsecond":
        return f"{seconds}.{ticks // 10:06d}Z"
    return f"{seconds}.{ticks:07d}Z"


def utc_of(fields):
    """UTC time of local fields and an offset in minutes; Rejected if it leaves years 1 to 9999."""
    local = datetime.datetime(fields["year"], fields["month"], fields["day"], fields["hour"], fields["minute"], fields["second"])
    try:
        return local - datetime.timedelta(minutes=fields["offset"])
    except OverflowError:
        raise Rejected("out of range")


def encode_timestamp(fields, time_format):
    return time_text(utc_of(fields), fields["ticks"], time_format).encode("ascii")


def encode_date(year, month, day):
    return datetime.date(year, month, day).isoformat().encode("ascii")


def encode_guid(text):
    return str(uuid.UUID(text)).encode("ascii")


def encode_ipv4(text):
    address = ipaddress.IPv4Address(text)
    return str(address).encode("ascii")


# --------------------------------------------------------------------------------------------------------------------------
# Decoders: text -> value, as lenient as the standard library is. Strictness comes from the round trip, see accepted().
# --------------------------------------------------------------------------------------------------------------------------


def decode_string(raw, delimiters):
    try:
        text = raw.decode("utf-8")  # strict: rejects overlong forms, surrogates, truncated sequences
    except UnicodeDecodeError:
        raise Rejected("not UTF-8")
    return units_of(text)


def decode_integer(raw, delimiters):
    return int(raw.decode("ascii"))  # int() accepts "+5", "05", "-0", " 5", "5_0"


def decode_digits(raw, width):
    return int(raw.decode("ascii"))


def decode_decimal(raw, delimiters):
    number = Decimal(raw.decode("ascii"))  # Decimal accepts "+1", "1.0", ".5", "1e3", "NaN"
    # What a .NET decimal can hold: at most 28 fraction digits and 96 bits of mantissa.
    if not number.is_finite() or number.as_tuple().exponent < -28 or abs(number) > DECIMAL_MAX:
        raise Rejected("not a decimal")
    return format(number, "f")


def decode_fixed_point(raw, integer_digits, fraction_digits):
    text = raw.decode("ascii")
    if text[:1] not in ("P", "N"):
        raise Rejected("sign")
    body = text[1:]
    if fraction_digits > 0:
        if body[integer_digits : integer_digits + 1] != ".":
            raise Rejected("point")
        digits = body[:integer_digits] + body[integer_digits + 1 :]
    else:
        digits = body
    if len(digits) != integer_digits + fraction_digits or any(c not in "0123456789" for c in digits):
        raise Rejected("digits")
    if text[0] == "N":
        digits = "".join(str(9 - int(c)) for c in digits)
    whole = digits[:integer_digits]
    fraction = digits[integer_digits:]
    number = Decimal(whole + ("." + fraction if fraction else ""))
    return format(-number if text[0] == "N" else number, "f")


TIMESTAMP_PATTERNS = {
    "Year": r"(\d{4})",
    "Month": r"(\d{4})-(\d{2})",
    "Day": r"(\d{4})-(\d{2})-(\d{2})",
    "Hour": r"(\d{4})-(\d{2})-(\d{2})T(\d{2})",
    "Minute": r"(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})",
    "Second": r"(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})Z",
    "Millisecond": r"(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\.(\d{3})Z",
    "Microsecond": r"(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\.(\d{6})Z",
    "Ticks": r"(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\.(\d{7})Z",
}


def decode_timestamp(raw, time_format):
    """Fields of the UTC time. \\d is ASCII only here, because the pattern is applied to a bytes object."""
    match = re.fullmatch(TIMESTAMP_PATTERNS[time_format].encode("ascii"), raw)
    if match is None:
        raise Rejected("shape")
    groups = [int(g) for g in match.groups()]
    fields = {"year": groups[0], "month": 1, "day": 1, "hour": 0, "minute": 0, "second": 0, "ticks": 0, "offset": 0}
    for name, value in zip(["year", "month", "day", "hour", "minute", "second"], groups):
        fields[name] = value
    if len(groups) > 6:
        digits = match.group(7).decode("ascii")
        fields["ticks"] = int(digits.ljust(7, "0"))
    try:
        datetime.datetime(fields["year"], fields["month"], fields["day"], fields["hour"], fields["minute"], fields["second"])
    except ValueError:
        raise Rejected("not a date")
    return fields


def decode_date(raw):
    fields = decode_timestamp(raw, "Day")
    return fields


def decode_guid(raw):
    return str(uuid.UUID(raw.decode("ascii")))


def decode_ipv4(raw):
    return str(ipaddress.ip_address(raw.decode("ascii")))


def ticks_since_epoch(fields):
    """100 ns ticks since 0001-01-01T00:00:00 of the UTC time of the fields, counted by Python's proleptic ordinal."""
    day = datetime.date(fields["year"], fields["month"], fields["day"]).toordinal() - 1
    seconds = day * 86400 + fields["hour"] * 3600 + fields["minute"] * 60 + fields["second"]
    return seconds * 10_000_000 + fields["ticks"]


# --------------------------------------------------------------------------------------------------------------------------
# Write vectors.
# --------------------------------------------------------------------------------------------------------------------------

WRITES = []
READS = []
COMPOSITES = []
COMPOSITE_READS = []


def hex_of(raw):
    return raw.hex()


def add_write(case_id, kind, params, encoder, note=None):
    """Adds a write vector; `encoder` returns the bytes or raises Rejected."""
    case = {"id": case_id, "kind": kind, **params}
    try:
        case["expected_hex"] = hex_of(encoder())
    except Rejected:
        case["error"] = True
    if note:
        case["note"] = note
    WRITES.append(case)


def string_params(units, delimiters):
    params = {"units": units, "delimiters": delimiters}
    try:
        params["text"] = text_of(units)
    except Rejected:
        pass
    return params


def add_string(name, units, delimiters="#"):
    add_write(f"string/{name}", "string", string_params(units, delimiters), lambda: encode_string(units, delimiters))


def build_strings():
    add_string("ascii", units_of("USER"))
    add_string("digits-and-letters", units_of("a1B2"))
    add_string("single-character", units_of("x"))
    add_string("space-inside", units_of("a b"))
    add_string("umlaut", units_of("Z" + U_UMLAUT + "rich"))
    add_string("cjk", units_of(CJK))
    add_string("supplementary-plane", units_of("a" + GRIN + "b"))
    add_string("max-scalar", units_of(MAX_SCALAR))
    add_string("max-scalar-inside", units_of("a" + MAX_SCALAR + "b"))
    add_string("max-scalar-at-end", units_of("ab" + MAX_SCALAR))
    add_string("max-scalar-and-more", units_of(MAX_SCALAR + "a"))
    add_string("last-scalar-below-the-maximum", units_of(chr(0x10FFFE)))
    add_string("last-bmp-noncharacter", units_of(chr(0xFFFF)))
    add_string("replacement-character-is-a-valid-character", units_of(REPLACEMENT))
    add_string("delete-character", units_of(DEL))
    add_string("c1-control", units_of(NEL))
    add_string("quote-and-backslash", units_of('a"b\\c'))
    add_string("long", units_of("ab" * 1500))
    add_string("empty", [])
    add_string("delimiter-only", units_of("#"))
    add_string("delimiter-inside", units_of("1#ORDER"))
    add_string("delimiter-at-start", units_of("#a"))
    add_string("delimiter-at-end", units_of("a#"))
    add_string("second-delimiter-of-the-set", units_of("a-b"), "#-")
    add_string("no-delimiters-allows-hash", units_of("a#b"), "")
    add_string("alphanumeric-delimiter", units_of("dev"), "v")
    add_string("nul", [0x61, 0x00, 0x62])
    add_string("unit-separator", [0x1F])
    add_string("tab", units_of("a\tb"))
    add_string("line-feed", units_of("a\nb"))
    add_string("lone-high-surrogate", [0x61, 0xD800])
    add_string("lone-low-surrogate", [0xDC00, 0x61])
    add_string("reversed-surrogate-pair", [0xDE00, 0xD83D])
    add_string("two-high-surrogates", [0xD83D, 0xD83D])
    add_string("high-surrogate-at-end", [0x61, 0x62, 0xD83D])
    add_string("max-surrogate-pair-is-valid", [0xDBFF, 0xDFFF])
    add_string("lowest-high-surrogate-alone", [0xD800])
    add_string("highest-low-surrogate-alone", [0xDFFF])


def add_integer(name, value, delimiters="#"):
    add_write(
        f"integer/{name}", "integer", {"value": str(value), "unsigned": value > LONG_MAX, "delimiters": delimiters},
        lambda: encode_integer(value, delimiters))


def build_integers():
    for name, value in [("zero", 0), ("one", 1), ("minus-one", -1), ("ten", 10), ("typical", 12345), ("negative", -9876),
                        ("long-max", LONG_MAX), ("long-min", LONG_MIN), ("ulong-max", ULONG_MAX), ("above-long-max", LONG_MAX + 1)]:
        add_integer(name, value)
    add_integer("minus-with-hyphen-delimiter", -5, "-")
    add_integer("positive-with-hyphen-delimiter", 5, "-")
    add_integer("digit-delimiter", 15, "5")
    add_integer("no-delimiters", -15, "")


def add_digits(name, value, width):
    add_write(
        f"digits/{name}", "digits", {"value": str(value), "width": width, "unsigned": value > LONG_MAX},
        lambda: encode_digits(value, width))


def build_digits():
    for name, value, width in [
        ("zero-width-1", 0, 1), ("nine-width-1", 9, 1), ("ten-width-1", 10, 1), ("zero-width-5", 0, 5), ("42-width-5", 42, 5),
        ("max-width-3", 999, 3), ("overflow-width-3", 1000, 3), ("negative", -1, 3), ("minus-ten", -10, 5),
        ("long-max-width-19", LONG_MAX, 19), ("long-max-width-18", LONG_MAX, 18), ("long-max-width-25", LONG_MAX, 25),
        ("ulong-max-width-20", ULONG_MAX, 20), ("ulong-max-width-19", ULONG_MAX, 19), ("ulong-max-width-30", ULONG_MAX, 30),
        ("zero-width-20", 0, 20), ("one-width-40", 1, 40), ("width-2-for-123", 123, 2), ("width-0", 0, 0), ("width-negative", 5, -3),
    ]:
        add_digits(name, value, width)


def add_decimal(name, value, delimiters="#"):
    add_write(f"decimal/{name}", "decimal", {"value": value, "delimiters": delimiters}, lambda: encode_decimal(value, delimiters))


def build_decimals():
    for name, value in [
        ("zero", "0"), ("zero-with-scale", "0.000"), ("negative-zero", "-0"), ("negative-zero-with-scale", "-0.00"),
        ("one", "1"), ("one-point-zero", "1.0"), ("trailing-zeros", "12.500"), ("trailing-zeros-integer", "100.00"),
        ("integer-with-zeros", "100"), ("ten", "10"), ("point-five", "0.5"), ("point-five-zeros", "0.50"),
        ("leading-point-zero", "0.0001"), ("negative", "-12.5"), ("negative-trailing-zeros", "-12.500"), ("one-point-one", "1.10"),
        ("seven", "7.0"), ("smallest", "0.0000000000000000000000000001"), ("negative-smallest", "-0.0000000000000000000000000001"),
        ("max", str(DECIMAL_MAX)), ("min", str(-DECIMAL_MAX)), ("max-with-fraction", "7.9228162514264337593543950335"),
        ("28-nines-fraction", "0.9999999999999999999999999999"), ("28-digit-fraction-trailing-zeros", "1.0000000000000000000000000000"),
        ("large-with-zeros", "10000000000000000000000000000"),
    ]:
        add_decimal(name, value)
    add_decimal("point-is-delimiter", "1.5", ".")
    add_decimal("integer-with-point-delimiter", "15", ".")
    add_decimal("minus-is-delimiter", "-1.5", "-")
    add_decimal("trailing-zeros-removed-before-delimiter-check", "1.0", ".")


def add_fixed_point(name, value, integer_digits, fraction_digits):
    add_write(
        f"fixed-point/n{integer_digits}.{fraction_digits}/{name}", "fixed_point",
        {"value": value, "integer_digits": integer_digits, "fraction_digits": fraction_digits},
        lambda: encode_fixed_point(value, integer_digits, fraction_digits))


def build_fixed_points():
    # The spec's own examples come first.
    for name, value in [
        ("spec-positive", "12.5"), ("spec-negative", "-12.5"), ("spec-zero", "0"), ("negative-zero", "-0.00"),
        ("negative-zero-with-scale", "-0.000"), ("trailing-zeros-fit", "12.500"), ("many-trailing-zeros", "12.50000000000000000000000000"),
        ("rounding-would-be-needed", "12.345"), ("tiny-fraction", "0.001"), ("half-of-a-step", "0.005"),
        ("just-over-a-step", "12.501"), ("max", "9999.99"), ("min", "-9999.99"), ("above-max", "10000"), ("below-min", "-10000"),
        ("just-above-max", "10000.00"), ("smallest-step", "0.01"), ("negative-smallest-step", "-0.01"), ("one", "1"),
        ("minus-one", "-1"), ("fraction-only", "0.5"), ("negative-fraction-only", "-0.5"), ("nine-nine", "99.99"),
        ("negative-sort-neighbours-a", "-12.51"), ("negative-sort-neighbours-b", "-12.49"), ("max-decimal", str(DECIMAL_MAX)),
        ("min-decimal", str(-DECIMAL_MAX)), ("wide-integer-part", "12345"), ("negative-wide-integer-part", "-12345"),
        ("rounds-up-to-range-edge", "9999.995"),
    ]:
        add_fixed_point(name, value, 4, 2)
    # n<i> without a point, for integers.
    for name, value in [("zero", "0"), ("nine", "9"), ("minus-nine", "-9"), ("ten", "10"), ("minus-one", "-1"), ("fraction", "0.5"),
                        ("negative-zero", "-0"), ("minus-ten", "-10")]:
        add_fixed_point(name, value, 1, 0)
    for name, value in [("zero", "0"), ("typical", "1234567890"), ("negative", "-1234567890"), ("max", "9999999999"),
                        ("min", "-9999999999"), ("over", "10000000000"), ("long-max", str(LONG_MAX)), ("long-min", str(LONG_MIN))]:
        add_fixed_point(name, value, 10, 0)
    for name, value in [("long-max", str(LONG_MAX)), ("long-min", str(LONG_MIN)), ("ulong-max", str(ULONG_MAX)), ("zero", "0"),
                        ("max", "9" * 20), ("min", "-" + "9" * 20), ("over", "1" + "0" * 20)]:
        add_fixed_point(name, value, 20, 0)
    # i + f = 28, the widest formats.
    for name, value in [("zero", "0"), ("max", "9999999999999999999999999999"), ("min", "-9999999999999999999999999999"),
                        ("one", "1"), ("minus-one", "-1"), ("over", "10000000000000000000000000000"),
                        ("decimal-max-is-29-digits", str(DECIMAL_MAX)), ("decimal-min-is-29-digits", str(-DECIMAL_MAX)),
                        ("fraction", "0.5"), ("typical", "123456789012345678901234567")]:
        add_fixed_point(name, value, 28, 0)
    for name, value in [("zero", "0"), ("max", "9.999999999999999999999999999"), ("min", "-9.999999999999999999999999999"),
                        ("smallest-step", "0.000000000000000000000000001"), ("negative-smallest-step", "-0.000000000000000000000000001"),
                        ("over", "10"), ("finer-than-the-step", "0.0000000000000000000000000001"), ("pi", "3.141592653589793238462643383"),
                        ("negative-pi", "-3.141592653589793238462643383"), ("one", "1"), ("minus-one", "-1")]:
        add_fixed_point(name, value, 1, 27)
    for name, value in [("zero", "0"), ("max", "9999999999999999999999999.999"), ("min", "-9999999999999999999999999.999"),
                        ("over", "10000000000000000000000000"), ("fine", "1234.5678")]:
        add_fixed_point(name, value, 25, 3)
    for name, value in [("zero", "0"), ("max", "99999999999999.99999999999999"), ("min", "-99999999999999.99999999999999"),
                        ("typical", "12345.678901234"), ("over", "100000000000000")]:
        add_fixed_point(name, value, 14, 14)
    # Money-like formats of the spec's single-table example.
    for name, value in [("zero", "0"), ("typical", "1234.5"), ("negative", "-1234.5"), ("max", "99999999.99"),
                        ("min", "-99999999.99"), ("over", "100000000"), ("cents-fraction", "0.001")]:
        add_fixed_point(name, value, 8, 2)
    for name, value in [("zero", "0"), ("typical", "42.5"), ("negative", "-42.5"), ("max", "9999999999.99"), ("min", "-9999999999.99")]:
        add_fixed_point(name, value, 10, 2)


def timestamp_fields(year, month, day, hour=0, minute=0, second=0, ticks=0, offset=0):
    return {"year": year, "month": month, "day": day, "hour": hour, "minute": minute, "second": second, "ticks": ticks,
            "offset": offset}


TIMESTAMP_INSTANTS = [
    ("spec-example", timestamp_fields(2026, 10, 31, 12, 34, 56, 7_890_123)),
    ("whole-millisecond", timestamp_fields(2026, 10, 31, 12, 34, 56, 7_890_000)),
    ("midnight", timestamp_fields(2026, 1, 1)),
    ("first-ticks", timestamp_fields(2026, 3, 4, 5, 6, 7, 1)),
    ("last-ticks-of-the-second", timestamp_fields(2026, 12, 31, 23, 59, 59, 9_999_999)),
    ("min", timestamp_fields(1, 1, 1)),
    ("max", timestamp_fields(9999, 12, 31, 23, 59, 59, 9_999_999)),
    ("leap-day", timestamp_fields(2024, 2, 29, 23, 59, 59, 9_999_999)),
    ("year-1000", timestamp_fields(1000, 1, 1, 0, 0, 0, 5)),
    ("year-999", timestamp_fields(999, 12, 31, 23, 59, 59, 9_999_999)),
    ("end-of-month", timestamp_fields(2026, 1, 31, 23, 59, 59, 5_000_000)),
    ("microsecond-precision", timestamp_fields(2026, 6, 15, 8, 9, 10, 1_234_560)),
    ("tick-below-microsecond", timestamp_fields(2026, 6, 15, 8, 9, 10, 1_234_569)),
    ("positive-offset-crosses-the-day", timestamp_fields(2026, 11, 1, 3, 30, 0, 1_234_567, offset=5 * 60 + 30)),
    ("negative-offset-crosses-the-year", timestamp_fields(2025, 12, 31, 20, 0, 0, 0, offset=-8 * 60)),
    ("offset-14-hours", timestamp_fields(2026, 7, 1, 12, 0, 0, 7_890_123, offset=14 * 60)),
    ("offset-minus-14-hours", timestamp_fields(2026, 7, 1, 12, 0, 0, 7_890_123, offset=-14 * 60)),
    ("offset-crosses-the-leap-day", timestamp_fields(2024, 3, 1, 1, 0, 0, 0, offset=2 * 60)),
    ("offset-at-max", timestamp_fields(9999, 12, 31, 23, 59, 59, 9_999_999, offset=0)),
    ("offset-at-min", timestamp_fields(1, 1, 1, 14, 0, 0, 0, offset=14 * 60)),
    ("offset-leaves-min", timestamp_fields(1, 1, 1, 0, 0, 0, 0, offset=60)),
]


def reject_unspecified():
    raise Rejected("a DateTime of kind Unspecified names no instant")


def add_timestamps():
    for name, fields in TIMESTAMP_INSTANTS:
        for time_format in TIME_FORMATS:
            try:
                utc_of(fields)
            except Rejected:
                continue  # not a time that .NET can hold either
            add_write(
                f"timestamp-offset/{time_format}/{name}", "timestamp_offset", {"fields": fields, "format": time_format},
                lambda: encode_timestamp(fields, time_format))
    # DateTime: Utc keeps the fields, Unspecified is rejected whatever the format, Local depends on the machine and is tested in C# only.
    for name, fields in TIMESTAMP_INSTANTS:
        if fields["offset"] != 0:
            continue
        for time_format in TIME_FORMATS:
            add_write(
                f"timestamp-datetime-utc/{time_format}/{name}", "timestamp_datetime", {"fields": fields, "format": time_format, "datetime_kind": "Utc"},
                lambda: encode_timestamp(fields, time_format))
    for time_format in TIME_FORMATS:
        fields = timestamp_fields(2026, 10, 31, 12, 34, 56, 7_890_123)
        add_write(
            f"timestamp-datetime-unspecified/{time_format}", "timestamp_datetime",
            {"fields": fields, "format": time_format, "datetime_kind": "Unspecified"}, reject_unspecified)


def build_dates_and_guids():
    for name, (year, month, day) in [("spec", (2026, 10, 31)), ("min", (1, 1, 1)), ("max", (9999, 12, 31)), ("leap-day", (2024, 2, 29)),
                                     ("year-999", (999, 12, 31)), ("new-year", (2026, 1, 1))]:
        add_write(f"date/{name}", "date", {"year": year, "month": month, "day": day}, lambda: encode_date(year, month, day))
    for name, text in [
        ("lowercase", "4f9c2a7e-0b1d-4e5f-8a6b-7c8d9e0f1a2b"), ("uppercase-input", "4F9C2A7E-0B1D-4E5F-8A6B-7C8D9E0F1A2B"),
        ("empty", "00000000-0000-0000-0000-000000000000"), ("all-f", "ffffffff-ffff-ffff-ffff-ffffffffffff"),
        ("byte-order", "00112233-4455-6677-8899-aabbccddeeff"), ("version-7", "018f3b9a-7c4e-7d12-9a3b-5c6d7e8f9a0b"),
        ("mixed-case-input", "AbCdEf01-2345-6789-aBcD-eF0123456789"),
    ]:
        add_write(f"guid/{name}", "guid", {"text": text}, lambda: encode_guid(text))


def build_formattable():
    for name, text, delimiters in [("ipv4", "10.0.0.1", "#"), ("ipv4-all-zero", "0.0.0.0", "#"), ("ipv4-broadcast", "255.255.255.255", "#"),
                                   ("ipv4-with-point-delimiter", "10.0.0.1", "."), ("ipv4-with-digit-delimiter", "10.0.0.1", "1")]:
        add_write(f"formattable/{name}", "ipv4", {"text": text, "delimiters": delimiters},
                  lambda: check_then(encode_ipv4(text), delimiters))


def check_then(raw, delimiters):
    check_segment(raw.decode("ascii"), delimiters)
    return raw


# --------------------------------------------------------------------------------------------------------------------------
# Read vectors: the verdict is the round trip; `intended` is what the author of the candidate expects.
# --------------------------------------------------------------------------------------------------------------------------


def accepted(kind, raw, params):
    """Returns (verdict, value): the candidate is accepted iff encode(decode(raw)) == raw."""
    try:
        if kind == "string":
            units = decode_string(raw, params["delimiters"])
            return encode_string(units, params["delimiters"]) == raw, units
        if kind == "integer":
            value = decode_integer(raw, params["delimiters"])
            if not ((0 if params["unsigned"] else LONG_MIN) <= value <= (ULONG_MAX if params["unsigned"] else LONG_MAX)):
                raise Rejected("range")
            return encode_integer(value, params["delimiters"]) == raw, str(value)
        if kind == "digits":
            value = decode_digits(raw, params["width"])
            if value > (ULONG_MAX if params["unsigned"] else LONG_MAX):
                raise Rejected("range")
            return encode_digits(value, params["width"]) == raw, str(value)
        if kind == "decimal":
            value = decode_decimal(raw, params["delimiters"])
            return encode_decimal(value, params["delimiters"]) == raw, value
        if kind == "fixed_point":
            value = decode_fixed_point(raw, params["integer_digits"], params["fraction_digits"])
            return encode_fixed_point(value, params["integer_digits"], params["fraction_digits"]) == raw, value
        if kind == "timestamp":
            fields = decode_timestamp(raw, params["format"])
            return encode_timestamp(fields, params["format"]) == raw, {"ticks": ticks_since_epoch(fields)}
        if kind == "date":
            fields = decode_date(raw)
            return encode_date(fields["year"], fields["month"], fields["day"]) == raw, {"ticks": ticks_since_epoch(fields)}
        if kind == "guid":
            value = decode_guid(raw)
            return encode_guid(value) == raw, value
        if kind == "ipv4":
            value = decode_ipv4(raw)
            return encode_ipv4(value) == raw and not any(d in value for d in params["delimiters"]), value
    except (Rejected, ValueError, ArithmeticError, UnicodeError):
        return False, None
    raise AssertionError(kind)


def add_read(case_id, kind, raw, intended, params):
    if isinstance(raw, str):
        raw = raw.encode("utf-8")
    verdict, value = accepted(kind, raw, params)
    if verdict != intended:
        sys.exit(f"{case_id}: the round trip says {'accept' if verdict else 'reject'}, the author meant {'accept' if intended else 'reject'}.")
    case = {"id": case_id, "kind": kind, **params, "input_hex": raw.hex(), "accept": verdict}
    if verdict:
        case["value"] = value
    try:
        case["text"] = raw.decode("utf-8")
    except UnicodeDecodeError:
        pass
    READS.append(case)


def build_string_reads():
    params = {"delimiters": "#"}
    for name, raw, intended in [
        ("ascii", "USER", True), ("umlaut", "Z" + U_UMLAUT + "rich", True), ("supplementary-plane", "a" + GRIN + "b", True),
        ("max-scalar", MAX_SCALAR, False), ("max-scalar-inside", "a" + MAX_SCALAR + "b", False),
        ("last-scalar-below-the-maximum", chr(0x10FFFE), True), ("last-bmp-noncharacter", chr(0xFFFF), True), ("space", "a b", True), ("replacement-character", REPLACEMENT, True),
        ("empty", "", False), ("delimiter", "a#b", False), ("only-delimiter", "#", False), ("nul", "a\x00b", False),
        ("unit-separator", "a\x1fb", False), ("tab", "a\tb", False),
        ("truncated-sequence", b"a\xc3", False), ("lone-continuation-byte", b"a\x80b", False), ("overlong-slash", b"\xc0\xaf", False),
        ("overlong-three-bytes", b"\xe0\x80\xaf", False), ("encoded-high-surrogate", b"\xed\xa0\x80", False),
        ("encoded-low-surrogate", b"\xed\xb0\x80", False), ("above-max-scalar", b"\xf4\x90\x80\x80", False),
        ("five-byte-form", b"\xf8\x88\x80\x80\x80", False), ("invalid-lead-byte", b"\xff", False), ("invalid-lead-byte-2", b"\xfe\x41", False),
        ("truncated-four-byte", b"\xf0\x9f\x98", False), ("max-scalar-bytes", b"\xf4\x8f\xbf\xbf", False), ("last-scalar-below-the-maximum-bytes", b"\xf4\x8f\xbf\xbe", True),
        ("bom-is-a-character", b"\xef\xbb\xbfa", True),
    ]:
        add_read(f"string/{name}", "string", raw, intended, params)
    add_read("string/second-delimiter", "string", "a-b", False, {"delimiters": "#-"})
    add_read("string/no-delimiters", "string", "a#b", True, {"delimiters": ""})


def build_integer_reads():
    for unsigned in (False, True):
        suffix = "unsigned" if unsigned else "signed"
        params = {"delimiters": "#", "unsigned": unsigned}
        cases = [("zero", "0", True), ("one", "1", True), ("typical", "12345", True), ("plus-sign", "+5", False),
                 ("leading-zero", "05", False), ("double-zero", "00", False), ("negative-zero", "-0", False), ("space-before", " 5", False),
                 ("space-after", "5 ", False), ("empty", "", False), ("minus-only", "-", False), ("plus-only", "+", False),
                 ("underscore", "5_0", False), ("exponent", "1e3", False), ("point", "1.0", False), ("hex", "0x10", False),
                 ("arabic-indic-digit", chr(0x0663), False), ("fullwidth-digit", chr(0xFF11), False), ("delimiter", "1#2", False),
                 ("ulong-max", str(ULONG_MAX), unsigned), ("long-max", str(LONG_MAX), True), ("above-long-max", str(LONG_MAX + 1), unsigned),
                 ("above-ulong-max", str(ULONG_MAX + 1), False), ("long-min", str(LONG_MIN), not unsigned),
                 ("below-long-min", str(LONG_MIN - 1), False), ("negative", "-5", not unsigned), ("minus-leading-zero", "-05", False),
                 ("double-minus", "--5", False), ("minus-plus", "-+5", False)]
        for name, raw, intended in cases:
            add_read(f"integer/{suffix}/{name}", "integer", raw, intended, params)
    add_read("integer/signed/hyphen-delimiter", "integer", "-5", False, {"delimiters": "-", "unsigned": False})


def build_digit_reads():
    for name, raw, width, unsigned, intended in [
        ("exact", "00042", 5, False, True), ("all-zero", "00000", 5, False, True), ("single", "7", 1, False, True),
        ("too-short", "0042", 5, False, False), ("too-long", "000042", 5, False, False), ("sign", "-0042", 5, False, False),
        ("plus-sign", "+0042", 5, False, False), ("letter", "00a42", 5, False, False), ("space", "0 042", 5, False, False),
        ("empty", "", 5, False, False), ("arabic-indic", chr(0x0663) * 3, 3, False, False),
        ("long-max-19", str(LONG_MAX), 19, False, True), ("above-long-max-19", str(LONG_MAX + 1), 19, False, False),
        ("above-long-max-19-unsigned", str(LONG_MAX + 1), 19, True, True), ("ulong-max-20", str(ULONG_MAX), 20, True, True),
        ("above-ulong-max-20", str(ULONG_MAX + 1), 20, True, False), ("padded-30", "0" * 10 + str(ULONG_MAX), 30, True, True),
        ("padded-30-signed", "0" * 10 + str(ULONG_MAX), 30, False, False), ("padded-30-small", "0" * 29 + "1", 30, False, True),
        ("overflow-with-many-digits", "9" * 30, 30, True, False),
    ]:
        add_read(f"digits/{name}", "digits", raw, intended, {"width": width, "unsigned": unsigned})


def build_decimal_reads():
    params = {"delimiters": "#"}
    for name, raw, intended in [
        ("zero", "0", True), ("integer", "100", True), ("fraction", "12.5", True), ("negative", "-12.5", True), ("small", "0.0001", True),
        ("smallest", "0.0000000000000000000000000001", True), ("max", str(DECIMAL_MAX), True), ("min", str(-DECIMAL_MAX), True),
        ("trailing-zero", "12.50", False), ("trailing-zeros-all", "1.0", False), ("trailing-point", "1.", False), ("leading-point", ".5", False),
        ("negative-zero", "-0", False), ("negative-zero-fraction", "-0.0", False), ("zero-fraction", "0.0", False), ("plus", "+1", False),
        ("leading-zero", "01", False), ("leading-zero-fraction", "00.5", False), ("exponent", "1e3", False), ("exponent-upper", "1E+3", False),
        ("not-a-number", "NaN", False), ("infinity", "Infinity", False), ("empty", "", False), ("minus-only", "-", False),
        ("point-only", ".", False), ("two-points", "1.2.3", False), ("space", " 1", False), ("thousands", "1,000", False),
        ("above-max", "79228162514264337593543950336", False), ("29-fraction-digits", "0." + "0" * 28 + "1", False),
        ("hex", "0x1", False), ("arabic-indic", chr(0x0663), False), ("delimiter", "1#2", False),
    ]:
        add_read(f"decimal/{name}", "decimal", raw, intended, params)
    add_read("decimal/point-delimiter", "decimal", "1.5", False, {"delimiters": "."})


def build_fixed_point_reads():
    for integer_digits, fraction_digits, texts in [
        (4, 2, [
            ("spec-positive", "P0012.50", True), ("spec-negative", "N9987.49", True), ("spec-zero", "P0000.00", True),
            ("all-nines", "N9999.99", False), ("lowest", "N0000.00", True), ("highest", "P9999.99", True), ("smallest-negative", "N9999.98", True),
            ("smallest-positive", "P0000.01", True), ("short-fraction", "P0012.5", False), ("long-fraction", "P0012.500", False),
            ("short-integer", "P012.50", False), ("long-integer", "P00012.50", False), ("comma", "P0012,50", False), ("no-point", "P001250", False),
            ("lowercase-p", "p0012.50", False), ("lowercase-n", "n9987.49", False), ("unknown-sign", "X0012.50", False),
            ("no-sign", "0012.50", False), ("minus-sign", "-0012.50", False), ("plus-sign", "+0012.50", False), ("sign-after", "P-012.50", False),
            ("letter", "P0012.5a", False), ("space", "P0012.50 ", False), ("leading-space", " P0012.50", False), ("empty", "", False),
            ("sign-only", "P", False), ("point-first", "P.001250", False), ("two-points", "P0.12.50", False),
            ("arabic-indic", "P" + chr(0x0660) * 4 + ".50", False), ("point-in-wrong-place", "P001.250", False),
            ("negative-mid", "N8765.43", True), ("negative-one", "N9998.99", True),
        ]),
        (1, 0, [("p0", "P0", True), ("p9", "P9", True), ("n8", "N8", True), ("n0", "N0", True), ("all-nines", "N9", False), ("empty-digits", "P", False),
                ("two-digits", "P10", False), ("with-point", "P1.", False), ("letter", "Pa", False), ("sign-only-n", "N", False)]),
        (10, 0, [("positive", "P1234567890", True), ("negative", "N8765432109", True), ("all-nines", "N9999999999", False),
                 ("short", "P123456789", False), ("lowest", "N0000000000", True), ("zero", "P0000000000", True)]),
        (28, 0, [("max", "P" + "9" * 28, True), ("min", "N" + "0" * 28, True), ("all-nines", "N" + "9" * 28, False), ("zero", "P" + "0" * 28, True),
                 ("short", "P" + "9" * 27, False), ("one", "P" + "0" * 27 + "1", True), ("minus-one", "N" + "9" * 27 + "8", True)]),
        (1, 27, [("max", "P9." + "9" * 27, True), ("min", "N0." + "0" * 27, True), ("all-nines", "N9." + "9" * 27, False), ("zero", "P0." + "0" * 27, True),
                 ("smallest-step", "P0." + "0" * 26 + "1", True), ("negative-smallest-step", "N9." + "9" * 26 + "8", True),
                 ("point-late", "P09" + "." + "9" * 26, False)]),
        (14, 14, [("max", "P" + "9" * 14 + "." + "9" * 14, True), ("zero", "P" + "0" * 14 + "." + "0" * 14, True),
                  ("all-nines", "N" + "9" * 14 + "." + "9" * 14, False), ("min", "N" + "0" * 14 + "." + "0" * 14, True)]),
    ]:
        for name, raw, intended in texts:
            add_read(f"fixed-point/n{integer_digits}.{fraction_digits}/{name}", "fixed_point", raw, intended,
                     {"integer_digits": integer_digits, "fraction_digits": fraction_digits})


def build_timestamp_reads():
    valid = {
        "Year": ["2026", "0001", "9999", "1000"],
        "Month": ["2026-10", "0001-01", "9999-12"],
        "Day": ["2026-10-31", "0001-01-01", "9999-12-31", "2024-02-29"],
        "Hour": ["2026-10-31T12", "0001-01-01T00", "9999-12-31T23"],
        "Minute": ["2026-10-31T12:34", "0001-01-01T00:00", "9999-12-31T23:59"],
        "Second": ["2026-10-31T12:34:56Z", "0001-01-01T00:00:00Z", "9999-12-31T23:59:59Z", "2024-02-29T23:59:59Z"],
        "Millisecond": ["2026-10-31T12:34:56.789Z", "0001-01-01T00:00:00.000Z", "9999-12-31T23:59:59.999Z"],
        "Microsecond": ["2026-10-31T12:34:56.789012Z", "0001-01-01T00:00:00.000000Z", "9999-12-31T23:59:59.999999Z"],
        "Ticks": ["2026-10-31T12:34:56.7890123Z", "0001-01-01T00:00:00.0000000Z", "9999-12-31T23:59:59.9999999Z", "2024-02-29T00:00:00.0000001Z"],
    }
    for time_format, texts in valid.items():
        for text in texts:
            add_read(f"timestamp/{time_format}/valid/{text}", "timestamp", text, True, {"format": time_format})
    full = "2026-10-31T12:34:56.7890123Z"
    for time_format in TIME_FORMATS:
        width = {"Year": 4, "Month": 7, "Day": 10, "Hour": 13, "Minute": 16, "Second": 19, "Millisecond": 23, "Microsecond": 26, "Ticks": 27}[time_format]
        zulu = "Z" if TIME_FORMATS.index(time_format) >= 5 else ""
        good = full[:width] + zulu
        bad = {
            "empty": "",
            "one-byte-short": good[:-1],
            "one-byte-long": good + "0",
            "trailing-space": good + " ",
            "leading-space": " " + good,
            "year-zero": "0000" + good[4:],
            "month-zero": good[:5] + "00" + good[7:] if width >= 7 else None,
            "month-13": good[:5] + "13" + good[7:] if width >= 7 else None,
            "day-zero": good[:8] + "00" + good[10:] if width >= 10 else None,
            "day-32": good[:8] + "32" + good[10:] if width >= 10 else None,
            "february-30": good[:5] + "02-30" + good[10:] if width >= 10 else None,
            "february-29-in-2026": good[:5] + "02-29" + good[10:] if width >= 10 else None,
            "april-31": good[:5] + "04-31" + good[10:] if width >= 10 else None,
            "hour-24": good[:11] + "24" + good[13:] if width >= 13 else None,
            "minute-60": good[:14] + "60" + good[16:] if width >= 16 else None,
            "second-60": good[:17] + "60" + good[19:] if width >= 19 else None,
            "space-instead-of-T": good[:10] + " " + good[11:] if width >= 13 else None,
            "lowercase-t": good[:10] + "t" + good[11:] if width >= 13 else None,
            "slash-instead-of-hyphen": good[:4] + "/" + good[5:] if width >= 7 else None,
            "no-hyphen-before-day": good[:7] + "x" + good[8:] if width >= 10 else None,
            "dot-instead-of-colon": good[:13] + "." + good[14:] if width >= 16 else None,
            "colon-before-seconds": good[:16] + "." + good[17:] if width >= 19 else None,
            "comma-instead-of-point": good[:19] + "," + good[20:] if width >= 23 else None,
            "lowercase-z": good[:-1] + "z" if zulu else None,
            "missing-z": good[:-1] if zulu and width >= 19 else None,
            "z-instead-of-digit": good[:-2] + "Z" if zulu and width >= 23 else None,
            "offset-instead-of-z": good[:-1] + "+00:00" if zulu else None,
            "letter-in-year": "20x6" + good[4:],
            "plus-in-year": "+026" + good[4:],
            "arabic-indic-year": chr(0x0662) + chr(0x0660) + chr(0x0662) + chr(0x0666) + good[4:],
            "z-on-a-format-without-it": good + "Z" if not zulu else None,
        }
        for name, text in bad.items():
            if text is not None:
                add_read(f"timestamp/{time_format}/reject/{name}", "timestamp", text, False, {"format": time_format})
    # Text of another format is never accepted.
    for time_format in TIME_FORMATS:
        for other in TIME_FORMATS:
            if other != time_format:
                width = {"Year": 4, "Month": 7, "Day": 10, "Hour": 13, "Minute": 16, "Second": 19, "Millisecond": 23, "Microsecond": 26, "Ticks": 27}[other]
                text = full[:width] + ("Z" if TIME_FORMATS.index(other) >= 5 else "")
                add_read(f"timestamp/{time_format}/reject/text-of-{other}", "timestamp", text, False, {"format": time_format})


def build_date_reads():
    for name, text, intended in [
        ("spec", "2026-10-31", True), ("min", "0001-01-01", True), ("max", "9999-12-31", True), ("leap-day", "2024-02-29", True),
        ("not-a-leap-year", "2026-02-29", False), ("century-not-leap", "1900-02-29", False), ("400-year-leap", "2000-02-29", True),
        ("month-13", "2026-13-01", False), ("year-zero", "0000-01-01", False), ("compact", "20261031", False), ("slash", "2026/10/31", False),
        ("short", "2026-10-3", False), ("long", "2026-10-311", False), ("with-time", "2026-10-31T00", False), ("empty", "", False),
    ]:
        add_read(f"date/{name}", "date", text, intended, {})


def build_guid_reads():
    lower = "4f9c2a7e-0b1d-4e5f-8a6b-7c8d9e0f1a2b"
    for name, text, intended in [
        ("lowercase", lower, True), ("empty-guid", "00000000-0000-0000-0000-000000000000", True), ("all-f", "ffffffff-ffff-ffff-ffff-ffffffffffff", True),
        ("uppercase", lower.upper(), False), ("one-uppercase-digit", lower[:-1] + "B", False), ("no-hyphens", lower.replace("-", ""), False),
        ("braces", "{" + lower + "}", False), ("parentheses", "(" + lower + ")", False), ("urn", "urn:uuid:" + lower, False),
        ("one-short", lower[:-1], False), ("one-long", lower + "0", False), ("empty", "", False), ("hyphen-moved", lower[:7] + "-" + lower[7] + lower[9:], False),
        ("non-hex", lower[:-1] + "g", False), ("space", lower[:-1] + " ", False), ("trailing-space", lower + " ", False),
        ("arabic-indic", lower[:-1] + chr(0x0663), False), ("hex-format-x", "{0x4f9c2a7e,0x0b1d,0x4e5f,{0x8a,0x6b,0x7c,0x8d,0x9e,0x0f,0x1a,0x2b}}", False),
        ("plus-sign", "+" + lower[1:], False), ("underscore", lower.replace("-", "_"), False),
    ]:
        add_read(f"guid/{name}", "guid", text, intended, {})


def build_formattable_reads():
    params = {"delimiters": "#"}
    for name, text, intended in [
        ("ipv4", "10.0.0.1", True), ("zero", "0.0.0.0", True), ("broadcast", "255.255.255.255", True),
        ("leading-zero", "010.0.0.1", False), ("short-form", "1.1", False), ("too-large", "256.0.0.1", False), ("trailing-space", "10.0.0.1 ", False),
        ("empty", "", False), ("delimiter", "10.0.0.1#2", False), ("hex-octet", "0x10.0.0.1", False), ("text", "host", False),
        ("plus", "+1.0.0.1", False), ("five-parts", "1.2.3.4.5", False), ("long-integer-form", "167772161", False),
    ]:
        add_read(f"formattable/{name}", "ipv4", text, intended, params)
    add_read("formattable/point-delimiter", "ipv4", "10.0.0.1", False, {"delimiters": "."})


# --------------------------------------------------------------------------------------------------------------------------
# Composite keys: templates, with the delimiters derived here.
# --------------------------------------------------------------------------------------------------------------------------


def parse_template(template):
    """Splits "USER#{Id}#X" into ["USER#", ("Id", kind), "#X"]. Placeholders are written {Name:kind}; literals are printable ASCII."""
    parts = []
    for piece in re.split(r"(\{[^}]*\})", template):
        if piece == "":
            continue
        if piece.startswith("{"):
            name, _, spec = piece[1:-1].partition(":")
            parts.append({"placeholder": name, "spec": spec or "string"})
        else:
            parts.append({"literal": piece})
    return parts


def delimiters_of(parts, index):
    """Every character adjacent to the placeholder: the last of the literal before it, the first of the literal after it."""
    found = ""
    if index > 0 and "literal" in parts[index - 1]:
        found += parts[index - 1]["literal"][-1]
    if index + 1 < len(parts) and "literal" in parts[index + 1]:
        found += parts[index + 1]["literal"][0]
    return "".join(sorted(set(found)))


def encode_spec(spec, value, delimiters):
    """Encodes one placeholder. `spec`: string, integer, guid, ms (timestamp), n<i>.<f> (fixed point), d<w> (digits)."""
    if spec == "string":
        return encode_string(units_of(value) if isinstance(value, str) else value, delimiters)
    if spec == "integer":
        return encode_integer(int(value), delimiters)
    if spec == "guid":
        return encode_guid(value)
    if spec == "ms":
        return encode_timestamp(value, "Millisecond")
    if spec.startswith("n"):
        integer_digits, _, fraction_digits = spec[1:].partition(".")
        return encode_fixed_point(value, int(integer_digits), int(fraction_digits or "0"))
    if spec.startswith("d"):
        return encode_digits(int(value), int(spec[1:]))
    raise AssertionError(spec)


def regex_of(parts):
    pattern = b""
    for index, part in enumerate(parts):
        if "literal" in part:
            pattern += re.escape(part["literal"].encode("ascii"))
            continue
        spec = part["spec"]
        delimiters = delimiters_of(parts, index)
        if spec == "string" or spec == "integer":
            excluded = re.escape(delimiters.encode("ascii"))
            pattern += b"([^" + excluded + b"]+)" if delimiters else b"(.+)"
        elif spec == "guid":
            pattern += rb"([0-9a-fA-F-]{36})"
        elif spec == "ms":
            pattern += rb"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z)"
        elif spec.startswith("n"):
            integer_digits, _, fraction_digits = spec[1:].partition(".")
            width = 1 + int(integer_digits) + (1 + int(fraction_digits) if fraction_digits not in ("", "0") else 0)
            pattern += b"(.{" + str(width).encode() + b"})"
        else:
            pattern += b"(.{" + spec[1:].encode() + b"})"
    return re.compile(pattern, re.DOTALL)


def decode_spec(spec, raw, delimiters):
    if spec == "string":
        return decode_string(raw, delimiters)
    if spec == "integer":
        return str(decode_integer(raw, delimiters))
    if spec == "guid":
        return decode_guid(raw)
    if spec == "ms":
        return decode_timestamp(raw, "Millisecond")
    if spec.startswith("n"):
        integer_digits, _, fraction_digits = spec[1:].partition(".")
        return decode_fixed_point(raw, int(integer_digits), int(fraction_digits or "0"))
    return str(decode_digits(raw, int(spec[1:])))


def value_of(spec, value):
    """The JSON form of a placeholder value: UTF-16 code units for text, otherwise as given."""
    return units_of(value) if spec == "string" and isinstance(value, str) else value


def parts_with_delimiters(parts):
    return [dict(p, delimiters=delimiters_of(parts, i)) if "placeholder" in p else p for i, p in enumerate(parts)]


def key_of(parts, segments):
    iterator = iter(segments)
    return b"".join(part["literal"].encode("ascii") if "literal" in part else next(iterator) for part in parts)


def add_composite(case_id, template, values, intended_error=False):
    """A key from a template and the values of its placeholders (in order)."""
    parts = parse_template(template)
    placeholders = [(i, p) for i, p in enumerate(parts) if "placeholder" in p]
    assert len(placeholders) == len(values), case_id
    case = {
        "id": case_id, "template": template, "parts": parts_with_delimiters(parts),
        "values": [value_of(p["spec"], v) for (_, p), v in zip(placeholders, values)],
    }
    segments = []
    try:
        for (index, part), value in zip(placeholders, values):
            segments.append(encode_spec(part["spec"], value, delimiters_of(parts, index)))
    except Rejected:
        if not intended_error:
            sys.exit(f"{case_id}: rejected, but the author expected a key.")
        case["error"] = True
        COMPOSITES.append(case)
        return
    if intended_error:
        sys.exit(f"{case_id}: a key was written, but the author expected an error.")
    key = key_of(parts, segments)
    case["expected_hex"] = key.hex()
    case["expected"] = key.decode("utf-8")
    verdict, _ = accepted_composite(template, key)
    assert verdict, f"{case_id}: the key that was written does not read back."
    COMPOSITES.append(case)


def accepted_composite(template, raw):
    """A key is accepted iff the template's regular expression matches it and re-encoding the decoded segments gives it back."""
    parts = parse_template(template)
    match = regex_of(parts).fullmatch(raw)
    if match is None:
        return False, None
    placeholders = [(i, p) for i, p in enumerate(parts) if "placeholder" in p]
    try:
        values = [decode_spec(p["spec"], group, delimiters_of(parts, i)) for (i, p), group in zip(placeholders, match.groups())]
        segments = [encode_spec(p["spec"], v, delimiters_of(parts, i)) for (i, p), v in zip(placeholders, values)]
    except (Rejected, ValueError, ArithmeticError, UnicodeError):
        return False, None
    return key_of(parts, segments) == raw, values


def add_composite_read(case_id, template, text, intended):
    raw = text.encode("utf-8")
    verdict, _ = accepted_composite(template, raw)
    if verdict != intended:
        sys.exit(f"{case_id}: the round trip says {'accept' if verdict else 'reject'}, the author meant {'accept' if intended else 'reject'}.")
    COMPOSITE_READS.append({"id": case_id, "template": template, "parts": parts_with_delimiters(parse_template(template)),
                            "input_hex": raw.hex(), "text": text, "accept": verdict})


def build_composites():
    ms = timestamp_fields(2026, 10, 31, 12, 34, 56, 7_890_123)
    guid = "4f9c2a7e-0b1d-4e5f-8a6b-7c8d9e0f1a2b"
    add_composite("composite/user-id", "USER#{Id}", ["42"])
    add_composite("composite/user-id-with-delimiter", "USER#{Id}", ["1#ORDER"], True)
    add_composite("composite/user-id-with-delimiter-at-start", "USER#{Id}", ["#1"], True)
    add_composite("composite/user-id-empty", "USER#{Id}", [""], True)
    add_composite("composite/plain-key", "PROFILE", [])
    add_composite("composite/single-placeholder-has-no-delimiters", "{Id}", ["1#2#3"])
    add_composite("composite/single-placeholder-empty", "{Id}", [""], True)
    add_composite("composite/tenant-user", "TENANT#{Tenant}#USER#{Id}", ["acme", "7"])
    add_composite("composite/tenant-injection-in-last", "TENANT#{Tenant}#USER#{Id}", ["acme", "1#USER#2"], True)
    add_composite("composite/tenant-injection-in-first", "TENANT#{Tenant}#USER#{Id}", ["a#USER#b", "1"], True)
    add_composite("composite/country-canton", "COUNTRY#{Country}#CANTON#{Canton}", ["CH", "Z" + U_UMLAUT + "rich"])
    add_composite("composite/spec-order-sort-key", "ORDER#{CreatedAt:ms}#{OrderId:guid}", [ms, guid])
    add_composite("composite/spec-order-guid-uppercase", "ORDER#{CreatedAt:ms}#{OrderId:guid}", [ms, guid.upper()])
    add_composite("composite/total-fixed-point", "{Total:n10.2}", ["-1234.5"])
    add_composite("composite/total-after-literal", "TOTAL#{Total:n10.2}", ["1234.5"])
    add_composite("composite/total-with-too-many-decimals", "TOTAL#{Total:n10.2}", ["1234.567"], True)
    add_composite("composite/mixed-delimiters", "A-{X}#B", ["a b"])
    add_composite("composite/mixed-delimiters-hyphen", "A-{X}#B", ["a-b"], True)
    add_composite("composite/mixed-delimiters-hash", "A-{X}#B", ["a#b"], True)
    add_composite("composite/alphanumeric-delimiter", "v{X}", ["12"])
    add_composite("composite/alphanumeric-delimiter-in-value", "v{X}", ["dev"], True)
    add_composite("composite/integer-negative", "N#{Id:integer}", ["-5"])
    add_composite("composite/integer-negative-with-hyphen-delimiter", "N-{Id:integer}", ["-5"], True)
    add_composite("composite/integer-positive-with-hyphen-delimiter", "N-{Id:integer}", ["5"])
    add_composite("composite/digits", "SEQ#{Seq:d6}#{Name}", ["42", "x"])
    add_composite("composite/digits-too-wide", "SEQ#{Seq:d2}#{Name}", ["420", "x"], True)
    add_composite("composite/three-placeholders", "{A}#{B}#{C}", ["a", "b", "c"])
    add_composite("composite/adjacent-placeholders-share-no-literal", "{A}#{B}", ["a", "b"])
    add_composite("composite/first-of-two-with-delimiter", "{A}#{B}", ["a#", "b"], True)
    add_composite("composite/unicode-segments", "L#{City}#{Name}", [CJK, "a" + GRIN])


def build_composite_reads():
    ms = "2026-10-31T12:34:56.789Z"
    guid = "4f9c2a7e-0b1d-4e5f-8a6b-7c8d9e0f1a2b"
    for name, template, text, intended in [
        ("user-id", "USER#{Id}", "USER#42", True),
        ("user-id-with-second-delimiter", "USER#{Id}", "USER#1#ORDER", False),
        ("user-id-empty", "USER#{Id}", "USER#", False),
        ("user-id-wrong-literal", "USER#{Id}", "ADMIN#42", False),
        ("user-id-prefix-only-literal", "USER#{Id}", "USER", False),
        ("user-id-control-character", "USER#{Id}", "USER#a\x01b", False),
        ("plain-key", "PROFILE", "PROFILE", True),
        ("plain-key-with-suffix", "PROFILE", "PROFILE#", False),
        ("single-placeholder-with-hash", "{Id}", "1#2", True),
        ("single-placeholder-empty", "{Id}", "", False),
        ("tenant-user", "TENANT#{Tenant}#USER#{Id}", "TENANT#acme#USER#7", True),
        ("tenant-user-extra-segment", "TENANT#{Tenant}#USER#{Id}", "TENANT#acme#USER#7#8", False),
        ("tenant-user-shifted-literal", "TENANT#{Tenant}#USER#{Id}", "TENANT#a#USER#b#USER#7", False),
        ("tenant-user-empty-tenant", "TENANT#{Tenant}#USER#{Id}", "TENANT##USER#7", False),
        ("tenant-user-empty-id", "TENANT#{Tenant}#USER#{Id}", "TENANT#acme#USER#", False),
        ("spec-order", "ORDER#{CreatedAt:ms}#{OrderId:guid}", "ORDER#" + ms + "#" + guid, True),
        ("spec-order-uppercase-guid", "ORDER#{CreatedAt:ms}#{OrderId:guid}", "ORDER#" + ms + "#" + guid.upper(), False),
        ("spec-order-no-fraction", "ORDER#{CreatedAt:ms}#{OrderId:guid}", "ORDER#2026-10-31T12:34:56Z#" + guid, False),
        ("spec-order-impossible-date", "ORDER#{CreatedAt:ms}#{OrderId:guid}", "ORDER#2026-02-30T12:34:56.789Z#" + guid, False),
        ("total", "TOTAL#{Total:n10.2}", "TOTAL#P0000001234.50", True),
        ("total-negative", "TOTAL#{Total:n10.2}", "TOTAL#N9999998765.49", True),
        ("total-all-nines", "TOTAL#{Total:n10.2}", "TOTAL#N9999999999.99", False),
        ("mixed-delimiters", "A-{X}#B", "A-a b#B", True),
        ("mixed-delimiters-hyphen-inside", "A-{X}#B", "A-a-b#B", False),
        ("alphanumeric-delimiter", "v{X}", "v12", True),
        ("alphanumeric-delimiter-inside", "v{X}", "vdev", False),
        ("integer", "N#{Id:integer}", "N#-5", True),
        ("integer-plus", "N#{Id:integer}", "N#+5", False),
        ("integer-leading-zero", "N#{Id:integer}", "N#05", False),
        ("digits", "SEQ#{Seq:d6}#{Name}", "SEQ#000042#x", True),
        ("digits-short", "SEQ#{Seq:d6}#{Name}", "SEQ#00042#x", False),
        ("three-placeholders", "{A}#{B}#{C}", "a#b#c", True),
        ("three-placeholders-fourth", "{A}#{B}#{C}", "a#b#c#d", False),
        ("three-placeholders-missing", "{A}#{B}#{C}", "a#b", False),
        ("unicode", "L#{City}#{Name}", "L#" + CJK + "#a" + GRIN, True),
    ]:
        add_composite_read(f"composite-read/{name}", template, text, intended)


# --------------------------------------------------------------------------------------------------------------------------
# Self checks of the grammar, independent of any C# code.
# --------------------------------------------------------------------------------------------------------------------------


def check_grammar():
    """Properties the spec states, checked on the vectors themselves."""
    # Spec examples.
    assert encode_fixed_point("12.5", 4, 2) == b"P0012.50"
    assert encode_fixed_point("-12.5", 4, 2) == b"N9987.49"
    assert encode_fixed_point("0", 4, 2) == b"P0000.00"
    assert encode_timestamp(timestamp_fields(2026, 10, 31, 12, 34, 56, 7_890_000), "Millisecond") == b"2026-10-31T12:34:56.789Z"
    # Formats up to minutes are byte prefixes of the finer ones; Second is not a prefix of Millisecond.
    fields = timestamp_fields(2026, 10, 31, 12, 34, 56, 7_890_123)
    texts = {f: encode_timestamp(fields, f) for f in TIME_FORMATS}
    for coarse in TIME_FORMATS[:5]:
        for fine in TIME_FORMATS[5:]:
            assert texts[fine].startswith(texts[coarse]), (coarse, fine)
    assert not texts["Millisecond"].startswith(texts["Second"])
    # Widths.
    assert [len(texts[f]) for f in TIME_FORMATS] == [4, 7, 10, 13, 16, 20, 24, 27, 28]
    # Fixed width encodings order like the numbers: every neighbouring pair of a dense sample.
    for integer_digits, fraction_digits in [(2, 1), (1, 2), (3, 0)]:
        step = Decimal(1).scaleb(-fraction_digits)
        limit = Decimal(10) ** integer_digits - step
        values = []
        value = -limit
        while value <= limit:
            values.append(value)
            value += step
        encoded = [encode_fixed_point(format(v, "f"), integer_digits, fraction_digits) for v in values]
        assert encoded == sorted(encoded) and len(set(encoded)) == len(encoded), (integer_digits, fraction_digits)
    # Chronological order of timestamps in every format (non-strict: truncation merges neighbours).
    instants = sorted((utc_of(f), f["ticks"]) for _name, f in TIMESTAMP_INSTANTS if _utc_ok(f))
    for time_format in TIME_FORMATS:
        encoded = [time_text(u, t, time_format) for u, t in instants]
        assert encoded == sorted(encoded), time_format


def _utc_ok(fields):
    try:
        utc_of(fields)
        return True
    except Rejected:
        return False


# --------------------------------------------------------------------------------------------------------------------------
# Output.
# --------------------------------------------------------------------------------------------------------------------------


def build():
    check_grammar()
    build_strings()
    build_integers()
    build_digits()
    build_decimals()
    build_fixed_points()
    add_timestamps()
    build_dates_and_guids()
    build_formattable()
    add_write("upper-bound/prefix", "upper_bound", {"prefix": "ORDER#2026-10-31T12:34:56.789Z#"},
              lambda: "ORDER#2026-10-31T12:34:56.789Z#".encode("ascii") + MAX_SCALAR.encode("utf-8"))
    build_string_reads()
    build_integer_reads()
    build_digit_reads()
    build_decimal_reads()
    build_fixed_point_reads()
    build_timestamp_reads()
    build_date_reads()
    build_guid_reads()
    build_formattable_reads()
    build_composites()
    build_composite_reads()
    for collection, label in ((WRITES, "write"), (READS, "read"), (COMPOSITES, "composite"), (COMPOSITE_READS, "composite read")):
        ids = [case["id"] for case in collection]
        if len(set(ids)) != len(ids):
            duplicates = sorted({i for i in ids if ids.count(i) > 1})
            sys.exit(f"Duplicate {label} vector ids: {duplicates[:5]}")
    return {"generator": "generate_key_vectors.py", "write": WRITES, "read": READS, "composite": COMPOSITES,
            "composite_read": COMPOSITE_READS}


def render(document):
    return json.dumps(document, indent=1, ensure_ascii=True) + "\n"


def main():
    arguments = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    arguments.add_argument("--check", action="store_true", help="fail if the committed file differs from the generated one")
    options = arguments.parse_args()

    document = build()
    text = render(document)
    if options.check:
        # Git may check the file out with CRLF.
        current = OUTPUT.read_text(encoding="utf-8").replace("\r\n", "\n") if OUTPUT.exists() else ""
        if current != text:
            sys.exit(f"{OUTPUT.name} is stale: run this script without --check.")
        print(f"{OUTPUT.name} is up to date.")
        return

    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as file:
        file.write(text)
    print(f"Wrote {len(WRITES)} write, {len(READS)} read, {len(COMPOSITES)} composite and {len(COMPOSITE_READS)} composite read vectors "
          f"to {OUTPUT}.")


if __name__ == "__main__":
    main()
