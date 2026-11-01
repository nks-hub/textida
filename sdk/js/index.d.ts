import type { ApplyParams, DocumentInfoResult, FactsResult, Methods, PositionResult, RangeEdit, ReadTextResult, Selection } from './api';

export * from './api';

export const API_VERSION: 1;

export declare class RpcError extends Error {
  constructor(code: number, message: string, data?: unknown);
  code: number;
  data?: unknown;
}

export declare const errors: { METHOD_NOT_FOUND: number; INVALID_PARAMS: number; INTERNAL_ERROR: number; REVISION_CONFLICT: number };

/** The document a command started on, bound to the revision the command saw. */
export declare class Document {
  readonly id: string;
  readonly revision: number;
  info(): Promise<DocumentInfoResult>;
  selections(): Promise<Selection[]>;
  readText(start: number, end: number): Promise<ReadTextResult>;
  facts(): Promise<FactsResult>;
  position(offset: number): Promise<PositionResult>;
  /** Needs the permission document.edit.direct. */
  apply(edits: RangeEdit[], batchId?: string): Promise<void>;
  /** Needs the permission document.edit.propose. */
  propose(title: string, edits: RangeEdit[]): Promise<void>;
}

export interface CommandContext {
  command: string;
  language: string;
  params: Record<string, unknown>;
  /** Aborted when the editor cancels the request. */
  signal: AbortSignal;
  /** The document and revision the command started on; throws when the command names none. */
  document(): Document;
}

export interface CommandResult {
  status: string;
  diagnostics?: string[];
}

export type CommandHandler = (context: CommandContext) => Promise<CommandResult | string | void> | CommandResult | string | void;

export interface PluginOptions {
  input?: NodeJS.ReadableStream;
  output?: NodeJS.WritableStream;
  exit?: (code: number) => void;
}

export declare class Plugin {
  constructor(name: string, options?: PluginOptions);
  settings: Record<string, unknown>;
  command(id: string, handler: CommandHandler): this;
  method(name: string, handler: (params: any, signal: AbortSignal) => unknown): this;
  on(eventName: string, handler: (payload: any) => void): this;
  request<M extends keyof Methods>(method: M, params: Methods[M]['params']): Promise<Methods[M]['result']>;
  request(method: string, params?: unknown): Promise<any>;
  notify(method: string, params?: unknown): void;
  showNotice(level: 'info' | 'warning' | 'error', message: string): Promise<void>;
  run(): Promise<void>;
}
