"""LambdaFlow Python example backend.

Mirrors the C# example: same set of tools, same wire-level kinds, plus a
typed-object (Dog) demo.
"""

import lambdaflow as lf


@lf.receive("backend.ping")
def backend_ping(_):
    return {"status": "pong"}


# -- Text utilities -----------------------------------------------------

@lf.receive("uppercase")
def uppercase(req):
    return {"text": req["text"].upper()}


@lf.receive("lowercase")
def lowercase(req):
    return {"text": req["text"].lower()}


@lf.receive("reverse")
def reverse(req):
    return {"text": req["text"][::-1]}


@lf.receive("charcount")
def charcount(req):
    return {"text": str(len(req["text"]))}


@lf.receive("wordcount")
def wordcount(req):
    return {"text": str(len(req["text"].split()))}


@lf.receive("textstats")
def textstats(req):
    text = req["text"]
    normalized = _normalize_letters_and_digits(text)
    return {
        "characters": len(text),
        "charactersNoSpaces": _count_non_whitespace(text),
        "words": len(text.split()),
        "lines": _line_count(text),
        "isPalindrome": _is_palindrome(normalized),
        "normalized": normalized,
    }


# -- Number conversion --------------------------------------------------

@lf.receive("numberconverter")
def number_converter(req):
    mode  = req["mode"]
    value = req["value"]
    try:
        if mode == "dec2hex":
            return {"text": format(int(value), "X")}
        if mode == "hex2dec":
            return {"text": str(int(value, 16))}
        if mode == "dec2bin":
            return {"text": bin(int(value))[2:]}
        if mode == "bin2dec":
            return {"text": str(int(value, 2))}
    except ValueError:
        return {"text": f"Invalid {mode.split('2')[0]} number"}
    return {"text": "Invalid mode"}


# -- Typed-object demo --------------------------------------------------

@lf.receive("describeDog")
def describe_dog(dog):
    return lf.entity("animals.dogDescription", {
        "greeting":      f"Hi! I'm {dog['name']}, a {dog['age']}-year-old {dog['breed']}.",
        "ageInDogYears": dog["age"] * 7,
        "isPuppy":       dog["age"] < 1,
    })


def _count_non_whitespace(text):
    return sum(1 for ch in text if not ch.isspace())


def _line_count(text):
    if not text:
        return 0
    return text.replace("\r\n", "\n").count("\n") + 1


def _normalize_letters_and_digits(text):
    return "".join(ch.lower() for ch in text if ch.isalnum())


def _is_palindrome(normalized):
    if not normalized:
        return False
    return normalized == normalized[::-1]


if __name__ == "__main__":
    lf.run()
