const assert = require('node:assert/strict');
const { getEventListeners } = require('node:events');
const fs = require('node:fs');
const vm = require('node:vm');

const source = fs.readFileSync(`${__dirname}/app.js`, 'utf8');
const start = source.indexOf("  const rowMoreMenu = document.getElementById('rowMoreMenu');");
const end = source.indexOf("  const packageInfoModal =", start);
assert.ok(start >= 0 && end > start, 'The test must execute the actual row-menu code.');

const download = new EventTarget();
const buttons = ['a', 'b'].map(id => Object.assign(new EventTarget(), {
  dataset: { packageUrl: `https://example.invalid/${id}.zip` },
  clientHeight: 12,
}));
const menu = {
  hidden: true,
  style: {},
  contains: () => false,
  querySelector: id => {
    assert.equal(id, '#rowMoreMenuDownload');
    return download;
  },
};
const document = Object.assign(new EventTarget(), {
  getElementById: id => {
    assert.equal(id, 'rowMoreMenu');
    return menu;
  },
  querySelectorAll: selector => {
    assert.equal(selector, '.rowMenuButton');
    return buttons;
  },
});
const opened = [];
vm.runInNewContext(source.slice(start, end), {
  document,
  window: { open: (url, target) => opened.push([url, target]) },
  setTimeout: callback => callback(),
});

const openMenu = button => button.dispatchEvent(Object.assign(new Event('click'), { clientX: 150, clientY: 40 }));
const closeMenu = () => document.dispatchEvent(new Event('click'));
download.dispatchEvent(new Event('change'));
assert.deepEqual(opened, [], 'No package is selected before the first menu opens.');
openMenu(buttons[0]);
closeMenu();
openMenu(buttons[1]);
assert.equal(getEventListeners(download, 'change').length, 1);
download.dispatchEvent(new Event('change'));
assert.deepEqual(opened, [['https://example.invalid/b.zip', '_blank']]);
closeMenu();
openMenu(buttons[0]);
download.dispatchEvent(new Event('change'));
assert.deepEqual(opened, [
  ['https://example.invalid/b.zip', '_blank'],
  ['https://example.invalid/a.zip', '_blank'],
]);
assert.equal(getEventListeners(download, 'change').length, 1);
console.log('PASS: one download handler opens only the currently selected package.');
