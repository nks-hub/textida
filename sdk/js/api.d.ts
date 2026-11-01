// Generated from the types of the SDK by SdkTypesTests (TEXTIDA_UPDATE_SDK=1 writes it). Do not edit.

export interface InitializeParams {
  apiVersion: number;
  restarted: boolean;
  hostName: string;
  settings?: unknown;
}

export interface InitializeResult {
  apiVersion: number;
  name: string | null;
  eventAcks?: boolean;
  idleStop?: boolean;
}

export interface DocumentRef {
  document: string;
}

export interface DocumentInfoResult {
  revision: number;
  byteLength: number;
  codec: string;
  bomLength: number;
  path: string | null;
}

export interface SelectionsResult {
  revision: number;
  selections: (Selection | null)[];
}

export interface Selection {
  anchor: number;
  caret: number;
}

export interface ReadTextParams {
  document: string;
  revision: number;
  start: number;
  end: number;
}

export interface ReadTextResult {
  text: string;
  invalid: number[];
}

export interface RevisionParams {
  document: string;
  revision: number;
}

export interface FactsResult {
  lineEndings: LineEndingFacts;
}

export interface LineEndingFacts {
  dominant: string;
}

export interface PositionParams {
  document: string;
  revision: number;
  offset: number;
}

export interface PositionResult {
  line: number;
  column: number;
}

export interface ApplyParams {
  document: string;
  revision: number;
  batchId: string;
  edits: RangeEdit[];
}

export interface RangeEdit {
  start?: number;
  end?: number;
  text?: string;
}

export interface ProposeParams {
  document: string;
  revision: number;
  title: string;
  edits: RangeEdit[];
}

export interface NoticeParams {
  level: string;
  message: string;
}

export interface TaskStartParams {
  title: string;
  cancellable: boolean;
}

export interface TaskStartResult {
  task: string;
}

export interface TaskProgressParams {
  task: string;
  fraction: number;
  message: string | null;
}

export interface TaskEndParams {
  task: string;
}

/** The calls a plugin makes to the editor and the handshake: method name to the type of its parameters and of its result. */
export interface Methods {
  "initialize": { params: InitializeParams; result: InitializeResult };
  "document/info": { params: DocumentRef; result: DocumentInfoResult };
  "document/selections": { params: DocumentRef; result: SelectionsResult };
  "document/readText": { params: ReadTextParams; result: ReadTextResult };
  "document/facts": { params: RevisionParams; result: FactsResult };
  "document/offsetToPosition": { params: PositionParams; result: PositionResult };
  "edit/apply": { params: ApplyParams; result: null };
  "edit/propose": { params: ProposeParams; result: null };
  "ui/notify": { params: NoticeParams; result: null };
  "ui/task/start": { params: TaskStartParams; result: TaskStartResult };
  "ui/task/progress": { params: TaskProgressParams; result: null };
  "ui/task/end": { params: TaskEndParams; result: null };
}
