#!/usr/bin/env python3
"""
Standalone content validator for puzzle_templates.json. Does not touch
Unity, does not modify the file. Run it after any content edit:

    python3 validate_puzzle_templates.py path/to/puzzle_templates.json

It reports, per template:
  - unknown/unlisted knowledgeComponent
  - constructs present that are out of scope for that KC (e.g. an "if" in a
    variables/basic_operations template, or a "def" anywhere -- functions
    aren't one of the six KCs at all)
  - variableName that never actually appears in codeLines
  - bugLineIndex out of range (a negative value is fine -- it just means
    "let the format pick randomly")
  - correctOrder that isn't a valid permutation of the line indices
  - for LineScramble templates: how many valid orderings the dependency
    checker (mirroring LineScramblePuzzleFormat.cs's logic) finds, flagging
    the degenerate case where every line is mutually independent (which
    makes the scramble trivial regardless of the order-checking fix)

ALLOWED_CONSTRUCTS below encodes a judgment call: whether a "for" loop
containing a plain "if" should count as a loops-KC violation. As shipped
this treats each KC's templates as a "pure" demonstration of that KC, since
that's what motivated this script (an if-statement showing up in a
variables/basic_operations template). If that's too strict, e.g. loops
containing a simple embedded conditional is content you actually want,
loosen ALLOWED_CONSTRUCTS["loops"] yourself; it's a content-design decision,
not something this script can decide for you.
"""

import json
import re
import sys
from itertools import permutations

VALID_KCS = {
    "print_statements", "variables", "input_handling",
    "conditionals", "loops", "basic_operations",
}

# Unity's JsonUtility serializes C# enums as their underlying int, not their
# name, so puzzleType/difficulty show up as plain numbers in the JSON. These
# mappings mirror the enum declarations in PuzzleTemplate.cs exactly -- if
# that enum ever gets reordered or extended, update these too.
PUZZLE_TYPE_NAMES = {
    0: "FillInTheBlank", 1: "SpotTheBug", 2: "LineScramble",
    3: "TrueOrFalse", 4: "PredictTheOutput", 5: "PairACode",
}
DIFFICULTY_NAMES = {0: "Beginner", 1: "Intermediate", 2: "Advanced"}

# Construct -> substring(s) that indicate its presence in a code line.
CONSTRUCT_MARKERS = {
    "if": ["if "],
    "elif": ["elif "],
    "else": ["else"],
    "for": ["for "],
    "while": ["while "],
    "def": ["def "],
    "input": ["input("],
}

# Which constructs each KC's templates are allowed to contain. "def" is
# deliberately absent from every list: functions are not one of the six KCs
# at all, so no template should contain one regardless of tag.
ALLOWED_CONSTRUCTS = {
    "print_statements": set(),
    "variables": set(),
    "basic_operations": set(),
    "input_handling": {"input"},
    "conditionals": {"if", "elif", "else"},
    "loops": {"for", "while"},
}

PY_KEYWORDS = {"print", "input", "int", "str", "len", "range", "True", "False", "None",
               "if", "elif", "else", "for", "while", "in", "and", "or", "not", "def", "return"}


def load_templates(path):
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    if isinstance(data, dict) and "templates" in data:
        return data["templates"]
    if isinstance(data, list):
        return data
    raise ValueError("Unrecognized JSON shape: expected {'templates': [...]} or a bare list")


def check_scope(template, errors):
    kc = template.get("knowledgeComponent", "")
    if kc not in VALID_KCS:
        errors.append(f"unknown knowledgeComponent '{kc}'")
        return

    allowed = ALLOWED_CONSTRUCTS.get(kc, set())
    code_lines = template.get("codeLines", [])
    for construct, markers in CONSTRUCT_MARKERS.items():
        if construct in allowed:
            continue
        for line in code_lines:
            if any(marker in line for marker in markers):
                errors.append(
                    f"KC '{kc}' should not contain '{construct.strip()}' "
                    f"(found in line: {line!r})"
                )
                break


def check_variable_consistency(template, errors):
    var_name = template.get("variableName", "")
    if not var_name:
        return
    code_lines = template.get("codeLines", [])
    if not any(var_name in line for line in code_lines):
        errors.append(
            f"variableName '{var_name}' does not appear in any codeLines "
            f"(template text and metadata have drifted apart)"
        )


def check_bug_line_index(template, errors):
    if "bugLineIndex" not in template:
        return
    idx = template["bugLineIndex"]
    n = len(template.get("codeLines", []))
    if idx < 0:
        return  # explicit "let the format choose" sentinel, fine
    if idx >= n:
        errors.append(f"bugLineIndex {idx} is out of range for {n} codeLines")


def check_correct_order(template, errors):
    order = template.get("correctOrder", [])
    if not order:
        return
    n = len(template.get("codeLines", []))
    if sorted(order) != list(range(n)):
        errors.append(
            f"correctOrder {order} is not a valid permutation of 0..{n - 1}"
        )


def strip_string_literals(line):
    return re.sub(r"'[^']*'|\"[^\"]*\"", "", line)


def extract_defines(line):
    trimmed = line.strip()
    m = re.match(r'^for\s+(\w+)\s+in\s+', trimmed)
    if m:
        return [m.group(1)]
    m = re.match(r'^(\w+)\s*=(?!=)', trimmed)
    if m:
        return [m.group(1)]
    return []


def extract_uses(line):
    trimmed = line.strip()
    search_scope = trimmed
    m = re.match(r'^\w+\s*=(?!=)(.+)$', trimmed)
    if m:
        search_scope = m.group(1)
    m2 = re.match(r'^for\s+\w+\s+in\s+(.+):$', trimmed)
    if m2:
        search_scope = m2.group(1)
    search_scope = strip_string_literals(search_scope)
    result = []
    for tok in re.findall(r'\b[a-zA-Z_]\w*\b', search_scope):
        if tok in PY_KEYWORDS or tok in result:
            continue
        result.append(tok)
    return result


def count_valid_orders(code_lines, cap=200):
    """Mirrors LineScramblePuzzleFormat.cs's BuildMustPrecedePairs +
    IsValidDependencyOrder. Brute-forces all permutations, capped, since
    templates are short (a handful of lines) -- fine for offline validation,
    would NOT be fine at runtime in Unity, which is why the C# side uses
    the pairwise-constraint check directly instead of enumerating orders."""
    n = len(code_lines)
    if n > 7:
        return None  # 7! = 5040, still fine, but guard against surprises
    defs = [set(extract_defines(l)) for l in code_lines]
    uses = [set(extract_uses(l)) for l in code_lines]
    pairs = []
    for i in range(n):
        for j in range(i + 1, n):
            if (defs[i] & (uses[j] | defs[j])) or (uses[i] & defs[j]):
                pairs.append((i, j))
    count = 0
    for perm in permutations(range(n)):
        pos = {row: idx for idx, row in enumerate(perm)}
        if all(pos[i] < pos[j] for (i, j) in pairs):
            count += 1
        if count > cap:
            return count
    return count


def check_line_scramble(template, infos):
    if template.get("puzzleType") != 2:  # 2 == LineScramble, see PUZZLE_TYPE_NAMES
        return
    code_lines = template.get("codeLines", [])
    n = len(code_lines)
    if n < 2:
        return
    total = count_valid_orders(code_lines)
    if total is None:
        infos.append(f"LineScramble template has {n} lines, skipped combinatorial check (too many permutations)")
        return
    if total == 1:
        return  # strict chain, exactly the "one right answer" case, nothing to flag
    if total >= n:  # heuristic: a lot of freedom relative to line count
        infos.append(
            f"LineScramble template has {total} valid orderings out of {n} lines -- "
            f"if that's ALL lines mutually independent, the scramble may feel trivial "
            f"even with the order-checking fix; consider whether this template still "
            f"tests sequencing understanding"
        )
    else:
        infos.append(f"LineScramble template has {total} valid orderings (expected with the new order-aware checker)")


def describe(t):
    kc = t.get("knowledgeComponent")
    pt = PUZZLE_TYPE_NAMES.get(t.get("puzzleType"), f"unknown({t.get('puzzleType')})")
    diff = DIFFICULTY_NAMES.get(t.get("difficulty"), f"unknown({t.get('difficulty')})")
    return f"kc={kc}, type={pt}, difficulty={diff}"


def print_bucket_counts(templates):
    """Counts templates per (KC, difficulty, puzzleType) bucket, since that's
    the actual unit PCGEngine draws from at runtime. A KC can look
    well-covered in total while individual buckets are down to 1-2
    templates, which is what actually drives repetition regardless of how
    good the mutation logic is."""
    buckets = {}
    for t in templates:
        kc = t.get("knowledgeComponent", "?")
        pt = PUZZLE_TYPE_NAMES.get(t.get("puzzleType"), "?")
        diff = DIFFICULTY_NAMES.get(t.get("difficulty"), "?")
        key = (kc, diff, pt)
        buckets[key] = buckets.get(key, 0) + 1

    print("=== Templates per (KC, difficulty, puzzleType) bucket ===")
    thin = []
    for key in sorted(buckets.keys()):
        count = buckets[key]
        flag = "  <-- thin" if count <= 2 else ""
        print(f"  {key[0]:<18} {key[1]:<13} {key[2]:<16} {count}{flag}")
        if count <= 2:
            thin.append((key, count))

    all_kcs = {"print_statements", "variables", "input_handling", "conditionals", "loops", "basic_operations"}
    all_diffs = ["Beginner", "Intermediate", "Advanced"]
    all_types = list(PUZZLE_TYPE_NAMES.values())
    missing = []
    for kc in sorted(all_kcs):
        for diff in all_diffs:
            for pt in all_types:
                if (kc, diff, pt) not in buckets:
                    missing.append((kc, diff, pt))

    print(f"\n{len(thin)} bucket(s) with 1-2 templates (repeat-avoidance can't help much here)")
    print(f"{len(missing)} bucket(s) with ZERO templates out of {len(all_kcs) * len(all_diffs) * len(all_types)} possible")
    if missing:
        print("(PCGEngine falls back to ignoring puzzleType, then ignoring difficulty, for these -- "
              "meaning the actual difficulty/format the player sees may not match what was requested)")
    print()


def main():
    if len(sys.argv) < 2:
        print("Usage: python3 validate_puzzle_templates.py path/to/puzzle_templates.json")
        sys.exit(1)

    templates = load_templates(sys.argv[1])
    print(f"Loaded {len(templates)} templates\n")

    print_bucket_counts(templates)

    total_errors = 0
    for t in templates:
        tid = t.get("id", "<no id>")
        errors = []
        infos = []

        check_scope(t, errors)
        check_variable_consistency(t, errors)
        check_bug_line_index(t, errors)
        check_correct_order(t, errors)
        check_line_scramble(t, infos)

        if errors:
            total_errors += len(errors)
            print(f"[ERROR] {tid} ({describe(t)})")
            for e in errors:
                print(f"    - {e}")
        if infos:
            print(f"[INFO]  {tid} ({describe(t)})")
            for i in infos:
                print(f"    - {i}")

    print(f"\n{total_errors} error(s) across {len(templates)} templates")
    sys.exit(1 if total_errors else 0)


if __name__ == "__main__":
    main()
