# Generated from the types of the SDK by SdkTypesTests (TEXTIDA_UPDATE_SDK=1 writes it). Do not edit.
from __future__ import annotations

from typing import Any, NotRequired, TypedDict


class InitializeParams(TypedDict):
    apiVersion: int
    restarted: bool
    hostName: str
    settings: NotRequired[Any]


class InitializeResult(TypedDict):
    apiVersion: int
    name: str | None
    eventAcks: NotRequired[bool]
    idleStop: NotRequired[bool]


class DocumentRef(TypedDict):
    document: str


class DocumentInfoResult(TypedDict):
    revision: int
    byteLength: int
    codec: str
    bomLength: int
    path: str | None


class Selection(TypedDict):
    anchor: int
    caret: int


class SelectionsResult(TypedDict):
    revision: int
    selections: list[Selection | None]


class ReadTextParams(TypedDict):
    document: str
    revision: int
    start: int
    end: int


class ReadTextResult(TypedDict):
    text: str
    invalid: list[int]


class RevisionParams(TypedDict):
    document: str
    revision: int


class LineEndingFacts(TypedDict):
    dominant: str


class FactsResult(TypedDict):
    lineEndings: LineEndingFacts


class PositionParams(TypedDict):
    document: str
    revision: int
    offset: int


class PositionResult(TypedDict):
    line: int
    column: int


class RangeEdit(TypedDict):
    start: NotRequired[int]
    end: NotRequired[int]
    text: NotRequired[str]


class ApplyParams(TypedDict):
    document: str
    revision: int
    batchId: str
    edits: list[RangeEdit]


class ProposeParams(TypedDict):
    document: str
    revision: int
    title: str
    edits: list[RangeEdit]


class NoticeParams(TypedDict):
    level: str
    message: str


class TaskStartParams(TypedDict):
    title: str
    cancellable: bool


class TaskStartResult(TypedDict):
    task: str


class TaskProgressParams(TypedDict):
    task: str
    fraction: float
    message: str | None


class TaskEndParams(TypedDict):
    task: str


# method name -> (type of the parameters, type of the result or None)
METHODS: dict[str, tuple[str, str | None]] = {
    "initialize": ("InitializeParams", "InitializeResult"),
    "document/info": ("DocumentRef", "DocumentInfoResult"),
    "document/selections": ("DocumentRef", "SelectionsResult"),
    "document/readText": ("ReadTextParams", "ReadTextResult"),
    "document/facts": ("RevisionParams", "FactsResult"),
    "document/offsetToPosition": ("PositionParams", "PositionResult"),
    "edit/apply": ("ApplyParams", None),
    "edit/propose": ("ProposeParams", None),
    "ui/notify": ("NoticeParams", None),
    "ui/task/start": ("TaskStartParams", "TaskStartResult"),
    "ui/task/progress": ("TaskProgressParams", None),
    "ui/task/end": ("TaskEndParams", None),
}
