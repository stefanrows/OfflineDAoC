#!/usr/bin/env python3
"""Conservative MySQL/MariaDB -> SQLite converter for the gaheris ToA migrations.

The gaheris overlay (Pactor/gaheris, sql/) writes its Atlantis and Master Level
migrations for the stock OpenDAoC MySQL database. Our runtime database is
SQLite. This tool rewrites those files statement by statement, in their
original order, and changes only what SQLite cannot run as written:

* backtick identifiers become double-quoted identifiers;
* MySQL double-quoted strings become single-quoted strings, and MySQL backslash
  escapes inside strings are decoded and re-quoted SQLite style;
* ``INSERT ... ON DUPLICATE KEY UPDATE a = VALUES(a)`` becomes
  ``ON CONFLICT DO UPDATE SET a = excluded.a``; a clause that only assigns a
  column to itself (``x = x``) becomes ``ON CONFLICT DO NOTHING``;
* ``INSERT IGNORE`` / ``REPLACE INTO`` become ``INSERT OR IGNORE`` /
  ``INSERT OR REPLACE``;
* ``UUID()`` becomes a deterministic UUID literal (uuid5 of source file,
  statement number and occurrence), so a re-run produces the same ids;
  ``--uuid-mode expr`` emits a random SQLite expression instead;
* a few MySQL functions (NOW, CONCAT, IF, LCASE, ...) are mapped;
  ``TRUE``/``FALSE`` become ``1``/``0``;
* MySQL session statements (``SET``, ``COMMIT``, ``START TRANSACTION``,
  ``LOCK TABLES`` ...) are kept as comments only: the caller owns the
  transaction;
* explicit, documented schema rules (``TABLE_RENAMES``, ``DROP_COLUMN_RULES``) drop
  columns our schema does not have. Rules never invent values.

Anything else is passed through unchanged and listed as a warning, so the
probe (probe_import.py) shows the real SQLite error instead of a silent guess.

Usage:
    python3 tools/toa/convert_mysql_to_sqlite.py \
        --src /mnt/c/OfflineDAoC-ext/gaheris/sql --out tools/toa/sql
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import uuid
from dataclasses import dataclass, field
from pathlib import Path

CONVERTER_VERSION = "1"

# The gaheris migrations WP0.3 converts, in apply order.
DEFAULT_FILES = [
    "12-master-levels.sql",
    "13-atlantis-mobs.sql",
    "14-atlantis-travel.sql",
    "28-atlantis-portals.sql",
    "29-atlantis-zonepoints.sql",
    "35-atlantis-npcs.sql",
    "36-artifact-scrolls.sql",
    "39-master-level-paths.sql",
    "42-master-levels-gaps.sql",
    "43-master-levels-corrected.sql",
]

UUID_NAMESPACE = uuid.uuid5(uuid.NAMESPACE_URL, "offlinedaoc:toa-import:gaheris")

# MySQL table name (lower case) -> our SQLite table name. SQLite matches
# table names case-insensitively, so only real renames belong here.
TABLE_RENAMES: dict[str, str] = {}

# (table lower, column lower) -> (probe round that added the rule, reason).
# The column is removed from INSERT column lists (with its value in every
# row), from UPDATE SET lists and from ON CONFLICT assignments. Only for
# columns our schema does not have; never used to invent a value.
# --rules-round N applies the rules added up to round N (0 = none), so every
# probe round in docs/TOA_IMPORT_PROBE.md can be reproduced.
DROP_COLUMN_RULES: dict[tuple[str, str], tuple[int, str]] = {
    # Round 1: 74 LineXSpell inserts (files 12, 42, 43) failed with "table
    # linexspell has no column named PackageID". gaheris only tags its rows
    # ('gaheris', 'gaheris-ml'); its own DELETEs key on LineName/SpellID, and
    # no server code reads LineXSpell.PackageID.
    ("linexspell", "packageid"): (1, "our LineXSpell has no PackageID column; gaheris uses it only as a row tag"),
}


def drop_columns_for_round(max_round: int | None) -> dict[tuple[str, str], str]:
    return {k: reason for k, (rnd, reason) in DROP_COLUMN_RULES.items() if max_round is None or rnd <= max_round}

SESSION_STATEMENTS = {"SET", "COMMIT", "START", "BEGIN", "ROLLBACK", "LOCK", "UNLOCK", "USE"}

SIMPLE_FUNCTION_MAP = {
    "NOW": "datetime('now')",
    "SYSDATE": "datetime('now')",
    "CURRENT_TIMESTAMP": "CURRENT_TIMESTAMP",
    "UTC_TIMESTAMP": "datetime('now')",
    "CURDATE": "date('now')",
    "UNIX_TIMESTAMP": "CAST(strftime('%s','now') AS INTEGER)",
}
RENAMED_FUNCTIONS = {"LCASE": "lower", "UCASE": "upper", "GREATEST": "max", "LEAST": "min", "IF": "iif", "RAND": "random"}
UNSUPPORTED_FUNCTIONS = {"SUBSTRING_INDEX", "DATE_FORMAT", "FROM_UNIXTIME", "LAST_INSERT_ID", "CONCAT_WS", "FIND_IN_SET", "ISNULL"}


class ConversionError(Exception):
    pass


@dataclass
class Token:
    kind: str  # ws, comment, string, ident, word, number, punct, semi
    text: str
    value: str | None = None  # decoded string / identifier value

    @property
    def significant(self) -> bool:
        return self.kind not in ("ws", "comment")

    def upper(self) -> str:
        return self.text.upper() if self.kind == "word" else ""


_MYSQL_ESCAPES = {"0": "\0", "'": "'", '"': '"', "b": "\b", "n": "\n", "r": "\r", "t": "\t", "Z": "\x1a", "\\": "\\"}


def _read_quoted(sql: str, i: int, quote: str, backslash: bool) -> tuple[str, int]:
    """Return (decoded value, index after closing quote) for a quoted run at i."""
    out = []
    j = i + 1
    n = len(sql)
    while j < n:
        c = sql[j]
        if backslash and c == "\\" and j + 1 < n:
            nxt = sql[j + 1]
            if nxt in ("%", "_"):
                out.append("\\" + nxt)  # MySQL keeps these two literally
            else:
                out.append(_MYSQL_ESCAPES.get(nxt, nxt))
            j += 2
            continue
        if c == quote:
            if j + 1 < n and sql[j + 1] == quote:
                out.append(quote)
                j += 2
                continue
            return "".join(out), j + 1
        out.append(c)
        j += 1
    raise ConversionError(f"unterminated {quote} quote starting at offset {i}")


_TWO_CHAR_OPS = {"<=", ">=", "<>", "!=", "||", "==", "<<", ">>", ":="}


def tokenize(sql: str, dialect: str = "mysql") -> list[Token]:
    """Split SQL into tokens. dialect 'mysql' or 'sqlite' (affects quotes, #)."""
    mysql = dialect == "mysql"
    tokens: list[Token] = []
    i, n = 0, len(sql)
    while i < n:
        c = sql[i]
        if c in " \t\r\n\f":
            j = i
            while j < n and sql[j] in " \t\r\n\f":
                j += 1
            tokens.append(Token("ws", sql[i:j]))
            i = j
        elif sql.startswith("--", i) and (not mysql or i + 2 >= n or sql[i + 2] in " \t\r\n"):
            j = sql.find("\n", i)
            j = n if j < 0 else j
            tokens.append(Token("comment", sql[i:j]))
            i = j
        elif mysql and c == "#":
            j = sql.find("\n", i)
            j = n if j < 0 else j
            tokens.append(Token("comment", "--" + sql[i + 1:j]))
            i = j
        elif sql.startswith("/*", i):
            j = sql.find("*/", i + 2)
            if j < 0:
                raise ConversionError(f"unterminated block comment at offset {i}")
            text = sql[i:j + 2]
            tokens.append(Token("execcomment" if mysql and text.startswith("/*!") else "comment", text))
            i = j + 2
        elif c == "'":
            value, j = _read_quoted(sql, i, "'", backslash=mysql)
            tokens.append(Token("string", sql[i:j], value))
            i = j
        elif c == '"':
            value, j = _read_quoted(sql, i, '"', backslash=mysql)
            tokens.append(Token("string" if mysql else "ident", sql[i:j], value))
            i = j
        elif c == "`":
            value, j = _read_quoted(sql, i, "`", backslash=False)
            tokens.append(Token("ident", sql[i:j], value))
            i = j
        elif c == "[" and not mysql:
            j = sql.find("]", i)
            if j < 0:
                raise ConversionError(f"unterminated [ident] at offset {i}")
            tokens.append(Token("ident", sql[i:j + 1], sql[i + 1:j]))
            i = j + 1
        elif c == ";":
            tokens.append(Token("semi", ";"))
            i += 1
        elif c.isalpha() or c == "_" or c == "@":
            j = i + 1
            while j < n and (sql[j].isalnum() or sql[j] in "_$@"):
                j += 1
            tokens.append(Token("word", sql[i:j]))
            i = j
        elif c.isdigit() or (c == "." and i + 1 < n and sql[i + 1].isdigit()):
            m = re.compile(r"0[xX][0-9a-fA-F]+|\d*\.?\d+(?:[eE][+-]?\d+)?").match(sql, i)
            assert m is not None
            tokens.append(Token("number", m.group(0)))
            i = m.end()
        else:
            two = sql[i:i + 2]
            if two in _TWO_CHAR_OPS:
                tokens.append(Token("punct", two))
                i += 2
            else:
                tokens.append(Token("punct", c))
                i += 1
    return tokens


@dataclass
class Statement:
    tokens: list[Token]  # leading trivia + body (+ trailing semi if any)

    def sig(self) -> list[int]:
        return [k for k, t in enumerate(self.tokens) if t.significant]

    @property
    def has_body(self) -> bool:
        return any(t.significant for t in self.tokens)


def split_statements(tokens: list[Token]) -> list[Statement]:
    """Split tokens at top-level semicolons. Trailing trivia becomes its own entry."""
    out: list[Statement] = []
    cur: list[Token] = []
    for t in tokens:
        cur.append(t)
        if t.kind == "semi":
            out.append(Statement(cur))
            cur = []
    if cur:
        out.append(Statement(cur))
    return out


def sqlite_string(value: str) -> str:
    if "\0" in value:
        raise ConversionError("NUL byte inside a string literal")
    return "'" + value.replace("'", "''") + "'"


def sqlite_ident(value: str) -> str:
    return '"' + value.replace('"', '""') + '"'


def ident_name(t: Token) -> str | None:
    if t.kind == "ident":
        return t.value
    if t.kind == "word":
        return t.text
    return None


def render_default(t: Token) -> str:
    if t.kind == "string":
        return sqlite_string(t.value or "")
    if t.kind == "ident":
        return sqlite_ident(t.value or "")
    if t.kind == "word" and t.text.upper() in ("TRUE", "FALSE"):
        return "1" if t.text.upper() == "TRUE" else "0"
    return t.text


@dataclass
class FileReport:
    source: str
    statements: int = 0
    session_dropped: int = 0
    inserts: int = 0
    insert_rows: int = 0
    upserts_update: int = 0
    upserts_nothing: int = 0
    uuid_literals: int = 0
    columns_dropped: dict[str, int] = field(default_factory=dict)
    tables: dict[str, int] = field(default_factory=dict)
    warnings: list[str] = field(default_factory=list)

    def warn(self, msg: str) -> None:
        if msg not in self.warnings:
            self.warnings.append(msg)


class StatementConverter:
    def __init__(self, stmt: Statement, source_name: str, stmt_no: int, uuid_mode: str, report: FileReport,
                 drop_columns: dict[tuple[str, str], str], table_renames: dict[str, str]):
        self.toks = stmt.tokens
        self.sig = stmt.sig()
        self.repl: dict[int, str] = {}
        self.source_name = source_name
        self.stmt_no = stmt_no
        self.uuid_mode = uuid_mode
        self.uuid_count = 0
        self.report = report
        self.drop_columns = drop_columns
        self.table_renames = table_renames

    # -- helpers ---------------------------------------------------------
    def word(self, s: int) -> str:
        if s >= len(self.sig):
            return ""
        return self.toks[self.sig[s]].upper()

    def tok(self, s: int) -> Token:
        return self.toks[self.sig[s]]

    def match_paren(self, s: int) -> int:
        """s indexes sig and points at '('. Return sig index of matching ')'."""
        depth = 0
        for k in range(s, len(self.sig)):
            t = self.tok(k)
            if t.kind == "punct" and t.text == "(":
                depth += 1
            elif t.kind == "punct" and t.text == ")":
                depth -= 1
                if depth == 0:
                    return k
        raise ConversionError(f"unbalanced parenthesis in statement {self.stmt_no}")

    def split_list(self, lo: int, hi: int) -> list[tuple[int, int]]:
        """Split sig range [lo, hi) at depth-0 commas -> list of (first, last) sig idx."""
        items, depth, start = [], 0, lo
        for k in range(lo, hi):
            t = self.tok(k)
            if t.kind == "punct" and t.text == "(":
                depth += 1
            elif t.kind == "punct" and t.text == ")":
                depth -= 1
            elif depth == 0 and t.kind == "punct" and t.text == ",":
                items.append((start, k - 1))
                start = k + 1
        if start <= hi - 1:
            items.append((start, hi - 1))
        return items

    def delete_item(self, items: list[tuple[int, int]], idx: int) -> None:
        """Blank out list item idx including one adjacent comma and whitespace."""
        first, last = items[idx]
        if idx + 1 < len(items):
            a, b = self.sig[first], self.sig[items[idx + 1][0]]  # up to next item
        elif idx > 0:
            a, b = self.sig[items[idx - 1][1]] + 1, self.sig[last] + 1  # from prev item end
        else:
            a, b = self.sig[first], self.sig[last] + 1
        for k in range(a, b):
            self.repl[k] = ""

    def blank_word(self, s: int) -> None:
        """Remove a keyword token and the whitespace that follows it."""
        k = self.sig[s]
        self.repl[k] = ""
        if k + 1 < len(self.toks) and self.toks[k + 1].kind == "ws":
            self.repl[k + 1] = ""

    def replace_range(self, first_sig: int, last_sig: int, text: str) -> None:
        a, b = self.sig[first_sig], self.sig[last_sig]
        self.repl[a] = text
        for k in range(a + 1, b + 1):
            self.repl[k] = ""

    def table_at(self, s: int) -> str:
        t = self.tok(s)
        name = ident_name(t)
        if name is None:
            raise ConversionError(f"expected a table name in statement {self.stmt_no}, got {t.text!r}")
        if self.word(s + 1) == "" and s + 1 < len(self.sig) and self.tok(s + 1).text == ".":
            raise ConversionError(f"schema-qualified table in statement {self.stmt_no} is not supported")
        target = self.table_renames.get(name.lower(), name)
        self.repl[self.sig[s]] = sqlite_ident(target)
        self.report.tables[target.lower()] = self.report.tables.get(target.lower(), 0) + 1
        return target

    def new_uuid(self) -> str:
        self.uuid_count += 1
        self.report.uuid_literals += 1
        if self.uuid_mode == "expr":
            return ("lower(hex(randomblob(4))||'-'||hex(randomblob(2))||'-4'||substr(hex(randomblob(2)),2)"
                    "||'-'||substr('89ab',1+(abs(random())%4),1)||substr(hex(randomblob(2)),2)||'-'||hex(randomblob(6)))")
        u = uuid.uuid5(UUID_NAMESPACE, f"{self.source_name}:{self.stmt_no}:{self.uuid_count}")
        return sqlite_string(str(u))

    # -- passes ----------------------------------------------------------
    def functions_pass(self, lo: int, hi: int, values_to_excluded: bool = False) -> None:
        k = lo
        while k < hi:
            t = self.tok(k)
            nxt = self.tok(k + 1) if k + 1 < hi else None
            if t.kind == "word" and nxt is not None and nxt.kind == "punct" and nxt.text == "(":
                name = t.text.upper()
                close = self.match_paren(k + 1)
                if name == "UUID" and close == k + 2:
                    self.replace_range(k, close, self.new_uuid())
                    k = close + 1
                    continue
                if name in SIMPLE_FUNCTION_MAP and close == k + 2:
                    self.replace_range(k, close, SIMPLE_FUNCTION_MAP[name])
                    k = close + 1
                    continue
                if name == "VALUES" and values_to_excluded:
                    inner = self.split_list(k + 2, close)
                    if len(inner) != 1 or inner[0][0] != inner[0][1]:
                        raise ConversionError(f"VALUES() with an expression in statement {self.stmt_no}")
                    col = ident_name(self.tok(inner[0][0]))
                    self.replace_range(k, close, "excluded." + sqlite_ident(col or ""))
                    k = close + 1
                    continue
                if name in RENAMED_FUNCTIONS:
                    self.repl[self.sig[k]] = RENAMED_FUNCTIONS[name]
                elif name == "CONCAT":
                    args = self.split_list(k + 2, close)
                    rendered = [self.render_sig_range(a, b) for a, b in args]
                    self.replace_range(k, close, "(" + " || ".join(rendered) + ")")
                    k = close + 1
                    continue
                elif name in UNSUPPORTED_FUNCTIONS:
                    self.report.warn(f"statement {self.stmt_no}: MySQL function {name}() left as is")
            k += 1

    def render_sig_range(self, a: int, b: int) -> str:
        lo, hi = self.sig[a], self.sig[b]
        return "".join(self.repl.get(i, render_default(self.toks[i])) for i in range(lo, hi + 1))

    def odku_pass(self, table: str, on_idx: int, end: int) -> None:
        """Rewrite ON DUPLICATE KEY UPDATE ... (sig on_idx .. end-1)."""
        assign_lo = on_idx + 4
        items = self.split_list(assign_lo, end)
        self.functions_pass(assign_lo, end, values_to_excluded=True)
        kept, all_self = [], True
        for first, last in items:
            eq = next((k for k in range(first, last + 1) if self.tok(k).kind == "punct" and self.tok(k).text == "="), None)
            if eq is None or eq != first + 1:
                raise ConversionError(f"cannot parse ON DUPLICATE KEY assignment in statement {self.stmt_no}")
            col = ident_name(self.tok(first)) or ""
            if (table.lower(), col.lower()) in self.drop_columns:
                self.report.columns_dropped[f"{table}.{col}"] = self.report.columns_dropped.get(f"{table}.{col}", 0) + 1
                continue
            rhs_self = last == eq + 1 and (ident_name(self.tok(last)) or "").lower() == col.lower()
            all_self = all_self and rhs_self
            kept.append((first, last))
        if not kept or all_self:
            self.replace_range(on_idx, end - 1, "ON CONFLICT DO NOTHING")
            self.report.upserts_nothing += 1
            return
        parts = []
        for first, last in kept:
            parts.append(self.render_sig_range(first, last))
        self.replace_range(on_idx, end - 1, "ON CONFLICT DO UPDATE SET " + ", ".join(p.strip() for p in parts))
        self.report.upserts_update += 1

    def convert_insert(self, end: int) -> None:
        verb = self.word(0)
        conflict = " OR REPLACE" if verb == "REPLACE" else ""
        modifiers = {"IGNORE", "LOW_PRIORITY", "DELAYED", "HIGH_PRIORITY"}
        k = 1
        while self.word(k) in modifiers:
            if self.word(k) == "IGNORE":
                conflict = " OR IGNORE"
            self.blank_word(k)
            k += 1
        if self.word(k) == "INTO":
            self.repl[self.sig[0]] = "INSERT" + conflict
            k += 1
        else:  # MySQL allows "INSERT tbl ..." without INTO
            self.repl[self.sig[0]] = "INSERT" + conflict + " INTO"
        table = self.table_at(k)
        k += 1
        cols: list[str] = []
        col_items: list[tuple[int, int]] = []
        if k < end and self.tok(k).text == "(":
            close = self.match_paren(k)
            col_items = self.split_list(k + 1, close)
            cols = [ident_name(self.tok(a)) or "" for a, _ in col_items]
            k = close + 1
        drop_idx = [i for i, c in enumerate(cols) if (table.lower(), c.lower()) in self.drop_columns]
        on_idx = next((j for j in range(k, end) if self.word(j) == "ON" and self.word(j + 1) == "DUPLICATE"), None)
        body_end = on_idx if on_idx is not None else end
        self.report.inserts += 1
        if self.word(k) in ("VALUES", "VALUE"):
            self.repl[self.sig[k]] = "VALUES"
            k += 1
            tuples = []
            while k < body_end:
                if self.tok(k).text != "(":
                    raise ConversionError(f"expected a row tuple in statement {self.stmt_no}")
                close = self.match_paren(k)
                tuples.append((k, close))
                k = close + 1
                if k < body_end and self.tok(k).text == ",":
                    k += 1
            self.report.insert_rows += len(tuples)
            for lo, hi in tuples:
                vals = self.split_list(lo + 1, hi)
                if cols and len(vals) != len(cols):
                    raise ConversionError(f"row with {len(vals)} values for {len(cols)} columns in statement {self.stmt_no}")
                self.functions_pass(lo + 1, hi)
                for i in reversed(drop_idx):
                    self.delete_item(vals, i)
        elif self.word(k) == "SELECT":
            self.functions_pass(k, body_end)
            if on_idx is not None and not any(self.word(j) == "WHERE" for j in range(k, body_end)):
                last = body_end - 1
                self.repl[self.sig[last]] = self.render_sig_range(last, last) + " WHERE true"
            if drop_idx:
                raise ConversionError(f"column drop on INSERT ... SELECT in statement {self.stmt_no} is not supported")
        elif self.word(k) == "SET":
            raise ConversionError(f"INSERT ... SET in statement {self.stmt_no} is not supported")
        else:
            raise ConversionError(f"unexpected token after INSERT column list in statement {self.stmt_no}")
        for i in reversed(drop_idx):
            key = f"{table}.{cols[i]}"
            self.report.columns_dropped[key] = self.report.columns_dropped.get(key, 0) + 1
            self.delete_item(col_items, i)
        if on_idx is not None:
            self.odku_pass(table, on_idx, end)

    def convert_update(self, end: int) -> None:
        k = 1
        while self.word(k) in ("LOW_PRIORITY", "IGNORE"):
            if self.word(k) == "IGNORE":
                self.repl[self.sig[0]] = "UPDATE OR IGNORE"
            self.blank_word(k)
            k += 1
        table = self.table_at(k)
        k += 1
        if self.word(k) != "SET":
            raise ConversionError(f"UPDATE with a join or alias in statement {self.stmt_no} is not supported")
        lo = k + 1
        depth, hi = 0, end
        for j in range(lo, end):
            t = self.tok(j)
            if t.text == "(":
                depth += 1
            elif t.text == ")":
                depth -= 1
            elif depth == 0 and self.word(j) in ("WHERE", "ORDER", "LIMIT"):
                hi = j
                break
        if any(self.word(j) in ("ORDER", "LIMIT") for j in range(hi, end)):
            self.report.warn(f"statement {self.stmt_no}: UPDATE ... ORDER BY/LIMIT needs SQLITE_ENABLE_UPDATE_DELETE_LIMIT")
        items = self.split_list(lo, hi)
        drops = []
        for i, (first, _last) in enumerate(items):
            col = ident_name(self.tok(first)) or ""
            if (table.lower(), col.lower()) in self.drop_columns:
                drops.append(i)
                key = f"{table}.{col}"
                self.report.columns_dropped[key] = self.report.columns_dropped.get(key, 0) + 1
        if drops and len(drops) == len(items):
            self.report.warn(f"statement {self.stmt_no}: every SET column is dropped; statement commented out")
            self.comment_out = True
        self.functions_pass(lo, end)
        for i in reversed(drops):
            self.delete_item(items, i)
        self.check_where_drops(table, hi, end)

    def check_where_drops(self, table: str, lo: int, end: int) -> None:
        for j in range(lo, end):
            name = ident_name(self.tok(j))
            if name and (table.lower(), name.lower()) in self.drop_columns:
                self.report.warn(f"statement {self.stmt_no}: WHERE references dropped column {table}.{name}")

    def convert_delete(self, end: int) -> None:
        k = 1
        while self.word(k) in ("LOW_PRIORITY", "QUICK", "IGNORE"):
            self.blank_word(k)
            k += 1
        if self.word(k) != "FROM":
            raise ConversionError(f"multi-table DELETE in statement {self.stmt_no} is not supported")
        table = self.table_at(k + 1)
        if any(self.word(j) in ("ORDER", "LIMIT") for j in range(k + 2, end)):
            self.report.warn(f"statement {self.stmt_no}: DELETE ... ORDER BY/LIMIT needs SQLITE_ENABLE_UPDATE_DELETE_LIMIT")
        self.check_where_drops(table, k + 2, end)
        self.functions_pass(k + 2, end)

    def convert(self) -> tuple[str, str]:
        """Return (kind, sqlite text). kind: empty, session, insert, update, delete, select, other."""
        self.comment_out = False
        if not self.sig:
            return "empty", "".join(render_default(t) for t in self.toks)
        end = len(self.sig)
        if self.tok(end - 1).kind == "semi":
            end -= 1
        for t in self.toks:
            if t.kind == "execcomment":
                self.report.warn(f"statement {self.stmt_no}: MySQL executable comment {t.text[:40]!r} kept as a plain comment")
        first = self.word(0)
        kind = "other"
        if first in SESSION_STATEMENTS:
            kind = "session"
            self.report.session_dropped += 1
        elif first in ("INSERT", "REPLACE"):
            kind = "insert"
            self.convert_insert(end)
        elif first == "UPDATE":
            kind = "update"
            self.convert_update(end)
        elif first == "DELETE":
            kind = "delete"
            self.convert_delete(end)
        elif first in ("SELECT", "WITH"):
            kind = "select"
            self.functions_pass(0, end)
        else:
            self.report.warn(f"statement {self.stmt_no}: {first} passed through unchanged")
            self.functions_pass(0, end)
        lead = self.sig[0]
        trivia = "".join(render_default(t) for t in self.toks[:lead])
        body = "".join(self.repl.get(i, render_default(self.toks[i])) for i in range(lead, len(self.toks)))
        if kind == "session" or self.comment_out:
            label = "MySQL session statement" if kind == "session" else "all columns dropped"
            body = "\n".join(f"-- [{label}, not run] " + line for line in body.splitlines())
        return kind, trivia + body


def convert_text(sql: str, source_name: str, uuid_mode: str = "literal",
                 drop_columns: dict[tuple[str, str], str] | None = None,
                 table_renames: dict[str, str] | None = None) -> tuple[str, FileReport]:
    report = FileReport(source=source_name)
    drop_columns = drop_columns_for_round(None) if drop_columns is None else drop_columns
    table_renames = TABLE_RENAMES if table_renames is None else table_renames
    parts = []
    stmt_no = 0
    for stmt in split_statements(tokenize(sql, "mysql")):
        if stmt.has_body:
            stmt_no += 1
            report.statements += 1
        conv = StatementConverter(stmt, source_name, stmt_no, uuid_mode, report, drop_columns, table_renames)
        _kind, text = conv.convert()
        parts.append(text)
    return "".join(parts), report


def header(source_name: str, sha256: str, report: FileReport, uuid_mode: str, rules_round: int | None) -> str:
    lines = [
        f"-- Generated from gaheris sql/{source_name} by tools/toa/convert_mysql_to_sqlite.py",
        f"-- (converter v{CONVERTER_VERSION}, uuid mode {uuid_mode}, rules round "
        f"{'all' if rules_round is None else rules_round}). Do not hand-edit; rerun the converter.",
        f"-- Source sha256: {sha256}",
        "-- Dialect: SQLite 3.35+ (UPSERT without a conflict target). Run inside one",
        "-- transaction; MySQL session statements below are comments only.",
        f"-- Statements: {report.statements}; inserts: {report.inserts} ({report.insert_rows} VALUES rows);",
        f"-- upserts: {report.upserts_update} DO UPDATE, {report.upserts_nothing} DO NOTHING; UUID literals: {report.uuid_literals}.",
    ]
    for col, count in sorted(report.columns_dropped.items()):
        table, column = col.split(".", 1)
        reason = DROP_COLUMN_RULES.get((table.lower(), column.lower()), (0, ""))[1]
        lines.append(f"-- Dropped column {col} in {count} statement(s): {reason}")
    for w in report.warnings:
        lines.append(f"-- Warning: {w}")
    return "\n".join(lines) + "\n\n"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--src", required=True, type=Path, help="gaheris sql/ directory (read only)")
    ap.add_argument("--out", required=True, type=Path, help="output directory for converted .sql files")
    ap.add_argument("--uuid-mode", choices=("literal", "expr"), default="literal")
    ap.add_argument("--rules-round", type=int, default=None,
                    help="apply schema rules added up to this probe round (default: all, 0: none)")
    ap.add_argument("files", nargs="*", help="file names inside --src (default: the WP0.3 set)")
    args = ap.parse_args(argv)
    files = args.files or DEFAULT_FILES
    args.out.mkdir(parents=True, exist_ok=True)
    summary = []
    for name in files:
        src = args.src / name
        raw = src.read_bytes()
        sql = raw.decode("utf-8")
        text, report = convert_text(sql, name, args.uuid_mode, drop_columns_for_round(args.rules_round))
        out = args.out / name
        with open(out, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(header(name, hashlib.sha256(raw).hexdigest(), report, args.uuid_mode, args.rules_round))
            fh.write(text.replace("\r\n", "\n"))
            if not text.endswith("\n"):
                fh.write("\n")
        summary.append(report.__dict__)
        print(f"{name}: {report.statements} statements, {report.inserts} inserts / {report.insert_rows} rows, "
              f"{report.session_dropped} session dropped, {len(report.warnings)} warnings")
    with open(args.out / "conversion-report.json", "w", encoding="utf-8", newline="\n") as fh:
        json.dump(summary, fh, indent=2, sort_keys=True)
        fh.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
