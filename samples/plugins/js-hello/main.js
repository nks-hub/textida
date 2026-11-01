'use strict';
// Sample plugin on the JavaScript SDK (sdk/js): writes the selections of the document, or all of it, in capital letters as one undo step.
const { Plugin } = require('textida-plugin');

const plugin = new Plugin('js-hello');

plugin.command('example.js-hello.shout', async (context) => {
  const document = context.document();
  const info = await document.info();
  let ranges = (await document.selections()).filter((s) => s.anchor !== s.caret).map((s) => [Math.min(s.anchor, s.caret), Math.max(s.anchor, s.caret)]);
  if (ranges.length === 0) ranges = [[info.bomLength, info.byteLength]];

  const edits = [];
  for (const [start, end] of ranges) {
    const read = await document.readText(start, end);
    if (read.invalid.length > 0) return { status: 'failed', diagnostics: ['The range holds bytes that are not valid in the encoding of the file.'] };
    const shouted = read.text.toUpperCase();
    if (shouted !== read.text) edits.push({ start, end, text: shouted });
  }
  if (edits.length === 0) return 'unchanged';
  await document.apply(edits);
  return 'applied';
});

plugin.run();
