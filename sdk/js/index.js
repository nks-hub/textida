'use strict';
// Textida plugin SDK for JavaScript: the plugin protocol (JSON-RPC 2.0, one message per line on stdin and stdout) and the calls of the document API.
// It has no dependencies. Types are in index.d.ts and api.d.ts; api.d.ts is generated from the types of the C# SDK.

const API_VERSION = 1;
const METHOD_NOT_FOUND = -32601;
const INVALID_PARAMS = -32602;
const INTERNAL_ERROR = -32603;
const REVISION_CONFLICT = -32020;

/** An error answer of the editor, or an error a handler wants to send. */
class RpcError extends Error {
  constructor(code, message, data) {
    super(message);
    this.name = 'RpcError';
    this.code = code;
    this.data = data;
  }
}

/** The document a command started on, bound to the revision the command saw: a call fails with a revision conflict when the document has moved on. */
class Document {
  constructor(plugin, id, revision) {
    this.plugin = plugin;
    this.id = id;
    this.revision = revision;
  }

  async info() {
    const info = await this.plugin.request('document/info', { document: this.id });
    if (info.revision !== this.revision) throw new RpcError(REVISION_CONFLICT, 'Stale.');
    return info;
  }

  /** The selections as byte offsets, a bare caret included. */
  async selections() {
    const result = await this.plugin.request('document/selections', { document: this.id });
    if (result.revision !== this.revision) throw new RpcError(REVISION_CONFLICT, 'Stale.');
    return result.selections;
  }

  /** The bytes [start, end) as text, on character boundaries. */
  async readText(start, end) {
    return this.plugin.request('document/readText', { document: this.id, revision: this.revision, start, end });
  }

  async facts() {
    return this.plugin.request('document/facts', { document: this.id, revision: this.revision });
  }

  async position(offset) {
    return this.plugin.request('document/offsetToPosition', { document: this.id, revision: this.revision, offset });
  }

  /** Applies all edits as one undo step; none is applied unless all are. Needs the permission document.edit.direct. */
  async apply(edits, batchId) {
    await this.plugin.request('edit/apply', { document: this.id, revision: this.revision, batchId: batchId || newId(), edits });
  }

  /** Offers the edits as a proposal the user reviews; the document stays as it is until then. Needs document.edit.propose. */
  async propose(title, edits) {
    await this.plugin.request('edit/propose', { document: this.id, revision: this.revision, title, edits });
  }
}

let counter = 0;
function newId() {
  counter += 1;
  return `${process.pid.toString(16)}${Date.now().toString(16)}${counter.toString(16)}`;
}

class Plugin {
  /**
   * @param {string} name the name of the plugin, for the log of the editor
   * @param {{input?: NodeJS.ReadableStream, output?: NodeJS.WritableStream, exit?: (code: number) => void}} [options] streams and the exit, replaced in tests
   */
  constructor(name, options = {}) {
    this.name = name;
    this.input = options.input || process.stdin;
    this.output = options.output || process.stdout;
    this.exit = options.exit || ((code) => process.exit(code));
    this.commands = new Map();
    this.methods = new Map();
    this.events = new Map();
    this.pending = new Map();
    this.running = new Map();
    this.nextId = 1;
    this.settings = {};
    this.language = 'en';
  }

  /** Registers a command of the manifest. The handler gets {command, document(), language, params, signal} and returns {status, diagnostics?}, a status word or nothing. */
  command(id, handler) {
    this.commands.set(id, handler);
    return this;
  }

  /** Registers a method the editor may call (a request of the editor that is not a command). */
  method(name, handler) {
    this.methods.set(name, handler);
    return this;
  }

  /** Registers a handler for an event of the editor ($/event with this name); the handler gets the payload. */
  on(eventName, handler) {
    this.events.set(eventName, handler);
    return this;
  }

  /** Calls a method of the editor; resolves with the result, rejects with an RpcError. */
  request(method, params) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.send({ jsonrpc: '2.0', id, method, params });
    });
  }

  notify(method, params) {
    this.send({ jsonrpc: '2.0', method, params });
  }

  /** Shows a notice in the window of the editor. level is info, warning or error. */
  async showNotice(level, message) {
    await this.request('ui/notify', { level, message });
  }

  send(message) {
    this.output.write(JSON.stringify(message) + '\n');
  }

  /** Reads the messages of the editor until it ends the process. Resolves when the input is closed. */
  run() {
    return new Promise((resolve) => {
      this.input.setEncoding('utf8');
      let buffer = '';
      this.input.on('data', (chunk) => {
        buffer += chunk;
        let at;
        while ((at = buffer.indexOf('\n')) >= 0) {
          const line = buffer.slice(0, at).trim();
          buffer = buffer.slice(at + 1);
          if (line) this.receive(line);
        }
      });
      this.input.on('end', resolve);
    });
  }

  receive(line) {
    let message;
    try {
      message = JSON.parse(line);
    } catch {
      return; // not JSON: the editor never sends it
    }
    if (message.method !== undefined) {
      if (message.id === undefined) this.notification(message.method, message.params || {});
      else this.incoming(message);
    } else if (message.id !== undefined && this.pending.has(message.id)) {
      const { resolve, reject } = this.pending.get(message.id);
      this.pending.delete(message.id);
      if (message.error) reject(new RpcError(message.error.code, message.error.message, message.error.data));
      else resolve(message.result);
    }
  }

  notification(method, params) {
    if (method === 'exit') this.exit(0);
    else if (method === '$/cancel') this.running.get(params.id)?.abort();
    else if (method === '$/event') this.events.get(params.name)?.(params.payload);
  }

  async incoming(message) {
    const controller = new AbortController();
    this.running.set(message.id, controller);
    try {
      const result = await this.dispatch(message.method, message.params || {}, controller.signal);
      this.send({ jsonrpc: '2.0', id: message.id, result: result === undefined ? null : result });
    } catch (e) {
      const error = e instanceof RpcError ? e : new RpcError(INTERNAL_ERROR, String((e && e.message) || e));
      process.stderr.write(`${this.name}: ${message.method} failed: ${error.message}\n`);
      this.send({ jsonrpc: '2.0', id: message.id, error: { code: error.code, message: error.message, data: error.data } });
    } finally {
      this.running.delete(message.id);
    }
  }

  async dispatch(method, params, signal) {
    switch (method) {
      case 'initialize':
        if (params.apiVersion !== API_VERSION) throw new RpcError(INVALID_PARAMS, `This SDK speaks plugin API version ${API_VERSION}, the editor asked for ${params.apiVersion}.`);
        this.settings = params.settings || {};
        return { apiVersion: API_VERSION, name: this.name };
      case '$/resync':
        return null;
      case '$/ping':
        return true;
      case 'shutdown':
        return null;
      case 'command/execute':
        return this.execute(params, signal);
      default: {
        const handler = this.methods.get(method);
        if (!handler) throw new RpcError(METHOD_NOT_FOUND, `Unknown method ${method}`);
        return handler(params, signal);
      }
    }
  }

  async execute(params, signal) {
    const handler = this.commands.get(params.command);
    if (!handler) throw new RpcError(METHOD_NOT_FOUND, `Unknown command ${params.command}`);
    this.language = params.language || this.language;
    const context = {
      command: params.command,
      language: this.language,
      params,
      signal,
      document: () => {
        if (typeof params.document !== 'string' || typeof params.revision !== 'number') throw new RpcError(INVALID_PARAMS, 'The command names no document and revision.');
        return new Document(this, params.document, params.revision);
      },
    };
    const result = await handler(context);
    if (typeof result === 'string') return { status: result };
    return result || { status: 'done' };
  }
}

module.exports = { Plugin, Document, RpcError, API_VERSION, errors: { METHOD_NOT_FOUND, INVALID_PARAMS, INTERNAL_ERROR, REVISION_CONFLICT } };
